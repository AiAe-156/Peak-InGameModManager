using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModConfigEnhance.Overview;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
int passed = 0, failed = 0;
void Check(string name, bool ok, string detail = "") { if (ok) passed++; else failed++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : " · " + detail)); }

// --- path logic ---
var roots = new[] {
    "F:/P/plugins/0核心/[0依赖]Mods", "F:/P/plugins/0核心/[0依赖]Mods/PEAKLib_Core", "F:/P/plugins/0核心/[0依赖]Mods/XUnity",
    "F:/P/plugins/3玩法/地形定制器", "F:/P/plugins/3玩法/地形定制器/Snosz-UBImGui",
    "F:/P/plugins/0核心/ESC资料卡", "F:/P/plugins/0核心/ESC资料卡/plugins",
    "F:/P/plugins/3玩法/天阶壳/CptPICHU-TrueFinalAscent" };
var outer = NestedMods.OuterOf(roots);
Check("内层找到最近外层", outer["F:/P/plugins/0核心/[0依赖]Mods/PEAKLib_Core"] == "F:/P/plugins/0核心/[0依赖]Mods");
Check("内嵌库算嵌套", outer["F:/P/plugins/3玩法/地形定制器/Snosz-UBImGui"] == "F:/P/plugins/3玩法/地形定制器");
Check("plugins/ 打包结构不算嵌套", outer["F:/P/plugins/0核心/ESC资料卡/plugins"] == "");
Check("顶层没有外层", outer["F:/P/plugins/0核心/[0依赖]Mods"] == "" && outer["F:/P/plugins/3玩法/天阶壳/CptPICHU-TrueFinalAscent"] == "");
Check("同名 manifest 不算嵌套", NestedMods.OuterOf(new[] { "a/X", "a/X/Y" }, p => p.EndsWith("Y") || p.EndsWith("X") ? "Same" : null)["a/X/Y"] == "");
Check("前缀相同的兄弟不算嵌套", NestedMods.OuterOf(new[] { "a/Mod", "a/Mod2" })["a/Mod2"] == "");
Check("反斜杠与大小写", NestedMods.OuterOf(new[] { @"C:\p\Outer", @"c:/P/outer/Inner" })["c:/P/outer/Inner"] == "C:/p/Outer");

