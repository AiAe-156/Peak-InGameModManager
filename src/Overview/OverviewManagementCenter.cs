#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

// Publication and preview deliberately occupy the last two positions.
internal enum ManagementTab { Information, Packs, Groups, Profiles, Updates, Network, Tools, Publication, Preview }

internal sealed class OverviewManagementCenter : IDisposable
{
    private readonly RectTransform _root, _panel;
    private readonly Action<ManagementTab, RectTransform> _build;
    private readonly Action _closed;
    private readonly Action _updates;
    private readonly Dictionary<ManagementTab, RectTransform> _pages = new();
    private readonly Dictionary<ManagementTab, Button> _tabs = new();
    private readonly Dictionary<ManagementTab, Func<bool>> _dirty = new();
    private readonly Dictionary<ManagementTab, Action<Action>> _leave = new();
    private readonly Dictionary<ManagementTab, Action> _stage = new();
    internal void SetStage(ManagementTab tab, Action stage) => _stage[tab] = stage;
    internal void StageDrafts() { foreach (var stage in _stage.Values.ToArray()) stage(); }
    private readonly TextMeshProUGUI _status;
    private readonly GameObject? _returnFocus;
    private readonly CanvasGroup _canvas;
    internal ManagementTab Current { get; private set; }
    internal bool IsOpen => _root != null && _root.gameObject.activeInHierarchy;
    internal bool HasDirty => _dirty.Values.Any(check => check());
    internal RectTransform? Host(ManagementTab tab) => _pages.TryGetValue(tab, out var host) ? host : null;
    internal void SetDirty(ManagementTab tab, Func<bool> check) => _dirty[tab] = check;
    internal void RefreshDirtyIndicators()
    {
        foreach (var tab in _tabs)
        {
            bool dirty = _dirty.TryGetValue(tab.Key, out var check) && check();
            var indicator = tab.Value.transform.Find("MCE_UnsavedCorner");
            if (indicator == null && dirty)
            {
                var corner = OverviewUi.Rect("MCE_UnsavedCorner", tab.Value.transform);
                corner.anchorMin = corner.anchorMax = corner.pivot = Vector2.one;
                corner.anchoredPosition = new Vector2(-3, -3); corner.sizeDelta = new Vector2(7, 7);
                corner.gameObject.AddComponent<RoundedChip>().color = new Color(1f, .55f, .12f, 1f); indicator = corner;
            }
            if (indicator != null) indicator.gameObject.SetActive(dirty);
        }
    }
    internal void SetLeave(ManagementTab tab, Action<Action> leave) => _leave[tab] = leave;

    internal OverviewManagementCenter(RectTransform parent, Action<ManagementTab, RectTransform> build, Action closed, Action updates)
    {
        _build = build; _closed = closed; _updates = updates; _returnFocus = EventSystem.current?.currentSelectedGameObject;
        _root = OverviewUi.Panel("MCE_ManagementCenter", parent, ModConfigLook.Surface(new Color(0, 0, 0, .88f))); OverviewUi.Fill(_root); _root.SetAsLastSibling();
        _canvas = _root.gameObject.AddComponent<CanvasGroup>();
        _panel = OverviewUi.Panel("ManagementWindow", _root, ModConfigLook.Surface(new Color(.16f, .13f, .10f, 1))); OverviewUi.Fill(_panel, 12);
        float width = _panel.rect.width, height = _panel.rect.height;
        var title = OverviewUi.Text(_panel, Loc.T("Management center", "管理中心", "管理中心"), 27);
        OverviewUi.PlaceClipped(title, 18, 9, width - 140, 38);
        var close = OverviewUi.Button(_panel, Loc.T("Close", "关闭", "關閉"), () => TryClose(), 94, 32, UiActionRole.Neutral);
        OverviewUi.Place((RectTransform)close.transform, width - 112, 12, 94, 32);
        var labels = new[] { Loc.T("Info", "资料", "資料"), Loc.T("Modpacks", "整合包", "整合包"), Loc.T("Groups", "分组", "分組"), Loc.T("Profiles", "方案", "方案"),
            Loc.T("Updates", "更新", "更新"), Loc.T("Network", "网络", "網路"), Loc.T("Tools", "工具", "工具"),
            Loc.T("Publish", "发布", "發布"), Loc.T("Preview", "预览", "預覽") };
        float tabWidth = Mathf.Min(132, (width - 36 - (labels.Length - 1) * 5) / labels.Length);
        for (int i = 0; i < labels.Length; i++)
        {
            var tab = (ManagementTab)i;
            var button = OverviewUi.Button(_panel, labels[i], () => Show(tab), tabWidth, 35, UiActionRole.Neutral);
            var caption = button.GetComponentInChildren<TextMeshProUGUI>(true); caption.enableAutoSizing = true; caption.fontSizeMin = 12; caption.fontSizeMax = 19;
            OverviewUi.Place((RectTransform)button.transform, 18 + i * (tabWidth + 5), 54, tabWidth, 35); _tabs.Add(tab, button);
        }
        _status = OverviewUi.Text(_panel, "", 15); _status.color = OverviewUi.Muted;
        OverviewUi.PlaceClipped(_status, 18, height - 36, width - 36, 27);
        OverviewUi.ManagementOpen = true;
        PublicationNavigation.Attach(_panel);
    }

