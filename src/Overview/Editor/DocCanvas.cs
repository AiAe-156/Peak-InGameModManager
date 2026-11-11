#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview.Editor;

/// <summary>
/// Renders a <see cref="Document"/> as one continuous page and answers geometry questions
/// (caret rectangles, hit testing, line navigation). Coordinates named "doc" are measured from
/// the page's top-left corner, x to the right and y downwards.
/// </summary>
internal sealed class DocCanvas
{
    internal const float PadX = 30, PadTop = 18, PadBottom = 48, IndentStep = 28, ListGutter = 26, BoxPadX = 14, BoxPadY = 9, RowGap = 12;
    internal static readonly Color Ink = new(.94f, .92f, .87f, 1), Gold = new(.87f, .76f, .45f, 1), Muted = new(.78f, .76f, .70f, 1);
    internal static readonly Color SelectionColor = new(.22f, .55f, .95f, .38f), CaretColor = new(.98f, .95f, .86f, 1);

    internal sealed class MediaCell
    {
        public MediaBlock Block = null!; public RectTransform Rect = null!; public IntroductionMedia Media = null!;
        public string Source = "", ResolvedPath = ""; public MediaKind Kind;
        public float X, Y, W, H;
        public bool Shows(MediaBlock media, string resolvedPath) => media.Kind == Kind && string.Equals(media.Source, Source, StringComparison.OrdinalIgnoreCase) && string.Equals(resolvedPath, ResolvedPath, StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class BlockView
    {
        public DocBlock Block = null!; public RectTransform Rect = null!;
        public float Y, Height, SpaceBefore;
        public TextMeshProUGUI? Text; public int[] Map = Array.Empty<int>(); public int[] ModelToChar = Array.Empty<int>();
        public string Rich = ""; public float TextX, TextTop, TextWidth, FontSize; public bool Dirty = true;
        public Image? Box, Bar; public TextMeshProUGUI? Number; public DocDotGraphic? Dot; public TextMeshProUGUI? Placeholder;
        public readonly List<MediaCell> Cells = new();
        public float ObjectX, ObjectWidth;
    }

    public RectTransform Content { get; }
    public ScrollRect Scroll { get; }
    public RectTransform Viewport => Scroll.viewport;
    public DocEditor Editor;
    public bool Author;
    // Each reader can use its own text size without changing profile introductions.
    public float TextScale = 1;
    public Func<string, string> ResolveMedia = p => p;
    public Action<string>? Status;
    public Action<DocObject>? ObjectClicked;
    public string Composition = "";
    public IReadOnlyList<BlockView> Views => _views;
    public float PageWidth => Content.rect.width;
    public float Usable => Mathf.Max(80, PageWidth - PadX * 2);

    private readonly Func<Transform, string, float, TextMeshProUGUI> _factory;
    private readonly Transform _modalParent;
    private readonly List<BlockView> _views = new();
    private readonly Dictionary<string, IntroductionMediaState> _mediaStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly RectTransform _blocks, _overlay;
    private readonly DocSelectionGraphic _selection;
    private readonly Image _caret;
    private readonly DocFrameGraphic _objectFrame;
    private readonly TextMeshProUGUI _empty;
    private float _measuredWidth = -1;
    private int _syncedRevision = -1;
    private bool _layoutPending;

    public DocCanvas(RectTransform content, ScrollRect scroll, DocEditor editor, Func<Transform, string, float, TextMeshProUGUI> factory, Transform modalParent)
    {
        Content = content; Scroll = scroll; Editor = editor; _factory = factory; _modalParent = modalParent;
        _blocks = Node(content, "Blocks"); Stretch(_blocks);
        _overlay = Node(content, "Overlay"); Stretch(_overlay);
        _selection = _overlay.gameObject.AddComponent<DocSelectionGraphic>(); _selection.color = SelectionColor; _selection.raycastTarget = false;
        var caret = Node(_overlay, "Caret"); TopLeft(caret); _caret = caret.gameObject.AddComponent<Image>(); _caret.color = CaretColor; _caret.raycastTarget = false; caret.gameObject.SetActive(false);
        var frame = Node(_overlay, "ObjectFrame"); TopLeft(frame); _objectFrame = frame.gameObject.AddComponent<DocFrameGraphic>(); _objectFrame.color = new Color(.36f, .68f, 1f, .95f); _objectFrame.raycastTarget = false; frame.gameObject.SetActive(false);
        _empty = factory(_blocks, Loc.T("No pack introduction yet.", "暂无整合包介绍。", "暫無整合包介紹。"), 22); _empty.color = Muted; TopLeft(_empty.rectTransform); _empty.gameObject.SetActive(false);
    }

    internal RectTransform OverlayRoot => _overlay;

    // ---------------- sync & layout ----------------

    public void MarkLayoutDirty() => _layoutPending = true;
    public void InvalidateAll() { foreach (var v in _views) v.Dirty = true; _layoutPending = true; }

    /// <summary>Brings views in line with the document. Cheap when nothing changed.</summary>
    public void Refresh(bool force = false)
    {
        // Refresh runs every visible frame; a gap means the page was hidden and has just been shown again.
        if (Time.frameCount - _lastRefreshFrame > 1)
        {
            // Measurements taken while hidden may be stale; re-measure once the page is really visible.
            _recullFrames = RecullFrameCount;
            foreach (var v in _views) v.Dirty = true;
            force = true;
        }
        _lastRefreshFrame = Time.frameCount;
        bool widthChanged = Mathf.Abs(Content.rect.width - _measuredWidth) > .5f;
        if (force || widthChanged || _layoutPending || Editor.Revision != _syncedRevision)
        {
            if (widthChanged) foreach (var v in _views) v.Dirty = true;
            Sync();
            UpdateTexts();
            Layout();
            _measuredWidth = Content.rect.width; _syncedRevision = Editor.Revision; _layoutPending = false;
            _recullFrames = RecullFrameCount;
        }
        RecullTexts();
    }

    private const int RecullFrameCount = 3;
    private int _recullFrames = RecullFrameCount, _lastRefreshFrame = -10;

    /// <summary>
    /// TMP defers RectMask2D culling while its layout is dirty and only resolves it on the next mesh
    /// rebuild. Static paragraphs never rebuild, so text laid out off-screen stayed culled until the
    /// page scrolled. For a few frames after each layout, cull text against the viewport directly.
    /// </summary>
    private void RecullTexts()
    {
        if (_recullFrames <= 0 || !Content.gameObject.activeInHierarchy) return;
        _recullFrames--;
        Rect view = WorldRect(Viewport);
        if (view.width <= 0 || view.height <= 0) { _recullFrames = RecullFrameCount; return; }
        foreach (var text in Content.GetComponentsInChildren<TextMeshProUGUI>(false))
        {
            bool cull = !WorldRect(text.rectTransform).Overlaps(view);
            if (text.canvasRenderer.cull != cull) text.canvasRenderer.cull = cull;
            foreach (var sub in text.GetComponentsInChildren<TMP_SubMeshUI>(true))
                if (sub.canvasRenderer.cull != cull) sub.canvasRenderer.cull = cull;
        }
    }

    private static readonly Vector3[] Corners = new Vector3[4];
    private static Rect WorldRect(RectTransform rect)
    {
        rect.GetWorldCorners(Corners);
        float xMin = Mathf.Min(Corners[0].x, Corners[2].x), xMax = Mathf.Max(Corners[0].x, Corners[2].x);
        float yMin = Mathf.Min(Corners[0].y, Corners[2].y), yMax = Mathf.Max(Corners[0].y, Corners[2].y);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private void Sync()
    {
        var blocks = Editor.Doc.Blocks;
        var old = _views.ToDictionary(v => v.Block, v => v, ReferenceEqualityComparer.Instance);
        var orphanMedia = new List<MediaCell>();
        var kept = new HashSet<BlockView>();
        var next = new List<BlockView>(blocks.Count);
        foreach (var block in blocks)
        {
            if (old.TryGetValue(block, out var view) && kept.Add(view)) { next.Add(view); continue; }
            next.Add(null!);
        }
        foreach (var view in _views)
            if (!kept.Contains(view))
            {
                // Lift players out before the old block is destroyed, otherwise they die with it.
                foreach (var cell in view.Cells) cell.Rect.SetParent(_blocks, false);
                orphanMedia.AddRange(view.Cells); view.Cells.Clear(); Destroy(view);
            }
        for (int i = 0; i < blocks.Count; i++)
        {
            if (next[i] == null) next[i] = Create(blocks[i]);
            SyncCells(next[i], orphanMedia);
        }
        foreach (var cell in orphanMedia) DisposeCell(cell);
        _views.Clear(); _views.AddRange(next);
        _empty.gameObject.SetActive(!Author && blocks.All(b => b is ParagraphBlock p && p.Length == 0));
    }

    private BlockView Create(DocBlock block)
    {
        var view = new BlockView { Block = block };
        view.Rect = Node(_blocks, block.GetType().Name); TopLeft(view.Rect);
        if (block is ParagraphBlock)
        {
            var text = _factory(view.Rect, "", 22); TopLeft(text.rectTransform);
            text.richText = true; text.parseCtrlCharacters = false; text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false; text.enableAutoSizing = false; text.margin = Vector4.zero;
            (text.GetComponent<IntroductionTextMaterial>() ?? text.gameObject.AddComponent<IntroductionTextMaterial>()).Apply(text);
            view.Text = text;
        }
        else if (block is DividerBlock)
        {
            var line = Node(view.Rect, "Line"); line.anchorMin = new Vector2(0, .5f); line.anchorMax = new Vector2(1, .5f); line.sizeDelta = new Vector2(0, 2);
            var dash = line.gameObject.AddComponent<IntroductionDashedRule>(); dash.color = new Color(.67f, .64f, .57f, .75f); dash.raycastTarget = false;
        }
        return view;
    }

    private void SyncCells(BlockView view, List<MediaCell> orphans)
    {
        IEnumerable<MediaBlock> wanted = view.Block is RowBlock row ? row.Cells : view.Block is MediaBlock m ? new[] { m } : Array.Empty<MediaBlock>();
        var current = view.Cells.ToList(); view.Cells.Clear();
        foreach (var media in wanted)
        {
            string resolved = ResolveMedia(media.Source);
            // Reuse a player showing the same file, so moves, rows and undo never reload or flash media.
            var cell = current.FirstOrDefault(c => ReferenceEquals(c.Block, media) && c.Shows(media, resolved))
                ?? orphans.FirstOrDefault(c => ReferenceEquals(c.Block, media) && c.Shows(media, resolved))
                ?? current.FirstOrDefault(c => c.Shows(media, resolved))
                ?? orphans.FirstOrDefault(c => c.Shows(media, resolved));
            if (cell != null) { current.Remove(cell); orphans.Remove(cell); cell.Block = media; cell.Rect.SetParent(view.Rect, false); }
            else cell = CreateCell(view.Rect, media);
            view.Cells.Add(cell);
        }
        orphans.AddRange(current);
    }

    private MediaCell CreateCell(Transform parent, MediaBlock media)
    {
        var cell = new MediaCell { Block = media, Rect = Node(parent, "Media"), Source = media.Source, Kind = media.Kind }; TopLeft(cell.Rect);
        string path = ResolveMedia(media.Source);
        cell.ResolvedPath = path;
        int occurrence = _views.Sum(v => v.Cells.Count(c => string.Equals(c.Block.Source, media.Source, StringComparison.OrdinalIgnoreCase)));
        string key = path + "|" + occurrence;
        if (!_mediaStates.TryGetValue(key, out var state)) _mediaStates[key] = state = new IntroductionMediaState();
        string type = media.Kind == MediaKind.Gif ? "gif" : media.Kind == MediaKind.Video ? "video" : "image";
        cell.Media = new IntroductionMedia(cell.Rect, type, path, _factory, Status, () => ObjectClicked?.Invoke(ObjectFor(cell)), Viewport, _modalParent, state, "center", () => _layoutPending = true,
            // While editing, a click selects the media; playback stays on its own play button.
            clickToggles: () => !Author);
        return cell;
    }

    private DocObject ObjectFor(MediaCell cell)
    {
        for (int i = 0; i < _views.Count; i++)
        {
            int index = _views[i].Cells.IndexOf(cell);
            if (index >= 0) return _views[i].Block is RowBlock ? new DocObject(i, index) : new DocObject(i);
        }
        return DocObject.None;
    }

    private static void DisposeCell(MediaCell cell) { cell.Media.Dispose(); if (cell.Rect != null) UnityEngine.Object.Destroy(cell.Rect.gameObject); }
    private static void Destroy(BlockView view)
    {
        foreach (var cell in view.Cells) DisposeCell(cell);
        if (view.Rect != null) { view.Rect.gameObject.SetActive(false); UnityEngine.Object.Destroy(view.Rect.gameObject); }
    }

    public void Dispose() { foreach (var v in _views) Destroy(v); _views.Clear(); }

    public static float BaseSize(ParaStyle style) => style switch { ParaStyle.Heading1 => 36, ParaStyle.Heading2 => 29, ParaStyle.Heading3 => 25, _ => 22 };
    private static float SpaceBefore(ParaStyle style) => style switch { ParaStyle.Heading1 => 20, ParaStyle.Heading2 => 16, ParaStyle.Heading3 => 12, ParaStyle.Bullet or ParaStyle.Numbered => 3, _ => 7 };
    private static Color StyleColor(ParaStyle style) => style switch { ParaStyle.Heading1 or ParaStyle.Heading2 => Gold, ParaStyle.Quote => Muted, _ => Ink };
    private static bool Boxed(ParagraphBlock p) => p.Style == ParaStyle.Callout || p.Background.Length != 0;
    internal static Color ParseColor(string value, Color fallback) => ColorUtility.TryParseHtmlString(value, out var c) ? c : fallback;

    private void UpdateTexts()
    {
        var caret = Editor.Selection.Focus;
        for (int i = 0; i < _views.Count; i++)
        {
            var view = _views[i];
            if (view.Block is not ParagraphBlock p || view.Text == null) continue;
            bool composing = Composition.Length > 0 && Author && i == caret.Block && Editor.Selection.Object.IsNone;
            var built = DocRichText.Build(p, IntroductionFontRegistry.RegistryName, composing ? caret.Offset : -1, composing ? Composition : "", composing ? Editor.StyleAtCaret() : null);
            float size = BaseSize(p.Style) * TextScale;
            if (built.Rich == view.Rich && Mathf.Approximately(size, view.FontSize) && !view.Dirty) continue;
            view.Rich = built.Rich; view.Map = built.SourceToModel; view.FontSize = size; view.Dirty = true;
            var text = view.Text;
            text.fontSize = size; text.color = StyleColor(p.Style);
            text.fontStyle = p.Style == ParaStyle.Heading3 ? FontStyles.Bold : p.Style == ParaStyle.Quote ? FontStyles.Italic : FontStyles.Normal;
            text.alignment = p.Align == "center" ? TextAlignmentOptions.Top : p.Align == "right" ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
            text.text = built.Rich;
        }
    }

    private void Layout()
    {
        float y = PadTop, usable = Usable;
        var numbers = new Dictionary<int, int>();
        bool firstVisible = true;
        bool onlyEmpty = Author && _views.Count == 1 && _views[0].Block is ParagraphBlock only && only.Length == 0;
        for (int i = 0; i < _views.Count; i++)
        {
            var view = _views[i];
            switch (view.Block)
            {
                case ParagraphBlock p:
                {
                    if (!p.IsList) numbers.Clear();
                    float before = firstVisible ? 0 : SpaceBefore(p.Style);
                    if (i > 0 && _views[i - 1].Block is ParagraphBlock prev && prev.IsList && p.IsList) before = 3;
                    y += before; view.SpaceBefore = before;
                    LayoutParagraph(view, p, y, usable, numbers, onlyEmpty);
                    y += view.Height; break;
                }
                case DividerBlock:
                {
                    numbers.Clear(); float before = firstVisible ? 0 : 6; y += before; view.SpaceBefore = before;
                    view.Y = y; view.Height = 24; view.ObjectX = PadX; view.ObjectWidth = usable;
                    Place(view.Rect, PadX, y, usable, view.Height); y += view.Height; break;
                }
                default:
                {
                    numbers.Clear(); float before = firstVisible ? 0 : 10; y += before; view.SpaceBefore = before;
                    LayoutMedia(view, y, usable); y += view.Height; break;
                }
            }
            firstVisible = false;
        }
        if (_empty.gameObject.activeSelf) { Place(_empty.rectTransform, PadX, PadTop, usable, 40); y = Mathf.Max(y, PadTop + 40); }
        float height = Mathf.Max(y + PadBottom, Viewport.rect.height);
        Content.sizeDelta = new Vector2(Content.sizeDelta.x, height);
        UpdateOverlay();
    }

    private void LayoutParagraph(BlockView view, ParagraphBlock p, float y, float usable, Dictionary<int, int> numbers, bool placeholder)
    {
        var text = view.Text!;
        float left = PadX + p.Indent * IndentStep, right = PadX + usable;
        bool boxed = Boxed(p);
        float padY = boxed ? BoxPadY : 0;
        float textLeft = left + (boxed ? BoxPadX : 0) + (p.Style == ParaStyle.Quote ? 16 : 0) + (p.IsList ? ListGutter : 0);
        float textRight = right - (boxed ? BoxPadX : 0);
        float width = Mathf.Max(40, textRight - textLeft);
        if (view.Dirty || Mathf.Abs(view.TextWidth - width) > .5f)
        {
            text.rectTransform.sizeDelta = new Vector2(width, 10);
            // TMP refreshes its wrap width only on rect changes while active. The first layout runs while
            // the page is hidden, so without this every paragraph was measured at a stale, tiny width.
            text.ComputeMarginSize();
            text.ForceMeshUpdate(true, true);
            BuildCharMap(view, p);
            view.Dirty = false;
        }
        var info = text.textInfo;
        float textHeight = info.lineCount > 0 ? info.lineInfo[0].ascender - info.lineInfo[info.lineCount - 1].descender : view.FontSize * 1.3f;
        textHeight = Mathf.Max(textHeight, view.FontSize * 1.2f);
        view.Y = y; view.TextX = textLeft; view.TextTop = y + padY; view.TextWidth = width; view.Height = textHeight + padY * 2;
        Place(view.Rect, 0, 0, PageWidth, 0);
        Place(text.rectTransform, textLeft, view.TextTop, width, textHeight);

        if (boxed)
        {
            view.Box ??= MakeImage(view.Rect, "Box", 0);
            view.Box.color = ParseColor(p.Background.Length != 0 ? p.Background : "#2E3D4ACC", new Color(.18f, .24f, .29f, .8f));
            Place(view.Box.rectTransform, left, y, right - left, view.Height);
        }
        else if (view.Box != null) view.Box.gameObject.SetActive(false);
        if (view.Box != null) view.Box.gameObject.SetActive(boxed);

        bool quote = p.Style == ParaStyle.Quote;
        if (quote) { view.Bar ??= MakeImage(view.Rect, "QuoteBar", 0); view.Bar.color = new Color(.78f, .68f, .42f, .85f); Place(view.Bar.rectTransform, left + (boxed ? BoxPadX : 0), y + padY, 4, textHeight); }
        if (view.Bar != null) view.Bar.gameObject.SetActive(quote);

        float firstLineMiddle = info.lineCount > 0 ? (info.lineInfo[0].ascender + info.lineInfo[0].descender) / 2 : -view.FontSize * .6f;
        float markerX = textLeft - ListGutter;
        if (p.Style == ParaStyle.Numbered)
        {
            foreach (int deeper in numbers.Keys.Where(k => k > p.Indent).ToList()) numbers.Remove(deeper);
            numbers.TryGetValue(p.Indent, out int n); numbers[p.Indent] = ++n;
            if (view.Number == null) { view.Number = _factory(view.Rect, "", 22); view.Number.raycastTarget = false; view.Number.alignment = TextAlignmentOptions.TopRight; (view.Number.gameObject.AddComponent<IntroductionTextMaterial>()).Apply(view.Number); }
            view.Number.fontSize = view.FontSize; view.Number.color = Ink; view.Number.text = n + ".";
            Place(view.Number.rectTransform, markerX - 14, view.TextTop, ListGutter + 8, textHeight);
        }
        else if (p.Style == ParaStyle.Bullet)
        {
            foreach (int deeper in numbers.Keys.Where(k => k >= p.Indent).ToList()) numbers.Remove(deeper);
            if (view.Dot == null) { var dot = Node(view.Rect, "Bullet"); TopLeft(dot); view.Dot = dot.gameObject.AddComponent<DocDotGraphic>(); view.Dot.raycastTarget = false; }
            view.Dot.color = Ink; view.Dot.Shape = p.Indent % 3; float d = Mathf.Max(6, view.FontSize * .28f);
            Place(view.Dot.rectTransform, markerX + 6, view.TextTop - firstLineMiddle - d / 2, d, d); view.Dot.SetVerticesDirty();
        }
        if (view.Number != null) view.Number.gameObject.SetActive(p.Style == ParaStyle.Numbered);
        if (view.Dot != null) view.Dot.gameObject.SetActive(p.Style == ParaStyle.Bullet);

        if (placeholder)
        {
            if (view.Placeholder == null) { view.Placeholder = _factory(view.Rect, Loc.T("Type here. Use the toolbar or # / - / 1. shortcuts for headings and lists.", "在这里输入正文。标题和列表可用工具栏，或输入 # 、- 、1. 加空格。", "在這裡輸入正文。標題和清單可用工具列，或輸入 # 、- 、1. 加空格。"), 20); view.Placeholder.color = new Color(.7f, .68f, .62f, .55f); view.Placeholder.raycastTarget = false; view.Placeholder.textWrappingMode = TextWrappingModes.Normal; }
            Place(view.Placeholder.rectTransform, textLeft, view.TextTop, width, textHeight);
        }
        if (view.Placeholder != null) view.Placeholder.gameObject.SetActive(placeholder && Composition.Length == 0);
    }

    private void BuildCharMap(BlockView view, ParagraphBlock p)
    {
        var info = view.Text!.textInfo;
        var map = new int[p.Length + 1];
        for (int i = 0; i < map.Length; i++) map[i] = -1;
        for (int k = 0; k < info.characterCount; k++)
        {
            int source = info.characterInfo[k].index;
            if (source < 0 || source >= view.Map.Length) continue;
            int model = view.Map[source];
            if (model >= 0 && model < map.Length && map[model] < 0) map[model] = k;
        }
        for (int i = 1; i < map.Length; i++) if (map[i] < 0) map[i] = map[i - 1];
        if (map.Length > 0 && map[0] < 0) for (int i = 0; i < map.Length; i++) map[i] = Math.Max(0, Math.Min(info.characterCount - 1, i));
        view.ModelToChar = map;
    }

    private void LayoutMedia(BlockView view, float y, float usable)
    {
        string align = view.Block is RowBlock row ? row.Align : ((MediaBlock)view.Block).Align;
        float total = view.Cells.Sum(c => c.Block.Width);
        float gaps = RowGap * Mathf.Max(0, view.Cells.Count - 1);
        float scale = total > 100 ? 100f / total : 1;
        float available = Mathf.Max(40, usable - gaps);
        var widths = view.Cells.Select(c => Mathf.Max(60, available * c.Block.Width * scale / 100f)).ToList();
        float rowWidth = widths.Sum() + gaps;
        float x = PadX + (align == "center" ? (usable - rowWidth) / 2 : align == "right" ? usable - rowWidth : 0);
        x = Mathf.Max(PadX, x);
        float height = 0;
        Place(view.Rect, 0, 0, PageWidth, 0);
        view.ObjectX = x; view.ObjectWidth = rowWidth;
        for (int i = 0; i < view.Cells.Count; i++)
        {
            var cell = view.Cells[i]; float w = widths[i];
            float h = w / cell.Media.AspectRatio + IntroductionMedia.ControlBarHeight;
            cell.X = x; cell.Y = y; cell.W = w; cell.H = h;
            Place(cell.Rect, x, y, w, h);
            x += w + RowGap; height = Mathf.Max(height, h);
        }
        view.Y = y; view.Height = Mathf.Max(40, height);
    }

    // ---------------- overlay ----------------

    public bool CaretVisible = true;

    public void UpdateOverlay()
    {
        var selection = Editor.Selection;
        var rects = new List<Rect>();
        if (selection.Object.IsNone && !selection.Collapsed) rects.AddRange(SelectionRects(selection.Start, selection.End));
        _selection.Set(rects);
        bool caret = Author && selection.Object.IsNone && CaretVisible;
        if (caret && CaretRect(selection.Focus) is Rect r) { _caret.gameObject.SetActive(true); Place(_caret.rectTransform, r.x - 1, r.y, 2, r.height); }
        else _caret.gameObject.SetActive(false);
        var obj = ObjectRect(selection.Object);
        _objectFrame.gameObject.SetActive(obj != null);
        if (obj is Rect o) Place(_objectFrame.rectTransform, o.x - 3, o.y - 3, o.width + 6, o.height + 6);
        _overlay.SetAsLastSibling();
    }

    public void SetCaretBlink(bool on) { if (_caret.gameObject.activeSelf || on) _caret.enabled = on; }

    public Rect? ObjectRect(DocObject o)
    {
        if (o.IsNone || o.Block < 0 || o.Block >= _views.Count) return null;
        var view = _views[o.Block];
        if (view.Block is DividerBlock) return new Rect(view.ObjectX, view.Y, view.ObjectWidth, view.Height);
        int index = view.Block is RowBlock ? o.Cell : 0;
        if (index < 0 || index >= view.Cells.Count) return null;
        var c = view.Cells[index]; return new Rect(c.X, c.Y, c.W, c.H);
    }
    public MediaCell? CellAt(DocObject o)
    {
        if (o.IsNone || o.Block < 0 || o.Block >= _views.Count) return null;
        var view = _views[o.Block]; int index = view.Block is RowBlock ? o.Cell : 0;
        return index >= 0 && index < view.Cells.Count ? view.Cells[index] : null;
    }

    /// <summary>Caret rectangle in doc coordinates (x, top y, width 0, height).</summary>
    public Rect? CaretRect(DocPos pos)
    {
        if (pos.Block < 0 || pos.Block >= _views.Count) return null;
        var view = _views[pos.Block];
        if (view.Text == null || view.ModelToChar.Length == 0) return null;
        var info = view.Text.textInfo; if (info.characterCount == 0 || info.lineCount == 0) return new Rect(view.TextX, view.TextTop, 0, view.FontSize * 1.2f);
        int k = view.ModelToChar[Mathf.Clamp(pos.Offset, 0, view.ModelToChar.Length - 1)];
        k = Mathf.Clamp(k, 0, info.characterCount - 1);
        var ch = info.characterInfo[k]; var line = info.lineInfo[Mathf.Clamp(ch.lineNumber, 0, info.lineCount - 1)];
        float x = view.TextX + ch.origin;
        return new Rect(x, view.TextTop - line.ascender, 0, line.ascender - line.descender);
    }

    private IEnumerable<Rect> SelectionRects(DocPos start, DocPos end)
    {
        for (int b = start.Block; b <= end.Block && b < _views.Count; b++)
        {
            var view = _views[b];
            if (view.Text == null)
            {
                if (view.Block is DividerBlock || view.Cells.Count > 0) yield return new Rect(view.ObjectX, view.Y, view.ObjectWidth, view.Height);
                continue;
            }
            var p = (ParagraphBlock)view.Block; var info = view.Text.textInfo;
            int s = b == start.Block ? start.Offset : 0, e = b == end.Block ? end.Offset : p.Length;
            bool fullBreak = b != end.Block;
            if (e <= s && !fullBreak) continue;
            var lines = new Dictionary<int, (float Min, float Max)>();
            for (int m = s; m < e; m++)
            {
                int k = view.ModelToChar[m]; if (k < 0 || k >= info.characterCount) continue;
                var ch = info.characterInfo[k];
                float x0 = ch.origin, x1 = p[m] == '\n' ? ch.origin + 6 : Mathf.Max(ch.xAdvance, ch.origin + 2);
                lines[ch.lineNumber] = lines.TryGetValue(ch.lineNumber, out var r) ? (Mathf.Min(r.Min, x0), Mathf.Max(r.Max, x1)) : (x0, x1);
            }
            if (fullBreak && info.characterCount > 0)
            {
                // Show the paragraph break as a small block, as text editors do.
                var ch = info.characterInfo[view.ModelToChar[p.Length]];
                lines[ch.lineNumber] = lines.TryGetValue(ch.lineNumber, out var r) ? (r.Min, Mathf.Max(r.Max, ch.origin + 7)) : (ch.origin, ch.origin + 7);
            }
            foreach (var pair in lines)
            {
                var line = info.lineInfo[Mathf.Clamp(pair.Key, 0, info.lineCount - 1)];
                yield return new Rect(view.TextX + pair.Value.Min, view.TextTop - line.ascender, pair.Value.Max - pair.Value.Min, line.ascender - line.descender);
            }
        }
    }

    // ---------------- hit testing & navigation ----------------

    public Vector2 ScreenToDoc(Vector2 screen, Camera? camera)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(Content, screen, camera, out var local);
        return new Vector2(local.x - Content.rect.xMin, Content.rect.yMax - local.y);
    }
    public Vector2 DocToScreen(Vector2 doc, Camera? camera)
    {
        var local = new Vector3(doc.x + Content.rect.xMin, Content.rect.yMax - doc.y, 0);
        return RectTransformUtility.WorldToScreenPoint(camera, Content.TransformPoint(local));
    }

    /// <summary>Block index at a doc y (nearest when between blocks).</summary>
    public int BlockAtY(float y)
    {
        if (_views.Count == 0) return -1;
        for (int i = 0; i < _views.Count; i++)
        {
            var v = _views[i]; float next = i + 1 < _views.Count ? _views[i + 1].Y - _views[i + 1].SpaceBefore / 2 : float.MaxValue;
            if (y < next) return i;
        }
        return _views.Count - 1;
    }

    public DocObject ObjectAt(Vector2 doc)
    {
        int b = BlockAtY(doc.y); if (b < 0) return DocObject.None;
        var view = _views[b];
        if (view.Block is DividerBlock) return doc.y >= view.Y && doc.y <= view.Y + view.Height ? new DocObject(b) : DocObject.None;
        for (int i = 0; i < view.Cells.Count; i++)
        {
            var c = view.Cells[i];
            if (doc.x >= c.X && doc.x <= c.X + c.W && doc.y >= c.Y && doc.y <= c.Y + c.H) return view.Block is RowBlock ? new DocObject(b, i) : new DocObject(b);
        }
        return DocObject.None;
    }

    public DocPos PositionAt(Vector2 doc)
    {
        int b = BlockAtY(doc.y); if (b < 0) return new DocPos(0, 0);
        var view = _views[b];
        if (view.Text == null)
        {
            // Over an object: snap to the nearest text before or after it.
            bool after = doc.y > view.Y + view.Height / 2;
            var next = Editor.NextParagraph(b); var previous = Editor.PreviousParagraph(b);
            if (after && next != null) return new DocPos(next.Value, 0);
            if (previous != null) return new DocPos(previous.Value, Editor.Para(previous.Value)!.Length);
            return next != null ? new DocPos(next.Value, 0) : Editor.FirstPos();
        }
        return new DocPos(b, OffsetInParagraph(view, doc));
    }

    private int OffsetInParagraph(BlockView view, Vector2 doc)
    {
        var info = view.Text!.textInfo; var p = (ParagraphBlock)view.Block;
        if (info.characterCount == 0 || info.lineCount == 0) return 0;
        float lx = doc.x - view.TextX, ly = view.TextTop - doc.y;
        int line = info.lineCount - 1;
        for (int i = 0; i < info.lineCount; i++) if (ly > info.lineInfo[i].descender) { line = i; break; }
        return OffsetOnLine(view, p, line, lx);
    }

    private int OffsetOnLine(BlockView view, ParagraphBlock p, int line, float lx)
    {
        var info = view.Text!.textInfo; var li = info.lineInfo[line];
        int last = Mathf.Min(li.lastCharacterIndex, info.characterCount - 1);
        int lastModel = 0;
        for (int k = li.firstCharacterIndex; k <= last; k++)
        {
            var ch = info.characterInfo[k]; int model = ModelOf(view, k); if (model < 0) continue;
            lastModel = model;
            if (lx < (ch.origin + ch.xAdvance) / 2) return Mathf.Min(model, p.Length);
        }
        if (lastModel >= p.Length) return p.Length;
        // Past the end of a wrapped line: after its last character (a hard break stays before the break).
        return p[lastModel] == '\n' ? lastModel : Mathf.Min(p.Length, lastModel + 1);
    }

    private static int ModelOf(BlockView view, int k)
    {
        int source = view.Text!.textInfo.characterInfo[k].index;
        return source >= 0 && source < view.Map.Length ? view.Map[source] : -1;
    }

    public int LineOf(DocPos pos)
    {
        var view = _views[pos.Block]; if (view.Text == null || view.ModelToChar.Length == 0) return 0;
        var info = view.Text.textInfo; int k = Mathf.Clamp(view.ModelToChar[Mathf.Clamp(pos.Offset, 0, view.ModelToChar.Length - 1)], 0, Math.Max(0, info.characterCount - 1));
        return info.characterCount == 0 ? 0 : info.characterInfo[k].lineNumber;
    }

    /// <summary>Up/Down by visual line keeping a goal x; crosses paragraphs and skips objects.</summary>
    public DocPos Vertical(DocPos pos, int direction, float goalX)
    {
        var view = _views[pos.Block];
        int line = LineOf(pos); int lines = view.Text?.textInfo.lineCount ?? 1;
        int target = line + direction;
        if (view.Text != null && target >= 0 && target < lines) return new DocPos(pos.Block, OffsetOnLine(view, (ParagraphBlock)view.Block, target, goalX - view.TextX));
        int? block = direction < 0 ? Editor.PreviousParagraph(pos.Block) : Editor.NextParagraph(pos.Block);
        if (block == null) return direction < 0 ? new DocPos(pos.Block, 0) : new DocPos(pos.Block, ((ParagraphBlock)view.Block).Length);
        var other = _views[block.Value]; int otherLines = Math.Max(1, other.Text!.textInfo.lineCount);
        return new DocPos(block.Value, OffsetOnLine(other, (ParagraphBlock)other.Block, direction < 0 ? otherLines - 1 : 0, goalX - other.TextX));
    }

    public DocPos LineEdge(DocPos pos, bool end)
    {
        var view = _views[pos.Block]; if (view.Text == null) return pos;
        var p = (ParagraphBlock)view.Block; var info = view.Text.textInfo; if (info.lineCount == 0) return pos;
        var li = info.lineInfo[Mathf.Clamp(LineOf(pos), 0, info.lineCount - 1)];
        if (!end) { int m = ModelOf(view, li.firstCharacterIndex); return new DocPos(pos.Block, Mathf.Clamp(m, 0, p.Length)); }
        int lastModel = ModelOf(view, Mathf.Min(li.lastCharacterIndex, info.characterCount - 1));
        if (lastModel >= p.Length) return new DocPos(pos.Block, p.Length);
        return new DocPos(pos.Block, p[lastModel] == '\n' || p[lastModel] == ' ' && li.lastCharacterIndex < info.characterCount - 1 ? lastModel : lastModel + 1);
    }

    public string LinkAt(Vector2 doc)
    {
        int b = BlockAtY(doc.y); if (b < 0 || _views[b].Text == null) return "";
        var view = _views[b]; var p = (ParagraphBlock)view.Block;
        if (doc.x < view.TextX || doc.x > view.TextX + view.TextWidth || doc.y < view.TextTop || doc.y > view.Y + view.Height) return "";
        int offset = OffsetInParagraph(view, doc);
        foreach (int candidate in new[] { offset, offset - 1 })
            if (candidate >= 0 && candidate < p.Length && p.StyleAt(candidate).Link.Length != 0)
            {
                var rect = CaretRect(new DocPos(b, candidate)); var after = CaretRect(new DocPos(b, candidate + 1));
                if (rect is Rect r0 && after is Rect r1 && (Mathf.Abs(r0.y - r1.y) > 1 || doc.x >= Mathf.Min(r0.x, r1.x) - 2 && doc.x <= Mathf.Max(r0.x, r1.x) + 2)) return p.StyleAt(candidate).Link;
            }
        return "";
    }

    /// <summary>Scrolls just enough to show a doc-space rectangle.</summary>
    public void Reveal(Rect rect)
    {
        float view = Viewport.rect.height, offset = Content.anchoredPosition.y, max = Mathf.Max(0, Content.rect.height - view);
        float top = rect.y - 12, bottom = rect.y + rect.height + 12;
        if (top < offset) offset = top; else if (bottom > offset + view) offset = bottom - view;
        offset = Mathf.Clamp(offset, 0, max);
        if (Mathf.Abs(offset - Content.anchoredPosition.y) > .5f) { Scroll.StopMovement(); Content.anchoredPosition = new Vector2(Content.anchoredPosition.x, offset); }
    }
    public float ScrollOffset { get => Content.anchoredPosition.y; set { Scroll.StopMovement(); Content.anchoredPosition = new Vector2(Content.anchoredPosition.x, Mathf.Clamp(value, 0, Mathf.Max(0, Content.rect.height - Viewport.rect.height))); } }

    public bool AnyMediaEnlarged => _views.Any(v => v.Cells.Any(c => c.Media.Enlarged));
    public bool CloseEnlarged() { foreach (var v in _views) foreach (var c in v.Cells) if (c.Media.CloseEnlarged()) return true; return false; }

    // ---------------- helpers ----------------

    internal static RectTransform Node(Transform parent, string name) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return (RectTransform)go.transform; }
    internal static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0, 1); rect.offsetMin = rect.offsetMax = Vector2.zero; }
    internal static void TopLeft(RectTransform rect) { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); }
    // Called every frame for floating tools; unchanged values must not dirty the canvas.
    internal static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        var corner = new Vector2(0, 1);
        if (rect.anchorMin != corner || rect.anchorMax != corner || rect.pivot != corner) TopLeft(rect);
        var position = new Vector2(x, -y); var size = new Vector2(width, height);
        if (rect.anchoredPosition != position) rect.anchoredPosition = position;
        if (rect.sizeDelta != size) rect.sizeDelta = size;
    }
    private static Image MakeImage(Transform parent, string name, int sibling)
    {
        var rect = Node(parent, name); TopLeft(rect); rect.SetSiblingIndex(sibling);
        var image = rect.gameObject.AddComponent<Image>(); image.raycastTarget = false; ModConfigPaperStyle.Apply(image, 6); rect.gameObject.AddComponent<OverviewPaperPanel>(); return image;
    }
}

