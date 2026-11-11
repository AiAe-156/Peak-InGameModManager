#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>个人标签条件，不随整合包发布；包含取交集，排除任一命中即隐藏。</summary>
internal sealed class OverviewTagSelection
{
    public List<string> include = new(), exclude = new();
    internal int Count => include.Count + exclude.Count;
    internal bool Matches(OverviewMod mod, Func<OverviewMod, string, bool>? status = null) => include.All(tag => (status ?? Has)(mod, tag)) && !exclude.Any(tag => (status ?? Has)(mod, tag));
    internal static bool Has(OverviewMod mod, string tag) => tag == Unknown ? !mod.CategoriesKnown :
        tag == None ? mod.CategoriesKnown && mod.Categories.Count == 0 :
        mod.CategoriesKnown && mod.Categories.Contains(tag, StringComparer.OrdinalIgnoreCase);
    internal const string Unknown = "$unknown", None = "$none";
}

internal static class OverviewTagFilter
{
    private static readonly string[] StandardTags = {
        "AI Generated", "Host Only", "Client Side", "All Clients", "Quality Of Life", "Optimization",
        "Mods", "Modpacks", "Libraries", "Tools", "Translations", "World", "Biomes", "Gameplay",
        "Items", "Audio", "Cosmetics", "Faces", "Afflictions", "Accolades", "Asset Replacements", "Mixtapes", "Misc"
    };
    private static string T(string en, string zh) => Loc.T(en, zh, zh);
    internal static string Caption(OverviewTagSelection selection) => T("Tags", "标签筛选") +
        (selection.Count > 0 ? " (" + selection.Count + ")" : " " + OverviewUi.Caret);

