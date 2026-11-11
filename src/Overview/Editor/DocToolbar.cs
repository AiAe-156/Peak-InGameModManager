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
/// Compact Word-style toolbar. Buttons are plain pointer targets (not Selectables) placed under the
/// editor surface, so clicking them never takes keyboard focus or the selection away from the page.
/// Widths follow their captions and the bar wraps onto more rows when the page is narrow.
/// </summary>
internal sealed class DocToolbar
{
    internal const float RowHeight = 30, Gap = 4, GroupGap = 12, Pad = 6;
    internal static Color Normal => ModConfigLook.Surface(new(.23f, .19f, .14f, .96f));
    internal static Color Hover => ModConfigLook.Surface(new(.33f, .27f, .19f, .98f));
    internal static Color Active => ModConfigLook.Surface(new(.56f, .45f, .22f, 1));
    internal static Color Strip => ModConfigLook.Surface(new(.13f, .11f, .08f, .92f));

    private sealed class Item
    {
        public DocToolButton Button = null!; public float Width; public bool GroupStart;
        public Func<bool>? IsActive; public Func<bool>? IsEnabled; public Func<string>? Caption;
    }

    public RectTransform Root { get; }
    public float Height { get; private set; } = RowHeight + Pad * 2;
    private readonly List<Item> _items = new();
    private readonly Func<Transform, string, float, TextMeshProUGUI> _factory;
    private readonly RectTransform _tooltip;
    private readonly TextMeshProUGUI _tooltipText;
    private bool _nextStartsGroup;
    private float _laidOutWidth = -1;

    public DocToolbar(Transform parent, Func<Transform, string, float, TextMeshProUGUI> factory, Transform tooltipLayer)
    {
        _factory = factory;
        Root = DocCanvas.Node(parent, "DocToolbar");
        var strip = Root.gameObject.AddComponent<Image>(); strip.color = Strip; ModConfigSidebarWidgets.ApplyRounded(strip, 6);
        Root.gameObject.AddComponent<DocEventSink>();
        _tooltip = DocCanvas.Node(tooltipLayer, "DocTooltip"); DocCanvas.TopLeft(_tooltip);
        var back = _tooltip.gameObject.AddComponent<Image>(); back.color = ModConfigLook.Surface(new Color(.06f, .05f, .04f, .95f)); back.raycastTarget = false; ModConfigSidebarWidgets.ApplyRounded(back, 4);
        _tooltipText = factory(_tooltip, "", 15); _tooltipText.raycastTarget = false; _tooltipText.textWrappingMode = TextWrappingModes.NoWrap;
        var tr = _tooltipText.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(8, 3); tr.offsetMax = new Vector2(-8, -3);
        _tooltip.gameObject.SetActive(false);
    }

    public void Group() => _nextStartsGroup = true;

    public DocToolButton Add(string caption, Action click, string tooltip = "", Func<bool>? active = null, bool dropdown = false, float minWidth = 0, Func<string>? dynamicCaption = null, Func<bool>? enabled = null, UiActionRole role = UiActionRole.Edit)
    {
        var button = DocToolButton.Create(Root, caption, _factory, dropdown, role: role);
        button.Clicked = click; button.Tooltip = tooltip; button.ShowTooltip = ShowTooltip; button.HideTooltip = HideTooltip;
        float width = Mathf.Max(minWidth, button.PreferredWidth);
        _items.Add(new Item { Button = button, Width = width, GroupStart = _nextStartsGroup && _items.Count > 0, IsActive = active, IsEnabled = enabled, Caption = dynamicCaption });
        _nextStartsGroup = false; _laidOutWidth = -1;
        return button;
    }

    /// <summary>Flows the buttons into rows for the given width; returns the bar height.</summary>
    public float Layout(float width)
    {
        if (Mathf.Abs(width - _laidOutWidth) < .5f) return Height;
        _laidOutWidth = width;
        float x = Pad, y = Pad;
        foreach (var item in _items)
        {
            float gap = item.GroupStart ? GroupGap : Gap;
            if (x > Pad) x += gap;
            if (x + item.Width > width - Pad && x > Pad) { x = Pad; y += RowHeight + Gap; }
            DocCanvas.Place((RectTransform)item.Button.transform, x, y, item.Width, RowHeight);
            x += item.Width;
        }
        Height = y + RowHeight + Pad;
        return Height;
    }

