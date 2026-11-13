#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed class ManagementModSelector
{
    private readonly OverviewRepository _repo;
    private readonly IReadOnlyList<OverviewMod> _mods;
    private readonly Action<OverviewMod> _select;
    private readonly RectTransform _content;
    private readonly float _width;
    private readonly HashSet<string> _collapsed = new(StringComparer.OrdinalIgnoreCase);
    private string _selected, _query = "";
    private int _sort;
    internal ManagementModSelector(RectTransform host, OverviewRepository repo, IReadOnlyList<OverviewMod> mods, string selected, Action<OverviewMod> select)
    {
        _repo = repo; _mods = mods; _selected = selected; _select = select; _width = host.rect.width - 20;
        var input = OverviewUi.Input(host, "");
        var placeholder = OverviewUi.Text(input.textViewport, Loc.T("Search name or author", "搜索名称、作者", "搜尋名稱、作者"), 16); OverviewUi.Fill(placeholder.rectTransform); placeholder.color = OverviewUi.Muted; input.placeholder = placeholder;
        OverviewUi.Place((RectTransform)input.transform, 0, 0, host.rect.width, 36);
        var sort = OverviewUi.Button(host, Loc.T("Order: overview", "排序：沿用概览", "排序：沿用概覽"), () => { }, 150, 30, UiActionRole.Neutral);
        OverviewUi.Place((RectTransform)sort.transform, 0, 44, host.rect.width, 30);
        sort.onClick.AddListener(() => { _sort = (_sort + 1) % 3; sort.GetComponentInChildren<TMP_Text>(true).text = _sort == 0 ? Loc.T("Order: overview", "排序：沿用概览", "排序：沿用概覽") : _sort == 1 ? Loc.T("Order: name", "排序：名称", "排序：名稱") : Loc.T("Order: author", "排序：作者", "排序：作者"); Draw(); });
        var area = OverviewUi.Rect("ModRows", host); OverviewUi.Place(area, 0, 84, host.rect.width, host.rect.height - 84);
        var scroll = area.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 40; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = OverviewUi.Rect("Viewport", area); OverviewUi.Fill(viewport); viewport.offsetMax = new Vector2(-16, 0); viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        _content = OverviewUi.Rect("Content", viewport); _content.anchorMin = new Vector2(0, 1); _content.anchorMax = Vector2.one; _content.pivot = new Vector2(0, 1);
        scroll.viewport = viewport; scroll.content = _content; ManagementUi.Scrollbar(area, scroll);
        input.onValueChanged.AddListener(value => { _query = value.Trim(); Draw(); }); Draw();
    }
    private void Draw()
    {
        PublicationNavigation.Preserve(_content);
        foreach (Transform child in _content) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
        float y = 0;
        var filtered = _mods.Where(m => _query.Length == 0 || (m.Name + " " + m.RawName + " " + m.Author).IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        var tree = new GroupTree(_repo.State.groups);
        foreach (var group in filtered.GroupBy(m => tree.Contains(m.Rule.groupId) ? m.Rule.groupId : "").OrderBy(g => tree.Position(g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            string id = group.Key; var definition = tree.Get(id);
            if (_query.Length == 0 && id.Length > 0 && tree.HeaderHidden(id, _collapsed)) continue;
            string name = definition == null ? Loc.T("Ungrouped", "未分组", "未分組") : tree.Path(id);
            Color tint = ColorUtility.TryParseHtmlString(definition?.color ?? "", out var parsed) ? parsed : OverviewUi.Muted;
            bool folded = _collapsed.Contains(id) && _query.Length == 0;
            y += y > 0 ? 14 : 4;
            var header = OverviewUi.Button(_content, "", () => { if (!_collapsed.Add(id)) _collapsed.Remove(id); Draw(); }, _width, 44, UiActionRole.Neutral);
            OverviewUi.Place((RectTransform)header.transform, 0, y, _width, 44); UiActionStyle.Tint(header, tint);
            Rail(header.transform, tint, 12, 7, 30);
            var arrow = Label(header.transform, folded ? "+" : "−", 16); OverviewUi.PlaceClipped(arrow, 25, 0, 18, 44);
            var groupTitle = Label(header.transform, name + " · " + group.Count(), 17); groupTitle.color = tint; OverviewUi.PlaceClipped(groupTitle, 45, 0, _width - 74, 44); y += 52;
            if (folded) continue;
            IEnumerable<OverviewMod> rows = _sort == 0 ? group.OrderBy(m => m.Rule.order).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                : _sort == 1 ? group.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase) : group.OrderBy(m => m.Author, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var mod in rows)
            {
                float rowWidth = _width - 8, inset = Mathf.Clamp(rowWidth * .09f, 24, 34);
                var row = OverviewUi.Button(_content, "", () => { _selected = mod.Id; _select(mod); Draw(); }, rowWidth, 32, mod.Id == _selected ? UiActionRole.Edit : UiActionRole.Neutral);
                row.name = "Mod_" + mod.Id; OverviewUi.Place((RectTransform)row.transform, 8, y, rowWidth, 32); Rail(row.transform, tint, 12, 5, 22);
                var version = Label(row.transform, mod.PluginVersion.Length > 0 ? mod.PluginVersion : mod.PackageVersion, 15);
                float versionWidth = Mathf.Min(rowWidth - inset * 2 - 36, Mathf.Ceil(version.GetPreferredValues(version.text).x) + 10);
                version.alignment = TextAlignmentOptions.MidlineRight; version.enableAutoSizing = true; version.fontSizeMin = 1; version.fontSizeMax = 15;
                OverviewUi.PlaceClipped(version, rowWidth - inset - versionWidth, 0, versionWidth, 32); version.overflowMode = TextOverflowModes.Overflow;
                var title = Label(row.transform, mod.Name, 15); OverviewUi.PlaceClipped(title, inset, 0, Mathf.Max(24, rowWidth - inset * 2 - versionWidth - 12), 32); y += 34;
            }
        }
        if (filtered.Count == 0) { var empty = OverviewUi.Text(_content, Loc.T("No matching mods", "没有匹配的模组", "沒有符合的模組"), 16); OverviewUi.PlaceClipped(empty, 0, 0, _width, 32); y = 40; }
        _content.sizeDelta = new Vector2(0, y);
    }
    private static TextMeshProUGUI Label(Transform parent, string text, float size)
    { var label = OverviewUi.Text(parent, text, size); label.alignment = TextAlignmentOptions.MidlineLeft; label.margin = Vector4.zero; label.textWrappingMode = TextWrappingModes.NoWrap; return label; }
    private static void Rail(Transform parent, Color tint, float x, float y, float height)
    { var rail = OverviewUi.Panel("GroupColorRail", parent, tint); OverviewUi.Place(rail, x, y, 4, height); rail.GetComponent<Image>().raycastTarget = false; }
}
