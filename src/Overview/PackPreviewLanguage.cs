#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>Pack-local dictionaries only; never consults or changes the running game's language.</summary>
internal static class PackPreviewLanguage
{
    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.Ordinal);
    internal static Dictionary<string, string> Entries(PackTask task)
    {
        string key = task.Id + "|" + task.Proposed.Language;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string root = task.Request.Operation == "view-stored" ? task.StagingDirectory : Path.Combine(task.StagingDirectory, "content");
        var directories = new List<string>();
        if (task.Proposed.AuthorRoot.Length > 0) directories.Add(OverviewProfileAssets.CheckedPath(root, task.Proposed.AuthorRoot + "/language"));
        foreach (string p in task.Proposed.Files.Select(f => f.Path.Replace((char)92, '/')))
        {
            int at = p.IndexOf("/MCEPack/language/", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) directories.Add(OverviewProfileAssets.CheckedPath(root, p[..(at + "/MCEPack/language".Length)]));
        }
        if (task.Request.Operation == "view-stored") directories.Add(OverviewProfileAssets.CheckedPath(PackLibrary.Home, task.Proposed.Id + "/workspace/language"));
        foreach (string directory in directories.Distinct(StringComparer.OrdinalIgnoreCase).Where(Directory.Exists))
        {
            var group = PackLanguages.Scan(directory).FirstOrDefault(g => g.Key == task.Proposed.Language);
            if (group == null) continue;
            foreach (string name in group.Files.OrderBy(n => n.Contains('_') ? 1 : 0).ThenBy(n => n, StringComparer.OrdinalIgnoreCase))
                foreach (var pair in Loc.Parse(Path.Combine(directory, name)).Entries) result[pair.Key] = pair.Value;
        }
        Cache[key] = result; return result;
    }
    internal static string Translate(PackTask task, string original)
    {
        var entries = Entries(task);
        return entries.TryGetValue(original, out var value) && !string.IsNullOrWhiteSpace(value) ? value : original;
    }
    internal static string TranslateMarkdown(PackTask task, string source)
    {
        // Only exact whole-line dictionary entries are safe here. Never rewrite paths, markup or editor metadata.
        var entries = Entries(task);
        return string.Join("\n", source.Split('\n').Select(line => entries.TryGetValue(line.TrimEnd('\r'), out var translated) ? translated : line));
    }
}
