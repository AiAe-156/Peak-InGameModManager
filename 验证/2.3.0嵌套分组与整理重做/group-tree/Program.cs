using System;
using System.Collections.Generic;
using System.Linq;
using ModConfigEnhance.Overview;

int passed = 0, failed = 0;
void Check(string name, bool ok, string detail = "") { if (ok) passed++; else failed++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : " · " + detail)); }
OverviewGroup G(string id, string parent = "", int order = 0, string color = "#7CC7E6") => new() { id = id, name = id, parentId = parent, order = order, color = color };
List<OverviewGroup> Sample() => new() { G("play", "", 1), G("tfa", "play", 0), G("tfa-zh", "tfa", 0), G("tfa-addon", "tfa", 1), G("terrain", "play", 1), G("core", "", 0) };

var tree = new GroupTree(Sample());
Check("树序：深度优先、同级按 order", string.Join(",", tree.Flat.Select(g => g.id)) == "core,play,tfa,tfa-zh,tfa-addon,terrain", string.Join(",", tree.Flat.Select(g => g.id)));
Check("深度 1/2/3", tree.Depth("play") == 1 && tree.Depth("tfa") == 2 && tree.Depth("tfa-zh") == 3 && tree.Depth("nope") == 0);
Check("路径", tree.Path("tfa-zh") == "play › tfa › tfa-zh", tree.Path("tfa-zh"));
Check("子树", tree.Subtree("tfa").SetEquals(new[] { "tfa", "tfa-zh", "tfa-addon" }));
Check("高度", tree.Height("play") == 3 && tree.Height("tfa") == 2 && tree.Height("core") == 1);

Check("禁止成环", !tree.CanAttach("play", "tfa-zh", out var r1) && r1 == "cycle", r1);
Check("恰好 3 层可以挂（tfa 高 2 挂到 core 深 1）", tree.CanAttach("tfa", "core", out var r2), r2);
Check("禁止超 3 层（4 层）", !tree.CanAttach("play", "core", out var r3) && r3 == "depth", r3);
Check("挂到自己", !tree.CanAttach("tfa", "tfa", out var r4) && r4 == "cycle", r4);
Check("移到顶层总是可以", tree.CanAttach("tfa-zh", "", out _));

var bad = new List<OverviewGroup> { G("a", "b"), G("b", "a"), G("c", "ghost"), G("d", "c"), G("e", "d"), G("f", "e") };
var badTree = new GroupTree(bad);
Check("成环数据当顶层（不丢组）", badTree.Flat.Count == 6 && badTree.Depth("a") == 1 && badTree.Depth("b") == 1, string.Join(",", badTree.Flat.Select(g => g.id + ":" + badTree.Depth(g.id))));
Check("悬空上级当顶层", badTree.Depth("c") == 1);
Check("超深链截断", badTree.Depth("e") == 3 && badTree.Depth("f") == 1, "e=" + badTree.Depth("e") + " f=" + badTree.Depth("f"));

var collapsed = new HashSet<string> { "tfa" };
Check("折叠父级：成员隐藏", tree.HiddenByCollapse("tfa-zh", collapsed) && tree.HiddenByCollapse("tfa", collapsed) && !tree.HiddenByCollapse("play", collapsed));
Check("折叠父级：子标题隐藏、自身标题可见", tree.HeaderHidden("tfa-zh", collapsed) && !tree.HeaderHidden("tfa", collapsed));

var rules = new List<OverviewModRule> { new() { id = "m1", groupId = "tfa" }, new() { id = "m2", groupId = "tfa-zh" }, new() { id = "m3", groupId = "play" }, new() { id = "m4", groupId = "" } };
Check("子树计数", tree.SubtreeCount("play", rules, x => x.groupId) == 3 && tree.SubtreeCount("tfa", rules, x => x.groupId) == 2);

var groups = Sample(); var col = new HashSet<string> { "tfa" };
GroupTree.Remove(groups, rules, "tfa", col);
var after = new GroupTree(groups);
Check("删除：子分组上移一层", after.Parent("tfa-zh") == "play" && after.Parent("tfa-addon") == "play" && !after.Contains("tfa"));
Check("删除：成员归上级", rules.First(x => x.id == "m1").groupId == "play" && rules.First(x => x.id == "m2").groupId == "tfa-zh");
Check("删除：清折叠记录", !col.Contains("tfa"));
Check("删除：同级重新编号", after.Children("play").Select(g => g.order).SequenceEqual(Enumerable.Range(0, after.Children("play").Count)));
groups = Sample(); var rules2 = new List<OverviewModRule> { new() { id = "x", groupId = "core" } };
GroupTree.Remove(groups, rules2, "core");
Check("删除顶层：成员变未分组", rules2[0].groupId == "");