    public void RefreshState()
    {
        foreach (var item in _items)
        {
            if (item.Caption != null)
            {
                string caption = item.Caption();
                if (caption != item.Button.Caption) { item.Button.SetCaption(caption); }
            }
            item.Button.SetState(item.IsActive?.Invoke() == true, item.IsEnabled?.Invoke() != false);
        }
    }

    private void ShowTooltip(DocToolButton button)
    {
        if (string.IsNullOrEmpty(button.Tooltip)) return;
        _tooltipText.text = button.Tooltip;
        var size = _tooltipText.GetPreferredValues(button.Tooltip);
        var parent = (RectTransform)_tooltip.parent;
        var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
        Vector2 local = parent.InverseTransformPoint(corners[0]);
        float x = Mathf.Clamp(local.x - parent.rect.xMin, 0, Mathf.Max(0, parent.rect.width - size.x - 16));
        float y = parent.rect.yMax - local.y + 4;
        DocCanvas.Place(_tooltip, x, y, size.x + 16, size.y + 6);
        _tooltip.gameObject.SetActive(true); _tooltip.SetAsLastSibling();
    }
    public void HideTooltip(DocToolButton? _ = null) => _tooltip.gameObject.SetActive(false);
}

/// <summary>Stops pointer events from reaching the page surface behind toolbars and menus.</summary>
internal sealed class DocEventSink : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    internal Action? Clicked;
    public void OnPointerDown(PointerEventData eventData) { }
    public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData) { }
}

internal sealed class DocToolButton : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    internal Action? Clicked;
    internal string Tooltip = "";
    internal Action<DocToolButton>? ShowTooltip, HideTooltip;
    internal string Caption { get; private set; } = "";
    internal float PreferredWidth { get; private set; }
    internal Image Background = null!;
    internal TextMeshProUGUI Label = null!;
    internal Color? Swatch;
    internal UiActionRole Role;
    private bool? _lastDark;
    private DocTriangleGraphic? _arrow;
    private bool _hover, _active, _enabled = true;

    internal static DocToolButton Create(Transform parent, string caption, Func<Transform, string, float, TextMeshProUGUI> factory, bool dropdown, float fontSize = 15, UiActionRole role = UiActionRole.Edit)
    {
        var rect = DocCanvas.Node(parent, "Tool"); DocCanvas.TopLeft(rect);
        var button = rect.gameObject.AddComponent<DocToolButton>();
        button.Role = role;
        button.Background = rect.gameObject.AddComponent<Image>(); ModConfigSidebarWidgets.ApplyRounded(button.Background, 5);
        button.Label = factory(rect, "", fontSize); MceTextWeight.Apply(button.Label); button.Label.richText = true; button.Label.alignment = TextAlignmentOptions.Midline; button.Label.raycastTarget = false;
        button.Label.textWrappingMode = TextWrappingModes.NoWrap; button.Label.overflowMode = TextOverflowModes.Ellipsis;
        foreach (var shadow in button.Label.GetComponents<Shadow>()) shadow.enabled = false;
        var lr = button.Label.rectTransform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = new Vector2(7, 0); lr.offsetMax = new Vector2(dropdown ? -16 : -7, 0);
        if (dropdown)
        {
            var arrow = DocCanvas.Node(rect, "Arrow"); arrow.anchorMin = arrow.anchorMax = new Vector2(1, .5f); arrow.pivot = new Vector2(1, .5f); arrow.sizeDelta = new Vector2(8, 5); arrow.anchoredPosition = new Vector2(-6, 0);
            button._arrow = arrow.gameObject.AddComponent<DocTriangleGraphic>(); button._arrow.color = DocCanvas.Ink; button._arrow.raycastTarget = false;
        }
        button.SetCaption(caption);
        button.Apply();
        return button;
    }

    internal void SetCaption(string caption)
    {
        Caption = caption; Label.text = caption;
        PreferredWidth = Mathf.Ceil(Label.GetPreferredValues(caption).x) + 14 + (_arrow != null ? 10 : 0);
        PreferredWidth = Mathf.Max(PreferredWidth, 28);
    }
    internal void SetState(bool active, bool enabled)
    {
        if (active == _active && enabled == _enabled && _lastDark == ModConfigLook.IsDark) return;
        _active = active; _enabled = enabled; Apply();
    }
    private void Apply()
    {
        _lastDark = ModConfigLook.IsDark;
        var color = UiActionStyle.ColorFor(Role, _lastDark == true);
        if (_active) color = Color.Lerp(color, Color.white, .18f); else if (_hover && _enabled) color = Color.Lerp(color, Color.white, .10f);
        if (!_enabled) color = Color.Lerp(color, ModConfigLook.Surface(new Color(.23f, .22f, .20f)), .7f);
        Background.color = Swatch is Color s ? (_hover ? Color.Lerp(s, Color.white, .25f) : s) : color;
        Label.alpha = _enabled ? 1 : .4f;
    }

    public void OnPointerDown(PointerEventData eventData) { }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !_enabled) return;
        HideTooltip?.Invoke(this);
        Clicked?.Invoke();
    }
    public void OnPointerEnter(PointerEventData eventData) { _hover = true; Apply(); ShowTooltip?.Invoke(this); }
    public void OnPointerExit(PointerEventData eventData) { _hover = false; Apply(); HideTooltip?.Invoke(this); }
    public void OnBeginDrag(PointerEventData eventData) { }
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData) { }
    private void OnDisable() { _hover = false; if (Background != null) Apply(); HideTooltip?.Invoke(this); }
}

