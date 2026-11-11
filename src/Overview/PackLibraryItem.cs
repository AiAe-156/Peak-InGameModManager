#nullable enable
using System;
using ModConfigEnhance.Updating;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>A single selectable list record. Every label belongs to the same rectangular hit area.</summary>
internal static class PackLibraryItem
{
    internal static float Draw(RectTransform parent, PackRecord pack, float y, float width, bool selected, bool active, Action choose)
    {
        const float height = 112;
        // Do not use the game's ribbon prefab for a library record.
        var row = OverviewUi.Rect("LibraryItem_" + pack.Id, parent); OverviewUi.Place(row, 0, y, width, height);
        var image = row.gameObject.AddComponent<Image>(); image.color = selected ? ModConfigLook.Surface(new Color(.19f, .26f, .29f, 1)) : ModConfigLook.Surface(new Color(.12f, .13f, .14f, 1));
        var button = row.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1.13f, 1.13f, 1.13f); colors.pressedColor = ModConfigLook.Surface(new Color(.8f, .8f, .8f)); colors.selectedColor = Color.white; button.colors = colors;
        button.onClick.AddListener(() => choose());
        if (selected)
        {
            var rail = OverviewUi.Rect("SelectedRail", row); OverviewUi.Place(rail, 0, 0, 3, height); rail.gameObject.AddComponent<Image>().color = OverviewUi.Blue;
        }
        var name = OverviewUi.Text(row, pack.Name, 18); name.textWrappingMode = TextWrappingModes.Normal; name.overflowMode = TextOverflowModes.Ellipsis;
        OverviewUi.Place(name.rectTransform, 12, 8, width - 24, 48);
        if (active)
        {
            var badge = OverviewUi.Rect("ActiveBadge", row); OverviewUi.Place(badge, 12, 59, 84, 22); badge.gameObject.AddComponent<Image>().color = ModConfigLook.Surface(new Color(.19f, .34f, .20f, 1));
            var text = OverviewUi.Text(badge, Loc.T("Active", "当前启用", "目前啟用"), 13); text.color = OverviewUi.Green; text.alignment = TextAlignmentOptions.Midline; OverviewUi.Fill(text.rectTransform);
        }
        else
        {
            var state = OverviewUi.Text(row, pack.Problems.Count > 0 ? Loc.T("Needs attention", "存在问题", "存在問題") : Loc.T("Stored", "已保存", "已儲存"), 13);
            state.color = pack.Problems.Count > 0 ? PackPreviewView.Warn : OverviewUi.Muted; OverviewUi.Place(state.rectTransform, 12, 61, width - 24, 20);
        }
        string kind = pack.PublicationId.Length == 0 ? Loc.T("Snapshot", "临时快照", "臨時快照") : Loc.T("Imported", "已导入", "已匯入");
        if (pack.Version.Length > 0) kind += " · v" + pack.Version;
        var meta = OverviewUi.Text(row, kind, 12); meta.color = OverviewUi.Muted; OverviewUi.PlaceClipped(meta, 12, 87, width - 108, 20);
        var files = OverviewUi.Text(row, pack.Files.Count + Loc.T(" files", " 个文件", " 個檔案"), 12); files.color = OverviewUi.Muted; files.alignment = TextAlignmentOptions.TopRight;
        OverviewUi.Place(files.rectTransform, width - 108, 87, 96, 20);
        // Text and decoration never steal selection from their parent record.
        foreach (var g in row.GetComponentsInChildren<Graphic>(true)) if (g != image) g.raycastTarget = false;
        return y + height + 8;
    }
}
