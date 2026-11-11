using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModConfigEnhance.Updating;

Console.OutputEncoding = new System.Text.UTF8Encoding(false);
int passed = 0, failed = 0;
void Check(string name, bool ok, string detail = "") { if (ok) passed++; else failed++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : " · " + detail)); }

FolderLayoutInput Input() => new() { BepInExAbsolutePath = @"F:\Game\BepInEx", UngroupedFolderName = "未分组" };
void Dir(FolderLayoutInput i, string path, string manifest = "", string version = "1.0.0", params string[] deps)
{ i.Entries.Add(new FolderEntry { Path = path, IsDirectory = true, HasManifest = manifest.Length > 0, ManifestName = manifest, ManifestVersion = version, Dependencies = deps.ToList() }); }
void Dll(FolderLayoutInput i, string path, params string[] guids) => i.Entries.Add(new FolderEntry { Path = path, IsAssembly = true, PluginGuids = guids.ToList() });
void Plain(FolderLayoutInput i, string path) => i.Entries.Add(new FolderEntry { Path = path });
void Row(FolderLayoutInput i, string guid, string group, string root, bool loaded = true, string version = "1.0.0") => i.Rows.Add(new FolderRow { Guids = { guid }, GroupId = group, PackageRoot = root, Loaded = loaded, Version = version });
void G(FolderLayoutInput i, string id, string name, string parent = "") => i.Groups.Add(new FolderGroup { Id = id, Name = name, ParentId = parent });
string? MoveOf(FolderLayoutResult r, string from) => r.Moves.FirstOrDefault(m => m.From == from)?.To;
LayoutTreeNode? Node(FolderLayoutResult r, string original) => r.Nodes.FirstOrDefault(n => n.OriginalPath == original);
string Moves(FolderLayoutResult r) => string.Join(" | ", r.Moves.Select(m => m.From + "→" + m.To));