internal sealed class DocTriangleGraphic : MaskableGraphic
{
    public override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var r = rectTransform.rect;
        vh.AddVert(new Vector3(r.xMin, r.yMax), color, Vector2.zero); vh.AddVert(new Vector3(r.xMax, r.yMax), color, Vector2.zero); vh.AddVert(new Vector3(r.center.x, r.yMin), color, Vector2.zero);
        vh.AddTriangle(0, 1, 2);
    }
}

/// <summary>Six-dot drag handle drawn as geometry.</summary>
internal sealed class DocGripGraphic : MaskableGraphic
{
    public override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var r = rectTransform.rect; float d = 3;
        // Transparent full quad keeps the whole handle area clickable.
        var clear = new Color(color.r, color.g, color.b, 0);
        vh.AddVert(new Vector3(r.xMin, r.yMin), clear, Vector2.zero); vh.AddVert(new Vector3(r.xMin, r.yMax), clear, Vector2.zero);
        vh.AddVert(new Vector3(r.xMax, r.yMax), clear, Vector2.zero); vh.AddVert(new Vector3(r.xMax, r.yMin), clear, Vector2.zero);
        vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);
        for (int col = 0; col < 2; col++)
            for (int row = 0; row < 3; row++)
            {
                float cx = r.center.x + (col == 0 ? -3 : 3), cy = r.center.y + (row - 1) * 6;
                int i = vh.currentVertCount;
                vh.AddVert(new Vector3(cx - d / 2, cy - d / 2), color, Vector2.zero); vh.AddVert(new Vector3(cx - d / 2, cy + d / 2), color, Vector2.zero);
                vh.AddVert(new Vector3(cx + d / 2, cy + d / 2), color, Vector2.zero); vh.AddVert(new Vector3(cx + d / 2, cy - d / 2), color, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
            }
    }
}

/// <summary>
/// Drop-down menu anchored under a toolbar button. Lives under the editor surface, so opening it and
/// picking an item keeps the caret and selection; clicking elsewhere closes it.
/// </summary>
internal sealed class DocPopover
{
    private readonly GameObject _shade;
    private readonly RectTransform _panel, _content;
    private readonly Func<Transform, string, float, TextMeshProUGUI> _factory;
    private float _y;
    private float _x;
    private readonly float _width;
    private readonly int _columns;
    private readonly float _cell;
    private int _column;
    internal Action? Closed;
    internal bool IsOpen => _shade != null;