// --- enablement cascade ---
OverviewMod M(string guid, bool on = true) => new() { Guid = guid, Name = guid, TargetEnabled = on, PluginGuids = new() { guid } };
var shell = M("shell"); var a = M("a"); var b = M("b", false); var deep = M("deep");
shell.Inner.AddRange(new[] { a, b }); a.Outer = shell; b.Outer = shell; a.Inner.Add(deep); deep.Outer = a;
var draft = new List<OverviewProfileMod>();
OverviewEnablementDraft.SetWithNested(draft, shell, false);
Check("外层停用连带内层", !OverviewEnablementDraft.Planned(draft, shell) && !OverviewEnablementDraft.Planned(draft, a) && !OverviewEnablementDraft.Planned(draft, b));
Check("连带到更深一层", !OverviewEnablementDraft.Planned(draft, deep));
Check("记下停用前开着的内层", draft.First(d => d.guid == "shell").nestedEnabled.SequenceEqual(new[] { "a" }), string.Join(",", draft.First(d => d.guid == "shell").nestedEnabled));
Check("外层关着时内层不能单开", OverviewEnablementDraft.BlockedByOuter(draft, a));
bool threw = false; try { OverviewEnablementDraft.SetWithNested(draft, a, true); } catch (InvalidOperationException) { threw = true; }
Check("强行单开内层被拒", threw && !OverviewEnablementDraft.Planned(draft, a));
OverviewEnablementDraft.SetWithNested(draft, shell, true);
Check("重新启用外层：只恢复原来开着的", OverviewEnablementDraft.Planned(draft, a) && !OverviewEnablementDraft.Planned(draft, b), "a=" + OverviewEnablementDraft.Planned(draft, a) + " b=" + OverviewEnablementDraft.Planned(draft, b));
Check("深层也恢复", OverviewEnablementDraft.Planned(draft, deep));
Check("恢复后清掉记录", draft.First(d => d.guid == "shell").nestedEnabled.Count == 0);
var draft2 = new List<OverviewProfileMod>();
OverviewEnablementDraft.SetWithNested(draft2, a, false);
Check("只停内层不影响外层", OverviewEnablementDraft.Planned(draft2, shell) && !OverviewEnablementDraft.Planned(draft2, a));
OverviewEnablementDraft.SetWithNested(draft2, a, true);
Check("外层开着时内层可单开", OverviewEnablementDraft.Planned(draft2, a));
// 2.3.1：只记了外层停用的方案（导入的、旧草稿）落盘时内层也算停用
{
    var o = M("o"); var i1 = M("i1"); o.Inner.Add(i1); i1.Outer = o;
    var only = new List<OverviewProfileMod> { new() { guid = "o", enabled = false } };
    Check("2.3.1 只记外层停用：内层也按停用算", !OverviewEnablementDraft.Planned(only, i1));
    var carried = new List<OverviewProfileMod> { new() { guid = "o", enabled = false } };
    OverviewEnablementDraft.CarryOuterState(carried, new[] { o, i1 });
    Check("2.3.1 应用前把连带状态写回草稿", carried.Any(x => x.guid == "i1" && !x.enabled) && !OverviewEnablementDraft.Planned(carried, i1));
    var locked = M("locked"); locked.CanToggle = false; o.Inner.Add(locked); locked.Outer = o;
    Check("2.3.1 不可启停的内层保持原状，不连带", OverviewEnablementDraft.Planned(only, locked));
    Check("2.3.1 不可启停的内层不显示为被外层挡住", !OverviewEnablementDraft.BlockedByOuter(only, locked));
    var d = new List<OverviewProfileMod>();
    OverviewEnablementDraft.SetWithNested(d, o, false);
    Check("2.3.1 停外层时不把不可启停的内层写进草稿", !d.Any(x => x.guid == "locked") && d.First(x => x.guid == "o").nestedEnabled.SequenceEqual(new[] { "i1" }), string.Join(",", d.Select(x => x.guid + ":" + x.enabled)));
    OverviewEnablementDraft.SetWithNested(d, o, true);
    Check("2.3.1 重新启用外层：内层恢复", OverviewEnablementDraft.Planned(d, i1) && OverviewEnablementDraft.Planned(d, locked));
}
var flat = M("flat"); var draft3 = new List<OverviewProfileMod>();
OverviewEnablementDraft.SetWithNested(draft3, flat, false); OverviewEnablementDraft.SetWithNested(draft3, flat, true);
Check("普通模组行为不变", OverviewEnablementDraft.Planned(draft3, flat) && draft3.Single().nestedEnabled.Count == 0);

// --- real plugins folder (read-only) ---
string plugins = args.Length > 0 ? args[0] : "";
if (plugins.Length > 0 && Directory.Exists(plugins))
{
    var real = Directory.EnumerateFiles(plugins, "manifest.json", SearchOption.AllDirectories).Select(f => Path.GetDirectoryName(f)!).ToList();
    var map = NestedMods.OuterOf(real, NestedMods.ManifestName);
    var nested = map.Where(p => p.Value.Length > 0).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToList();
    Console.WriteLine($"现场：{real.Count} 个 manifest 目录，其中 {nested.Count} 个嵌套在别的模组里：");
    string Rel(string p) => p.Substring(NestedMods.Normalize(plugins).Length + 1);
    foreach (var pair in nested) Console.WriteLine("  " + Rel(pair.Key) + "  ⊂  " + Rel(pair.Value));
    Check("现场：ESC 资料卡的 plugins/ 不算嵌套", !nested.Any(p => p.Key.Contains("Profile_Cards")));
    // [0依赖]Mods has no manifest of its own: it is a category folder (a group for smart grouping), not a mod, so its contents are not "nested mods".
    Check("现场：无 manifest 的分类文件夹里的模组不算嵌套", !nested.Any(p => p.Value.EndsWith("[0依赖]Mods")));
    Check("现场：UBImGui、UpscaleLib 识别为嵌套", nested.Any(p => p.Key.EndsWith("Snosz-UBImGui")) && nested.Any(p => p.Key.EndsWith("glarmer-UpscaleLib")));
}
Console.WriteLine($"=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;
