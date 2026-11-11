using System.Text.Json;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal sealed record InstalledPackage(string Name, string Version, string Root, string Manifest, List<string> Dependencies);

/// <summary>What is installed, read from manifests only: folder names are the user's business and never part of identity.</summary>
internal static class InstallInventory
{
    static readonly string[] Skipped = { "备份", "验证", "backup", "backups", "Demo" };

    internal static List<InstalledPackage> Scan(string root)
    {
        var result = new List<InstalledPackage>();
        foreach (string area in new[] { "plugins", "patchers" })
        {
            string home = Path.Combine(root, area);
            foreach (string file in InstallArchive.Walk(home))
            {
                string leaf = Path.GetFileName(file);
                if (!leaf.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) && !leaf.Equals("manifest.json.old", StringComparison.OrdinalIgnoreCase)) continue;
                string directory = Path.GetDirectoryName(file)!;
                if (Path.GetRelativePath(home, directory).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => Skipped.Contains(part, Files.Compare))) continue;
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file));
                    var value = document.RootElement;
                    string name = value.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    string version = value.TryGetProperty("version_number", out var v) ? v.GetString() ?? "" : "";
                    if (name.Length == 0) continue;
                    var dependencies = value.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Array ? d.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : new List<string>();
                    result.Add(new InstalledPackage(name, version, directory, file, dependencies));
                }
                catch (JsonException) { }
                catch (IOException) { }
            }
        }
        return result;
    }

    /// <summary>Resolves a Thunderstore key (namespace-name) against the inventory by package name.</summary>
    internal static InstalledPackage? Find(string key, IReadOnlyList<InstalledPackage> inventory)
    {
        int split = key.IndexOf('-');
        string name = split > 0 ? key.Substring(split + 1) : key;
        var candidates = inventory.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];
        var exact = candidates.FirstOrDefault(p => Path.GetFileName(p.Root).Equals(key, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        var folder = candidates.FirstOrDefault(p => Path.GetFileName(p.Root).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        if (folder != null) return folder;
        return candidates.OrderByDescending(p => ParseVersion(p.Version) ?? new Version(0, 0)).First();
    }

    /// <summary>A package counts as enabled when every plugin assembly it ships is the live .dll, not a disabled copy.</summary>
    internal static bool IsEnabled(string root, InstalledPackage package)
    {
        var assemblies = InstallArchive.Walk(package.Root).Where(PluginFileNames.IsAssembly).ToList();
        var plugins = assemblies.Where(p => InstallMetadata.Read(root, p).Count != 0).ToList();
        if (plugins.Count > 0) return plugins.All(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        return !assemblies.Any(p => !p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    }

    internal static Version? ParseVersion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string value = text.Trim();
        if (value.StartsWith("v", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
        int cut = value.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut > 0) value = value.Substring(0, cut);
        string[] parts = value.Split('.');
        if (parts.Length == 1) value += ".0.0"; else if (parts.Length == 2) value += ".0";
        return Version.TryParse(value, out var version) ? version : null;
    }

    internal static int CompareVersions(string a, string b)
    {
        var left = ParseVersion(a); var right = ParseVersion(b);
        if (left == null || right == null) return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        return left.CompareTo(right);
    }
}
