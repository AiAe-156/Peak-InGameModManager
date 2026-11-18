#nullable enable
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.1.0 概览页的手柄部分：View 显示 / 隐藏按键提示条。提示条只在手柄态显示，内容随焦点所在的格子变化。
/// 同步自 MCE 1.1.0（上线前修正与实测修正，用户 2026-11-16）：
///   · LB / RB：焦点在第二行工具行的按钮间跳（屏幕从左到右，到头绕回；焦点不在工具行时 RB 到最左、LB 到最右）。
///     管理中心与更新队列开着时仍切它们自己的页（IGMM 原样）；LT + LB / RB 仍是全部展开 / 收起（各页自己处理）。
///   · RT + LB / RB：只把焦点在页签按钮（模组列表 / 介绍 / 快捷键设置 / 模组设置）间移动，到头不绕回，按 A 才切换（按钮自己的 onClick）。
///   · 十字键 / 左摇杆按住连发（外壳自己做，不依赖引擎）。
/// </summary>
internal sealed partial class OverviewPage
{
    private RectTransform? _promptBar;
    private TextMeshProUGUI? _promptText;
    private string _promptKey = "";
    private static bool _promptsHidden;

    private void GamepadPageTick()
    {
        bool gamepad = GamepadSupport.IsGamepad && !TypingGuard.Active;
        UpdatePromptBar(gamepad);
        DriveLayerScroll(gamepad);
        if (!gamepad) { ReleaseMoveRepeat(); return; }
        var pad = Gamepad.current; if (pad == null) { ReleaseMoveRepeat(); return; }
        ShoulderTick(pad);
        MoveRepeatTick(pad);
        if (GamepadIntent.Layer) return;
        if (pad.selectButton.wasPressedThisFrame && !pad.rightTrigger.isPressed && _hotkeys?.IsRecording != true) { _promptsHidden = !_promptsHidden; _promptKey = ""; }
    }

    private int _shoulderFrame = -1;
    /// <summary>LB / RB（见类注释）。弹窗、下拉层、录键、打字时不响应；同一帧只处理一次。</summary>
    private void ShoulderTick(Gamepad pad)
    {
        if (_shoulderFrame == Time.frameCount) return; _shoulderFrame = Time.frameCount;
        bool left = pad.leftShoulder.wasPressedThisFrame, right = pad.rightShoulder.wasPressedThisFrame;
        if (left == right) return; // 都没按，或同一帧两个都按（意图不明）
        int step = right ? 1 : -1;
        if (OverviewHotkeyCapture.ConsumedCancelFrame == Time.frameCount || _hotkeys?.IsRecording == true) return;
        if (!GamepadIntent.Layer && pad.leftTrigger.ReadValue() > .5f) return; // LT + LB / RB = collapse / expand all, handled by the page content
        if (_updatesView?.IsOpen == true) { if (!GamepadIntent.Layer && !OverviewUi.HasDialog) _updatesView.StepTab(); return; }
        if (_management?.IsOpen == true) { if (!GamepadIntent.Layer && !OverviewUi.HasDialog) _management.StepTab(step); return; }
        if (OverviewUi.HasModal || WindowPopover.AnyOpen) return;
        var system = EventSystem.current; if (system == null) return;
        var selected = system.currentSelectedGameObject;
        if (GamepadIntent.Layer)
        {
            // RT + LB / RB：只把焦点移到页签按钮之一，按 A 才切换。
            var tabs = new[] { _listTab, _introTab, _hotkeyTab, _settingsTab }.Where(t => t != null && t.gameObject.activeInHierarchy).ToList();
            int focused = selected == null ? -1 : tabs.FindIndex(t => t.gameObject == selected || selected.transform.IsChildOf(t));
            int active = tabs.IndexOf(TabRect(_tab));
            int next = OverviewPadSteps.TabFocusStep(focused, active, step, tabs.Count);
            if (next >= 0) system.SetSelectedGameObject(tabs[next].gameObject);
            return;
        }
        var stops = ToolbarStops();
        int at = selected == null ? -1 : stops.FindIndex(s => s.gameObject == selected || selected.transform.IsChildOf(s.transform));
        int target = OverviewPadSteps.ToolbarStep(at, step, stops.Count);
        if (target >= 0) system.SetSelectedGameObject(stops[target].gameObject);
    }
    private RectTransform TabRect(PageTab tab) => tab == PageTab.Mods ? _listTab : tab == PageTab.Hotkeys ? _hotkeyTab : _introTab;

