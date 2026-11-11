#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Mods nested in another mod's folder (2.3.0). A row is nested when its package folder (the folder with its
/// own manifest.json) lies inside another row's package folder. A package's own layout does not count: the inner
/// folder sitting directly in plugins/, BepInEx/ or patchers/ of the outer one, or carrying the same manifest name.
/// The outer is the nearest such row. Pure path logic over the rows' PackageRoot, so it is checked without Unity.</summary>
internal static class NestedMods
{
    private static readonly string[] PackagingFolders = { "plugins", "BepInEx", "patchers" };

    internal static string Normalize(string path) => (path ?? "").Replace('\\', '/').TrimEnd('/');

    private static bool Under(string inner, string outer) => inner.Length > outer.Length && inner.StartsWith(outer + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="inner"/> is the outer package's own packaging rather than a separate mod.</summary>
    internal static bool IsPackaging(string inner, string outer, Func<string, string?>? manifestName = null)
    {
        string relative = inner.Substring(outer.Length + 1);
        string first = relative.Split('/')[0];
        if (PackagingFolders.Any(f => f.Equals(first, StringComparison.OrdinalIgnoreCase))) return true;
        if (manifestName != null)
        {
            string? a = manifestName(inner), b = manifestName(outer);
            if (!string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>For each package root, the nearest outer package root it is nested in ("" when top level).</summary>
    internal static Dictionary<string, string> OuterOf(IEnumerable<string> packageRoots, Func<string, string?>? manifestName = null)
    {
        var roots = packageRoots.Select(Normalize).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string inner in roots)
        {
            string best = "";
            foreach (string outer in roots)
                if (Under(inner, outer) && !IsPackaging(inner, outer, manifestName) && outer.Length > best.Length) best = outer;
            result[inner] = best;
        }
        return result;
    }

    /// <summary>Reads the "name" of a folder's manifest.json; null when absent or unreadable.</summary>
    internal static string? ManifestName(string folder)
    {
        try
        {
            string file = Path.Combine(folder, "manifest.json");
            if (!File.Exists(file)) file += ".old";   // disabled by MCE
            if (!File.Exists(file)) return null;
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
            return json.Value<string>("name");
        }
        catch (Exception) { return null; }
    }
}
