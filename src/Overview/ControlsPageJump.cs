#nullable enable
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
            // Its own OnEnable (declared on PauseMenuControlsPage); OnPageEnter lives on the shared base and would patch every page.
            var enable = page != null ? AccessTools.DeclaredMethod(page, "OnEnable") : null;
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
