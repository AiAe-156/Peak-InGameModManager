#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal enum SourceMenuSection { Pages, Source, Maintenance }

/// <summary>Three independent action cards; the caller owns source identity and permission decisions.</summary>
internal static class SourceMenuView
{
    internal static void Show(string name, IReadOnlyList<(SourceMenuSection Section, UiMenuEntry Entry)> entries)
    {
        if (OverviewUi.ModalParent is not RectTransform parent) return;
        var panel = OverviewUi.CreateModalPanel("MCE_SourceMenu", 960, 580); if (panel == null) return;
        var heading = OverviewUi.Text(panel, name.Replace('\r', ' ').Replace('\n', ' '), 23);
        heading.textWrappingMode = TextWrappingModes.NoWrap;
        var viewport = OverviewUi.Rect("CardsViewport", panel);
        viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40; scroll.inertia = false;
        var content = OverviewUi.Rect("Cards", viewport);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1);
        scroll.viewport = viewport; scroll.content = content;
        var close = OverviewUi.Button(panel, Loc.T("Close", "关闭", "關閉"), OverviewUi.CloseModal, 108, 32);
        var sections = new[] { SourceMenuSection.Pages, SourceMenuSection.Source, SourceMenuSection.Maintenance };
        Vector2 lastSize = Vector2.zero;
        int language = Loc.TranslationRevision;

