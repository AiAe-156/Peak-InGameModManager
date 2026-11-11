from pathlib import Path
import sys
root = Path(sys.argv[1]) / 'src/Overview'
p = root / 'LayoutReviewView.cs'
s = p.read_text(encoding='utf-8')
tail_at = s.index('    private static (string, Color) Source(LayoutTreeNode node) => node.Source switch')
tail = s[tail_at:]
tail = tail.replace('''        "BundledOtherGroup" => Loc.T("Bundles a library from another group; it moves with this package", "内含其他分组的依赖库，随本包一起移动", "內含其他分組的相依程式庫，隨本套件一起移動"),''',
'''        "BundledOtherGroup" => Loc.T("Bundles a library from another group; it moves with this package", "内含其他分组的依赖库，随本包一起移动", "內含其他分組的相依程式庫，隨本套件一起移動"),
        "DestinationInsidePackage" => Loc.T("The target folder is inside another mod's folder", "目标位置在另一个模组的文件夹里", "目標位置在另一個模組的資料夾裡"),
        "GroupFromSameName" => Loc.T("No plugin was read from it; it follows the mod with the same name", "没有读到插件，跟随同名模组的分组", "沒有讀到外掛，跟隨同名模組的分組"),
        "GroupFromDependent" => Loc.T("Has no plugin of its own; it follows the mod that depends on it", "自身没有插件，跟随依赖它的模组的分组", "自身沒有外掛，跟隨相依它的模組的分組"),
        "NoGroupKnown" => Loc.T("No plugin and no mod depends on it, so it has no group; it stays where it is", "没有插件，也没有模组依赖它，无法确定分组，保持原位", "沒有外掛，也沒有模組相依它，無法確定分組，保持原位"),
        "NameSuffixed" => Loc.T("Another folder with the same name lands here; this one gets its version added", "同名文件夹会落到同一位置，这一份加上版本号", "同名資料夾會落到同一位置，這一份加上版本號"),
        "EmptyAfterMove" => Loc.T("Empty once its mods have moved out", "里面的模组移走后就空了", "裡面的模組移走後就空了"),''')
