#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal static partial class OverviewUi
{
    internal static void ShowProfileReview(string title, ProfileChangeReview review, Action? confirmed = null, string? message = null, string? confirmLabel = null)
    {
        var panel = CreateModalPanel("MCE_ProfileReview", 1080, 720); if (panel == null) return;
        var owner = _modal; float width = panel.rect.width;
        SaveDialogHeader(panel, title, review.ProfileName + "  ·  " + Loc.F("{0} changes", "{0} 项变更", "{0} 項變更", review.Items.Count));
        var status = Text(panel, message ?? Loc.T("Review first. Only saving commits the draft; effect timing is shown per item.", "核对后保存才会提交草稿，生效时机逐项列出。", "核對後儲存才會提交草稿，生效時機逐項列出。"), 15);
        status.color = Muted; status.textWrappingMode = TextWrappingModes.Normal;
        Place(status.rectTransform, 24, 118, width - 48, 28);
        var filters = Rect("Filters", panel); Place(filters, 24, 153, width - 48, 32);
        var search = Input(panel, ""); search.characterLimit = 256;
        Place((RectTransform)search.transform, 24, 194, width - 48, 32);
        var placeholder = Text(search.transform, Loc.T("Search mod, setting or reason", "搜索模组、配置项或原因", "搜尋模組、設定項或原因"), 16);
        Fill(placeholder.rectTransform, 6); placeholder.color = Muted; search.placeholder = placeholder;
        var column = Panel("Columns", panel, ModConfigLook.Surface(new Color(.19f, .19f, .18f, 1))); Place(column, 24, 235, width - 48, 32);
        var body = Rect("Changes", panel); Place(body, 24, 271, width - 48, Mathf.Max(80, panel.rect.height - 349));
        var rows = SourceUi.Rows(body, 0, body.sizeDelta.y, out var scroll);
        rows.parent.GetComponent<RectTransform>().offsetMax += new Vector2(-12, 0); ManagementUi.Scrollbar(body, scroll);
        var collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tabs = new List<(ProfileReviewCategory? Category, Button Button)>();
        ProfileReviewCategory? selected = null; string query = "";
        float available = Mathf.Max(100, body.rect.width - 48);
        float nameWidth = available * .42f, valueWidth = available * .21f;
        TextMeshProUGUI Label(Transform parent, string value, float x, float y, float w, float h, Color color, float size = 16)
        {
            var text = Text(parent, value, size); text.color = color; text.richText = false;
            text.overflowMode = TextOverflowModes.Ellipsis; Place(text.rectTransform, x, y, w, h); return text;
        }
        Label(column, Loc.T("Mod / setting", "模组／配置项", "模組／設定項"), 10, 0, nameWidth, 32, Ivory);
        Label(column, Loc.T("Before", "原值", "原值"), 10 + nameWidth, 0, valueWidth, 32, Muted);
        Label(column, Loc.T("After", "新值", "新值"), 10 + nameWidth + valueWidth, 0, valueWidth, 32, Green);
        Label(column, Loc.T("Effect", "生效方式", "生效方式"), 10 + nameWidth + 2 * valueWidth, 0, available - nameWidth - 2 * valueWidth, 32, Muted);
        string Category(ProfileReviewCategory category) => category switch {
            ProfileReviewCategory.Conflicts => Loc.T("Advice", "建议", "建議"),
            ProfileReviewCategory.Enablement => Loc.T("Mods", "模组启停", "模組啟停"),
            ProfileReviewCategory.Parameters => Loc.T("Settings", "参数", "參數"),
            ProfileReviewCategory.Hotkeys => Loc.T("Hotkeys", "快捷键", "快捷鍵"),
            ProfileReviewCategory.Groups => Loc.T("Groups", "分组", "分組"),
            ProfileReviewCategory.Information => Loc.T("Information", "资料", "資料"),
            ProfileReviewCategory.Introduction => Loc.T("Resources", "介绍资源", "介紹資源"),
            _ => Loc.T("Attention", "需要处理", "需要處理")
        };
        IEnumerable<ProfileReviewItem> Active() => review.Items.Where(i => i.Conflict == null || !i.Conflict.Ignored);
        void Draw()
        {
            foreach (Transform child in rows) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            foreach (var tab in tabs) UiActionStyle.Select(tab.Button, selected == tab.Category);
            var visible = Active().Where(i => (selected == null || i.Category == selected) && (query.Length == 0 ||
                (i.ModName + "\n" + i.Title + "\n" + i.Note + "\n" + i.Before + "\n" + i.Target).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            float y = 0;
            foreach (var mod in visible.GroupBy(i => i.ModId, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.First().GroupOrder).ThenBy(g => g.First().ModOrder))
            {
                var first = mod.First(); string key = mod.Key;
                string name = first.ModName.Length > 0 ? first.ModName : first.GroupName;
                var group = Button(rows, (collapsed.Contains(key) ? "+ " : "− ") + name + " · " + mod.Count(), () => {
                    if (!collapsed.Add(key)) collapsed.Remove(key); Draw();
                }, available, 34, UiActionRole.Neutral);
                Place((RectTransform)group.transform, 0, y, available, 34);
                var caption = group.GetComponentInChildren<TMP_Text>(true); caption.richText = false; caption.alignment = TextAlignmentOptions.MidlineLeft; caption.margin = new Vector4(12, 0, 12, 0);
                y += 39; if (collapsed.Contains(key)) continue;
                foreach (var item in mod)
                {
                    if (item.Conflict is { } conflict)
                    {
                        y += PackConflictUi.Card(rows, conflict, 8, y, available - 8, review.IgnoreConflict == null ? null : () => {
                            try { review.IgnoreConflict(conflict); Draw(); } catch (Exception error) { status.text = error.Message; }
                        }, false); continue;
                    }
                    bool attention = item.Category == ProfileReviewCategory.Unavailable;
                    float rowHeight = item.Note.Length > 0 ? 76 : 40;
                    var row = Panel("Change", rows, ModConfigLook.Surface(new Color(.075f, .08f, .085f, .98f))); Place(row, 0, y, available, rowHeight);
                    Label(row, item.Title, 10, 2, nameWidth - 16, 34, Ivory);
                    Label(row, item.Before.Length == 0 ? "—" : item.Before, nameWidth + 10, 2, valueWidth - 12, 34, Muted);
                    Label(row, item.Target.Length == 0 ? "—" : item.Target, nameWidth + valueWidth + 10, 2, valueWidth - 12, 34, Green);
                    string effect = OverviewRuntimeSettingsBridge.TimingLabel(item.EffectTiming);
                    Label(row, effect, nameWidth + 2 * valueWidth + 10, 2, available - nameWidth - 2 * valueWidth - 20, 34, attention ? Gold : Blue, 15);
                    if (item.Note.Length > 0) Label(row, item.Note, 10, 39, available - 20, 30, attention ? Gold : Muted, 15);
                    // Full values, path and notes remain available without expanding every row.
                    var details = Button(row, "", () => ShowText(item.Title, item.Before + "\n→ " + item.Target + "\n\n" + item.Note), available, rowHeight, UiActionRole.Neutral);
                    Place((RectTransform)details.transform, 0, 0, available, rowHeight);
                    details.GetComponent<Image>().color = Color.clear;
                    details.GetComponentInChildren<TMP_Text>(true).gameObject.SetActive(false);
                    y += rowHeight + 4;
                }
                y += 8;
            }
            if (visible.Count == 0) Label(rows, Loc.T("No changes to show", "没有需要列出的变更", "沒有需要列出的變更"), 10, 12, available - 20, 34, Muted);
            rows.sizeDelta = new Vector2(0, Mathf.Max(48, y));
        }
        float tabX = 0; const float tabGap = 4;
        int tabCount = review.Items.Select(i => i.Category).Distinct().Count() + 1;
        float tabLimit = Mathf.Max(34, (width - 44 - (tabCount - 1) * tabGap) / tabCount);
        void Tab(ProfileReviewCategory? category, string text)
        {
            float w = Mathf.Min(tabLimit, category == null ? 80 : Mathf.Clamp(38 + text.Length * 14, 76, 128));
            var button = Button(filters, text, () => { selected = category; Draw(); scroll.verticalNormalizedPosition = 1; }, w, 32, UiActionRole.Neutral);
            Place((RectTransform)button.transform, tabX, 0, w, 32); tabX += w + tabGap;
            var caption = button.GetComponentInChildren<TMP_Text>(true); caption.enableAutoSizing = true; caption.fontSizeMin = 10; caption.fontSizeMax = 17; caption.overflowMode = TextOverflowModes.Ellipsis;
            tabs.Add((category, button));
        }
        Tab(null, Loc.T("All", "全部", "全部"));
        foreach (var category in review.Items.Select(i => i.Category).Distinct().OrderBy(c => c)) Tab(category, Category(category));
        search.onValueChanged.AddListener(value => { query = value; Draw(); scroll.verticalNormalizedPosition = 1; });
        var close = SaveDialogAction(panel, confirmed == null ? Loc.T("Close", "关闭", "關閉") : Loc.T("Back to editing", "返回编辑", "返回編輯"), CloseModal, confirmed == null ? 20 : 204, 130, UiActionRole.Neutral);
        if (confirmed != null)
        {
            SaveDialogAction(panel, confirmLabel ?? Loc.T("Save configuration", "保存配置", "儲存設定"), () => { confirmed(); if (_modal == owner) CloseModal(); }, 20, 174, UiActionRole.Commit);
        }
        Draw(); scroll.verticalNormalizedPosition = 1; EventSystem.current?.SetSelectedGameObject(close.gameObject);
    }
}
