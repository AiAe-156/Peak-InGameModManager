using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
using K = ModConfigEnhance.Overview.LayoutReviewModel.Kind;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
int passed = 0, failed = 0;
void Check(string name, bool ok, string detail = "") { if (ok) passed++; else failed++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : " · " + detail)); }
LayoutTreeNode N(string from, string to, InstallationLayoutDisposition d, LayoutNodeKind kind = LayoutNodeKind.Package, int mods = 1, params string[] notes)
    => new() { OriginalPath = from, FinalPath = to, Folder = to.Substring(to.LastIndexOf('/') + 1), Disposition = d, Kind = kind, ModCount = mods, Notes = notes.ToList() };

var synthetic = new List<LayoutTreeNode> {
    N("plugins/0核心", "plugins/0基础", InstallationLayoutDisposition.Move, LayoutNodeKind.Container, 0),
    N("plugins/0核心/A", "plugins/0基础/A", InstallationLayoutDisposition.Move, mods: 1, notes: "CarriedByFolderRename"),
    N("plugins/X", "plugins/3玩法/天阶/X", InstallationLayoutDisposition.Move, mods: 2),
    N("plugins/TC/UB", "plugins/依赖/UB", InstallationLayoutDisposition.Move, mods: 1, notes: "NestedExtracted"),
    N("plugins/Fog", "plugins/Fog", InstallationLayoutDisposition.Unchanged, mods: 0, notes: "NoGroupKnown"),
    N("plugins/l10n", "plugins/l10n", InstallationLayoutDisposition.Unchanged, LayoutNodeKind.Empty, 0),
};
var sections = LayoutReviewModel.Build(synthetic, out var summary);
Check("计数：文件夹与模组分开", summary.Folders == 4 && summary.Mods == 5, summary.Folders + "/" + summary.Mods);
Check("改名的分类文件夹记在卡片上", sections.Any(s => s.Key == "0基础" && s.RenamedFrom == "0核心") && summary.Renamed == 1);
Check("随改名到位的算原位", sections.First(s => s.Key == "0基础").Entries.Single().Kind == K.InPlace);
Check("嵌套分组生成父卡片", sections.Any(s => s.Key == "3玩法") && sections.First(s => s.Key == "3玩法/天阶").Depth == 2);
Check("拆出单独计数", summary.Extracted == 1 && sections.First(s => s.Key == "依赖").Entries.Single().Kind == K.Extracted);
Check("不动与空文件夹在最后", sections[^2].Stays && sections[^1].EmptyFolders);
Check("父卡计数含子卡", LayoutReviewModel.SubtreeMods(sections.First(s => s.Key == "3玩法"), sections) == 2);
Check("折叠父卡隐藏子卡", LayoutReviewModel.HiddenUnder(sections.First(s => s.Key == "3玩法/天阶"), new HashSet<string> { "3玩法" }));

string path = Path.Combine(AppContext.BaseDirectory, "../../../../../2.3.0嵌套分组与整理重做/group-dry-run.json");
var real = JsonSerializer.Deserialize<List<LayoutTreeNode>>(File.ReadAllText(path))!;
var realSections = LayoutReviewModel.Build(real, out var rs);
Console.WriteLine($"现场干跑：{rs.Folders} 个文件夹 · 共 {rs.Mods} 个模组 · 移入 {rs.Move} 拆出 {rs.Extracted} 改名 {rs.Renamed} 原位 {rs.InPlace} 未分组 {rs.Ungrouped} 不动 {rs.Stays} 空文件夹 {rs.Empty}");
foreach (var s in realSections) Console.WriteLine($"  {new string(' ', (s.Depth - 1) * 2)}[{(s.OpenByDefault ? "展开" : "折叠")}] {(s.Stays ? "不动" : s.EmptyFolders ? "空文件夹" : s.Key + "/")}{(s.RenamedFrom.Length > 0 ? " ← 由 " + s.RenamedFrom + " 改名" : "")} · {s.Entries.Count} 个");
Check("现场：每个节点恰好出现一次（分类文件夹除外）", realSections.Sum(s => s.Entries.Count) == real.Count(n => n.Kind != LayoutNodeKind.Container));
Check("现场：模组数接近概览（62~66）", rs.Mods >= 60, rs.Mods.ToString());
Check("现场：说明码都有中文", realSections.SelectMany(s => s.Entries).SelectMany(e => e.Reasons.Concat(e.Notes)).All(c => LayoutReviewView_Reason(c) != c), string.Join(",", realSections.SelectMany(s => s.Entries).SelectMany(e => e.Reasons.Concat(e.Notes)).Distinct()));
Console.WriteLine($"=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

// The reason table lives in the Unity view; read the codes it handles from its source.
static string LayoutReviewView_Reason(string code)
{
    string view = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../../src/Overview/LayoutReviewView.cs"));
    return view.Contains("\"" + code + "\"") ? "ok" : code;
}