head = r'''#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using ModConfigEnhance.Updating;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using K = ModConfigEnhance.Overview.LayoutReviewModel.Kind;

namespace ModConfigEnhance.Overview;

/// <summary>The folder-organization review (2.3.0): a framed, foldable tree of destination folders. Nested group folders
/// are nested cards (indented, one gold rail per level); a renamed category folder shows "renamed from" in its header;
/// each mod folder is a framed row with a coloured tag, its unchanged name, where it comes from in small grey text and,
/// when needed, the reason in plain words. "Stays where it is" and "empty folders" (each with a Recycle Bin button) come last.</summary>
internal sealed class LayoutReviewView
{
    private enum Filter { All, Changes, Stays }

    private static readonly Color CardColor = new(.13f, .15f, .15f, .97f), RowColor = new(.09f, .10f, .10f, .92f), HostColor = new(.065f, .08f, .095f, 1);
    private static readonly Color MoveColor = new(.40f, .72f, .82f), RenameColor = new(.56f, .66f, .92f), StayColor = new(.50f, .77f, .49f);
    private static readonly Color UngroupedColor = new(.56f, .56f, .52f), SkipColor = new(.88f, .63f, .33f), ReasonInk = new(.93f, .72f, .45f), NoteInk = new(.63f, .75f, .83f);
    private const float CardIndent = 18;

    private readonly RectTransform _host, _content;
    private readonly ScrollRect _scroll;
    private readonly List<LayoutReviewModel.Section> _sections;
    private readonly LayoutReviewModel.Summary _summary;
    private readonly HashSet<string> _folded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deleted = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Button Button, Filter Filter)> _filters = new();
    private readonly Action<string>? _status;
    private Filter _filter = Filter.All;

    internal static void Show(RectTransform panel, IReadOnlyList<LayoutTreeNode> tree, IReadOnlyList<(string Text, bool Warning)> notice, Action<string>? status = null)
        => new LayoutReviewView(panel, tree, notice, status);

    private LayoutReviewView(RectTransform panel, IReadOnlyList<LayoutTreeNode> tree, IReadOnlyList<(string Text, bool Warning)> notice, Action<string>? status)
    {
        _status = status;
        _sections = LayoutReviewModel.Build(tree, out _summary);
        foreach (var section in _sections) if (!section.OpenByDefault) _folded.Add(section.Key);
        _host = OverviewUi.Panel("LayoutReview", panel, HostColor); OverviewUi.Fill(_host);
        _host.offsetMin = new Vector2(24, 82); _host.offsetMax = new Vector2(-24, -124);
        float width = panel.rect.width - 48, y = 10;

        foreach (var (text, warning) in notice)
        {
            var line = OverviewUi.Text(_host, text, 14); line.color = warning ? ReasonInk : OverviewUi.Muted;
            line.textWrappingMode = TextWrappingModes.Normal;
            float height = Mathf.Ceil(line.GetPreferredValues(text, width - 28, Mathf.Infinity).y) + 2;
            OverviewUi.Place(line.rectTransform, 14, y, width - 28, height); y += height + 2;
        }
        // Totals: folders vs mods, so the count matches the overview list.
        var totals = OverviewUi.Text(_host, Loc.F("{0} folders · {1} mods", "{0} 个文件夹 · 共 {1} 个模组", "{0} 個資料夾 · 共 {1} 個模組", _summary.Folders, _summary.Mods), 15);
        totals.color = OverviewUi.Ivory; OverviewUi.PlaceClipped(totals, 14, y + 2, width - 28, 20); y += 26;
        float x = 14;
        x = Chip(_host, x, y, MoveColor, Loc.T("Move in", "移入", "移入"), _summary.Move);
        x = Chip(_host, x, y, RenameColor, Loc.T("Moved out of a mod", "拆出", "拆出"), _summary.Extracted);
        x = Chip(_host, x, y, OverviewUi.Gold, Loc.T("Folder renamed", "文件夹改名", "資料夾改名"), _summary.Renamed);
        x = Chip(_host, x, y, StayColor, Loc.T("In place", "原位", "原位"), _summary.InPlace);
        x = Chip(_host, x, y, UngroupedColor, Loc.T("Ungrouped", "未分组", "未分組"), _summary.Ungrouped);
        x = Chip(_host, x, y, SkipColor, Loc.T("Not moved", "不动", "不動"), _summary.Stays);
        Chip(_host, x, y, OverviewUi.Muted, Loc.T("Empty folders", "空文件夹", "空資料夾"), _summary.Empty);
        y += 30;
        float fx = 14;
        foreach (var (label, filter) in new[] { (Loc.T("All", "全部", "全部"), Filter.All), (Loc.T("Changes only", "只看变动", "只看變動"), Filter.Changes), (Loc.T("Not moved and empty", "只看不动与空文件夹", "只看不動與空資料夾"), Filter.Stays) })
        {
            float w = filter == Filter.Stays ? 150 : 92;
            var button = OverviewUi.Button(_host, label, () => { _filter = filter; Rebuild(); }, w, 26, UiActionRole.Neutral);
            var caption = button.GetComponentInChildren<TMP_Text>(true); caption.enableAutoSizing = true; caption.fontSizeMin = 10; caption.fontSizeMax = 13;
            OverviewUi.Place((RectTransform)button.transform, fx, y, w, 26); fx += w + 6;
            _filters.Add((button, filter));
        }
        y += 34;
        var divider = OverviewUi.Panel("Divider", _host, new Color(1, 1, 1, .08f)); OverviewUi.Place(divider, 14, y, width - 28, 1); y += 8;

        _scroll = OverviewUi.Rect("Tree", _host).gameObject.AddComponent<ScrollRect>();
        var area = (RectTransform)_scroll.transform; OverviewUi.Fill(area); area.offsetMax = new Vector2(-6, -y); area.offsetMin = new Vector2(8, 8);
        _scroll.horizontal = false; _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 40;
        var viewport = OverviewUi.Rect("Viewport", area); OverviewUi.Fill(viewport); viewport.offsetMax = new Vector2(-14, 0);
        viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        _content = OverviewUi.Rect("Content", viewport); _content.anchorMin = new Vector2(0, 1); _content.anchorMax = Vector2.one; _content.pivot = new Vector2(0, 1); _content.sizeDelta = Vector2.zero;
        _scroll.viewport = viewport; _scroll.content = _content;
        ManagementUi.Scrollbar(area, _scroll);
        GamepadSupport.MarkSelectable(area.gameObject, _scroll, _content);
        Rebuild();
    }

    private static float Chip(RectTransform parent, float x, float y, Color color, string label, int count)
    {
        var chip = OverviewUi.Panel("Chip", parent, new Color(color.r, color.g, color.b, count > 0 ? .22f : .08f));
        var dot = OverviewUi.Panel("Dot", chip, count > 0 ? color : new Color(color.r, color.g, color.b, .35f)); OverviewUi.Place(dot, 8, 8, 8, 8);
        var text = OverviewUi.Text(chip, label + "  " + count, 13); text.color = count > 0 ? OverviewUi.Ivory : OverviewUi.Muted;
        float width = Mathf.Ceil(text.GetPreferredValues(text.text, Mathf.Infinity, Mathf.Infinity).x) + 30;
        OverviewUi.Place(chip, x, y, width, 24); OverviewUi.PlaceClipped(text, 22, 3, width - 26, 18);
        return x + width + 8;
    }

    private bool Visible(LayoutReviewModel.Entry entry) => !_deleted.Contains(entry.From) && _filter switch
    {
        Filter.Changes => entry.Kind is K.Move or K.Extracted or K.Ungrouped,
        Filter.Stays => entry.Kind is K.Stays or K.Skipped or K.Empty,
        _ => true
    };

    private void Rebuild()
    {
        for (int i = _content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
        foreach (var (button, filter) in _filters) UiActionStyle.Set(button, filter == _filter ? UiActionRole.Edit : UiActionRole.Neutral);
        float width = _content.rect.width > 10 ? _content.rect.width : ((RectTransform)_scroll.transform).rect.width - 14;
        float y = 0;
        var rootText = OverviewUi.Text(_content, "plugins/", 13); rootText.color = OverviewUi.Muted; OverviewUi.PlaceClipped(rootText, 4, y, width, 18); y += 22;
        int shown = 0;
        foreach (var section in _sections)
        {
            if (_filter == Filter.All && LayoutReviewModel.HiddenUnder(section, _folded)) continue;
            var entries = section.Entries.Where(Visible).ToList();
            bool renamed = section.RenamedFrom.Length > 0 && _filter != Filter.Stays;
            bool hasChildren = _filter == Filter.All && _sections.Any(c => c != section && !c.Stays && !c.EmptyFolders && section.Key.Length > 0 && c.Key.StartsWith(section.Key + "/", StringComparison.OrdinalIgnoreCase));
            if (entries.Count == 0 && !renamed && !hasChildren) continue;
            shown++;
            y = Card(section, entries, y, width) + 6;
        }
        if (shown == 0)
        {
            var empty = OverviewUi.Text(_content, _filter == Filter.Stays ? Loc.T("Everything moves; nothing is left out.", "所有目录都会整理，没有不动的。", "所有目錄都會整理，沒有不動的。") : Loc.T("Nothing changes.", "没有需要变动的目录。", "沒有需要變動的目錄。"), 15);
            empty.color = OverviewUi.Muted; OverviewUi.PlaceClipped(empty, 8, y + 6, width - 16, 20); y += 34;
        }
        _content.sizeDelta = new Vector2(0, y + 4);
    }

    private float Card(LayoutReviewModel.Section section, List<LayoutReviewModel.Entry> entries, float y, float width)
    {
        bool special = section.Stays || section.EmptyFolders;
        int depth = special ? 1 : section.Depth;
        float left = (depth - 1) * CardIndent, cardWidth = width - left;
        bool open = !_folded.Contains(section.Key) || _filter != Filter.All;
        Color accent = section.Stays ? SkipColor : section.EmptyFolders ? OverviewUi.Muted : section.Ungrouped ? UngroupedColor : OverviewUi.Gold;
        var card = OverviewUi.Panel("Section", _content, depth <= 1 ? CardColor : new Color(CardColor.r + .02f, CardColor.g + .02f, CardColor.b + .02f, CardColor.a));
        for (int level = 0; level < depth; level++)
        {
            var bar = OverviewUi.Panel("Accent", card, level == depth - 1 ? accent : new Color(accent.r, accent.g, accent.b, .45f));
            bar.anchorMin = Vector2.zero; bar.anchorMax = new Vector2(0, 1); bar.pivot = Vector2.zero; bar.sizeDelta = new Vector2(3, 0); bar.anchoredPosition = new Vector2(level == depth - 1 ? 0 : -(depth - 1 - level) * 5f, 0);
        }
        float top = y, inner = 8;
        var toggle = OverviewUi.IconButton(card, open ? "triangle-down" : "triangle-right", () => { if (!_folded.Add(section.Key)) _folded.Remove(section.Key); Rebuild(); }, 24, 24);
        OverviewUi.Place((RectTransform)toggle.transform, 10, inner, 24, 24);
        string title = section.Stays ? Loc.T("Not moved", "不动", "不動")
            : section.EmptyFolders ? Loc.T("Empty folders", "空文件夹", "空資料夾")
            : section.Key.Length == 0 ? Loc.T("plugins/ (top level)", "plugins/（根目录）", "plugins/（根目錄）") : section.Title;
        var heading = OverviewUi.Text(card, title, depth <= 1 ? 16 : 15); heading.fontStyle = FontStyles.Bold; heading.color = section.Stays ? SkipColor : OverviewUi.Ivory;
        var counts = OverviewUi.Text(card, Counts(section), 12); counts.color = OverviewUi.Muted; counts.alignment = TextAlignmentOptions.MidlineRight;
        float countsWidth = Mathf.Ceil(counts.GetPreferredValues(counts.text, Mathf.Infinity, Mathf.Infinity).x) + 4;
        OverviewUi.PlaceClipped(counts, cardWidth - 12 - countsWidth, inner + 3, countsWidth, 18);
        float headingWidth = Mathf.Min(Mathf.Ceil(heading.GetPreferredValues(title, Mathf.Infinity, Mathf.Infinity).x) + 4, Mathf.Max(80, cardWidth - 64 - countsWidth));
        OverviewUi.PlaceClipped(heading, 40, inner + 1, headingWidth, 22);
        if (section.RenamedFrom.Length > 0)
        {
            var renamed = OverviewUi.Text(card, Loc.T("renamed from ", "由「", "由「") + section.RenamedFrom + Loc.T("", "」改名", "」改名"), 12); renamed.color = OverviewUi.Gold;
            OverviewUi.PlaceClipped(renamed, 46 + headingWidth, inner + 4, Mathf.Max(40, cardWidth - 64 - countsWidth - headingWidth - 12), 18);
        }
        var click = heading.gameObject.AddComponent<OverviewHeaderClick>(); heading.raycastTarget = true;
        click.Clicked = _ => { if (!_folded.Add(section.Key)) _folded.Remove(section.Key); Rebuild(); };
        float cy = inner + 32;
        if (open) foreach (var entry in entries) cy = Row(card, entry, cy, cardWidth) + 4;
        cy += open && entries.Count > 0 ? 4 : 0;
        OverviewUi.Place(card, left, top, cardWidth, cy);
        return top + cy;
    }

    private string Counts(LayoutReviewModel.Section section)
    {
        if (section.EmptyFolders) return Loc.F("{0} folders", "{0} 个", "{0} 個", section.Entries.Count(e => !_deleted.Contains(e.From)));
        int mods = section.Stays ? section.Mods : LayoutReviewModel.SubtreeMods(section, _sections);
        var parts = new List<string> { Loc.F("{0} mods", "{0} 个模组", "{0} 個模組", mods) };
        if (section.Count(K.Move) > 0) parts.Add(Loc.F("{0} move in", "移入 {0}", "移入 {0}", section.Count(K.Move)));
        if (section.Count(K.Extracted) > 0) parts.Add(Loc.F("{0} moved out of a mod", "拆出 {0}", "拆出 {0}", section.Count(K.Extracted)));
        if (!section.Stays && section.Count(K.InPlace) > 0) parts.Add(Loc.F("{0} in place", "原位 {0}", "原位 {0}", section.Count(K.InPlace)));
        return string.Join(" · ", parts);
    }

    private float Row(RectTransform card, LayoutReviewModel.Entry entry, float y, float width)
    {
        float x = 20, rowWidth = width - x - 10, textX = 76, textWidth = rowWidth - textX - (entry.Kind == K.Empty ? 120 : 44);
        var row = OverviewUi.Panel("Package", card, RowColor);
        var (tagText, tagColor) = Tag(entry.Kind);
        var tag = OverviewUi.Panel("Tag", row, new Color(tagColor.r, tagColor.g, tagColor.b, .9f)); OverviewUi.Place(tag, 8, 8, 56, 20);
        var tagLabel = OverviewUi.Text(tag, tagText, 12); tagLabel.color = new Color(.07f, .08f, .08f); tagLabel.fontStyle = FontStyles.Bold; tagLabel.alignment = TextAlignmentOptions.Center;
        OverviewUi.Fill(tagLabel.rectTransform);
        float sourceWidth = 0;
        if (entry.Kind != K.Empty)
        {
            var (source, sourceColor) = Source(entry.Node);
            string countText = entry.Node.ModCount > 1 ? Loc.F("{0} mods · ", "{0} 个模组 · ", "{0} 個模組 · ", entry.Node.ModCount) : "";
            var sourceText = OverviewUi.Text(row, countText + source, 11); sourceText.color = sourceColor; sourceText.alignment = TextAlignmentOptions.MidlineRight;
            sourceWidth = Mathf.Ceil(sourceText.GetPreferredValues(sourceText.text, Mathf.Infinity, Mathf.Infinity).x) + 4;
            OverviewUi.PlaceClipped(sourceText, textX + textWidth - sourceWidth, 8, sourceWidth, 18);
        }
        var name = OverviewUi.Text(row, entry.Folder, 14); name.color = OverviewUi.Ivory;
        OverviewUi.PlaceClipped(name, textX, 7, Mathf.Max(60, textWidth - sourceWidth - 10), 20);
        float ry = 30;
        if (entry.Kind == K.Empty)
        {
            string contents = entry.Node.FileCount == 0 ? Loc.T("Empty", "空的", "空的") : Loc.F("{0} files, no mod", "{0} 个文件，没有模组", "{0} 個檔案，沒有模組", entry.Node.FileCount);
            ry = Line(row, contents, OverviewUi.Muted, textX, ry, textWidth, 11);
        }
        else
        {
            bool moving = entry.Kind is K.Move or K.Extracted or K.Ungrouped && !string.Equals(entry.From, entry.To, StringComparison.OrdinalIgnoreCase);
            ry = Line(row, (moving ? Loc.T("from  ", "来自  ", "來自  ") : Loc.T("at  ", "位置  ", "位置  ")) + entry.From, OverviewUi.Muted, textX, ry, textWidth, 11);
        }
        if (entry.Node.Manager) ry = Line(row, Loc.T("Mod manager itself · moved last, after the game exits", "模组管理器自身 · 退出游戏后最后移动", "模組管理器自身 · 結束遊戲後最後移動"), NoteInk, textX, ry, textWidth, 11);
        foreach (string reason in entry.Reasons) ry = Line(row, "· " + Reason(reason), ReasonInk, textX, ry, textWidth, 12);
        foreach (string note in entry.Notes) ry = Line(row, "· " + Reason(note), NoteInk, textX, ry, textWidth, 11);
        string absolute = Path.Combine(Paths.BepInExRootPath, entry.From.Replace('/', Path.DirectorySeparatorChar));
        var folder = OverviewUi.IconButton(row, "folder", () => { try { PackFileShortcuts.Open(absolute, true); } catch (Exception error) { Plugin.Log.LogWarning("[目录整理] 打开目录失败：" + error.Message); } }, 26, 26);
        OverviewUi.Place((RectTransform)folder.transform, rowWidth - 34, 6, 26, 26);
        if (entry.Kind == K.Empty)
        {
            var delete = OverviewUi.Button(row, Loc.T("Recycle", "删除", "刪除"), () => ConfirmRecycle(entry, absolute), 70, 26, UiActionRole.Danger);
            OverviewUi.Place((RectTransform)delete.transform, rowWidth - 112, 6, 70, 26);
        }
        float height = Mathf.Max(38, ry + 6);
        OverviewUi.Place(row, x, y, rowWidth, height);
        return y + height;
    }

    /// <summary>Empty folders go to the Windows Recycle Bin after one confirmation; folders with mod files are refused.</summary>
    private void ConfirmRecycle(LayoutReviewModel.Entry entry, string absolute)
    {
        string? refusal = RecycleBin.Refusal(absolute);
        if (refusal != null) { _status?.Invoke(Loc.T("This folder cannot be moved to the Recycle Bin: ", "不能放进回收站：", "不能放進資源回收筒：") + refusal); return; }
        OverviewUi.Confirm(Loc.T("Move to the Recycle Bin: ", "放进回收站：", "放進資源回收筒：") + entry.Folder + (entry.Node.FileCount > 0 ? Loc.F(" ({0} files)", "（含 {0} 个文件）", "（含 {0} 個檔案）", entry.Node.FileCount) : ""), () =>
        {
            try { RecycleBin.Send(absolute); _deleted.Add(entry.From); _status?.Invoke(Loc.T("Moved to the Recycle Bin.", "已放进回收站。", "已放進資源回收筒。")); Rebuild(); }
            catch (Exception error) { _status?.Invoke(Loc.LocalizeMessage(error.Message)); }
        }, UiActionRole.Danger);
    }

    private static float Line(RectTransform row, string text, Color color, float x, float y, float width, float size)
    {
        var line = OverviewUi.Text(row, text, size); line.color = color; line.textWrappingMode = TextWrappingModes.Normal;
        float height = Mathf.Ceil(line.GetPreferredValues(text, width, Mathf.Infinity).y) + 1;
        OverviewUi.Place(line.rectTransform, x, y, width, height);
        return y + height + 1;
    }

    private static (string, Color) Tag(K kind) => kind switch
    {
        K.Move => (Loc.T("Move", "移入", "移入"), MoveColor),
        K.Extracted => (Loc.T("Out", "拆出", "拆出"), RenameColor),
        K.InPlace => (Loc.T("Stays", "原位", "原位"), StayColor),
        K.Ungrouped => (Loc.T("No group", "未分组", "未分組"), UngroupedColor),
        K.Empty => (Loc.T("Empty", "空", "空"), OverviewUi.Muted),
        _ => (Loc.T("Kept", "不动", "不動"), SkipColor)
    };

'''
p.write_text(head + tail, encoding='utf-8')
print('ok')
