#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.SceneManagement;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewRepository
{
    private OverviewProfileStore? _profiles;
    private OverviewConfigService? _configs;
    private OverviewConfigSnapshot _draftConfig = new();
    private List<OverviewProfileMod> _draftMods = new();
    private OverviewState? _migrationState;
    private string _migrationResources = "";
    private string _savedDraftSignature = "";
    private OverviewConfigSnapshot _editingRuntimeBaseline = new();
    private bool _legacyTargetsNeedSave;
    internal string ReadWarning { get; private set; } = "";
    public OverviewProfileStore Profiles => _profiles ?? throw new InvalidOperationException(Loc.T("Profiles have not been initialized.", "方案尚未初始化。", "方案尚未初始化。"));
    public OverviewConfigService Configs => _configs ??= new OverviewConfigService(Path.GetDirectoryName(ConfigRootDirectory), Path.Combine(Updating.MceDirectories.Backups(ConfigRootDirectory), "config"));
    public OverviewConfigSnapshot DraftConfig => _draftConfig;
    public IReadOnlyList<OverviewProfileMod> DraftMods => _draftMods;
    public void SaveUiState() { SaveRuntimeState(); _loadedRevision = ++_sharedRevision; }
    public string ViewedProfileId => _appliedView ? Profiles.AppliedId : Profiles.ViewedId;
    private string ProfileRuntimeDirectory => _profiles?.RuntimeDirectory ?? PackLibrary.RuntimeRoot;
    public OverviewProfile ViewedProfile => _appliedView ? Profiles.Applied : Profiles.Viewed;
    public bool IsProfileDirty { get { if (_appliedView) return false; MergeEditedRules(); return _savedDraftSignature != DraftSignature(State, _draftConfig, _draftMods); } }
    /// <summary>1.1.4 diagnosis (user: an unsaved prompt on the first switch to the introduction): which part differs
    /// from the saved baseline. Logged once per prompt; parts are compared separately.</summary>
    internal string DirtyParts()
    {
        if (_appliedView) return "applied-view";
        var parts = new List<string>();
        var saved = _savedDraftProfile;
        if (saved?.state == null || saved.config == null || saved.mods == null) return "no-baseline";
        string Sig(OverviewState s, OverviewConfigSnapshot c, IEnumerable<OverviewProfileMod> m) => DraftSignature(s, c, m);
        if (Sig(State, saved.config, saved.mods) != Sig(saved.state, saved.config, saved.mods))
        {
            if (Newtonsoft.Json.JsonConvert.SerializeObject(State.pack) != Newtonsoft.Json.JsonConvert.SerializeObject(saved.state.pack)) parts.Add("pack");
            if (Newtonsoft.Json.JsonConvert.SerializeObject(State.groups.OrderBy(g => g.id)) != Newtonsoft.Json.JsonConvert.SerializeObject(saved.state.groups.OrderBy(g => g.id))) parts.Add("groups");
            if (parts.Count == 0) parts.Add("mod-rules");
        }
        if (Sig(saved.state, _draftConfig, saved.mods) != Sig(saved.state, saved.config, saved.mods))
        {
            var before = saved.config.values.ToDictionary(v => v.Identity, v => v.serializedValue);
            var changed = _draftConfig.values.Where(v => !before.TryGetValue(v.Identity, out var old) || old != v.serializedValue).Select(v => v.section + "/" + v.key).Take(5).ToList();
            parts.Add("parameters(" + _draftConfig.values.Count + " vs " + saved.config.values.Count + (changed.Count > 0 ? ": " + string.Join(", ", changed) : "") + ")");
        }
        if (Sig(saved.state, saved.config, _draftMods) != Sig(saved.state, saved.config, saved.mods)) parts.Add("enablement");
        return parts.Count == 0 ? "none" : string.Join("; ", parts);
    }
    private string _authorComparisonId = "", _authorBaselineSignature = "";
    private bool _authorModified;
    private float _authorComparisonUntil;
    internal bool IsModifiedFromAuthor
    {
        get
        {
            if (_authorComparisonId == ViewedProfileId && UnityEngine.Time.unscaledTime < _authorComparisonUntil) return _authorModified;
            var baseline = Profiles.AuthorBaseline(ViewedProfileId);
            if (_authorComparisonId != ViewedProfileId)
            {
                _authorComparisonId = ViewedProfileId;
                _authorBaselineSignature = baseline == null ? "" : PresetSignature(baseline.config, baseline.mods);
            }
            _authorComparisonUntil = UnityEngine.Time.unscaledTime + .2f;
            return _authorModified = baseline != null && PresetSignature(_draftConfig, _draftMods) != _authorBaselineSignature;
        }
    }
    /// <summary>PEAK's main menu is the scene named "Title" ("Pretitle" is only the boot screen). "MainMenu" is kept for older builds.</summary>
    private static string PresetSignature(OverviewConfigSnapshot config, IEnumerable<OverviewProfileMod> mods)
        => JsonConvert.SerializeObject(new { values = config.values.OrderBy(v => v.Identity).Select(v => new { v.Identity, v.type, v.serializedValue }),
            enabled = mods.OrderBy(m => m.guid).Select(m => new { m.guid, m.enabled }) });
    public static bool IsMainMenuScene { get { string name = SceneManager.GetActiveScene().name; return name.Equals("Title", StringComparison.OrdinalIgnoreCase) || name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase); } }

    public void Reload()
    {
        var oldState = State; var oldProfiles = _profiles; var oldConfig = _draftConfig;
        var oldMods = _draftMods; var oldDirectory = PackDirectory; var oldApplied = _appliedSnapshot;
        try
        {
        if (_migrationState == null)
        {
            ReloadLegacy();
            bool freshPack = PackLibrary.Managed && !File.Exists(Path.Combine(PackLibrary.ProfileRoot, "index.json"));
            _migrationState = freshPack ? new OverviewState() : Clone(State);
            _migrationResources = freshPack ? "" : PackDirectory;
        }
        var previous = _appliedSnapshot?.config ?? _profiles?.Applied.config;
        var initial = Configs.Capture(previous);
        // Scan shares only validated physical discovery with the later list scan.
        // Its rows/rules belong to this initialization stage, before LoadProfileDraft.
        var installed = new OverviewCatalogue().Scan(this);
        ReloadInstalled = installed;
        _profiles = new OverviewProfileStore(PackLibrary.Managed ? PackLibrary.ProfileRoot : Path.Combine(ConfigRootDirectory, "profiles"), _plugins);
        bool first = !File.Exists(Profiles.IndexPath);
        if (first)
        {
            string backup = Path.Combine(Updating.MceDirectories.Backups(ConfigRootDirectory), "before-profiles-" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            Directory.CreateDirectory(backup);
            string legacy = Path.Combine(_legacyRootDirectory, "state.json");
            if (File.Exists(legacy)) File.Copy(legacy, Path.Combine(backup, "state.json"), false);
            AtomicWrite(Path.Combine(backup, "config-values.json"), JsonConvert.SerializeObject(initial, Formatting.Indented));
        }
        Profiles.Initialize(_migrationState, _migrationResources, initial, installed);
        MigrateRuntimeFiles();
        LoadAppliedSnapshot();
        LoadProfileDraft();
        _loadedRevision = _sharedRevision;
        // Legacy deferred application is migrated as a reviewable draft, never applied
        // automatically while opening the configuration UI.
        }
        catch
        {
            State = oldState; _profiles = oldProfiles; _draftConfig = oldConfig; _draftMods = oldMods;
            PackDirectory = oldDirectory; _appliedSnapshot = oldApplied;
            throw;
        }
    }

    private void MigrateRuntimeFiles()
    {
        Directory.CreateDirectory(RootDirectory);
        string cache = Path.Combine(_legacyRootDirectory, "updates.json"), target = Path.Combine(RootDirectory, "updates.json");
        if (!File.Exists(target) && File.Exists(cache)) File.Copy(cache, target, false);
        if (!File.Exists(Path.Combine(RootDirectory, "ui.json")))
        {
            var original = State; State = Clone(_migrationState!);
            SaveRuntimeState(); State = original;
        }
    }

    private void LoadProfileDraft()
    {
        OverviewProfile profile = _appliedView ? _appliedSnapshot! : ViewedProfile;
        ReadWarning = ""; _restoreSavedBaseline = false;
        if (!profile.IsValid && !_appliedView)
        {
            ReadWarning = Loc.T("The author profile is unavailable; previewing a personal profile while keeping live settings: ", "作者方案不可用，已切换到个人方案预览；实际配置保持原样：", "作者方案無法使用，已切換至個人方案預覽；實際設定保持原樣：") + profile.loadError;
            var personal = Profiles.ListProfiles().FirstOrDefault(p => !p.readOnly && p.IsValid);
            if (personal == null) throw new InvalidDataException(ReadWarning);
            Profiles.Select(personal.id); profile = personal; Plugin.Log.LogWarning(ReadWarning);
        }
        if (!profile.IsValid) throw new InvalidDataException(Loc.T("Could not read the profile; live settings were kept: ", "方案读取失败，未改变实际配置：", "方案讀取失敗，未變更實際設定：") + profile.loadError);
        State = Clone(profile.state);
        _temporaryRules.Clear();
        _draftConfig = Clone(profile.config);
        _draftMods = Clone(profile.mods);
        PackDirectory = profile.directory;
        if (PackWorkspaceInformation.Read(Profiles.RootDirectory) == null && Profiles.IsPackConfiguration(profile) && !profile.readOnly && Profiles.AuthorBaseline(profile.id) == null)
            Profiles.CaptureOriginalPresetBaseline(profile);
        State.packOverridden = !profile.readOnly;
        HasPublishedPack = profile.readOnly || profile.source.Length > 0 || Profiles.IsPackConfiguration(profile);
        _published = profile.readOnly ? Clone(profile.state) : new OverviewState();
        if (!profile.readOnly && profile.source.Length > 0)
        {
            var original = Profiles.ListProfiles().FirstOrDefault(p => p.id == profile.source && p.readOnly);
            if (original?.IsValid == true) _published = Clone(original.state);
            else if (_appliedSnapshot != null && (_appliedSnapshot.id == profile.source || _appliedSnapshot.source == profile.source)) _published = Clone(_appliedSnapshot.state);
        }
        if (!_appliedView && PackWorkspaceInformation.Read(Profiles.RootDirectory) is { } packInformation)
        {
            State.pack = Clone(packInformation.pack); State.groups = Clone(packInformation.groups); State.mods = Clone(packInformation.rules);
            string resources = Path.GetFullPath(packInformation.resourceDirectory);
            string boundary = Path.GetFullPath(Path.GetDirectoryName(Profiles.RootDirectory)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resources.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pack resource workspace is outside its boundary.");
            PackDirectory = resources;
        }
        LoadRuntimeState();
        if (!_appliedView) MigrateLegacyHotkeyTargets();
        MarkSavedDraftBaseline();
        _editingRuntimeBaseline = Configs.Capture(_draftConfig);
        RestoreRecoveryDraft();
        MigrateLegacyUnappliedDraft();
    }

    private void LoadRuntimeState()
    {
        string file = Path.Combine(RootDirectory, "ui.json");
        if (!File.Exists(file)) return;
        try
        {
            var ui = JObject.Parse(File.ReadAllText(file, Encoding.UTF8));
            State.hideDisabled = ui.Value<bool?>("hideDisabled") ?? false;
            State.modTagFilter = ui["modTagFilter"]?.ToObject<OverviewTagSelection>() ?? new();
            State.hotkeyTagFilter = ui["hotkeyTagFilter"]?.ToObject<OverviewTagSelection>() ?? new();
            State.collapsedGroups = ui["collapsedGroups"]?.ToObject<List<string>>() ?? new();
            State.collapsedHotkeyMods = ui["collapsedHotkeyMods"]?.ToObject<List<string>>() ?? new();
            State.ignoredHotkeyConflicts = ui["ignoredHotkeyConflicts"]?.ToObject<List<OverviewIgnoredHotkeyConflict>>() ?? new();
            State.ignoredPackConflicts = ui["ignoredPackConflicts"]?.ToObject<List<string>>()?.Where(key => key != null && key.Length == 64).Distinct(StringComparer.Ordinal).ToList() ?? new();
            State.globalUpdateAllowed = ui.Value<bool?>("globalUpdateAllowed") ?? State.globalUpdateAllowed;
            State.globalUpdateOverridden = ui.Value<bool?>("globalUpdateOverridden") ?? State.globalUpdateOverridden;
            State.updateWarningsDismissed = ui.Value<bool?>("updateWarningsDismissed") ?? false;
            State.automaticUpdateChecks = ui.Value<bool?>("automaticUpdateChecks") ?? State.automaticUpdateChecks;
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[方案] 页面状态无法读取，保留原文件：" + ex.Message); }
    }

    private void SaveRuntimeState()
    {
        AtomicWrite(Path.Combine(RootDirectory, "ui.json"), JsonConvert.SerializeObject(new
        {
            State.hideDisabled, State.modTagFilter, State.hotkeyTagFilter, State.collapsedGroups,
            State.collapsedHotkeyMods, State.ignoredHotkeyConflicts, State.ignoredPackConflicts,
            State.globalUpdateAllowed, State.globalUpdateOverridden, State.updateWarningsDismissed, State.automaticUpdateChecks
        }, Formatting.Indented));
    }

    private void SaveProfileDraft(string? unifiedName = null)
    {
        SaveRuntimeState();
        if (_appliedView) return;
        MergeEditedRules();
        if (!IsProfileDirty && !_legacyTargetsNeedSave && unifiedName == null) return;
        var persistedState = Clone(State);
        // Legacy profile serialization strips translated display fields; authoritative
        // pack presentation is saved separately and must retain them.
        var saved = Profiles.Save(ViewedProfileId, persistedState, _draftConfig, DraftCatalogue(), unifiedName: unifiedName);
        State.packNameOverride = "";
        if (PackWorkspaceInformation.Read(Profiles.RootDirectory) is { } packInformation)
            PackDirectory = packInformation.resourceDirectory;
        else PackDirectory = saved.Profile.directory;
        PackWorkspaceInformation.Write(Profiles.RootDirectory, State, PackDirectory);
        _legacyTargetsNeedSave = false; _restoreSavedBaseline = false;
        MarkSavedDraftBaseline();
        ClearRecoveryDraft();
        if (unifiedName != null) SyncAppliedProfileName(saved.Profile.id, saved.Profile.name);
    }

    private IEnumerable<OverviewMod> DraftCatalogue() => _draftMods.Select(m => new OverviewMod
    {
        Id = m.id, Guid = m.guid, Name = m.name, PluginVersion = string.IsNullOrEmpty(m.pluginVersion) ? m.version : m.pluginVersion,
        PackageVersion = m.packageVersion, PackageNamespace = m.packageNamespace, PackageName = m.packageName,
        TargetEnabled = m.enabled
    });

    public void SelectProfile(string id)
    {
        string old = Profiles.ViewedId;
        PersistRecoveryDraft();
        try { Profiles.Select(id); LoadProfileDraft(); _loadedRevision = ++_sharedRevision; }
        catch { Profiles.Select(old); throw; }
    }

    public void DiscardProfileDraft() { RestoreSavedDraftBaseline(); ClearRecoveryDraft(); LoadProfileDraft(); }

    public void EnsureEditableProfile(string? name = null)
    {
        if (!ViewedProfile.readOnly) return;
        OverviewState draft = State;
        OverviewConfigSnapshot config = _draftConfig;
        List<OverviewProfileMod> mods = _draftMods;
        var working = Profiles.BeginAuthorEditing(ViewedProfileId);
        State = draft; _draftConfig = config; _draftMods = mods; PackDirectory = working.directory;
        State.packNameOverride = "";
        State.packOverridden = true;
        _loadedRevision = ++_sharedRevision;
    }

    /// <summary>从指定方案派生一份个人方案；Copy 本身会把查看目标指向副本。</summary>
    public OverviewProfile CreateProfile(string sourceId, string name, bool currentParameters)
    {
        PersistRecoveryDraft();
        var copy = Profiles.Copy(sourceId, name);
        LoadProfileDraft();
        if (currentParameters) CaptureCurrentIntoProfile();
        _loadedRevision = ++_sharedRevision;
        return copy;
    }

    internal void SaveDraftAsNewProfile(string name)
    {
        var state = State; var config = _draftConfig; var mods = _draftMods;
        var copy = Profiles.Copy(ViewedProfileId, name);
        State = state; _draftConfig = config; _draftMods = mods;
        PackDirectory = PackWorkspaceInformation.Read(Profiles.RootDirectory)?.resourceDirectory ?? copy.directory;
        _savedDraftProfile = Clone(copy);
        _savedDraftSignature = DraftSignature(copy.state, copy.config, copy.mods);
        _loadedRevision = ++_sharedRevision;
    }

    public void RenameProfile(string id, string name)
    {
        var renamed = Profiles.Rename(id, name);
        if (id == ViewedProfileId)
        {
            State.packNameOverride = "";
            _savedDraftProfile.state.packNameOverride = "";
            _savedDraftSignature = DraftSignature(_savedDraftProfile.state, _savedDraftProfile.config, _savedDraftProfile.mods);
        }
        SyncAppliedProfileName(id, renamed.name); _loadedRevision = ++_sharedRevision;
    }

    public string DeleteProfile(string id)
    {
        if (id == Profiles.AppliedId) throw new InvalidOperationException(Loc.T("Apply another profile before deleting the currently applied profile.", "请先应用另一套方案，再删除当前已应用方案。", "請先套用另一套方案，再刪除目前已套用的方案。"));
        string backup = Profiles.Trash(id);
        // 回收的若是正在查看的方案，Trash 已经把查看目标修正到别处，这里重新读草稿。
        LoadProfileDraft(); _loadedRevision = ++_sharedRevision; return backup;
    }

    public void CaptureCurrentIntoProfile()
    {
        EnsureEditableProfile();
        _draftConfig = Configs.Capture(_draftConfig);
        var installed = new OverviewCatalogue().Scan(this);
        foreach (var m in installed)
        {
            var prior = _draftMods.FirstOrDefault(p => p.guid == m.Guid);
            if (prior == null) { prior = new OverviewProfileMod { id = m.Id, guid = m.Guid, name = m.Name }; _draftMods.Add(prior); }
            prior.version = m.DisplayVersion; prior.pluginVersion = m.PluginVersion; prior.packageVersion = m.PackageVersion; prior.enabled = m.TargetEnabled; prior.installed = true;
        }
        Save();
    }

    public int IncludeUnrecordedCurrentConfiguration()
    {
        EnsureEditableProfile();
        var current = Configs.Capture(_draftConfig);
        var recorded = new HashSet<string>(_draftConfig.values.Select(v => v.Identity), StringComparer.Ordinal);
        int added = 0;
        foreach (var value in current.values.Where(v => !v.missing))
            if (recorded.Add(value.Identity)) { _draftConfig.values.Add(Clone(value)); added++; }
        _draftConfig.files = current.files; _draftConfig.capturedUtc = current.capturedUtc;
        return added;
    }

    public void IncludeModInProfile(OverviewMod mod) => SetPlannedEnabled(new[] { mod }, mod.TargetEnabled);

    public bool HasRecordedMod(OverviewMod mod) => _draftMods.Any(m => m.guid == mod.Guid);
    public bool PlannedEnabled(OverviewMod mod) => OverviewEnablementDraft.Planned(_draftMods, mod);
    /// <summary>2.3.0: an inner mod cannot be switched on while the mod it is nested in is planned off.</summary>
    public bool BlockedByOuter(OverviewMod mod) => OverviewEnablementDraft.BlockedByOuter(_draftMods, mod);
    public void SetPlannedEnabled(IReadOnlyList<OverviewMod> mods, bool enabled)
    {
        EnsureEditableProfile();
        foreach (var mod in mods)
        {
            if (!mod.CanToggle && enabled != mod.TargetEnabled) throw new InvalidOperationException(mod.Name + "：" + mod.ToggleReason);
            OverviewEnablementDraft.SetWithNested(_draftMods, mod, enabled);
        }
        PersistRecoveryDraft();
    }

    public OverviewConfigValue? FindDraftConfig(string guid, ConfigEntryBase entry)
    {
        string relative = RelativeConfig(entry.ConfigFile.ConfigFilePath);
        return _draftConfig.values.FirstOrDefault(v => v.pluginGuid == guid && v.configFile.Equals(relative, StringComparison.OrdinalIgnoreCase) &&
            v.section == entry.Definition.Section && v.key == entry.Definition.Key);
    }

    private string RelativeConfig(string path)
    {
        string prefix = Path.GetFullPath(Path.GetDirectoryName(ConfigRootDirectory)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full.Substring(prefix.Length).Replace('\\', '/') : "";
    }

    /// <summary>1.1.4: the settings page writes values directly. Mirror the live value into the viewed profile's working
    /// copy and its saved baseline (memory only), so the overview neither shows it as unsaved nor reverts it on a later
    /// save. Only for parameters the profile already records; nothing is written to disk here.</summary>
    /// <summary>The draft identity of one config entry (same form as <see cref="OverviewConfigValue.Identity"/>).</summary>
    internal string ConfigIdentity(string guid, ConfigEntryBase entry)
        => new OverviewConfigValue { pluginGuid = guid, configFile = RelativeConfig(entry.ConfigFile.ConfigFilePath), section = entry.Definition.Section, key = entry.Definition.Key }.Identity;
    internal void FollowLiveValue(string guid, ConfigEntryBase entry)
    {
        if (_appliedView || guid == Plugin.PluginGuid || ViewedProfileId != Profiles.AppliedId) return; // a previewed profile is not the live one
        string file = RelativeConfig(entry.ConfigFile.ConfigFilePath); if (file.Length == 0) return;
        string live = entry.GetSerializedValue();
        void Update(OverviewConfigSnapshot? snapshot)
        {
            if (snapshot?.values == null) return;
            foreach (var value in snapshot.values)
                if (value.pluginGuid.Equals(guid, StringComparison.OrdinalIgnoreCase) && value.configFile.Replace('\\', '/').Equals(file.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)
                    && value.section == entry.Definition.Section && value.key == entry.Definition.Key) value.serializedValue = live;
        }
        bool wasClean = !IsProfileDirty;
        Update(_draftConfig); Update(_savedDraftProfile.config); Update(_editingRuntimeBaseline);
        // The saved baseline holds the live value too: recompute its signature from the baseline itself, so undoing
        // other edits later still reads as clean (review #7).
        _savedDraftSignature = wasClean ? DraftSignature(State, _draftConfig, _draftMods) : DraftSignature(_savedDraftProfile.state, _savedDraftProfile.config, _savedDraftProfile.mods);
        InvalidatePendingView();
    }
    public bool SetDraftConfig(string guid, ConfigEntryBase entry, string serialized, out string error)
    {
        var key = new OverviewConfigValue
        {
            pluginGuid = guid, configFile = RelativeConfig(entry.ConfigFile.ConfigFilePath), section = entry.Definition.Section,
            key = entry.Definition.Key, type = entry.SettingType.FullName ?? entry.SettingType.Name, serializedValue = entry.GetSerializedValue()
        };
        if (guid == Plugin.PluginGuid || key.configFile.Length == 0) { error = Loc.T("This is a global setting and is independent of profiles.", "此项属于全局配置，不参与方案切换。", "此項屬於全域設定，與方案切換獨立。"); return false; }
        return SetDraftConfig(key, serialized, out error);
    }

    public bool SetDraftConfig(OverviewConfigValue key, string value, out string error)
    {
        if (!Configs.TryValidateDraft(key, value, out string normalized, out error)) return false;
        EnsureEditableProfile();
        bool success = Configs.TryEditDraft(_draftConfig, key, normalized, out error);
        if (success) PersistRecoveryDraftSoon(); // 1.1.4: one write after a slider drag, not one per frame
        return success;
    }

    public void ClearDraftConfig(string guid, ConfigEntryBase entry)
    {
        EnsureEditableProfile();
        var key = FindDraftConfig(guid, entry); if (key != null) _draftConfig.values.Remove(key);
        PersistRecoveryDraft();
    }

    public bool SetDraftConfigs(IEnumerable<(string Guid, ConfigEntryBase Entry, string Serialized)> values, out string error)
    {
        var draft = Clone(_draftConfig);
        foreach (var item in values)
        {
            var key = new OverviewConfigValue { pluginGuid = item.Guid, configFile = RelativeConfig(item.Entry.ConfigFile.ConfigFilePath),
                section = item.Entry.Definition.Section, key = item.Entry.Definition.Key,
                type = item.Entry.SettingType.FullName ?? "", serializedValue = item.Entry.GetSerializedValue() };
            if (!Configs.TryEditDraft(draft, key, item.Serialized, out error)) return false;
        }
        EnsureEditableProfile(); _draftConfig = draft; PersistRecoveryDraft(); error = ""; return true;
    }

    public void ClearAllDraftHotkeys(IEnumerable<(string Guid, ConfigEntryBase Entry)> entries)
    {
        EnsureEditableProfile();
        foreach (var item in entries) { var value = FindDraftConfig(item.Guid, item.Entry); if (value != null) _draftConfig.values.Remove(value); }
        PersistRecoveryDraft();
    }

    private void MigrateLegacyHotkeyTargets()
    {
        _legacyTargetsNeedSave = false;
        if (State.profileHotkeysMigrated) return;
        if (State.packHotkeyPresetMode)
        {
            var live = Configs.LoadedEntries();
            foreach (var preset in State.packHotkeyPresets.Where(p => !p.cleared))
            {
                var match = live.FirstOrDefault(pair => pair.Key.StartsWith(preset.guid.ToUpperInvariant() + "\n", StringComparison.Ordinal) &&
                    pair.Value.Definition.Section == preset.section && pair.Value.Definition.Key == preset.key);
                if (match.Value == null || preset.type.Length > 0 && preset.type != match.Value.SettingType.FullName) continue;
                var key = new OverviewConfigValue { pluginGuid = preset.guid, configFile = RelativeConfig(match.Value.ConfigFile.ConfigFilePath),
                    section = preset.section, key = preset.key, type = match.Value.SettingType.FullName ?? "" };
                if (Configs.TryEditDraft(_draftConfig, key, preset.value, out string error)) _legacyTargetsNeedSave = true;
                else Plugin.Log.LogWarning("[方案] 旧快捷键目标未迁入，保持现值：" + preset.id + "：" + error);
            }
        }
        State.profileHotkeysMigrated = true;
    }

    public IReadOnlyList<OverviewConfigDiff> ParameterDifferences() => Configs.Diff(_draftConfig, Configs.Capture());
    public bool HasCurrentParameterChanges => ParameterDifferences().Any(d => d.Kind == OverviewConfigDiffKind.Changed);

    // Shown the moment the toolbar button is focused, so it only formats counts the page already holds.
    // A full ParameterDifferences() here re-captured every config file on each focus change.
    public string ProfileSummary(ProfilePendingState pending)
    {
        return Loc.F("Editing: {0}\nIn use: {1}\n{2} unsaved; {3} next-round; {4} game-restart.\nSave writes changes; each mod determines when they take effect.",
            "正在编辑：{0}\n当前使用：{1}\n{2} 项未保存；{3} 项重新开局生效；{4} 项重启游戏生效。\n保存后写入，各项按模组声明的时机生效。",
            "正在編輯：{0}\n目前使用：{1}\n{2} 項未儲存；{3} 項重新開局生效；{4} 項重新啟動遊戲生效。\n儲存後寫入，各項依模組宣告的時機生效。",
            ViewedProfile.name, AppliedProfileName, pending.UnsavedCount, pending.RoundRestartCount, pending.RestartCount);
    }

    public OverviewToggleResult ApplyViewedProfile(IReadOnlyList<OverviewMod> mods, bool mainMenu, bool skipEnablement = false)
        => ApplyProfileTransaction(mods, mainMenu, skipEnablement);

    public string ExportViewedProfile() => ExportProfiles(new[] { ViewedProfileId });
    public string ExportProfiles(IEnumerable<string> ids, IEnumerable<string>? languages = null)
    {
        var selected = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        return ConfigurationPresetFile.Export(Profiles.ListProfiles().Where(p => selected.Contains(p.id)), Updating.MceDirectories.PackOutput(ConfigRootDirectory));
    }

    internal void RestoreOriginalPresetDraft()
    {
        var original = Profiles.GetOriginalPreset(ViewedProfileId);
        _draftConfig = Clone(original.config); _draftMods = Clone(original.mods);
        PersistRecoveryDraft();
    }

    internal void ImportConfigurationDraft(string path)
    {
        var imported = ConfigurationPresetFile.Read(path);
        EnsureEditableProfile();
        _draftConfig = Clone(imported.config); _draftMods = Clone(imported.mods);
        PersistRecoveryDraft();
        _loadedRevision = ++_sharedRevision;
    }
    internal List<OverviewProfileExport.ExportItem> PrepareExport(IEnumerable<string> ids)
    {
        // Export reads committed content. Browsing/exporting must never commit unrelated drafts.
        var profiles = Profiles.ListProfiles().Select(Clone).ToList();
        // 1.1.4: private-looking values never leave in a profile snapshot (clones only; stored profiles are untouched).
        foreach (var profile in profiles) PublicationScrub.ScrubSnapshot(profile.config);
        var information = PackWorkspaceInformation.Read(Profiles.RootDirectory);
        if (information != null)
            foreach (var profile in profiles)
            {
                profile.state.pack = Clone(information.pack); profile.state.groups = Clone(information.groups); profile.state.mods = Clone(information.rules);
                profile.directory = information.resourceDirectory;
            }
        return OverviewProfileExport.PrepareItems(profiles, ids);
    }
}