        string Title(SourceMenuSection section) => section switch {
            SourceMenuSection.Pages => Loc.T("Pages", "页面", "頁面"),
            SourceMenuSection.Source => Loc.T("Source", "来源", "來源"),
            _ => Loc.T("Maintenance", "维护", "維護")
        };
        Color Tint(SourceMenuSection section) => section switch {
            SourceMenuSection.Pages => ModConfigLook.Surface(new Color(.13f, .23f, .29f, .98f)),
            SourceMenuSection.Source => ModConfigLook.Surface(new Color(.13f, .24f, .20f, .98f)),
            _ => ModConfigLook.Surface(new Color(.28f, .17f, .16f, .98f))
        };
        Color Accent(SourceMenuSection section) => section switch {
            SourceMenuSection.Pages => OverviewUi.Blue,
            SourceMenuSection.Source => OverviewUi.Green,
            _ => new Color(.85f, .56f, .40f)
        };
        float Card(SourceMenuSection section, float x, float y, float width)
        {
            var actions = entries.Where(e => e.Section == section && !e.Entry.Header).Select(e => e.Entry).ToArray();
            var card = OverviewUi.Panel("SourceCard_" + section, content, Tint(section));
            var rail = OverviewUi.Panel("Accent", card, Accent(section));
            var title = OverviewUi.Text(card, Title(section), 17); title.color = Accent(section);
            OverviewUi.PlaceClipped(title, 16, 6, Mathf.Max(1, width - 32), 30);
            float top = 42;
            int columns = width >= 520 ? 2 : 1;
            float buttonWidth = Mathf.Min(260, Mathf.Max(1, (width - 32 - (columns - 1) * 8) / columns));
            float left = (width - (buttonWidth * columns + (columns - 1) * 8)) / 2;
            for (int i = 0; i < actions.Length; i++)
            {
                var entry = actions[i];
                var button = OverviewUi.Button(card, entry.Label.Replace('\r', ' ').Replace('\n', ' '), () => {
                    if (!entry.Enabled || entry.CanInvoke?.Invoke() == false) return;
                    OverviewUi.CloseModal(); entry.Execute();
                }, buttonWidth, 32, entry.Role);
                button.gameObject.name = "SourceAction_" + section + "_" + i;
                button.interactable = entry.Enabled;
                OverviewUi.Place((RectTransform)button.transform, left + (i % columns) * (buttonWidth + 8),
                    top + (i / columns) * 38, buttonWidth, 32);
                var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label != null) {
                    label.alignment = TextAlignmentOptions.Midline; label.textWrappingMode = TextWrappingModes.NoWrap;
                    label.richText = false; label.enableAutoSizing = true; label.fontSizeMin = 12; label.fontSizeMax = 16;
                    label.fontSize = 16;
                    label.margin = new Vector4(16, 1, 16, 1); label.overflowMode = TextOverflowModes.Ellipsis;
                }
                GamepadSupport.MarkSelectable(button.gameObject, scroll, (RectTransform)button.transform);
            }
            if (actions.Length == 0) {
                var empty = OverviewUi.Text(card, Loc.T("None", "无", "無"), 15); empty.color = OverviewUi.Muted;
                OverviewUi.PlaceClipped(empty, 16, top, Mathf.Max(1, width - 32), 28); top += 32;
            }
            else top += ((actions.Length + columns - 1) / columns) * 38;
            string reason = string.Join("\n", actions.Where(e => !e.Enabled && e.DisabledReason.Length > 0)
                .Select(e => e.DisabledReason).Distinct(StringComparer.Ordinal));
            if (reason.Length > 0) {
                var message = OverviewUi.Text(card, reason, 14); message.color = OverviewUi.Muted;
                message.textWrappingMode = TextWrappingModes.Normal;
                float height = Mathf.Max(26, Mathf.Ceil(message.GetPreferredValues(reason, Mathf.Max(1, width - 32), 0).y) + 4);
                OverviewUi.Place(message.rectTransform, 16, top + 2, Mathf.Max(1, width - 32), height); top += height + 6;
            }
            float cardHeight = top + 10;
            OverviewUi.Place(card, x, y, width, cardHeight); OverviewUi.Place(rail, 0, 8, 3, cardHeight - 16);
            return cardHeight;
        }
        void Render()
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            string? focus = selected != null && selected.transform.IsChildOf(content) ? selected.name : null;
            float position = scroll.verticalNormalizedPosition;
            for (int i = content.childCount - 1; i >= 0; i--) {
                var child = content.GetChild(i).gameObject; child.SetActive(false); UnityEngine.Object.Destroy(child);
            }
            lastSize = parent.rect.size; language = Loc.TranslationRevision;
            float width = Mathf.Min(960, Mathf.Max(1, lastSize.x - 32)), bodyWidth = Mathf.Max(1, width - 36);
            bool horizontal = bodyWidth >= 744;
            int actionColumns = bodyWidth >= 520 ? 2 : 1;
            float estimate = horizontal
                ? 60 + sections.Max(section => entries.Count(e => e.Section == section && !e.Entry.Header)) * 38
                : sections.Sum(section => 60 + ((entries.Count(e => e.Section == section && !e.Entry.Header) + actionColumns - 1) / actionColumns) * 38) + 20;
            float height = Mathf.Min(Mathf.Max(1, lastSize.y - 32), Mathf.Clamp(estimate + 110, 300, 620));
            panel.sizeDelta = new Vector2(width, height);
            OverviewUi.PlaceClipped(heading, 18, 12, bodyWidth, 36);
            OverviewUi.Place(viewport, 18, 56, bodyWidth, Mathf.Max(1, height - 110));
            OverviewUi.Bottom((RectTransform)close.transform, Mathf.Max(12, width - 126), 14);
            float contentHeight = 0;
            if (horizontal) {
                float cardWidth = (bodyWidth - 20) / 3;
                for (int i = 0; i < sections.Length; i++) contentHeight = Mathf.Max(contentHeight, Card(sections[i], i * (cardWidth + 10), 0, cardWidth));
            }
            else {
                foreach (var section in sections) contentHeight += Card(section, 0, contentHeight, bodyWidth) + 10;
                contentHeight = Mathf.Max(0, contentHeight - 10);
            }
            content.sizeDelta = new Vector2(0, contentHeight);
            Canvas.ForceUpdateCanvases(); scroll.verticalNormalizedPosition = position;
            if (focus != null) {
                var replacement = content.GetComponentsInChildren<Selectable>(false).FirstOrDefault(s => s.name == focus && s.IsInteractable());
                if (replacement != null) EventSystem.current?.SetSelectedGameObject(replacement.gameObject);
            }
            PublicationNavigation.Attach(panel);
        }
        Render();
        panel.gameObject.AddComponent<SourceMenuResize>().Tick = () => {
            if (parent.rect.size != lastSize || language != Loc.TranslationRevision) Render();
        };
    }
}

internal sealed class SourceMenuResize : MonoBehaviour
{
    internal Action? Tick;
    private void Update() => Tick?.Invoke();
    private void OnDestroy() => Tick = null;
}
