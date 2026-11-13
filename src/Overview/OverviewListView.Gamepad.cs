#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.1.0 模组列表的手柄操作（用户 2026-11-12 拍板）。鼠标路径不动。
///   · 每一列都是独立的焦点格；选中哪格按 A 就做哪格的事，不设跨列快捷键。
///   · 名称格按 A、任意格按 Y：打开这一行的菜单（代替双击 / 右键；角标对应的动作也在里面）。
///   · 提示框继续用悬浮框，但贴着焦点格弹出、不读鼠标；名称格额外并排一张「角标」卡，和依赖卡一左一右。
///   · 按住 RT：↑↓ 跳到上 / 下一个分组标题，←→ 翻页，Y 分组菜单，L3 全部折叠 / 展开。
///   · L3 折叠 / 展开所在分组；R3 跳到搜索框；右摇杆只滚动，焦点不动。
///   · 作者模式的多选、排序、拖进分组仍用键鼠，这里不做。
/// </summary>
internal sealed partial class OverviewListView
{
    private RectTransform? _chipCard;
    private TextMeshProUGUI? _chipCardText;
    private GameObject? _chipCardSource;
    private RectTransform? _hintAnchor;
    private int _gamepadFrame = -1;
    private int? _preferredColumn;
    private bool _selectingColumn;
    private float[] _columnStarts = new float[8];
    private float? _verticalScrollBeforeMove;

    /// <summary>每帧由列表的 Tick 调用（只在列表可见时）。</summary>
    private void GamepadTick()
    {
        if (_gamepadFrame == Time.frameCount) return; _gamepadFrame = Time.frameCount;
        var root = _root.gameObject;
        if (!GamepadSupport.IsGamepad || OverviewUi.HasModal || _disposed || TypingGuard.Active)
        {
            GamepadIntent.Release(root);
            if (!GamepadSupport.IsGamepad) HideChipCard();
            return;
        }
        GamepadIntent.HoldNavigation(root, GamepadIntent.WantsHold);
        var pad = Gamepad.current; if (pad == null) return;
        RecoverFocus();
        float scroll = GamepadIntent.ScrollY;
        if (Mathf.Abs(scroll) > 0)
        {
            _scroll.StopMovement();
            var position = _content.anchoredPosition;
            float speed = GamepadIntent.ScrollSpeed(650);
            position.y = Mathf.Clamp(position.y - scroll * speed * Time.unscaledDeltaTime, 0, Mathf.Max(0, _content.rect.height - _viewport.rect.height));
            _content.anchoredPosition = position; RefreshVirtualRows();
        }
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        var row = selected != null ? RowOf(selected) : null;
        // LT + RB / LT + LB: expand / collapse all (the page skips its own LB/RB tab switch while LT is held).
        if (pad.leftTrigger.ReadValue() > .5f)
        {
            if (pad.rightShoulder.wasPressedThisFrame) { SetAllFolded(true); return; }
            if (pad.leftShoulder.wasPressedThisFrame) { SetAllFolded(false); return; }
        }
        if (GamepadIntent.Layer)
        {
            switch (GamepadIntent.PressedDirection())
            {
                case MoveDirection.Up: JumpGroup(row, -1); break;
                case MoveDirection.Down: JumpGroup(row, 1); break;
                case MoveDirection.Left: PageBy(row, -1); break;
                case MoveDirection.Right: PageBy(row, 1); break;
            }
            if (pad.buttonNorth.wasPressedThisFrame && row != null && !IsComponentRow(row)) GroupMenuFor(row, selected!);
            if (pad.leftStickButton.wasPressedThisFrame) FoldAll();
            return;
        }
        if (pad.buttonNorth.wasPressedThisFrame && row != null && selected != null)
        {
            if (row.Mod != null) ShowGamepadRowMenu(row.Mod, (RectTransform)selected.transform);
            else if (row.Key.StartsWith("group:", StringComparison.Ordinal)) GroupMenuFor(row, selected); // Y on a group heading = its menu
        }
        if (pad.leftStickButton.wasPressedThisFrame && row != null) FoldGroupOf(row);
        if (pad.rightStickButton.wasPressedThisFrame && _overviewSearch != null && _overviewSearch.gameObject.activeInHierarchy)
            EventSystem.current?.SetSelectedGameObject(_overviewSearch.gameObject);
    }
    /// <summary>Called from the cell's own move handler, before any other navigation: if the focused row has been
    /// scrolled out of view (right stick), the move brings the focus back into view instead of moving from the old row.</summary>
    private bool TryReturnFocusToView(VirtualRow row, Selectable source)
    {
        if (!GamepadSupport.IsGamepad || RowVisible(row)) return false;
        ReturnFocusToView(row, source.gameObject); return true;
    }

