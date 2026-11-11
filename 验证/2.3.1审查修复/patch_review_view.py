"""2.3.1: rewrite the header/notice/empty-folder parts of LayoutReviewView.cs."""
import sys
sys.path.insert(0, r"F:\Games\SteamLibrary\steamapps\common\PEAK\MODs\ModConfigEnhance\验证\2.3.1审查修复")
from patchlib import sub

F = "src/Overview/LayoutReviewView.cs"

sub(F, '''/// when needed, the reason in plain words. "Stays where it is" and "empty folders" (each with a Recycle Bin button) come last.</summary>''',
'''/// when needed, the reason in plain words. "Stays where it is" and "empty folders" come last. 2.3.1: empty folders carry
/// checkboxes (one for the section, one per folder, ticked by default); the ticked ones go to the Recycle Bin together
/// with "Organize", after every move succeeded — no separate dialog.</summary>''')

sub(F, '''    private readonly HashSet<string> _deleted = new(StringComparer.OrdinalIgnoreCase);
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
        float x = 14;''',
'''    /// <summary>Empty folders (their OriginalPath) the user unticked; everything else listed as empty is recycled.</summary>
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
        var plain = notice.Where(n => !n.Warning).Select(n => n.Text.Replace("\\n", " ")).Where(t => t.Trim().Length > 0).ToList();
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
        float x = 14 + totalsWidth + 12;''')

sub(F, '''        Chip(_host, x, y, OverviewUi.Muted, Loc.T("Empty folders", "空文件夹", "空資料夾"), _summary.Empty);
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
        y += 34;''',
'''        x = Chip(_host, x, y, OverviewUi.Muted, Loc.T("Empty folders", "空文件夹", "空資料夾"), _summary.Empty);
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
        y += 32;''')

sub(F, '''    private bool Visible(LayoutReviewModel.Entry entry) => !_deleted.Contains(entry.From) && _filter switch''',
'''    private bool Visible(LayoutReviewModel.Entry entry) => _filter switch''')

# section header: a checkbox for the whole empty-folder section
sub(F, '''        var click = heading.gameObject.AddComponent<OverviewHeaderClick>(); heading.raycastTarget = true;
        click.Clicked = _ => { if (!_folded.Add(section.Key)) _folded.Remove(section.Key); Rebuild(); };
        float cy = inner + 32;''',
'''        var click = heading.gameObject.AddComponent<OverviewHeaderClick>(); heading.raycastTarget = true;
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
        }''')

# rows: a per-folder checkbox instead of the delete button
sub(F, '''        float x = 20, rowWidth = width - x - 10, textX = 76, textWidth = rowWidth - textX - (entry.Kind == K.Empty ? 120 : 44);''',
'''        bool tickable = entry.Kind == K.Empty && _canRecycle;
        float x = 20, rowWidth = width - x - 10, textX = tickable ? 108 : 76, textWidth = rowWidth - textX - 44;''')
sub(F, '''        var tag = OverviewUi.Panel("Tag", row, new Color(tagColor.r, tagColor.g, tagColor.b, .9f)); OverviewUi.Place(tag, 8, 8, 56, 20);''',
'''        if (tickable)
        {
            var check = OverviewUi.Check(row, !_keep.Contains(entry.From), value => { if (value) _keep.Remove(entry.From); else _keep.Add(entry.From); Rebuild(); });
            OverviewUi.Place((RectTransform)check.transform, 6, 5, 26, 26);
        }
        var tag = OverviewUi.Panel("Tag", row, new Color(tagColor.r, tagColor.g, tagColor.b, .9f)); OverviewUi.Place(tag, tickable ? 40 : 8, 8, 56, 20);''')
sub(F, '''        if (entry.Kind == K.Empty)
        {
            var delete = OverviewUi.Button(row, Loc.T("Recycle", "删除", "刪除"), () => ConfirmRecycle(entry, absolute), 70, 26, UiActionRole.Danger);
            OverviewUi.Place((RectTransform)delete.transform, rowWidth - 112, 6, 70, 26);
        }
        float height''', '''        float height''')
sub(F, '''    /// <summary>Empty folders go to the Windows Recycle Bin after one confirmation; folders with mod files are refused.</summary>
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

''', '')
sub(F, '''        if (section.EmptyFolders) return Loc.F("{0} folders", "{0} 个", "{0} 個", section.Entries.Count(e => !_deleted.Contains(e.From)));''',
'''        if (section.EmptyFolders) return Loc.F("{0} folders", "{0} 个", "{0} 個", section.Entries.Count);''')
