#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview.Editor;

/// <summary>
/// Author-mode object tools: corner handles that resize the selected media in place, a floating
/// bar for alignment/size/caption, and a block grip that drags paragraphs or media to a new place
/// (including beside another picture to form a row) with a translucent ghost and a drop marker.
/// </summary>
internal sealed class DocMediaTools
{
    private static readonly int[] Snaps = { 25, 33, 50, 67, 75, 100 };
    private readonly DocCanvas _canvas;
    private readonly DocEditor _editor;
    private readonly RectTransform _layer, _root;
    private readonly Func<Transform, string, float, TextMeshProUGUI> _factory;
    private readonly Action _changed;
    private readonly List<RectTransform> _handles = new();
    private readonly RectTransform _bar, _sizeBadge, _grip, _marker;
    private readonly TextMeshProUGUI _sizeText;
    private readonly List<DocToolButton> _barButtons = new();
    private readonly DocToolButton _caption;
    private RectTransform? _ghost;
    internal Action<DocObject>? Replace, Delete, EditCaption;
    internal bool Author;
    internal bool Busy => _resizing || _dragging;

    // Resize gesture
    private bool _resizing; private DocObject _resizeTarget; private int _resizeCorner; private float _resizeLeft, _resizeRight;
    // Block drag gesture
    private bool _dragging; private int _dragBlock = -1, _dragCell = -1; private int _hoverBlock = -1, _hoverCell = -1;
    private (int Block, int Cell, DropPlacement Placement)? _drop;
    private Camera? _camera;

    public DocMediaTools(DocCanvas canvas, DocEditor editor, RectTransform root, Func<Transform, string, float, TextMeshProUGUI> factory, Action changed)
    {
        _canvas = canvas; _editor = editor; _root = root; _factory = factory; _changed = changed;
        _layer = DocCanvas.Node(canvas.OverlayRoot, "ObjectTools"); DocCanvas.Stretch(_layer);
        for (int i = 0; i < 4; i++)
        {
            var handle = DocCanvas.Node(_layer, "Handle" + i); DocCanvas.TopLeft(handle);
            var image = handle.gameObject.AddComponent<Image>(); image.color = new Color(.36f, .68f, 1f, 1);
            var drag = handle.gameObject.AddComponent<DocDragRelay>(); int corner = i;
            drag.Begin = data => BeginResize(corner, data); drag.Move = MoveResize; drag.End = _ => EndResize();
            handle.gameObject.SetActive(false); _handles.Add(handle);
        }
        _sizeBadge = DocCanvas.Node(_layer, "SizeBadge"); DocCanvas.TopLeft(_sizeBadge);
        var badge = _sizeBadge.gameObject.AddComponent<Image>(); badge.color = ModConfigLook.Surface(new Color(.06f, .05f, .04f, .9f)); badge.raycastTarget = false; ModConfigSidebarWidgets.ApplyRounded(badge, 4);
        _sizeText = factory(_sizeBadge, "", 15); _sizeText.alignment = TextAlignmentOptions.Midline; _sizeText.raycastTarget = false;
        var st = _sizeText.rectTransform; st.anchorMin = Vector2.zero; st.anchorMax = Vector2.one; st.offsetMin = st.offsetMax = Vector2.zero;
        _sizeBadge.gameObject.SetActive(false);

        _bar = DocCanvas.Node(_layer, "ObjectBar"); DocCanvas.TopLeft(_bar);
        var barImage = _bar.gameObject.AddComponent<Image>(); barImage.color = DocToolbar.Strip; ModConfigSidebarWidgets.ApplyRounded(barImage, 6);
        _bar.gameObject.AddComponent<DocEventSink>();
        void Tool(string caption, Action click, string tip = "", UiActionRole role = UiActionRole.Edit) { var b = DocToolButton.Create(_bar, caption, factory, false, 14, role); b.Clicked = click; b.Tooltip = tip; _barButtons.Add(b); }
        Tool(Loc.T("Left", "靠左", "靠左"), () => Align("left"));
        Tool(Loc.T("Center", "居中", "置中"), () => Align("center"));
        Tool(Loc.T("Right", "靠右", "靠右"), () => Align("right"));
        foreach (int size in new[] { 25, 50, 75, 100 }) { int s = size; Tool(s + "%", () => SetWidth(s)); }
        Tool(Loc.T("Caption", "说明", "說明"), () => EditCaption?.Invoke(_editor.Selection.Object)); _caption = _barButtons[_barButtons.Count - 1];
        Tool(Loc.T("Replace", "替换", "替換"), () => Replace?.Invoke(_editor.Selection.Object));
        Tool(Loc.T("Delete", "删除", "刪除"), () => Delete?.Invoke(_editor.Selection.Object), role: UiActionRole.Danger);
        float x = 5; foreach (var b in _barButtons) { float w = Mathf.Max(34, b.PreferredWidth); DocCanvas.Place((RectTransform)b.transform, x, 4, w, 26); x += w + 3; }
        _bar.sizeDelta = new Vector2(x + 2, 34);
        _bar.gameObject.SetActive(false);

        _grip = DocCanvas.Node(_layer, "BlockGrip"); DocCanvas.TopLeft(_grip); _grip.sizeDelta = new Vector2(18, 24);
        var grip = _grip.gameObject.AddComponent<DocGripGraphic>(); grip.color = ModConfigLook.Surface(new Color(.85f, .80f, .68f, .75f));
        var gripDrag = _grip.gameObject.AddComponent<DocDragRelay>();
        gripDrag.Begin = BeginBlockDrag; gripDrag.Move = MoveBlockDrag; gripDrag.End = EndBlockDrag; gripDrag.Click = ClickGrip;
        _grip.gameObject.SetActive(false);

        _marker = DocCanvas.Node(_layer, "DropMarker"); DocCanvas.TopLeft(_marker);
        var marker = _marker.gameObject.AddComponent<Image>(); marker.color = new Color(.30f, .70f, 1f, .95f); marker.raycastTarget = false;
        _marker.gameObject.SetActive(false);
    }

