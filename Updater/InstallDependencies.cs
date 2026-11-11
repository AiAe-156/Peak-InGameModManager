using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal sealed record DependencyRequirement(string Key, string Namespace, string Package, string Minimum);

/// <summary>Thunderstore dependency strings against what is actually installed. Missing dependencies become jobs, not failures.</summary>
internal static class InstallDependencies
{
    internal static List<DependencyRequirement> Parse(IEnumerable<string> dependencies, List<string>? warnings)
    {
        var result = new List<DependencyRequirement>();
        foreach (string text in dependencies)
        {
            int at = text.LastIndexOf('-');
            string key = at > 0 ? text.Substring(0, at) : "", version = at > 0 ? text.Substring(at + 1) : "";
            int split = key.IndexOf('-');
            if (at < 1 || split < 1 || InstallInventory.ParseVersion(version) == null) { warnings?.Add("依赖声明格式无法识别（已忽略）：" + text); continue; }
            if (PackageRules.IsLoader(key) || ManagerIdentity.IsManagerDependency(key)) continue;
            if (result.Any(r => Files.Compare.Equals(r.Key, key))) continue;
            result.Add(new DependencyRequirement(key, key.Substring(0, split), key.Substring(split + 1), version));
        }
        return result;
    }

    internal static InstalledPackage? Satisfied(DependencyRequirement requirement, IReadOnlyList<InstalledPackage> inventory)
    {
        var installed = InstallInventory.Find(requirement.Key, inventory);
        if (installed == null) return null;
        return InstallInventory.CompareVersions(installed.Version, requirement.Minimum) >= 0 ? installed : null;
    }

    internal static UpdateJob JobFor(string root, DependencyRequirement requirement, InstalledPackage? installed, UpdateJob parent)
    {
        bool fresh = installed == null;
        return new UpdateJob
        {
            Id = Guid.NewGuid().ToString("N"), Namespace = requirement.Namespace, Package = requirement.Package, DisplayName = requirement.Package,
            PackageRoot = installed?.Root ?? Path.Combine(root, "plugins", requirement.Key),
            InstalledVersion = installed?.Version ?? "0.0.0", TargetVersion = "", Install = fresh, AutoDependency = true, RequestedBy = parent.Id,
            Enabled = true, SourceProcessId = parent.SourceProcessId, State = UpdateStates.Queued, CreatedUtc = DateTime.UtcNow.ToString("O"), UpdatedUtc = DateTime.UtcNow.ToString("O")
        };
    }
}
