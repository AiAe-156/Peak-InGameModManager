#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal static partial class OverviewUi
{
    private static Color DialogSurface => ModConfigLook.Surface(new(.105f, .12f, .135f, 1));
    private static Color DialogInset => ModConfigLook.Surface(new(.065f, .08f, .095f, 1));

    /// <summary>Title, one-line or two-line subtitle and divider. <paramref name="dividerY"/> lets a dialog that needs the
    /// room (the folder-organization review) pull the divider up under a one-line subtitle.</summary>
    internal static void SaveDialogHeader(RectTransform panel, string title, string subtitle, bool compactSummary = false, float dividerY = 109)
    {
        panel.GetComponent<Image>().color = DialogSurface;
        var accent = Panel("Accent", panel, Blue); Place(accent, 0, 0, panel.rect.width, 3);
        var heading = Text(panel, title, 24); heading.richText = false;
        heading.enableAutoSizing = true; heading.fontSizeMin = 16; heading.fontSizeMax = 24;
        Place(heading.rectTransform, 24, 17, panel.rect.width - 48, 38);
        var summary = Text(panel, subtitle, 15); summary.color = Muted; summary.richText = false;
        summary.textWrappingMode = TextWrappingModes.Normal;
        Place(summary.rectTransform, 24, 59, panel.rect.width - 48, compactSummary ? 18 : 42);
        var divider = Panel("HeaderDivider", panel, ModConfigLook.Surface(new Color(1, 1, 1, .1f)));
        Place(divider, 24, dividerY, panel.rect.width - 48, 1);
        var footer = Panel("Footer", panel, DialogInset);
        footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0); footer.pivot = Vector2.zero;
        footer.sizeDelta = new Vector2(0, 68); footer.anchoredPosition = Vector2.zero;
    }

    internal static Button SaveDialogAction(RectTransform panel, string label, Action action, float right, float width, UiActionRole role)
    {
        // Preserve the right-aligned action hierarchy on narrow windows as well.
        float scale = Mathf.Min(1, (panel.rect.width - 40) / 496);
        right = 20 + Mathf.Max(0, right - 20) * scale; width *= scale;
        var button = Button(panel, label, action, width, 36, role);
        Bottom((RectTransform)button.transform, panel.rect.width - right - width, 16);
        var caption = button.GetComponentInChildren<TMP_Text>(true);
        caption.enableAutoSizing = true; caption.fontSizeMin = 12; caption.fontSizeMax = 18;
        return button;
    }

    internal static void SaveDialogBody(RectTransform panel, string body, bool attention = false, bool formatted = false, bool horizontalReport = false, float top = 0)
    {
        var host = Panel("Message", panel, DialogInset); Fill(host);
        host.offsetMin = new Vector2(24, 82); host.offsetMax = new Vector2(-24, top > 0 ? -top : -124);
        if (attention)
        {
            var rail = Panel("Attention", host, Gold); rail.anchorMin = Vector2.zero; rail.anchorMax = new Vector2(0, 1);
            rail.pivot = Vector2.zero; rail.sizeDelta = new Vector2(3, 0); rail.anchoredPosition = Vector2.zero;
        }
        var viewport = Rect("Viewport", host); Fill(viewport, 14); viewport.gameObject.AddComponent<RectMask2D>();
        var text = Text(viewport, body, 18); text.richText = formatted;
        text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Overflow;
        var content = text.rectTransform; content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0, 1); content.sizeDelta = Vector2.zero;
        text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        ScrollRect scroll = horizontalReport ? host.gameObject.AddComponent<IssueReportScrollRect>() : host.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport; scroll.content = content;
        scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        ManagementUi.Scrollbar(host, scroll);
        var reader = host.gameObject.AddComponent<ControllerReadingSurface>(); reader.Scroll = scroll;
        reader.targetGraphic = host.GetComponent<Image>(); reader.transition = Selectable.Transition.None;
        GamepadSupport.MarkSelectable(host.gameObject, null, null);
        text.lineSpacing = 8;
        text.margin = new Vector4(0, 2, 12, 4);
        if (horizontalReport)
        {
            viewport.offsetMax = new Vector2(-14, -34);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = true;
            content.anchorMax = content.anchorMin = new Vector2(0, 1);
            // Measure the complete rich-text lines, with no arbitrary width cutoff.
            content.sizeDelta = new Vector2(Mathf.Max(panel.rect.width - 76, text.GetPreferredValues(body, Mathf.Infinity, Mathf.Infinity).x + 28), 0);
            scroll.horizontal = true; scroll.inertia = false;
            var track = Panel("HorizontalScrollbar", host, ModConfigLook.Surface(new Color(.07f, .07f, .06f, 1)));
            Place(track, 14, 8, panel.rect.width - 86, 12);
            var handle = Panel("Handle", track, Gold); Fill(handle);
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.targetGraphic = handle.GetComponent<Image>(); bar.handleRect = handle;
            bar.direction = Scrollbar.Direction.LeftToRight;
            scroll.horizontalScrollbar = bar;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.horizontalNormalizedPosition = 0;
        }
    }

    internal static void ShowPresetSaveChoice(string presetName, Action current, Action copy)
    {
        var panel = CreateModalPanel("MCE_PresetSaveChoice", 740, 370); if (panel == null) return;
        SaveDialogHeader(panel, Loc.T("Save preset", "保存预设", "儲存預設"), presetName);
        SaveDialogBody(panel, Loc.T("Update this preset on this PC, or keep it and create a named configuration.\n\nThe imported original is retained for restoration. Other unsaved changes are not committed by a local save.",
            "在本机更新当前预设，或另存一份有独立名称的配置。\n\n导入原版始终保留，可恢复到草稿。局部保存只提交对应范围。",
            "在本機更新目前預設，或另存一份有獨立名稱的設定。\n\n匯入原版保留，可還原至草稿。局部儲存只提交對應範圍。"));
        SaveDialogAction(panel, Loc.T("Save here", "保存当前预设", "儲存目前預設"), () => { CloseModal(); current(); }, 20, 154, UiActionRole.Commit);
        SaveDialogAction(panel, Loc.T("Save as new", "另存为新配置", "另存新設定"), () => { CloseModal(); copy(); }, 184, 154, UiActionRole.Edit);
        var cancel = SaveDialogAction(panel, Loc.T("Cancel", "取消", "取消"), CloseModal, 348, 92, UiActionRole.Neutral);
        EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
    }
}