    private void Align(string align) { _editor.SetMediaAlign(_editor.Selection.Object, align); _changed(); }
    private void SetWidth(int width)
    {
        var o = _editor.Selection.Object; if (_editor.MediaOf(o) == null) return;
        _editor.Checkpoint("structure"); _editor.SetMediaWidth(o, Mathf.Min(width, MaxWidth(o))); _changed();
    }

    private int MaxWidth(DocObject o)
    {
        if (o.Block < 0 || o.Block >= _editor.Doc.Blocks.Count || _editor.Doc.Blocks[o.Block] is not RowBlock row) return 100;
        // Keeping the row total within 100% keeps pictures side by side however they are resized.
        return Mathf.Max(10, 100 - row.Cells.Where((c, i) => i != o.Cell).Sum(c => c.Width));
    }

    /// <summary>Called every frame after layout.</summary>
    public void Update(Vector2? pointerScreen, Camera? camera)
    {
        _camera = camera;
        var o = _editor.Selection.Object;
        var rect = Author ? _canvas.ObjectRect(o) : null;
        bool media = rect != null && _editor.MediaOf(o) != null;
        for (int i = 0; i < 4; i++)
        {
            _handles[i].gameObject.SetActive(media);
            if (media) { var r = rect!.Value; float hx = i % 2 == 0 ? r.xMin : r.xMax, hy = i < 2 ? r.yMin : r.yMax; DocCanvas.Place(_handles[i], hx - 6, hy - 6, 12, 12); }
        }
        _bar.gameObject.SetActive(rect != null && !_dragging);
        if (rect is Rect objectRect)
        {
            for (int i = 0; i < _barButtons.Count - 1; i++) _barButtons[i].gameObject.SetActive(media);
            float width = media ? _bar.sizeDelta.x : 0;
            if (!media) { var del = _barButtons[_barButtons.Count - 1]; DocCanvas.Place((RectTransform)del.transform, 5, 4, Mathf.Max(34, del.PreferredWidth), 26); width = del.PreferredWidth + 8; }
            else { float x = 5; foreach (var b in _barButtons) { float w = Mathf.Max(34, b.PreferredWidth); DocCanvas.Place((RectTransform)b.transform, x, 4, w, 26); x += w + 3; } width = x + 2; }
            float barX = Mathf.Clamp(objectRect.x, 4, Mathf.Max(4, _canvas.PageWidth - width - 4));
            float barY = objectRect.y - 40 >= _canvas.ScrollOffset ? objectRect.y - 40 : objectRect.yMax + 8;
            DocCanvas.Place(_bar, barX, barY, width, 34);
        }
        _sizeBadge.gameObject.SetActive(_resizing && rect != null);
        if (_resizing && rect is Rect rr && _editor.MediaOf(o) is MediaBlock m) { _sizeText.text = m.Width + "%"; DocCanvas.Place(_sizeBadge, rr.center.x - 30, rr.center.y - 14, 60, 28); }

        if (_dragging && pointerScreen is Vector2 drag) { AutoScroll(drag); UpdateDrop(drag); }
        else if (!_resizing) UpdateGrip(pointerScreen);
    }

