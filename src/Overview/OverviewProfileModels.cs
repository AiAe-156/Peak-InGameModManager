#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

internal enum OverviewProfileKind
{
    Default,
    Personal,
    Author
}

[Serializable]
internal sealed class OverviewProfileIndex
{
    public int schemaVersion = 1;
    public string viewedProfileId = "default";
    public string appliedProfileId = "default";
    public List<OverviewProfileReference> profiles = new();
}

[Serializable]
internal sealed class OverviewProfileReference
{
    public string id = "";
    public string name = "";
    public OverviewProfileKind kind = OverviewProfileKind.Personal;
    public string directory = "";
    public string source = "";
    public string legacyFile = "";
    public bool readOnly;
    public DateTime createdUtc;
    public DateTime modifiedUtc;
}

[Serializable]
internal sealed class OverviewProfile
{
    public int schemaVersion = 1;
    public string id = "";
    public string publicationId = "";
    public string name = "";
    public OverviewProfileKind kind = OverviewProfileKind.Personal;
    public string directory = "";
    public string source = "";
    public bool readOnly;
    public DateTime createdUtc;
    public DateTime modifiedUtc;
    public OverviewState state = new();
    public OverviewConfigSnapshot config = new();
    public List<OverviewProfileMod> mods = new();
    public string loadError = "";
    [JsonIgnore] public bool IsValid => loadError.Length == 0;
}

[Serializable]
internal sealed class OverviewProfileMod
{
    public string id = "";
    public string guid = "";
    public string name = "";
    public string version = "";
    public string pluginVersion = "", packageVersion = "";
    public string packageNamespace = "";
    public string packageName = "";
    public bool enabled = true;
    public bool installed = true;
    // Package folder (relative to BepInEx) of the copy this entry enables when the GUID is installed more than once; empty = no preference.
    public string copy = "";
    /// <summary>2.3.0: GUIDs of nested mods that were enabled when this (outer) mod was disabled; restored when it is enabled again.</summary>
    public List<string> nestedEnabled = new();
}

[Serializable]
internal sealed class OverviewAuthorProfileManifest
{
    public int schemaVersion = 1;
    public List<OverviewAuthorProfileReference> profiles = new();
}

[Serializable]
internal sealed class OverviewAuthorProfileReference
{
    public string id = "";
    public string name = "";
    public string directory = "";
}

[Serializable]
internal sealed class OverviewConfigSnapshot
{
    public int schemaVersion = 1;
    public DateTime capturedUtc;
    public List<OverviewConfigValue> values = new();
    public List<OverviewConfigFileStatus> files = new();
    [JsonIgnore] public bool IsEmpty => values == null || values.Count == 0;
}

[Serializable]
internal sealed class OverviewConfigValue
{
    public string pluginGuid = "";
    public string configFile = "";
    public string section = "";
    public string key = "";
    public string type = "";
    public string serializedValue = "";
    // Editing baseline includes disk absence as well as the independently captured live value.
    public bool diskBaselineKnown, diskExists;
    public bool liveBaselineKnown;
    public string liveValue = "";
    public string diskValue = "";
    public bool missing;
    public string missingReason = "";
    [JsonIgnore] public string Identity => pluginGuid.ToUpperInvariant() + "\n" + configFile.Replace('\\', '/').ToUpperInvariant() + "\n" + section + "\n" + key;
}

[Serializable]
internal sealed class OverviewConfigFileStatus
{
    public string pluginGuid = "";
    public string configFile = "";
    public bool loaded;
    public bool supported = true;
    public string message = "";
}

internal enum OverviewConfigDiffKind
{
    Equal,
    Changed,
    ProfileOnlyMissing,
    CurrentOnlyNotRecorded,
    Unsupported
}

internal sealed class OverviewConfigDiff
{
    public OverviewConfigDiffKind Kind;
    public OverviewConfigValue? ProfileValue;
    public OverviewConfigValue? CurrentValue;
    public string Message = "";
}

internal sealed class OverviewProfileSaveResult
{
    public OverviewProfile Profile = null!;
    public bool Forked;
}

internal sealed class OverviewProfileInitializationResult
{
    public OverviewProfile Default = null!;
    public OverviewProfile Current = null!;
    public bool MigratedLegacy;
}
