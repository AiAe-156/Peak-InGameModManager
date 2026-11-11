#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Updating;

// No Unity/BepInEx dependencies: shared by the game UI and the external worker (Updater/PackMaintenance.cs).
// Contracts for the two-phase maintenance flow: Preview (read-only except its own preview record) -> Execute (re-validates everything).

public static class MaintenanceOperations
{
    public const string DeletePack = "delete-pack";         // move a non-current pack into the worker's recoverable trash
    public const string DeleteMod = "delete-mod";           // write a new revision of a non-current pack without an explicit file list
    public const string CleanCache = "clean-cache";         // whitelisted disposable caches / completed transactions
    public const string PurgePackTrash = "purge-pack-trash"; // permanently delete trashed packs; needs PermanentConfirmed

    public static bool Known(string value) => value is DeletePack or DeleteMod or CleanCache or PurgePackTrash;
}

public static class MaintenanceScopes
{
    public const string Downloads = "downloads";       // BepInEx/IGMM/downloads
    public const string ViewCache = "view-cache";      // BepInEx/IGMM/packs/.view-cache
    public const string Transactions = "transactions"; // BepInEx/IGMM/packs/.transactions/<id> whose switch plan is installed or rolled-back
    public const string PackTrash = "pack-trash";      // BepInEx/IGMM/packs/.trash/<id>; only used by purge-pack-trash

    public const string ShopTasks = "shop-tasks";
    public const string ShopResources = "shop-resources";
    public static readonly string[] Cache = { Downloads, ViewCache, Transactions, ShopTasks, ShopResources };
}

public sealed class MaintenanceRequest
{
    public string Operation { get; set; } = "";
    public string PreviewId { get; set; } = "";                       // empty for preview, required for execute
    public string PackId { get; set; } = "";                          // delete-pack / delete-mod
    public List<string> ModFiles { get; set; } = new();               // delete-mod: explicit pack-relative paths (plugins/...), never wildcards
    public List<string> Scopes { get; set; } = new();                 // clean-cache: MaintenanceScopes.Cache subset; purge-pack-trash ignores it
    public List<string> SelectedPaths { get; set; } = new(); // exact preview item paths; never arbitrary disk targets
    public bool WholeCategory { get; set; } // class cleanup: enumerate safe candidates, never target the class root directly
    public bool PermanentConfirmed { get; set; }                      // purge-pack-trash only, at execute
}

public sealed class MaintenanceItem
{
    public string Kind { get; set; } = "";   // pack | mod-file | file | directory
    public string Scope { get; set; } = "";
    public string Path { get; set; } = "";   // relative to the BepInEx root, '/' separated, for display and for matching at execute time
    public long Bytes { get; set; }
    public int FileCount { get; set; }
    public string Stamp { get; set; } = ""; // content hash (mod files) or size/time digest; must be unchanged at execute time
}

public sealed class MaintenanceIssue
{
    public string Code { get; set; } = "";   // active-pack | pending-task | pending-update | dependency | not-in-pack | host-file | author-data | revision-changed | missing | links | ...
    public string Message { get; set; } = "";
}

public sealed class MaintenancePreview
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Operation { get; set; } = "";
    public string PackId { get; set; } = "";
    public string PackName { get; set; } = "";
    public List<string> ModFiles { get; set; } = new();
    public List<string> Scopes { get; set; } = new();
    public List<string> SelectedPaths { get; set; } = new();
    public List<MaintenanceItem> Items { get; set; } = new();
    public List<MaintenanceIssue> Blockers { get; set; } = new();  // any entry means Execute is refused
    public List<string> Warnings { get; set; } = new();
    public List<string> Details { get; set; } = new();
    public bool WholeCategory { get; set; }
    public long IndexRevision { get; set; }
    public long TotalBytes { get; set; }
    public int TotalFiles { get; set; }
    public string Fingerprint { get; set; } = "";
    public string CreatedUtc { get; set; } = "";
    public string ExpiresUtc { get; set; } = "";
    public bool CanExecute => Blockers.Count == 0 && Items.Count > 0;
}

public sealed class MaintenanceResult
{
    public int SchemaVersion { get; set; } = 1;
    public string PreviewId { get; set; } = "";
    public string Operation { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int RemovedItems { get; set; }
    public long FreedBytes { get; set; }
    public string TrashPath { get; set; } = "";    // delete-pack: where the pack was quarantined (relative to the BepInEx root)
    public string NewRevision { get; set; } = "";  // delete-mod: the new content path of the pack, e.g. revisions/<id>
    public List<string> Failures { get; set; } = new();
}