    private (string Key, int Column)? _lastGamepadFocus;
    private GameObject? _lastRecoverSelection;
    /// <summary>When the focus is lost (mouse took over and cleared it, back from the settings page, a row rebuilt),
    /// the next gamepad input puts it back on the last row/cell, or the first visible row (user 2026-11-12).</summary>
    private void RecoverFocus()
    {
        var system = EventSystem.current; if (system == null) return;
        var selected = system.currentSelectedGameObject;
        if (selected != null && selected.activeInHierarchy)
        {
            if (selected != _lastRecoverSelection && selected.transform.IsChildOf(_content))
            { var row = RowOf(selected); if (row != null && row.View != null) _lastGamepadFocus = (row.Key, IsNavigationGroup(row) && _preferredColumn.HasValue ? _preferredColumn.Value : ColumnOf(row.View, selected.GetComponent<Selectable>())); }
            _lastRecoverSelection = selected;
            return;
        }
        _lastRecoverSelection = null;
        if (!AnyGamepadPress()) return;
        VirtualRow? target = _lastGamepadFocus.HasValue ? _virtualRows.Find(r => r.Key == _lastGamepadFocus.Value.Key) : null;
        if (target != null && RowVisible(target)) { FocusRow(target, _lastGamepadFocus!.Value.Column); return; }
        float top = _content.anchoredPosition.y;
        var first = _virtualRows.FirstOrDefault(r => r.Y + r.Height > top && r.Key.StartsWith("mod:", StringComparison.Ordinal))
            ?? _virtualRows.FirstOrDefault(r => r.Y + r.Height > top);
        if (first != null) FocusRow(first, first.Key.StartsWith("mod:", StringComparison.Ordinal) ? 1 : 0);
    }
    private static bool AnyGamepadPress()
    {
        var pad = Gamepad.current; if (pad == null) return false;
        return pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame
            || pad.buttonSouth.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame || GamepadIntent.PressedDirection() != MoveDirection.None;
    }
    /// <summary>Puts the focus on the list (the remembered cell, else the first visible row). Used when a tab switch
    /// brings the list back, so the focus does not stay on the tab strip (user 2026-11-12).</summary>
    internal void FocusListForGamepad()
    {
        if (!GamepadSupport.IsGamepad) return;
        VirtualRow? target = _lastGamepadFocus.HasValue ? _virtualRows.Find(r => r.Key == _lastGamepadFocus.Value.Key) : null;
        if (target != null) { FocusRow(target, _lastGamepadFocus!.Value.Column); return; }
        float top = _content.anchoredPosition.y;
        var first = _virtualRows.FirstOrDefault(r => r.Y + r.Height > top && r.Key.StartsWith("mod:", StringComparison.Ordinal));
        if (first != null) FocusRow(first, 1);
    }

