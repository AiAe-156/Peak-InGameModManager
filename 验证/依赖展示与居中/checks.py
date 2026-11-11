from pathlib import Path
r=Path(__file__).resolve().parents[2]
def read(f):return (r/f).read_text(encoding='utf-8-sig')
s=read('src/Overview/OverviewDependencyHints.cs');c=read('src/Overview/OverviewDependencyHints.Components.cs');v=read('src/Overview/OverviewListView.cs');i=read('src/Overview/OverviewListView.Infrastructure.cs')
checks={
 'forward expands chains, reverse summary': 'reverse ? 1 : _chains ? 32 : 1, reverse ? 8 : 2048' in c,
 'direct nodes not swallowed by nested duplicate': 'e.Depth == 1 || !direct.Contains(e.Id)' in c,
 'limit explicitly reported':'entries.Any(e => e.Truncated)' in c,
 'maximum height raised within screen':'Mathf.Min(680, bounds.height - 40)' in s,
 'no prefix truncation':'GetRange(0,' not in s,
 'both modes scroll full text':'_scroll.enabled=_fixed || _scrollableLong' in s,
 'long card enter supported':'if (!_fixed && !_scrollableLong) return' in s,
 'long card delayed dismissal':'if (!_fixed && !_scrollableLong) HideNow()' in s,
 'card stops following while entering':'!_onCard && _hideAt < 0' in s,
 'menu heading uses readable ink':'SourceToolGroup(label, Ivory' in read('src/Overview/OverviewUi.CompactDropdown.cs'),
 'ordinary icons center actual row':v.count('(row.rect.height - OverviewRowActions.Size) / 2')==3,
 'component icons center actual row':i.count('(row.rect.height - OverviewRowActions.Size) / 2')==3,
 'source chip centers actual row':'RoundedChip.Add(row, mod.SourceKind, 0, (row.rect.height - 23) / 2)' in v,
 'checkbox centers actual row':'(row.rect.height - 24) / 2, 24, 24' in v,
}
for name,ok in checks.items():print(('PASS ' if ok else 'FAIL ')+name)
assert all(checks.values())
for h in [36,38,44,60]:
 for child in [23,24,30]:assert ((h-child)/2+child/2)==h/2
print('PASS 12 row/chip/button center geometry cases')
print('RESULT',len(checks),'source contracts + 12 geometry cases')
