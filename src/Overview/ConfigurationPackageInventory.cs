#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace ModConfigEnhance.Overview;
// Shared read-only package evidence. No installs, source guesses or file ownership grants.
internal static class ConfigurationPackageInventory
{
    private static string _key = "";
    private static ShopInstalled.Snapshot? _snapshot;
    internal static ShopInstalled.Snapshot Get(IReadOnlyList<OverviewMod> mods)
    {
        string context = PackLibrary.Managed ? Path.Combine(PackLibrary.Workspace, "shop-installed.json")
            : ModConfigEnhance.Updating.ManagerIdentity.Runtime(BepInEx.Paths.BepInExRootPath, "shop", "installed-unmanaged.json");
        string key = context + "|" + OverviewPageCache.Inventory + "|" + ModSources.ChoiceRevision + "|" + string.Join(";", mods.Select(m =>
            m.Location + "|" + m.SourceConfirmed + "|" + m.SourceNamespace + "|" + m.SourceName + "|" + m.PackageVersion));
        if (_snapshot != null && key == _key) return _snapshot;
        // Only a plugin's own payload can testify to its source, not unrelated sibling DLLs.
        var confirmed = mods.Where(m => m.SourceConfirmed && m.IdentityConflict.Length == 0 && m.Location.Length > 0)
            .Select(m => new ShopInstalled.Evidence { Key = m.SourceNamespace + "-" + m.SourceName,
                Version = m.PackageVersion, Path = m.Location }).ToArray();
        _snapshot = ShopInstalled.Build(BepInEx.Paths.BepInExRootPath, context, context, confirmed);
        _key = key; return _snapshot;
    }
    internal static string Status(OverviewRepository repo, IReadOnlyList<OverviewMod> mods, ShopInstalled.Snapshot inventory, string key, Version minimum)
    {
        var rows = mods.Where(m => ConfigurationIssueReport.PackageMatches(m, key)).ToList();
        var evidence = inventory.Packages.TryGetValue(key, out var found) ? found : new List<ShopInstalled.Evidence>();
        bool Active(ShopInstalled.Evidence e)
        {
            if (e.Cached) return false;
            var owners = rows.Where(m => m.Location.Equals(e.Path, StringComparison.OrdinalIgnoreCase)
                || m.Location.StartsWith(e.Path.TrimEnd('\\','/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToList();
            return owners.Count > 0 ? owners.Any(repo.PlannedEnabled) : !e.Disabled;
        }
        var versions = rows.Where(repo.PlannedEnabled).Select(m => m.PackageVersion)
            .Concat(evidence.Where(Active).Select(e => e.Version)).ToList();
        if (versions.Any(v => Version.TryParse(v, out var actual) && actual >= minimum)) return "";
        if (versions.Count > 0) return versions.Any(v => !Version.TryParse(v, out _)) ? "unknown-version" : "outdated";
        if (rows.Count > 0 || evidence.Count > 0) return "disabled";
        int split = key.IndexOf('-'); string name = split < 0 ? key : key.Substring(split + 1);
        // An authorless manifest proves a local payload exists, not which store publisher owns it.
        var unresolved = inventory.Unresolved.Where(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (unresolved.Count > 0)
        {
            var candidates = mods.Where(m => m.PackageName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            bool Available(ShopInstalled.Evidence e) => !e.Cached && (candidates.Count > 0 ? candidates.Any(repo.PlannedEnabled) : !e.Disabled);
            var active = unresolved.Where(Available).ToList();
            if (active.Count == 0) return "disabled";
            if (active.Any(e => Version.TryParse(e.Version, out var actual) && actual >= minimum)) return "";
            return active.Any(e => !Version.TryParse(e.Version, out _)) ? "unknown-version" : "outdated";
        }
        return inventory.Failure.Length > 0 ? "unavailable" : "missing";
    }
}
