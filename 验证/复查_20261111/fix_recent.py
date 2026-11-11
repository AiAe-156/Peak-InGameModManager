# -*- coding: utf-8 -*-
"""第二轮复查（2.5.4 / 2.5.5 / MCE 1.0.3）的代码侧修复：#1 #6 #8 #9。#2 在 MceDirectories/MceDirectoryMigration 里单独改。
用法：python -I fix_recent.py"""
import io
import os

MODS = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
IG = os.path.join(MODS, "InGameModManager")
MC = os.path.join(MODS, "ModConfigEnhance")


def sub(p, old, new, n=1):
    raw = io.open(p, "rb").read(); bom = raw.startswith(b"\xef\xbb\xbf")
    t = raw.decode("utf-8-sig"); crlf = "\r\n" in t; t = t.replace("\r\n", "\n")
    assert t.count(old) == n, (p, old[:90], t.count(old))
    t = t.replace(old, new)
    if crlf: t = t.replace("\n", "\r\n")
    io.open(p, "wb").write((b"\xef\xbb\xbf" if bom else b"") + t.encode("utf-8"))


# ── #1：小数补丁独立挂载（两个模组），不再跟树形侧栏一起 ──
for root in (IG, MC):
    sub(os.path.join(root, "src", "ModConfigTreeLayoutPatch.cs"),
        "        ModFloatDisplayPatch.Apply(harmony);\n        Plugin.Log.LogInfo(\"[小数显示] \" + ModFloatDisplayPatch.State);\n", "")
sub(os.path.join(IG, "src", "Plugin.cs"), """        Overview.OverviewController.Register(harmony);
        if (EnableTreeLayout.Value)""", """        Overview.OverviewController.Register(harmony);
        // 复查：小数显示是数据正确性（PEAK 3.1 下手打 1.25 会存错），与树形侧栏无关，独立挂载
        ModFloatDisplayPatch.Apply(harmony);
        Log.LogInfo("[小数显示] " + ModFloatDisplayPatch.State);
        if (EnableTreeLayout.Value)""")
sub(os.path.join(MC, "src", "Plugin.cs"), """        Harmony harmony = new(PluginGuid);
        if (EnableTreeLayout.Value)""", """        Harmony harmony = new(PluginGuid);
        // 1.0.3：小数显示是数据正确性（PEAK 3.1 下手打 1.25 会存错），与树形侧栏无关，独立挂载
        ModFloatDisplayPatch.Apply(harmony);
        Log.LogInfo("[小数显示] " + ModFloatDisplayPatch.State);
        if (EnableTreeLayout.Value)""")

# ── #6：分包合并先换上结果再删其余文件 ──
sub(os.path.join(IG, "src", "Loc.Import.cs"), """                    if (kept > 0) File.WriteAllText(staged, content, new UTF8Encoding(false));
                    foreach (string file in files) { File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), true); }
                    foreach (string file in files) { File.Delete(file); moved++; }
                    if (kept > 0) { File.Move(staged, target); merged++; }""", """                    if (kept > 0) File.WriteAllText(staged, content, new UTF8Encoding(false));
                    foreach (string file in files) { File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), true); }
                    // 复查：先把合并结果换上（目标是同组基础文件，覆盖它），再删其余分包。中途被占用时已删的分包内容都在合并结果里，
                    // 不会只剩在备份目录；下次重跑时合并结果作为基础文件参与，不丢译文。
                    if (kept > 0) { File.Copy(staged, target, true); File.Delete(staged); merged++; }
                    foreach (string file in files.Where(f => !f.Equals(target, StringComparison.OrdinalIgnoreCase) || kept == 0)) { File.Delete(file); moved++; }""")

# ── #8：退役清单换代时重跑清理 ──
sub(os.path.join(IG, "src", "Loc.Import.cs"), """        if (File.Exists(Path.Combine(language, RetiredMarker))) return;""",
    """        // 复查：标记里记退役清单的代次，清单增加（2.5.4 的 NOTICE_IMPORTED、2.5.5 的 TIP_OPEN_DIR_V2）后已升级的用户再清一次
        string marker = Path.Combine(language, RetiredMarker);
        if (File.Exists(marker) && File.ReadAllText(marker).StartsWith(RetiredGeneration + " ", StringComparison.Ordinal)) return;""")
sub(os.path.join(IG, "src", "Loc.Import.cs"), """            File.WriteAllText(Path.Combine(language, RetiredMarker), "2.5.1 " + stamp + "\\n", new UTF8Encoding(false));""",
    """            File.WriteAllText(marker, RetiredGeneration + " " + stamp + "\\n", new UTF8Encoding(false));""")
sub(os.path.join(IG, "src", "Loc.Import.cs"), """    private const string RetiredMarker = ".mce-language-v3";""",
    """    private const string RetiredMarker = ".mce-language-v3";
    /// <summary>退役清单代次：<see cref="Loc.RetiredTerms"/> 或 <c>RetiredOriginal</c> 增加条目时加一。</summary>
    private const string RetiredGeneration = "g3";""")

# ── #9：MCE 默认不部署（拆分后 MCE 不装进游戏） ──
sub(os.path.join(MC, "ModConfigEnhance.csproj"), """<Target Name="Deploy" AfterTargets="PostBuildEvent" Condition="'$(EnableDeployment)' != 'false'">""",
    """<!-- 1.0.3：拆分后游戏里装的是完整版 IGMM，MCE 默认只构建不部署；确需装进游戏时加 -p:EnableDeployment=true -->
  <Target Name="Deploy" AfterTargets="PostBuildEvent" Condition="'$(EnableDeployment)' == 'true'">""")
print("ok")