    /// <summary>A row's focusable cells, left to right on screen. The tiny status chips in the name lane (and the green
    /// dot in the version cell) are left out: on a gamepad the name cell explains them and its menu holds their actions
    /// (user decision 2026-11-12). Their own navigation is switched off so a move never lands on them.</summary>
    private static Selectable[] RowControls(RectTransform view)
    {
        var all = view.GetComponentsInChildren<Selectable>(false);
        var cells = new List<Selectable>(all.Length);
        foreach (var control in all)
        {
            if (IsChipControl(control)) { if (control.navigation.mode != Navigation.Mode.None) control.navigation = new Navigation { mode = Navigation.Mode.None }; continue; }
            cells.Add(control);
        }
        var corners = new Vector3[4];
        return cells.Select(c => { ((RectTransform)c.transform).GetWorldCorners(corners); return (c, x: corners[0].x); })
            .OrderBy(p => p.x).Select(p => p.c).ToArray();
    }
    private static bool IsChipControl(Selectable control)
    {
        for (var t = control.transform; t != null; t = t.parent)
        {
            if (t.name == "StatusChip" || t.name == "CompatibilityAdvice" || t.name == "NameMarkers" || t.name == "ComponentUpdateMarkers") return true;
            if (t.name.StartsWith("Mod_", StringComparison.Ordinal) || t.name.StartsWith("Group_", StringComparison.Ordinal)) return false;
        }
        return false;
    }

    /// <summary>The interactable cell in <paramref name="cells"/> whose centre is closest in x to <paramref name="from"/>.
    /// Rows differ in cells (the "+" button, missing author column), so a column index alone can land a cell over.</summary>
    private static Selectable? NearestCell(Selectable[] cells, RectTransform from)
    {
        float x = from.TransformPoint(from.rect.center).x; Selectable? best = null; float distance = float.MaxValue;
        foreach (var cell in cells)
        {
            if (!cell.IsInteractable() || !cell.IsActive()) continue;
            var rect = (RectTransform)cell.transform; float d = Mathf.Abs(rect.TransformPoint(rect.rect.center).x - x);
            if (d < distance) { distance = d; best = cell; }
        }
        return best;
    }

