from pathlib import Path
r=Path(__file__).resolve().parents[2]/'src/Overview'
s=(r/'OverviewUi.SaveDialogs.cs').read_text(encoding='utf-8-sig')
w=(r/'IssueReportScrollRect.cs').read_text(encoding='utf-8-sig')
i=(r/'OverviewUi.Issues.cs').read_text(encoding='utf-8-sig')
assert 'horizontalReport: true' in i
for token in ['TextWrappingModes.NoWrap','Mathf.Infinity','scroll.horizontal = true','HorizontalScrollbar','Place(track, 14, 8','Scrollbar.Direction.LeftToRight','text.raycastTarget = true','scroll.horizontalNormalizedPosition = 0']:
 assert token in s,token
for token in ['KeyCode.LeftShift','KeyCode.RightShift','Mathf.Clamp01','data.Use(); return;','base.OnScroll(data)']:
 assert token in w,token
print('PASS 14 horizontal-report wiring contracts')