groups = Sample();
Check("挂载成功", GroupTree.Attach(groups, "terrain", "core", out _) && new GroupTree(groups).Parent("terrain") == "core");
Check("挂载失败不改动", !GroupTree.Attach(groups, "play", "tfa", out var r5) && r5 == "cycle" && new GroupTree(groups).Parent("play") == "");
Check("同级移动", GroupTree.MoveAmongSiblings(groups, "tfa-addon", -1) && new GroupTree(groups).Children("tfa")[0].id == "tfa-addon");
Check("同级移动到边界返回 false", !GroupTree.MoveAmongSiblings(groups, "tfa-addon", -1));
Check("新建 order", GroupTree.NextOrder(groups, "tfa") == 2 && GroupTree.NextOrder(groups, "") == 2, GroupTree.NextOrder(groups, "tfa") + "/" + GroupTree.NextOrder(groups, ""));

Check("schema：无嵌套=1", GroupTree.SchemaFor(new[] { G("a"), G("b") }) == 1);
Check("schema：有嵌套=2", GroupTree.SchemaFor(Sample()) == 2);
Check("schema：只认 1 和 2", GroupTree.SupportedSchema(1) && GroupTree.SupportedSchema(2) && !GroupTree.SupportedSchema(3) && !GroupTree.SupportedSchema(0));

// palette
var tops = new List<string?>(); for (int i = 0; i < 5; i++) tops.Add(GroupPalette.NextTopColor(tops));
Check("顶层色系互不相同", tops.Select(GroupPalette.FamilyOf).Distinct().Count() == 5, string.Join(",", tops));
string parent = GroupPalette.Families[0][0];
var kids = new List<string?>(); for (int i = 0; i < 3; i++) kids.Add(GroupPalette.NextChildColor(parent, kids));
Check("子色同色系、互不相同、不同于父色", kids.All(k => GroupPalette.FamilyOf(k) == 0) && kids.Distinct().Count() == 3 && !kids.Contains(parent), string.Join(",", kids));
Check("自定义父色：子级沿用", GroupPalette.NextChildColor("#123456", Array.Empty<string?>()) == "#123456");
var mix = new List<OverviewGroup> { G("a", "", 0, GroupPalette.Families[0][0]), G("b", "a", 0, GroupPalette.Families[0][1]) };
Check("新组颜色：子级取未用子色", GroupPalette.ForNewGroup(mix, "a") == GroupPalette.Families[0][2], GroupPalette.ForNewGroup(mix, "a"));
Check("新组颜色：顶层取新色系", GroupPalette.FamilyOf(GroupPalette.ForNewGroup(mix, "")) == 1);

// 2.3.1 审查修复
{
    // P(play) 下 [X(0), D(1), Y(2)]，D 下 [c1(0), c2(1)]：删 D 后 c1、c2 连续占 D 的位置
    var g = new List<OverviewGroup> { G("P"), G("X", "P", 0), G("D", "P", 1), G("Y", "P", 2), G("c1", "D", 0), G("c2", "D", 1) };
    GroupTree.Remove(g, new List<OverviewModRule>(), "D");
    var order = string.Join(",", new GroupTree(g).Children("P").Select(x => x.id));
    Check("2.3.1 删除：上移的子分组连续占被删分组的位置", order == "X,c1,c2,Y", order);
    var top = new List<OverviewGroup> { G("A", "", 0), G("B", "", 1), G("b1", "B", 0), G("C", "", 2) };
    GroupTree.Remove(top, new List<OverviewModRule>(), "B");
    var order2 = string.Join(",", new GroupTree(top).Roots.Select(x => x.id));
    Check("2.3.1 删除顶层：子分组占原位置", order2 == "A,b1,C", order2);
}
{
    var all = GroupPalette.Families.SelectMany(f => f).ToList();
    var dup = all.GroupBy(c => c, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
    Check("2.3.1 色板：没有两个色系共用的颜色", dup.Count == 0, string.Join(",", dup));
    Check("2.3.1 色板：每个子色都归回自己的色系", Enumerable.Range(0, GroupPalette.Families.Length).All(f => GroupPalette.Families[f].All(c => GroupPalette.FamilyOf(c) == f)));
}

Console.WriteLine($"=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;
