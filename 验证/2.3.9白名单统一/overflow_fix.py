"""2.3.9: fixes for the English-overflow audit (验证/发布前审查/英文越界检查.md), verified against the code.

1. Profile management "Content"/"Profile" group labels: fixed 52 px slot; English "Content" ~60 px. Labels now
   measure their text and the action row starts after the wider of the two.
2. Source batch row: "Identify manually" (100 px) sits 10 px left of "Mark as local" (108 px). Both now size to
   their text and are laid out right-to-left.
3. Store filters: "Core / preloader updates" in a 154 px column → shortened to "Core / preloader".
4. Publish wizard: the format button caps at 360 px and "Complete store ZIP (fixed dependencies / inline)" does
   not fit → the button shows a short label; the full name stays in the window title and the chooser.
No global auto-shrink: changing every OverviewUi.Button caption at once is out of scope without in-game review.
"""
import os, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
def edit(rel, pairs):
    p = os.path.join(ROOT, 'src', rel)
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if s.count(a) != 1: sys.exit(f'{rel}: expected 1 match, got {s.count(a)}: {a[:90]}')
        s = s.replace(a, b)
    open(p, 'w', encoding='utf-8', newline='').write(s)
    print('edited', rel, len(pairs))

edit('Overview/OverviewPage.Profiles.cs', [
 ('''            OverviewUi.Place(label.rectTransform, detailX, actionY, 52, 34); placed.Add(label.rectTransform);''',
  '''            // 2.3.9: sized to the text (English "Content" did not fit the old fixed 52 px); actions start after it.
            float labelWidth = Mathf.Max(52, label.GetPreferredValues(caption).x + 6);
            OverviewUi.Place(label.rectTransform, detailX, actionY, labelWidth, 34); placed.Add(label.rectTransform);
            actionX = Mathf.Max(actionX, detailX + labelWidth + 4);'''),
])

edit('Overview/SourceUi.cs', [
 ('''}, 100, 34, role: resolved ? UiActionRole.Neutral : UiActionRole.Edit); OverviewUi.Place((RectTransform)inspect.transform, width - 222, y + 8, 100, 34);''',
  '''}, 100, 34, role: resolved ? UiActionRole.Neutral : UiActionRole.Edit);
            // 2.3.9: both buttons fit their text (English "Identify manually" overflowed into "Mark as local").
            float inspectWidth = Mathf.Max(100, inspect.GetComponentInChildren<TMP_Text>(true).GetPreferredValues(inspect.GetComponentInChildren<TMP_Text>(true).text).x + 22);
            var measureCaption = inspect.GetComponentInChildren<TMP_Text>(true);
            float localWidth = resolved ? 0 : Mathf.Max(108, measureCaption.GetPreferredValues(Loc.T("Mark as local", "标记为本地", "標記為本機")).x + 22);
            OverviewUi.Place((RectTransform)inspect.transform, width - 4 - localWidth - (resolved ? 0 : 8) - inspectWidth, y + 8, inspectWidth, 34);'''),
 ('''}, 108, 34, role: UiActionRole.Edit); OverviewUi.Place((RectTransform)local.transform, width - 112, y + 8, 108, 34);''',
  '''}, 108, 34, role: UiActionRole.Edit); OverviewUi.Place((RectTransform)local.transform, width - 4 - localWidth, y + 8, localWidth, 34);'''),
])

edit('Overview/ShopWindow.cs', [
 ('Filter(T("Core / preloader updates","核心／启动补丁更新"),', 'Filter(Loc.T("Core / preloader","核心／启动补丁更新","核心／啟動修補更新"),'),
])

edit('Overview/PublishWizard.cs', [
 ('''        Button(T("Publish format: ", "发布方式：") + FormatLabel(_request.Mode), ''',
  '''        Button(T("Publish format: ", "发布方式：") + ShortFormatLabel(_request.Mode), '''),
 ('''    internal static string FormatLabel(PublishMode mode) => mode switch {''',
  '''    /// <summary>2.3.9: the format button caps at 360 px; the full names stay in the title and the chooser.</summary>
    private static string ShortFormatLabel(PublishMode mode) => mode switch {
        PublishMode.Local => Loc.T("Local modpack", "完整本地整合包", "完整本機整合包"),
        PublishMode.ThunderstoreComplete => Loc.T("Store ZIP (complete)", "完整商店包", "完整商店包"),
        PublishMode.Thunderstore => Loc.T("Store ZIP (legacy)", "依赖式商店包", "相依式商店包"), _ => Loc.T("Author data only", "仅作者资料", "僅作者資料") };
    internal static string FormatLabel(PublishMode mode) => mode switch {'''),
])
print('ok')
