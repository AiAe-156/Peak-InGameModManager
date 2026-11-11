// Read-only. Real PluginFileNames (disabled-name rule) and OverviewScanInventory (assembly + manifest walk) against the
// live plugins/ folder, then the resource-pack selection as written in OverviewCatalogue.AddResourcePacks.
using ModConfigEnhance.Updating;
using ModConfigEnhance.Overview;
int passed = 0; void Check(bool c, string n) { if (!c) throw new Exception("FAIL " + n); passed++; Console.WriteLine("ok  " + n); }
foreach (var (name, want) in new[] { ("a.dll", false), ("a.dll.d", true), ("a.dll.disabled-by-mce", true), ("ModConfigEnhance.dll1", true), ("a.dllx", true),
    ("a.dll_off", true), ("a.dll.config", false), ("a.dll.mdb", false), ("a.dll.pdb", false), ("a.dll.xml", false), ("readme.txt", false), ("my.dlltools.dll.d", true) })
    Check(PluginFileNames.IsDisabled(name) == want && (want ? PluginFileNames.IsAssembly(name) : !name.EndsWith(".dll") ? !PluginFileNames.IsAssembly(name) : true), "disabled rule: " + name);
Check(PluginFileNames.OriginalPath("ModConfigEnhance.dll1") == "ModConfigEnhance.dll", "x.dll1 restores to x.dll");
Check(PluginFileNames.OriginalPath("my.dlltools.dll.d") == "my.dlltools.dll", "last .dll wins");
string plugins = args[0];
var inv = new OverviewScanInventory(plugins, PluginFileNames.IsAssembly);
Console.WriteLine($"assemblies={inv.Assemblies.Count} manifests={inv.Manifests.Count}");
Check(inv.Assemblies.Any(a => a.EndsWith("ModConfigEnhance.dll1")), "ModConfigEnhance.dll1 is now an assembly");
// Package roots of assembly rows: nearest manifest folder above each assembly, else the assembly's folder.
string Root(string a) { var d = Path.GetDirectoryName(a)!; for (var p = d; p != null && p.Length > plugins.Length; p = Path.GetDirectoryName(p)) if (File.Exists(Path.Combine(p, "manifest.json")) || File.Exists(Path.Combine(p, "manifest.json.old"))) return p; return d; }
var owned = new HashSet<string>(inv.Assemblies.Select(Root), StringComparer.OrdinalIgnoreCase);
var assemblyDirs = inv.Assemblies.Select(a => Path.GetDirectoryName(a)!).ToList();
var packs = new List<string>();
foreach (string manifest in inv.Manifests.OrderBy(m => m.Length))
{
    string folder = Path.GetDirectoryName(manifest)!;
    if (owned.Contains(folder)) continue;
    string? parent = Path.GetDirectoryName(folder);
    if (Path.GetFileName(folder).Equals("plugins", StringComparison.OrdinalIgnoreCase) && parent != null && owned.Contains(parent)) continue;
    if (owned.Any(o => o.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
    if (assemblyDirs.Any(d => d.Equals(folder, StringComparison.OrdinalIgnoreCase) || d.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
    packs.Add(folder.Substring(plugins.Length + 1)); owned.Add(folder);
}
Console.WriteLine("resource-pack candidates (before the component filter):"); foreach (var p in packs) Console.WriteLine("   " + p);
Check(packs.Count(p => p.Contains("AeralisFoundation-")) == 2, "both Aeralis resource packs found");
Check(!packs.Any(p => p.Contains("Profile_Cards")), "Profile_Cards packaging copy is not a resource pack");
Check(packs.Any(p => p == "MonoDetour-MonoDetour"), "MonoDetour plugins half is a candidate (dropped later as a confirmed component)");
Console.WriteLine($"shape probe passed: {passed}");
