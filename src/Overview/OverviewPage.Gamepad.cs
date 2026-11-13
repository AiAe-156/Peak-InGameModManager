#nullable enable
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.1.0 概览页的手柄部分：LB / RB 切页签（模组列表 / 介绍 / 快捷键；管理中心与更新队列里切它们自己的页），
/// View 显示 / 隐藏按键提示条。提示条只在手柄态显示，内容随焦点所在的格子变化。
/// </summary>
internal sealed partial class OverviewPage
{
    private RectTransform? _promptBar;
    private TextMeshProUGUI? _promptText;
    private string _promptKey = "";
    private static bool _promptsHidden;
    private InputAction? _tabLeft, _tabRight;
    private bool _tabActionsResolved;

    private void GamepadPageTick()
    {
        bool gamepad = GamepadSupport.IsGamepad && !TypingGuard.Active;
        UpdatePromptBar(gamepad);
        DriveLayerScroll(gamepad);
        if (!gamepad || GamepadIntent.Layer) return;
        var pad = Gamepad.current; if (pad == null) return;
        if (pad.selectButton.wasPressedThisFrame && !pad.rightTrigger.isPressed && _hotkeys?.IsRecording != true) { _promptsHidden = !_promptsHidden; _promptKey = ""; }
        int step = TabStep();
        if (step == 0) return;
        if (pad.leftTrigger.ReadValue() > .5f) return; // LT + LB / RB = collapse / expand all, handled by the page content
        if (OverviewHotkeyCapture.ConsumedCancelFrame == Time.frameCount || _hotkeys?.IsRecording == true) return;
        if (_updatesView?.IsOpen == true) { if (!OverviewUi.HasDialog) _updatesView.StepTab(); return; }
        if (_management?.IsOpen == true) { if (!OverviewUi.HasDialog) _management.StepTab(step); return; }
        if (OverviewUi.HasModal) return;
        StepPageTab(step);
    }
    /// <summary>弹窗、管理中心、更新队列开着时，右摇杆滚动它们的内容（列表本身由列表自己处理）。</summary>
    private void DriveLayerScroll(bool gamepad)
    {
        Transform? layer = OverviewUi.TopLayer != null ? OverviewUi.TopLayer.transform
            : _updatesView?.IsOpen == true ? _updatesView.Root : _management?.IsOpen == true ? _management.Root : null;
        if (gamepad && layer != null) GamepadIntent.ScrollWithin(layer);
        GamepadIntent.HoldNavigation(this, gamepad && layer != null && GamepadIntent.RightStickHeld);
    }
    private int TabStep()
    {
        if (!_tabActionsResolved)
        {
            _tabActionsResolved = true;
            try { _tabLeft = InputSystem.actions.FindAction("UITabLeft"); _tabRight = InputSystem.actions.FindAction("UITabRight"); }
            catch (System.Exception error) { Plugin.Log.LogWarning("[手柄] 页签动作解析失败：" + error.Message); }
        }
        if (_tabLeft?.WasPressedThisFrame() == true) return -1;
        if (_tabRight?.WasPressedThisFrame() == true) return 1;
        return 0;
    }
    private void StepPageTab(int step)
    {
        var tabs = new[] { PageTab.Mods, PageTab.Introduction, PageTab.Hotkeys }
            .Where(t => t != PageTab.Introduction || _introTab.gameObject.activeSelf).ToList();
        int index = tabs.IndexOf(_tab);
        var next = tabs[((index < 0 ? 0 : index) + step + tabs.Count) % tabs.Count];
        if (next == _tab) return;
        SwitchTab(next);
        FocusTabContent();
    }
    /// <summary>After a tab switch the focus goes into the new page's content (list row / keybinding), not the tab strip.</summary>
    private void FocusTabContent()
    {
        if (!GamepadSupport.IsGamepad) return;
        if (_tab == PageTab.Mods) _list?.FocusListForGamepad();
        else if (_tab == PageTab.Hotkeys) _hotkeys?.FocusForGamepad();
        var system = EventSystem.current;
        if (system != null && (system.currentSelectedGameObject == null || !system.currentSelectedGameObject.activeInHierarchy))
        {
            var tab = _tab == PageTab.Mods ? _listTab : _tab == PageTab.Hotkeys ? _hotkeyTab : _introTab;
            system.SetSelectedGameObject(tab.gameObject);
        }
    }

    // ── 按键提示条 ─────────────────────────────────────────────

