using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
int passed = 0, failed = 0;
void Check(string name, bool ok, string detail = "") { if (ok) passed++; else failed++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : " · " + detail)); }
int n = 0; string Id() => "g" + (n++);
List<FolderEntry> E(params (string Path, bool Dir, bool Manifest, bool Dll)[] items) => items.Select(i => new FolderEntry { Path = i.Path, IsDirectory = i.Dir, HasManifest = i.Manifest, IsAssembly = i.Dll }).ToList();

var sample = E(("plugins/1拓展", true, false, false),
    ("plugins/1拓展/A", true, true, false), ("plugins/1拓展/A/a.dll", false, false, true),
    ("plugins/1拓展/附属", true, false, false), ("plugins/1拓展/附属/B", true, true, false), ("plugins/1拓展/附属/B/b.dll", false, false, true),
    ("plugins/1拓展/附属/汉化", true, false, false), ("plugins/1拓展/附属/汉化/C", true, true, false), ("plugins/1拓展/附属/汉化/C/c.dll", false, false, true),
    ("plugins/1拓展/附属/汉化/更深", true, false, false), ("plugins/1拓展/附属/汉化/更深/D", true, true, false), ("plugins/1拓展/附属/汉化/更深/D/d.dll", false, false, true),
    ("plugins/TC", true, true, false), ("plugins/TC/tc.dll", false, false, true), ("plugins/TC/UB", true, true, false), ("plugins/TC/UB/ub.dll", false, false, true),
    ("plugins/空分类", true, false, false), ("plugins/空分类/空", true, false, false),
    ("plugins/Loose", true, true, false), ("plugins/Loose/l.dll", false, false, true));
var proposal = SmartGrouping.Propose(sample, Id);
var tree = new GroupTree(proposal.Groups);
string GroupOf(string package) => proposal.PackageGroup.TryGetValue(package, out var g) ? tree.Path(g) : "";
Check("分类文件夹 → 分组，按层级嵌套", tree.Path(proposal.Groups.First(g => g.name == "汉化").id) == "1拓展 › 附属 › 汉化");
Check("模组归入所在分类", GroupOf("plugins/1拓展/A") == "1拓展" && GroupOf("plugins/1拓展/附属/B") == "1拓展 › 附属");
Check("超过三层的并入第三层", GroupOf("plugins/1拓展/附属/汉化/更深/D") == "1拓展 › 附属 › 汉化" && !proposal.Groups.Any(g => g.name == "更深"), GroupOf("plugins/1拓展/附属/汉化/更深/D"));
Check("模组内嵌不建组", !proposal.Groups.Any(g => g.name == "TC" || g.name == "UB"));
Check("根目录散装模组不分组", GroupOf("plugins/Loose") == "" && GroupOf("plugins/TC") == "");
Check("没有模组的分类不建组", !proposal.Groups.Any(g => g.name == "空分类"));
var top = proposal.Groups.Where(g => g.parentId == "").ToList();
var child = proposal.Groups.First(g => g.name == "附属"); var grand = proposal.Groups.First(g => g.name == "汉化");
Check("子分组用同色系子色", GroupPalette.FamilyOf(child.color) == GroupPalette.FamilyOf(top[0].color) && child.color != top[0].color && GroupPalette.FamilyOf(grand.color) == GroupPalette.FamilyOf(child.color), top[0].color + "/" + child.color + "/" + grand.color);
Check("没有分类时不提示", !SmartGrouping.HasCategories(E(("plugins/A", true, true, false), ("plugins/A/a.dll", false, false, true))));

// Real plugins folder, read-only.
string plugins = args.Length > 0 ? args[0] : "";
if (plugins.Length > 0 && Directory.Exists(plugins))
{
    string bep = Path.GetDirectoryName(plugins.TrimEnd('/', '\\'))!;
    var real = new List<FolderEntry>();
    foreach (string dir in Directory.EnumerateDirectories(plugins, "*", SearchOption.AllDirectories))
    {
        string rel = Path.GetRelativePath(bep, dir).Replace('\\', '/');
        if (GroupFolderPlanner.NeverTouch(rel)) continue;
        real.Add(new FolderEntry { Path = rel, IsDirectory = true, HasManifest = File.Exists(Path.Combine(dir, "manifest.json")) });
    }
    foreach (string file in Directory.EnumerateFiles(plugins, "*", SearchOption.AllDirectories))
    {
        string rel = Path.GetRelativePath(bep, file).Replace('\\', '/');
        if (GroupFolderPlanner.NeverTouch(rel)) continue;
        string name = Path.GetFileName(file);
        real.Add(new FolderEntry { Path = rel, IsAssembly = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.Contains(".dll.", StringComparison.OrdinalIgnoreCase) });
    }
    var p = SmartGrouping.Propose(real, Id);
    var t = new GroupTree(p.Groups);
    Console.WriteLine($"现场：将生成 {p.Groups.Count} 个分组，{p.PackageGroup.Count} 个模组文件夹归组：");
    foreach (var g in t.Flat) Console.WriteLine("  " + new string(' ', (t.Depth(g.id) - 1) * 2) + g.name + "  " + g.color + "  · " + t.SubtreeCount(g.id, p.PackageGroup, kv => kv.Value) + " 个");
    var ungrouped = GroupFolderPlanner.Classify(real).Packages.Where(pk => !p.PackageGroup.ContainsKey(pk)).ToList();
    Console.WriteLine("  未分组：" + ungrouped.Count + " 个（" + string.Join("、", ungrouped.Select(GroupFolderPlanner.Leaf).Take(8)) + (ungrouped.Count > 8 ? "…" : "") + "）");
    Check("现场：识别出四个顶层分类", t.Roots.Count == 4, string.Join(",", t.Roots.Select(r => r.name)));
    Check("现场：[0依赖]Mods 成为 0核心&基础模组 的子分组", p.Groups.Any(g => g.name == "[0依赖]Mods" && t.Parent(g.id).Length > 0 && t.Get(t.Parent(g.id))!.name == "0核心&基础模组"));
    Check("现场：顶层色系互不相同", t.Roots.Select(r => GroupPalette.FamilyOf(r.color)).Distinct().Count() == t.Roots.Count);
}
Console.WriteLine($"=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;
