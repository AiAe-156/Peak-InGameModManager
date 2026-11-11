#nullable enable
using System.Collections.Generic;
namespace ModConfigEnhance.Updating;
public sealed class LayoutQueueRequest
{
    public string Operation { get; set; } = "preview";
    public string PreviewId { get; set; } = "";
    public string PlanToken { get; set; } = "";
    public string AppliedProfileId { get; set; } = "";
    public string AppliedSnapshotPath { get; set; } = "";
    public string AppliedSnapshotHash { get; set; } = "";
    public int SourceProcessId { get; set; }
    public InstallationLayoutMode Mode { get; set; }
    public List<LayoutQueueGroup> Groups { get; set; } = new();
    public List<LayoutQueueMod> Mods { get; set; } = new();
    /// <summary>2.3.0: rows (when present the worker uses the group-folder organizer).</summary>
    public List<LayoutQueueRow> Rows { get; set; } = new();
    /// <summary>Folder name for mods nobody grouped, in the user's language.</summary>
    public string UngroupedFolder { get; set; } = "Ungrouped";
    /// <summary>2.3.1, confirm only: empty folders from the preview (their OriginalPath) to send to the Recycle Bin after organizing.</summary>
    public List<string> RecycleEmpty { get; set; } = new();
}
public sealed class LayoutQueueGroup { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string ParentId { get; set; } = ""; }
public sealed class LayoutQueueMod { public string Guid { get; set; } = ""; public string GroupId { get; set; } = ""; public string Name { get; set; } = ""; }
/// <summary>2.3.0: one overview row as the user sees it — every plugin GUID it carries, its group, its package folder.</summary>
public sealed class LayoutQueueRow { public List<string> Guids { get; set; } = new(); public string GroupId { get; set; } = ""; public string PackageRoot { get; set; } = ""; public string Version { get; set; } = ""; public bool Loaded { get; set; } }