    private VirtualRow? RowOf(GameObject selected)
    {
        foreach (var row in _virtualRows) if (row.View != null && selected.transform.IsChildOf(row.View)) return row;
        return null;
    }
    private bool RowVisible(VirtualRow row)
    {
        float top = _content.anchoredPosition.y, bottom = top + _viewport.rect.height;
        return row.Y + row.Height > top && row.Y < bottom;
    }
    private void ReturnFocusToView(VirtualRow from, GameObject selected)
    {
        int column = _preferredColumn ?? (from.View == null ? 1 : ColumnOf(from.View, selected.GetComponent<Selectable>()));
        float top = _content.anchoredPosition.y;
        foreach (var row in _virtualRows) {
            if (row.Y + row.Height <= top || row.View == null || row.Layers != null) continue;
            var pick = ColumnTarget(row, column); if (pick == null) continue;
            SelectColumn(pick, column); return;
        }
    }
    /// <summary>RT+↑↓：跳到上 / 下一个分组标题（焦点落在标题的折叠箭头上）。</summary>
    private void JumpGroup(VirtualRow? from, int step)
    {
        int index = from == null ? (step > 0 ? -1 : _virtualRows.Count) : _virtualRows.IndexOf(from);
        for (int i = index + step; i >= 0 && i < _virtualRows.Count; i += step)
        {
            var candidate = _virtualRows[i];
            if (!candidate.Key.StartsWith("group:", StringComparison.Ordinal) && !candidate.Key.StartsWith("infrastructure-group:", StringComparison.Ordinal)) continue;
            FocusRow(candidate, 0); return;
        }
    }
    /// <summary>RT+←→：按视口高度翻页，焦点跟到新一页里同一列。</summary>
    private void PageBy(VirtualRow? from, int step)
    {
        float viewport = _viewport.rect.height;
        var position = _content.anchoredPosition;
        position.y = Mathf.Clamp(position.y + step * viewport * .9f, 0, Mathf.Max(0, _content.rect.height - viewport));
        _content.anchoredPosition = position; RefreshVirtualRows();
        int column = 0;
        if (from?.View != null && EventSystem.current?.currentSelectedGameObject is GameObject selected)
            column = _preferredColumn ?? ColumnOf(from.View, selected.GetComponent<Selectable>());
        var target = _virtualRows.FirstOrDefault(r => r.Y >= position.y && r.Key.StartsWith("mod:", StringComparison.Ordinal))
            ?? _virtualRows.LastOrDefault(r => r.Key.StartsWith("mod:", StringComparison.Ordinal));
        if (target != null) FocusRow(target, column);
    }
    private void FocusRow(VirtualRow row, int column)
    {
        _verticalScrollBeforeMove = null;
        if (IsNavigationGroup(row) && column == 0) column = 1;
        ScrollToVirtualRow(row);
        var previous = EventSystem.current?.currentSelectedGameObject;
        if (row.View == null || row.Layers != null) { _layerMoveDirection = MoveDirection.None; _layerFocus = (row, column, previous); return; }
        var target = ColumnTarget(row, column);
        if (target != null) SelectColumn(target, column);
    }
    private static bool IsNavigationGroup(VirtualRow row) => row.Key.StartsWith("group:", StringComparison.Ordinal) || row.Key.StartsWith("infrastructure-group:", StringComparison.Ordinal);
    private static int ColumnOf(RectTransform view, Selectable? control)
    {
        if (control == null) return 1;
        var marker = control.GetComponent<OverviewRowColumn>();
        return marker != null ? marker.Column : Math.Max(0, Array.IndexOf(RowControls(view), control));
    }
    private static Selectable? ColumnTarget(VirtualRow row, int column)
    {
        if (row.View == null) return null;
        var controls = RowControls(row.View);
        var columns = controls.Select((c, i) => { var marker = c.GetComponent<OverviewRowColumn>(); return marker == null ? i : marker.Column; }).ToArray();
        int index = ListColumnPolicy.Find(column, columns, controls.Select(c => c.IsActive() && c.IsInteractable()).ToArray(), IsNavigationGroup(row));
        return index < 0 ? null : controls[index];
    }
    private void SelectColumn(Selectable target, int column)
    {
        _preferredColumn = column; _verticalScrollBeforeMove = null; _selectingColumn = true;
        try { EventSystem.current?.SetSelectedGameObject(target.gameObject); }
        finally { _selectingColumn = false; }
    }
    /// <summary>移到焦点所在模组（「跳到外层模组」用）。</summary>
    private void FocusMod(OverviewMod mod)
    {
        var row = _virtualRows.Find(r => ReferenceEquals(r.Mod, mod));
        if (row == null) { _status(Loc.T("That mod is hidden by the current filter or a collapsed group.", "该模组被当前筛选或折叠的分组隐藏了。", "該模組被目前篩選或摺疊的分組隱藏了。")); return; }
        FocusRow(row, 1);
    }
    private static bool IsComponentRow(VirtualRow row) => row.Key.StartsWith("component:", StringComparison.Ordinal) || row.Key.StartsWith("infrastructure-group:", StringComparison.Ordinal);
    private string GroupIdOf(VirtualRow row)
    {
        if (row.Key.StartsWith("group:", StringComparison.Ordinal)) return row.Key.Substring(6);
        return row.Mod != null && Groups.Contains(row.Mod.Rule.groupId) ? row.Mod.Rule.groupId : "";
    }
    private void FoldGroupOf(VirtualRow row)
    {
        if (IsComponentRow(row))
        {
            // Components fold as one block of their own.
            _infrastructureCollapsed = !_infrastructureCollapsed; RenderFold();
            var block = _virtualRows.Find(r => r.Key == "infrastructure-group:"); if (block != null) FocusRow(block, 0);
            return;
        }
        string groupId = GroupIdOf(row);
        if (groupId.Length == 0) return; // "Ungrouped" has no fold
        if (!_repo.State.collapsedGroups.Remove(groupId)) _repo.State.collapsedGroups.Add(groupId);
        _repo.SaveUiState(); _selected.Clear(); _anchor = -1;
        string key = "group:" + groupId;
        RenderFold(); RefreshFoldAllIcon();
        var header = _virtualRows.Find(r => r.Key == key);
        if (header != null) FocusRow(header, 0);
    }
    private Button? _foldAllButton;
    /// <summary>Expand (true) / collapse (false) every group, or flip: expand if anything is folded.</summary>
    internal void SetAllFolded(bool? expand)
    {
        var all = _repo.State.groups.Select(g => g.id).ToList();
        bool open = expand ?? all.Any(_repo.State.collapsedGroups.Contains) || (expand == null && _infrastructureCollapsed);
        _repo.State.collapsedGroups.Clear();
        if (!open) _repo.State.collapsedGroups.AddRange(all);
        _infrastructureCollapsed = !open;
        _repo.SaveUiState(); _selected.Clear(); _anchor = -1; RenderFold(); RefreshFoldAllIcon();
        _status(open ? Loc.T("All groups expanded.", "已展开全部分组。", "已展開全部分組。") : Loc.T("All groups collapsed.", "已收起全部分组。", "已收起全部分組。"));
    }
    private void RefreshFoldAllIcon()
    {
        if (_foldAllButton == null) return;
        bool anyFolded = _repo.State.groups.Any(g => _repo.State.collapsedGroups.Contains(g.id));
        var icon = _foldAllButton.GetComponentInChildren<OverviewIconGraphic>(true);
        if (icon != null) { icon.Kind = anyFolded ? "triangle-right" : "triangle-down"; icon.SetVerticesDirty(); }
    }
    private void FoldAll()
    {
        var all = _repo.State.groups.Select(g => g.id).ToList();
        bool collapse = all.Any(id => !_repo.State.collapsedGroups.Contains(id));
        _repo.State.collapsedGroups.Clear();
        if (collapse) _repo.State.collapsedGroups.AddRange(all);
        _repo.SaveUiState(); _selected.Clear(); _anchor = -1; RenderFold();
        var first = _virtualRows.FirstOrDefault(r => r.Key.StartsWith("group:", StringComparison.Ordinal));
        if (first != null) FocusRow(first, 0);
        _status(collapse ? Loc.T("All groups collapsed.", "已折叠全部分组。", "已摺疊全部分組。") : Loc.T("All groups expanded.", "已展开全部分组。", "已展開全部分組。"));
    }
    private void GroupMenuFor(VirtualRow row, GameObject anchor)
    {
        string id = GroupIdOf(row);
        var group = id.Length == 0 ? null : Groups.Get(id);
        HideAllHints();
        OverviewUi.CompactDropdown((RectTransform)anchor.transform, group == null ? T("Ungrouped", "未分组") : Groups.Path(id), GroupMenuEntries(group));
    }