// 1. nested groups → folder chain; folder names unchanged.
{
    var i = Input(); G(i, "play", "3玩法模组"); G(i, "tfa", "天阶", "play"); G(i, "zh", "天阶汉化", "tfa");
    Dir(i, "plugins/Foo-Foo", "Foo"); Dll(i, "plugins/Foo-Foo/Foo.dll", "team.foo"); Row(i, "team.foo", "zh", "plugins/Foo-Foo");
    var r = GroupFolderPlanner.Plan(i);
    Check("三层分组 → 三层文件夹，模组名不改", MoveOf(r, "plugins/Foo-Foo") == "plugins/3玩法模组/天阶/天阶汉化/Foo-Foo", Moves(r));
}
// 2. illegal characters dropped.
Check("非法字符直接去掉", GroupFolderPlanner.FolderName("A:B/C*?\"<>|D") == "ABCD" && GroupFolderPlanner.FolderName("CON") == "_CON" && GroupFolderPlanner.FolderName("  ") == "_");
// 3. category folder renamed to the group name; packages ride along; strays move in.
{
    var i = Input(); G(i, "core", "0基础模组");
    Dir(i, "plugins/0核心&基础模组");
    Dir(i, "plugins/0核心&基础模组/A-A", "A"); Dll(i, "plugins/0核心&基础模组/A-A/A.dll", "a"); Row(i, "a", "core", "plugins/0核心&基础模组/A-A");
    Dir(i, "plugins/0核心&基础模组/B-B", "B"); Dll(i, "plugins/0核心&基础模组/B-B/B.dll", "b"); Row(i, "b", "core", "plugins/0核心&基础模组/B-B");
    Dir(i, "plugins/C-C", "C"); Dll(i, "plugins/C-C/C.dll", "c"); Row(i, "c", "core", "plugins/C-C");
    var r = GroupFolderPlanner.Plan(i);
    Check("分类文件夹改成分组名（一次改名）", r.Moves.Count(m => m.Container) == 1 && MoveOf(r, "plugins/0核心&基础模组") == "plugins/0基础模组", Moves(r));
    Check("里面的模组随之到位、不单独移动", MoveOf(r, "plugins/0核心&基础模组/A-A") == null && Node(r, "plugins/0核心&基础模组/A-A")?.FinalPath == "plugins/0基础模组/A-A", Node(r, "plugins/0核心&基础模组/A-A")?.FinalPath ?? "null");
    Check("散装的移入", MoveOf(r, "plugins/C-C") == "plugins/0基础模组/C-C", Moves(r));
    Check("改名先于移入", r.Moves.FindIndex(m => m.Container) < r.Moves.FindIndex(m => m.From == "plugins/C-C"));
}
// 4. nested package extracted; packaging layout kept.
{
    var i = Input(); G(i, "play", "3玩法模组"); G(i, "lib", "依赖模组");
    Dir(i, "plugins/TC", "TerrainCustomiser"); Dll(i, "plugins/TC/TC.dll", "tc"); Row(i, "tc", "play", "plugins/TC");
    Dir(i, "plugins/TC/Snosz-UBImGui", "UBImGui"); Dll(i, "plugins/TC/Snosz-UBImGui/UBImGui.dll", "ub"); Row(i, "ub", "lib", "plugins/TC/Snosz-UBImGui");
    Dir(i, "plugins/ESC", "ProfileCards"); Dir(i, "plugins/ESC/plugins", "ProfileCards"); Dll(i, "plugins/ESC/plugins/pc.dll", "pc"); Row(i, "pc", "play", "plugins/ESC");
    var r = GroupFolderPlanner.Plan(i);
    Check("嵌套模组拆出到自己的分组", MoveOf(r, "plugins/TC/Snosz-UBImGui") == "plugins/依赖模组/Snosz-UBImGui", Moves(r));
    Check("拆内层在移外层之前", r.Moves.FindIndex(m => m.From == "plugins/TC/Snosz-UBImGui") < r.Moves.FindIndex(m => m.From == "plugins/TC"), Moves(r));
    Check("拆出标注", Node(r, "plugins/TC/Snosz-UBImGui")?.Notes.Contains("NestedExtracted") == true);
    Check("打包结构 plugins/ 不拆", !r.Moves.Any(m => m.From.StartsWith("plugins/ESC/")) && MoveOf(r, "plugins/ESC") == "plugins/3玩法模组/ESC", Moves(r));
}
// 5. pure category folder takes the majority group's name.
{
    var i = Input(); G(i, "lib", "依赖模组");
    Dir(i, "plugins/[0依赖]Mods");
    Dir(i, "plugins/[0依赖]Mods/Core", "PEAKLib_Core"); Dll(i, "plugins/[0依赖]Mods/Core/c.dll", "core"); Row(i, "core", "lib", "plugins/[0依赖]Mods/Core");
    Dir(i, "plugins/[0依赖]Mods/UI", "PEAKLib_UI"); Dll(i, "plugins/[0依赖]Mods/UI/u.dll", "ui"); Row(i, "ui", "lib", "plugins/[0依赖]Mods/UI");
    var r = GroupFolderPlanner.Plan(i);
    Check("纯分类文件夹按多数分组改名", MoveOf(r, "plugins/[0依赖]Mods") == "plugins/依赖模组", Moves(r));
}
// 6. ungrouped → 未分组 folder; never-touch folders untouched and unlisted.
{
    var i = Input();
    Dir(i, "plugins/FD", "FrameDoctor"); Dll(i, "plugins/FD/FD.dll", "fd"); Plain(i, "plugins/FD/FD.dll.old"); Row(i, "fd", "", "plugins/FD");
    Dir(i, "plugins/MMHOOK"); Dir(i, "plugins/MMHOOK/Managed"); Dll(i, "plugins/MMHOOK/Managed/MMHOOK_Assembly-CSharp.dll");
    Dir(i, "plugins/MCEPack_6b"); Plain(i, "plugins/MCEPack_6b/x.json");
    var r = GroupFolderPlanner.Plan(i);
    Check("未分组移到「未分组」", MoveOf(r, "plugins/FD") == "plugins/未分组/FD" && Node(r, "plugins/FD")?.Ungrouped == true, Moves(r));
    Check("MMHOOK / MCEPack 不碰不列", !r.Nodes.Any(n => n.OriginalPath.Contains("MMHOOK") || n.OriginalPath.Contains("MCEPack")) && !r.Moves.Any(m => m.From.Contains("MMHOOK")));
}
// 7. inactive copy follows the same-named mod; collision gets the version suffix; the loaded copy keeps its name.
{
    var i = Input(); G(i, "play", "3玩法模组");
    Dir(i, "plugins/Shell"); Dir(i, "plugins/Shell/CptPICHU-TFA", "TrueFinalAscent", "3.0.4"); Dll(i, "plugins/Shell/CptPICHU-TFA/T.dll", "tfa"); Row(i, "tfa", "play", "plugins/Shell/CptPICHU-TFA", true, "3.0.4");
    Dir(i, "plugins/CptPICHU-TFA", "TrueFinalAscent", "3.0.0"); Dll(i, "plugins/CptPICHU-TFA/T.dll", "tfa"); Row(i, "tfa", "play", "plugins/CptPICHU-TFA", false, "3.0.0");
    var r = GroupFolderPlanner.Plan(i);
    Check("生效那份保持原名", MoveOf(r, "plugins/Shell/CptPICHU-TFA") == "plugins/3玩法模组/CptPICHU-TFA", Moves(r));
    Check("重名的加版本号", MoveOf(r, "plugins/CptPICHU-TFA") == "plugins/3玩法模组/CptPICHU-TFA 3.0.0", Moves(r));
    Check("拆完的空壳列为空文件夹", r.Nodes.Any(n => n.Kind == LayoutNodeKind.Empty && n.OriginalPath == "plugins/Shell"), string.Join(",", r.Nodes.Where(n => n.Kind == LayoutNodeKind.Empty).Select(n => n.OriginalPath)));
    var j = Input(); G(j, "play", "3玩法模组");
    Dir(j, "plugins/Old", "Mce", "1.0"); Plain(j, "plugins/Old/M.dll1");
    Dir(j, "plugins/New", "Mce", "2.0"); Dll(j, "plugins/New/M.dll", "mce"); Row(j, "mce", "play", "plugins/New");
    var rj = GroupFolderPlanner.Plan(j);
    Check("没识别出插件的旧副本跟同名模组走", MoveOf(rj, "plugins/Old") == "plugins/3玩法模组/Old" && Node(rj, "plugins/Old")?.Notes.Contains("GroupFromSameName") == true, Moves(rj));
}
// 8. DLL-less packages follow whoever depends on them; otherwise stay.
{
    var i = Input(); G(i, "lib", "依赖模组");
    Dir(i, "plugins/MonoDetour-MonoDetour", "MonoDetour");
    Dir(i, "plugins/Core", "PEAKLib_Core", "1.0.0", "MonoDetour-MonoDetour-0.7.17"); Dll(i, "plugins/Core/c.dll", "core"); Row(i, "core", "lib", "plugins/Core");
    Dir(i, "plugins/Fog", "PLL_Fog_Resources");
    var r = GroupFolderPlanner.Plan(i);
    Check("无 DLL 的包跟依赖它的模组", MoveOf(r, "plugins/MonoDetour-MonoDetour") == "plugins/依赖模组/MonoDetour-MonoDetour" && Node(r, "plugins/MonoDetour-MonoDetour")?.Notes.Contains("GroupFromDependent") == true, Moves(r));
    Check("找不到归属的原位不动", MoveOf(r, "plugins/Fog") == null && Node(r, "plugins/Fog")?.Notes.Contains("NoGroupKnown") == true);
}
// 9. manager last; links stay; empty folders listed; path length.
{
    var i = Input(); G(i, "core", "0基础模组");
    Dir(i, "plugins/MCE", "ModConfigEnhance"); Dll(i, "plugins/MCE/ModConfigEnhance.dll", "com.aiae.modconfigenhance"); Row(i, "com.aiae.modconfigenhance", "core", "plugins/MCE");
    Dir(i, "plugins/Z", "Z"); Dll(i, "plugins/Z/z.dll", "z"); Row(i, "z", "core", "plugins/Z");
    i.Entries.Add(new FolderEntry { Path = "plugins/L", IsDirectory = true, HasManifest = true, ManifestName = "L", HasLink = true }); Dll(i, "plugins/L/l.dll", "l"); Row(i, "l", "core", "plugins/L");
    Dir(i, "plugins/glarmer-Empty"); Dir(i, "plugins/local"); Plain(i, "plugins/local/README.md");
    var r = GroupFolderPlanner.Plan(i);
    Check("管理器最后移动", r.Moves.Count > 0 && r.Moves.Last().From == "plugins/MCE" && Node(r, "plugins/MCE")?.Manager == true, Moves(r));
    Check("含链接的不动", MoveOf(r, "plugins/L") == null && Node(r, "plugins/L")?.Reasons.Contains("LinkRejected") == true);
    Check("空文件夹列出（含只有 md 的）", r.Nodes.Count(n => n.Kind == LayoutNodeKind.Empty) == 2 && r.Nodes.First(n => n.OriginalPath == "plugins/local").FileCount == 1, string.Join(",", r.Nodes.Where(n => n.Kind == LayoutNodeKind.Empty).Select(n => n.OriginalPath)));
    var k = Input(); k.MaximumFullPathLength = 40; G(k, "core", "0基础模组");
    Dir(k, "plugins/Long", "Long"); Dll(k, "plugins/Long/AVeryLongAssemblyName.dll", "x"); Row(k, "x", "core", "plugins/Long");
    Check("路径超长不动", GroupFolderPlanner.Plan(k).Nodes.First(n => n.OriginalPath == "plugins/Long").Reasons.Contains("PathLengthExceeded"));
}
// 10. already in place → nothing.
{
    var i = Input(); G(i, "core", "0基础模组");
    Dir(i, "plugins/0基础模组"); Dir(i, "plugins/0基础模组/A", "A"); Dll(i, "plugins/0基础模组/A/a.dll", "a"); Row(i, "a", "core", "plugins/0基础模组/A");
    var r = GroupFolderPlanner.Plan(i);
    Check("已就位则零移动（幂等）", r.Moves.Count == 0 && Node(r, "plugins/0基础模组/A")?.Disposition == InstallationLayoutDisposition.Unchanged, Moves(r));
}
// 11. 2.3.1：MCE 停用过的模组（manifest.json.old、DLL 在子目录）仍是一个包，跟自己的分组走
{
    var i = Input(); G(i, "play", "3玩法模组");
    // 采集端把 manifest.json.old 记为 HasManifest（Updater/GroupLayoutPlanner.Collect）
    Dir(i, "plugins/Off-Off", "Off"); Dir(i, "plugins/Off-Off/plugins"); Dll(i, "plugins/Off-Off/plugins/Off.dll.d", "team.off");
    Row(i, "team.off", "play", "plugins/Off-Off", loaded: false);
    var r = GroupFolderPlanner.Plan(i);
    Check("2.3.1 停用模组整体移入自己的分组", MoveOf(r, "plugins/Off-Off") == "plugins/3玩法模组/Off-Off" && !r.Moves.Any(m => m.From.Contains("Off-Off/")), Moves(r));
    Check("2.3.1 停用模组不被当成分类文件夹或空文件夹", !r.Nodes.Any(n => n.Kind != LayoutNodeKind.Package && n.OriginalPath.StartsWith("plugins/Off-Off")));
}
// 12. 2.3.1：分类文件夹改名后，里面的空文件夹给出改名后的位置（回收站按它找）
{
    var i = Input(); G(i, "core", "0基础模组");
    Dir(i, "plugins/0核心&基础模组");
    Dir(i, "plugins/0核心&基础模组/A-A", "A"); Dll(i, "plugins/0核心&基础模组/A-A/A.dll", "a"); Row(i, "a", "core", "plugins/0核心&基础模组/A-A");
    Dir(i, "plugins/0核心&基础模组/B-B", "B"); Dll(i, "plugins/0核心&基础模组/B-B/B.dll", "b"); Row(i, "b", "core", "plugins/0核心&基础模组/B-B");
    Dir(i, "plugins/0核心&基础模组/leftover");
    var r = GroupFolderPlanner.Plan(i);
    var empty = r.Nodes.FirstOrDefault(n => n.Kind == LayoutNodeKind.Empty && n.OriginalPath == "plugins/0核心&基础模组/leftover");
    Check("2.3.1 空文件夹随分类文件夹改名换位置", empty?.FinalPath == "plugins/0基础模组/leftover", empty?.FinalPath ?? "null");
}

Console.WriteLine($"=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;
