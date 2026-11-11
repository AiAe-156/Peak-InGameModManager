#nullable enable
using System.Collections.Generic;
namespace ModConfigEnhance.Updating;
public enum ComponentAction { Install, Upgrade, Downgrade, Reinstall }
public static class ComponentActions
{
    public static ComponentAction Select(string installed, string target)
    {
        if (!System.Version.TryParse(target, out var next)) throw new System.ArgumentException("Invalid target version");
        if (string.IsNullOrEmpty(installed)) return ComponentAction.Install;
        if (!System.Version.TryParse(installed, out var current)) throw new System.ArgumentException("Installed version is ambiguous or unknown");
        int comparison = next.CompareTo(current);
        return comparison > 0 ? ComponentAction.Upgrade : comparison < 0 ? ComponentAction.Downgrade : ComponentAction.Reinstall;
    }
}
public sealed class ShopRequest
{
    public string OperationId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Package { get; set; } = "";
    public string Version { get; set; } = "";
    public string PreviewId { get; set; } = "";
    public string PlanToken { get; set; } = "";
    public int SourceProcessId { get; set; }
    public string GroupId { get; set; } = "";
    public string NewGroupName { get; set; } = "";
    public string NewGroupColor { get; set; } = "#7CC7E6";
    public string AppliedSnapshotPath { get; set; } = "";
    public string AppliedSnapshotHash { get; set; } = "";
    public bool RemoveConfiguration { get; set; }
    public bool IgnoreDependencyWarnings { get; set; }
    public string DependencyWarningToken { get; set; } = "";
}
public sealed class ShopPackage
{
    public string Key { get; set; } = "";
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public List<string> Dependencies { get; set; } = new();
    public List<PackFile> Files { get; set; } = new();
    public List<string> PluginGuids { get; set; } = new();
    public bool Reuse { get; set; }
    public bool External { get; set; }
    public string ClaimRoot { get; set; } = "";
    public string LayoutGroupId { get; set; } = "";
    public string LayoutGroupFolder { get; set; } = "";
    public string LayoutOuterFolder { get; set; } = "";
    public bool VerificationRequired { get; set; }
    public string ArchiveHash { get; set; } = "";
    public bool InstalledAsDependency { get; set; }
}
public sealed class ShopLedger
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public List<ShopPackage> Packages { get; set; } = new();
}
public sealed class ShopTask
{
    public string OperationId { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string State { get; set; } = "";
    public string Message { get; set; } = "";
    public string Error { get; set; } = "";
    public string CreatedUtc { get; set; } = "";
    public string PlanToken { get; set; } = "";
    public bool Uninstall { get; set; }
    public long IndexRevision { get; set; }
    public List<ShopPackage> VerificationPackages { get; set; } = new();
    public List<PackFile> ApprovedCoreFiles { get; set; } = new();
    public long LedgerRevision { get; set; }
    public string ActivePackId { get; set; } = "";
    public ShopRequest Request { get; set; } = new();
    public List<ShopPackage> Packages { get; set; } = new();
    public List<ShopPackage> PreviousPackages { get; set; } = new();
    public List<ShopPackage> CoreRequirements { get; set; } = new();
    public ComponentAction Action { get; set; }
    public List<string> Blockers { get; set; } = new();
    public List<string> DependencyWarnings { get; set; } = new();
    public List<string> Details { get; set; } = new();
    public List<PackFile> Desired { get; set; } = new();
    /// <summary>Structured folder-layout result of a layout preview; empty for every other operation.</summary>
    public List<LayoutTreeNode> Tree { get; set; } = new();
    public List<PackFile> Originals { get; set; } = new();
    public List<ShopPackage> OrphanCandidates { get; set; } = new();
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    /// <summary>The user cleared this finished folder/import task from the queue; the file and its tasks/ copy stay.</summary>
    public bool Dismissed { get; set; }
    public bool BlocksWrites => State is "requested" or "resolving" or "downloading" or "waiting-exit" or "committing" or "recovery-required";
}
