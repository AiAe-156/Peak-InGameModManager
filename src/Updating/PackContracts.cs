#nullable enable
using System;
using System.Collections.Generic;

namespace ModConfigEnhance.Updating;

// No Unity/BepInEx dependencies: shared by the game and its external worker.
public sealed class PackIndex
{
    public int SchemaVersion { get; set; } = 1;
    public int MinimumWorkerProtocol { get; set; } = 1;
    public long Revision { get; set; }
    public string ActivePackId { get; set; } = "";
    public List<PackRecord> Packs { get; set; } = new();
    public List<PackFile> ActiveFiles { get; set; } = new();
    public List<PackHostPlugin> HostPlugins { get; set; } = new();
    public List<string> HostRoots { get; set; } = new();
}

public sealed class PackRecord
{
    // Absent fields are legacy, even in a schema-2 index. Completeness is always explicit.
    public int EnvironmentVersion { get; set; } = 1;
    public int MinimumWorkerProtocol { get; set; } = 1;
    public string RevisionStateHash { get; set; } = ""; // SHA256 of the immutable revision-state envelope

    public string Id { get; set; } = "";
    public string PublicationId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Content { get; set; } = "";
    public string AuthorRoot { get; set; } = "";
    public string ModifiedUtc { get; set; } = "";
    public string Language { get; set; } = ""; // language group key to apply when this pack is used; empty keeps the player's own choice
    public List<PackFile> Files { get; set; } = new();
    public List<PackHostPlugin> Requirements { get; set; } = new();
    public List<PackFile> CoreRequirements { get; set; } = new();
    public List<string> Problems { get; set; } = new();
    public PackReport? Report { get; set; } // persisted content summary, available without the original ZIP
    public PackRecord? PendingUpdate { get; set; }
    public PackRecord? UnsavedEnvironment { get; set; } // latest runtime revision; original saved Content/Files stay unchanged on direct switch
}

public sealed class PackFile
{
    public string Path { get; set; } = ""; // relative to BepInEx, never an absolute write destination
    public string Hash { get; set; } = "";
}

public sealed class PackHostPlugin
{
    public string Guid { get; set; } = "";
    public string Version { get; set; } = "";
}

public sealed class PackRequest
{
    public string Operation { get; set; } = "";
    public string Source { get; set; } = "";
    public string PackId { get; set; } = "";
    public MaintenanceRequest? Maintenance { get; set; }
    public bool SaveCurrentBeforeSwitch { get; set; } = true;
    public string SnapshotName { get; set; } = ""; // user-facing name for the first saved temporary modpack
    public string PreviewId { get; set; } = "";
    public int SourceProcessId { get; set; }
    public List<PackHostPlugin> HostPlugins { get; set; } = new();
    public List<string> HostRoots { get; set; } = new();
    public List<string> ConfirmedConfigFiles { get; set; } = new();
    public string GameLanguage { get; set; } = ""; // "zh-cn", "zh-tw", "en-us"
    public string PackLanguage { get; set; } = ""; // chosen group key from the preview; empty = keep the current choice
    public bool Approved { get; set; } // only "confirm-download" may carry it; the worker also checks it against the stored preview
    public string ApprovedPlan { get; set; } = ""; // DownloadPlan hash shown to the player; the worker never takes a source or URL from the confirmation
}

/// <summary>One Thunderstore package the preview resolved but did not download. The player must approve the whole list.</summary>
public sealed class PackDownloadItem
{
    public string Key { get; set; } = ""; // Namespace-Name
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string RequiredBy { get; set; } = "";
}

public sealed class PackTask
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string State { get; set; } = "";
    public string AttemptStartedUtc { get; set; } = "";
    public string AttemptEndedUtc { get; set; } = "";
    public string AttemptId { get; set; } = "";
    public string Message { get; set; } = "";
    public string Error { get; set; } = "";
    public PackRequest Request { get; set; } = new();
    public PackRecord Proposed { get; set; } = new();
    public List<string> Details { get; set; } = new();
    public PackReport Report { get; set; } = new();
    public List<string> UnknownConfigurations { get; set; } = new();
    public string StagingDirectory { get; set; } = "";
    public bool SingleProfile { get; set; }
    public MaintenancePreview? Maintenance { get; set; }
    public List<PackDownloadItem> PendingDownloads { get; set; } = new(); // resolved but not downloaded; empty once nothing is waiting for consent
    public string DownloadPlan { get; set; } = ""; // hash of PendingDownloads, produced by the worker
    public bool BlocksUpdates => State == "waiting-exit" || State == "switching" || State == "recovery-required" || State == "saving";
}

