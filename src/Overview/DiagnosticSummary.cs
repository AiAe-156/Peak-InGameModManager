#nullable enable
using System;
using System.Linq;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>UI contract: bounded, localized status for hints; verbatim evidence only for the scrollable/copyable details viewer.
/// Does not grant backend permissions. Environment/ownership callers must supply their actual structured state.</summary>
internal sealed class DiagnosticSummary
{
    internal enum Status { Ready, NotRead, RequiredByMce, Incomplete, VerificationRequired, CompleteEnvironmentRequired, OwnershipUnverified }
    internal Status State { get; }
    internal bool CanUninstall { get; }
    internal bool CanUpdate { get; }
    private readonly string _details;
    private DiagnosticSummary(Status state, bool canUninstall, bool canUpdate, string details)
    { State = state; CanUninstall = canUninstall; CanUpdate = canUpdate; _details = details; }
    internal string Summary => State switch {
        Status.NotRead => Loc.T("Dependency information has not been read", "依赖信息尚未读取", "依賴資訊尚未讀取"),
        Status.RequiredByMce => Loc.T("MCE runs on this component; it cannot be disabled or uninstalled", "MCE 运行依赖此组件，不能停用或卸载", "MCE 執行依賴此組件，不能停用或解除安裝"),
        Status.Incomplete => Loc.T("Dependency information is incomplete; uninstall is not yet available", "依赖信息不完整，暂不能卸载", "依賴資訊不完整，暫不能解除安裝"),
        Status.VerificationRequired => Loc.T("Uninstall preview is available; file ownership and dependencies still need verification", "可进入卸载预览，仍需核实文件归属和依赖", "可進入解除安裝預覽，仍需核實檔案歸屬與依賴"),
        Status.CompleteEnvironmentRequired => Loc.T("Migrate the current pack to a complete environment first", "当前整合包尚未迁移为完整环境", "目前整合包尚未遷移為完整環境"),
        Status.OwnershipUnverified => Loc.T("File ownership has not been verified", "文件归属尚未核实", "檔案歸屬尚未核實"),
        _ => ""
    };
    internal string BlockReason => CanUninstall ? "" : Summary;
    internal static string DetailsLabel => Loc.T("View inspection details", "查看检查详情", "查看檢查詳情");
    internal string Details => Summary + (_details.Length > 0 ? "\n\n" + _details : "");
    internal static DiagnosticSummary FromCapability(ComponentDependencyGraph.Capability? capability)
    {
        if (capability == null) return new DiagnosticSummary(Status.NotRead, false, true, "");
        var state = capability.ProtectionPath.Count > 0 ? Status.RequiredByMce
            : !capability.CanUninstall ? Status.Incomplete
            : capability.VerificationRequired || capability.Unknown ? Status.VerificationRequired : Status.Ready;
        // 2.3.9 (user decision): the protected whitelist is a hard stop for uninstall, as it is for enable/disable.
        return new DiagnosticSummary(state, capability.CanUninstall && capability.ProtectionPath.Count == 0, capability.CanUpdate, capability.Reason
            + (capability.ProtectionPath.Count > 0 ? "\n\n" + string.Join(" → ", capability.ProtectionPath) : ""));
    }
    internal static DiagnosticSummary ForPackage(ComponentDependencyGraph? graph, string key)
    {
        var summary = FromCapability(graph?.GetCapability(key));
        if (graph == null) return summary;
        // Do not spread unrelated scoped failures through every package's details (or any tooltip).
        var ids = graph.Components.Where(c => string.Equals(c.Id, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.PackageKey, key, StringComparison.OrdinalIgnoreCase)).Select(c => c.Id)
            .Concat(graph.GetCapability(key).ProtectionPath).ToArray();
        var scopes = graph.FailureScopes.Where(f => f.Node.Length == 0 || ids.Contains(f.Node, StringComparer.OrdinalIgnoreCase))
            .Select(f => f.Node + "\n" + f.Path + "\n" + f.Reason).ToArray();
        return scopes.Length == 0 ? summary : new DiagnosticSummary(summary.State, summary.CanUninstall, summary.CanUpdate,
            summary._details + "\n\n" + string.Join("\n\n", scopes));
    }
    internal static DiagnosticSummary EnvironmentRequired(string details)
        => new DiagnosticSummary(Status.CompleteEnvironmentRequired, false, true, details);
    internal static DiagnosticSummary OwnershipRequired(string details)
        => new DiagnosticSummary(Status.OwnershipUnverified, false, true, details);
}