/// <summary>Selection highlight: all rectangles in one mesh, coordinates in doc space.</summary>
internal sealed class DocSelectionGraphic : MaskableGraphic
{
    private readonly List<Rect> _rects = new();
    internal void Set(List<Rect> rects)
    {
        if (rects.Count == _rects.Count && !rects.Where((r, i) => r != _rects[i]).Any()) return;
        _rects.Clear(); _rects.AddRange(rects); SetVerticesDirty();
    }
    public override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var origin = rectTransform.rect;
        foreach (var r in _rects)
        {
            int i = vh.currentVertCount; float x0 = origin.xMin + r.xMin, x1 = origin.xMin + r.xMax, y0 = origin.yMax - r.yMin, y1 = origin.yMax - r.yMax;
            vh.AddVert(new Vector3(x0, y1), color, Vector2.zero); vh.AddVert(new Vector3(x0, y0), color, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), color, Vector2.zero); vh.AddVert(new Vector3(x1, y1), color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}

/// <summary>Solid rectangle outline (object selection, drop targets).</summary>
internal sealed class DocFrameGraphic : MaskableGraphic
{
    internal float Thickness = 2;
    public override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var r = rectTransform.rect; float t = Thickness;
        void Quad(float x0, float y0, float x1, float y1) { int i = vh.currentVertCount; vh.AddVert(new Vector3(x0, y0), color, Vector2.zero); vh.AddVert(new Vector3(x0, y1), color, Vector2.zero); vh.AddVert(new Vector3(x1, y1), color, Vector2.zero); vh.AddVert(new Vector3(x1, y0), color, Vector2.zero); vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3); }
        Quad(r.xMin, r.yMax - t, r.xMax, r.yMax); Quad(r.xMin, r.yMin, r.xMax, r.yMin + t);
        Quad(r.xMin, r.yMin, r.xMin + t, r.yMax); Quad(r.xMax - t, r.yMin, r.xMax, r.yMax);
    }
}

