#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview.Editor;

/// <summary>
/// The pack introduction page: one continuous document that reads like a page and, in author mode,
/// edits like a word processor (caret and selection across paragraphs, Enter/Backspace structure,
/// state-aware toolbar, in-place media resizing and drag-to-arrange).
/// Stored as <c>introduction.mcedoc.json</c>; a Markdown copy is written beside it for older builds.
/// </summary>
internal sealed class IntroductionEditorView : IDisposable
{
    private const string DocumentFile = "introduction.mcedoc.json", MarkdownFile = "introduction.md";
    private static readonly int[] FontSizes = { 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 40, 48, 64 };
    private static readonly string[] Highlights = { "#F2D35B", "#F0A35A", "#7FD48A", "#6FB7E8", "#B49AE0", "#E88FB0" };
    private static readonly string[] Backgrounds = { "#2E3D4ACC", "#29493680", "#4A3A2480", "#4A2A2A80", "#3B2E4A80", "#25343D90" };
    private static DocFragment? _clipboard;

    public GameObject Root { get; }
    public event Action<string>? StatusChanged;
    public event Action? DraftChanged;
    public bool IsImporting => _importing;
    public bool IsDirty
    {
        get
        {
            if (!_author || !_loaded) return false;
            if (_dirtyRevision != _editor.Revision) { _dirty = DocJson.Write(_editor.Doc) != _savedJson; _dirtyRevision = _editor.Revision; }
            return _dirty;
        }
    }

    private readonly OverviewRepository? _repository;
    private readonly IDocumentEditorSource? _documentSource;
    private OverviewRepository Repository => _repository ?? throw new InvalidOperationException("This document does not belong to a profile.");
    private readonly Func<Transform, string, float, TextMeshProUGUI> _factory;
    private readonly DocEditor _editor;
    private readonly DocCanvas _canvas;
    private readonly DocSurface _surface;
    private readonly ControllerReadingSurface _reading;
    private bool _controllerReading = true;
    private readonly OverviewCascade _cascade;
    private readonly DocToolbar _toolbar;
    private readonly DocMediaTools _tools;
    private readonly RectTransform _rootRect, _scrollRect, _menuLayer;
    private readonly CancellationTokenSource _importCancellation = new();
    private DocPopover? _popover;
    private GameObject? _sourcePanel;
    private TMP_InputField? _sourceInput;
    private string _sourceInitial = "";
    private bool _author, _importing, _disposed, _wantFocus;
    private string _savedJson = "";
    private bool _dirty, _loaded; private int _dirtyRevision = -1;
    private float _goalX = -1, _blinkStart;
    private int _enterFrame = -1, _toolbarRevision = -1;
    private DocPos _dragAnchor; private int _dragGranularity;

    public IntroductionEditorView(Transform parent, OverviewRepository repository, Func<Transform, string, float, TextMeshProUGUI> textFactory) : this(parent, textFactory, repository, null) { }
    public IntroductionEditorView(Transform parent, IDocumentEditorSource source, Func<Transform, string, float, TextMeshProUGUI> textFactory) : this(parent, textFactory, null, source) { }
    private IntroductionEditorView(Transform parent, Func<Transform, string, float, TextMeshProUGUI> textFactory, OverviewRepository? repository, IDocumentEditorSource? source)
    {
        _repository = repository; _documentSource = source; _factory = textFactory;
        _editor = new DocEditor(new Document(), () => Time.unscaledTimeAsDouble);
        Root = new GameObject("Introduction", typeof(RectTransform)); Root.transform.SetParent(parent, false);
        _rootRect = (RectTransform)Root.transform; _rootRect.anchorMin = Vector2.zero; _rootRect.anchorMax = Vector2.one; _rootRect.pivot = new Vector2(.5f, .5f); _rootRect.offsetMin = _rootRect.offsetMax = Vector2.zero;
        var hit = Root.AddComponent<Image>(); hit.color = Color.clear;
        _cascade = Root.AddComponent<OverviewCascade>();
        _surface = Root.AddComponent<DocSurface>(); _surface.transition = Selectable.Transition.None; _surface.navigation = new Navigation { mode = Navigation.Mode.None }; _surface.targetGraphic = hit;
        _surface.HoldEditingFocus = source != null;

        // Page scroll area. The viewport has no raycast image: clicks fall through to the surface.
        _scrollRect = DocCanvas.Node(_rootRect, "IntroductionScroll"); _scrollRect.anchorMin = Vector2.zero; _scrollRect.anchorMax = Vector2.one; _scrollRect.pivot = new Vector2(.5f, .5f); _scrollRect.offsetMin = _scrollRect.offsetMax = Vector2.zero;
        var scroll = _scrollRect.gameObject.AddComponent<IntroductionScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 40; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = DocCanvas.Node(_scrollRect, "Viewport"); viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one; viewport.pivot = new Vector2(0, 1); viewport.offsetMin = Vector2.zero; viewport.offsetMax = new Vector2(-18, 0); viewport.gameObject.AddComponent<RectMask2D>();
        var bar = DocCanvas.Node(_scrollRect, "Scrollbar"); bar.anchorMin = new Vector2(1, 0); bar.anchorMax = new Vector2(1, 1); bar.pivot = new Vector2(1, .5f); bar.sizeDelta = new Vector2(12, 0); bar.anchoredPosition = Vector2.zero;
        var barImage = bar.gameObject.AddComponent<Image>(); barImage.color = ModConfigLook.Surface(new Color(.25f, .16f, .11f, .95f));
        bar.gameObject.AddComponent<OverviewPaperPanel>();
        var sliding = DocCanvas.Node(bar, "SlidingArea"); sliding.anchorMin = Vector2.zero; sliding.anchorMax = Vector2.one; sliding.offsetMin = sliding.offsetMax = Vector2.zero;
        var handle = DocCanvas.Node(sliding, "Handle"); handle.anchorMin = Vector2.zero; handle.anchorMax = Vector2.one; handle.offsetMin = handle.offsetMax = Vector2.zero;
        var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = ModConfigLook.Surface(new Color(.91f, .85f, .71f, .9f));
        handle.gameObject.AddComponent<OverviewPaperPanel>();
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage; scrollbar.direction = Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        var content = DocCanvas.Node(viewport, "Page"); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0, 1); content.sizeDelta = Vector2.zero; content.anchoredPosition = Vector2.zero;
        scroll.viewport = viewport; scroll.content = content;

        _canvas = new DocCanvas(content, scroll, _editor, textFactory, _rootRect) { ResolveMedia = ResolveMedia };
        var reading = OverviewUi.Rect("MCE_IntroductionReading", _rootRect); OverviewUi.Fill(reading);
        var readingImage = reading.gameObject.AddComponent<Image>(); readingImage.color = Color.clear; readingImage.raycastTarget = false;
        _reading = reading.gameObject.AddComponent<ControllerReadingSurface>(); _reading.Scroll = scroll; _reading.targetGraphic = readingImage; _reading.transition = Selectable.Transition.None;
        _reading.TryMovePage = amount => { float before = _canvas.ScrollOffset; _canvas.ScrollOffset += amount; return Mathf.Abs(_canvas.ScrollOffset - before) > .1f; };
        GamepadSupport.MarkSelectable(reading.gameObject, null, null);
        if (source != null) _canvas.TextScale = .8f;
        _canvas.Status = s => StatusChanged?.Invoke(s);
        _canvas.ObjectClicked = o => { if (!_author) return; ClosePopover(); _editor.SelectObject(o); _wantFocus = true; _surface.Focus(); Changed(false); };

        _toolbar = new DocToolbar(_rootRect, textFactory, _rootRect);
        var tr = _toolbar.Root; tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(.5f, 1); tr.anchoredPosition = Vector2.zero; tr.sizeDelta = new Vector2(0, _toolbar.Height);
        BuildToolbar();
        _menuLayer = _rootRect;

        _tools = new DocMediaTools(_canvas, _editor, _rootRect, textFactory, () => Changed(false));
        _tools.Delete = _ => { _editor.DeleteObject(); Changed(); };
        _tools.Replace = o => ImportMedia(KindName(_editor.MediaOf(o)?.Kind ?? MediaKind.Image), o);
        _tools.EditCaption = EditCaption;

