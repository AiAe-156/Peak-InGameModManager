from pathlib import Path
r=Path(__file__).resolve().parents[2]
def read(n):return (r/'src/Overview'/n).read_text(encoding='utf-8-sig')
c=read('OverviewDependencyHints.Components.cs');s=read('OverviewDependencyHints.cs');v=read('OverviewListView.VirtualRows.cs')
checks={
 'both directions two levels':'_chains ? 2 : 1, reverse ? 32 : 2048' in c,
 'per branch ellipsis':'if (entry.Truncated) lines.Add' in c and 'DependencyDepthStyle.Wrap(TreeArrow() + " …' in c,
 'no omission paragraphs':'第三级及更深依赖已省略' not in c and '其余被依赖项请查看' not in c,
 'no duplicated footer':'更多关系请点击名称查看' not in c,
 'no shared-reference grey note':'Shared reference' not in c,
 'single requested footer':'双击名称查看依赖详情' in s,
 'footer always visible':'_footer.gameObject.SetActive(true)' in s,
 'no extra scrolling instruction':'移入此窗口滚动' not in s,
 'total height remains 480':'Mathf.Min(480 - 24, bounds.height - 40)' in s,
 'focus cannot jump queue':'var priority=' not in v,
 'fixed order captured once':'if(_buildOrderCaptured)return;' in v,
 'unbuilt focus waits':'row.View==null || row.Layers!=null' in v and 'candidate.View==null || candidate.Layers!=null' in v,
 'no viewport virtualization':'VirtualRowRange.Contains' not in v and 'ParkRow(' not in v,
}
for k,b in checks.items():print(('PASS ' if b else 'FAIL ')+k)
assert all(checks.values())
print('RESULT',len(checks),'source contracts')
