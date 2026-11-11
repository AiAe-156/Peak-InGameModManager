from pathlib import Path
import subprocess
r=Path(__file__).resolve().parents[2];out=Path(__file__).resolve().parent
def read(p):return (r/p).read_text(encoding='utf-8-sig')
def method(text,signature):
 start=text.index(signature);b=text.index('{',start);end=b+1;depth=1
 while depth:
  depth+=(text[end]=='{')-(text[end]=='}');end+=1
 return text[start:end]
v=read('src/Overview/OverviewListView.cs');rows=read('src/Overview/OverviewListView.VirtualRows.cs');c=read('src/Overview/OverviewController.cs');s=read('src/Overview/SourceUi.cs');cascade=read('src/Overview/OverviewCascade.cs')
checks={
 'no child fade file':not (r/'src/Overview/OverviewLayerFade.cs').exists(),
 'no toolbar reveal file':not (r/'src/Overview/OverviewToolbarReveal.cs').exists(),
 'no fade references':all('OverviewLayerFade' not in p.read_text(encoding='utf-8-sig') and 'OverviewToolbarReveal' not in p.read_text(encoding='utf-8-sig') for p in (r/'src').rglob('*.cs')),
 'interactive ancestors not faded':method(cascade,'private Entry? Hide').index('GetComponentInChildren<Selectable>')<method(cascade,'private Entry? Hide').index('group.alpha = 0'),
 'incomplete rows excluded from cascade':'row.View != null && row.Layers == null' in v,
 'one row budget':'_layerBuildBudget' not in rows and '_pendingLayers' not in rows,
 'cursor not repeated full scan':'while(_buildCursor<_buildOrder.Count)' in rows and 'foreach(var row in _buildOrder)' not in rows,
 'no full color scan during construction':'RefreshSelectionColors' not in method(rows,'private void RefreshVirtualRows'),
 'shared budget for shell and children':method(rows,'private bool BuildResidentRow').count('_rowBuildBudget.TryTake')==2,
 'finish row before next cursor':'if(!BuildResidentRow(_buildOrder[_buildCursor]))break;' in rows,
 'hidden tabs stop before hide':method(c,'private void SwitchTab').index('_list?.OnTabHidden()')<method(c,'private void SwitchTab').index('_list.Root.SetActive'),
 'tab pause does not cancel network':'_round' not in method(v,'internal void OnTabHidden'),
 'builder checks actual visibility':'!ListVisible' in method(rows,'private void RefreshVirtualRows'),
 'pending focus runs even after cursor completion':'immediate==null && !_layerFocus.HasValue' in rows,
 'plus does not render':'Render(' not in v[v.index('marker = OverviewUi.IconButton(row, "plus"'):v.index('OverviewUi.Place((RectTransform)marker.transform')],
 'live security graph preserved':'_dependencyContext != DependencyContext' in s,
 'uninstall uses live graph':'DiagnosticSummary.ForPackage(DependencyGraph, key)' in s,
 'watcher failure fallback': 'DisplayContext.Refresh(OverviewPageCache.Environment' in s and 'environment.Error +=' in read('src/Overview/OverviewPageCache.cs'),
 'hint hot path uses display snapshot':'SourceUi.DependencyGraph' not in read('src/Overview/OverviewDependencyHints.cs'),
 'resident lists retained':'VirtualRowRange.Contains' not in rows and 'ParkRow(' not in rows,
}
for name,ok in checks.items():print(('PASS ' if ok else 'FAIL ')+name,flush=True)
assert all(checks.values())
# Execute the actual production RefreshVirtualRows scheduling method against a bounded
# row-step fake, and real binding class against Unity storage stubs (no native renderer).
sync=method(read('src/Overview/OverviewUi.cs'),'internal static void SyncCheck')
refresh=method(rows,'private void RefreshVirtualRows')
(out/'Production.g.cs').write_text('#nullable enable\nusing UnityEngine.UI;\nnamespace ModConfigEnhance.Overview { internal static partial class OverviewUi {\n'+sync+'\n} partial class SchedulerHarness {\n'+refresh+'\n} }',encoding='utf-8')
subprocess.run(['C:/Program Files/dotnet/dotnet.exe','run','--project',str(out/'Checks.csproj'),'-c','Release','-p:EnableDeployment=false'],cwd=r,check=True)
print('RESULT',len(checks),'source contracts',flush=True)
