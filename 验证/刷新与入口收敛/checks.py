from pathlib import Path
import subprocess
r=Path(__file__).resolve().parents[2]
out=Path(__file__).resolve().parent
def read(p):return (r/p).read_text(encoding='utf-8-sig')
def method(text,signature):
    start=text.index(signature); b=text.index('{',start); end=b+1; depth=1
    while depth:
        depth+=(text[end]=='{')-(text[end]=='}');end+=1
    return text[start:end]
v=read('src/Overview/OverviewListView.cs'); i=read('src/Overview/OverviewListView.Infrastructure.cs'); rows=read('src/Overview/OverviewListView.VirtualRows.cs'); c=read('src/Overview/OverviewController.cs'); cascade=read('src/Overview/OverviewCascade.cs')
checks={
'reload does not render intermediate list':'Render();' not in method(v,'public void Reload()'),
'all structural requests gated during scan':'_infrastructureLoading || !_cascade.BeforeRebuild()' in v,
'render consumes completion':'_infrastructureRedraw = false; UiPerfProbe.ListCommit();' in v,
'stale results rejected':'generation != _infrastructureGeneration' in i and 'revision != OverviewPageCache.Inventory || context != SourceUi.DependencyContext' in i,
'scan retains old snapshot':'_infrastructure = Array.Empty' not in method(i,'private void RefreshInfrastructure').split('ContinueWith')[0],
'failure clears loading':'_infrastructureLoading = false;' in i and 'if (task.IsFaulted || task.IsCanceled)' in i,
'no full tree entry lookup':'GetComponentsInChildren<Button>' not in method(c,'private void PositionSettingsEntry()'),
'entry early return before layout write':'_settingsLayout.Value.Equals(layout)) return;' in c,
'missing reference lookup throttled':'_settingsReferenceRetryAt = Time.unscaledTime + .25f' in c,
'font uses configured auto-size max':'_settingsReferenceText.enableAutoSizing ? _settingsReferenceText.fontSizeMax' in c,
'no all-row navigation rewrite':'LinkRowNavigation' not in rows,
'per-frame shared budget':'_rowBuildBudget.TryTake(Time.frameCount' in rows,
'navigation target only bypass':'ReferenceEquals(row,immediate)' in rows,
'scan pauses old row construction':'_building || _infrastructureLoading)return' in rows,
'prewarm waits for row and fade completion':'_residentPending ||' in v and '_cascade.IsAnimating)return' in v,
'new generation fades once':'if (cleared && _opened) PlayAppearance();' in v,
'no forced global layout':'Canvas.ForceUpdateCanvases' not in cascade,
'whole list stays resident':'ParkRow(' not in rows and 'VirtualRowRange.Contains' not in rows,
'mod shell has labels before first yield':all(x in method(v,'System.Collections.IEnumerator BuildModLayers').split('yield return null;')[0] for x in ['var title =','var versionText =','var description =']),
'mod buttons after first yield':'OverviewUi.Check' not in method(v,'System.Collections.IEnumerator BuildModLayers').split('yield return null;')[0],
'component shell before buttons':'OverviewRowActions.Settings' not in method(i,'System.Collections.IEnumerator BuildComponentLayers').split('yield return null;')[0],
'shell and child work independent':'RefreshRowLayers();' in v and '_layerBuildBudget.TryTake' in rows,
'child starts after shell frame':'row.ShellFrame==Time.frameCount' in rows,
'stale child generation skipped':'!ReferenceEquals(row.Layers,work.Steps)' in rows,
'child work disposed on release':'(row.Layers as IDisposable)?.Dispose();row.Layers=null;' in method(rows,'private void ReleaseRow'),
'focus waits for child completion':'_layerFocus=(candidate,column,source.gameObject)' in rows and 'CompleteLayerFocus(row)' in rows,
'late row layers independent of toolbar speed':'PlaySound' not in read('src/Overview/OverviewLayerFade.cs') and '_respectAppearanceSpeed=waiting' in read('src/Overview/OverviewLayerFade.cs') and 'OverviewLayerFade.Attach(child);' in rows,
'row animation timing and sound unchanged':all(x in cascade for x in ['Interval = .05f, LerpSpeed = 10f','_speed = OverviewOptions.AppearanceSpeed.Value','entry.Started = true; PlaySound();','Time.unscaledDeltaTime * LerpSpeed * _speed']),
'scroll does not recapture order':'if(_buildOrderCaptured)return;' in rows and 'foreach(var row in _buildOrder)' in rows,
'focus does not end reveal':'if (selected != null && selected.transform.IsChildOf(transform)) CompleteAppearance();' not in cascade,
'page exit preserves row sequence':'Keep the original queue' in cascade and 'private void OnDisable() => Finish()' not in cascade,
'reentry resumes instead of replaying':'_cascade.Resume();' in v and '!_cascade.HasSequence' in v,
'child fade pauses on hidden page':'private void OnDisable()=>Restore()' not in read('src/Overview/OverviewLayerFade.cs'),
'independent toolbar horizontal order':'ThenBy(t=>((RectTransform)t.transform).anchoredPosition.x)' in read('src/Overview/OverviewToolbarReveal.cs'),
'no toolbar replay on layout':'_seen.Add(child.GetInstanceID())' in read('src/Overview/OverviewToolbarReveal.cs'),
'200ms retained':'Delay = .200f' in read('src/Overview/HoverDwell.cs'),
}
for name,ok in checks.items():print(('PASS ' if ok else 'FAIL ')+name,flush=True)
assert all(checks.values())
print('RESULT',len(checks),'source contracts passed',flush=True)
# Execute the exact production publication method, not a rewritten state machine.
(out/'Production.g.cs').write_text('partial class PublicationHarness {\n'+method(i,'private void FlushInfrastructureRedraw()')+'\n}',encoding='utf-8')
subprocess.run(['C:/Program Files/dotnet/dotnet.exe','run','--project',str(out/'RefreshChecks.csproj'),'-c','Release','-p:EnableDeployment=false'],check=True,cwd=r)
