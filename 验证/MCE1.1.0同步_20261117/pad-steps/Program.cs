#nullable enable
using System;
using ModConfigEnhance.Overview;

// MCE 1.1.0 同步（2026-11-17）：概览页手柄与分组的纯逻辑夹具。被测代码 = IGMM src/Overview/OverviewPadSteps.cs 与 GroupSteps.cs
// （run.py 复制进暂存目录一起编译，不是复刻）。检查项照 MCE 验证/快捷键页_20261115/combo-checks 的同名部分，
// 页签部分改成 IGMM 的写法：页签数随「介绍」页签显隐（2 或 3 个），调用方传 count。
internal static class Program
{
    private static int _pass, _fail;

    private static void Check(string name, bool ok)
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
    }

    private static int Main()
    {
        // RT + LB / RB：只把焦点移到页签按钮上，按 A 才切换。无介绍页签：模组列表(0)、快捷键(1)。
        Check("两页签：焦点在列表内容 + RB → 快捷键按钮", OverviewPadSteps.TabFocusStep(-1, 0, 1, 2) == 1);
        Check("两页签：焦点在列表内容 + LB → 停在模组列表按钮", OverviewPadSteps.TabFocusStep(-1, 0, -1, 2) == 0);
        Check("两页签：焦点在快捷键内容 + RB → 停在快捷键按钮（没有更右的）", OverviewPadSteps.TabFocusStep(-1, 1, 1, 2) == 1);
        Check("两页签：已在快捷键按钮 + RB → 不动（到头不绕回）", OverviewPadSteps.TabFocusStep(1, 0, 1, 2) == -1);
        Check("两页签：已在模组列表按钮 + LB → 不动", OverviewPadSteps.TabFocusStep(0, 1, -1, 2) == -1);
        // 有介绍页签：模组列表(0)、介绍(1)、快捷键(2)。
        Check("三页签：焦点在列表内容 + RB → 介绍按钮", OverviewPadSteps.TabFocusStep(-1, 0, 1, 3) == 1);
        Check("三页签：已在介绍按钮 + RB → 快捷键按钮", OverviewPadSteps.TabFocusStep(1, 0, 1, 3) == 2);
        Check("三页签：焦点在快捷键内容 + LB → 介绍按钮", OverviewPadSteps.TabFocusStep(-1, 2, -1, 3) == 1);
        Check("三页签：当前页签找不到（-1）且焦点不在页签行 → 不动", OverviewPadSteps.TabFocusStep(-1, -1, 1, 3) == -1);
        Check("页签：步长 0 / 没有页签 → 不动", OverviewPadSteps.TabFocusStep(-1, 0, 0, 3) == -1 && OverviewPadSteps.TabFocusStep(-1, 0, 1, 0) == -1);
        // LB / RB 在工具行跳：焦点不在工具行时 RB 到最左、LB 到最右；在工具行里左右走、到头绕回。
        Check("工具行：不在工具行 + RB → 最左", OverviewPadSteps.ToolbarStep(-1, 1, 7) == 0);
        Check("工具行：不在工具行 + LB → 最右", OverviewPadSteps.ToolbarStep(-1, -1, 7) == 6);
        Check("工具行：中间 + RB → 右一个", OverviewPadSteps.ToolbarStep(2, 1, 7) == 3);
        Check("工具行：最右 + RB → 绕回最左", OverviewPadSteps.ToolbarStep(6, 1, 7) == 0);
        Check("工具行：最左 + LB → 绕回最右", OverviewPadSteps.ToolbarStep(0, -1, 7) == 6);
        Check("工具行：没有按钮 → -1", OverviewPadSteps.ToolbarStep(-1, 1, 0) == -1);
        Check("工具行：只有一个按钮 → 停在它上面", OverviewPadSteps.ToolbarStep(0, 1, 1) == 0 && OverviewPadSteps.ToolbarStep(-1, -1, 1) == 0);
        // 防连发（RT + L3 / LT + LB / RB 全部展开 / 收起）。
        Check("防连发：没做过 → 放行", OverviewPadSteps.Cooled(10f, -1f, .3f));
        Check("防连发：0.1 秒内再按 → 挡住", !OverviewPadSteps.Cooled(10.1f, 10f, .3f));
        Check("防连发：过了间隔 → 放行", OverviewPadSteps.Cooled(10.31f, 10f, .3f));
        Check("防连发：时间倒退 → 放行（不会永远卡住）", OverviewPadSteps.Cooled(5f, 10f, .3f));
        // 十字键 / 左摇杆按住连发：先等 0.4 秒，之后每 0.08 秒一次。
        Check("连发：按住 0.3 秒 → 还没连发", OverviewPadSteps.RepeatCount(.3f, .4f, .08f) == 0);
        Check("连发：刚到 0.4 秒 → 第 1 次", OverviewPadSteps.RepeatCount(.4f, .4f, .08f) == 1);
        Check("连发：0.5 秒 → 第 2 次", OverviewPadSteps.RepeatCount(.5f, .4f, .08f) == 2);
        Check("连发：1.2 秒 → 11 次", OverviewPadSteps.RepeatCount(1.2f, .4f, .08f) == 11);
        Check("连发：间隔 ≤ 0 → 不连发（防除零）", OverviewPadSteps.RepeatCount(5f, .4f, 0f) == 0);

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

        Console.WriteLine($"通过 {_pass}，失败 {_fail}");
        return _fail == 0 ? 0 : 1;
    }
}