    internal void Show(ManagementTab tab)
    {
        if (tab == ManagementTab.Updates) { _updates(); return; }
        if (_leave.TryGetValue(Current, out var leave)) { leave(() => ShowPage(tab)); return; }
        ShowPage(tab);
    }
    private void ShowPage(ManagementTab tab)
    {
        Current = tab;
        if (tab == ManagementTab.Preview && _pages.ContainsKey(tab)) Rebuild(tab);
        if (!_pages.TryGetValue(tab, out var host))
        {
            host = OverviewUi.Rect("Management_" + tab, _panel);
            OverviewUi.Place(host, 18, 101, _panel.rect.width - 36, Mathf.Max(160, _panel.rect.height - 148));
            _pages.Add(tab, host);
            try { _build(tab, host); }
            catch (Exception error) { Status(Loc.LocalizeMessage(error.Message)); Plugin.Log.LogError(error); }
        }
        foreach (var item in _pages) item.Value.gameObject.SetActive(item.Key == tab);
        foreach (var item in _tabs) { UiActionStyle.Set(item.Value, UiActionRole.Neutral); UiActionStyle.Select(item.Value, item.Key == tab); }
        PublicationNavigation.Attach(host);
        EventSystem.current?.SetSelectedGameObject(_tabs[tab].gameObject);
    }

