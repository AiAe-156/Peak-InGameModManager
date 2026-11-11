using System.Reflection;
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;

string root = Path.Combine(AppContext.BaseDirectory, "fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
BepInEx.Paths.BepInExRootPath = root;
BepInEx.Paths.PluginPath = Path.Combine(root, "plugins");
string plugins = BepInEx.Paths.PluginPath;
Directory.CreateDirectory(plugins);
string package = Path.Combine(plugins, "Team-Package"); Directory.CreateDirectory(package);
string dll = Path.Combine(package, "test.dll"); File.WriteAllText(dll, "dll");
int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
try
{
    var inv = new OverviewScanInventory(plugins, PluginFileNames.IsAssembly);
    Check(inv.Assemblies.SequenceEqual(new[] { dll }), "production traversal discovers assembly");
    Check(inv.IsCurrent(), "unchanged inventory validates");
    File.AppendAllText(dll, "new"); Check(!inv.IsCurrent(), "DLL size change invalidates");
    inv = new OverviewScanInventory(plugins, PluginFileNames.IsAssembly);
    File.SetLastWriteTimeUtc(dll, DateTime.UtcNow.AddSeconds(3)); Check(!inv.IsCurrent(), "same-size DLL timestamp change invalidates");
    inv = new OverviewScanInventory(plugins, PluginFileNames.IsAssembly);
    string disabled = dll + ".disabled-by-mce"; File.Move(dll, disabled);
    Check(!inv.IsCurrent(), "enablement rename invalidates");
    inv = new OverviewScanInventory(plugins, PluginFileNames.IsAssembly);
    string child = Path.Combine(package, "new"); Directory.CreateDirectory(child);
    Check(!inv.IsCurrent(), "nested directory addition invalidates");
    inv = new OverviewScanInventory(plugins, PluginFileNames.IsAssembly);
    string manifest = Path.Combine(package, "manifest.json"); inv.WatchFile(manifest);
    File.WriteAllText(manifest, "{}"); Check(!inv.IsCurrent(), "previously absent manifest invalidates");
    inv = new OverviewScanInventory(plugins, PluginFileNames.IsAssembly); inv.WatchFile(manifest);
    File.AppendAllText(manifest, " "); Check(!inv.IsCurrent(), "manifest content stamp invalidates");
    inv = new OverviewScanInventory(plugins, PluginFileNames.IsAssembly); inv.WatchFile(manifest);
    File.Delete(manifest); Check(!inv.IsCurrent(), "manifest removal invalidates");
    string backup = Path.Combine(plugins, "backup"); Directory.CreateDirectory(backup); File.WriteAllText(Path.Combine(backup, "ignored.dll"), "");
    inv = new OverviewScanInventory(plugins, PluginFileNames.IsAssembly);
    Check(inv.Assemblies.Count == 1 && inv.Assemblies[0] == disabled, "backup exclusion and disabled suffix retained");
    Check(PluginFileNames.IsAssembly("x.__mce_2.0.dll") && !PluginFileNames.IsAssembly("x.dll.mce-candidate"), "candidate filename rules retained");
    string missing = Path.Combine(root, "missing"); inv = new OverviewScanInventory(missing, PluginFileNames.IsAssembly);
    Check(inv.IsCurrent(), "missing root represented"); Directory.CreateDirectory(missing);
    Check(!inv.IsCurrent(), "new plugin root invalidates empty scan");

    OverviewCatalogue.Reset();
    object first = OverviewCatalogue.Discover(); int reads = OverviewCatalogue.Reads;
    Check(ReferenceEquals(first, OverviewCatalogue.Discover()) && OverviewCatalogue.Reads == reads, "production evidence cache reuses physical discovery");
    Check(OverviewCatalogue.FirstName() == "original", "immutable evidence has original metadata");
    OverviewCatalogue.MutateRecord();
    Check(OverviewCatalogue.FirstName() == "original" && OverviewCatalogue.FirstDependency() == "dependency", "metadata records and dependencies are copied into immutable evidence");
    OverviewCatalogue.Invalidate(); Check(!ReferenceEquals(first, OverviewCatalogue.Discover()), "generation invalidation rejects old evidence");
    object beforeManifest = OverviewCatalogue.Discover();
    File.WriteAllText(Path.Combine(root, "manifest.json"), "{}");
    Check(!ReferenceEquals(beforeManifest, OverviewCatalogue.Discover()), "ancestor manifest appearance rejects physical cache");
    object beforeNewDll = OverviewCatalogue.Discover(); File.WriteAllText(Path.Combine(child, "second.dll.disabled"), "");
    Check(!ReferenceEquals(beforeNewDll, OverviewCatalogue.Discover()), "new nested disabled DLL rejects cache");
    object beforeEnvironment = OverviewCatalogue.Discover();
    BepInEx.Paths.PluginPath = missing;
    Check(!ReferenceEquals(beforeEnvironment, OverviewCatalogue.Discover()), "plugin root environment is part of evidence identity");
    object beforeBep = OverviewCatalogue.Discover(); BepInEx.Paths.BepInExRootPath = missing;
    Check(!ReferenceEquals(beforeBep, OverviewCatalogue.Discover()), "BepInEx root environment is part of evidence identity");
    BepInEx.Paths.PluginPath = plugins; BepInEx.Paths.BepInExRootPath = root;
    OverviewCatalogue.Reset(); OverviewCatalogue.InvalidateDuringRead = true; OverviewCatalogue.Discover();
    Check(!OverviewCatalogue.HasPublished, "in-flight generation invalidation cannot publish old evidence");
    OverviewCatalogue.Discover(); Check(OverviewCatalogue.HasPublished, "next stable scan publishes again");
    // Schema guard: evidence must never start retaining mutable projection state.
    var evidenceTypes = typeof(OverviewCatalogue).GetNestedTypes(BindingFlags.NonPublic).Where(t => t.Name.EndsWith("Evidence"));
    Check(evidenceTypes.SelectMany(t => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).All(f => !f.FieldType.Name.Contains("OverviewMod") && !f.FieldType.Name.Contains("Repository")), "evidence schema excludes mutable rows, rules and repositories");
    Console.WriteLine($"RESULT {checks} production evidence checks passed (filesystem fixtures only)");
}
finally { Directory.Delete(root, true); }

namespace BepInEx { static class Paths { internal static string PluginPath = "", BepInExRootPath = ""; } }
namespace Newtonsoft.Json { enum Formatting { None } }
namespace ModConfigEnhance.Overview
{
    class OverviewDependency { internal string Id = "", MinimumVersion = ""; internal bool Optional = false; }
    internal sealed partial class OverviewCatalogue
    {
        private sealed class PluginRecord
        { internal string Guid = "guid", Name = "original", Version = "1"; internal readonly List<OverviewDependency> Dependencies = new() { new() { Id = "dependency" } }; }
        private static int _cacheGeneration;
        internal static int Reads;
        internal static bool InvalidateDuringRead;
        private static PluginRecord _record = new();
        internal static void MutateRecord() { _record.Name = "mutated"; _record.Dependencies[0].Id = "mutated"; }
        private static List<PluginRecord> AssemblyRecords(string path)
        {
            Reads++;
            if (InvalidateDuringRead) { InvalidateDuringRead = false; Invalidate(); }
            return new() { _record };
        }
        private static string? FindManifest(string path) => null;
        private static FakeJson? ManifestData(string path) => null;
        private sealed class FakeJson { internal string ToString(Newtonsoft.Json.Formatting formatting) => "{}"; }
        internal static object Discover() => DiscoverPhysicalEvidence();
        internal static string FirstName() => DiscoverPhysicalEvidence()[0].Records[0].Name;
        internal static string FirstDependency() => DiscoverPhysicalEvidence()[0].Records[0].Dependencies[0].Id;
        internal static void Invalidate() { System.Threading.Interlocked.Increment(ref _cacheGeneration); InvalidateScanEvidence(); }
        internal static bool HasPublished => _scanEvidence != null;
        internal static void Reset() { Invalidate(); Reads = 0; _record = new(); }
    }
}
