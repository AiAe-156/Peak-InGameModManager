#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using BepInEx;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewCatalogue
{
    private sealed class DependencyEvidence
    {
        internal string Id { get; }
        internal string MinimumVersion { get; }
        internal bool Optional { get; }
        internal DependencyEvidence(OverviewDependency value)
        { Id = value.Id; MinimumVersion = value.MinimumVersion; Optional = value.Optional; }
    }
    private sealed class PluginEvidence
    {
        internal string Guid { get; }
        internal string Name { get; }
        internal string Version { get; }
        internal ReadOnlyCollection<DependencyEvidence> Dependencies { get; }
        internal PluginEvidence(PluginRecord value)
        {
            Guid = value.Guid; Name = value.Name; Version = value.Version;
            Dependencies = value.Dependencies.Select(d => new DependencyEvidence(d)).ToList().AsReadOnly();
        }
    }
    private sealed class PhysicalEvidence
    {
        internal string Path { get; }
        internal string? ManifestPath { get; }
        // A string, not a shared mutable JObject or OverviewMod.
        internal string? ManifestJson { get; }
        internal ReadOnlyCollection<PluginEvidence> Records { get; }
        internal PhysicalEvidence(string path)
        {
            Path = path; ManifestPath = FindManifest(path);
            ManifestJson = ManifestPath == null ? null : ManifestData(ManifestPath)?.ToString(Formatting.None);
            Records = AssemblyRecords(path).Select(p => new PluginEvidence(p)).ToList().AsReadOnly();
        }
    }
    private sealed class ScanEvidence
    {
        internal readonly int Generation;
        internal readonly string Plugins, BepInEx;
        internal readonly OverviewScanInventory Inventory;
        internal readonly ReadOnlyCollection<PhysicalEvidence> Files;
        internal ScanEvidence(int generation, string plugins, string bepinex)
        {
            Generation = generation; Plugins = plugins; BepInEx = bepinex;
            Inventory = new OverviewScanInventory(plugins, PluginFileNames.IsAssembly);
            // Watch all possible manifest locations before reading them. This includes missing
            // files, .old fallbacks and manifests above PluginPath but inside BepInExRootPath.
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string prefix = bepinex.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            foreach (string path in Inventory.Assemblies)
            {
                string? directory = System.IO.Path.GetDirectoryName(path);
                while (directory != null && (directory + System.IO.Path.DirectorySeparatorChar).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (!directories.Add(directory)) break;
                    Inventory.WatchFile(System.IO.Path.Combine(directory, "manifest.json"));
                    Inventory.WatchFile(System.IO.Path.Combine(directory, "manifest.json.old"));
                    directory = System.IO.Path.GetDirectoryName(directory);
                }
            }
            Files = Inventory.Assemblies.Select(p => new PhysicalEvidence(p)).ToList().AsReadOnly();
        }
        internal bool Matches(int generation, string plugins, string bepinex) => Generation == generation &&
            Plugins.Equals(plugins, StringComparison.OrdinalIgnoreCase) && BepInEx.Equals(bepinex, StringComparison.OrdinalIgnoreCase) && Inventory.IsCurrent();
    }
    private static ScanEvidence? _scanEvidence;
    private static void InvalidateScanEvidence() => Volatile.Write(ref _scanEvidence, null);

    private static ReadOnlyCollection<PhysicalEvidence> DiscoverPhysicalEvidence() => DiscoverScan().Files;
    /// <summary>The whole scan (assemblies and, since 2.3.4, manifests). Returned directly so a caller never depends on
    /// the cache field, which is only published when nothing changed during the walk.</summary>
    private static ScanEvidence DiscoverScan()
    {
        int generation = Volatile.Read(ref _cacheGeneration);
        string plugins = Path.GetFullPath(Paths.PluginPath), bepinex = Path.GetFullPath(Paths.BepInExRootPath);
        var cached = Volatile.Read(ref _scanEvidence);
        if (cached != null && cached.Matches(generation, plugins, bepinex)) return cached;
        var discovered = new ScanEvidence(generation, plugins, bepinex);
        // Never publish a partial traversal or a result invalidated while IO was in flight.
        if (generation == Volatile.Read(ref _cacheGeneration) && discovered.Matches(generation, plugins, bepinex))
            Volatile.Write(ref _scanEvidence, discovered);
        return discovered;
    }
}
