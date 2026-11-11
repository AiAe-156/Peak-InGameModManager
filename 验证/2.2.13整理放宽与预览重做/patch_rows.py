from pathlib import Path
import sys
root = Path(sys.argv[1])
def sub(path, old, new, count=1):
    p = root / path; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (path, old[:70], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
v = 'src/Overview/UpdateQueueView.cs'
sub(v, '''        internal TextMeshProUGUI Name = null!, Versions = null!, State = null!, Amount = null!, Speed = null!;''',
'''        internal TextMeshProUGUI Name = null!, Versions = null!, State = null!, Amount = null!, Speed = null!;
        /// <summary>Second-line geometry: the state runs from the left, the version follows it, the speed sits on the right.</summary>
        internal float LineWidth, SpeedWidth;''')
old = (root / v).read_text(encoding='utf-8')
start = old.index('            // 三段互不相交：名称/版本/百分比同一行')
end = old.index('            var track = OverviewUi.Rect("Progress", row.Root);')
new_block = '''            // 两行：第一行名称占满、右端时间或百分比；第二行状态 → 版本紧随其后，下载速度靠右。各列按实际文字宽度占位，空列不占位。
            float lineWidth = width - 28;
            row.Amount = OverviewUi.Text(row.Root, "", 11); row.Amount.alignment = TextAlignmentOptions.MidlineRight; row.Amount.color = OverviewUi.Muted;
            row.Name = OverviewUi.Text(row.Root, JobName(job), 14);
            row.Versions = OverviewUi.Text(row.Root, "", 11); row.Versions.color = OverviewUi.Muted;
            row.Speed = OverviewUi.Text(row.Root, "", 10); row.Speed.color = OverviewUi.Muted; row.Speed.alignment = TextAlignmentOptions.MidlineRight;
            row.State = OverviewUi.Text(row.Root, "", 13); row.State.textWrappingMode = TextWrappingModes.NoWrap;
            row.LineWidth = lineWidth;
            row.State.raycastTarget = true;
            row.State.gameObject.AddComponent<OverviewHeaderClick>().Clicked = _ => ShowDetails(row.Id);
'''
s = old[:start] + new_block + old[end:]
s = s.replace('''            row.Amount = OverviewUi.Text(row.Root, "", 11); row.Amount.alignment = TextAlignmentOptions.MidlineRight; row.Amount.color = OverviewUi.Muted;
            OverviewUi.PlaceClipped(row.Amount, width - 106, 3, 88, 18);
''', '', 1)
(root / v).write_text(s, encoding='utf-8')
sub(v, '''        row.Speed.text = DownloadNote(job);
        row.Amount.text = recorded ? UpdatedAt(job) : job.TotalBytes > 0 ? Math.Min(100, Math.Max(0, job.Progress * 100)).ToString("0") + "%" : "";
        OverviewPage.SetUpdateProgress(row.Fill, job.Progress);''',
'''        row.Speed.text = DownloadNote(job);
        row.Amount.text = recorded ? UpdatedAt(job) : job.TotalBytes > 0 ? Math.Min(100, Math.Max(0, job.Progress * 100)).ToString("0") + "%" : "";
        LayoutLines(row);
        OverviewPage.SetUpdateProgress(row.Fill, job.Progress);''')
sub(v, '''    private void Command(string action, string id)
    {''', '''    /// <summary>Places both text lines from what they actually say, so an empty column takes no room and the name keeps the whole first line.</summary>
    private static void LayoutLines(Row row)
    {
        const float left = 8, gap = 10;
        float right = left + row.LineWidth;
        float Width(TextMeshProUGUI text) => text.text.Length == 0 ? 0 : Mathf.Ceil(text.GetPreferredValues(text.text, Mathf.Infinity, Mathf.Infinity).x) + 2;
        float amount = Width(row.Amount);
        row.Amount.gameObject.SetActive(amount > 0);
        if (amount > 0) OverviewUi.PlaceClipped(row.Amount, right - amount, 3, amount, 18);
        OverviewUi.PlaceClipped(row.Name, left, 3, Mathf.Max(48, row.LineWidth - (amount > 0 ? amount + gap : 0)), 18);
        float speed = Width(row.Speed);
        row.Speed.gameObject.SetActive(speed > 0);
        if (speed > 0) OverviewUi.PlaceClipped(row.Speed, right - speed, 25, speed, 18);
        float available = row.LineWidth - (speed > 0 ? speed + gap : 0);
        float version = Width(row.Versions);
        // The state shrinks first (it is clickable for the full text); the version keeps its full width when it fits at all.
        float state = Mathf.Min(Width(row.State), Mathf.Max(60, available - (version > 0 ? version + gap : 0)));
        OverviewUi.PlaceClipped(row.State, left, 25, Mathf.Max(60, state), 18);
        bool showVersion = version > 0 && state + gap + version <= available + .5f;
        row.Versions.gameObject.SetActive(showVersion);
        if (showVersion) OverviewUi.PlaceClipped(row.Versions, left + state + gap, 26, version, 18);
    }

    private void Command(string action, string id)
    {''')
print('ok')