/// <summary>List bullet drawn as geometry so it never depends on a font glyph. Shape 0 disc, 1 ring, 2 square.</summary>
internal sealed class DocDotGraphic : MaskableGraphic
{
    internal int Shape;
    public override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var r = rectTransform.rect; var c = r.center; float radius = Mathf.Min(r.width, r.height) / 2;
        if (Shape == 2)
        {
            vh.AddVert(new Vector3(r.xMin, r.yMin), color, Vector2.zero); vh.AddVert(new Vector3(r.xMin, r.yMax), color, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMax), color, Vector2.zero); vh.AddVert(new Vector3(r.xMax, r.yMin), color, Vector2.zero);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3); return;
        }
        const int segments = 20; float inner = Shape == 1 ? radius * .55f : 0;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2 / segments, a1 = (i + 1) * Mathf.PI * 2 / segments;
            var o0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)); var o1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            int v = vh.currentVertCount;
            vh.AddVert(c + o0 * inner, color, Vector2.zero); vh.AddVert(c + o0 * radius, color, Vector2.zero);
            vh.AddVert(c + o1 * radius, color, Vector2.zero); vh.AddVert(c + o1 * inner, color, Vector2.zero);
            vh.AddTriangle(v, v + 1, v + 2); vh.AddTriangle(v, v + 2, v + 3);
        }
    }
}

internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
{
    public static readonly ReferenceEqualityComparer Instance = new();
    public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
    public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
