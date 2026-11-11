// 2.4.4 read-only probe: run ManifestCopies over the real BepInEx core/patchers/plugins, print every package with more than one
// manifest.json and the version that now wins. Writes nothing.
using ModConfigEnhance.Updating;
string root = args.Length > 0 ? args[0] : @"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx";
var copies = new List<ManifestCopies.Copy>();
foreach (var area in new[] { "core", "patchers", "plugins" })
    foreach (var file in Directory.EnumerateFiles(Path.Combine(root, area), "manifest.json", SearchOption.AllDirectories))
    { var c = ManifestCopies.Read(file, File.ReadAllText); if (c != null) copies.Add(c); }
int multi = 0, stale = 0;
foreach (var v in ManifestCopies.Resolve(copies).OrderBy(v => v.Key))
{
    int n = copies.Count(c => c.Key.Equals(v.Key, StringComparison.OrdinalIgnoreCase)); if (n < 2) continue;
    multi++; if (v.HasStale) stale++;
    Console.WriteLine($"{v.Key}: 取 {v.Version}（{Path.GetRelativePath(root, v.Authoritative)}）" + (v.HasStale ? " 残留：" + string.Join("；", v.Stale.Select(s => s.Version + " " + Path.GetRelativePath(root, s.ManifestPath))) : " 无残留"));
}
Console.WriteLine($"包 {copies.Select(c => c.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()}，多份说明文件 {multi}，有残留 {stale}");