        _surface.Key = OnKey; _surface.Press = (data, count) => { _cascade.CompleteAppearance(); OnPress(data, count); }; _surface.DragTo = OnDrag; _surface.DragEnd = _ => { }; _surface.Click = data => { _cascade.CompleteAppearance(); OnClick(data); }; _surface.Tick = Tick;
        _surface.Scrolled = data => _canvas.Scroll.OnScroll(data);
        _surface.Focused = () => { _blinkStart = Time.unscaledTime; };
        SetAuthorMode(false); // 文档交给 OnPageEnter 的 Reload 读一次，别在构造期再读一遍
    }

    // ---------------- public surface used by the overview page ----------------

    public void ActivateEditor() { if (_author && Root.activeInHierarchy) { _wantFocus = true; _surface.Focus(); } }
    internal Action<string>? FollowLink;
    internal float ScrollOffset { get => _canvas.ScrollOffset; set => _canvas.ScrollOffset = value; }
    internal void ScrollToBlock(int index) { _canvas.Refresh(true); if (index >= 0 && index < _canvas.Views.Count) _canvas.ScrollOffset = Mathf.Max(0, _canvas.Views[index].Y - 14); }

    public void OnOpened()
    {
        if (!_author) _cascade.Play(_canvas.Scroll, AppearanceRows, layout: () => _canvas.Refresh(true));
    }
    public void OnClosed() => _cascade.PauseForExit();
    private IEnumerable<OverviewCascade.Row> AppearanceRows()
    {
        foreach (var view in _canvas.Views)
        {
            if (view.Block is ParagraphBlock paragraph && paragraph.Length == 0 &&
                view.Box?.gameObject.activeSelf != true && view.Bar?.gameObject.activeSelf != true &&
                view.Dot?.gameObject.activeSelf != true && view.Number?.gameObject.activeSelf != true) continue;
            // Paragraph roots have zero height; their text is placed in page coordinates.
            yield return new OverviewCascade.Row(view.Rect, view.Text != null ? view.Text.rectTransform : view.Rect);
        }
    }

    internal void SetControllerReading(bool enabled) { _controllerReading = enabled; _reading.gameObject.SetActive(enabled && !_author); }

    public void SetAuthorMode(bool authorMode)
    {
        _cascade.Finish();
        _author = authorMode;
        _reading.gameObject.SetActive(_controllerReading && !authorMode);
        PublicationNavigation.Invalidate(_rootRect);
        ClosePopover(); CloseSource(); _tools.Cancel();
        IntroductionFontRegistry.PackFontsDirectory = PackFontsDirectory();
        _canvas.Author = authorMode; _tools.Author = authorMode; _surface.TypingEnabled = authorMode; _surface.ApplyImeMode();
        _toolbar.Root.gameObject.SetActive(authorMode);
        if (!authorMode) { _editor.SetCaret(_editor.Selection.Focus, false); _canvas.Composition = ""; }
        _canvas.InvalidateAll(); _toolbarWidth = -1; LayoutChrome(); _canvas.Refresh(true);
        if (authorMode) { _wantFocus = true; _surface.Focus(); }
    }

    public void Reload()
    {
        if (!_cascade.BeforeRebuild()) return;
        float scrollOffset = _canvas.ScrollOffset;
        IntroductionFontRegistry.PackFontsDirectory = PackFontsDirectory();
        Document document;
        try { document = LoadDocument(out string? note); if (note != null) StatusChanged?.Invoke(note); }
        catch (Exception ex) { document = new Document(); StatusChanged?.Invoke(Loc.F("Could not read introduction: {0}", "介绍读取失败：{0}", "介紹讀取失敗：{0}", ex.Message)); }
        _editor.Reset(document);
        _savedJson = DocJson.Write(_editor.Doc); _dirtyRevision = -1;
        _loaded = true;
        _canvas.Refresh(true); _canvas.ScrollOffset = scrollOffset;
    }

    public void Save() => TrySave();

    public bool TryCloseLayer()
    {
        if (_tools.Busy) { _tools.Cancel(); return true; }
        if (_canvas.CloseEnlarged()) return true;
        if (_popover?.IsOpen == true) { ClosePopover(); return true; }
        if (_sourcePanel != null)
        {
            if (_sourceInput != null && _sourceInput.text != _sourceInitial) OverviewUi.ConfirmDiscard(Loc.T("Discard source edits?", "放弃源码中的修改？", "放棄原始碼中的修改？"), CloseSource, () => _sourceInput?.ActivateInputField());
            else CloseSource();
            return true;
        }
        return false;
    }

    public void RequestLeave(Action leave, Action? stay = null)
    {
        if (_importing) { StatusChanged?.Invoke(Loc.T("Importing assets. Wait for completion.", "正在导入素材，请等待完成。", "正在導入素材，請等待完成。")); return; }
        if (_surface.CompositionString.Length > 0) return;
        if (TryCloseLayer()) return;
        if (!IsDirty) { leave(); return; }
        OverviewUi.ConfirmUnsaved(_documentSource == null ? Loc.T("The introduction has unsaved changes", "介绍有未保存的修改", "介紹有未保存的修改") : Loc.T("The guide has unsaved changes", "说明有未保存的修改", "說明有未儲存的修改"), () => { Save(); if (!IsDirty) leave(); }, () => { _editor.Reset(DocJson.Read(_savedJson)); _dirtyRevision = -1; leave(); }, () => { _wantFocus = true; _surface.Focus(); stay?.Invoke(); });
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _cascade.Finish();
        _importCancellation.Cancel(); if (!_importing) _importCancellation.Dispose();
        ClosePopover(); _canvas.Dispose();
        if (Root != null) UnityEngine.Object.Destroy(Root);
    }

    // ---------------- loading & saving ----------------

    private string PackFontsDirectory()
    {
        string folder = OverviewRepository.SafePath(_documentSource?.RootDirectory ?? Repository.PackDirectory, "fonts");
        return folder;
    }

    private string ResolveMedia(string relative) => _documentSource?.ResolveMedia(relative) ?? Repository.ResolvePackPath(relative);
    private string EnsureEditableRoot() { if (_documentSource == null) return Repository.EnsureLocalPackCopy(); Directory.CreateDirectory(_documentSource.RootDirectory); return _documentSource.RootDirectory; }
    private string ExportsDirectory() => _documentSource?.ExportDirectory ?? Loc.PackOutputDirectory;
    private Document ImportMarkdown(string source) => _documentSource?.ImportMarkdown(source) ?? DocMarkdown.Import(source);

    private Document LoadDocument(out string? note)
    {
        note = null;
        if (_documentSource != null) { var document = _documentSource.Load(); note = _documentSource.ReadNote; return document; }
        string docPath = Repository.IntroductionDocumentPath, mdPath = Repository.IntroductionPath;
        if (docPath.Length != 0 && File.Exists(docPath)) return DocJson.Read(File.ReadAllText(docPath, Encoding.UTF8));
        if (mdPath.Length != 0 && File.Exists(mdPath)) return ImportMarkdown(File.ReadAllText(mdPath, Encoding.UTF8));
        return new Document();
    }

    internal bool TrySave()
    {
        if (!_author) return false;
        if (_surface.CompositionString.Length > 0) { StatusChanged?.Invoke(Loc.T("Finish the current text input before saving the document.", "请先完成当前文字输入，再保存文档。", "請先完成當前文字輸入，再儲存文件。")); return false; }
        try
        {
            if (_documentSource != null)
            {
                EnsureEditableRoot(); IntroductionFontRegistry.PackFontsDirectory = PackFontsDirectory();
                foreach (string font in _editor.Doc.FontIds()) IntroductionFontRegistry.AdoptIntoPack(font);
                _documentSource.Save(_editor.Doc); _savedJson = DocJson.Write(_editor.Doc); _dirtyRevision = -1;
                StatusChanged?.Invoke(Loc.T("Guide changes saved locally.", "说明修改已保存到本地。", "說明修改已儲存至本機。"));
                _toolbar.RefreshState(); DraftChanged?.Invoke(); return true;
            }
            EnsureEditableRoot();
            IntroductionFontRegistry.PackFontsDirectory = PackFontsDirectory();
            var pack = Repository.State.pack;
            if (string.IsNullOrWhiteSpace(pack.introDocument)) pack.introDocument = DocumentFile;
            if (string.IsNullOrWhiteSpace(pack.introFile)) pack.introFile = MarkdownFile;
            string docPath = Repository.IntroductionDocumentPath, mdPath = Repository.IntroductionPath;
            if (docPath.Length == 0 || mdPath.Length == 0) throw new IOException(Loc.T("Invalid introduction file path.", "介绍文件路径无效。", "介紹文件路徑無效。"));
            bool firstDocument = !File.Exists(docPath) && (!File.Exists(mdPath) || new FileInfo(mdPath).Length == 0);
            // The first JSON save replaces a hand-written Markdown file with an export; keep the original once.
            if (!File.Exists(docPath) && File.Exists(mdPath)) { string backup = mdPath + ".before-1.3.bak"; if (!File.Exists(backup)) File.Copy(mdPath, backup, false); }
            string json = DocJson.Write(_editor.Doc);
            var checkpoint = new ProfileMetadataCheckpoint(new[] { docPath, mdPath });
            try
            {
            OverviewRepository.AtomicWrite(docPath, json);
            OverviewRepository.AtomicWrite(mdPath, DocMarkdown.Export(_editor.Doc));
            foreach (string font in _editor.Doc.FontIds()) { try { IntroductionFontRegistry.AdoptIntoPack(font); } catch (Exception ex) { Plugin.Log.LogWarning("[介绍字体] 无法复制到整合包：" + ex.Message); } }
            bool hasContent = _editor.Doc.Blocks.Any(b => b is not ParagraphBlock p || p.Length > 0);
            if (firstDocument && hasContent) pack.introEnabled = true;
            Repository.SavePackInformation(ConfigurationSaveScope.Introduction);
            }
            catch { checkpoint.CaptureWritten(); checkpoint.Restore(); throw; }
            _savedJson = json; _dirtyRevision = -1;
            StatusChanged?.Invoke(Loc.F("Introduction saved to the shared modpack workspace: {0}", "介绍已保存到整合包共用工作区：{0}", "介紹已儲存到整合包共用工作區：{0}", docPath));
            _toolbar.RefreshState();
            return true;
        }
        catch (Exception ex) { StatusChanged?.Invoke(Loc.F("Save failed: {0}", "保存失败：{0}", "保存失敗：{0}", ex.Message)); return false; }
    }

    internal bool ExportPackData()
    {
        if (!_author) { StatusChanged?.Invoke(Loc.T("Enable author mode before exporting data.", "请先启用作者模式，再导出资料。", "請先啟用作者模式，再導出資料。")); return false; }
        try
        {
            if (!TrySave()) return false;
            if (_documentSource != null) { string output = _documentSource.Export(); OverviewUi.ShowText(Loc.T("Guide export complete", "说明资料已导出", "說明資料已匯出"), output); StatusChanged?.Invoke(output); return true; }
            PublishWizard.Show(Repository, new[] { Repository.ViewedProfileId }, message => StatusChanged?.Invoke(message));
            return true;
        }
        catch (Exception ex) { StatusChanged?.Invoke(Loc.F("Data export failed; no complete deliverable package was generated: {0}", "资料导出失败，未生成可交付完整包：{0}", "資料導出失敗，未生成可交付完整包：{0}", ex.Message)); return false; }
    }

    // ---------------- per-frame ----------------

    private float _toolbarWidth = -1;
    private void LayoutChrome()
    {
        float width = _rootRect.rect.width;
        float top = 0;
        if (_author)
        {
            if (Mathf.Abs(width - _toolbarWidth) > .5f) { _toolbarWidth = width; _toolbar.Layout(width); }
            var size = new Vector2(0, _toolbar.Height); if (_toolbar.Root.sizeDelta != size) _toolbar.Root.sizeDelta = size;
            top = _toolbar.Height + 6;
        }
        if (Mathf.Abs(_scrollRect.offsetMax.y + top) > .5f) _scrollRect.offsetMax = new Vector2(0, -top);
    }

    private Camera? EventCamera
    {
        get { var canvas = Root.GetComponentInParent<Canvas>(); return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera; }
    }

    private void Tick()
    {
        if (_disposed) return;
        LayoutChrome();
        string composition = _author && _surface.IsSelected ? _surface.CompositionString : "";
        if (composition != _canvas.Composition) { _canvas.Composition = composition; _canvas.MarkLayoutDirty(); }
        _canvas.Refresh();
        if (_toolbarRevision != _editor.Revision) { _toolbarRevision = _editor.Revision; _toolbar.RefreshState(); }

        Vector2? pointer = Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2?)null;
        if (_surface.Dragging && _surface.LastPointer != null && pointer != null) DragAutoScroll(pointer.Value);
        _tools.Update(pointer, EventCamera);

        bool focused = _surface.IsSelected;
        if (_author && _wantFocus && !focused && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null && !OverviewUi.HasModal && _sourcePanel == null && Root.activeInHierarchy)
            _surface.Focus();
        if (_author && focused && _canvas.CaretRect(_editor.Selection.Focus) is Rect caret)
        {
            _surface.SetCompositionCursor(_canvas.DocToScreen(new Vector2(caret.x, caret.yMax), EventCamera));
        }
        bool blinkOn = _author && focused && ((Time.unscaledTime - _blinkStart) % 1.05f) < .6f;
        _canvas.SetCaretBlink(blinkOn);
    }

    private void DragAutoScroll(Vector2 screen)
    {
        var viewport = _canvas.Viewport;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screen, EventCamera, out var local)) return;
        float speed = 0, edge = 24;
        if (local.y > viewport.rect.yMax - edge) speed = -(local.y - (viewport.rect.yMax - edge)) - 4;
        else if (local.y < viewport.rect.yMin + edge) speed = (viewport.rect.yMin + edge) - local.y + 4;
        if (speed == 0) return;
        _canvas.ScrollOffset += speed * Time.unscaledDeltaTime * 10;
        ExtendDrag(_canvas.ScreenToDoc(screen, EventCamera));
    }

    /// <summary>Re-render, keep the caret visible and restart its blink after an edit or move.</summary>
    private void Changed(bool reveal = true)
    {
        DraftChanged?.Invoke();
        _canvas.Refresh();
        _toolbarRevision = _editor.Revision; _toolbar.RefreshState();
        _blinkStart = Time.unscaledTime;
        if (reveal)
        {
            var o = _canvas.ObjectRect(_editor.Selection.Object);
            if (o != null) _canvas.Reveal(o.Value); else if (_canvas.CaretRect(_editor.Selection.Focus) is Rect r) _canvas.Reveal(r);
        }
        _canvas.UpdateOverlay();
    }

    // ---------------- keyboard ----------------

    private void OnKey(Event e)
    {
        bool ctrl = (e.modifiers & (EventModifiers.Control | EventModifiers.Command)) != 0;
        bool shift = (e.modifiers & EventModifiers.Shift) != 0, alt = (e.modifiers & EventModifiers.Alt) != 0;
        if (_popover?.IsOpen == true) { if (e.keyCode == KeyCode.Escape) ClosePopover(); return; }
        if (OverviewUi.HasModal) return;
        if (_surface.CompositionString.Length > 0 && e.keyCode != KeyCode.None) return;
        if (!_author) { ReadingKey(e, ctrl, shift); return; }
        var sel = _editor.Selection;
        bool hasObject = !sel.Object.IsNone;
        switch (e.keyCode)
        {
            case KeyCode.Backspace: _editor.DeleteBackward(ctrl); Changed(); return;
            case KeyCode.Delete: _editor.DeleteForward(ctrl); Changed(); return;
            case KeyCode.LeftArrow: case KeyCode.RightArrow:
            {
                int dir = e.keyCode == KeyCode.LeftArrow ? -1 : 1;
                if (hasObject) { LeaveObject(dir); }
                else if (!sel.Collapsed && !shift) _editor.SetCaret(dir < 0 ? sel.Start : sel.End, false);
                else _editor.SetCaret(_editor.MoveHorizontal(sel.Focus, dir, ctrl), shift);
                _goalX = -1; Changed(); return;
            }
            case KeyCode.UpArrow: case KeyCode.DownArrow:
            {
                int dir = e.keyCode == KeyCode.UpArrow ? -1 : 1;
                if (hasObject) { LeaveObject(dir); Changed(); return; }
                if (_goalX < 0 && _canvas.CaretRect(sel.Focus) is Rect r) _goalX = r.x;
                var target = _canvas.Vertical(sel.Focus, dir, _goalX);
                float keep = _goalX; _editor.SetCaret(target, shift); _goalX = keep; Changed(); return;
            }
            case KeyCode.Home: case KeyCode.End:
            {
                bool end = e.keyCode == KeyCode.End;
                var target = ctrl ? (end ? _editor.LastPos() : _editor.FirstPos()) : _canvas.LineEdge(sel.Focus, end);
                _editor.SetCaret(target, shift); _goalX = -1; Changed(); return;
            }
            case KeyCode.PageUp: case KeyCode.PageDown:
            {
                if (_canvas.CaretRect(sel.Focus) is not Rect r) return;
                float delta = (_canvas.Viewport.rect.height - 40) * (e.keyCode == KeyCode.PageUp ? -1 : 1);
                _canvas.ScrollOffset += delta;
                _editor.SetCaret(_canvas.PositionAt(new Vector2(_goalX >= 0 ? _goalX : r.x, r.center.y + delta)), shift); Changed(); return;
            }
            case KeyCode.Return: case KeyCode.KeypadEnter:
                _enterFrame = Time.frameCount;
                if (shift) _editor.InsertSoftBreak(); else _editor.SplitParagraph();
                Changed(); return;
            case KeyCode.Tab: _editor.ChangeIndent(shift ? -1 : 1); Changed(); return;
            case KeyCode.Escape:
                if (hasObject) { _editor.SetCaret(_editor.Clamp(new DocPos(sel.Object.Block + 1, 0)), false); Changed(false); }
                return;
        }
        if (ctrl && !alt && Shortcut(e.keyCode, shift)) return;
        char c = e.character;
        if (c == '\n' || c == '\r') { if (_enterFrame != Time.frameCount && e.keyCode == KeyCode.None) { _editor.SplitParagraph(); Changed(); } return; }
        if (c == '\t' || c < ' ' || c == '\u007f') return;
        if (ctrl && !alt) return;
        _editor.InsertText(c.ToString()); _goalX = -1; Changed();
    }

    private void LeaveObject(int dir)
    {
        var o = _editor.Selection.Object;
        int? target = dir < 0 ? _editor.PreviousParagraph(o.Block) : _editor.NextParagraph(o.Block);
        if (target == null) target = dir < 0 ? _editor.NextParagraph(o.Block) : _editor.PreviousParagraph(o.Block);
        if (target == null) return;
        var p = _editor.Para(target.Value)!;
        _editor.SetCaret(new DocPos(target.Value, dir < 0 && target < o.Block ? p.Length : 0), false);
    }

    private bool Shortcut(KeyCode key, bool shift)
    {
        switch (key)
        {
            case KeyCode.A: _editor.SelectAll(); break;
            case KeyCode.C: Copy(); return true;
            case KeyCode.X: Copy(); _editor.Cut(); break;
            case KeyCode.V: Paste(); break;
            case KeyCode.Z: if (shift) _editor.Redo(); else _editor.Undo(); break;
            case KeyCode.Y: _editor.Redo(); break;
            case KeyCode.B: _editor.ToggleMark(Mark.Bold); break;
            case KeyCode.I: _editor.ToggleMark(Mark.Italic); break;
            case KeyCode.U: _editor.ToggleMark(Mark.Underline); break;
            case KeyCode.D: _editor.ToggleMark(Mark.Strike); break;
            case KeyCode.K: EditLink(); return true;
            case KeyCode.S: Save(); return true;
            case KeyCode.L: if (shift) _editor.SetParagraphStyle(ParaStyle.Bullet); else _editor.SetAlign("left"); break;
            case KeyCode.E: _editor.SetAlign("center"); break;
            case KeyCode.R: _editor.SetAlign("right"); break;
            case KeyCode.Alpha0: case KeyCode.Keypad0: _editor.SetParagraphStyle(ParaStyle.Normal); break;
            case KeyCode.Alpha1: case KeyCode.Keypad1: _editor.SetParagraphStyle(ParaStyle.Heading1); break;
            case KeyCode.Alpha2: case KeyCode.Keypad2: _editor.SetParagraphStyle(ParaStyle.Heading2); break;
            case KeyCode.Alpha3: case KeyCode.Keypad3: _editor.SetParagraphStyle(ParaStyle.Heading3); break;
            case KeyCode.Alpha7: if (!shift) return false; _editor.SetParagraphStyle(ParaStyle.Numbered); break;
            case KeyCode.Alpha8: if (!shift) return false; _editor.SetParagraphStyle(ParaStyle.Bullet); break;
            case KeyCode.RightBracket: StepSize(1); break;
            case KeyCode.LeftBracket: StepSize(-1); break;
            default: return false;
        }
        Changed(); return true;
    }

    private void ReadingKey(Event e, bool ctrl, bool shift)
    {
        float page = _canvas.Viewport.rect.height - 40;
        switch (e.keyCode)
        {
            case KeyCode.C when ctrl: Copy(); break;
            case KeyCode.A when ctrl: _editor.SelectAll(); _canvas.UpdateOverlay(); break;
            case KeyCode.PageDown: _canvas.ScrollOffset += page; break;
            case KeyCode.PageUp: _canvas.ScrollOffset -= page; break;
            case KeyCode.DownArrow: _canvas.ScrollOffset += 60; break;
            case KeyCode.UpArrow: _canvas.ScrollOffset -= 60; break;
            case KeyCode.Home: _canvas.ScrollOffset = 0; break;
            case KeyCode.End: _canvas.ScrollOffset = float.MaxValue; break;
        }
    }

    private void Copy()
    {
        var fragment = _editor.Copy(); if (fragment == null) return;
        _clipboard = fragment; GUIUtility.systemCopyBuffer = fragment.PlainText;
    }
    private void Paste()
    {
        string text = (GUIUtility.systemCopyBuffer ?? "").Replace("\r\n", "\n");
        if (_clipboard != null && _clipboard.PlainText.Replace("\r\n", "\n") == text) _editor.Paste(_clipboard);
        else if (text.Length != 0) _editor.InsertText(text, "paste");
    }

    // ---------------- pointer ----------------

    private void OnPress(PointerEventData data, int clicks)
    {
        if (_popover?.IsOpen == true) ClosePopover();
        var doc = _canvas.ScreenToDoc(data.position, data.pressEventCamera);
        _goalX = -1; _blinkStart = Time.unscaledTime;
        bool shift = ModifierHeld(k => k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
        if (_author)
        {
            _wantFocus = true;
            var obj = _canvas.ObjectAt(doc);
            if (!obj.IsNone && _editor.Doc.Blocks[obj.Block] is DividerBlock) { _editor.SelectObject(obj); _dragGranularity = -1; Changed(false); return; }
        }
        var pos = _canvas.PositionAt(doc);
        _dragGranularity = clicks;
        if (clicks >= 3 && _editor.Para(pos.Block) is ParagraphBlock paragraph)
        { _dragAnchor = new DocPos(pos.Block, 0); _editor.SelectRange(_dragAnchor, new DocPos(pos.Block, paragraph.Length)); }
        else if (clicks == 2)
        { var (s, e) = _editor.WordAt(pos); _dragAnchor = new DocPos(pos.Block, s); _editor.SelectRange(_dragAnchor, new DocPos(pos.Block, e)); }
        else { _editor.SetCaret(pos, shift); _dragAnchor = _editor.Selection.Anchor; }
        Changed(false);
    }

    // The typing guard hides keyboard state from other mods while the page is focused; our own reads pass through it.
    private static bool ModifierHeld(Func<Keyboard, bool> test) => TypingGuard.ReadKeyboard(() => Keyboard.current != null && test(Keyboard.current));

    private void OnDrag(PointerEventData data) => ExtendDrag(_canvas.ScreenToDoc(data.position, data.pressEventCamera));

    private void ExtendDrag(Vector2 doc)
    {
        if (_dragGranularity < 0) return;
        var pos = _canvas.PositionAt(doc);
        if (_dragGranularity == 2 && _editor.Para(pos.Block) != null)
        {
            var (s, e) = _editor.WordAt(pos);
            pos = pos < _dragAnchor ? new DocPos(pos.Block, s) : new DocPos(pos.Block, e);
        }
        _editor.SelectRange(_dragAnchor, pos);
        _canvas.UpdateOverlay(); _toolbar.RefreshState();
        if (_canvas.CaretRect(pos) is Rect r) _canvas.Reveal(r);
    }

    private void OnClick(PointerEventData data)
    {
        if (!_editor.Selection.Collapsed) return;
        bool ctrl = ModifierHeld(k => k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed);
        if (_author && !ctrl) return;
        string url = _canvas.LinkAt(_canvas.ScreenToDoc(data.position, data.pressEventCamera));
        if (url.Length == 0) return;
        if (FollowLink != null) { FollowLink(url); return; }
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")) Application.OpenURL(url);
        else { GUIUtility.systemCopyBuffer = url; StatusChanged?.Invoke(Loc.F("Link copied: {0}", "链接已复制：{0}", "鏈接已復制：{0}", url)); }
    }

    // ---------------- toolbar ----------------

    private static string T(string en, string hans, string? hant = null) => Loc.T(en, hans, hant ?? hans);

    private TextStyle Current => _editor.StyleAtCaret();
    private ParagraphBlock? Paragraph => _editor.CurrentParagraph;

    private static string StyleName(ParaStyle style) => style switch
    {
        ParaStyle.Heading1 => T("Heading 1", "标题 1", "標題 1"), ParaStyle.Heading2 => T("Heading 2", "标题 2", "標題 2"), ParaStyle.Heading3 => T("Heading 3", "标题 3", "標題 3"),
        ParaStyle.Quote => T("Quote", "引用"), ParaStyle.Callout => T("Callout", "提示框"), ParaStyle.Bullet => T("Bullets", "项目符号", "項目符號"),
        ParaStyle.Numbered => T("Numbering", "编号", "編號"), _ => T("Body", "正文")
    };

    private void BuildToolbar()
    {
        var tb = _toolbar;
        tb.Add(T("Undo", "撤销", "復原"), () => { _editor.Undo(); Changed(); }, T("Undo (Ctrl+Z)", "撤销（Ctrl+Z）", "復原（Ctrl+Z）"), enabled: () => _editor.CanUndo);
        tb.Add(T("Redo", "重做", "重做"), () => { _editor.Redo(); Changed(); }, T("Redo (Ctrl+Y)", "重做（Ctrl+Y）"), enabled: () => _editor.CanRedo);
        tb.Group();
        DocToolButton? style = null; style = tb.Add(StyleName(ParaStyle.Normal), () => StyleMenu(style!), T("Paragraph style (Ctrl+0/1/2/3)", "段落样式（Ctrl+0/1/2/3）", "段落樣式（Ctrl+0/1/2/3）"), dropdown: true, minWidth: 92,
            dynamicCaption: () => StyleName(Paragraph?.Style is ParaStyle s && s != ParaStyle.Bullet && s != ParaStyle.Numbered ? s : ParaStyle.Normal));
        DocToolButton? font = null; font = tb.Add(T("Default font", "默认字体", "預設字型"), () => FontMenu(font!), T("Font", "字体", "字型"), dropdown: true, minWidth: 126,
            dynamicCaption: () => IntroductionFontRegistry.ShortLabel(Current.Font));
        DocToolButton? size = null; size = tb.Add("22", () => SizeMenu(size!), T("Font size (Ctrl+[ / Ctrl+])", "字号（Ctrl+[ / Ctrl+]）", "字號（Ctrl+[ / Ctrl+]）"), dropdown: true, minWidth: 54,
            dynamicCaption: () => (Current.Size != 0 ? Current.Size : (int)DocCanvas.BaseSize(Paragraph?.Style ?? ParaStyle.Normal)).ToString());
        tb.Add("A+", () => { StepSize(1); Changed(); }, T("Larger", "增大字号", "增大字號"));
        tb.Add("A-", () => { StepSize(-1); Changed(); }, T("Smaller", "减小字号", "減小字號"));
        tb.Group();
        tb.Add("<b>B</b>", () => { _editor.ToggleMark(Mark.Bold); Changed(); }, T("Bold (Ctrl+B)", "粗体（Ctrl+B）", "粗體（Ctrl+B）"), () => _editor.AllHave(s => s.Bold), minWidth: 30);
        tb.Add("<i>I</i>", () => { _editor.ToggleMark(Mark.Italic); Changed(); }, T("Italic (Ctrl+I)", "斜体（Ctrl+I）", "斜體（Ctrl+I）"), () => _editor.AllHave(s => s.Italic), minWidth: 30);
        tb.Add("<u>U</u>", () => { _editor.ToggleMark(Mark.Underline); Changed(); }, T("Underline (Ctrl+U)", "下划线（Ctrl+U）", "底線（Ctrl+U）"), () => _editor.AllHave(s => s.Underline), minWidth: 30);
        tb.Add("<s>S</s>", () => { _editor.ToggleMark(Mark.Strike); Changed(); }, T("Strikethrough (Ctrl+D)", "删除线（Ctrl+D）", "刪除線（Ctrl+D）"), () => _editor.AllHave(s => s.Strike), minWidth: 30);
        DocToolButton? color = null; color = tb.Add(T("Color", "文字色", "文字色"), () => ColorMenu(color!), T("Text color", "文字颜色", "文字顏色"), dropdown: true);
        DocToolButton? mark = null; mark = tb.Add(T("Highlight", "高亮"), () => HighlightMenu(mark!), T("Highlight", "高亮底纹", "螢光標記"), dropdown: true);
        tb.Group();
        tb.Add(T("Left", "左"), () => { _editor.SetAlign("left"); Changed(); }, T("Align left (Ctrl+L)", "左对齐（Ctrl+L）", "靠左（Ctrl+L）"), () => Paragraph?.Align == "left");
        tb.Add(T("Center", "中"), () => { _editor.SetAlign("center"); Changed(); }, T("Center (Ctrl+E)", "居中（Ctrl+E）", "置中（Ctrl+E）"), () => Paragraph?.Align == "center");
        tb.Add(T("Right", "右"), () => { _editor.SetAlign("right"); Changed(); }, T("Align right (Ctrl+R)", "右对齐（Ctrl+R）", "靠右（Ctrl+R）"), () => Paragraph?.Align == "right");
        tb.Add(T("Bullets", "• 列表", "• 清單"), () => { _editor.SetParagraphStyle(ParaStyle.Bullet); Changed(); }, T("Bulleted list (Ctrl+Shift+8, or type - and space)", "项目符号（Ctrl+Shift+8，或输入 - 加空格）", "項目符號（Ctrl+Shift+8，或輸入 - 加空格）"), () => Paragraph?.Style == ParaStyle.Bullet);
        tb.Add(T("1. List", "1. 编号", "1. 編號"), () => { _editor.SetParagraphStyle(ParaStyle.Numbered); Changed(); }, T("Numbered list (Ctrl+Shift+7, or type 1. and space)", "编号列表（Ctrl+Shift+7，或输入 1. 加空格）", "編號清單（Ctrl+Shift+7，或輸入 1. 加空格）"), () => Paragraph?.Style == ParaStyle.Numbered);
        tb.Add(T("Outdent", "减缩进", "減縮排"), () => { _editor.ChangeIndent(-1); Changed(); }, T("Decrease indent (Shift+Tab)", "减少缩进（Shift+Tab）", "減少縮排（Shift+Tab）"));
        tb.Add(T("Indent", "加缩进", "加縮排"), () => { _editor.ChangeIndent(1); Changed(); }, T("Increase indent (Tab)", "增加缩进（Tab）", "增加縮排（Tab）"));
        DocToolButton? fill = null; fill = tb.Add(T("Shading", "底色"), () => BackgroundMenu(fill!), T("Paragraph shading", "段落底色"), dropdown: true, active: () => Paragraph?.Background.Length > 0);
        tb.Group();
        tb.Add(T("Link", "链接", "連結"), EditLink, T("Insert or edit link (Ctrl+K); Ctrl+click opens", "插入或修改链接（Ctrl+K）；Ctrl+单击打开", "插入或修改連結（Ctrl+K）；Ctrl+點擊開啟"), () => Current.Link.Length > 0);
        DocToolButton? insert = null; insert = tb.Add(T("Insert", "插入"), () => InsertMenu(insert!), T("Pictures, GIF, video, divider", "图片、GIF、视频、分割线", "圖片、GIF、影片、分隔線"), dropdown: true);
        tb.Group();
        DocToolButton? more = null; more = tb.Add(T("More", "更多"), () => MoreMenu(more!), T("Markdown source, import/export, demo", "Markdown 源码、导入导出、示范稿", "Markdown 原始碼、匯入匯出、示範稿"), dropdown: true);
        tb.Add(T("Save", "保存", "儲存"), () => { Save(); }, T("Save (Ctrl+S)", "保存（Ctrl+S）", "儲存（Ctrl+S）"), () => IsDirty, role: UiActionRole.Commit);
    }

    private DocPopover OpenMenu(DocToolButton anchor, float width, int columns = 1, float cell = 0)
    {
        ClosePopover();
        _popover = new DocPopover(_menuLayer, (RectTransform)anchor.transform, width, _factory, columns, cell);
        _popover.Closed = () => { _popover = null; _wantFocus = true; _surface.Focus(); };
        return _popover;
    }
    private void ClosePopover() { var p = _popover; _popover = null; p?.Close(); }

    private void StyleMenu(DocToolButton anchor)
    {
        var menu = OpenMenu(anchor, 190);
        var current = Paragraph?.Style ?? ParaStyle.Normal;
        foreach (var (style, size) in new[] { (ParaStyle.Normal, 17), (ParaStyle.Heading1, 24), (ParaStyle.Heading2, 21), (ParaStyle.Heading3, 18), (ParaStyle.Quote, 17), (ParaStyle.Callout, 17) })
        {
            var s = style;
            string caption = s == ParaStyle.Quote ? "<i>" + StyleName(s) + "</i>" : s == ParaStyle.Heading1 || s == ParaStyle.Heading2 ? "<color=#DDC374>" + StyleName(s) + "</color>" : StyleName(s);
            menu.Item(caption, () => { _editor.SetParagraphStyle(s); Changed(); }, current == s, s == ParaStyle.Heading1 ? 34 : 30, size);
        }
    }

    private void FontMenu(DocToolButton anchor)
    {
        var menu = OpenMenu(anchor, 280);
        string current = Current.Font;
        foreach (var (label, id) in IntroductionFontRegistry.Options())
        {
            string choice = id == IntroductionFontRegistry.DefaultId ? "" : id;
            string caption = label;
            // Preview with the face itself only when it is already loaded (game fonts); imported files load on use.
            if (id.StartsWith("game:", StringComparison.Ordinal)) caption = "<font=\"" + IntroductionFontRegistry.RegistryName(id) + "\">" + label + "</font>";
            menu.Item(caption, () => { _editor.ApplyStyle(s => s.WithFont(choice)); if (choice.StartsWith("file:", StringComparison.Ordinal)) TryAdopt(choice); Changed(); }, choice == current, 28, 15);
        }
        menu.Separator();
        menu.Item(T("Import font (TTF / OTF / TTC)", "导入字体（TTF / OTF / TTC）", "匯入字型（TTF / OTF / TTC）"), ImportFont, false, 28, 14);
        menu.Item(T("Open fonts folder (TTF / OTF / TTC)", "打开字体目录（TTF / OTF / TTC）", "開啟字型目錄（TTF / OTF / TTC）"), () =>
        {
            try { EnsureEditableRoot(); IntroductionFontRegistry.PackFontsDirectory = PackFontsDirectory(); IntroductionFontRegistry.EnsureDirectory(); Application.OpenURL(new Uri(IntroductionFontRegistry.WritableDirectory).AbsoluteUri); StatusChanged?.Invoke(Loc.F("Place font files in {0}, then reopen the font menu. They will be included when exporting this document.", "把字体文件放进：{0}，再打开字体菜单即可选择。导出当前文档资料时会一并带上。", "將字型檔案放進：{0}，再開啟字型選單即可選擇。匯出目前文件資料時會一併附上。", IntroductionFontRegistry.WritableDirectory)); }
            catch (Exception ex) { StatusChanged?.Invoke(Loc.F("Could not open fonts directory: {0}", "无法打开字体目录：{0}", "無法打開字體目錄：{0}", ex.Message)); }
        }, false, 28, 14);
    }
    private void TryAdopt(string id) { try { IntroductionFontRegistry.AdoptIntoPack(id); } catch (Exception ex) { StatusChanged?.Invoke(Loc.F("Could not copy the font into the modpack directory: {0}", "字体未能复制到整合包目录：{0}", "字體未能復制到整合包目錄：{0}", ex.Message)); } }
    private void ImportFont()
    {
        if (!_author || _importing) return;
        OverviewUi.Ask(Loc.T("Import document font", "导入文档字体", "匯入文件字型"), new[] { (Loc.T("Local font path (TTF / OTF / TTC)", "本地字体路径（TTF / OTF / TTC）", "本地字體路徑（TTF / OTF / TTC）"), "") }, values => {
            string path = values[0].Trim().Trim('"');
            if (!File.Exists(path)) throw new FileNotFoundException(Loc.T("Font file not found.", "找不到字体文件。", "找不到字體文件。"), path);
            _ = ImportFontFile(path);
        });
    }
    private async System.Threading.Tasks.Task ImportFontFile(string path)
    {
        _importing = true;
        try
        {
            string root = EnsureEditableRoot();
            var imported = await OverviewProfileAssets.ImportFontAsync(path, root, IntroductionMedia.ImportLimitBytes, _importCancellation.Token);
            if (_disposed || !Root.activeInHierarchy) return;
            IntroductionFontRegistry.PackFontsDirectory = PackFontsDirectory();
            string id = "file:" + Path.GetFileName(imported.RelativePath);
            if (!IntroductionFontRegistry.TryResolve(id, out _, out string error)) throw new IOException(error);
            _editor.ApplyStyle(style => style.WithFont(id)); Changed();
            StatusChanged?.Invoke(imported.Reused ? Loc.T("Reused the identical font already in this document.", "已复用当前文档内的相同字体。", "已復用目前文件內的相同字型。") : Loc.T("Font imported into the document assets and included when exporting them.", "字体已导入文档素材目录，可随资料导出。", "字型已匯入文件素材目錄，可隨資料匯出。"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusChanged?.Invoke(Loc.F("Font import failed: {0}", "字体导入失败：{0}", "字體導入失敗：{0}", ex.Message)); }
        finally { _importing = false; }
    }

    private void SizeMenu(DocToolButton anchor)
    {
        var menu = OpenMenu(anchor, 92);
        int current = Current.Size;
        menu.Item(T("Auto", "默认"), () => { _editor.ApplyStyle(s => s.WithSize(0)); Changed(); }, current == 0, 26, 14);
        foreach (int size in FontSizes) { int s = size; menu.Item(s.ToString(), () => { _editor.ApplyStyle(st => st.WithSize(s)); Changed(); }, current == s, 26, 15); }
    }

    private void StepSize(int direction)
    {
        int current = Current.Size != 0 ? Current.Size : (int)DocCanvas.BaseSize(Paragraph?.Style ?? ParaStyle.Normal);
        int next = direction > 0 ? FontSizes.FirstOrDefault(s => s > current) : FontSizes.LastOrDefault(s => s < current);
        if (next == 0) return;
        _editor.ApplyStyle(s => s.WithSize(next));
    }

    private void ColorMenu(DocToolButton anchor)
    {
        var menu = OpenMenu(anchor, 6 * 34 + 14, 6, 30);
        string current = Current.Color;
        foreach (string hex in OverviewUi.Palette.Concat(new[] { "#FFFFFF", "#9A968A", "#E84545", "#F2D35B", "#4FA3E0", "#000000" }).Distinct())
        {
            string choice = hex.ToUpperInvariant();
            menu.Swatch("", DocCanvas.ParseColor(choice, Color.white), () => { _editor.ApplyStyle(s => s.WithColor(choice)); Changed(); }, choice == current);
        }
        menu.Separator();
        var reset = menu.Item(T("Automatic", "自动（随段落）", "自動（隨段落）"), () => { _editor.ApplyStyle(s => s.WithColor("")); Changed(); }, current.Length == 0, 26, 14);
    }

    private void HighlightMenu(DocToolButton anchor)
    {
        var menu = OpenMenu(anchor, 6 * 34 + 14, 6, 30);
        string current = Current.Highlight;
        foreach (string hex in Highlights) { string choice = hex; menu.Swatch("", DocCanvas.ParseColor(choice, Color.yellow), () => { _editor.ApplyStyle(s => s.WithHighlight(choice)); Changed(); }, choice == current); }
        menu.Separator();
        menu.Item(T("No highlight", "无高亮", "無標記"), () => { _editor.ApplyStyle(s => s.WithHighlight("")); Changed(); }, current.Length == 0, 26, 14);
    }

    private void BackgroundMenu(DocToolButton anchor)
    {
        var menu = OpenMenu(anchor, 6 * 34 + 14, 6, 30);
        string current = Paragraph?.Background ?? "";
        foreach (string hex in Backgrounds) { string choice = hex; menu.Swatch("", DocCanvas.ParseColor(choice, Color.gray), () => { _editor.SetBackground(choice); Changed(); }, choice == current); }
        menu.Separator();
        menu.Item(T("None", "无底色", "無底色"), () => { _editor.SetBackground(""); Changed(); }, current.Length == 0, 26, 14);
    }

    private void InsertMenu(DocToolButton anchor)
    {
        var menu = OpenMenu(anchor, 170);
        menu.Item(T("Picture (PNG/JPEG)", "图片（PNG/JPEG）", "圖片（PNG/JPEG）"), () => ImportMedia("image", null));
        menu.Item(T("Animated GIF", "GIF 动图", "GIF 動圖"), () => ImportMedia("gif", null));
        menu.Item(T("Short video", "短视频", "短影片"), () => ImportMedia("video", null));
        menu.Item(T("Divider", "分割线", "分隔線"), () => { _editor.InsertBlock(new DividerBlock()); Changed(); });
        menu.Item(T("Link", "链接", "連結"), EditLink);
    }

    private void MoreMenu(DocToolButton anchor)
    {
        var menu = OpenMenu(anchor, 230);
        menu.Item(T("Edit Markdown source", "编辑 Markdown 源码", "編輯 Markdown 原始碼"), OpenSource);
        menu.Item(T("Import Markdown file…", "导入 Markdown 文件…", "匯入 Markdown 檔案…"), ImportMarkdownFile);
        menu.Item(T("Save a Markdown copy", "另存 Markdown 副本", "另存 Markdown 副本"), ExportMarkdownCopy, role: UiActionRole.Commit);
        menu.Item(T("Load demo draft", "载入示范稿", "載入示範稿"), LoadDemo);
        menu.Separator();
        menu.Item(_documentSource == null ? T("Export pack data", "导出整合包资料", "匯出整合包資料") : T("Export guide data", "导出说明资料", "匯出說明資料"), () => ExportPackData(), role: UiActionRole.Commit);
        menu.Item(T("Media decode budget", "媒体解码预算", "媒體解碼預算"), () => OverviewUi.Ask(T("Decode budget in megapixels (1–256; 1 GB per file)", "解码像素百万（1–256；单文件上限 1GB）", "解碼像素百萬（1–256；單檔上限 1GB）"),
            new[] { (T("Megapixels", "百万像素", "百萬像素"), (IntroductionMedia.PixelBudget / 1024 / 1024).ToString()) },
            values => { if (int.TryParse(values[0], out int budget) && budget >= 1 && budget <= 256) IntroductionMedia.PixelBudget = budget * 1024 * 1024; }));
    }

    // ---------------- dialogs ----------------

    private void EditLink()
    {
        ClosePopover();
        var sel = _editor.Selection;
        if (!sel.Object.IsNone) return;
        if (sel.Collapsed && _editor.LinkRangeAtCaret() is { } range) { _editor.SelectRange(range.Start, range.End); sel = _editor.Selection; Changed(false); }
        if (!sel.Collapsed)
        {
            string existing = _editor.Para(sel.Start.Block) is ParagraphBlock p && sel.Start.Offset < p.Length ? p.StyleAt(sel.Start.Offset).Link : "";
            OverviewUi.Ask(T("Link address (empty removes the link)", "链接地址（留空则移除链接）", "連結網址（留空則移除連結）"), new[] { (T("Address", "地址", "網址"), existing.Length != 0 ? existing : "https://") },
                values => { string url = values[0].Trim(); if (url == "https://") url = ""; _editor.ApplyStyle(s => s.WithLink(url)); Changed(); _wantFocus = true; });
            return;
        }
        OverviewUi.Ask(T("Insert link", "插入链接", "插入連結"), new[] { (T("Text", "显示文字", "顯示文字"), ""), (T("Address", "地址", "網址"), "https://") },
            values => { string url = values[1].Trim(); if (url.Length == 0 || url == "https://") return; _editor.InsertLink(values[0], url); Changed(); _wantFocus = true; });
    }

    private void EditCaption(DocObject o)
    {
        var media = _editor.MediaOf(o); if (media == null) return;
        OverviewUi.Ask(T("Media caption (alt text)", "媒体说明（替代文字）", "媒體說明（替代文字）"), new[] { (T("Caption", "说明", "說明"), media.Caption) },
            values => { _editor.SetMediaCaption(o, values[0]); Changed(false); _wantFocus = true; });
    }

    private void OpenSource()
    {
        CloseSource();
        var shade = DocCanvas.Node(_rootRect, "MarkdownSource"); shade.anchorMin = Vector2.zero; shade.anchorMax = Vector2.one; shade.offsetMin = shade.offsetMax = Vector2.zero;
        var image = shade.gameObject.AddComponent<Image>(); image.color = ModConfigLook.Surface(new Color(.06f, .05f, .04f, .97f));
        shade.gameObject.AddComponent<DocEventSink>();
        _sourcePanel = shade.gameObject;
        var title = _factory(shade, T("Markdown source — Apply replaces the page (undoable)", "Markdown 源码 — 应用后替换整页（可撤销）", "Markdown 原始碼 — 套用後取代整頁（可復原）"), 18);
        DocCanvas.TopLeft(title.rectTransform); title.rectTransform.anchoredPosition = new Vector2(12, -8); title.rectTransform.sizeDelta = new Vector2(900, 28);
        _sourceInitial = DocMarkdown.Export(_editor.Doc);
        _sourceInput = OverviewUi.Input(shade, _sourceInitial, true);
        var ir = (RectTransform)_sourceInput.transform; ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one; ir.offsetMin = new Vector2(10, 52); ir.offsetMax = new Vector2(-10, -40);
        _sourceInput.textComponent.fontSize = 17; _sourceInput.characterLimit = 0;
        var apply = OverviewUi.Button(shade, T("Apply", "应用", "套用"), () =>
        {
            string text = _sourceInput?.text ?? ""; CloseSource();
            try { _editor.ReplaceDocument(ImportMarkdown(text)); Changed(); } catch (Exception ex) { StatusChanged?.Invoke(Loc.F("Could not parse source: {0}", "源码解析失败：{0}", "源碼解析失敗：{0}", ex.Message)); }
        }, 120, 34, role: UiActionRole.Commit);
        OverviewUi.Bottom((RectTransform)apply.transform, 10, 10);
        var cancel = OverviewUi.Button(shade, T("Cancel", "取消"), () => TryCloseLayer(), 120, 34, role: UiActionRole.Neutral);
        OverviewUi.Bottom((RectTransform)cancel.transform, 140, 10);
        EventSystem.current?.SetSelectedGameObject(_sourceInput.gameObject); _sourceInput.ActivateInputField();
    }
    private void CloseSource()
    {
        if (_sourcePanel == null) return;
        var panel = _sourcePanel; _sourcePanel = null; _sourceInput = null;
        panel.SetActive(false); UnityEngine.Object.Destroy(panel);
        _wantFocus = true;
    }

    private void ImportMarkdownFile()
    {
        if (!IntroductionFilePicker.TrySelect("markdown", out string? path) || string.IsNullOrEmpty(path)) return;
        try { EnsureEditableRoot(); IntroductionFontRegistry.PackFontsDirectory = PackFontsDirectory(); _editor.ReplaceDocument(ImportMarkdown(File.ReadAllText(path, Encoding.UTF8))); Changed(); StatusChanged?.Invoke(Loc.T("Markdown imported. Save to use it, or undo to restore the previous document. Media paths are relative to the current document asset directory.", "已导入 Markdown，保存后生效；可用撤销恢复。媒体路径按当前文档素材目录解析。", "已導入 Markdown，保存后生效；可用撤銷恢復。媒體路徑以目前文件素材目錄解析。")); }
        catch (Exception ex) { StatusChanged?.Invoke(Loc.F("Import failed: {0}", "导入失败：{0}", "導入失敗：{0}", ex.Message)); }
    }

    private void ExportMarkdownCopy()
    {
        try
        {
            string path = Path.Combine(ExportsDirectory(), (_documentSource == null ? "introduction_" : "guide_") + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, DocMarkdown.Export(_editor.Doc), new UTF8Encoding(false));
            StatusChanged?.Invoke(Loc.F("Markdown copy saved: {0}", "Markdown 副本已保存：{0}", "Markdown 副本已保存：{0}", path));
        }
        catch (Exception ex) { StatusChanged?.Invoke(Loc.F("Save copy failed: {0}", "另存失败：{0}", "另存失敗：{0}", ex.Message)); }
    }

    private void LoadDemo()
    {
        async void Open()
        {
            if (_importing || _disposed) return;
            _importing = true;
            try
            {
                string directory = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "Demo");
                string file = Path.Combine(directory, "introduction.md"); if (!File.Exists(file)) throw new FileNotFoundException(Loc.T("Demo data is not installed", "示范资料未安装", "示范資料未安裝"), file);
                string source = File.ReadAllText(file, Encoding.UTF8), root = EnsureEditableRoot();
                string media = Path.Combine(directory, "media");
                if (Directory.Exists(media)) foreach (string asset in OverviewRepository.SafeFiles(media))
                {
                    string relative = "media/" + Path.GetFileName(asset);
                    var imported = await OverviewProfileAssets.ImportMediaAsync(asset, root, IntroductionMedia.ImportLimitBytes, _importCancellation.Token);
                    if (_disposed) return;
                    source = source.Replace(relative, imported.RelativePath);
                }
                IntroductionFontRegistry.PackFontsDirectory = PackFontsDirectory();
                _editor.ReplaceDocument(ImportMarkdown(source)); Changed(); _canvas.ScrollOffset = 0;
                StatusChanged?.Invoke(Loc.T("Demo draft loaded but not saved. Save to replace the current document, or undo to restore it.", "示范草稿已载入，尚未保存；点击保存才替换当前文档，可用撤销恢复。", "示範草稿已載入，尚未儲存；點擊儲存才取代目前文件，可用復原恢復。"));
            }
            catch (OperationCanceledException) { if (!_disposed) StatusChanged?.Invoke(Loc.T("Loading the demo draft was canceled.", "载入示范草稿已取消。", "載入示范草稿已取消。")); }
            catch (Exception ex) { if (!_disposed) StatusChanged?.Invoke(Loc.F("Could not load demo draft: {0}", "载入示范草稿失败：{0}", "載入示范草稿失敗：{0}", ex.Message)); }
            finally { _importing = false; if (_disposed) _importCancellation.Dispose(); }
        }
        if (IsDirty) OverviewUi.ConfirmDiscard(Loc.T("Loading the demo draft replaces the current unsaved content (undoable). Continue?", "载入示范草稿会替换当前未保存的内容（可撤销），继续？", "載入示范草稿會替換當前未保存的內容（可撤銷），繼續？"), Open); else Open();
    }

    // ---------------- media import ----------------

    private static string KindName(MediaKind kind) => kind == MediaKind.Gif ? "gif" : kind == MediaKind.Video ? "video" : "image";

    private void ImportMedia(string type, DocObject? replace)
    {
        ClosePopover();
        if (_importing || _disposed) { if (!_disposed) StatusChanged?.Invoke(Loc.T("Importing assets. Wait for completion.", "正在导入素材，请等待完成。", "正在導入素材，請等待完成。")); return; }
        if (IntroductionFilePicker.TrySelect(type, out string? selected)) { if (!string.IsNullOrEmpty(selected)) ImportPath(type, selected!, replace); return; }
        OverviewUi.Ask(Loc.F("Full path to the local {0} file", "输入本地{0}文件完整路径", "輸入本機{0}檔案完整路徑", type == "video" ? Loc.T("video", "视频", "影片") : type == "gif" ? "GIF" : Loc.T("image", "图片", "圖片")), new[] { (Loc.T("Path", "路径", "路徑"), "") }, values => ImportPath(type, values[0], replace));
    }

    private async void ImportPath(string type, string path, DocObject? replace)
    {
        if (_importing || _disposed) return; _importing = true;
        try
        {
            string root = EnsureEditableRoot();
            IntroductionFontRegistry.PackFontsDirectory = PackFontsDirectory();
            StatusChanged?.Invoke(Loc.T("Importing assets. Keep this page open until completion.", "正在导入素材，完成前请勿关闭本页。", "正在導入素材，完成前請勿關閉本頁。"));
            var imported = await OverviewProfileAssets.ImportMediaAsync(path, root, IntroductionMedia.ImportLimitBytes, _importCancellation.Token);
            _importCancellation.Token.ThrowIfCancellationRequested(); if (_disposed) return;
            var kind = type == "gif" ? MediaKind.Gif : type == "video" ? MediaKind.Video : MediaKind.Image;
            if (replace is DocObject o && _editor.MediaOf(o) != null) _editor.ReplaceMedia(o, imported.RelativePath, kind);
            else _editor.InsertBlock(new MediaBlock { Kind = kind, Source = imported.RelativePath, Width = kind == MediaKind.Image ? 100 : 75 });
            Changed(); StatusChanged?.Invoke(imported.Reused ? Loc.T("Reused the identical media file already in this document.", "已复用当前文档内相同的媒体文件。", "已復用目前文件內相同的媒體檔案。") : Loc.T("Media imported into this document's asset directory.", "媒体已导入当前文档素材目录。", "媒體已匯入目前文件素材目錄。"));
        }
        catch (OperationCanceledException) { if (!_disposed) StatusChanged?.Invoke(Loc.T("Asset import canceled.", "素材导入已取消。", "素材導入已取消。")); }
        catch (Exception ex) { if (!_disposed) StatusChanged?.Invoke(Loc.F("Asset import failed: {0}. Incomplete .partial files were kept.", "素材导入失败：{0}；未完成的 .partial 文件会保留。", "素材導入失敗：{0}；未完成的 .partial 文件會保留。", ex.Message)); }
        finally { _importing = false; if (_disposed) _importCancellation.Dispose(); }
    }
}
