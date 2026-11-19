#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ModConfigEnhance;
using ModConfigEnhance.Overview;

// 手柄组合键判定规则的纯逻辑夹具（2026-11-15，快捷键页第 2 章）。
// 被测代码 = src/GamepadComboRules.cs（IGMM 0.0.2 起指向 IGMM 的 src）（run.py 复制进来一起编译，不是复刻）。
// Fire() 模拟 GamepadCombos.Tick 的逐组判定：最后一个键按着 且 GamepadComboRules.Satisfied(...)；
// _heldButtons = 所有组「先按住」键的并集（GamepadCombos.Reindex）。
internal static class Program
{
    private const string A = "buttonSouth", B = "buttonEast", X = "buttonWest", LT = "leftTrigger", LB = "leftShoulder", RT = "rightTrigger", RB = "rightShoulder";
    private static int _pass, _fail;

    private sealed record Combo(string Name, string[] Held, string Button);

    private static string[] Fire(Combo[] combos, params string[] pressed)
    {
        var down = new HashSet<string>(pressed);
        var allHeld = new HashSet<string>(combos.SelectMany(c => c.Held));
        return combos.Where(c => down.Contains(c.Button) && GamepadComboRules.Satisfied(c.Held, c.Button, allHeld, down.Contains))
            .Select(c => c.Name).ToArray();
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : "  → " + detail));
    }

    private static void Fires(string name, Combo[] combos, string[] pressed, params string[] expected)
    {
        var got = Fire(combos, pressed);
        Check(name, got.OrderBy(s => s).SequenceEqual(expected.OrderBy(s => s)), "得到 [" + string.Join(", ", got) + "]，应为 [" + string.Join(", ", expected) + "]");
    }

    private static int Main()
    {
        var single = new Combo("A", new string[0], A);
        var two = new Combo("LT+A", new[] { LT }, A);
        var three = new Combo("LT+LB+A", new[] { LT, LB }, A);
        var four = new Combo("LT+LB+RB+A", new[] { LT, LB, RB }, A);

        Console.WriteLine("── 单键 / 两键 / 三键");
        Fires("单键：只按 A 触发 A", new[] { single }, new[] { A }, "A");
        Fires("单键：没按 A 不触发", new[] { single }, new[] { B });
        Fires("单键：按着与组合无关的 B 时 A 仍触发（B 不是任何组的先按住键）", new[] { single }, new[] { A, B }, "A");
        Fires("两键：LT+A 触发", new[] { two }, new[] { LT, A }, "LT+A");
        Fires("两键：只按 A 不触发", new[] { two }, new[] { A });
        Fires("两键：只按 LT 不触发", new[] { two }, new[] { LT });
        Fires("三键：LT+LB+A 触发", new[] { three }, new[] { LT, LB, A }, "LT+LB+A");
        Fires("三键：少一个先按住键不触发", new[] { three }, new[] { LT, A });
        Fires("四键：LT+LB+RB+A 触发", new[] { four }, new[] { LT, LB, RB, A }, "LT+LB+RB+A");

        Console.WriteLine("── 互相包含（按住了别组的先按住键时不触发）");
        var nested = new[] { single, two, three };
        Fires("A / LT+A / LT+LB+A 并存：只按 A → 只触发 A", nested, new[] { A }, "A");
        Fires("并存：LT+A → 只触发 LT+A（单键 A 被屏蔽）", nested, new[] { LT, A }, "LT+A");
        Fires("并存：LT+LB+A → 只触发三键（两键、单键被屏蔽）", nested, new[] { LT, LB, A }, "LT+LB+A");
        Fires("并存：LB+A → 都不触发（LB 是三键组的先按住键，单键 A 让位；两键 / 三键不满足）", nested, new[] { LB, A });
        Fires("不同最后键：LT+A 与 LT+X 互不影响", new[] { two, new Combo("LT+X", new[] { LT }, X) }, new[] { LT, X }, "LT+X");
        var ltSingle = new Combo("LT", new string[0], LT);
        Fires("现状记录：单键 LT 与 LT+A 并存，按 LT+A 时两组都触发（LT 是自己的最后键，不算「别组的先按住键」）",
            new[] { ltSingle, two }, new[] { LT, A }, "LT", "LT+A");

        Console.WriteLine("── 按下顺序（只屏蔽最后一个键，且最后一个键晚于先按住键）");
        Func<string, int?> since(Dictionary<string, int> map) => h => map.TryGetValue(h, out int f) ? f : null;
        Check("先按 LT（第 10 帧）再按 A（第 12 帧）→ 屏蔽 A", GamepadComboRules.MaskLastButton(two.Held, 12, since(new() { [LT] = 10 })));
        Check("同一帧按下 → 屏蔽", GamepadComboRules.MaskLastButton(two.Held, 10, since(new() { [LT] = 10 })));
        Check("先按住 A（第 8 帧）再按 LT（第 10 帧）→ 不屏蔽（例：按着 RT 攀爬再按 LT，游戏不能丢 RT）",
            !GamepadComboRules.MaskLastButton(two.Held, 8, since(new() { [LT] = 10 })));
        Check("三键：LT 早、LB 晚于 A → 不屏蔽", !GamepadComboRules.MaskLastButton(three.Held, 12, since(new() { [LT] = 5, [LB] = 15 })));
        Check("三键：LT、LB 都早于 A → 屏蔽", GamepadComboRules.MaskLastButton(three.Held, 12, since(new() { [LT] = 5, [LB] = 11 })));
        Check("单键组从不屏蔽（游戏照常读到这个键）", !GamepadComboRules.MaskLastButton(single.Held, 12, since(new())));
        Check("没记到最后键的按下帧 → 照常屏蔽", GamepadComboRules.MaskLastButton(two.Held, null, since(new() { [LT] = 10 })));

        Console.WriteLine("── 旧格式 modifier 并入 held / 读文件整理");
        static bool Same(List<string> got, params string[] want) => got.SequenceEqual(want);
        Check("只有 modifier（IGMM 旧格式）→ held = [LT]", Same(GamepadComboRules.NormalizeHeld(null, LT, A), LT));
        Check("modifier + held → modifier 放最前", Same(GamepadComboRules.NormalizeHeld(new() { RB }, LT, A), LT, RB));
        Check("modifier 已在 held 里 → 不重复", Same(GamepadComboRules.NormalizeHeld(new() { RB, LT }, LT, A), RB, LT));
        Check("modifier 与最后一个键相同 → 去掉", Same(GamepadComboRules.NormalizeHeld(null, A, A)));
        Check("空 modifier → held 不变", Same(GamepadComboRules.NormalizeHeld(new() { LB }, "", A), LB));
        Check("未知键名、重复键 → 去掉", Same(GamepadComboRules.NormalizeHeld(new() { "bogus", LB, LB }, null, A), LB));
        Check("四个键（3 先按住 + 1）可用", GamepadComboRules.IsUsable("id", A, 3));
        Check("五个键超出上限 → 丢弃", !GamepadComboRules.IsUsable("id", A, 4));
        Check("最后一个键不认识 → 丢弃", !GamepadComboRules.IsUsable("id", "bogus", 0));
        Check("没有 id → 丢弃", !GamepadComboRules.IsUsable("", A, 0));
        Check("同一组键先按住的顺序不同 → 视为同一组（SetKey 相同）",
            GamepadComboRules.SetKey(new[] { LB, LT }, A) == GamepadComboRules.SetKey(new[] { LT, LB }, A));
        Check("最后一个键不同 → 不同组", GamepadComboRules.SetKey(new[] { LT }, A) != GamepadComboRules.SetKey(new[] { A }, LT));

        // 1.1.0 上线前修正（2026-11-16）：概览页手柄（MCE src/Overview/OverviewPadSteps.cs，run.py 一并复制）。
        // 页签一格一格走：模组列表(0) → 快捷键设置(1) → 模组设置(2)，到头不绕回（实测修正后由 TabFocusStep 调用，只移焦点）。
        Check("页签：模组列表 + RB → 快捷键设置", OverviewPadSteps.TabStep(OverviewPadSteps.ModsTab, 1) == OverviewPadSteps.HotkeysTab);
        Check("页签：快捷键设置 + RB → 模组设置", OverviewPadSteps.TabStep(OverviewPadSteps.HotkeysTab, 1) == OverviewPadSteps.SettingsTab);
        Check("页签：快捷键设置 + LB → 模组列表", OverviewPadSteps.TabStep(OverviewPadSteps.HotkeysTab, -1) == OverviewPadSteps.ModsTab);
        Check("页签：模组列表 + LB → 不动（不绕到模组设置、不跳出本页）", OverviewPadSteps.TabStep(OverviewPadSteps.ModsTab, -1) == -1);
        Check("页签：模组设置 + RB → 不动", OverviewPadSteps.TabStep(OverviewPadSteps.SettingsTab, 1) == -1);
        Check("页签：步长 0 / 越界 → 不动", OverviewPadSteps.TabStep(1, 0) == -1 && OverviewPadSteps.TabStep(5, 1) == -1 && OverviewPadSteps.TabStep(-1, 1) == -1);
        // LB / RB 在工具行跳：焦点不在工具行时 RB 到最左、LB 到最右；在工具行里左右走、到头绕回。
        Check("工具行：不在工具行 + RB → 最左", OverviewPadSteps.ToolbarStep(-1, 1, 7) == 0);
        Check("工具行：不在工具行 + LB → 最右", OverviewPadSteps.ToolbarStep(-1, -1, 7) == 6);
        Check("工具行：中间 + RB → 右一个", OverviewPadSteps.ToolbarStep(2, 1, 7) == 3);
        Check("工具行：最右 + RB → 绕回最左", OverviewPadSteps.ToolbarStep(6, 1, 7) == 0);
        Check("工具行：最左 + LB → 绕回最右", OverviewPadSteps.ToolbarStep(0, -1, 7) == 6);
        Check("工具行：没有按钮 → -1", OverviewPadSteps.ToolbarStep(-1, 1, 0) == -1);
        Check("工具行：只有一个按钮 → 停在它上面", OverviewPadSteps.ToolbarStep(0, 1, 1) == 0 && OverviewPadSteps.ToolbarStep(-1, -1, 1) == 0);
        // 防连发（RT + L3 全部展开 / 收起）。
        Check("防连发：没做过 → 放行", OverviewPadSteps.Cooled(10f, -1f, .3f));
        Check("防连发：0.1 秒内再按 → 挡住", !OverviewPadSteps.Cooled(10.1f, 10f, .3f));
        Check("防连发：过了间隔 → 放行", OverviewPadSteps.Cooled(10.31f, 10f, .3f));
        Check("防连发：时间倒退 → 放行（不会永远卡住）", OverviewPadSteps.Cooled(5f, 10f, .3f));

        // 1.1.0 实测修正（2026-11-16）：RT + LB / RB 只把焦点移到页签按钮上（TabFocusStep），按 A 才切换。
        const int M = OverviewPadSteps.ModsTab, H = OverviewPadSteps.HotkeysTab, S = OverviewPadSteps.SettingsTab;
        Check("页签焦点：焦点在列表内容（当前模组列表）+ RB → 快捷键设置按钮", OverviewPadSteps.TabFocusStep(-1, M, 1) == H);
        Check("页签焦点：焦点在列表内容（当前模组列表）+ LB → 停在模组列表按钮（没有更左的）", OverviewPadSteps.TabFocusStep(-1, M, -1) == M);
        Check("页签焦点：焦点在快捷键页内容 + RB → 模组设置按钮（只移焦点，不跳设置页）", OverviewPadSteps.TabFocusStep(-1, H, 1) == S);
        Check("页签焦点：焦点在快捷键页内容 + LB → 模组列表按钮", OverviewPadSteps.TabFocusStep(-1, H, -1) == M);
        Check("页签焦点：已在模组列表按钮 + RB → 快捷键设置按钮", OverviewPadSteps.TabFocusStep(M, M, 1) == H);
        Check("页签焦点：已在快捷键设置按钮 + RB → 模组设置按钮", OverviewPadSteps.TabFocusStep(H, M, 1) == S);
        Check("页签焦点：已在模组设置按钮 + RB → 不动（到头不绕回）", OverviewPadSteps.TabFocusStep(S, M, 1) == -1);
        Check("页签焦点：已在模组列表按钮 + LB → 不动", OverviewPadSteps.TabFocusStep(M, H, -1) == -1);
        Check("页签焦点：步长 0 → 不动", OverviewPadSteps.TabFocusStep(-1, M, 0) == -1);
        // 十字键 / 左摇杆按住连发（RepeatCount）：先等 0.4 秒，之后每 0.08 秒一次。
        Check("连发：按住 0.3 秒 → 还没连发", OverviewPadSteps.RepeatCount(.3f, .4f, .08f) == 0);
        Check("连发：刚到 0.4 秒 → 第 1 次", OverviewPadSteps.RepeatCount(.4f, .4f, .08f) == 1);
        Check("连发：0.5 秒 → 第 2 次", OverviewPadSteps.RepeatCount(.5f, .4f, .08f) == 2);
        Check("连发：1.2 秒 → 11 次", OverviewPadSteps.RepeatCount(1.2f, .4f, .08f) == 11);
        Check("连发：间隔 ≤ 0 → 不连发（防除零）", OverviewPadSteps.RepeatCount(5f, .4f, 0f) == 0);

        // 1.1.0 实测修正（2026-11-16）：分组的纯逻辑（MCE src/Overview/GroupSteps.cs，run.py 一并复制）。
        // 分组标题拖放落点：上 1/4 = 前面（同层），下 1/4 = 后面（同层），中间 = 子分组。
        Check("落点：标题最上方 → 前面", GroupSteps.Zone(0f) == GroupSteps.DropZone.Before);
        Check("落点：上 1/5 → 前面", GroupSteps.Zone(.2f) == GroupSteps.DropZone.Before);
        Check("落点：正中 → 子分组", GroupSteps.Zone(.5f) == GroupSteps.DropZone.Inside);
        Check("落点：刚好 1/4、3/4 → 子分组（边界归中间）", GroupSteps.Zone(.25f) == GroupSteps.DropZone.Inside && GroupSteps.Zone(.75f) == GroupSteps.DropZone.Inside);
        Check("落点：下 1/5 → 后面", GroupSteps.Zone(.8f) == GroupSteps.DropZone.After);
        Check("落点：换算失败（NaN）→ 子分组", GroupSteps.Zone(float.NaN) == GroupSteps.DropZone.Inside);
        // 新分组预填名称：数字从 1 递增，避开已有名称。
        Check("预填名：没有分组 → 新分组1", GroupSteps.NumberedName("新分组", new string[0]) == "新分组1");
        Check("预填名：已有 新分组1 → 新分组2", GroupSteps.NumberedName("新分组", new[] { "新分组1", "别的" }) == "新分组2");
        Check("预填名：1、3 已占 → 取空着的 2", GroupSteps.NumberedName("新分组", new[] { "新分组1", "新分组3" }) == "新分组2");
        Check("预填名：英文忽略大小写与前后空白", GroupSteps.NumberedName("New subgroup ", new[] { " new subgroup 1 ", null }) == "New subgroup 2");
        Check("预填名：子分组与分组各自编号", GroupSteps.NumberedName("新子分组", new[] { "新分组1", "新分组2" }) == "新子分组1");
        // 成员排序键：子分组（更深）的成员排在父分组直属成员前面；树序靠后的子树整体靠后。
        Check("排序：同一子树末尾时，更深的分组成员在前", GroupSteps.MemberRank(1, 2, 3) < GroupSteps.MemberRank(1, 1, 3));
        Check("排序：第三层 < 第二层 < 第一层", GroupSteps.MemberRank(4, 3, 3) < GroupSteps.MemberRank(4, 2, 3) && GroupSteps.MemberRank(4, 2, 3) < GroupSteps.MemberRank(4, 1, 3));
        Check("排序：父分组直属成员排在下一个同层分组的成员前面", GroupSteps.MemberRank(1, 1, 3) < GroupSteps.MemberRank(2, 1, 3));
        Check("排序：未知分组（-1）排最后", GroupSteps.MemberRank(-1, 1, 3) == int.MaxValue);

        // IGMM 0.0.2：旧版 gamepad-combos.json（全局唯一修饰键，没有 version 字段）的迁移，照旧版 Load 的实际效果。
        Console.WriteLine("── IGMM 旧格式迁移（LegacyModifier）");
        const string Back = "select";
        Check("旧：条目 modifier = null（1.1.2）、顶层 Back → 共用修饰键 Back", GamepadComboRules.LegacyModifier(null, Back, A) == Back);
        Check("旧：条目 LT、顶层 Back → 换成共用的 Back（旧版显示与触发都用共用修饰键）", GamepadComboRules.LegacyModifier(LT, Back, A) == Back);
        Check("旧：条目空（单键）→ 仍是单键", GamepadComboRules.LegacyModifier("", Back, A) == "");
        Check("旧：顶层「无」→ 组合全变单键", GamepadComboRules.LegacyModifier(LT, "", A) == "");
        Check("旧：顶层缺失 → 旧版默认 Back", GamepadComboRules.LegacyModifier(LT, null, A) == Back);
        Check("旧：顶层不认识 → 旧版默认 Back", GamepadComboRules.LegacyModifier(LT, "bogus", A) == Back);
        Check("旧：共用修饰键与最后一个键相同 → 丢掉（旧版也丢）", GamepadComboRules.LegacyModifier(LT, Back, Back) == null);
        Check("旧：条目修饰键不认识 → 丢掉", GamepadComboRules.LegacyModifier("bogus", Back, A) == null);
        Check("迁移后并入 held 最前面：LT + A（顶层 LT）→ held = [LT]", Same(GamepadComboRules.NormalizeHeld(new(), GamepadComboRules.LegacyModifier(LT, LT, A), A), LT));
        Check("迁移后 SetKey 与新录的同一组相同（旧 LT+A 与新录 LT+A 视为同一组）",
            GamepadComboRules.SetKey(GamepadComboRules.NormalizeHeld(null, GamepadComboRules.LegacyModifier(LT, LT, A), A), A) == GamepadComboRules.SetKey(new[] { LT }, A));
        Check("方案预设（单个 modifier）同样并入：RB + X → held = [RB]", Same(GamepadComboRules.NormalizeHeld(new(), RB, X), RB));

        Console.WriteLine($"通过 {_pass}，失败 {_fail}");
        return _fail == 0 ? 0 : 1;
    }
}
