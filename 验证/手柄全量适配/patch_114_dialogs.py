# -*- coding: utf-8 -*-
"""1.1.4: controls page "Mod settings" opens the overview keybindings tab; save-check dialog layout + gamepad focus."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', 'src'))

def edit(name, pairs):
    p = os.path.join(SRC, name)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (name, a[:90], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

# --- controls page button ---
p = os.path.join(SRC, 'Overview', 'ControlsPageJump.cs')
io.open(p, 'w', encoding='utf-8', newline='\n').write('''#nullable enable
using System;
using HarmonyLib;
using PEAKLib.UI.Elements;
using UnityEngine;
using UnityEngine.UI;
using Zorro.UI;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.1.4（用户 2026-11-12）：游戏「操作方式」页上 ModConfig 加的「模组设置」按钮，原本打开设置页并筛选成按键；
/// 改为直接打开模组概览的「快捷键设置」页签（所有模组的快捷键都在那一页，含手柄列）。
/// 做法：ModConfig 往暂停菜单的「操作方式」页里建这个按钮后，替换它的点击事件。
/// </summary>
internal static class ControlsPageJump
{
    internal static void Install(Harmony harmony)
    {
        try
        {
            var page = AccessTools.TypeByName("PauseMenuControlsPage");
            var enable = page != null ? AccessTools.Method(page, "OnPageEnter") ?? AccessTools.Method(page, "OnEnable") : null;
            if (enable == null) { Plugin.Log.LogWarning("[操作方式] 找不到 PauseMenuControlsPage，按钮跳转不改。"); return; }
            harmony.Patch(enable, postfix: new HarmonyMethod(typeof(ControlsPageJump), nameof(Postfix)));
        }
        catch (Exception error) { Plugin.Log.LogWarning("[操作方式] 挂载失败：" + error.Message); }
    }

    private static void Postfix(Component __instance)
    {
        try
        {
            var root = __instance.transform.parent != null ? __instance.transform.parent : __instance.transform;
            foreach (var button in root.GetComponentsInChildren<PeakMenuButton>(true))
            {
                if (button == null || button.name != "UI_MainMenuButton_MOD SETTINGS" || button.GetComponent<Rewired>() != null) continue;
                button.gameObject.AddComponent<Rewired>();
                var page = __instance as UIPage;
                button.Button.onClick = new Button.ButtonClickedEvent();
                button.Button.onClick.AddListener(() => OverviewController.OpenHotkeys(page != null ? page : __instance.GetComponentInParent<UIPage>(true)));
            }
        }
        catch (Exception error) { Plugin.Log.LogWarning("[操作方式] 改写按钮失败：" + error.Message); }
    }

    private sealed class Rewired : MonoBehaviour { }
}
''')
edit('Plugin.cs', [('        GamepadCombos.Install(harmony); // 1.1.2: LT + controller button presses a keyboard-only mod hotkey',
                    '        GamepadCombos.Install(harmony); // 1.1.2: LT + controller button presses a keyboard-only mod hotkey\n        Overview.ControlsPageJump.Install(harmony); // 1.1.4: controls page "Mod settings" opens the keybindings tab')])

# --- save-check dialog: buttons below the two-line subtitle, and gamepad focus ---
edit('Overview/OverviewUi.Issues.cs', [
('''        SaveDialogHeader(panel, Loc.T("Review before saving", "保存前检查结果", "儲存前檢查結果"),''',
 '''        // 1.1.4 (user screenshot): the source buttons sat on the subtitle's second line. Header divider moves down and
        // the buttons go between the subtitle and the report.
        SaveDialogHeader(panel, Loc.T("Review before saving", "保存前检查结果", "儲存前檢查結果"),'''),
('''        SaveDialogBody(panel, report, attention: true, formatted: true, horizontalReport: true);''',
 '''        bool sourceButtons = retrySources != null && ConfigurationIssueReport.Unidentified(mods).Count > 0;
        SaveDialogBody(panel, report, attention: true, formatted: true, horizontalReport: true, top: sourceButtons ? 148 : 0);'''),
('''            Place((RectTransform)retry.transform, 24, 77, 148, 28);''', '''            Place((RectTransform)retry.transform, 24, 108, 148, 30);'''),
('''                Place((RectTransform)manual.transform, 182, 77, 130, 28);''', '''                Place((RectTransform)manual.transform, 182, 108, 130, 30);'''),
('''        SaveDialogAction(panel, Loc.T("Back", "返回编辑", "返回編輯"), CloseModal, 210, 128, UiActionRole.Neutral);
    }''', '''        var back = SaveDialogAction(panel, Loc.T("Back", "返回编辑", "返回編輯"), CloseModal, 210, 128, UiActionRole.Neutral);
        // Gamepad: start on "Back" (safe), with the dialog's buttons reachable by left/right.
        if (GamepadSupport.IsGamepad) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);
    }'''),
])
edit('Overview/OverviewUi.SaveDialogs.cs', [
('''    internal static void SaveDialogBody(RectTransform panel, string body, bool attention = false, bool formatted = false, bool horizontalReport = false)''',
 '''    internal static void SaveDialogBody(RectTransform panel, string body, bool attention = false, bool formatted = false, bool horizontalReport = false, float top = 0)'''),
])
print('ok')
