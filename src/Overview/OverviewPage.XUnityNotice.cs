#nullable enable
using System;
using System.Linq;
using UnityEngine.EventSystems;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 2.5.0：XUnity 兼容提醒。翻译已由本管理器自己完成；XUnity 若开着在线机翻，会把屏幕上的英文发到网上翻译，
/// 和本管理器的译文混在一起。每次启动第一次进概览时提醒一次（智能分组弹窗优先），可一键关掉、或不再提醒。
/// 不拦截任何操作；只装了 XUnity 没开机翻时不提醒。
/// </summary>
internal sealed partial class OverviewPage
{
    private static bool _xunityChecked;

    private void MaybeWarnXUnity()
    {
        if (_xunityChecked || OverviewUi.HasModal) return;
        _xunityChecked = true;
        if (!Plugin.WarnXUnity.Value) return;
        var endpoints = XUnityBridge.OnlineEndpoints();
        if (endpoints.Count == 0) return;
        string services = string.Join(" / ", endpoints.Distinct(StringComparer.OrdinalIgnoreCase));
        string body = Loc.F(
            "XUnity AutoTranslator is installed with online machine translation on ({0}).\n\nIt sends English text on screen to an online service and replaces it with the machine result. Mod names, settings and descriptions are already translated by this manager's language files, so the two will mix: some texts show our translation, others a machine one, and the result can change between launches.\n\n\"Turn off its machine translation\" backs up AutoTranslatorConfig.ini and clears its Endpoint / FallbackEndpoint lines; it takes effect next launch. Until you restart, don't change XUnity's own settings (its overlay or hotkeys) — XUnity would write its old settings back. XUnity's own dictionaries keep working.",
            "检测到 XUnity AutoTranslator 开着在线机翻（{0}）。\n\n它会把屏幕上的英文发到在线服务，换成机翻结果。模组名、设置项和说明已经由本管理器的语言文件翻译，两边会混在一起：有的显示我们的译文，有的是机翻，每次启动还可能不一样。\n\n「关闭它的在线机翻」会先备份 AutoTranslatorConfig.ini，再清空它的 Endpoint / FallbackEndpoint 两行，下次启动生效。重启之前别动 XUnity 自己的设置（它的面板或快捷键），否则它会把旧设置写回去。XUnity 自己的词典照常可用。",
            "偵測到 XUnity AutoTranslator 開著線上機翻（{0}）。\n\n它會把螢幕上的英文送到線上服務，換成機翻結果。模組名、設定項和說明已經由本管理器的語言檔案翻譯，兩邊會混在一起：有的顯示我們的譯文，有的是機翻，每次啟動還可能不一樣。\n\n「關閉它的線上機翻」會先備份 AutoTranslatorConfig.ini，再清空它的 Endpoint / FallbackEndpoint 兩行，下次啟動生效。重新啟動之前別動 XUnity 自己的設定（它的面板或快捷鍵），否則它會把舊設定寫回去。XUnity 自己的詞典照常可用。",
            services);
        var panel = OverviewUi.CreateModalPanel("MCE_XUnityNotice", 820, 520); if (panel == null) return;
        OverviewUi.SaveDialogHeader(panel, Loc.T("XUnity machine translation is on", "XUnity 开着在线机翻", "XUnity 開著線上機翻"),
            Loc.T("Translation is handled by this manager now. Choose what to do with XUnity's machine translation.", "翻译现在由本管理器完成。选择怎么处理 XUnity 的在线机翻。", "翻譯現在由本管理器完成。選擇怎麼處理 XUnity 的線上機翻。"));
        OverviewUi.SaveDialogBody(panel, body, attention: true);
        var off = OverviewUi.SaveDialogAction(panel, Loc.T("Turn off its machine translation", "关闭它的在线机翻", "關閉它的線上機翻"), () =>
        {
            try
            {
                string backup = XUnityBridge.DisableMachineTranslation();
                OverviewUi.CloseModal();
                SetStatus(Loc.F("XUnity machine translation turned off; restart the game to apply (don't change XUnity's settings before that). Backup: {0}", "已关闭 XUnity 在线机翻，重启游戏后生效（重启前别动 XUnity 的设置）。备份：{0}", "已關閉 XUnity 線上機翻，重新啟動遊戲後生效（重新啟動前別動 XUnity 的設定）。備份：{0}", backup));
            }
            catch (Exception ex)
            {
                OverviewUi.CloseModal();
                SetStatus(Loc.F("Could not change AutoTranslatorConfig.ini: {0}", "无法修改 AutoTranslatorConfig.ini：{0}", "無法修改 AutoTranslatorConfig.ini：{0}", ex.Message));
            }
        }, 20, 250, UiActionRole.Commit);
        OverviewUi.SaveDialogAction(panel, Loc.T("Don't remind again", "不再提醒", "不再提醒"), () => { Plugin.WarnXUnity.Value = false; OverviewUi.CloseModal(); }, 290, 140, UiActionRole.Neutral);
        var later = OverviewUi.SaveDialogAction(panel, Loc.T("Got it", "知道了", "知道了"), OverviewUi.CloseModal, 450, 110, UiActionRole.Neutral);
        EventSystem.current?.SetSelectedGameObject(later.gameObject);
    }
}
