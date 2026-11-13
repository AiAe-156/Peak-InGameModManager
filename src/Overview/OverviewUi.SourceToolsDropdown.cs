#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed class SourceToolOption
{
    internal UiMenuEntry Entry = new();
    internal Func<bool>? ReadValue;
    internal Action<bool>? WriteValue;
    internal Func<string>? Hint;
}
internal sealed class SourceToolGroup
{
    internal readonly string Label;
    internal readonly Color Tint;
    internal readonly IReadOnlyList<SourceToolOption> Options;
    internal SourceToolGroup(string label, Color tint, IReadOnlyList<SourceToolOption> options)
    { Label = label; Tint = tint; Options = options; }
}
internal static partial class OverviewUi
{
    internal static RectTransform? SourceToolsDropdown(RectTransform anchor, IReadOnlyList<SourceToolGroup> groups,
        Action<GameObject, Func<string>> bindHint, RectTransform? owner = null)
    {
        var host = owner ?? ModalParent as RectTransform;
        if (host == null || anchor == null) return null;
        bool owned = owner != null;
        RectTransform? shade = null;
        void CloseMenu() { if (owned) owner!.GetComponent<WindowPopover>()?.Close(); else CloseModal(); }
        // The opener can be an item in More, so capture its button position before replacing that modal.
        var corners = new Vector3[4]; anchor.GetWorldCorners(corners);
        var bottomLeft = host.InverseTransformPoint(corners[0]); var topRight = host.InverseTransformPoint(corners[2]);
        var anchorBox = new UiGeometry.Box(bottomLeft.x - host.rect.xMin, host.rect.yMax - topRight.y,
            topRight.x - bottomLeft.x, topRight.y - bottomLeft.y);
        if (owned) owner!.GetComponent<WindowPopover>()?.Close(); else CloseModal();
        shade = Panel("MCE_SourceToolsDropdown", host, ModConfigLook.Surface(new Color(0, 0, 0, .22f)));
        Fill(shade); shade.SetAsLastSibling();
        if (owned) WindowPopover.Adopt(owner!, shade); else _modal = shade.gameObject;
        shade.gameObject.AddComponent<OverviewTextClick>().Clicked = _ => CloseMenu();
        var panel = Panel("SourceTools", shade, Brown); panel.gameObject.AddComponent<RectMask2D>();
        panel.gameObject.AddComponent<OverviewTextClick>();
        panel.gameObject.AddComponent<DropdownFocusReturn>().Anchor = anchor;
        // This menu navigates by whole rows; each embedded checkbox shares its row's submit action.
        PublicationNavigation.AttachBoundary(panel);
        var automaticNavigation = panel.GetComponent<PublicationNavigation>();
        if (automaticNavigation != null) automaticNavigation.enabled = false;
        var measure = Text(panel, "", 16);
        string Caption(SourceToolOption option) => (option.ReadValue == null && option.Entry.Checked ? "✓ " : "")
            + option.Entry.Label.Replace('\r', ' ').Replace('\n', ' ');
        float measured = groups.SelectMany(group => group.Options).Select(option =>
            measure.GetPreferredValues(Caption(option)).x + (option.ReadValue != null ? 76 : 36))
            .Concat(groups.Select(group => measure.GetPreferredValues(group.Label).x + 36)).DefaultIfEmpty(300).Max();
        measure.gameObject.SetActive(false); UnityEngine.Object.Destroy(measure.gameObject);
        float availableWidth = Mathf.Max(1, host.rect.width - 16);
        float width = Mathf.Clamp(measured, Mathf.Min(260, availableWidth), Mathf.Min(420, availableWidth));
        float rowWidth = Mathf.Max(1, width - 16);
        var viewport = Rect("Viewport", panel); Fill(viewport, 8);
        viewport.gameObject.AddComponent<Image>().color = Color.clear; viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Options", viewport); content.anchorMax = Vector2.one;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.inertia = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        var controls = new List<Button>(); var checks = new List<(Toggle Check, Button Row)>(); Button? selected = null; float y = 0;
        foreach (var group in groups)
        {
            if (y > 0)
            {
                var line = Panel("SectionRule", content, new Color(group.Tint.r, group.Tint.g, group.Tint.b, .42f));
                line.GetComponent<Image>().raycastTarget = false; Place(line, 8, y + 3, Mathf.Max(1, rowWidth - 16), 1);
                y += 8;
            }
            var heading = Text(content, group.Label, 15); heading.color = group.Tint;
            heading.alignment = TextAlignmentOptions.MidlineLeft; heading.enableAutoSizing = true;
            heading.fontSizeMin = 12; heading.fontSizeMax = 15;
            float headingHeight = Mathf.Max(26, Mathf.Ceil(heading.GetPreferredValues(group.Label).y) + 4);
            heading.gameObject.SetActive(group.Label.Length > 0);
            if (group.Label.Length > 0) { PlaceClipped(heading, 12, y, Mathf.Max(1, rowWidth - 24), headingHeight); y += headingHeight; }
            foreach (var option in group.Options)
            {
                var item = option.Entry; Toggle? check = null;
                var button = FlatListRow.Create(content, () => {
                    if (!item.Enabled || item.CanInvoke?.Invoke() == false) return;
                    if (check != null) { check.isOn = !option.ReadValue!(); return; }
                    CloseMenu(); item.Execute();
                }, rowWidth, 32, item.Checked ? ModConfigLook.Surface(new Color(.20f, .48f, .60f)) : Color.Lerp(ModConfigLook.Surface(new Color(.12f, .11f, .09f)), UiActionStyle.ColorFor(item.Role, ModConfigLook.IsDark), .35f));
                button.gameObject.name = "SourceTool_" + controls.Count; button.interactable = item.Enabled;
                var label = Text(button.transform, Caption(option), 16);
                label.color = item.Enabled ? Ivory : Muted; label.alignment = TextAlignmentOptions.MidlineLeft;
                label.enableAutoSizing = true; label.fontSizeMin = 12; label.fontSizeMax = 16; label.margin = Vector4.zero;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                float rowHeight = Mathf.Max(32, Mathf.Ceil(label.GetPreferredValues(label.text).y) + 4);
                var rect = (RectTransform)button.transform; Place(rect, 0, y, rowWidth, rowHeight); y += rowHeight + 2;
                if (option.ReadValue != null && option.WriteValue != null)
                {
                    check = Check(button.transform, option.ReadValue(), value => {
                        if (item.Enabled && item.CanInvoke?.Invoke() != false) option.WriteValue(value);
                        if (check != null) SyncCheck(check, option.ReadValue());
                    });
                    check.interactable = item.Enabled; check.navigation = new Navigation { mode = Navigation.Mode.None };
                    Place((RectTransform)check.transform, 12, (rowHeight - 28) / 2, 28, 28);
                    bindHint(check.gameObject, option.Hint ?? (() => item.Label));
                    GamepadSupport.MarkSelectable(check.gameObject, scroll, rect); checks.Add((check, button));
                }
                float labelX = check != null ? 48 : 16;
                PlaceClipped(label, labelX, 0, Mathf.Max(1, rowWidth - labelX - 16), rowHeight);
                bindHint(button.gameObject, option.Hint ?? (() => item.Label));
                GamepadSupport.MarkSelectable(button.gameObject, scroll, rect);
                if (item.Enabled) { controls.Add(button); if (item.Checked && selected == null) selected = button; }
            }
        }
        content.sizeDelta = new Vector2(0, Mathf.Max(1, y));
        var layout = panel.gameObject.AddComponent<SourceToolsDropdownLayout>();
        layout.Initialize(host, panel, anchor, anchorBox, width, y + 16, CloseMenu);
        for (int i = 0; i < controls.Count; i++) controls[i].navigation = new Navigation {
            mode = Navigation.Mode.Explicit, selectOnUp = i > 0 ? controls[i - 1] : controls[controls.Count - 1],
            selectOnDown = i + 1 < controls.Count ? controls[i + 1] : controls[0]
        };
        foreach (var (check, row) in checks)
        {
            var navigation = row.navigation; navigation.selectOnLeft = row; navigation.selectOnRight = row;
            check.navigation = navigation;
        }
        if (controls.Count > 0) EventSystem.current?.SetSelectedGameObject((selected ?? controls[0]).gameObject);
        return panel;
    }
}

internal sealed class SourceToolsDropdownLayout : MonoBehaviour
{
    private RectTransform _host = null!, _panel = null!, _anchor = null!;
    private UiGeometry.Box _anchorBox;
    private Vector2 _hostSize;
    private float _width, _height;
    private Action _close = null!;
    internal void Initialize(RectTransform host, RectTransform panel, RectTransform anchor, UiGeometry.Box anchorBox, float width, float height, Action close)
    { _host = host; _panel = panel; _anchor = anchor; _anchorBox = anchorBox; _width = width; _height = height; _close = close; Reposition(); }
    private void Reposition()
    {
        _hostSize = _host.rect.size;
        var bounds = UiGeometry.Popover(_hostSize.x, _hostSize.y, _anchorBox, _width, _height);
        OverviewUi.Place(_panel, bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
    private void LateUpdate()
    {
        if (_host == null || _anchor == null || !_anchor.gameObject.activeInHierarchy) { _close(); return; }
        if (_hostSize != _host.rect.size) Reposition();
    }
}
