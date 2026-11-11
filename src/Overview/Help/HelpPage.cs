#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ModConfigEnhance.Overview.Editor;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Zorro.UI;

namespace ModConfigEnhance.Overview.Help;

/// <summary>Localized guide reader with an opt-in shared editor and independent personal documents.</summary>
internal sealed partial class HelpPage : UIPage, IHaveParentPage
{
    private UIPage _parent = null!;
    private RectTransform _frame = null!, _articleHost = null!, _sidebar = null!, _chapterRows = null!;
    private TextMeshProUGUI _title = null!, _status = null!, _version = null!, _chapterTitle = null!;
    private Button _historyButton = null!, _back = null!;
    private readonly List<Button> _tabs = new();
    private readonly Stack<(string Page, float Offset)> _history = new();
    private readonly Dictionary<string, float> _positions = new();
    private DocCanvas _canvas = null!;
    private DocEditor _model = null!;
    private HelpReadingSurface _surface = null!;
    private HelpDocuments.Article _article = null!;
    private string _page = "overview";
    private string _language = "";
    private int _translationRevision = -1;
    private string? _pendingAnchor;
    private float? _pendingOffset;
    private float _width = -1, _height = -1;
    private int _settleFrames;
    private GameObject? _returnSelection;

    internal static HelpPage Create(UIPage parent)
    {
        var root = OverviewUi.Rect("MCE_HelpPage", parent.transform.parent); OverviewUi.Fill(root);
        root.gameObject.SetActive(false);
        var help = root.gameObject.AddComponent<HelpPage>(); help.RememberFontDirectory(); help.Initialize(parent);
        return help;
    }
    internal void Open()
    {
        RememberFontDirectory();
        _returnSelection = EventSystem.current?.currentSelectedGameObject;
        var handler = _parent.GetComponentInParent<UIPageHandler>();
        if (handler != null) handler.TransistionToPage(this, new SetActivePageTransistion());
    }
    private void Initialize(UIPage parent)
    {
        _parent = parent;
        gameObject.AddComponent<TypingGuard.Driver>();
        var backdrop = OverviewUi.Panel("Backdrop", transform, ModConfigLook.Surface(new Color(.05f, .07f, .07f, .84f))); OverviewUi.Fill(backdrop);
        var border = OverviewUi.Panel("Frame", transform, OverviewUi.Gold); OverviewUi.Fill(border, 14);
        _frame = OverviewUi.Panel("Paper", border, OverviewUi.Brown); OverviewUi.Fill(_frame, 2);
        _back = HelpButton(_frame, Loc.T("Back", "返回", "返回"), Back, 86, 36);
        _historyButton = HelpButton(_frame, Loc.T("Previous location", "返回上一处", "返回上一處"), Previous, 128, 36);
        _title = OverviewUi.Text(_frame, "", 22);
        _title.enableAutoSizing = true; _title.fontSizeMin = 14; _title.fontSizeMax = 22;
        _version = OverviewUi.Text(_frame, "", 13);
        _version.alignment = TextAlignmentOptions.TopRight;
        _version.rectTransform.anchorMin = _version.rectTransform.anchorMax = _version.rectTransform.pivot = Vector2.one;
        _version.rectTransform.anchoredPosition = new Vector2(-18, -18); _version.rectTransform.sizeDelta = new Vector2(240, 32);
        _sidebar = OverviewUi.Rect("Chapters", _frame);
        _chapterTitle = OverviewUi.Text(_sidebar, Loc.T("User guide", "使用说明", "使用說明"), 18); OverviewUi.Place(_chapterTitle.rectTransform, 8, 0, 186, 42);
        var chapterHost = OverviewUi.Rect("ChapterScroll", _sidebar); OverviewUi.Fill(chapterHost); chapterHost.offsetMax = new Vector2(0, -43);
        var chapterScroll = chapterHost.gameObject.AddComponent<ScrollRect>(); chapterScroll.horizontal = false; chapterScroll.scrollSensitivity = 40;
        chapterScroll.movementType = ScrollRect.MovementType.Clamped;
        var chapterViewport = OverviewUi.Rect("Viewport", chapterHost); OverviewUi.Fill(chapterViewport);
        chapterViewport.gameObject.AddComponent<RectMask2D>(); chapterViewport.gameObject.AddComponent<Image>().color = Color.clear;
        _chapterRows = OverviewUi.Rect("Chapters", chapterViewport); _chapterRows.anchorMin = new Vector2(0, 1); _chapterRows.anchorMax = Vector2.one;
        _chapterRows.pivot = new Vector2(0, 1); _chapterRows.sizeDelta = new Vector2(0, HelpDocuments.Pages.Length * 46);
        chapterScroll.viewport = chapterViewport; chapterScroll.content = _chapterRows;
        for (int i = 0; i < HelpDocuments.Pages.Length; i++)
        {
            var page = HelpDocuments.Pages[i];
            var tab = HelpButton(_chapterRows, (i + 1).ToString("00") + "  " + page.Title, () => Navigate(page.Id, null), 204, 37);
            _tabs.Add(tab);
        }
        _articleHost = OverviewUi.Rect("Article", _frame);

        // The reader and editor own separate input surfaces. A reader Selectable on
        // their shared parent can take focus from the nested document editor.
        var reader = _readingHost = OverviewUi.Rect("Reader", _articleHost); OverviewUi.Fill(reader);
        var hit = reader.gameObject.AddComponent<Image>(); hit.color = Color.clear;
        _surface = reader.gameObject.AddComponent<HelpReadingSurface>();
        _surface.targetGraphic = hit; _surface.transition = Selectable.Transition.None;
        _surface.Click = ClickLink; _surface.MovePage = amount => { _canvas.ScrollOffset += amount; };
        _surface.Links = ShowLinks; _surface.Leave = () => EventSystem.current?.SetSelectedGameObject(_tabs[Array.FindIndex(HelpDocuments.Pages, p => p.Id == _page)].gameObject);

        var host = OverviewUi.Rect("ReadingScroll", reader); OverviewUi.Fill(host);
        var scroll = host.gameObject.AddComponent<IntroductionScrollRect>(); scroll.horizontal = false;
        scroll.scrollSensitivity = 40; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = OverviewUi.Rect("Viewport", host); OverviewUi.Fill(viewport); viewport.offsetMax = new Vector2(-18, 0);
        viewport.gameObject.AddComponent<RectMask2D>();
        var bar = OverviewUi.Panel("Scrollbar", host, ModConfigLook.Surface(new Color(.1f, .1f, .1f, .5f)));
        bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.offsetMin = new Vector2(-12, 0); bar.offsetMax = Vector2.zero;
        var handle = OverviewUi.Panel("Handle", bar, OverviewUi.Gold); OverviewUi.Fill(handle);
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle; scrollbar.targetGraphic = handle.GetComponent<Image>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        var content = OverviewUi.Rect("Document", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0, 1); content.sizeDelta = Vector2.zero;
        scroll.viewport = viewport; scroll.content = content; _surface.Scroll = scroll;
        _model = new DocEditor(new Document(), () => Time.unscaledTimeAsDouble);
        _canvas = new DocCanvas(content, scroll, _model, FixedText, transform) { Author = false, CaretVisible = false, TextScale = .8f, ResolveMedia = ResolveHelpMedia };
        _status = OverviewUi.Text(_frame, "", 13); _status.color = OverviewUi.Muted;
        _language = HelpDocuments.LanguageCode; _translationRevision = Loc.TranslationRevision;
        LoadPage("overview", "top", null);
    }
    // Body text is an author-owned publication; only the surrounding controls use language bindings.
    private static TextMeshProUGUI FixedText(Transform parent, string text, float size)
    {
        var label = OverviewUi.Text(parent, text, size);
        var binding = label.GetComponent<InlineLanguageLabel>(); if (binding != null) { binding.enabled = false; Destroy(binding); }
        return label;
    }
    private static Button HelpButton(Transform parent, string label, Action clicked, float width, float height, UiActionRole role = UiActionRole.Neutral)
    {
        var button = OverviewUi.Button(parent, label, clicked, width, height, role);
        var caption = button.GetComponentInChildren<TextMeshProUGUI>(true);
        caption.enableAutoSizing = true; caption.fontSizeMin = 11; caption.fontSizeMax = 13;
        return button;
    }
    public override void OnPageEnter()
    {
        base.OnPageEnter(); OverviewUi.ModalParent = transform;
        ModConfigPaperStyle.Resolve(_parent.transform); OverviewPaperPanel.Refresh(transform);
        RefreshLanguage();
        Layout();
        UpdateEditingMode();
        if ((_guideResourcesStale || _loadedOverrideStamp != OverrideStamp()) && _editorView?.IsDirty != true) LoadPage(_page, null, _positions.TryGetValue(PositionKey(_page), out float last) ? last : 0);
        _pendingOffset ??= _positions.TryGetValue(PositionKey(_page), out float offset) ? offset : 0;
        _settleFrames = 3; Layout();
        PublicationNavigation.Attach(_frame);
        if (_editing && !GamepadSupport.IsGamepad) _editorView?.ActivateEditor();
        else EventSystem.current?.SetSelectedGameObject(_tabs[Array.FindIndex(HelpDocuments.Pages, p => p.Id == _page)].gameObject);
    }
    public override void OnPageExit()
    {
        RememberPosition(); OverviewUi.CloseModal(); OverviewUi.ModalParent = null; base.OnPageExit();
        RestoreFontDirectory();
        if (_returnSelection != null && _parent != null)
        {
            var restore = _parent.GetComponent<HelpFocusRestore>() ?? _parent.gameObject.AddComponent<HelpFocusRestore>();
            restore.Restore(_returnSelection);
        }
    }
    public (UIPage, PageTransistion) GetParentPage()
    {
        if (OverviewUi.TryCloseTopModal()) return (this, GamepadSupport.NoopTransistion.Shared);
        if (_editing && _editorView != null)
        {
            if (_editorView.TryCloseLayer()) return (this, GamepadSupport.NoopTransistion.Shared);
            if (_editorView.IsDirty || _editorView.IsImporting) { _editorView.RequestLeave(LeaveToParent); return (this, GamepadSupport.NoopTransistion.Shared); }
        }
        return (_parent, new SetActivePageTransistion());
    }
    private void Back()
    {
        if (OverviewUi.TryCloseTopModal()) return;
        RequestDocumentLeave(LeaveToParent);
    }
    private string PositionKey(string page) => _language + "/" + page;
    private void RememberPosition() { if (_canvas != null) _positions[PositionKey(_page)] = _editing && _editorView != null ? _editorView.ScrollOffset : _canvas.ScrollOffset; }
    private void Navigate(string page, string? anchor)
        => RequestDocumentLeave(() => NavigateCore(page, anchor));
    private void NavigateCore(string page, string? anchor)
    {
        if (page == _page && anchor == null) return;
        RememberPosition();
        _history.Push((_page, _canvas.ScrollOffset));
        LoadPage(page, anchor, anchor == null && _positions.TryGetValue(PositionKey(page), out float offset) ? offset : null);
    }
    private void LoadPage(string page, string? anchor, float? offset)
    {
        _article = HelpDocuments.Load(page); _page = page;
        // DocCanvas only reads this model. No editing callbacks are registered on this page.
        _model.Reset(_article.Document.Clone());
        LoadEditorPage();
        _pendingAnchor = anchor; _pendingOffset = offset ?? (anchor == null ? 0 : (float?)null); _settleFrames = 3;
        RefreshChrome();
        _historyButton.interactable = _history.Count > 0;
        for (int i = 0; i < _tabs.Count; i++)
        {
            bool selected = HelpDocuments.Pages[i].Id == page;
            UiActionStyle.Set(_tabs[i], selected ? UiActionRole.Edit : UiActionRole.Neutral);
            UiActionStyle.Select(_tabs[i], selected);
        }
        if (isActiveAndEnabled) { Layout(); if (!_editing) _canvas.Refresh(true); }
    }
    private void RefreshChrome()
    {
        _back.GetComponentInChildren<TextMeshProUGUI>(true).text = Loc.T("Back", "返回", "返回");
        _historyButton.GetComponentInChildren<TextMeshProUGUI>(true).text = Loc.T("Previous location", "返回上一处", "返回上一處");
        _title.text = "ModConfigEnhance · " + HelpDocuments.Find(_page)!.Title;
        _version.text = Loc.F("MCE {0} · {1}", "MCE {0} · {1}", "MCE {0} · {1}", Plugin.PluginVersion, GuideSourceLabel());
        _chapterTitle.text = Loc.T("User guide", "使用说明", "使用說明");
        _status.text = Loc.T("Click blue text to navigate · Previous location restores your place · Scroll with the right stick", "点击蓝色文字跳转 · 返回上一处恢复阅读位置 · 手柄右摇杆滚动", "點擊藍色文字跳轉 · 返回上一處恢復閱讀位置 · 手把右搖桿捲動");
        if (_editing) _status.text = Loc.T("Click the document to type · Use the toolbar to format and save · Changes stay on this device", "点击正文即可输入 · 工具栏可排版和保存 · 修改保存在本机", "點擊正文即可輸入 · 工具列可排版和儲存 · 修改儲存在本機");
        if (_editing && Source.ReadNote != null) _status.text = Source.ReadNote;
        for (int i = 0; i < _tabs.Count; i++)
            _tabs[i].GetComponentInChildren<TextMeshProUGUI>(true).text = (i + 1).ToString("00") + "  " + HelpDocuments.Pages[i].Title;
        RefreshRestoreButton();
    }
    private void RefreshLanguage()
    {
        string language = HelpDocuments.LanguageCode;
        if (_editing && _editorView?.IsDirty == true && language != _language) { _status.text = Loc.T("Save or discard the current guide draft before switching language.", "请先保存或放弃当前说明草稿，再切换语言。", "請先儲存或放棄目前說明草稿，再切換語言。"); return; }
        if (_translationRevision == Loc.TranslationRevision && _language == language) return;
        RememberPosition();
        bool changed = _language != language;
        _language = language; _translationRevision = Loc.TranslationRevision;
        if (changed)
        {
            // Each language owns its reading position; avoid history offsets into another translation.
            _history.Clear();
            LoadPage(_page, null, _positions.TryGetValue(PositionKey(_page), out float offset) ? offset : 0);
        }
        else RefreshChrome();
    }
    private void Previous()
        => RequestDocumentLeave(PreviousCore);
    private void PreviousCore()
    {
        if (_history.Count == 0) return;
        RememberPosition(); var point = _history.Pop(); LoadPage(point.Page, null, point.Offset);
    }
    private void FollowLink(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
        { Application.OpenURL(uri.AbsoluteUri); return; }
        RequestDocumentLeave(() => { if (HelpDocuments.TryResolve(_page, target, out string page, out string anchor)) NavigateCore(page, anchor);
            else _status.text = Loc.F("Help section not found: {0}", "未找到说明章节：{0}", "未找到說明章節：{0}", target); });
    }
    private void ClickLink(PointerEventData data)
    {
        string target = _canvas.LinkAt(_canvas.ScreenToDoc(data.position, data.pressEventCamera));
        if (target.Length > 0) FollowLink(target);
    }
    private void ShowLinks()
    {
        var options = _article.Links.Select(l => (l.Label + (Uri.TryCreate(l.Target, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") ? Loc.T(" (external)", "（外部）", "（外部）") : ""), l.Target)).ToList();
        if (options.Count > 0) OverviewUi.Choose(Loc.T("Links on this page", "本页链接", "本頁連結"), options, "", FollowLink);
    }
    private void Layout()
    {
        float width = _frame.rect.width, height = _frame.rect.height; _width = width; _height = height;
        OverviewUi.Place((RectTransform)_back.transform, 16, 14, 86, 36);
        OverviewUi.Place((RectTransform)_historyButton.transform, 114, 14, 128, 36);
        OverviewUi.Place(_title.rectTransform, 258, 14, Mathf.Max(140, width - 528), 48);
        float sideWidth = width < 1000 ? 184 : 210;
        OverviewUi.Place(_sidebar, 18, 78, sideWidth, height - 112);
        for (int i = 0; i < _tabs.Count; i++) OverviewUi.Place((RectTransform)_tabs[i].transform, 0, i * 46, sideWidth, 37);
        _articleHost.anchorMin = Vector2.zero; _articleHost.anchorMax = Vector2.one;
        _articleHost.offsetMin = new Vector2(sideWidth + 40, 52); _articleHost.offsetMax = new Vector2(-20, -76);
        LayoutRestoreButton(sideWidth, height);
        OverviewUi.Place(_status.rectTransform, sideWidth + 48, height - 35, Mathf.Max(200, width - sideWidth - 72), 24);
        _status.enableAutoSizing = true; _status.fontSizeMin = 10; _status.fontSizeMax = 13;
    }
    private void Update()
    {
        if (_frame == null) return;
        RefreshLanguage();
        UpdateEditingMode();
        if (Mathf.Abs(_frame.rect.width - _width) > .5f || Mathf.Abs(_frame.rect.height - _height) > .5f) { Layout(); _settleFrames = Math.Max(_settleFrames, 2); }
        if (_editing && _editorView != null) { UpdateEditorScroll(); return; }
        _canvas.Refresh();
        if (_settleFrames > 0)
        {
            Canvas.ForceUpdateCanvases(); _canvas.Refresh(true);
            if (_pendingAnchor != null && _article.Anchors.TryGetValue(_pendingAnchor, out int index) && index < _canvas.Views.Count)
                _canvas.ScrollOffset = Mathf.Max(0, _canvas.Views[index].Y - 14);
            else if (_pendingOffset.HasValue) _canvas.ScrollOffset = _pendingOffset.Value;
            if (--_settleFrames == 0) { _pendingAnchor = null; _pendingOffset = null; }
        }
        if (!OverviewUi.HasModal && GamepadSupport.IsGamepad && Gamepad.current != null)
        {
            float stick = Gamepad.current.rightStick.ReadValue().y;
            if (Mathf.Abs(stick) > .15f) _canvas.ScrollOffset -= stick * 650 * Time.unscaledDeltaTime;
        }
    }
    private void OnDestroy() { _editorView?.Dispose(); _canvas?.Dispose(); }
}

internal sealed class HelpFocusRestore : MonoBehaviour
{
    private GameObject? _target;
    private Coroutine? _pending;
    internal void Restore(GameObject target) { _target = target; Schedule(); }
    private void OnEnable() => Schedule();
    private void OnDisable() { if (_pending != null) StopCoroutine(_pending); _pending = null; }
    private void Schedule() { if (_target != null && isActiveAndEnabled && _pending == null) _pending = StartCoroutine(Apply()); }
    private IEnumerator Apply()
    {
        yield return null;
        if (_target != null && _target.activeInHierarchy && !OverviewUi.HasModal) EventSystem.current?.SetSelectedGameObject(_target);
        _target = null; _pending = null;
    }
}

/// <summary>Read-only pointer/scroll/focus surface. It accepts navigation, never text or editor commands.</summary>
internal sealed class HelpReadingSurface : Selectable, IPointerClickHandler, IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, ISubmitHandler
{
    internal ScrollRect Scroll = null!;
    internal Action<PointerEventData>? Click;
    internal Action<float>? MovePage;
    internal Action? Links, Leave;
    private bool _dragged;
    public override void OnPointerDown(PointerEventData data) { _dragged = false; base.OnPointerDown(data); }
    public void OnPointerClick(PointerEventData data) { if (!_dragged && data.button == PointerEventData.InputButton.Left) Click?.Invoke(data); }
    public void OnInitializePotentialDrag(PointerEventData data) => Scroll.OnInitializePotentialDrag(data);
    public void OnBeginDrag(PointerEventData data) { _dragged = true; data.eligibleForClick = false; Scroll.OnBeginDrag(data); }
    public void OnDrag(PointerEventData data) => Scroll.OnDrag(data);
    public void OnEndDrag(PointerEventData data) => Scroll.OnEndDrag(data);
    public void OnScroll(PointerEventData data) => Scroll.OnScroll(data);
    public void OnSubmit(BaseEventData data) { Links?.Invoke(); data.Use(); }
    public override void OnMove(AxisEventData data)
    {
        if (data.moveDir == MoveDirection.Up) MovePage?.Invoke(-90);
        else if (data.moveDir == MoveDirection.Down) MovePage?.Invoke(90);
        else if (data.moveDir == MoveDirection.Left) Leave?.Invoke();
        else if (data.moveDir == MoveDirection.Right) Links?.Invoke();
        data.Use();
    }
}
