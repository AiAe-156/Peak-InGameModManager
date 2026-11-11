#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewRepository
{
    public string RootDirectory { get; }
    public OverviewState State { get; private set; } = new();
    public string PackDirectory { get; private set; } = "";
    public string IntroductionPath => ResolvePackPath(State.pack.introFile);
    public string IntroductionDocumentPath => string.IsNullOrWhiteSpace(State.pack.introDocument) ? "" : ResolvePackPath(State.pack.introDocument);
    private readonly string _plugins;
    private readonly string _legacyRootDirectory;
    private readonly bool _appliedView;
    public string ConfigRootDirectory { get; }
    private readonly object _saveLock = new();
    private OverviewState _published = new();
    // The overview page and the settings sidebar hold separate instances of the same state file.
    private static int _sharedRevision;
    private int _loadedRevision = -1;
    public bool IsStale => _loadedRevision != _sharedRevision;
    /// <summary>The scan the reload already performs, so a caller that needs the installed list does not scan twice.</summary>
    internal System.Collections.Generic.IReadOnlyList<OverviewMod> ReloadInstalled { get; private set; } = System.Array.Empty<OverviewMod>();
    public bool HasPublishedPack { get; private set; }
    // Edit mode changes available controls, never request permission.
    public void SetAuthorMode(bool enabled) { }
    public bool AuthorGlobalUpdateAllowed => _published.globalUpdateAllowed;
    public OverviewRepository(string? rootDirectory = null, string? pluginsDirectory = null, bool appliedView = false, OverviewConfigService? configService = null)
    {
        // 2.5.2：旧版 overview\ 搬到 data\legacy\；ConfigRootDirectory 仍是 config\ModConfigEnhance
        ConfigRootDirectory = rootDirectory != null ? Path.GetDirectoryName(rootDirectory)! : Loc.RootDirectory;
        _legacyRootDirectory = rootDirectory ?? Updating.MceDirectories.Legacy(ConfigRootDirectory);
        RootDirectory = Updating.MceDirectories.Runtime(ConfigRootDirectory); // 夹具传入 rootDirectory 时也走同一布局
        _appliedView = appliedView;
        _configs = configService;
        _plugins = pluginsDirectory ?? Paths.PluginPath;
        Reload();
    }
    private void ReloadLegacy()
    {
        _loadedRevision = _sharedRevision;
        _published = new OverviewState();
        HasPublishedPack = false;
        PackDirectory = _legacyRootDirectory;
        if (Directory.Exists(_plugins))
        {
            var packs = SafeFiles(_plugins).Where(p => ModConfigEnhance.Updating.ManagerIdentity.IsPackFolder(Path.GetFileName(Path.GetDirectoryName(p)))
                && new[] { "packinfo.cfg", "packinfo.json", "pack.json" }.Contains(Path.GetFileName(p).ToLowerInvariant())).OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            foreach (string file in packs)
            {
                try
                {
                    _published = ReadPack(file);
                    HasPublishedPack = true;
                    PackDirectory = Path.GetDirectoryName(file)!;
                    break;
                }
                catch (Exception ex) { Plugin.Log.LogWarning("[概览] 资料读取失败：" + ex.Message); }
            }
        }
        string stateFile = Path.Combine(_legacyRootDirectory, "state.json");
        bool hasState = File.Exists(stateFile);
        try { State = hasState ? OverviewProfileStore.DeserializeJson<OverviewState>(File.ReadAllText(stateFile, Encoding.UTF8)) : new(); }
        catch (Exception ex) { throw new InvalidDataException("原个人设置读取失败；保留原文件，拒绝迁移空配置：" + ex.Message, ex); }
        if (!hasState)
        {
            State.globalUpdateAllowed = _published.globalUpdateAllowed;
            State.automaticUpdateChecks = _published.automaticUpdateChecks;
        }
        State.mods ??= new(); State.groups ??= new(); State.pack ??= new();
        State.customColors ??= new();
        State.collapsedGroups ??= new();
        State.collapsedHotkeyMods ??= new(); State.hotkeyPresets ??= new();
        State.packHotkeyPresets ??= new(); State.ignoredHotkeyConflicts ??= new();
        State.hotkeyPresets.RemoveAll(p => !p.overridden && !p.cleared);
        foreach (var preset in _published.hotkeyPresets ?? new())
            if (!State.hotkeyPresets.Any(p => p.id == preset.id)) { var inherited = Clone(preset); inherited.overridden = false; State.hotkeyPresets.Add(inherited); }
        State.packHotkeyPresets.RemoveAll(p => !p.overridden);
        foreach (var preset in _published.packHotkeyPresets ?? new())
            if (!State.packHotkeyPresets.Any(p => p.id == preset.id)) { var inherited = Clone(preset); inherited.overridden = false; State.packHotkeyPresets.Add(inherited); }
        MigrateUpdatePermissions(hasState);
        if (string.IsNullOrWhiteSpace(_published.pack.sourceNamespace) && ThunderstoreUpdateService.TryParseSource(_published.pack.id, out string packNs, out string packName))
        { _published.pack.sourceNamespace = packNs; _published.pack.sourceName = packName; }
        if (!State.packOverridden) { State.pack = Clone(_published.pack); if (!string.IsNullOrWhiteSpace(State.packNameOverride)) State.pack.name = State.packNameOverride; }
        else PackDirectory = Path.Combine(_legacyRootDirectory, "local-pack");
        if (!State.packOverridden)
            foreach (OverviewGroup group in _published.groups ?? new())
                if (!State.groups.Any(g => g.id == group.id)) State.groups.Add(Clone(group));
    }
    public OverviewModRule GetRule(string id)
    {
        var rule = State.mods.FirstOrDefault(r => r.id == id);
        if (rule != null) return rule;
        rule = _published.mods?.FirstOrDefault(r => r.id == id);
        rule = rule == null ? new OverviewModRule { id = id } : Clone(rule);
        rule.updateOverridden = false;
        if (_temporaryRules.TryGetValue(id, out var existing)) return existing;
        _temporaryRules[id] = rule;
        return rule;
    }
    /// <summary>Read-only lookup; unlike GetRule it never adds a personal rule.</summary>
    public string GroupOf(string id)
    {
        string group = State.mods.FirstOrDefault(r => r.id == id)?.groupId ?? _published.mods?.FirstOrDefault(r => r.id == id)?.groupId ?? "";
        return group.Length > 0 && State.groups.Any(g => g.id == group) ? group : "";
    }
    public bool AuthorModUpdateAllowed(OverviewModRule rule) => _published.mods?.FirstOrDefault(r => r.id == rule.id)?.updateAllowed ?? true;
    public List<OverviewHotkeyPreset> GetEffectivePackPresets() => State.packHotkeyPresets.Where(p => !p.cleared).Select(Clone).ToList();
    public bool GetPackPresetMode() => HasPublishedPack && !State.packHotkeyPresetModeOverridden ? _published.packHotkeyPresetMode : State.packHotkeyPresetMode;
    public bool GetGlobalUpdateAllowed() => !HasPublishedPack || State.globalUpdateOverridden ? State.globalUpdateAllowed : AuthorGlobalUpdateAllowed;
    public bool GetModUpdateAllowed(OverviewModRule rule) => !HasPublishedPack || rule.updateOverridden ? rule.updateAllowed : AuthorModUpdateAllowed(rule);
    public bool IsUpdateAllowed(OverviewModRule rule) => GetModUpdateAllowed(rule);
    public bool IsPackUpdateAllowed(OverviewPack pack) => pack.updateAllowed;
    public bool NeedsUpdateWarning(OverviewModRule? rule = null) => HasPublishedPack && !State.updateWarningsDismissed &&
        !(rule == null ? AuthorGlobalUpdateAllowed : AuthorModUpdateAllowed(rule));
    private void MigrateUpdatePermissions(bool hasState)
    {
        if (State.updatePermissionVersion >= 1) return;
        if (hasState && HasPublishedPack)
        {
            // A restrictive legacy choice is safe to preserve. A permissive value against an
            // author prohibition was previously blocked by the hard gate, so it is not proof
            // of informed consent. Keep its stored value but require explicit confirmation.
            State.globalUpdateOverridden = State.globalUpdateOverridden || (!State.globalUpdateAllowed && AuthorGlobalUpdateAllowed);
            foreach (var rule in State.mods)
                rule.updateOverridden = rule.updateOverridden || (!rule.updateAllowed && AuthorModUpdateAllowed(rule));
        }
        State.updatePermissionVersion = 1;
    }
    public void Save()
    {
        lock (_saveLock) SaveProfileDraft();
        _loadedRevision = ++_sharedRevision;
        OverviewPageCache.ConfigurationSaved();
    }
    public string EnsureLocalPackCopy()
    {
        EnsureEditableProfile();
        Directory.CreateDirectory(PackDirectory);
        return PackDirectory;
    }
    public void SavePack(OverviewPack pack)
    {
        bool forked = ViewedProfile.readOnly;
        string originalName = ViewedProfile.name;
        EnsureLocalPackCopy(); State.pack = Clone(pack);
        string unifiedName = forked && pack.name == originalName ? ViewedProfile.name : pack.name;
        if (string.IsNullOrWhiteSpace(State.pack.sourceNamespace) && ThunderstoreUpdateService.TryParseSource(State.pack.id, out string ns, out string name))
        { State.pack.sourceNamespace = ns; State.pack.sourceName = name; }
        lock (_saveLock) SavePackInformation(ConfigurationSaveScope.Information);
        _loadedRevision = ++_sharedRevision;
    }
    public string GetDraftIntroductionPath()
    {
        string path = Path.Combine(RootDirectory, "drafts", "introduction.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
    public string ResolvePackPath(string relative)
    {
        try { return SafePath(PackDirectory, Uri.UnescapeDataString(relative)); } catch { return ""; }
    }
    internal static string SafePath(string root, string relative)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return "";
            string basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(basePath, relative));
            if (!full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) return "";
            // Symlinks/junctions would circumvent the lexical directory boundary.
            string? current = full;
            while (current != null && current.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return "";
                current = Path.GetDirectoryName(current);
            }
            return full;
        }
        catch { return ""; }
    }
    internal static IEnumerable<string> SafeFiles(string root)
    {
        var result = new List<string>(); var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            string folder = pending.Pop();
            try
            {
                result.AddRange(Directory.GetFiles(folder));
                foreach (string child in Directory.GetDirectories(folder))
                {
                    string name = Path.GetFileName(child);
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0 || name.Equals("备份") || name.Equals("backup", StringComparison.OrdinalIgnoreCase) || name.Equals("backups", StringComparison.OrdinalIgnoreCase) || name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
                    pending.Push(child);
                }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return result;
    }
    internal static void AtomicWrite(string file, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temporary = file + ".tmp";
        File.WriteAllText(temporary, content, new UTF8Encoding(false));
        if (File.Exists(file)) File.Replace(temporary, file, file + ".bak");
        else File.Move(temporary, file);
    }
    private static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;
    private static OverviewState ReadPack(string file)
    {
        if (!file.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
        {
            JObject json = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
            if (json.Value<int?>("schemaVersion") is int version && version != 1) throw new InvalidDataException("不支持的资料版本 " + version);
            var state = json.ToObject<OverviewState>() ?? new();
            state.pack = (json["packInfo"] ?? json["PackInfo"] ?? json["pack"] ?? json).ToObject<OverviewPack>() ?? new();
            return state;
        }
        var result = new OverviewState(); JObject fields = new(); var sections = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase); string section = "";
        foreach (string raw in File.ReadAllLines(file, Encoding.UTF8))
        {
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
            int equals = line.IndexOf('='); if (equals < 1) continue;
            string key = line.Substring(0, equals).Trim(); string value = line.Substring(equals + 1).Trim();
            JObject destination = fields;
            if (section.StartsWith("group:", StringComparison.OrdinalIgnoreCase) || section.StartsWith("mod:", StringComparison.OrdinalIgnoreCase) || section.StartsWith("hotkey:", StringComparison.OrdinalIgnoreCase) || section.StartsWith("packhotkey:", StringComparison.OrdinalIgnoreCase))
            { if (!sections.TryGetValue(section, out destination!)) sections[section] = destination = new JObject(); }
            else if (section.Length != 0 && !section.Equals("PackInfo", StringComparison.OrdinalIgnoreCase) && !section.Equals("pack", StringComparison.OrdinalIgnoreCase) && !section.Equals("general", StringComparison.OrdinalIgnoreCase)) continue;
            if (key.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase) && value != "1") throw new InvalidDataException("不支持的资料版本");
            if (bool.TryParse(value, out bool boolean)) destination[key] = boolean; else if (key.Equals("order", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int order)) destination[key] = order; else destination[key] = value;
        }
        result.globalUpdateAllowed = fields.Value<bool?>("globalUpdateAllowed") ?? true;
        result.automaticUpdateChecks = fields.Value<bool?>("automaticUpdateChecks") ?? true;
        result.packHotkeyPresetMode = fields.Value<bool?>("packHotkeyPresetMode") ?? false;
        result.pack = fields.ToObject<OverviewPack>() ?? new();
        foreach (var entry in sections)
        {
            entry.Value["id"] = entry.Key.Substring(entry.Key.IndexOf(':') + 1).Trim();
            if (entry.Key.StartsWith("group:", StringComparison.OrdinalIgnoreCase)) result.groups.Add(entry.Value.ToObject<OverviewGroup>()!);
            else if (entry.Key.StartsWith("hotkey:", StringComparison.OrdinalIgnoreCase)) result.hotkeyPresets.Add(entry.Value.ToObject<OverviewHotkeyPreset>()!);
            else if (entry.Key.StartsWith("packhotkey:", StringComparison.OrdinalIgnoreCase)) result.packHotkeyPresets.Add(entry.Value.ToObject<OverviewHotkeyPreset>()!);
            else result.mods.Add(entry.Value.ToObject<OverviewModRule>()!);
        }
        return result;
    }
}