    private bool _promptShown;
    private GameObject? _promptSelected;
    private bool _promptLayer, _promptModal;
    private PageTab _promptTab;
    private int _promptRevision = -1;
    private void UpdatePromptBar(bool gamepad)
    {
        bool show = gamepad && !_promptsHidden && _frame != null && _repo != null;
        if (show != _promptShown) { _promptShown = show; if (_repo != null) RefreshHeader(); } // status line height follows the strip
        if (!show) { if (_promptBar != null) _promptBar.gameObject.SetActive(false); _promptKey = ""; return; }
        if (_promptBar == null)
        {
            _promptBar = OverviewUi.Rect("GamepadPrompts", _frame!);
            _promptText = OverviewUi.Text(_promptBar, "", 16); OverviewUi.Fill(_promptText.rectTransform);
            _promptText.alignment = TextAlignmentOptions.MidlineRight; _promptText.richText = true; _promptText.color = OverviewUi.Ivory;
            _promptText.textWrappingMode = TextWrappingModes.NoWrap; _promptText.overflowMode = TextOverflowModes.Ellipsis; _promptText.raycastTarget = false;
            _promptText.parseCtrlCharacters = false;
        }
        _promptBar.gameObject.SetActive(true);
        OverviewUi.Place(_promptBar, 20, _frame!.rect.height - 30, _frame.rect.width - 40, 26);
        _promptBar.SetAsLastSibling();
        // Rebuild the text only when something it depends on changed (no per-frame string work).
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        bool layer = GamepadIntent.Layer, modal = OverviewUi.HasModal;
        if (_promptKey.Length > 0 && selected == _promptSelected && layer == _promptLayer && modal == _promptModal && _tab == _promptTab && _promptRevision == Loc.TranslationRevision) return;
        _promptSelected = selected; _promptLayer = layer; _promptModal = modal; _promptTab = _tab; _promptRevision = Loc.TranslationRevision;
        string key = PromptKey();
        if (key == _promptKey) return;
        _promptKey = key;
        _promptText!.text = PromptText(key);
    }
    private string PromptKey()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        string where = OverviewUi.HasModal ? "modal" : _tab.ToString();
        string kind = selected == null ? "" : selected.GetComponent<TMP_InputField>() != null ? "input" : selected.GetComponent<Toggle>() != null ? "toggle"
            : selected.GetComponent<OverviewGamepadSubmit>() != null ? "name" : "button";
        return where + "|" + kind + "|" + GamepadIntent.Layer + "|" + Loc.TranslationRevision + "|" + (Gamepad.current?.displayName ?? "");
    }
    private string PromptText(string key)
    {
        var parts = key.Split('|');
        string where = parts[0], kind = parts[1];
        bool listLike = where == PageTab.Mods.ToString() || where == PageTab.Hotkeys.ToString(); // 1.1.4: one scheme
        bool layer = parts[2] == "True" && listLike;
        var items = new System.Collections.Generic.List<string>();
        void Add(string control, string label) => items.Add(Glyph(control) + " " + label);
        if (layer)
        {
            // Shown only while RT is held; each item reads "RT + key".
            string rt = Glyph("rightTrigger") + "+";
            items.Add(rt + Glyph("dpad") + " " + Loc.T("Previous / next group", "上 / 下一个分组", "上 / 下一個分組"));
            items.Add(rt + Glyph("left") + Glyph("right") + " " + Loc.T("Page", "翻页", "翻頁"));
            if (where == PageTab.Mods.ToString()) items.Add(rt + Glyph("buttonNorth") + " " + Loc.T("Group menu", "分组菜单", "分組選單"));
            items.Add(rt + Glyph("leftStickPress") + " " + Loc.T("Fold all groups", "折叠全部分组", "摺疊全部分組"));
            return string.Join("　　", items);
        }
        string confirm = kind == "name" ? Loc.T("Menu", "菜单", "選單") : kind == "toggle" ? Loc.T("Tick", "勾选", "勾選") : kind == "input" ? Loc.T("Type", "输入", "輸入") : Loc.T("Select", "确认", "確認");
        Add("buttonSouth", confirm);
        if (where == PageTab.Mods.ToString() || where == PageTab.Hotkeys.ToString()) Add("buttonNorth", Loc.T("More", "更多", "更多"));
        Add("buttonEast", Loc.T("Back", "返回", "返回"));
        if (where != "modal") { Add("leftShoulder", ""); items[items.Count - 1] = Glyph("leftShoulder") + Glyph("rightShoulder") + " " + Loc.T("Tabs", "切页签", "切頁籤"); }
        if (listLike) Add("rightTrigger", Loc.T("Hold for more", "按住看更多", "按住看更多"));
        if (listLike) Add("leftStickPress", Loc.T("Fold group", "折叠分组", "摺疊分組"));
        Add("select", Loc.T("Hide these hints", "隐藏按键提示", "隱藏按鍵提示"));
        return string.Join("　　", items);
    }
    private string Glyph(string control)
    {
        if (control == "dpad") return Glyph("up") + Glyph("down");
        string glyph = GamepadIntent.Glyph(control, out var sprites);
        if (sprites != null && _promptText != null) { _promptText.spriteAsset = sprites; return glyph; }
        return "<b>" + glyph + "</b>";
    }
}
