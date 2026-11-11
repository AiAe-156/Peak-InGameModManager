#nullable enable
using BepInEx.Configuration;

namespace ModConfigEnhance.Overview;

internal enum OverviewDependencyHintMode { FollowMouse, Fixed }

internal static class OverviewDependencySettings
{
    internal static ConfigEntry<OverviewDependencyHintMode> Mode = null!;
    internal static ConfigEntry<bool> ShowDependencyChains = null!;
    internal static void Bind(ConfigFile cfg)
    {
        Mode = cfg.Bind("模组总览 - 依赖提示", "显示位置", OverviewDependencyHintMode.FollowMouse,
            "FollowMouse: the hint follows the mouse and disappears when it leaves the name, without blocking clicks. Fixed: the hint stays in place; move into it and scroll to read long content. Both modes fit the content and stay within the screen.");
        ShowDependencyChains = cfg.Bind("模组总览 - 依赖提示", "显示完整依赖链", true,
            "On: show up to two dependency levels in name hints; deeper levels are omitted. Off: show all direct dependencies only. Dependants use a compact two-level summary. Omitted branches show an ellipsis. Long hints can be scrolled in either placement mode. Duplicate references are merged and circular branches stop expanding.");
    }
}
