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
    /// <summary>1.1.0 实测修正（2026-11-16）：选项文字的颜色（日志等级按日志正文同色）；null = 默认象牙白。</summary>
    internal Color? LabelColor;
    /// <summary>实测修正（2026-11-16）：true = 点整行执行选项（关菜单），勾选框只在点框本身 / 手柄左移到框上按 A 时切换（日志「按模组筛选」的收藏框）。</summary>
    internal bool RowInvokes;
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
        Action<GameObject, Func<string>> bindHint, RectTransform? owner = null, float maxWidth = 420)
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
            .Concat(groups.Select(group => measure.GetPreferredValues(group.Label).x * 1.25f + 36)).DefaultIfEmpty(300).Max(); // 标题 19 号粗体，按 16 号量再放宽
        measure.gameObject.SetActive(false); UnityEngine.Object.Destroy(measure.gameObject);
        float availableWidth = Mathf.Max(1, host.rect.width - 16);
        float width = Mathf.Clamp(measured, Mathf.Min(260, availableWidth), Mathf.Min(maxWidth, availableWidth));
        float rowWidth = Mathf.Max(1, width - 16);
        var viewport = Rect("Viewport", panel); Fill(viewport, 8);
        viewport.gameObject.AddComponent<Image>().color = Color.clear; viewport.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Options", viewport); content.anchorMax = Vector2.one;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.inertia = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        var controls = new List<Button>(); var checks = new List<(Toggle Check, Button Row)>(); Button? selected = null; float y = 0;
        var checkValues = new List<(Toggle Check, SourceToolOption Option)>();
        var invokingChecks = new List<(Toggle Check, Button Row)>();
        bool firstGroup = true;
        foreach (var group in groups)
        {
            if (y > 0)
            {
                var line = Panel("SectionRule", content, new Color(group.Tint.r, group.Tint.g, group.Tint.b, .42f));
                line.GetComponent<Image>().raycastTarget = false; Place(line, 8, y + 3, Mathf.Max(1, rowWidth - 16), 1);
                y += 8;
            }
            // 1.1.0 实测修正（用户 2026-11-16：标题行字比选项还小、底色与选项分不开）：菜单的主标题（第一段的标题）
            // 用 19 号粗体，压在一条深色底带上，底带下缘一条金线；后面各段的小节标题用 16 号粗体（与选项同大、靠粗体与颜色区分）。
            bool title = firstGroup; firstGroup = false;
            var heading = Text(content, group.Label, title ? 19 : 16); heading.color = group.Tint;
            heading.fontStyle = FontStyles.Bold;
            heading.alignment = TextAlignmentOptions.MidlineLeft; heading.enableAutoSizing = true;
            heading.fontSizeMin = 13; heading.fontSizeMax = title ? 19 : 16;
            heading.textWrappingMode = TextWrappingModes.NoWrap; heading.overflowMode = TextOverflowModes.Ellipsis;
            float headingHeight = Mathf.Max(title ? 34 : 28, Mathf.Ceil(heading.GetPreferredValues(group.Label).y) + 6);
            heading.gameObject.SetActive(group.Label.Length > 0);
            if (group.Label.Length > 0)
            {
                if (title)
                {
                    var band = Panel("TitleBand", content, ModConfigLook.Surface(new Color(.03f, .04f, .05f, .92f)));
                    band.GetComponent<Image>().raycastTarget = false; Place(band, 0, y, rowWidth, headingHeight); band.SetSiblingIndex(heading.transform.GetSiblingIndex());
                    var rule = Panel("TitleRule", band, new Color(Gold.r, Gold.g, Gold.b, .7f));
                    rule.GetComponent<Image>().raycastTarget = false; Place(rule, 0, headingHeight - 2, rowWidth, 2);
                }
                PlaceClipped(heading, 12, y, Mathf.Max(1, rowWidth - 24), headingHeight); y += headingHeight + (title ? 6 : 0);
            }
            foreach (var option in group.Options)
            {
                var item = option.Entry; Toggle? check = null;
                var button = FlatListRow.Create(content, () => {
                    if (!item.Enabled || item.CanInvoke?.Invoke() == false) return;
                    if (check != null && !option.RowInvokes) { check.isOn = !option.ReadValue!(); return; }
                    CloseMenu(); item.Execute();
                }, rowWidth, 32, item.Checked ? ModConfigLook.Surface(new Color(.20f, .48f, .60f)) : Color.Lerp(ModConfigLook.Surface(new Color(.12f, .11f, .09f)), UiActionStyle.ColorFor(item.Role, ModConfigLook.IsDark), .35f));
                button.gameObject.name = "SourceTool_" + controls.Count; button.interactable = item.Enabled;
                var label = Text(button.transform, Caption(option), 16);
                label.color = !item.Enabled ? Muted : option.LabelColor ?? Ivory; label.alignment = TextAlignmentOptions.MidlineLeft;
                label.enableAutoSizing = true; label.fontSizeMin = 12; label.fontSizeMax = 16; label.margin = Vector4.zero;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                float rowHeight = Mathf.Max(32, Mathf.Ceil(label.GetPreferredValues(label.text).y) + 4);
                var rect = (RectTransform)button.transform; Place(rect, 0, y, rowWidth, rowHeight); y += rowHeight + 2;
                if (option.ReadValue != null && option.WriteValue != null)
                {
                    check = Check(button.transform, option.ReadValue(), value => {
                        if (item.Enabled && item.CanInvoke?.Invoke() != false) option.WriteValue(value);
                        // 一行改了可能连带别的行（日志等级的「全部」）：整份菜单的勾选框都按当前值重画。
                        foreach (var (other, otherOption) in checkValues) if (other != null && otherOption.ReadValue != null) SyncCheck(other, otherOption.ReadValue());
                    });
                    checkValues.Add((check, option));
                    check.interactable = item.Enabled; check.navigation = new Navigation { mode = Navigation.Mode.None };
                    Place((RectTransform)check.transform, 12, (rowHeight - 28) / 2, 28, 28);
                    bindHint(check.gameObject, option.Hint ?? (() => item.Label));
                    GamepadSupport.MarkSelectable(check.gameObject, scroll, rect); checks.Add((check, button));
                    if (option.RowInvokes) invokingChecks.Add((check, button));
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
        // 点整行执行的行：勾选框要能用手柄够到——行上按左进框、框上按右 / 上下回行。
        foreach (var (check, row) in invokingChecks)
        {
            var navigation = row.navigation; navigation.selectOnLeft = check; row.navigation = navigation;
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