/// <summary>Structured, player-readable preview produced by the worker. Details stays as a plain-text log fallback.</summary>
public sealed class PackReport
{
    public string Verdict { get; set; } = ""; // ready | attention
    public string Headline { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public int ModCount { get; set; }
    public int EnabledCount { get; set; }
    public int DownloadCount { get; set; }
    public int BundledCount { get; set; }
    public string Kind { get; set; } = ""; // full | store | data
    public string KindText { get; set; } = "";
    public List<PackLanguage> Languages { get; set; } = new(); // language groups shipped inside the pack
    public string GameLanguage { get; set; } = ""; // game language code the preview was made for
    public int FileCount { get; set; }
    public List<PackReportLine> Lines { get; set; } = new();
    public List<PackReportFile> Files { get; set; } = new();
    public Dictionary<string, string> FolderVersions { get; set; } = new(); // "plugins/0核心&基础模组/[优化]XXX" -> "1.1.8"
}

public sealed class PackReportLine
{
    public string Section { get; set; } = ""; // problem | prereq | download | mod | note
    public string State { get; set; } = "info"; // ok | info | warn | error
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Hint { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Ignorable { get; set; }
    public List<string> Items { get; set; } = new();
}

public sealed class PackReportFile
{
    public string Group { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>Shared environment boundary. Does not grant ordinary updater/core write authority.</summary>
public static class PackEnvironmentPolicy
{
    public const int LegacyVersion = 1;
    public const int CompleteVersion = 2;
    public const int WorkerProtocol = 2;

    public static bool IsComplete(PackRecord pack) => pack != null
        && pack.EnvironmentVersion == CompleteVersion && pack.MinimumWorkerProtocol == WorkerProtocol;

    // Deliberately stricter than Path.GetFullPath: reject aliases, ADS and Windows device paths on every OS.
    public static bool TryNormalize(string path, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrEmpty(path)) return false;
        string p = path.Replace('\\', '/');
        var parts = p.Split('/');
        foreach (string part in parts)
        {
            if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(" ", StringComparison.Ordinal)
                || part.EndsWith(".", StringComparison.Ordinal)) return false;
            foreach (char c in part) if (c < 32 || ":*?\"<>|".IndexOf(c) >= 0) return false;
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL"
                || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                    && stem[3] >= '0' && stem[3] <= '9') return false;
        }
        normalized = p;
        return true;
    }

    public static bool LegacyGlobalConfig(string relative)
    {
        string p = relative.Replace('\\', '/');
        return ManagerIdentity.IsManagerConfigFile(p) || Equal(p, "BepInEx.cfg") || Equal(p, "AutoTranslatorConfig.ini")
            || Under(p, ManagerIdentity.ConfigDirectory) || Under(p, ManagerIdentity.LegacyConfigDirectory)
            || Under(p, ManagerIdentity.RuntimeDirectory) || Under(p, ManagerIdentity.LegacyRuntimeDirectory) || Under(p, "Translation")
            || p.StartsWith("XUnity", StringComparison.OrdinalIgnoreCase)
            || p.IndexOf("/_AutoGeneratedTranslations", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Management/preferences are not runtime dependencies. Translator config/cache/dictionaries ARE runtime data.
    public static bool ManagementConfig(string relative) => ManagerIdentity.IsManagerConfigFile(relative)
        || ManagerIdentity.IsConfigDirectory(relative) || Under(relative, ManagerIdentity.ConfigDirectory) || Under(relative, ManagerIdentity.LegacyConfigDirectory)
        || ManagerIdentity.IsRuntimeDirectory(relative) || Under(relative, ManagerIdentity.RuntimeDirectory) || Under(relative, ManagerIdentity.LegacyRuntimeDirectory);

    public static bool ManagedPath(string path, int environmentVersion)
    {
        if (!TryNormalize(path, out string p)) return false;
        if (Under(p, "plugins") || Under(p, "patchers")) return environmentVersion == 1 || environmentVersion == 2;
        if (environmentVersion == 2 && Under(p, "core")) return true;
        return Under(p, "config") && (environmentVersion == 1 ? !LegacyGlobalConfig(p.Substring(7))
            : environmentVersion == 2 && !ManagementConfig(p.Substring(7)));
    }
    public static bool ManagedPath(string path, PackRecord pack) => ManagedPath(path, pack.EnvironmentVersion);
    private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static bool Under(string p, string root) => p.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
}
