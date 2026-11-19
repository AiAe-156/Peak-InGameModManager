#nullable enable
using ModConfigEnhance.Updating;
using UnityEngine.EventSystems;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.0.0 拆分：简化版「ModConfig Enhance」与本管理器同时启用时提醒一次。
/// 0.0.2（2026-11-19，用户决定）：不再只提示——开机检查 <see cref="LegacyMceGuard"/> 已经把它停用（下次启动生效），这里告诉玩家结果；
/// 没能自动停用（例如配置了 r2modman 档案）时说明原因，请玩家自己停用。每次启动第一次进概览时提醒；智能分组与 XUnity 提醒在前，它们开着就留到下次进入。
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
        bool disabled = LegacyMceGuard.Result == LegacyMceGuard.Outcome.Disabled;
        var panel = OverviewUi.CreateModalPanel("IGMM_LegacyMceNotice", 760, 400); if (panel == null) return;
        OverviewUi.SaveDialogHeader(panel, disabled
                ? Loc.T("ModConfig Enhance has been disabled", "已停用 ModConfig Enhance", "已停用 ModConfig Enhance")
                : Loc.T("ModConfig Enhance is also enabled", "同时启用了 ModConfig Enhance", "同時啟用了 ModConfig Enhance"),
            Loc.T("This manager already includes everything ModConfig Enhance does.", "本管理器已包含 ModConfig Enhance 的全部功能。", "本管理器已包含 ModConfig Enhance 的全部功能。"));
        string body = disabled
            ? Loc.T(
                "It was loaded this launch, so the manager has disabled it; this takes effect after you restart the game. Until then the mod settings page may show the same changes twice (two sidebars, duplicated buttons).\n\nWhile this manager is installed, nothing re-enables ModConfig Enhance: profiles, modpacks and imports that list it as enabled are applied with it disabled.",
                "它这次被加载了，管理器已把它停用，重启游戏后生效。重启前模组设置页可能出现重复的改动（两套侧栏、重复的按钮）。\n\n本管理器在场时，任何操作都不会重新启用 ModConfig Enhance：方案、整合包、导入里记为启用的，应用时一律按停用处理。",
                "它這次被載入了，管理器已將它停用，重新啟動遊戲後生效。重新啟動前模組設定頁可能出現重複的變更（兩套側欄、重複的按鈕）。\n\n本管理器在場時，任何操作都不會重新啟用 ModConfig Enhance：方案、整合包、匯入裡記為啟用的，套用時一律按停用處理。")
            : Loc.T(
                "Both change the mod settings page, so running them together applies the same changes twice. The manager could not disable it automatically: ",
                "两者都会改模组设置页，一起开会把同样的改动叠两遍。管理器没能自动停用它：",
                "兩者都會改模組設定頁，一起開會把同樣的變更疊兩遍。管理器沒能自動停用它：") + LegacyMceGuard.Detail
              + Loc.T("\n\nDisable ModConfig Enhance yourself, then restart the game.", "\n\n请自行停用 ModConfig Enhance，然后重启游戏。", "\n\n請自行停用 ModConfig Enhance，然後重新啟動遊戲。");
        OverviewUi.SaveDialogBody(panel, body, attention: true);
        var ok = OverviewUi.SaveDialogAction(panel, Loc.T("Got it", "知道了", "知道了"), OverviewUi.CloseModal, 20, 140, UiActionRole.Neutral);
        EventSystem.current?.SetSelectedGameObject(ok.gameObject);
    }
}
