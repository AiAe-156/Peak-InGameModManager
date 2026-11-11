#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;
using YamlDotNet.RepresentationModel;

namespace ModConfigEnhance.Overview;

/// <summary>Applies the manager-profile follow-ups an install recorded: the package version to write into
/// mods.yml, or the entry to drop after a removal. The worker has no YAML parser and the file belongs to the
/// manager, so it only records the intent; this runs once per launch and a failed write stays pending rather
/// than being lost, so the manager never disagrees with the files MCE installed.
/// <para>The document is parsed only to confirm it really is a manager profile; the edit itself is line surgery,
/// so every other entry keeps the exact bytes the manager wrote instead of being reformatted wholesale.</para>
/// <para>Runs from Plugin.Awake, before any UI or localized text exists: it logs through ManagerLog only.</para></summary>
internal static class ManagerProfileSync
{
    private const int BackupLimit = 20;
    private static bool _applied;

    internal static string PendingPath => Path.Combine(Paths.BepInExRootPath, "MCE", "manager-sync.json");

    /// <summary>Configured profile when that folder holds mods.yml, else the folder beside BepInEx when it is
    /// one (r2modman/Thunderstore keep mods.yml there), else empty for a plain local installation.</summary>
    internal static string Resolve(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                string value = Path.GetFullPath(configured.Trim().Trim('"'));
                if (File.Exists(Path.Combine(value, "mods.yml"))) return value;
            }
            catch (Exception) { }
        }
        try
        {
            string beside = Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, ".."));
            if (File.Exists(Path.Combine(beside, "mods.yml"))) return beside;
        }
        catch (Exception) { }
        return "";
    }

    internal static void Apply(string configured)
    {
        if (_applied) return;
        _applied = true;
        try { ApplyCore(configured); }
        catch (Exception error) { Diagnostics.ManagerLog.Warn("管理器档案同步未完成（保持待处理，下次启动重试）：" + error.Message); }
    }

    private static void ApplyCore(string configured)
    {
        string pending = PendingPath;
        if (!File.Exists(pending)) return;
        ManagerSyncPending? data;
        try { data = JsonConvert.DeserializeObject<ManagerSyncPending>(File.ReadAllText(pending, Encoding.UTF8)); }
        catch (Exception error) { Diagnostics.ManagerLog.Warn("管理器档案同步记录无法读取，保持待处理：" + error.Message); return; }
        if (data == null || data.SchemaVersion != 1) { Diagnostics.ManagerLog.Warn("管理器档案同步记录版本不受支持，保持待处理。"); return; }
        if (data.Records.Count == 0) { File.Delete(pending); return; }
        string profile = Resolve(configured);
        if (profile.Length == 0)
        { Diagnostics.ManagerLog.Info($"管理器档案同步尚有 {data.Records.Count} 项待处理，但当前安装不在管理器档案内；保留待处理。"); return; }
        if (ManagerRunning())
        { Diagnostics.ManagerLog.Info("管理器档案同步推迟：请先关闭 r2modman/Thunderstore，下次启动重试。"); return; }
        string yamlPath = Path.Combine(profile, "mods.yml");
        byte[] original = File.ReadAllBytes(yamlPath);
        string text = Encoding.UTF8.GetString(original).TrimStart((char)0xFEFF);
        if (!IsManagerProfile(text)) { Diagnostics.ManagerLog.Warn("mods.yml 格式无法确认，未改动任何内容；保持待处理。"); return; }
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        bool trailingNewline = text.EndsWith("\n", StringComparison.Ordinal);
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (trailingNewline) lines.RemoveAt(lines.Count - 1);
        var changed = new List<string>();
        foreach (var record in data.Records.Where(r => r.Action != ManagerSyncAction.Remove))
        {
            if (!TryVersion(record.Version, out var version)) continue;
            var range = FindEntry(lines, record.Key);
            if (range == null) continue; // The manager never listed this package; there is nothing to follow.
            if (TryReadVersion(lines, range.Value, out var current) && current == version) continue;
            if (!TrySetVersion(lines, range.Value, version))
            { Diagnostics.ManagerLog.Warn("mods.yml 中 " + record.Key + " 的版本字段形状不符合预期，该项未改动；保持待处理。"); continue; }
            changed.Add(record.Key + " → " + record.Version);
        }
        // Drop whole entries last, from the bottom up, so the ranges found above stay valid.
        var drop = new List<(int Start, int End)>();
        foreach (var record in data.Records.Where(r => r.Action == ManagerSyncAction.Remove))
        {
            var range = FindEntry(lines, record.Key);
            if (range != null) drop.Add(range.Value);
        }
        foreach (var range in drop.OrderByDescending(r => r.Start))
        {
            changed.Add(EntryName(lines, range) + "（已移除）");
            lines.RemoveRange(range.Start, range.End - range.Start);
        }
        if (changed.Count == 0) { File.Delete(pending); return; }
        string output = string.Join(newline, lines) + (trailingNewline ? newline : "");
        string backups = Path.Combine(Paths.BepInExRootPath, "MCE", "manager-sync-backups");
        Directory.CreateDirectory(backups);
        File.WriteAllBytes(Path.Combine(backups, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_mods.yml.before"), original);
        // The manager owns this file and reads it at start: a crash mid-write must leave the old bytes, never a truncated profile.
        OverviewRepository.AtomicWrite(yamlPath, output);
        File.Delete(pending);
        PruneBackups(backups);
        Diagnostics.ManagerLog.Info("管理器档案已同步（mods.yml）：" + string.Join("；", changed));
    }

    /// <summary>Parses the document only to be sure it really is a manager profile before any line is touched.</summary>
    private static bool IsManagerProfile(string text)
    {
        try
        {
            var yaml = new YamlStream();
            using (var reader = new StringReader(text)) yaml.Load(reader);
            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlSequenceNode sequence) return false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in sequence.Children)
            {
                if (child is not YamlMappingNode node) return false;
                string name = Scalar(node, "name");
                if (name.Length == 0 || !seen.Add(name)) return false;
            }
            return true;
        }
        catch (Exception error) { Diagnostics.ManagerLog.Warn("mods.yml 解析失败，未改动任何内容：" + error.Message); return false; }
    }

    /// <summary>The [start, end) line range of every top-level sequence entry, in document order. Entries start at
    /// column zero, while their content (including folded block scalars) is always indented.</summary>
    private static List<(int Start, int End)> EntryRanges(List<string> lines)
    {
        var starts = new List<int>();
        for (int index = 0; index < lines.Count; index++)
            if (lines[index] == "-" || lines[index].StartsWith("- ", StringComparison.Ordinal)) starts.Add(index);
        var ranges = new List<(int Start, int End)>();
        for (int index = 0; index < starts.Count; index++)
            ranges.Add((starts[index], index + 1 < starts.Count ? starts[index + 1] : lines.Count));
        return ranges;
    }

    private static string EntryName(List<string> lines, (int Start, int End) range)
    {
        for (int index = range.Start; index < range.End; index++)
            if (lines[index].StartsWith("  name:", StringComparison.Ordinal))
                return lines[index].Substring("  name:".Length).Trim().Trim('\'', '"');
        return "";
    }

    private static (int Start, int End)? FindEntry(List<string> lines, string key)
    {
        foreach (var range in EntryRanges(lines))
            if (string.Equals(EntryName(lines, range), key, StringComparison.OrdinalIgnoreCase)) return range;
        return null;
    }

    private static int VersionBlock(List<string> lines, (int Start, int End) range)
    {
        for (int index = range.Start; index < range.End; index++)
            if (lines[index].StartsWith("  versionNumber:", StringComparison.Ordinal)) return index;
        return -1;
    }

    /// <summary>Line surgery for enablement (2.3.2): rewrites only the <c>enabled:</c> line of each named entry, so the manager's
    /// own formatting of every other line survives (a <c>yaml.Save</c> round trip reformatted all 68 entries). The document
    /// must still parse as a manager profile; an entry without a plain one-line <c>enabled:</c> field is reported, not guessed.</summary>
    internal static byte[] SetEnabled(byte[] original, IReadOnlyDictionary<string, bool> states)
    {
        string text = Encoding.UTF8.GetString(original).TrimStart((char)0xFEFF);
        if (!IsManagerProfile(text)) throw new InvalidDataException("mods.yml format could not be confirmed; nothing was changed.");
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        bool trailingNewline = text.EndsWith("\n", StringComparison.Ordinal);
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (trailingNewline) lines.RemoveAt(lines.Count - 1);
        foreach (var pair in states)
        {
            var range = FindEntry(lines, pair.Key) ?? throw new InvalidDataException("mods.yml has no entry " + pair.Key + ".");
            int line = -1;
            for (int index = range.Start; index < range.End; index++)
                if (lines[index].StartsWith("  enabled:", StringComparison.Ordinal)) { line = index; break; }
            if (line < 0 || !bool.TryParse(lines[line].Substring("  enabled:".Length).Trim(), out _))
                throw new InvalidDataException("mods.yml entry " + pair.Key + " has no plain enabled field; nothing was changed.");
            lines[line] = "  enabled: " + (pair.Value ? "true" : "false");
        }
        return Encoding.UTF8.GetBytes(string.Join(newline, lines) + (trailingNewline ? newline : ""));
    }

    /// <summary>The version this entry already records, so a follow-up that matches the file changes nothing.</summary>
    private static bool TryReadVersion(List<string> lines, (int Start, int End) range, out Version version)
    {
        version = new Version(0, 0, 0);
        int block = VersionBlock(lines, range);
        if (block < 0) return false;
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = block + 1; index < range.End; index++)
        {
            string line = lines[index];
            if (!line.StartsWith("    ", StringComparison.Ordinal)) break;
            int colon = line.IndexOf(':', 4);
            if (colon < 0) break;
            parts[line.Substring(4, colon - 4).Trim()] = line.Substring(colon + 1).Trim();
        }
        return parts.Count == 3 && Version.TryParse(parts["major"] + "." + parts["minor"] + "." + parts["patch"], out version!);
    }

    /// <summary>Rewrites the three version parts of this entry. Returns false when the entry is not in the shape
    /// the manager writes, so the caller leaves it alone rather than guessing at its structure.</summary>
    private static bool TrySetVersion(List<string> lines, (int Start, int End) range, Version version)
    {
        int block = VersionBlock(lines, range);
        if (block < 0) return false;
        var wanted = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["major"] = version.Major.ToString(), ["minor"] = version.Minor.ToString(), ["patch"] = version.Build.ToString() };
        int replaced = 0;
        for (int index = block + 1; index < range.End; index++)
        {
            string line = lines[index];
            if (!line.StartsWith("    ", StringComparison.Ordinal)) break;
            int colon = line.IndexOf(':', 4);
            if (colon < 0) break;
            string part = line.Substring(4, colon - 4).Trim();
            if (!wanted.TryGetValue(part, out var value)) continue;
            lines[index] = "    " + part + ": " + value;
            replaced++;
        }
        return replaced == 3;
    }

    private static void PruneBackups(string directory)
    {
        var stale = Directory.GetFiles(directory, "*_mods.yml.before").OrderByDescending(f => f, StringComparer.Ordinal).Skip(BackupLimit).ToList();
        foreach (string file in stale) try { File.Delete(file); } catch (Exception) { }
    }

    private static bool ManagerRunning() => RunningProbe?.Invoke() ?? ManagerProcessRunning();

    // Only used by the isolated checks; production always probes the real process list.
    internal static Func<bool>? RunningProbe = null;

    private static bool ManagerProcessRunning()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    string name = process.ProcessName;
                    if (name.IndexOf("r2modman", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Thunderstore", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                catch (Exception) { }
            }
        }
        return false;
    }

    private static bool TryVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        int suffix = text.IndexOfAny(new[] { '-', '+' });
        if (!Version.TryParse(suffix < 0 ? text : text.Substring(0, suffix), out var parsed) || parsed == null) return false;
        // mods.yml records exactly three parts; a fourth would be silently dropped, so leave that entry alone.
        if (parsed.Build < 0 || parsed.Revision >= 0) return false;
        version = parsed;
        return true;
    }

    private static string Scalar(YamlMappingNode node, string key)
        => node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value ?? "" : "";
}