    // ── 方向键连发（同步自 MCE 1.1.0 实测修正）──────────────────────────
    // 十字键 / 左摇杆按住时焦点不连续移动；连发改由外壳自己做：按下那一下仍由引擎走（各页原有的 OnMove 不变），按住 0.15 秒后
    // 暂停引擎的方向导航（免得引擎的连发若恢复了会一次走两步），0.4 秒起每 0.08 秒对当前焦点发一次与引擎相同的 Move 事件；
    // 松开、换方向、按 RT、推右摇杆、录键时立即交还。导航已被别处暂停（录键、键鼠录制）时不连发。
    private const float MoveRepeatDelay = .4f, MoveRepeatInterval = .08f, MoveRepeatTakeover = .15f;
    private MoveDirection _repeatDirection = MoveDirection.None;
    private float _repeatSince;
    private int _repeatFired;
    private bool _repeatHolding;
    private void MoveRepeatTick(Gamepad pad)
    {
        var direction = HeldDirection(pad);
        var system = EventSystem.current;
        if (direction == MoveDirection.None || system == null || _hotkeys?.IsRecording == true || GamepadIntent.Layer || GamepadIntent.RightStickHeld) { ReleaseMoveRepeat(); return; }
        if (direction != _repeatDirection)
        {
            ReleaseMoveRepeat(); // 新按下或换方向：这一下交给引擎走
            _repeatDirection = direction; _repeatSince = Time.unscaledTime; _repeatFired = 0;
            return;
        }
        float held = Time.unscaledTime - _repeatSince;
        if (!_repeatHolding)
        {
            if (held < MoveRepeatTakeover || !system.sendNavigationEvents) return; // 导航被别处暂停：不接管
            GamepadIntent.HoldNavigation(_frame, true); _repeatHolding = true;
        }
        int due = OverviewPadSteps.RepeatCount(held, MoveRepeatDelay, MoveRepeatInterval);
        if (_repeatFired >= due) return;
        _repeatFired = due; // 一帧最多走一步；掉帧时不补发
        var selected = system.currentSelectedGameObject;
        if (selected == null || !selected.activeInHierarchy) return;
        var data = new AxisEventData(system) { moveDir = direction, moveVector = MoveVector(direction) };
        ExecuteEvents.Execute(selected, data, ExecuteEvents.moveHandler);
    }
    /// <summary>页面被关掉（切到设置页、离开菜单、销毁）时一定交还方向导航，免得停在暂停状态。</summary>
    private void OnDisable() => ReleaseMoveRepeat();
    private void ReleaseMoveRepeat()
    {
        if (_repeatHolding && _frame != null) GamepadIntent.HoldNavigation(_frame, false);
        _repeatHolding = false; _repeatDirection = MoveDirection.None;
    }
    /// <summary>十字键优先；否则左摇杆推过一半时取主轴方向。</summary>
    private static MoveDirection HeldDirection(Gamepad pad)
    {
        if (pad.dpad.up.isPressed) return MoveDirection.Up;
        if (pad.dpad.down.isPressed) return MoveDirection.Down;
        if (pad.dpad.left.isPressed) return MoveDirection.Left;
        if (pad.dpad.right.isPressed) return MoveDirection.Right;
        var stick = pad.leftStick.ReadValue();
        if (stick.sqrMagnitude < .5f * .5f) return MoveDirection.None;
        return Mathf.Abs(stick.x) > Mathf.Abs(stick.y) ? (stick.x > 0 ? MoveDirection.Right : MoveDirection.Left) : (stick.y > 0 ? MoveDirection.Up : MoveDirection.Down);
    }
    private static Vector2 MoveVector(MoveDirection direction) => direction switch
    {
        MoveDirection.Up => Vector2.up, MoveDirection.Down => Vector2.down, MoveDirection.Left => Vector2.left, MoveDirection.Right => Vector2.right, _ => Vector2.zero
    };
    /// <summary>弹窗、管理中心、更新队列开着时，右摇杆滚动它们的内容（列表本身由列表自己处理）。</summary>
    private void DriveLayerScroll(bool gamepad)
    {
        Transform? layer = OverviewUi.TopLayer != null ? OverviewUi.TopLayer.transform
            : _updatesView?.IsOpen == true ? _updatesView.Root : _management?.IsOpen == true ? _management.Root : null;
        if (gamepad && layer != null) GamepadIntent.ScrollWithin(layer);
        GamepadIntent.HoldNavigation(this, gamepad && layer != null && GamepadIntent.RightStickHeld);
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
            // 同步自 MCE 1.1.0：RT + LB / RB 只移焦点到页签按钮，按 A 才切换。
            items.Add(rt + Glyph("leftShoulder") + Glyph("rightShoulder") + " " + Loc.T("Choose tab (A to open)", "选择页签（A 切换）", "選擇頁籤（A 切換）"));
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
        // 同步自 MCE 1.1.0：LB / RB 在工具行按钮间跳（管理中心 / 更新队列开着时仍切它们自己的页）。
        bool subTabs = _management?.IsOpen == true || _updatesView?.IsOpen == true;
        if (where != "modal") items.Add(Glyph("leftShoulder") + Glyph("rightShoulder") + " " + (subTabs ? Loc.T("Tabs", "切页签", "切頁籤") : Loc.T("Toolbar", "工具栏", "工具列")));
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
