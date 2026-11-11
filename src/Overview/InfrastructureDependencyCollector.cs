#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>Read-only background adapter. Confirmed identities come from the captured SourceKey/AssemblyPaths,
/// never from a similarly named directory. No live configuration, ledger or loaded assembly access.</summary>
internal static class InfrastructureDependencyCollector
{
    internal static ComponentDependencyGraph Collect(string loaderRoot, IEnumerable<ComponentDependencyCollector.ConfirmedFile> confirmed)
    {
        string root = Path.GetFullPath(loaderRoot).TrimEnd('\\', '/');
        var captured = confirmed.Select(c => new ComponentDependencyCollector.ConfirmedFile {
            PackageKey = c.PackageKey, Path = c.Path, Hash = c.Hash }).ToArray();
        // Associate each confirmed payload with its nearest manifest, not every manifest above it.
        // Child packages must not contribute their identities to a container/parent package.
        string? Boundary(string file)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(root, file)));
            while (directory != null && directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked confirmed package directory: " + directory);
                string manifest = Path.Combine(directory, "manifest.json");
                if (File.Exists(manifest)) return manifest;
                directory = Path.GetDirectoryName(directory);
            }
            return null;
        }
        // Boundary failures are kept local and reported by the collector's normal scoped scan.
        var boundaries = captured.Select(c => {
            try { return (Claim: c, Manifest: Boundary(c.Path), Error: ""); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            { return (Claim: c, Manifest: (string?)null, Error: e.Message); }
        }).ToArray();
        var graph = ComponentDependencyCollector.Collect(root, file =>
        {
            var data = JObject.Parse(File.ReadAllText(file));
            string name = data.Value<string>("name") ?? "";
            string team = data.Value<string>("namespace") ?? data.Value<string>("author") ?? "";
            string version = data.Value<string>("version_number") ?? "";
            var hints = boundaries.Where(c => string.Equals(c.Manifest, file, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Claim.PackageKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (hints.Length > 1) throw new IOException("Multiple confirmed packages at manifest boundary: " + file);
            if (team.Length == 0 && hints.Length == 1 && ShopInstalled.Key(hints[0])
                && string.Equals(hints[0].Split('-')[1], name, StringComparison.OrdinalIgnoreCase))
                team = hints[0].Split('-')[0];
            string key = team + "-" + name;
            if (!ShopInstalled.Key(key) || !Version.TryParse(version, out _))
                throw new IOException("Unconfirmed component manifest identity/version: " + file);
            if (hints.Length == 1 && !string.Equals(key, hints[0], StringComparison.OrdinalIgnoreCase))
                throw new IOException("Manifest contradicts confirmed package: " + file);
            string full = data.Value<string>("FullName") ?? "";
            if (full.Length > 0 && !string.Equals(full, key, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Manifest FullName contradicts package: " + file);
            return new ComponentDependencyCollector.Manifest { Key = key,
                Dependencies = (data["dependencies"] as JArray)?.Values<string>().Select(d => d ?? "").ToArray() ?? Array.Empty<string>() };
        }, captured, BepInEx.Paths.GameRootPath);
        foreach (var failure in boundaries.Where(b => b.Error.Length > 0))
            graph.AddFailure("Confirmed package boundary: " + failure.Error, failure.Claim.PackageKey, failure.Claim.Path);
        return graph;
    }
}