    // ── 焦点提示 ───────────────────────────────────────────────

    /// <summary>手柄焦点触发的提示锚定在焦点格下沿；鼠标悬停照旧跟随指针。</summary>
    private bool HintFollowsFocus => _hintAnchor != null && _hintAnchor.gameObject.activeInHierarchy && GamepadSupport.IsGamepad;
    private void ShowFocusHint(string text, GameObject target)
    {
        _hintAnchor = (RectTransform)target.transform;
        ShowHint(text);
    }
    /// <summary>焦点格在根下的位置（左上角为原点、y 向下），给提示框定位。</summary>
    private static readonly Vector3[] FocusCorners = new Vector3[4];
    private Vector2 FocusPoint(RectTransform anchor, out float height)
    {
        var corners = FocusCorners; anchor.GetWorldCorners(corners);
        var bottomLeft = _root.InverseTransformPoint(corners[0]); var topLeft = _root.InverseTransformPoint(corners[1]);
        height = topLeft.y - bottomLeft.y;
        return new Vector2(bottomLeft.x - _root.rect.xMin, _root.rect.yMax - bottomLeft.y);
    }

    /// <summary>名称格获得手柄焦点时，在依赖卡右边并排一张「角标」卡，逐条解释这一行的角标。</summary>
    private void ShowChipCard(OverviewMod mod, GameObject source)
    {
        if (!GamepadSupport.IsGamepad || OverviewUi.HasModal) return;
        string text = ChipLegend(mod);
        if (_chipCard == null)
        {
            _chipCard = OverviewUi.Panel("ChipCard", _root.parent, OverviewUi.Brown); HintCanvas.Attach(_chipCard, false);
            _chipCard.GetComponent<Image>().raycastTarget = false;
            _chipCardText = OverviewUi.Text(_chipCard, "", 17); OverviewUi.Fill(_chipCardText.rectTransform, 12);
            _chipCardText.richText = true; _chipCardText.textWrappingMode = TextWrappingModes.Normal; _chipCardText.raycastTarget = false;
        }
        _chipCardSource = source;
        _chipCardText!.text = text;
        float width = 330, height = Mathf.Clamp(_chipCardText.GetPreferredValues(text, width - 24, 0).y + 24, 44, 260);
        _chipCard.sizeDelta = new Vector2(width, height);
        _chipCard.gameObject.SetActive(true); _chipCard.SetAsLastSibling();
        PositionChipCard();
    }
    private void PositionChipCard()
    {
        if (_chipCard == null || _chipCardSource == null) return;
        var parent = (RectTransform)_chipCard.parent;
        float w = _chipCard.sizeDelta.x, h = _chipCard.sizeDelta.y, x, y;
        if (_dependencyHints.CardBox(_chipCardSource, out var card, out var size))
        {
            // Side by side with the dependency card; flip to its left when the right edge would clip.
            x = card.x + size.x + 10; y = card.y;
            if (x + w > parent.rect.width - 8) x = card.x - w - 10;
            // No room on either side (narrow window): stack it under the dependency card instead of overlapping.
            if (x < 8) { x = card.x; y = card.y + size.y + 8; }
        }
        else
        {
            var corners = new Vector3[4]; ((RectTransform)_chipCardSource.transform).GetWorldCorners(corners);
            var bottomLeft = parent.InverseTransformPoint(corners[0]);
            x = bottomLeft.x - parent.rect.xMin + 18; y = parent.rect.yMax - bottomLeft.y + 18;
        }
        x = Mathf.Clamp(x, 8, Mathf.Max(8, parent.rect.width - w - 8)); y = Mathf.Clamp(y, 8, Mathf.Max(8, parent.rect.height - h - 8));
        HintCanvas.Position(_chipCard, x, y);
    }
    private void HideChipCard() { _chipCardSource = null; if (_chipCard != null) _chipCard.gameObject.SetActive(false); }

