#nullable enable
namespace ModConfigEnhance.Overview;

/// <summary>
/// 同步自 MCE 1.1.0（上线前修正，用户 2026-11-16）：概览页手柄的纯逻辑（不碰 Unity），由 OverviewPage.Gamepad 与两个页签调用。
/// MCE 的夹具（ModConfigEnhance/验证/快捷键页_20261115/combo-checks/）链接同名文件做单元检查；IGMM 的检查在 验证/MCE1.1.0同步_20261117/pad-steps/。
/// IGMM 的页签数随「介绍」页签显隐变化，调用方传 count；下面的 TabCount / ModsTab 等常量是 MCE 三页签的写法，IGMM 不用。
/// </summary>
internal static class OverviewPadSteps
{
    /// <summary>页签顺序：模组列表 → 快捷键设置 → 模组设置（最后一个是跳到 ModConfig 的模组设置页）。</summary>
    internal const int TabCount = 3, ModsTab = 0, HotkeysTab = 1, SettingsTab = 2;

    /// <summary>页签一次走一格，到头不绕回。返回 -1 = 不动。实测修正后由 <see cref="TabFocusStep"/> 调用（只移焦点）。</summary>
    internal static int TabStep(int current, int step, int count = TabCount)
    {
        if (count <= 0 || step == 0 || current < 0 || current >= count) return -1;
        int next = current + (step > 0 ? 1 : -1);
        return next >= 0 && next < count ? next : -1;
    }

    /// <summary>
    /// 实测修正（用户 2026-11-16）：RT + LB / RB 只在三个页签按钮之间移动焦点，按 A 才切换（不再直接切页）。
    /// <paramref name="focused"/> = 焦点所在的页签按钮（-1 = 焦点不在页签行），<paramref name="active"/> = 当前显示的页签。
    /// 焦点在页签行：一次走一格，到头不绕回（返回 -1 = 不动）；焦点不在页签行：从当前页签走一格，没有下一格就停在当前页签上。
    /// </summary>
    internal static int TabFocusStep(int focused, int active, int step, int count = TabCount)
    {
        if (count <= 0 || step == 0) return -1;
        if (focused >= 0 && focused < count) return TabStep(focused, step, count);
        if (active < 0 || active >= count) return -1;
        int next = TabStep(active, step, count);
        return next >= 0 ? next : active;
    }

    /// <summary>
    /// 实测修正（用户 2026-11-16：十字键、左摇杆按住不能连续移动）：按住同一方向 <paramref name="heldFor"/> 秒后，
    /// 除了按下那一下以外应当已经连发了几次。先等 <paramref name="delay"/> 秒，之后每 <paramref name="interval"/> 秒一次。
    /// </summary>
    internal static int RepeatCount(float heldFor, float delay, float interval)
    {
        if (heldFor < delay || interval <= 0) return 0;
        return 1 + (int)((heldFor - delay) / interval);
    }

    /// <summary>
    /// LB / RB 在第二行工具行的按钮间跳：焦点不在工具行（<paramref name="current"/> &lt; 0）时 RB 到最左、LB 到最右；
    /// 已在工具行里就左右走，到头绕回（同工具行的十字键左右）。返回 -1 = 工具行没有可用按钮。
    /// </summary>
    internal static int ToolbarStep(int current, int step, int count)
    {
        if (count <= 0 || step == 0) return -1;
        if (current < 0 || current >= count) return step > 0 ? 0 : count - 1;
        return ((current + (step > 0 ? 1 : -1)) % count + count) % count;
    }

    /// <summary>
    /// 防连发：同一个动作两次之间至少隔 <paramref name="gap"/> 秒（RT + L3 全部展开 / 收起）。
    /// <paramref name="last"/> &lt; 0 表示还没做过。时间倒退（换场景后 unscaledTime 不会倒退，但防万一）也放行。
    /// </summary>
    internal static bool Cooled(float now, float last, float gap) => last < 0 || now < last || now - last >= gap;
}