    internal static void Show(OverviewTagSelection selection, IReadOnlyList<OverviewMod> mods,
        Action changed, Action<Action<string>> synchronize, Func<OverviewMod, string, bool>? statusMatch = null, bool showOfficial = false, Func<string, int>? componentCount = null)
    {
        var panel = OverviewUi.CreateModalPanel("MCE_TagFilter", 760, 670); if (panel == null) return;
        float width = panel.rect.width;
        var heading = OverviewUi.Text(panel, T("Mod filters", "模组筛选"), 27); OverviewUi.PlaceClipped(heading, 24, 16, width - 48, 40);
        var help = OverviewUi.Text(panel, T("Include: match every checked tag. Exclude: hide any matching tag.", "状态筛选优先；包含取交集，排除命中即隐藏。"), 16);
        OverviewUi.PlaceClipped(help, 24, 60, width - 224, 28);
        var header = OverviewUi.Rect("Columns", panel); OverviewUi.Place(header, 24, 100, width - 64, 30);
        var tagTitle = OverviewUi.Text(header, T("Tag", "标签"), 17);
        var countTitle = OverviewUi.Text(header, T("Count", "数量"), 17);
        var includeTitle = OverviewUi.Text(header, T("Include", "包含"), 17);
        var excludeTitle = OverviewUi.Text(header, T("Exclude", "排除"), 17);
        var host = OverviewUi.Panel("Tags", panel, ModConfigLook.Surface(new Color(.09f, .09f, .08f, .85f))); OverviewUi.Fill(host);
        host.offsetMin = new Vector2(24, 108); host.offsetMax = new Vector2(-24, -134);
        var scroll = host.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 40; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = OverviewUi.Rect("Viewport", host); OverviewUi.Fill(viewport); viewport.offsetMax = new Vector2(-16, 0);
        viewport.gameObject.AddComponent<Image>().color = Color.clear; viewport.gameObject.AddComponent<RectMask2D>();
        var content = OverviewUi.Rect("Rows", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0, 1);
        var dynamicTags = new[] { "$update", "$duplicate", "$enabled", "$disabled", "$issue", "$changed" };
        var tags = dynamicTags.Concat(showOfficial ? StandardTags.Concat(mods.SelectMany(m => m.Categories)) : Array.Empty<string>()).Concat(selection.include).Concat(selection.exclude)
            .Where(tag => tag != OverviewTagSelection.Unknown && tag != OverviewTagSelection.None && !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        tags.Add(OverviewTagSelection.Unknown); tags.Add(OverviewTagSelection.None);
        content.sizeDelta = new Vector2(0, tags.Count * 32); scroll.viewport = viewport; scroll.content = content;
        var bar = OverviewUi.Rect("Scrollbar", host); bar.anchorMin = new Vector2(1, 0); bar.anchorMax = Vector2.one; bar.offsetMin = new Vector2(-12, 0); bar.offsetMax = Vector2.zero;
        bar.gameObject.AddComponent<Image>().color = ModConfigLook.Surface(new Color(.1f, .1f, .1f, .5f));
        var handle = OverviewUi.Rect("Handle", bar); OverviewUi.Fill(handle); var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = OverviewUi.Muted;
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle; scrollbar.targetGraphic = handleImage; scrollbar.direction = Scrollbar.Direction.BottomToTop; scroll.verticalScrollbar = scrollbar;
        var status = OverviewUi.Text(panel, T("Cached tags work offline. Unfetched or unconfirmed sources appear under Unknown.", "缓存可离线使用；未读取或来源未确认的模组归入“标签未知”。"), 15);
        status.color = OverviewUi.Muted; status.textWrappingMode = TextWrappingModes.Normal;
        status.rectTransform.anchorMin = new Vector2(0, 0); status.rectTransform.anchorMax = new Vector2(1, 0); status.rectTransform.pivot = new Vector2(0, 0);
        status.rectTransform.anchoredPosition = new Vector2(24, 58); status.rectTransform.sizeDelta = new Vector2(-48, 40);
        string statusText = status.text;
        var checks = new List<(string Tag, Toggle Include, Toggle Exclude)>();
        var rows = new List<(string Tag, TextMeshProUGUI Name, TextMeshProUGUI Original, TextMeshProUGUI Count, Toggle Include, Toggle Exclude)>();
        for (int i = 0; i < tags.Count; i++)
        {
            string tag = tags[i], display = Display(tag);
            var row = OverviewUi.Rect("Tag_" + i, content); row.anchorMax = new Vector2(1, 1); row.sizeDelta = new Vector2(0, 32); row.anchoredPosition = new Vector2(0, -i * 32);
            row.gameObject.AddComponent<Image>().color = ModConfigLook.Surface(new Color(.18f, .18f, .16f, i % 2 == 0 ? .40f : .12f));
            var label = OverviewUi.Text(row, display, 17); label.alignment = TextAlignmentOptions.MidlineLeft; label.overflowMode = TextOverflowModes.Ellipsis;
            var original = OverviewUi.Text(row, !tag.StartsWith("$") && display != tag && tag != OverviewTagSelection.Unknown && tag != OverviewTagSelection.None ? tag : "", 14);
            var originalBinding = original.GetComponent<InlineLanguageLabel>(); if (originalBinding != null) UnityEngine.Object.DestroyImmediate(originalBinding);
            original.text = !tag.StartsWith("$") && display != tag && tag != OverviewTagSelection.Unknown && tag != OverviewTagSelection.None ? tag : "";
            original.alignment = TextAlignmentOptions.MidlineLeft; original.color = OverviewUi.Muted; original.overflowMode = TextOverflowModes.Ellipsis;
            var count = OverviewUi.Text(row, "", 17); count.alignment = TextAlignmentOptions.MidlineRight;
            var tip = row.gameObject.AddComponent<OverviewHover>(); tip.Enter = () => status.text = display + (original.text.Length > 0 ? " · " + tag : ""); tip.Exit = () => status.text = statusText;
            Toggle yes = null!, no = null!;
            void Apply(bool value, bool included)
            {
                var target = included ? selection.include : selection.exclude; var opposite = included ? selection.exclude : selection.include;
                target.RemoveAll(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
                if (value) { target.Add(tag); opposite.RemoveAll(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)); OverviewUi.SyncCheck(included ? no : yes, false); }
                changed(); heading.text = T("Mod filters", "模组筛选") + " · " + selection.Count;
            }
            yes = OverviewUi.Check(row, selection.include.Contains(tag, StringComparer.OrdinalIgnoreCase), value => Apply(value, true));
            no = OverviewUi.Check(row, selection.exclude.Contains(tag, StringComparer.OrdinalIgnoreCase), value => Apply(value, false));
            GamepadSupport.MarkSelectable(yes.gameObject, scroll, row); GamepadSupport.MarkSelectable(no.gameObject, scroll, row);
            checks.Add((tag, yes, no)); rows.Add((tag, label, original, count, yes, no));
        }
        void Align(TextMeshProUGUI text, float x, float w) => OverviewUi.PlaceClipped(text, x, 0, Mathf.Max(0, w), 32);
        void Reflow(float rowWidth)
        {
            var columns = new TagFilterColumns(rowWidth);
            Align(tagTitle, 10, columns.NameWidth); countTitle.alignment = TextAlignmentOptions.MidlineRight; Align(countTitle, columns.CountX, 48);
            includeTitle.alignment = excludeTitle.alignment = TextAlignmentOptions.Midline; Align(includeTitle, columns.IncludeX, 56); Align(excludeTitle, columns.ExcludeX, 56);
            foreach (var row in rows)
            {
                float primary = row.Original.text.Length == 0 ? columns.NameWidth : Mathf.Min(columns.NameWidth * .65f, row.Name.GetPreferredValues(row.Name.text).x + 4);
                Align(row.Name, 10, primary); Align(row.Original, 18 + primary, columns.NameWidth - primary - 8); Align(row.Count, columns.CountX, 48);
                OverviewUi.Place((RectTransform)row.Include.transform, columns.IncludeX + 17, 5, 22, 22); OverviewUi.Place((RectTransform)row.Exclude.transform, columns.ExcludeX + 17, 5, 22, 22);
            }
        }
        void RefreshCounts() { foreach (var row in rows) row.Count.text = (mods.Count(mod => (statusMatch ?? OverviewTagSelection.Has)(mod, row.Tag)) + (componentCount?.Invoke(row.Tag) ?? 0)).ToString(); }
        RefreshCounts(); var table = viewport.gameObject.AddComponent<TagFilterTable>(); table.Reflow = Reflow; table.Refresh();
        float actionWidth = width - 48, closeWidth = Mathf.Min(110, actionWidth * .25f), readWidth = Mathf.Min(170, actionWidth * .4f), clearWidth = Mathf.Min(120, actionWidth * .28f);
        Button sync = null!;
        sync = OverviewUi.Button(panel, T("Read store tags", "读取商店标签"), () => {
            sync.interactable = false; status.text = statusText = T("Reading store sources…", "正在读取商店来源……");
            synchronize(message => { if (panel == null) return; sync.interactable = true; status.text = statusText = message; RefreshCounts(); });
        }, readWidth, 34, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)sync.transform, 24, 16);
        var official = OverviewUi.Button(panel, showOfficial ? T("Hide store tags", "收起官方标签") : T("Store tags…", "官方标签…"), () => Show(selection, mods, changed, synchronize, statusMatch, !showOfficial, componentCount), 150, 28, UiActionRole.Neutral);
        OverviewUi.Place((RectTransform)official.transform, width - 180, 63, 150, 28);
        var clear = OverviewUi.Button(panel, T("Clear filters", "清空筛选"), () => {
            selection.include.Clear(); selection.exclude.Clear(); foreach (var pair in checks) { OverviewUi.SyncCheck(pair.Include, false); OverviewUi.SyncCheck(pair.Exclude, false); }
            changed(); heading.text = T("Mod filters", "模组筛选");
        }, clearWidth, 34, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)clear.transform, 34 + readWidth, 16);
        var close = OverviewUi.Button(panel, T("Close", "关闭"), OverviewUi.CloseModal, closeWidth, 34, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)close.transform, width - 24 - closeWidth, 16);
        PublicationNavigation.Attach(panel); EventSystem.current?.SetSelectedGameObject(checks.Count > 0 ? checks[0].Include.gameObject : sync.gameObject);
    }

    private static string Display(string tag) => tag switch {
        "$update" => T("Updates available", "可更新"), "$duplicate" => T("Duplicate mods", "重复模组"),
        "$enabled" => T("Enabled", "已启用"), "$disabled" => T("Disabled", "未启用"),
        "$issue" => T("Issues", "异常模组"), "$changed" => T("Unsaved changes", "有变更"),
        OverviewTagSelection.Unknown => T("Unknown tags", "标签未知"), OverviewTagSelection.None => T("No tags", "未标注标签"),
        "AI Generated" => Loc.T(tag, "AI 生成", "AI 生成"), "Host Only" => Loc.T(tag, "仅主机", "僅主機"), "Client Side" => Loc.T(tag, "客户端", "用戶端"), "All Clients" => Loc.T(tag, "所有玩家", "所有玩家"),
        "Quality Of Life" => Loc.T(tag, "生活质量", "生活品質"), "Optimization" => Loc.T(tag, "性能优化", "效能最佳化"), "Mods" => Loc.T(tag, "模组", "模組"), "Modpacks" => Loc.T(tag, "整合包", "整合包"),
        "Libraries" => Loc.T(tag, "基础库", "基礎函式庫"), "Tools" => Loc.T(tag, "工具", "工具"), "Translations" => Loc.T(tag, "汉化／翻译", "中文化／翻譯"), "World" => Loc.T(tag, "世界", "世界"), "Biomes" => Loc.T(tag, "生物群系", "生物群系"),
        "Gameplay" => Loc.T(tag, "玩法", "玩法"), "Items" => Loc.T(tag, "物品", "物品"), "Audio" => Loc.T(tag, "音效", "音效"), "Cosmetics" => Loc.T(tag, "外观", "外觀"), "Faces" => Loc.T(tag, "面部", "臉部"),
        "Afflictions" => Loc.T(tag, "状态效果", "狀態效果"), "Accolades" => Loc.T(tag, "成就", "成就"), "Asset Replacements" => Loc.T(tag, "资源替换", "資源替換"), "Mixtapes" => Loc.T(tag, "混音", "混音"), "Misc" => Loc.T(tag, "其他", "其他"), _ => tag
    };
}
