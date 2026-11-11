#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Presentation only: no plugin GUID, profile rule, enablement, or publication target.</summary>
internal sealed class InfrastructureOverviewModel
{
    internal sealed class InstallationLocation
    {
        internal string Area { get; }
        internal string Path { get; }
        internal bool IdentityConfirmed { get; }
        internal bool ManifestVerified { get; }
        internal InstallationLocation(InfrastructurePackages.Package package)
        { Area=package.Area;Path=package.Path;IdentityConfirmed=package.IdentityConfirmed;ManifestVerified=package.ManifestVerified; }
    }
    internal string Key { get; }
    internal string Name { get; }
    internal string Area { get; }
    internal string Author { get; }
    internal string Description { get; }
    internal static string GroupTitle => Loc.T("Core/Patcher", "基础组件(Core/Patcher)", "基礎組件(Core/Patcher)");
    internal string TypeTitle => Areas.Count>1 ? Loc.T("Core + Patcher (Core/Patcher)", "核心组件 + 启动补丁(Core/Patcher)", "核心組件 + 啟動補丁(Core/Patcher)")
        : Area=="core" ? Loc.T("Core", "核心组件(Core)", "核心組件(Core)") : Loc.T("Patcher", "启动补丁(Patcher)", "啟動補丁(Patcher)");
    internal string Version { get; }
    internal string Path { get; }
    internal IReadOnlyList<string> Dependencies { get; }
    internal IReadOnlyList<InstallationLocation> Locations { get; }
    internal IReadOnlyList<string> Areas { get; }
    internal int LocationCount => Locations.Count;
    internal bool IdentityConfirmed { get; }
    /// <summary>2.3.4: every runtime file of this package across its locations; enablement renames exactly these.</summary>
    internal IReadOnlyList<string> RuntimeFiles { get; }
    /// <summary>2.4.4: lower-version manifest.json copies of this package that the scan ignored.</summary>
    internal IReadOnlyList<string> StaleManifests { get; }
    /// <summary>Enabled when any of its assemblies is a live .dll; a package whose assemblies are all .dll.* is disabled.</summary>
    internal bool Enabled => RuntimeFiles.Count == 0 || RuntimeFiles.Any(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    // Display identity is one package, with all of its area/path locations retained below it.
    internal string Identity => Key;
    internal string LatestVersion => VersionContradicted ? "" : RawLatestVersion;
    private string RawLatestVersion
    {
        get
        {
            var installed = Version.Split('/').Select(v => v.Trim()).Where(v => v.Length > 0).ToArray();
            return !IdentityConfirmed || installed.Length == 0 ? "" : InfrastructurePackages.Results
                .Where(p => p.Key.Equals(Key, StringComparison.OrdinalIgnoreCase)).Select(p => p.Latest)
                .Where(candidate => installed.All(current => ThunderstoreUpdateService.IsNewer(candidate, current)))
                .Aggregate("", (best, candidate) => best.Length == 0 || ThunderstoreUpdateService.IsNewer(candidate, best) ? candidate : best);
        }
    }
    /// <summary>2.4.4: the update to <see cref="LatestVersion"/> already finished but the scan still reads lower (stale manifest elsewhere).</summary>
    internal bool VersionContradicted
    {
        get
        {
            int split = Key.IndexOf('-'); if (!IdentityConfirmed || split < 1) return false;
            string completed = OverviewUpdateQueue.CompletedVersion(Key.Substring(0, split), Key.Substring(split + 1));
            string latest = RawLatestVersion;
            return latest.Length > 0 && Version.Split('/').Select(v => v.Trim()).Any(v => ModConfigEnhance.Updating.ManifestCopies.Contradicts(v, latest, completed));
        }
    }
    internal bool HasUpdate => LatestVersion.Length > 0;
    internal bool MatchesStatus(string tag) => tag switch {
        "$update" => HasUpdate,
        "$enabled" => Enabled && Locations.Any(p => !p.Area.Contains("cache")),
        "$disabled" => !Enabled || Locations.All(p => p.Area.Contains("cache")),
        "$issue" => !IdentityConfirmed,
        "$changed" => ResourceOperationDraft.Snapshot().Any(c => (c.Shop != null ? c.Shop.Namespace + "-" + c.Shop.Package : c.Job != null ? c.Job.Namespace + "-" + c.Job.Package : "").Equals(Key, StringComparison.OrdinalIgnoreCase)),
        OverviewTagSelection.Unknown => true,
        _ => false
    };
    private InfrastructureOverviewModel(IEnumerable<InfrastructurePackages.Package> packages)
    {
        var rows=packages.OrderBy(p=>p.Area,StringComparer.Ordinal).ThenBy(p=>p.Path,StringComparer.OrdinalIgnoreCase).ToArray();
        var package=rows.First();
        Key=package.Key; Name=package.Name; Area=package.Area; Version=package.Version;
        Author=package.Author; Description=package.Description;Path=package.Path;
        Version=string.Join(" / ",rows.Select(p=>p.Version).Where(v=>v.Length>0).Distinct(StringComparer.OrdinalIgnoreCase));
        Dependencies=Array.AsReadOnly(rows.SelectMany(p=>p.Dependencies).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        Locations=Array.AsReadOnly(rows.GroupBy(p=>p.Area+"|"+p.Path,StringComparer.OrdinalIgnoreCase)
            .Select(g=>new InstallationLocation(g.OrderByDescending(p=>p.ManifestVerified).First())).ToArray());
        Areas=Array.AsReadOnly(Locations.Select(p=>p.Area).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        IdentityConfirmed=rows.All(p=>p.IdentityConfirmed);
        RuntimeFiles=Array.AsReadOnly(rows.SelectMany(p=>p.RuntimeFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        StaleManifests=Array.AsReadOnly(rows.SelectMany(p=>p.StaleManifests).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
    internal static IReadOnlyList<InfrastructureOverviewModel> Create(IEnumerable<InfrastructurePackages.Package> packages)
        => packages.Where(p => (p.Area=="core" || p.Area=="patchers") && p.Path.Length>0
                && (p.IdentityConfirmed ? ShopInstalled.Key(p.Key) : p.Key.StartsWith("file:",StringComparison.Ordinal)))
            .GroupBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(g=>new InfrastructureOverviewModel(g))
            .OrderBy(p=>p.Area,StringComparer.Ordinal).ThenBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).ToArray();
    // The display's file association never grants backend file ownership or permission.
    // Advisory only: worker rebuilds the live graph before every uninstall boundary.
    internal ModConfigEnhance.Updating.ComponentDependencyGraph.Capability Capability(ModConfigEnhance.Updating.ComponentDependencyGraph graph)
        => IdentityConfirmed ? graph.GetCapability(Key) : new ModConfigEnhance.Updating.ComponentDependencyGraph.Capability {
            Unknown=true,CanUninstall=false,CanUpdate=false,Reason="Component package identity is unconfirmed." };
    internal IReadOnlyList<ModConfigEnhance.Updating.ComponentDependencyGraph.TreeEntry> Consumers(ModConfigEnhance.Updating.ComponentDependencyGraph graph)
        => graph.ReverseTree(Key);
    internal IReadOnlyList<ModConfigEnhance.Updating.ComponentDependencyGraph.TreeEntry> Consumers(ModConfigEnhance.Updating.ComponentDependencyGraph graph, int depth, int limit)
        => graph.ReverseTree(Key, depth, limit);
    // Never expose Capability.Reason or graph failures in ordinary tooltips.
    internal static string ProtectionHint(ModConfigEnhance.Updating.ComponentDependencyGraph.Capability? capability)
        => capability == null ? Loc.T("Reading dependency protection…", "正在读取依赖保护……", "正在讀取依賴保護……")
            : capability.Unknown ? Loc.T("Dependency evidence incomplete. Open source menu for details.", "依赖资料不完整，请在来源菜单查看详情。", "依賴資料不完整，請在來源選單查看詳情。")
            : !capability.CanUninstall ? Loc.T("Required management component; independent uninstall unavailable. See source menu.", "必要管理组件，不可单独卸载；详情见来源菜单。", "必要管理組件，不可單獨解除安裝；詳情見來源選單。")
            : Loc.T("Uninstall requires safety checks in the source menu.", "卸载须在来源菜单通过安全核查。", "解除安裝須在來源選單通過安全核查。");
    internal static string ShortText(string text)
    {
        string compact = text.Replace('\r', ' ').Replace('\n', ' ');
        return compact.Length <= 140 ? compact : compact.Substring(0, 140) + "…";
    }
    internal static string TreeCaption(ModConfigEnhance.Updating.ComponentDependencyGraph.TreeEntry entry)
        => entry.Id + (entry.Cycle ? " [cycle]" : "") + (entry.Shared ? " [shared]" : "")
            + (entry.Missing ? " [unknown]" : "") + (entry.Truncated ? " […]" : "") + " · " + entry.Evidence;
    internal bool Matches(string query) => MatchesText(query,Key,Name,string.Join(" ",Areas),Version,
        string.Join(" ",Locations.Select(p=>p.Path)),Author,Description,
        (Areas.Contains("core") ? "Core components 核心组件 核心組件 " : "")
            +(Areas.Contains("patchers") ? "Startup patchers 启动补丁 啟動補丁" : ""),string.Join(" ",Dependencies));
    internal static bool MatchesText(string query,params string[] values)
        => OverviewSearchText.Matches(query, values);
}