    private string ChipLegend(OverviewMod mod)
    {
        var lines = new List<string> { "<color=#9FB3C6>" + Loc.T("Status chips", "角标", "角標") + "</color>" };
        void Add(Color color, string text) => lines.Add("<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">■</color> " + text);
        if (ChangeLedger.FindModUpdate("mod:" + mod.Guid) != null) Add(StatusChip.Unread, Loc.T("Blue: unread changes", "蓝色：有未读变动", "藍色：有未讀變動"));
        if (mod.HasUpdate) Add(AvailableUpdateDot.Green, AvailableUpdateDot.Hint(CurrentUpdateVersion(mod), mod.LatestVersion));
        var pending = MarkerState()?.Mod(mod.Guid);
        if (pending?.NeedsSave == true) Add(new Color(1f, .55f, .12f), Loc.T("Orange: change not saved yet", "橙色：改动还没保存", "橙色：改動還沒儲存"));
        if (pending?.NeedsRestart == true) Add(StatusChip.Restart, Loc.T("Violet: takes effect after restarting PEAK", "紫色：重启 PEAK 后生效", "紫色：重啟 PEAK 後生效"));
        if (mod.Outer != null) Add(StatusChip.Nested, Loc.T("Amber: inside another mod's folder", "琥珀色：在另一个模组的文件夹里", "琥珀色：在另一個模組的資料夾裡"));
        if (mod.ResourcePack) Add(StatusChip.ResourcePack, Loc.T("Teal: resource pack (no DLL)", "青色：资源包（没有 DLL）", "青色：資源包（沒有 DLL）"));
        if (mod.LooseFile) Add(StatusChip.LooseFile, Loc.T("Pink: loose DLL in plugins", "粉色：直接放在 plugins 里的 DLL", "粉色：直接放在 plugins 裡的 DLL"));
        if (_currentConflicts.Any(c => c.Any && c.Involves(mod))) Add(PackConflictUi.AdviceColor, Loc.T("Compatibility advice", "有冲突建议", "有衝突建議"));
        if (lines.Count == 1) lines.Add(Loc.T("No status chips on this row.", "这一行没有角标。", "這一行沒有角標。"));
        lines.Add("<color=#9FB3C6>" + Loc.T("A: menu · Y: more", "A：菜单 · Y：更多", "A：選單 · Y：更多") + "</color>");
        return string.Join("\n", lines);
    }

    private static void SetRowFocusGlow(RectTransform? view, bool on)
    {
        if (view == null) return;
        var glow = view.Find("GamepadRowGlow") as RectTransform;
        if (on && GamepadSupport.IsGamepad && glow == null)
        {
            glow = OverviewUi.Rect("GamepadRowGlow", view); OverviewUi.Fill(glow); glow.SetAsFirstSibling();
            var image = glow.gameObject.AddComponent<Image>(); image.color = new Color(1f, .96f, .86f, .07f); image.raycastTarget = false;
            glow.gameObject.AddComponent<OverviewRowFocusGlow>();
        }
        if (glow != null) glow.gameObject.SetActive(on && GamepadSupport.IsGamepad);
    }

    /// <summary>名称格：A 打开这一行的菜单（鼠标点击仍是选中）；手柄焦点时并排显示角标卡。</summary>
    private void BindGamepadName(GameObject title, OverviewMod mod)
    {
        var submit = title.GetComponent<OverviewGamepadSubmit>() ?? title.AddComponent<OverviewGamepadSubmit>();
        submit.Submit = () => { if (GamepadSupport.IsGamepad) ShowGamepadRowMenu(mod, (RectTransform)title.transform); };
        var focus = title.GetComponent<OverviewHotkeyFocus>() ?? title.AddComponent<OverviewHotkeyFocus>();
        focus.Selected += () => { if (GamepadSupport.IsGamepad) ShowChipCard(mod, title); };
        focus.Deselected += () => { if (_chipCardSource == title) HideChipCard(); };
    }
}

internal sealed partial class OverviewListView
{
    /// <summary>1.1.1 (user): the description cell is focusable on a gamepad; focus shows the full text in the floating
    /// hint (anchored to the cell), and A opens a small menu with "Edit description".</summary>
    private void BindGamepadDescription(GameObject description, OverviewMod mod)
    {
        if (description.GetComponent<Selectable>() == null)
        {
            var selectable = description.AddComponent<Selectable>(); selectable.transition = Selectable.Transition.None;
            selectable.targetGraphic = description.GetComponent<Graphic>();
        }
        var submit = description.GetComponent<OverviewGamepadSubmit>() ?? description.AddComponent<OverviewGamepadSubmit>();
        submit.Submit = () =>
        {
            if (!GamepadSupport.IsGamepad) return;
            HideAllHints();
            OverviewUi.CompactDropdown((RectTransform)description.transform, OverviewLanguageBridge.GetDisplayName(mod), new List<UiMenuEntry> {
                new UiMenuEntry { Label = Loc.T("Edit description", "编辑简介", "編輯簡介"), Role = UiActionRole.Edit, Invoke = () => ModInformationRequested?.Invoke(mod) },
                new UiMenuEntry { Label = Loc.T("Show full description", "查看完整简介", "檢視完整簡介"), Role = UiActionRole.Neutral,
                    Invoke = () => OverviewUi.ShowText(OverviewLanguageBridge.GetDisplayName(mod), OverviewLanguageBridge.GetDescription(mod)) },
            });
        };
    }
}

/// <summary>Soft light behind the row that holds the gamepad focus. Only shown on a gamepad.</summary>
internal sealed class OverviewRowFocusGlow : MonoBehaviour { }

/// <summary>A on a focused element that has no Button (the name label).</summary>
internal sealed class OverviewGamepadSubmit : MonoBehaviour, ISubmitHandler
{
    internal Action? Submit;
    public void OnSubmit(BaseEventData data) { if (Submit == null) return; Submit(); data.Use(); }
}