    private void UpdateGrip(Vector2? pointerScreen)
    {
        if (!Author || pointerScreen == null || !RectTransformUtility.RectangleContainsScreenPoint(_canvas.Viewport, pointerScreen.Value, _camera))
        { if (!_grip.gameObject.activeSelf || !Over(_grip, pointerScreen)) _grip.gameObject.SetActive(false); return; }
        if (Over(_grip, pointerScreen)) return;
        var doc = _canvas.ScreenToDoc(pointerScreen.Value, _camera);
        int b = _canvas.BlockAtY(doc.y); if (b < 0) { _grip.gameObject.SetActive(false); return; }
        var view = _canvas.Views[b];
        _hoverBlock = b; _hoverCell = -1;
        if (view.Cells.Count > 0)
        {
            int cell = 0; float best = float.MaxValue;
            for (int i = 0; i < view.Cells.Count; i++) { float d = Mathf.Abs(doc.x - (view.Cells[i].X + view.Cells[i].W / 2)); if (d < best) { best = d; cell = i; } }
            _hoverCell = view.Block is RowBlock ? cell : -1;
            var c = view.Cells[cell];
            DocCanvas.Place(_grip, c.X + 4, c.Y + 4, 18, 24);
        }
        else if (view.Block is ParagraphBlock p)
        {
            if (p.Length == 0 && _editor.Doc.Blocks.Count == 1) { _grip.gameObject.SetActive(false); return; }
            DocCanvas.Place(_grip, Mathf.Max(2, DocCanvas.PadX - 24 + p.Indent * DocCanvas.IndentStep), view.TextTop + Mathf.Max(0, view.FontSize * .65f - 12), 18, 24);
        }
        else DocCanvas.Place(_grip, Mathf.Max(2, DocCanvas.PadX - 24), view.Y, 18, 24);
        _grip.gameObject.SetActive(true);
    }

