#nullable enable
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
/// when needed, the reason in plain words. "Stays where it is" and "empty folders" come last. 2.3.1: empty folders carry
/// checkboxes (one for the section, one per folder, ticked by default); the ticked ones go to the Recycle Bin together
/// with "Organize", after every move succeeded — no separate dialog.</summary>
internal sealed class LayoutReviewView
{
    private enum Filter { All, Changes, Stays }

    private static Color CardColor => ModConfigLook.Surface(new(.13f, .15f, .15f, .97f));
    private static Color RowColor => ModConfigLook.Surface(new(.09f, .10f, .10f, .92f));
    private static Color HostColor => ModConfigLook.Surface(new(.065f, .08f, .095f, 1));
    private static readonly Color MoveColor = new(.40f, .72f, .82f), RenameColor = new(.56f, .66f, .92f), StayColor = new(.50f, .77f, .49f);
    private static readonly Color UngroupedColor = new(.56f, .56f, .52f), SkipColor = new(.88f, .63f, .33f), ReasonInk = new(.93f, .72f, .45f), NoteInk = new(.63f, .75f, .83f);
    private const float CardIndent = 18;

    private readonly RectTransform _host, _content;
    private readonly ScrollRect _scroll;
    private readonly List<LayoutReviewModel.Section> _sections;
    private readonly LayoutReviewModel.Summary _summary;
    private readonly HashSet<string> _folded = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Empty folders (their OriginalPath) the user unticked; everything else listed as empty is recycled.</summary>
    private readonly HashSet<string> _keep = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Button Button, Filter Filter)> _filters = new();
    private readonly bool _canRecycle;
    private Filter _filter = Filter.All;

    internal static LayoutReviewView Show(RectTransform panel, IReadOnlyList<LayoutTreeNode> tree, IReadOnlyList<(string Text, bool Warning)> notice, bool canRecycle)
        => new(panel, tree, notice, canRecycle);

    /// <summary>The ticked empty folders, as their preview paths, for the confirm request.</summary>
    internal List<string> RecycleSelection() => !_canRecycle ? new List<string>()
        : _sections.Where(s => s.EmptyFolders).SelectMany(s => s.Entries).Select(e => e.From).Where(path => !_keep.Contains(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private LayoutReviewView(RectTransform panel, IReadOnlyList<LayoutTreeNode> tree, IReadOnlyList<(string Text, bool Warning)> notice, bool canRecycle)
    {
        _canRecycle = canRecycle;
        _sections = LayoutReviewModel.Build(tree, out _summary);
        foreach (var section in _sections) if (!section.OpenByDefault) _folded.Add(section.Key);
        _host = OverviewUi.Panel("LayoutReview", panel, HostColor); OverviewUi.Fill(_host);
        // 2.3.1: the header is one line shorter (divider at 84), so the tree starts right under it.
        _host.offsetMin = new Vector2(24, 80); _host.offsetMax = new Vector2(-24, -94);
        float width = panel.rect.width - 48, y = 8;

        // Status and the helper's message share one line; warnings (errors, blockers) keep their own lines.
        var plain = notice.Where(n => !n.Warning).Select(n => n.Text.Replace("\n", " ")).Where(t => t.Trim().Length > 0).ToList();
        if (plain.Count > 0)
        {
            var line = OverviewUi.Text(_host, string.Join("   ·   ", plain), 14); line.color = OverviewUi.Muted;
            OverviewUi.PlaceClipped(line, 14, y, width - 28, 20); y += 22;
        }
        foreach (var (text, _) in notice.Where(n => n.Warning))
        {
            var line = OverviewUi.Text(_host, text, 14); line.color = ReasonInk;
            line.textWrappingMode = TextWrappingModes.Normal;
            float height = Mathf.Ceil(line.GetPreferredValues(text, width - 28, Mathf.Infinity).y) + 2;
            OverviewUi.Place(line.rectTransform, 14, y, width - 28, height); y += height + 2;
        }
        // Totals, the count chips and the filters on one row: folders vs mods first, so the count matches the overview list.
        var totals = OverviewUi.Text(_host, Loc.F("{0} folders · {1} mods", "{0} 个文件夹 · 共 {1} 个模组", "{0} 個資料夾 · 共 {1} 個模組", _summary.Folders, _summary.Mods), 15);
        totals.color = OverviewUi.Ivory;
        float totalsWidth = Mathf.Ceil(totals.GetPreferredValues(totals.text, Mathf.Infinity, Mathf.Infinity).x) + 6;
        OverviewUi.PlaceClipped(totals, 14, y + 3, totalsWidth, 20);
        float x = 14 + totalsWidth + 12;
        x = Chip(_host, x, y, MoveColor, Loc.T("Move in", "移入", "移入"), _summary.Move);
        x = Chip(_host, x, y, RenameColor, Loc.T("Moved out of a mod", "拆出", "拆出"), _summary.Extracted);
        x = Chip(_host, x, y, OverviewUi.Gold, Loc.T("Folder renamed", "文件夹改名", "資料夾改名"), _summary.Renamed);
        x = Chip(_host, x, y, StayColor, Loc.T("In place", "原位", "原位"), _summary.InPlace);
        x = Chip(_host, x, y, UngroupedColor, Loc.T("Ungrouped", "未分组", "未分組"), _summary.Ungrouped);
        x = Chip(_host, x, y, SkipColor, Loc.T("Not moved", "不动", "不動"), _summary.Stays);
        x = Chip(_host, x, y, OverviewUi.Muted, Loc.T("Empty folders", "空文件夹", "空資料夾"), _summary.Empty);
        // Filters go right-aligned on the same row when they fit, otherwise on a row of their own.
        var filters = new[] { (Loc.T("All", "全部", "全部"), Filter.All, 80f), (Loc.T("Changes only", "只看变动", "只看變動"), Filter.Changes, 92f), (Loc.T("Not moved and empty", "只看不动与空文件夹", "只看不動與空資料夾"), Filter.Stays, 150f) };
        float filtersWidth = filters.Sum(f => f.Item3) + 6 * (filters.Length - 1);
        float fx = width - 14 - filtersWidth;
        if (fx < x + 12) { y += 30; fx = 14; }
        foreach (var (label, filter, w) in filters)
        {
            var button = OverviewUi.Button(_host, label, () => { _filter = filter; Rebuild(); }, w, 26, UiActionRole.Neutral);
            var caption = button.GetComponentInChildren<TMP_Text>(true); caption.enableAutoSizing = true; caption.fontSizeMin = 10; caption.fontSizeMax = 13;
            OverviewUi.Place((RectTransform)button.transform, fx, y - 1, w, 26); fx += w + 6;
            _filters.Add((button, filter));
        }
        y += 32;
        var divider = OverviewUi.Panel("Divider", _host, ModConfigLook.Surface(new Color(1, 1, 1, .08f))); OverviewUi.Place(divider, 14, y, width - 28, 1); y += 8;

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

    private bool Visible(LayoutReviewModel.Entry entry) => _filter switch
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
        if (section.EmptyFolders && _canRecycle && section.Entries.Count > 0)
        {
            // One switch for the whole section; each row below can still be unticked on its own.
            bool all = section.Entries.All(e => !_keep.Contains(e.From));
            var check = OverviewUi.Check(card, all, value =>
            {
                foreach (var entry in section.Entries) { if (value) _keep.Remove(entry.From); else _keep.Add(entry.From); }
                Rebuild();
            });
            OverviewUi.Place((RectTransform)check.transform, 40, cy - 2, 26, 26);
            int ticked = section.Entries.Count(e => !_keep.Contains(e.From));
            var label = OverviewUi.Text(card, Loc.F("Move to the Recycle Bin when organizing ({0} of {1})", "整理时一并放进回收站（{0}/{1}）", "整理時一併放進資源回收筒（{0}/{1}）", ticked, section.Entries.Count), 14);
            label.color = OverviewUi.Ivory;
            OverviewUi.PlaceClipped(label, 74, cy + 1, cardWidth - 90, 20);
            var hint = OverviewUi.Text(card, Loc.T("Recycled after every move succeeded; each folder is checked again first and skipped if it is no longer empty. You can restore them from the Recycle Bin.",
                "所有移动成功后才放进回收站；放之前逐个再查一次，已经不空的会跳过。可以从回收站还原。", "所有移動成功後才放進資源回收筒；放之前逐個再查一次，已經不空的會略過。可以從資源回收筒還原。"), 11);
            hint.color = OverviewUi.Muted; hint.textWrappingMode = TextWrappingModes.Normal;
            float hintHeight = Mathf.Ceil(hint.GetPreferredValues(hint.text, cardWidth - 90, Mathf.Infinity).y) + 1;
            OverviewUi.Place(hint.rectTransform, 74, cy + 22, cardWidth - 90, hintHeight);
            cy += 26 + hintHeight + 6;
        }
        if (open) foreach (var entry in entries) cy = Row(card, entry, cy, cardWidth) + 4;
        cy += open && entries.Count > 0 ? 4 : 0;
        OverviewUi.Place(card, left, top, cardWidth, cy);
        return top + cy;
    }

    private string Counts(LayoutReviewModel.Section section)
    {
        if (section.EmptyFolders) return Loc.F("{0} folders", "{0} 个", "{0} 個", section.Entries.Count);
        int mods = section.Stays ? section.Mods : LayoutReviewModel.SubtreeMods(section, _sections);
        var parts = new List<string> { Loc.F("{0} mods", "{0} 个模组", "{0} 個模組", mods) };
        if (section.Count(K.Move) > 0) parts.Add(Loc.F("{0} move in", "移入 {0}", "移入 {0}", section.Count(K.Move)));
        if (section.Count(K.Extracted) > 0) parts.Add(Loc.F("{0} moved out of a mod", "拆出 {0}", "拆出 {0}", section.Count(K.Extracted)));
        if (!section.Stays && section.Count(K.InPlace) > 0) parts.Add(Loc.F("{0} in place", "原位 {0}", "原位 {0}", section.Count(K.InPlace)));
        return string.Join(" · ", parts);
    }

    private float Row(RectTransform card, LayoutReviewModel.Entry entry, float y, float width)
    {
        bool tickable = entry.Kind == K.Empty && _canRecycle;
        float x = 20, rowWidth = width - x - 10, textX = tickable ? 108 : 76, textWidth = rowWidth - textX - 44;
        var row = OverviewUi.Panel("Package", card, RowColor);
        var (tagText, tagColor) = Tag(entry.Kind);
        if (tickable)
        {
            var check = OverviewUi.Check(row, !_keep.Contains(entry.From), value => { if (value) _keep.Remove(entry.From); else _keep.Add(entry.From); Rebuild(); });
            OverviewUi.Place((RectTransform)check.transform, 6, 5, 26, 26);
        }
        var tag = OverviewUi.Panel("Tag", row, new Color(tagColor.r, tagColor.g, tagColor.b, .9f)); OverviewUi.Place(tag, tickable ? 40 : 8, 8, 56, 20);
        var tagLabel = OverviewUi.Text(tag, tagText, 12); tagLabel.color = ModConfigLook.Surface(new Color(.07f, .08f, .08f)); tagLabel.fontStyle = FontStyles.Bold; tagLabel.alignment = TextAlignmentOptions.Center;
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
        float height = Mathf.Max(38, ry + 6);
        OverviewUi.Place(row, x, y, rowWidth, height);
        return y + height;
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

    private static (string, Color) Source(LayoutTreeNode node) => node.Source switch
    {
        LayoutSourceKind.Store => (Loc.T("store-confirmed", "商店已确认", "商店已確認"), StayColor),
        LayoutSourceKind.Local => (Loc.T("local", "本地", "本機"), OverviewUi.Muted),
        _ => (Loc.T("source unverified", "来源待定", "來源待定"), ModConfigLook.Surface(new Color(.55f, .55f, .52f)))
    };

    /// <summary>The planner's codes in plain words. Unknown codes fall back to the code itself so nothing is hidden.</summary>
    internal static string Reason(string code) => code switch
    {
        "MissingOrConflictingGroup" => Loc.T("Its plugins belong to different groups, or to none, so it has no single target folder", "包内插件分属不同分组或没有分组，无法确定目标目录", "套件內外掛分屬不同分組或沒有分組，無法確定目標目錄"),
        "InvalidStableFolderName" => Loc.T("The target folder name is not a valid folder name", "目标目录名不是合法的文件夹名", "目標目錄名不是合法的資料夾名"),
        "GroupFolderCollision" => Loc.T("Two groups would use the same folder", "两个分组会用到同一个目录", "兩個分組會用到同一個目錄"),
        "DestinationCollision" => Loc.T("Another package would move to the same place", "另一个包也要移到同一位置", "另一個套件也要移到同一位置"),
        "DestinationOccupied" or "DestinationOccupiedByPackage" => Loc.T("Something is already at the target place", "目标位置已有其他文件", "目標位置已有其他檔案"),
        "SourceDestinationOverlap" or "OverlappingPackageRoots" => Loc.T("The source and target folders contain each other", "源目录与目标目录互相包含", "來源目錄與目標目錄互相包含"),
        "PathLengthExceeded" => Loc.T("The new path would be too long for Windows", "移动后的路径超出 Windows 长度限制", "移動後的路徑超出 Windows 長度限制"),
        "LinkRejected" or "LinkFactsUnknown" or "InventoryLinkRejected" or "InventoryLinkFactsUnknown" => Loc.T("Contains a link or junction; moving it could touch files elsewhere", "包含链接或目录联接，移动可能影响别处的文件", "包含連結或目錄聯結，移動可能影響別處的檔案"),
        "UnownedSourceInventoryFile" or "SharedOrUnknownFileOwner" => Loc.T("Holds files that belong to another package", "目录里有属于其他包的文件", "目錄裡有屬於其他套件的檔案"),
        "SourceOutsidePluginDirectory" => Loc.T("Not inside plugins/", "不在 plugins 目录内", "不在 plugins 目錄內"),
        "InvalidInternalPath" or "DuplicateInternalPath" or "OverlappingInternalFiles" => Loc.T("A file path inside the folder cannot be moved safely", "目录内有无法安全移动的文件路径", "目錄內有無法安全移動的檔案路徑"),
        "LoadedOrUnknownPlugins" => Loc.T("No readable plugin was found in it", "没有读到可识别的插件", "沒有讀到可識別的外掛"),
        "InvalidOrDuplicateUnitId" or "InvalidPlanInput" or "InventoryIncomplete" or "OwnershipOrFilesIncomplete" => Loc.T("The preview could not read this folder completely; preview again", "预览没能完整读取该目录，请重新预览", "預覽沒能完整讀取該目錄，請重新預覽"),
        "PackageLocked" or "LockFactsUnknown" or "SharedPackage" or "SharingFactsUnknown" => Loc.T("The folder is in use by another operation", "该目录正被其他操作使用", "該目錄正被其他操作使用"),
        "CoreOrPatcherExcluded" => Loc.T("BepInEx core or patcher files are never moved", "BepInEx 核心与预加载补丁不参与整理", "BepInEx 核心與預載補丁不參與整理"),
        "DuplicatePluginId" => Loc.T("Holds more than one copy of the same plugin (for example .dll and .dll.old); they move together", "包内有同一插件的多份副本（如 .dll 与 .dll.old），会一起移动", "套件內有同一外掛的多份副本（如 .dll 與 .dll.old），會一起移動"),
        "PluginOwnedByMultiplePackages" => Loc.T("The same plugin is also installed in another folder; check the duplicates in the overview", "同一插件在另一个目录也装了一份，可在概览「重复」里处理", "同一外掛在另一個目錄也裝了一份，可在概覽「重複」裡處理"),
        "BundledOtherGroup" => Loc.T("Bundles a library from another group; it moves with this package", "内含其他分组的依赖库，随本包一起移动", "內含其他分組的相依程式庫，隨本套件一起移動"),
        "DestinationInsidePackage" => Loc.T("The target folder is inside another mod's folder", "目标位置在另一个模组的文件夹里", "目標位置在另一個模組的資料夾裡"),
        "GroupFromSameName" => Loc.T("No plugin was read from it; it follows the mod with the same name", "没有读到插件，跟随同名模组的分组", "沒有讀到外掛，跟隨同名模組的分組"),
        "GroupFromDependent" => Loc.T("Has no plugin of its own; it follows the mod that depends on it", "自身没有插件，跟随依赖它的模组的分组", "自身沒有外掛，跟隨相依它的模組的分組"),
        "NoGroupKnown" => Loc.T("No plugin and no mod depends on it, so it has no group; it stays where it is", "没有插件，也没有模组依赖它，无法确定分组，保持原位", "沒有外掛，也沒有模組相依它，無法確定分組，保持原位"),
        "NameSuffixed" => Loc.T("Another folder with the same name lands here; this one gets its version added", "同名文件夹会落到同一位置，这一份加上版本号", "同名資料夾會落到同一位置，這一份加上版本號"),
        "EmptyAfterMove" => Loc.T("Empty once its mods have moved out", "里面的模组移走后就空了", "裡面的模組移走後就空了"),
        _ => code
    };
}
