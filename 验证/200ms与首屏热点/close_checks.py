from pathlib import Path
import subprocess
r=Path(__file__).resolve().parents[2];out=Path(__file__).resolve().parent
read=lambda p:(r/p).read_text(encoding='utf-8-sig')
i=read('src/Overview/OverviewListView.Infrastructure.cs');c=read('src/Overview/OverviewController.cs');v=read('src/Overview/OverviewListView.cs');q=read('src/Overview/OverviewUpdateQueue.cs');t=read('src/Overview/OverviewPage.Toolbar.cs')
callback=i[i.index('}).ContinueWith(task'):i.index('private void FlushInfrastructureRedraw')]
checks={
 'toolbar does not evaluate factory on entry':'QueueExternalHint(target,text)' in c and 'ShowExternalHint(text())' not in c,
 'toolbar refresh preserves pending deadline':'RefreshExternalHint(_activeToolHintTarget,_activeToolHintText)' in t,
 'controller immediate path':'QueueExternalHint(target,text,true)' in c,
 'modal and closure cancel factory':'if(OverviewUi.HasModal)HideAllHints()' in v and '_factoryHint.Cancel();_hintDwell.Cancel();' in v,
 'inactive target guarded':'go.activeInHierarchy' in v,
 'metadata published before graph collection':i.index('_infrastructure = rows; _infrastructureLoading = false')<i.index('return InfrastructureDependencyCollector.Collect'),
 'graph completion has no Render':'Render(' not in callback,
 'graph failure preserves metadata':'_infrastructure = Array.Empty' not in callback,
 'graph completion invalidates displayed hints':'_dependencyHints.HideNow(); _dependencyHints.InvalidateDisplay();' in callback,
 'generation context guard both publications':i.count('generation != _infrastructureGeneration')==2 and i.count('revision != OverviewPageCache.Inventory || context != SourceUi.DependencyContext')==2,
 'protection unknown stays locked':'!package.IdentityConfirmed || !diagnostic.CanUninstall' in i,
 'protection fixed title reserve':'nameWidth - 24' in i and 'lockedState' not in i,
 'old protection bindings cleared on new row tree':'_componentProtectionRefresh.Clear();' in i,
 'only executed result changes observe error':'if(result.Executed) { _observeError=result.Error;' in q,
 'read error separated':'_queueError=value.Error' in q and '_error=_queueError="Could not read' in q,
 'completion does not clear local error':'_localError = _error = result.Error' not in q,
 'diagnostics do not replace live context':'var active=PackLibrary.Active;' in read('src/Overview/SourceUi.cs') and 'finally { UiPerfProbe.DependencyContext(at); }' in read('src/Overview/SourceUi.cs'),
}
for n,ok in checks.items():print(('PASS ' if ok else 'FAIL ')+n,flush=True)
assert all(checks.values());print('RESULT',len(checks),'close wiring contracts passed',flush=True)
# Execute the actual completion block, with both real-reader and automatic error sources.
a=q.index('if(result.Executed)');b=q.index('\n                }',a);block=q[a:b]
h=out/'CompletionChecks';h.mkdir(exist_ok=True)
(h/'CompletionChecks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>',encoding='utf-8')
(h/'Production.cs').write_text('using System;partial class Check {static string _observeError="",_error="",_localError="",_queueError="";static DateTime _pollAt;class Result {internal bool Executed;internal string Error="";}static void Complete(Result result){'+block+'}}',encoding='utf-8')
subprocess.run(['C:/Program Files/dotnet/dotnet.exe','run','--project',str(h/'CompletionChecks.csproj'),'-c','Release','-p:EnableDeployment=false'],check=True,cwd=r)