    private bool Over(RectTransform rect, Vector2? screen) => screen != null && rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, screen.Value, _camera);

    // ---------------- resize ----------------

    private void BeginResize(int corner, PointerEventData data)
    {
        var o = _editor.Selection.Object; var rect = _canvas.ObjectRect(o); var media = _editor.MediaOf(o);
        if (rect == null || media == null) return;
        _resizing = true; _resizeTarget = o; _resizeCorner = corner; _resizeLeft = rect.Value.xMin; _resizeRight = rect.Value.xMax;
        _editor.Checkpoint("structure"); _editor.BreakCoalescing();
    }
    private void MoveResize(PointerEventData data)
    {
        if (!_resizing) return;
        var media = _editor.MediaOf(_resizeTarget); if (media == null) { _resizing = false; return; }
        var doc = _canvas.ScreenToDoc(data.position, data.pressEventCamera);
        bool inRow = _editor.Doc.Blocks[_resizeTarget.Block] is RowBlock;
        string align = inRow ? ((RowBlock)_editor.Doc.Blocks[_resizeTarget.Block]).Align : media.Align;
        bool rightHandle = _resizeCorner % 2 == 1;
        float px = !inRow && align == "center" ? 2 * Mathf.Abs(doc.x - (_resizeLeft + _resizeRight) / 2)
            : rightHandle ? doc.x - _resizeLeft : _resizeRight - doc.x;
        int cells = inRow ? ((RowBlock)_editor.Doc.Blocks[_resizeTarget.Block]).Cells.Count : 1;
        float available = Mathf.Max(40, _canvas.Usable - DocCanvas.RowGap * (cells - 1));
        int percent = Mathf.RoundToInt(px / available * 100);
        foreach (int snap in Snaps) if (Mathf.Abs(percent - snap) <= 2) { percent = snap; break; }
        percent = Mathf.Clamp(percent, 10, MaxWidth(_resizeTarget));
        _editor.SetMediaWidth(_resizeTarget, percent); _changed();
    }
    private void EndResize() { _resizing = false; }

    // ---------------- block drag ----------------

    private void ClickGrip(PointerEventData data)
    {
        if (_hoverBlock < 0 || _hoverBlock >= _editor.Doc.Blocks.Count) return;
        if (_editor.Doc.Blocks[_hoverBlock] is ParagraphBlock p) _editor.SelectRange(new DocPos(_hoverBlock, 0), new DocPos(_hoverBlock, p.Length));
        else _editor.SelectObject(new DocObject(_hoverBlock, _hoverCell));
        _changed();
    }

    private void BeginBlockDrag(PointerEventData data)
    {
        if (_hoverBlock < 0 || _hoverBlock >= _canvas.Views.Count) return;
        _dragging = true; _dragBlock = _hoverBlock; _dragCell = _hoverCell; _drop = null;
        var view = _canvas.Views[_dragBlock];
        _ghost = DocCanvas.Node(_root, "DragGhost"); _ghost.anchorMin = _ghost.anchorMax = _root.pivot; _ghost.pivot = new Vector2(0, 1);
        var group = _ghost.gameObject.AddComponent<CanvasGroup>(); group.alpha = .72f; group.blocksRaycasts = false; group.interactable = false;
        var back = _ghost.gameObject.AddComponent<Image>(); back.color = ModConfigLook.Surface(new Color(.17f, .2f, .24f, .96f)); back.raycastTarget = false; ModConfigSidebarWidgets.ApplyRounded(back, 6);
        var cell = view.Cells.Count > 0 ? view.Cells[Mathf.Clamp(_dragCell, 0, view.Cells.Count - 1)] : null;
        if (cell != null && cell.Media.PreviewTexture is Texture texture)
        {
            float w = 180, h = Mathf.Clamp(w / cell.Media.AspectRatio, 40, 180);
            _ghost.sizeDelta = new Vector2(w + 8, h + 8);
            var image = DocCanvas.Node(_ghost, "Thumbnail").gameObject.AddComponent<RawImage>(); image.texture = texture; image.raycastTarget = false;
            var ir = image.rectTransform; ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one; ir.offsetMin = new Vector2(4, 4); ir.offsetMax = new Vector2(-4, -4);
        }
        else
        {
            string caption = view.Block is ParagraphBlock p ? (p.Text.Length > 40 ? p.Text.Substring(0, 40) + "…" : p.Text) : view.Block is DividerBlock ? "———" : Loc.T("Media", "媒体", "媒體");
            _ghost.sizeDelta = new Vector2(260, 40);
            var text = _factory(_ghost, caption.Replace("\n", " "), 17); text.raycastTarget = false; text.overflowMode = TextOverflowModes.Ellipsis; text.alignment = TextAlignmentOptions.MidlineLeft;
            var tr = text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(10, 0); tr.offsetMax = new Vector2(-10, 0);
        }
        // The grip must stay active: it owns the drag events until the gesture ends.
        _grip.GetComponent<DocGripGraphic>().enabled = false;
        MoveBlockDrag(data);
    }

    private void MoveBlockDrag(PointerEventData data)
    {
        if (!_dragging || _ghost == null) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, data.position, data.pressEventCamera, out var local)) _ghost.anchoredPosition = local + new Vector2(16, -16);
        UpdateDrop(data.position);
    }

    private void UpdateDrop(Vector2 screen)
    {
        var doc = _canvas.ScreenToDoc(screen, _camera);
        int b = _canvas.BlockAtY(doc.y);
        _drop = null; _marker.gameObject.SetActive(false);
        if (b < 0) return;
        var view = _canvas.Views[b]; var source = _canvas.Views[_dragBlock];
        bool sourceMedia = source.Cells.Count > 0;
        if (sourceMedia && view.Cells.Count > 0)
        {
            for (int i = 0; i < view.Cells.Count; i++)
            {
                var c = view.Cells[i];
                if (doc.y < c.Y || doc.y > c.Y + c.H || doc.x < c.X - 20 || doc.x > c.X + c.W + 20) continue;
                bool self = b == _dragBlock && (view.Block is not RowBlock || i == _dragCell);
                if (self) break;
                float rel = (doc.x - c.X) / Mathf.Max(1, c.W);
                if (rel < .3f || rel > .7f)
                {
                    bool right = rel > .7f;
                    _drop = (b, view.Block is RowBlock ? i : -1, right ? DropPlacement.RightOf : DropPlacement.LeftOf);
                    DocCanvas.Place(_marker, right ? c.X + c.W + DocCanvas.RowGap / 2 - 2 : c.X - DocCanvas.RowGap / 2 - 2, c.Y, 4, c.H);
                    _marker.gameObject.SetActive(true); return;
                }
            }
        }
        bool after = doc.y > view.Y + view.Height / 2;
        _drop = (b, -1, after ? DropPlacement.After : DropPlacement.Before);
        float y = after ? view.Y + view.Height + 3 : view.Y - Mathf.Max(3, view.SpaceBefore / 2);
        DocCanvas.Place(_marker, DocCanvas.PadX, y - 1.5f, _canvas.Usable, 3);
        _marker.gameObject.SetActive(true);
    }

    private void AutoScroll(Vector2 screen)
    {
        var viewport = _canvas.Viewport;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screen, _camera, out var local)) return;
        float edge = 40, speed = 0;
        if (local.y > viewport.rect.yMax - edge) speed = -(local.y - (viewport.rect.yMax - edge));
        else if (local.y < viewport.rect.yMin + edge) speed = (viewport.rect.yMin + edge) - local.y;
        if (speed != 0) _canvas.ScrollOffset += speed * Time.unscaledDeltaTime * 12;
    }

    private void EndBlockDrag(PointerEventData data)
    {
        if (!_dragging) return;
        _dragging = false; _marker.gameObject.SetActive(false);
        _grip.GetComponent<DocGripGraphic>().enabled = true; _grip.gameObject.SetActive(false);
        if (_ghost != null) { _ghost.gameObject.SetActive(false); UnityEngine.Object.Destroy(_ghost.gameObject); _ghost = null; }
        if (_drop is not { } drop || drop.Block < 0 || _dragBlock >= _editor.Doc.Blocks.Count) return;
        bool moved;
        if (_editor.Doc.Blocks[_dragBlock] is ParagraphBlock)
        {
            if (drop.Placement == DropPlacement.LeftOf || drop.Placement == DropPlacement.RightOf) return;
            moved = _editor.MoveParagraphs(_dragBlock, drop.Block, drop.Placement);
        }
        else moved = _editor.MoveObject(new DocObject(_dragBlock, _dragCell), drop.Block, drop.Cell, drop.Placement);
        if (moved) _changed();
    }

    public void Cancel()
    {
        _resizing = false;
        if (_dragging) { _dragging = false; _marker.gameObject.SetActive(false); _grip.GetComponent<DocGripGraphic>().enabled = true; if (_ghost != null) { UnityEngine.Object.Destroy(_ghost.gameObject); _ghost = null; } }
    }
}

/// <summary>Forwards drag gestures of small handles; also blocks them from reaching the page surface.</summary>
internal sealed class DocDragRelay : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    internal Action<PointerEventData>? Begin, Move, End, Click;
    private bool _dragging;
    public void OnPointerDown(PointerEventData eventData) { }
    public void OnPointerClick(PointerEventData eventData) { if (!_dragging && eventData.button == PointerEventData.InputButton.Left) Click?.Invoke(eventData); }
    public void OnBeginDrag(PointerEventData eventData) { if (eventData.button != PointerEventData.InputButton.Left) return; _dragging = true; eventData.eligibleForClick = false; Begin?.Invoke(eventData); }
    public void OnDrag(PointerEventData eventData) { if (_dragging) Move?.Invoke(eventData); }
    public void OnEndDrag(PointerEventData eventData) { if (!_dragging) return; _dragging = false; End?.Invoke(eventData); }
    private void OnDisable() { if (_dragging) { _dragging = false; End?.Invoke(new PointerEventData(EventSystem.current)); } }
}
