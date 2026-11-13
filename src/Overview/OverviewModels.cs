#nullable enable
using System;
using System.Collections.Generic;

namespace ModConfigEnhance.Overview;

[Serializable]
internal sealed class OverviewState
{
    public int schemaVersion = 1;
    public OverviewPack pack = new();
    public bool packOverridden;
    public string packNameOverride = "";
    public bool globalUpdateAllowed = true;
    // Personal permission provenance; published pack values remain the author's defaults.
    public int updatePermissionVersion;
    public bool globalUpdateOverridden;
    public bool updateWarningsDismissed;
    public bool automaticUpdateChecks = true;
    public bool hideDisabled;
    public OverviewTagSelection modTagFilter = new(), hotkeyTagFilter = new();
    public List<OverviewGroup> groups = new();
    public List<OverviewModRule> mods = new();
    public List<string> customColors = new();
    public List<string> collapsedGroups = new();
    public List<string> collapsedHotkeyMods = new();
    // Retained only for compatibility with older personal/published data, never mod defaults.
    public List<OverviewHotkeyPreset> hotkeyPresets = new();
    public bool hotkeyAuthorPresetsImported = false;
    public bool profileHotkeysMigrated;
    public List<OverviewHotkeyPreset> packHotkeyPresets = new();
    /// <summary>1.1.4: controller keys / combos recorded in the profile (exported with packs; applied to this PC's
    /// combos when the profile is applied).</summary>
    public List<GamepadCombos.Item> gamepadComboPresets = new();
    public bool packHotkeyPresetMode;
    public bool packHotkeyPresetModeOverridden = false;
    // Conflict exceptions belong to the player, never the published pack.
    public List<OverviewIgnoredHotkeyConflict> ignoredHotkeyConflicts = new();
    [Newtonsoft.Json.JsonIgnore]
    public List<string> ignoredPackConflicts = new();
}

[Serializable]
internal sealed class OverviewPack
{
    public string id = "";
    public string name = "";
    public string version = "";
    public string author = "";
    public string contact = "";
    public string date = "";
    public string introFile = "introduction.md";
    // 1.3.0+: editable document. introFile stays a Markdown export so older builds can still show the page.
    public string introDocument = "";
    public string introTitle = "";
    public bool introEnabled;
    public bool updateAllowed = true;
    public string sourceNamespace = "";
    public string sourceName = "";
}

[Serializable]
internal sealed class OverviewGroup
{
    public string id = "";
    public string name = "";
    public string color = "#7CC7E6";
    public string description = "";
    /// <summary>Order among siblings (same <see cref="parentId"/>).</summary>
    public int order;
    /// <summary>2.3.0: the group this one hangs under; "" = top level. At most <see cref="GroupTree.MaxDepth"/> levels;
    /// a missing parent, cycle or over-deep chain is read as top level. See <see cref="GroupTree"/>.</summary>
    public string parentId = "";
}

[Serializable]
internal sealed class OverviewModRule
{
    public string id = "";
    public string alias = "";
    public string description = "";
    public string versionLabel = "";
    public bool descriptionOverridden;
    public string groupId = "";
    public int order;
    public bool updateAllowed = true;
    public bool updateOverridden;
    public string sourceNamespace = "";
    public string sourceName = "";
}

internal sealed partial class OverviewMod
{
    public string Id = "";
    public string Guid = "";
    public string Name = "";
    public string RawName = "";
    public string RawDescription = "";
    public string SettingDisplayName = "";
    public string PluginVersion = "";
    public string PackageVersion = "";
    /// <summary>2.4.4: lower-version manifest.json copies of this package inside its own folder (never used for the version).</summary>
    public List<string> StaleManifests = new();
    public string Author = "";
    public string Description = "";
    public string Location = "";
    public string PackageRoot = "";
    public string PackageName = "";
    public string PackageNamespace = "";
    public string SourceNamespace = "";
    public string SourceName = "";
    public string LatestVersion = "";
    public string UpdateNotes = "";
    public string CheckMessage = "";
    public bool Loaded;
    public bool TargetEnabled = true;
    public bool CanToggle = true;
    public string ToggleReason = "";
    public bool SourceConfirmed;
    public bool HasUpdate;
    public DateTime? LastCheckedUtc;
    internal List<OverviewPluginCopy> PluginCopies = new();
    // Every row (including this one) carrying the same GUID in another package folder; the draft enables at most one of them.
    internal List<OverviewMod> CopyRows = new();
    internal string CopyKey = "";
    internal bool DuplicateCopy => CopyRows.Count > 1;
    /// <summary>2.3.0: the mod whose folder this one is nested in (nearest, packaging layout excluded); null at top level.</summary>
    internal OverviewMod? Outer;
    /// <summary>2.3.4: a manifest package with no assembly at all (bundles, textures, data). Shown as a row with its own
    /// badge; it is neither disabled nor missing. Enablement renames its manifest like any other package.</summary>
    internal bool ResourcePack;
    /// <summary>2.3.4: a plugin DLL lying directly in plugins/ rather than in a package folder.</summary>
    internal bool LooseFile;
    /// <summary>Mods nested directly in this one's folder.</summary>
    internal List<OverviewMod> Inner = new();
    public List<string> PluginGuids = new();
    public List<string> AssemblyPaths = new();
    public OverviewModRule Rule = new();
    public string DisplayName => string.IsNullOrWhiteSpace(Rule.alias) ? Name : Rule.alias + "（" + Name + "）";
    public string DisplayVersion => !string.IsNullOrWhiteSpace(Rule.versionLabel) ? Rule.versionLabel : string.IsNullOrEmpty(PackageVersion) ? PluginVersion : PackageVersion;
    public string DisplayDescription => Rule.descriptionOverridden ? Rule.description : Description;
    public string SourceKey => SourceNamespace + "-" + SourceName;
}

internal readonly struct OverviewToggleResult
{
    public readonly bool Success;
    public readonly string Message;
    public OverviewToggleResult(bool success, string message) { Success = success; Message = message; }
}
