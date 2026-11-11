#nullable enable
using ModConfigEnhance.Updating;
using UnityEngine.EventSystems;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.0.0 拆分：简化版「ModConfig Enhance」与本管理器同时启用时提醒一次（只提示，不拦截）。
/// 两者都会改模组设置页，同开会重复叠加。每次启动第一次进概览时提醒；智能分组与 XUnity 提醒在前，它们开着就留到下次进入。
/// </summary>
internal sealed partial class OverviewPage
{
    private static bool _legacyChecked;

    /// <summary>简化版 MCE 用旧 GUID，BepInEx 加载了它就在 PluginInfos 里。</summary>
    internal static bool LegacyMceLoaded => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(ManagerIdentity.LegacyGuid);

    private void MaybeWarnLegacyMce()
    {
        if (_legacyChecked || OverviewUi.HasModal) return;
        _legacyChecked = true;
        if (!LegacyMceLoaded) return;
        var panel = OverviewUi.CreateModalPanel("IGMM_LegacyMceNotice", 760, 380); if (panel == null) return;
        OverviewUi.SaveDialogHeader(panel, Loc.T("ModConfig Enhance is also enabled", "同时启用了 ModConfig Enhance", "同時啟用了 ModConfig Enhance"),
            Loc.T("This manager already includes everything ModConfig Enhance does.", "本管理器已包含 ModConfig Enhance 的全部功能。", "本管理器已包含 ModConfig Enhance 的全部功能。"));
        OverviewUi.SaveDialogBody(panel, Loc.T(
            "Both change the mod settings page, so running them together applies the same changes twice (two sidebars, duplicated buttons). Keep only one: disable ModConfig Enhance in the mod list, then restart the game.\n\nNothing is blocked; this reminder appears once per launch.",
            "两者都会改模组设置页，一起开会把同样的改动叠两遍（两套侧栏、重复的按钮）。建议只保留一个：在模组列表里停用 ModConfig Enhance，然后重启游戏。\n\n不会阻止任何操作；每次启动提醒一次。",
            "兩者都會改模組設定頁，一起開會把同樣的改動疊兩遍（兩套側欄、重複的按鈕）。建議只保留一個：在模組列表裡停用 ModConfig Enhance，然後重新啟動遊戲。\n\n不會阻止任何操作；每次啟動提醒一次。"), attention: true);
        var ok = OverviewUi.SaveDialogAction(panel, Loc.T("Got it", "知道了", "知道了"), OverviewUi.CloseModal, 20, 140, UiActionRole.Neutral);
        EventSystem.current?.SetSelectedGameObject(ok.gameObject);
    }
}