    internal void Rebuild(ManagementTab tab, bool detailOnly = false)
    {
        if (_pages.TryGetValue(tab, out var host))
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            bool restoreFocus = selected != null && selected.transform.IsChildOf(host);
            var focusText = restoreFocus ? selected!.GetComponentInChildren<TMP_Text>(true)?.text : null;
            Vector3 focusPosition = restoreFocus ? selected!.transform.position : Vector3.zero;
            var scrolls = host.GetComponentsInChildren<ScrollRect>(true).Select(s => (Path: RelativeUiPath(host, s.transform), Position: s.content?.anchoredPosition ?? Vector2.zero)).ToList();
            foreach (Transform child in host)
            {
                if (detailOnly && tab == ManagementTab.Packs && child.name != "PackDetail") continue;
                child.gameObject.SetActive(false); child.name = "Retired_" + child.name; UnityEngine.Object.Destroy(child.gameObject);
            }
            _dirty.Remove(tab); _leave.Remove(tab); _build(tab, host);
            if (tab == ManagementTab.Packs)
            {
                foreach (var scroll in host.GetComponentsInChildren<ScrollRect>(false))
                {
                    var old = scrolls.FirstOrDefault(s => s.Path == RelativeUiPath(host, scroll.transform));
                    if (old.Path != null && scroll.content != null) scroll.content.anchoredPosition = old.Position;
                }
                if (restoreFocus && (selected == null || !selected.activeInHierarchy))
                {
                    var choices = host.GetComponentsInChildren<Selectable>(false).Where(s => s.IsInteractable()).ToList();
                    var next = choices.Where(s => s.GetComponentInChildren<TMP_Text>(true)?.text == focusText).OrderBy(s => Vector3.SqrMagnitude(s.transform.position - focusPosition)).FirstOrDefault()
                        ?? choices.OrderBy(s => Vector3.SqrMagnitude(s.transform.position - focusPosition)).FirstOrDefault();
                    EventSystem.current?.SetSelectedGameObject(next?.gameObject);
                }
            }
        }
    }
    private static string RelativeUiPath(Transform root, Transform child)
    {
        var parts = new List<string>(); while (child != root && child != null) { parts.Add(child.name); child = child.parent; } parts.Reverse(); return string.Join("/", parts);
    }
    internal void Invalidate(ManagementTab tab)
    {
        if (!_pages.TryGetValue(tab, out var host)) return;
        if (Current == tab) { Rebuild(tab); return; }
        host.gameObject.SetActive(false); UnityEngine.Object.Destroy(host.gameObject); _pages.Remove(tab);
    }
    internal void Status(string message) { if (_status != null) { _status.text = message.Replace('\n', ' '); _status.color = OverviewUi.Ivory; } }
    internal bool TryClose()
    {
        if (!IsOpen) return false;
        StageDrafts();
        if (HasDirty) OverviewUi.ConfirmDiscard(Loc.T("Discard remaining unsaved management changes?", "放弃尚未纳入草稿的管理修改？", "放棄尚未納入草稿的管理變更？"), _closed);
        else _closed();
        return true;
    }
    internal void RequestTransition(Action next)
    {
        StageDrafts();
        if (HasDirty) OverviewUi.ConfirmDiscard(Loc.T("Discard remaining unsaved management changes?", "放弃尚未纳入草稿的管理修改？", "放棄尚未納入草稿的管理變更？"), next);
        else next();
    }
    internal void SetInteractive(bool value) { if (_canvas != null) { _canvas.interactable = value; _canvas.blocksRaycasts = value; } }
    public void Dispose()
    {
        OverviewUi.ManagementOpen = false;
        if (_root != null) { _root.gameObject.SetActive(false); UnityEngine.Object.Destroy(_root.gameObject); }
        _dirty.Clear(); _stage.Clear(); _pages.Clear();
        if (_returnFocus != null && _returnFocus.activeInHierarchy) EventSystem.current?.SetSelectedGameObject(_returnFocus);
    }
}

