// Read-only: the real InfrastructurePackages/ShopInstalled scan against the live BepInEx, printing why each core/patchers
// folder ends up shown, merged, or dropped. Writes nothing.
using ModConfigEnhance.Overview;
string root = args[0];
var all = InfrastructurePackages.Scan(root);
Console.WriteLine("Scan rows: " + all.Count);
foreach (var p in all) Console.WriteLine($"  {p.Area,-8} key={p.Key,-40} v={p.Version,-10} confirmed={p.IdentityConfirmed} manifest={p.ManifestVerified} path={p.Path.Substring(root.Length + 1)}");
// Same filter as InfrastructureOverviewModel.Create
var shown = all.Where(p => (p.Area=="core" || p.Area=="patchers") && p.Path.Length>0 && (p.IdentityConfirmed ? ShopInstalled.Key(p.Key) : p.Key.StartsWith("file:",StringComparison.Ordinal)))
    .GroupBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
Console.WriteLine("Overview rows: " + shown.Count);
foreach (var m in shown) Console.WriteLine($"  {m.Area,-8} {m.Key}");
foreach (string area in new[] { "core", "patchers" })
    foreach (string dir in Directory.GetDirectories(Path.Combine(root, area)))
    {
        string manifest = Path.Combine(dir, "manifest.json");
        var dlls = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Where(ShopInstalled.Runtime).ToArray();
        var ev = File.Exists(manifest) ? ShopInstalled.Manifest(manifest, area) : null;
        Console.WriteLine($"  dir {area}/{Path.GetFileName(dir),-36} manifest={(File.Exists(manifest) ? (ev == null ? "INVALID" : ev.Key + " " + ev.Version) : "none")} dlls={dlls.Length}");
    }

namespace BepInEx {internal static class Paths {internal static string BepInExRootPath=>throw new Exception("Explicit root required");}}
namespace ModConfigEnhance.Overview {internal static class ThunderstoreUpdateService {internal static bool IsNewer(string a,string b)=>false;}}
namespace ModConfigEnhance {internal static class Loc {internal static string T(string en,string zh,string tw)=>en;}}
