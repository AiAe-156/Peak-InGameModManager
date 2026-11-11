using ModConfigEnhance.Updating;
namespace MCE.UpdateWorker;

internal sealed partial class ShopQueue
{
    // Deliberately rebuilt from live files and the current workspace ledger on every boundary.
    // Neither UI flags, preview graph, nor saved revisions are authority for uninstall permission.
    internal ComponentDependencyGraph ReadComponentGraph()
    {
        var claims = Ledger().Packages.SelectMany(p => p.Files.Select(f => new ComponentDependencyCollector.ConfirmedFile { PackageKey = p.Key, Path = f.Path, Hash = f.Hash })).ToArray();
        return ComponentDependencyCollector.Collect(storage.Root, file =>
        {
            ShopPackage package;
            try { package = ManifestPackage(file); }
            catch (IOException)
            {
                var data = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file));
                string name = data?["name"]?.GetValue<string>() ?? "";
                string directory = Path.GetDirectoryName(file)!;
                var keys = claims.Where(c => Files.Child(storage.Root, c.Path).StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && c.Hash.Length > 0 && File.Exists(Files.Child(storage.Root, c.Path)) && Files.Hash(Files.Child(storage.Root, c.Path)) == c.Hash)
                    .Select(c => c.PackageKey).Distinct(Files.Compare).ToArray();
                if (keys.Length != 1 || !keys[0].EndsWith("-" + name, StringComparison.OrdinalIgnoreCase)
                    || !Version.TryParse(data?["version_number"]?.GetValue<string>(), out _)
                    || (data?["namespace"]?.GetValue<string>() ?? "").Length > 0
                    || (data?["author"]?.GetValue<string>() ?? "").Length > 0) throw;
                package = new ShopPackage { Key = keys[0], Dependencies = (data?["dependencies"]?.AsArray() ?? new System.Text.Json.Nodes.JsonArray()).Select(d => d!.GetValue<string>()).ToList() };
            }
            return new ComponentDependencyCollector.Manifest { Key = package.Key, Dependencies = package.Dependencies.ToArray() };
        }, claims, Files.GameRootFor(storage.Root));
    }
    void RequireComponentUninstall(string key)
    {
        var capability = ReadComponentGraph().GetCapability(key);
        if (!capability.CanUninstall) throw new IOException(capability.Reason);
    }
    void RequireComponentUninstall(ShopTask task)
    {
        if (!(task.Uninstall || task.Request.Operation is "preview-uninstall" or "confirm-uninstall")) return;
        var graph = ReadComponentGraph();
        foreach (string key in task.Packages.Select(p => p.Key).Append(Key(task.Request)).Distinct(Files.Compare))
        {
            var capability = graph.GetCapability(key);
            if (!capability.CanUninstall) throw new IOException(capability.Reason);
        }
    }
}
