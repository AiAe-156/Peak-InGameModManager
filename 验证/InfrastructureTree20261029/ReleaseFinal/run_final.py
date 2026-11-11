from pathlib import Path
from datetime import datetime
import subprocess, os, json, re, sys
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).parent / ('verify_'+datetime.now().strftime('%Y%m%d_%H%M%S'))
HERE.mkdir(parents=True,exist_ok=False)
DOTNET='C:/Program Files/dotnet/dotnet.exe'
env=dict(os.environ,PYTHONUTF8='1',PYTHONIOENCODING='utf-8');env['PATH']='C:/Program Files/dotnet;'+env['PATH']
result={'status':'running','started_at':datetime.now().astimezone().isoformat(),'commands':[],'checks':[]}
def run(label,args,build=False):
 p=HERE/(label+'.log')
 with p.open('x',encoding='utf-8') as f:
  r=subprocess.run(args,cwd=ROOT,env=env,stdout=f,stderr=subprocess.STDOUT)
 text=p.read_text(encoding='utf-8',errors='replace')
 result['commands'].append({'label':label,'command':args,'exit':r.returncode,'log':str(p)})
 print(label,r.returncode,flush=True)
 if r.returncode: raise RuntimeError(label+' failed; see '+str(p))
 if build and (re.search(r'\b[1-9]\d* Warning\(s\)',text) or re.search(r'\bwarning [A-Z]+\d+',text,re.I)): raise RuntimeError(label+' has build warnings')
 return text
try:
 run('worker-publish',[DOTNET,'publish','Updater/MCE.UpdateWorker.csproj','-c','Release','--no-restore','-o','Updater/bin/bundle','-p:EnableDeployment=false'],True)
 run('protocol-manifest',[str(ROOT/'Updater/bin/bundle/MCE.UpdateWorker.exe'),'--validate-protocol-manifest',str(ROOT/'Updater/bin/bundle')])
 run('main-build',[DOTNET,'build','ModConfigEnhance.csproj','-c','Release','--no-restore','-p:EnableDeployment=false','-t:Build;CollectReleaseFiles'],True)
 tests=[('Graph21','InfrastructureTree20261029/Graph/Graph.csproj',21),('PlatformFix30','InfrastructureTree20261029/Graph/PlatformFix/PlatformFix.csproj',30),('ScopedFix12','InfrastructureTree20261029/Graph/ScopedFix/ScopedFix.csproj',12),('WorkerGraph26','InfrastructureTree20261029/Final/WorkerGraph/WorkerGraph.csproj',26),('UI36','InfrastructureTree20261029/UI/UiChecks.csproj',36),('Overview28','ComponentActions20261028/概览展示/OverviewChecks.csproj',28),('ShopUi7','IndependentEnvironment20261028/ShopUi/ShopUi.csproj',7),('Shop82','IndependentEnvironment20261028/Shop/Shop.csproj',82),('ShopChecks33','ShopChecks/ShopChecks.csproj',33),('Actions82','ComponentActions20261028/Actions/Actions.csproj',82),('Integration29','IndependentEnvironment20261028/Integration/Integration.csproj',29)]
 for label,project,count in tests:
  project='验证/'+project
  props=['-p:EnableDeployment=false']
  if label=='ScopedFix12': props+=['-p:BaseIntermediateOutputPath=obj-scoped/']
  run(label+'-restore',[DOTNET,'restore',project,*props])
  run(label+'-build',[DOTNET,'build',project,'-c','Release','--no-restore',*props],True)
  text=run(label+'-run',[DOTNET,'run','--project',project,'-c','Release','--no-build','--no-restore',*props])
  if not re.search(r'\b'+str(count)+r'\s+(?:passed|checks|shop checks|shop/action checks)',text,re.I): raise RuntimeError(label+' count requires review; stop')
  result['checks'].append({'name':label,'passed':count})
 project='验证/IndependentEnvironment20261028/CrossFlow/CrossFlow.csproj'
 run('CrossFlow-restore',[DOTNET,'restore',project,'-p:EnableDeployment=false'])
 run('CrossFlow-publish',[DOTNET,'publish',project,'-c','Release','--no-restore','-o','验证/IndependentEnvironment20261028/CrossFlow/release','-p:EnableDeployment=false'],True)
 home=ROOT/'验证/IndependentEnvironment20261028/CrossFlow'
 text=run('CrossFlow44-run',[str(home/'release/MCE.UpdateWorker.exe'),'--bootstrap',str(home)])
 if not re.search(r'\b44\s+(?:passed|checks)',text,re.I): raise RuntimeError('CrossFlow44 count requires review')
 result['checks'].append({'name':'CrossFlow44','passed':44})
 result['status']='verification-passed'
except Exception as e:
 result.update(status='blocked',error=str(e))
finally:
 result['completed_at']=datetime.now().astimezone().isoformat()
 (HERE/'summary.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
 print(str(HERE), result['status'],result.get('error',''),flush=True)
if result['status']!='verification-passed':sys.exit(1)