/// <summary>Shared measured form rows; the footer stays outside the clipped scrolling body.</summary>
internal sealed class ManagementForm
{
    internal readonly RectTransform Panel, Content;
    internal readonly ScrollRect Scroll;
    private float _y, _actionX, _buttonX;
    private readonly Action<Action> _run;
    internal float Width => Panel.rect.width - 34;
    internal ManagementForm(RectTransform parent, Action<Action> run)
    {
        Panel = parent; _run = run;
        var host = OverviewUi.Rect("FormScroll", parent); OverviewUi.Place(host, 0, 0, parent.rect.width, Mathf.Max(70, parent.rect.height - 62));
        Scroll = host.gameObject.AddComponent<ScrollRect>(); Scroll.horizontal = false; Scroll.movementType = ScrollRect.MovementType.Clamped; Scroll.scrollSensitivity = 40;
        var viewport = OverviewUi.Rect("Viewport", host); OverviewUi.Fill(viewport); viewport.offsetMax = new Vector2(-18, 0);
        viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        Content = OverviewUi.Rect("Content", viewport); Content.anchorMin = new Vector2(0, 1); Content.anchorMax = Vector2.one; Content.pivot = new Vector2(0, 1); Content.sizeDelta = Vector2.zero;
        Scroll.content = Content; Scroll.viewport = viewport; ManagementUi.Scrollbar(host, Scroll);
    }
    internal void Text(string value, bool heading = false)
    {
        EndButtons();
        var text = OverviewUi.Text(Content, value, heading ? 22 : 17); text.textWrappingMode = TextWrappingModes.Normal;
        text.color = heading ? OverviewUi.Ivory : OverviewUi.Muted;
        float height = Mathf.Max(heading ? 36 : 32, text.GetPreferredValues(value, Width, 0).y + 8);
        OverviewUi.Place(text.rectTransform, 0, _y, Width, height); _y += height + 8; Finish();
    }
    /// <summary>Lets a custom view draw its own measured block into the scrolling body; the callback returns the new bottom edge.</summary>
    internal void Block(Func<RectTransform, float, float> draw) { EndButtons(); _y = draw(Content, _y) + 8; Finish(); }
    /// <summary>Ends the current row of buttons so following blocks start below them.</summary>
    internal void EndRow() => EndButtons();
    internal TMP_InputField Field(string label, string value, bool multiline = false, Action<string>? changed = null)
    {
        Text(label); var input = OverviewUi.Input(Content, value, multiline); input.characterLimit = 16384;
        float height = multiline ? 78 : 42; OverviewUi.Place((RectTransform)input.transform, 0, _y, Width, height); _y += height + 12;
        if (changed != null) input.onValueChanged.AddListener(v => changed(v)); Finish(); return input;
    }
    internal Toggle Check(string label, bool value, Action<bool> changed)
    {
        EndButtons();
        var check = OverviewUi.Check(Content, value, changed); OverviewUi.Place((RectTransform)check.transform, 0, _y + 3, 28, 28);
        var text = OverviewUi.Text(Content, label, 18); text.textWrappingMode = TextWrappingModes.Normal;
        float height = Mathf.Max(38, text.GetPreferredValues(label, Width - 44, 0).y + 8);
        OverviewUi.Place(text.rectTransform, 42, _y, Width - 44, height); _y += height + 8; Finish(); return check;
    }
    internal Button Button(string label, Action action, UiActionRole role = UiActionRole.Edit)
    {
        var button = OverviewUi.Button(Content, label, () => _run(action), 150, 36, role);
        float width = Mathf.Clamp(button.GetComponentInChildren<TMP_Text>(true).GetPreferredValues(label).x + 28, 100, Mathf.Min(310, Width));
        if (_buttonX > 0 && _buttonX + width > Width) EndButtons();
        OverviewUi.Place((RectTransform)button.transform, _buttonX, _y, width, 36); _buttonX += width + 10; Finish(); return button;
    }
    internal Button Action(string label, Action action, UiActionRole role = UiActionRole.Commit)
    {
        var button = OverviewUi.Button(Panel, label, () => _run(action), 140, 36, role);
        float width = Mathf.Min(Mathf.Max(115, button.GetComponentInChildren<TMP_Text>(true).GetPreferredValues(label).x + 26), Mathf.Max(115, Panel.rect.width / 3 - 12));
        OverviewUi.Place((RectTransform)button.transform, _actionX, Panel.rect.height - 46, width, 36); _actionX += width + 12; return button;
    }
    private void EndButtons() { if (_buttonX <= 0) return; _buttonX = 0; _y += 48; }
    internal void Finish() => Content.sizeDelta = new Vector2(0, _y + (_buttonX > 0 ? 48 : 0) + 12);
}

internal static class ManagementUi
{
    internal static void Scrollbar(RectTransform host, ScrollRect scroll)
    {
        var track = OverviewUi.Panel("Scrollbar", host, ModConfigLook.Surface(new Color(.07f, .07f, .06f, 1))); track.anchorMin = new Vector2(1, 0); track.anchorMax = Vector2.one; track.pivot = Vector2.one; track.sizeDelta = new Vector2(10, 0); track.anchoredPosition = Vector2.zero;
        var handle = OverviewUi.Panel("Handle", track, OverviewUi.Gold); OverviewUi.Fill(handle);
        var bar = track.gameObject.AddComponent<UnityEngine.UI.Scrollbar>(); bar.targetGraphic = handle.GetComponent<Image>(); bar.handleRect = handle; bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
    }
    internal static void OpenFolder(string path)
    {
        if (!System.IO.Directory.Exists(path)) throw new System.IO.DirectoryNotFoundException(Loc.T("Folder not found: ", "目录不存在：", "目錄不存在：") + path);
        Application.OpenURL(new Uri(System.IO.Path.GetFullPath(path)).AbsoluteUri);
    }
}
