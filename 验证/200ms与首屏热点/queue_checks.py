from pathlib import Path
import subprocess
root=Path(__file__).resolve().parents[2];out=Path(__file__).resolve().parent/'QueueChecks';out.mkdir(exist_ok=True)
s=(root/'src/Overview/OverviewUpdateQueue.cs').read_text(encoding='utf-8-sig')
def method(signature):
 start=s.index(signature);end=s.index('{',start)+1;depth=1
 while depth:
  depth+=(s[end]=='{')-(s[end]=='}');end+=1
 return s[start:end]
checks={
 'automatic observe is background': 'Task.Run(() => Observe(' in method('internal static void Reconcile()'),
 'single flight': '_observeTask != null' in method('internal static void Reconcile()'),
 'runtime inputs captured before dispatch': method('internal static void Reconcile()').index('LoadedAssemblyPaths = LoadedPaths()')<method('internal static void Reconcile()').index('Task.Run('),
 'explicit commands share serialization': 'lock(CommandGate)' in method('private static void Post('),
 'worker preparation keeps protocol and path checks': all(x in method('private static string PrepareWorkerTools(string') for x in ['RejectLinks','RequireProtocol','PrepareTools','RecoveryLauncher.Write']),
 'preparation does not hold UI Gate': 'lock(Gate)' not in method('private static string PrepareWorkerTools(string'),
 'worker launch Gate scopes short': 'lock (Gate)' not in method('private static void EnsureWorker(string'),
 'main thread result rejects old context and explicit epoch': 'result.Context == context && result.Epoch ==' in method('internal static void Reconcile()'),
}
for name,ok in checks.items():
 print(('PASS ' if ok else 'FAIL ')+name,flush=True)
assert all(checks.values())
(out/'QueueChecks.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>',encoding='utf-8')
(out/'Observe.g.cs').write_text('using System;using System.Diagnostics;using System.IO;partial class Harness {\n'+method('private static ObserveResult Observe(')+'\n}',encoding='utf-8')
subprocess.run(['C:/Program Files/dotnet/dotnet.exe','run','--project',str(out/'QueueChecks.csproj'),'-c','Release','-p:EnableDeployment=false'],check=True,cwd=root)