    internal DocPopover(RectTransform layer, RectTransform anchor, float width, Func<Transform, string, float, TextMeshProUGUI> factory, int columns = 1, float cell = 0)
    {
        _factory = factory; _width = width; _columns = columns; _cell = cell > 0 ? cell : width - 12;
        _shade = DocCanvas.Node(layer, "DocMenuShade").gameObject; var shadeRect = (RectTransform)_shade.transform;
        shadeRect.anchorMin = Vector2.zero; shadeRect.anchorMax = Vector2.one; shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
        var shadeImage = _shade.AddComponent<Image>(); shadeImage.color = Color.clear;
        _shade.AddComponent<DocEventSink>().Clicked = Close;
        _panel = DocCanvas.Node(_shade.transform, "Menu"); DocCanvas.TopLeft(_panel);
        var image = _panel.gameObject.AddComponent<Image>(); image.color = ModConfigLook.Surface(new Color(.12f, .10f, .08f, .98f)); ModConfigSidebarWidgets.ApplyRounded(image, 6);
        _panel.gameObject.AddComponent<DocEventSink>();
        var viewport = DocCanvas.Node(_panel, "Viewport"); viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one; viewport.offsetMin = new Vector2(0, 4); viewport.offsetMax = new Vector2(0, -4); viewport.gameObject.AddComponent<RectMask2D>();
        _content = DocCanvas.Node(viewport, "Items"); _content.anchorMin = new Vector2(0, 1); _content.anchorMax = new Vector2(1, 1); _content.pivot = new Vector2(0, 1); _content.sizeDelta = Vector2.zero;
        var scroll = _panel.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = _content; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
        _y = 2; _x = 6;
        var corners = new Vector3[4]; anchor.GetWorldCorners(corners);
        Vector2 local = layer.InverseTransformPoint(corners[0]);
        float left = Mathf.Clamp(local.x - layer.rect.xMin, 2, Mathf.Max(2, layer.rect.width - width - 2));
        float top = layer.rect.yMax - local.y + 3;
        DocCanvas.Place(_panel, left, top, width, 10);
        _maxHeight = Mathf.Max(80, layer.rect.height - top - 6);
    }
    private readonly float _maxHeight;

    internal DocToolButton Item(string caption, Action pick, bool selected = false, float height = 28, float fontSize = 16, UiActionRole role = UiActionRole.Edit)
    {
        var button = DocToolButton.Create(_content, caption, _factory, false, fontSize, role);
        button.Label.alignment = _columns == 1 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Midline;
        button.Clicked = () => { Close(); pick(); };
        button.SetState(selected, true);
        if (_columns == 1) { DocCanvas.Place((RectTransform)button.transform, 6, _y, _width - 12, height); _y += height + 3; }
        else
        {
            DocCanvas.Place((RectTransform)button.transform, _x, _y, _cell, height);
            if (++_column >= _columns) { _column = 0; _x = 6; _y += height + 4; } else _x += _cell + 4;
        }
        Fit(height);
        return button;
    }
    internal DocToolButton Swatch(string caption, Color color, Action pick, bool selected = false)
    {
        var button = Item(caption, pick, selected, 26, 13); button.Swatch = color; button.SetState(true, true); button.SetState(selected, true);
        button.Label.color = color.grayscale > .55f ? ModConfigLook.Surface(new Color(.1f, .08f, .06f)) : DocCanvas.Ink;
        return button;
    }
    internal void Separator()
    {
        if (_column != 0) { _column = 0; _x = 6; _y += 30; }
        var line = DocCanvas.Node(_content, "Separator"); var image = line.gameObject.AddComponent<Image>(); image.color = ModConfigLook.Surface(new Color(1, 1, 1, .12f)); image.raycastTarget = false;
        DocCanvas.Place(line, 8, _y + 2, _width - 16, 1); _y += 7; Fit(0);
    }
    private void Fit(float lastHeight)
    {
        float total = _y + (_column != 0 ? lastHeight + 4 : 0) + 4;
        _content.sizeDelta = new Vector2(0, total);
        _panel.sizeDelta = new Vector2(_width, Mathf.Min(total + 8, _maxHeight));
    }
    internal void Close()
    {
        if (_shade == null) return;
        _shade.SetActive(false); UnityEngine.Object.Destroy(_shade);
        Closed?.Invoke(); Closed = null;
    }
}
