from pathlib import Path
r=Path(__file__).resolve().parents[2]
def read(p):return (r/p).read_text(encoding='utf-8-sig')
v=read('src/Overview/OverviewListView.cs');h=read('src/Overview/OverviewDependencyHints.cs');rows=read('src/Overview/OverviewListView.VirtualRows.cs');c=read('src/Overview/OverviewController.cs');pivot=read('src/Overview/RefreshIconFeedback.cs');profile=read('src/Overview/OverviewPage.Profiles.cs')
checks={
'200ms shared':'Delay = .200f' in read('src/Overview/HoverDwell.cs') and '+HoverDwell.Delay' in h,
'no synchronous font diagnostic on enter':'OverviewFontReport.Run' not in c,
'deferred inspect':'RequestInspect(transform)' in c,
'no global canvas force in cascade':'Canvas.ForceUpdateCanvases()' not in read('src/Overview/OverviewCascade.cs'),
'row budget bounded per frame':'_rowBuildBudget.TryTake(Time.frameCount' in rows and '_count>=2' in read('src/Overview/RowBuildBudget.cs') and 'if(pending)_virtualTop=float.NaN' in rows,
'navigation only bypasses target row budget':'ReferenceEquals(row,immediate)' in rows and 'RefreshVirtualRows(row)' in rows,
'new rows retain appearance':'_cascade.RegisterBuiltRow(view)' in rows,
'no active glyph bulk prewarm':'_prewarm.Tick()' not in v and 'EnqueueChunks' not in v,
'prewarm delayed until stable':'Time.unscaledTime+3' in v and '_warmSettledFrames<30' in v and '_infrastructureLoading' in v,
'prewarm step not synchronous drain':'_dependencyHints.TickPrewarm()' in v and 'try { return RefreshCard(hook.Content); }' not in h,
'prewarm cache only':'complete(result);' in h and 'ApplyLayout(result);complete(result)' not in h,
'prewarm font change invalidates':'if(_fontAtShow!=_text.font || _fallbackAtShow!=fallback)_layouts.Clear()' in h,
'refresh center preserves offsets':'rect.pivot=new Vector2(.5f,.5f);rect.offsetMin=min;rect.offsetMax=max' in pivot,
'profile distinction explicit':'游戏当前使用：{0}' in profile and '正在编辑：{0}' in profile,
'save does not switch explained':'“仅保存”不会切换生效方案' in profile,
'apply failure retained':'_applyFailure=result.Message' in profile,
'apply success refreshes review identity':'review.AppliedName=_repo.AppliedProfileName' in profile,
'backup kept separate':'ActionableCopies(original)' in read('src/Overview/ModToggleService.cs'),
'disable avoids overwriting old':'".disabled-by-mce"' in read('src/Overview/ModToggleService.cs'),
'conflict full paths':'paths.Select(Path.GetFullPath)' in read('src/Overview/OverviewFileConflicts.cs'),
}
for k,v in checks.items():print(('PASS ' if v else 'FAIL ')+k)
assert all(checks.values())
print(f'RESULT {len(checks)} wiring checks passed; Unity acceptance pending')
