using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using MCE.UpdateWorker;
using ModConfigEnhance.Updating;
int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
void Reject(Action a,string message){bool failed=false;try{a();}catch{failed=true;}Check(failed,message);}
string fixtures=Path.Combine(AppContext.BaseDirectory,"fixtures-"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
string Root(string name){string root=Path.Combine(fixtures,name,"BepInEx");Directory.CreateDirectory(Path.Combine(root,"plugins"));MceSeed.Add(root);return root;}
var handler=new FakeStore();ShopQueue.TestHandler=handler;Files.RunningProbe=_=>false;
string root=Root("install");var queue=new ShopQueue(root);
queue.Handle(new ShopRequest{Operation="preview-install",Namespace="Team",Package="Mod",Version="1.0.0",NewGroupName="Play"});await queue.Pump(new());var preview=queue.Read()!;
Check(preview.State=="preview"&&preview.Packages.Count==2,"dependency preview resolves entire tree");Check(handler.Downloads==0,"preview never downloads ZIP");
Reject(()=>queue.Handle(new ShopRequest{Operation="approve-download",PreviewId=preview.Id,PlanToken="bad"}),"download approval bound to plan token");
queue.Handle(new ShopRequest{Operation="approve-download",PreviewId=preview.Id,PlanToken=preview.PlanToken});await queue.Pump(new());var ready=queue.Read()!;
Check(ready.State=="ready"&&ready.Blockers.Count==0,"download prepares final file preview");Check(handler.Downloads==2,"only approved root and dependency downloaded");
Check(!Directory.Exists(Path.Combine(root,"plugins","Team-Mod")),"preparation leaves installed files untouched");
queue.Handle(new ShopRequest{Operation="confirm-install",PreviewId=ready.Id,PlanToken=ready.PlanToken});Files.RunningProbe=_=>true;await queue.Pump(new());Check(queue.Read()!.State=="waiting-exit","installation waits for game exit");Files.RunningProbe=_=>false;await queue.Pump(new());Check(queue.Read()!.State=="completed","batch installation completes");
Check(File.Exists(Path.Combine(root,"plugins/Team-Mod/mod.txt"))&&File.Exists(Path.Combine(root,"patchers/Team-Lib/hook.txt")),"plugins and patcher resources installed");
var ledger=Files.Read<ShopLedger>(Path.Combine(root,"MCE/shop/installed-unmanaged.json"));Check(ledger.Packages.Count==2,"precise per-package ownership recorded");
queue.Handle(new ShopRequest{Operation="preview-uninstall",Namespace="Team",Package="Lib",Version="1.0.0"});await queue.Pump(new());var dependencyUninstall=queue.Read()!;Check(dependencyUninstall.Blockers.Count==0&&dependencyUninstall.DependencyWarnings.Any(b=>b.Contains("Team-Mod")),"required dependency is a visible warning, not a file blocker");
Reject(()=>queue.Handle(new ShopRequest{Operation="confirm-uninstall",PreviewId=dependencyUninstall.Id,PlanToken=dependencyUninstall.PlanToken}),"dependency uninstall requires explicit warning consent");
Check(File.Exists(Path.Combine(root,"patchers/Team-Lib/hook.txt")),"warning rejection preserves provider files");
queue.Handle(new ShopRequest{Operation="preview-uninstall",Namespace="Team",Package="Mod",Version="1.0.0"});await queue.Pump(new());var uninstall=queue.Read()!;queue.Handle(new ShopRequest{Operation="confirm-uninstall",PreviewId=uninstall.Id,PlanToken=uninstall.PlanToken});await queue.Pump(new());Check(!File.Exists(Path.Combine(root,"plugins/Team-Mod/mod.txt"))&&File.Exists(Path.Combine(root,"patchers/Team-Lib/hook.txt")),"uninstall only removes selected ownership; dependency stays");
Check(queue.Read()!.OrphanCandidates.Single().Key=="Team-Lib","unused automatically installed dependency is suggested not deleted");
Check(ShopQueue.FindOrphanCandidates(new ShopLedger{Packages=new(){new ShopPackage{Key="T-Manual",InstalledAsDependency=false}}}).Count==0,"explicitly installed package never suggested as orphan");
// Interrupted batch restores original index/ledger and does not leave a partial new package.
string rollbackRoot=Root("rollback");var rollback=new ShopQueue(rollbackRoot);rollback.Handle(new ShopRequest{Operation="preview-install",Namespace="Team",Package="Mod",Version="1.0.0"});await rollback.Pump(new());var rp=rollback.Read()!;rollback.Handle(new ShopRequest{Operation="approve-download",PreviewId=rp.Id,PlanToken=rp.PlanToken});await rollback.Pump(new());rp=rollback.Read()!;rollback.Handle(new ShopRequest{Operation="confirm-install",PreviewId=rp.Id,PlanToken=rp.PlanToken});ShopQueue.StepProbe=_=>throw new IOException("fault probe");await rollback.Pump(new());ShopQueue.StepProbe=null;
Check(rollback.Read()!.State=="failed"&&!File.Exists(Path.Combine(rollbackRoot,"plugins/Team-Mod/mod.txt")),"injected commit failure rolls back entire batch");Check(Files.Read<ShopLedger>(Path.Combine(rollbackRoot,"MCE/shop/installed-unmanaged.json")).Packages.Count==0,"ownership rolled back atomically");
Check(ShopQueue.Route("BepInEx/core/Test.dll","T-P")=="core/Test.dll","core separately classified");
Check(ShopQueue.Route("plugins/Assets/a.bundle","T-P")=="plugins/T-P/Assets/a.bundle","plugin resource tree isolated per package");
// Explicit warning consent permits removing a dependency while retaining its consumer.
queue.Handle(new ShopRequest{Operation="preview-install",Namespace="Team",Package="Mod",Version="1.0.0"});await queue.Pump(new());var reinstall=queue.Read()!;
queue.Handle(new ShopRequest{Operation="approve-download",PreviewId=reinstall.Id,PlanToken=reinstall.PlanToken});await queue.Pump(new());reinstall=queue.Read()!;
queue.Handle(new ShopRequest{Operation="confirm-install",PreviewId=reinstall.Id,PlanToken=reinstall.PlanToken});await queue.Pump(new());
queue.Handle(new ShopRequest{Operation="preview-uninstall",Namespace="Team",Package="Lib",Version="1.0.0",IgnoreDependencyWarnings=true});await queue.Pump(new());var forced=queue.Read()!;
Check(forced.DependencyWarnings.Any(w=>w.Contains("Team-Mod")),"forced uninstall still exposes dependent package warning");
queue.Handle(new ShopRequest{Operation="confirm-uninstall",PreviewId=forced.Id,PlanToken=forced.PlanToken});await queue.Pump(new());
Check(queue.Read()!.State=="completed"&&!File.Exists(Path.Combine(root,"patchers/Team-Lib/hook.txt"))&&File.Exists(Path.Combine(root,"plugins/Team-Mod/mod.txt")),"explicit consent uninstalls provider only and keeps dependent mod");
ShopQueue.TestHandler=null;Files.RunningProbe=_=>false;
// Core payload is inspected but never written, even after download consent.
ShopQueue.TestHandler=new FakeStore();
string coreRoot=Root("core");var coreStorage=new PackStorage(coreRoot);coreStorage.Save(new PackIndex());
string coreIndexBefore=File.ReadAllText(coreStorage.IndexPath);
string coreLedgerPath=Path.Combine(coreRoot,"MCE/shop/installed-unmanaged.json");Files.Save(coreLedgerPath,new ShopLedger{Revision=7});string coreLedgerBefore=File.ReadAllText(coreLedgerPath);
var coreQueue=new ShopQueue(coreRoot);coreQueue.Handle(new ShopRequest{Operation="preview-install",Namespace="Team",Package="Core",Version="1.0.0"});await coreQueue.Pump(new());var cp=coreQueue.Read()!;coreQueue.Handle(new ShopRequest{Operation="approve-download",PreviewId=cp.Id,PlanToken=cp.PlanToken});await coreQueue.Pump(new());cp=coreQueue.Read()!;
Check(cp.State=="ready"&&cp.Blockers.Count==0,"standalone core package prepares without modpack migration");
Check(!File.Exists(Path.Combine(coreRoot,"core/Test.dll")),"core preparation does not write live files");
Check(File.ReadAllText(coreStorage.IndexPath)==coreIndexBefore&&File.ReadAllText(coreLedgerPath)==coreLedgerBefore,"core preparation leaves index and ledger unchanged");
coreQueue.Handle(new ShopRequest{Operation="confirm-install",PreviewId=cp.Id,PlanToken=cp.PlanToken});await coreQueue.Pump(new());
{var cr=coreQueue.Read()!;string cerr=(cr.Error??"").Replace("\r"," ").Replace("\n"," ");if(cerr.Length>220)cerr=cerr.Substring(0,220);Check(cr.State=="completed"&&File.Exists(Path.Combine(coreRoot,"core/Test.dll")),"standalone core transaction commits in sandbox without migration [state="+cr.State+" err="+cerr+" blockers="+string.Join(";",cr.Blockers)+" exists="+File.Exists(Path.Combine(coreRoot,"core/Test.dll"))+"]");}
// Editing a frozen preview must not authorize a different download plan.
string tamperRoot=Root("tamper");var tamper=new ShopQueue(tamperRoot);tamper.Handle(new ShopRequest{Operation="preview-install",Namespace="Team",Package="Mod",Version="1.0.0"});await tamper.Pump(new());var tp=tamper.Read()!;tp.Packages[0].Url="https://example.test/replaced.zip";Files.Save(Path.Combine(tamperRoot,"MCE/shop/task.json"),tp);
Reject(()=>tamper.Handle(new ShopRequest{Operation="approve-download",PreviewId=tp.Id,PlanToken=tp.PlanToken}),"approval rejects disk plan mutation");
// Crash during download clears the write block on helper restart.
Files.Save(Path.Combine(tamperRoot,"MCE/shop/task.json"),new ShopTask{Id=Guid.NewGuid().ToString("N"),State="downloading"});var resumed=new ShopQueue(tamperRoot);Check(!resumed.BlocksWrites&&resumed.Read()!.State=="failed","interrupted preparation releases writes without touching live files");
var maintenance=new PackStorage(root);var clean=PackMaintenance.Preview(maintenance,new MaintenanceRequest{Operation=MaintenanceOperations.CleanCache,Scopes=new(){MaintenanceScopes.ShopTasks},WholeCategory=true});
Check(clean.Items.Count>0&&clean.Items.All(i=>i.Path.StartsWith("MCE/shop/tasks/")),"space management lists only completed store tasks");
Check(!PackMaintenance.IsAllowedTarget(maintenance,MaintenanceScopes.ShopTasks,Path.Combine(root,"MCE/shop/installed-unmanaged.json")),"store ownership ledger excluded from cleanup");
Check(!PackMaintenance.IsAllowedTarget(maintenance,MaintenanceScopes.ShopTasks,Path.Combine(root,"plugins/Team-Lib")),"installed mod directory excluded from cache cleanup");
ShopQueue.TestHandler=null;
// Pre-existing independent manifest package supports a safe uninstall preview, not a silent no-op.
string externalRoot=Root("external-uninstall");string externalDir=Path.Combine(externalRoot,"plugins/Team-Existing");Directory.CreateDirectory(externalDir);File.WriteAllText(Path.Combine(externalDir,"manifest.json"),JsonSerializer.Serialize(new{@namespace="Team",name="Existing",version_number="1.0.0",dependencies=Array.Empty<string>()}));File.WriteAllText(Path.Combine(externalDir,"asset.txt"),"existing");
var externalQueue=new ShopQueue(externalRoot);externalQueue.Handle(new ShopRequest{Operation="preview-uninstall",Namespace="Team",Package="Existing",Version="1.0.0"});await externalQueue.Pump(new());
Check(externalQueue.Read()!.State=="uninstall-preview"&&externalQueue.Read()!.Originals.Count==2,"existing manifest package yields explicit uninstall preview");
Check(File.Exists(Path.Combine(externalDir,"asset.txt")),"uninstall preview does not delete any live files");
// Consent is tied to the exact warning set, not an unbounded force flag.
string warningRoot=Root("warning-token");Files.RunningProbe=_=>false;ShopQueue.TestHandler=new FakeStore();var warningQueue=new ShopQueue(warningRoot);
warningQueue.Handle(new ShopRequest{Operation="preview-install",Namespace="Team",Package="Mod",Version="1.0.0"});await warningQueue.Pump(new());var wt=warningQueue.Read()!;
warningQueue.Handle(new ShopRequest{Operation="approve-download",PreviewId=wt.Id,PlanToken=wt.PlanToken});await warningQueue.Pump(new());wt=warningQueue.Read()!;
warningQueue.Handle(new ShopRequest{Operation="confirm-install",PreviewId=wt.Id,PlanToken=wt.PlanToken});await warningQueue.Pump(new());
warningQueue.Handle(new ShopRequest{Operation="preview-uninstall",Namespace="Team",Package="Lib",Version="1.0.0"});await warningQueue.Pump(new());wt=warningQueue.Read()!;
Reject(()=>warningQueue.Handle(new ShopRequest{Operation="confirm-uninstall",PreviewId=wt.Id,PlanToken=wt.PlanToken,IgnoreDependencyWarnings=true,DependencyWarningToken="changed"}),"changed warning consent token is rejected");
warningQueue.Handle(new ShopRequest{Operation="confirm-uninstall",PreviewId=wt.Id,PlanToken=wt.PlanToken,IgnoreDependencyWarnings=true,DependencyWarningToken=ShopQueue.DependencyWarningToken(wt.DependencyWarnings)});await warningQueue.Pump(new());
Check(warningQueue.Read()!.State=="completed","exact warning list consent permits forced uninstall");ShopQueue.TestHandler=null;
// A known non-MCE core package can be removed without migrating the whole pack.
string coreUninstallRoot=Root("core-uninstall-owned");string coreUninstallDir=Path.Combine(coreUninstallRoot,"core/Team-Optional");Directory.CreateDirectory(coreUninstallDir);
string coreOptional=Path.Combine(coreUninstallDir,"resource.txt");File.WriteAllText(coreOptional,"owned");
Files.Save(Path.Combine(coreUninstallRoot,"MCE/shop/installed-unmanaged.json"),new ShopLedger{Packages=new(){new ShopPackage{Key="Team-Optional",Version="1.0.0",Files=new(){new PackFile{Path="core/Team-Optional/resource.txt",Hash=Files.Hash(coreOptional)}}}}});
File.WriteAllText(Path.Combine(coreUninstallDir,"manifest.json"),JsonSerializer.Serialize(new{@namespace="Team",name="Optional",version_number="1.0.0",dependencies=Array.Empty<string>()}));
var coreUninstallQueue=new ShopQueue(coreUninstallRoot);coreUninstallQueue.Handle(new ShopRequest{Operation="preview-uninstall",Namespace="Team",Package="Optional",Version="1.0.0"});await coreUninstallQueue.Pump(new());var optional=coreUninstallQueue.Read()!;
Check(optional.State=="uninstall-preview"&&optional.Blockers.Count==0,"owned optional core uninstall is independent of complete-pack migration");
coreUninstallQueue.Handle(new ShopRequest{Operation="confirm-uninstall",PreviewId=optional.Id,PlanToken=optional.PlanToken});await coreUninstallQueue.Pump(new());
Check(coreUninstallQueue.Read()!.State=="completed"&&!File.Exists(coreOptional),"optional core exact owned payload uninstalled in sandbox");
string lockRoot=Root("worker-idle-lock");using(var idleWorker=new QueueWorker(lockRoot)){using(var uiLease=InstallationWriteLease.Acquire(lockRoot)){Check(true,"idle worker instance does not reserve installation write lock against UI save");}Reject(()=>{using var secondWorker=new QueueWorker(lockRoot);},"worker singleton remains protected by separate lifetime lock");}
string controlRoot=Root("worker-busy-control");using(var controlWorker=new QueueWorker(controlRoot))
{
 bool singletonTyped=false;try{using var second=InstallationWriteLease.AcquireWorkerInstance(controlRoot);}catch(InstallationWriteLease.WorkerAlreadyRunningException){singletonTyped=true;}Check(singletonTyped,"duplicate singleton has distinct non-write-lock outcome");
 var job=new UpdateJob{Id=Guid.NewGuid().ToString("N"),State=UpdateStates.Queued};job.WorkDirectory=Path.Combine(controlRoot,"MCE/jobs",job.Id);controlWorker.Current.Jobs.Add(job);
 string commandId=Guid.NewGuid().ToString("N");Files.Save(Path.Combine(controlRoot,"MCE/commands",commandId+".json"),new UpdateCommand{Id=commandId,Action="pause",JobId=job.Id});
 using(var writes=InstallationWriteLease.Acquire(controlRoot)){controlWorker.ProcessCommands();Check(File.Exists(Path.Combine(controlRoot,"MCE/command-receipts",commandId+".json")),"control accepted while installation writer busy");Check(!File.Exists(Path.Combine(controlRoot,"MCE/command-rejections",commandId+".json")),"busy writer does not reject durable control");Check(File.Exists(Path.Combine(job.WorkDirectory,"control.json")),"control remains durable until write lock available");}
 await controlWorker.Run(true);Check(job.State==UpdateStates.Paused,"saved pause applied after writer releases");
}
// Legacy saved-resource batches are accepted for compatibility and run as queue jobs; the batch executor itself is gone.
string batchRoot=Root("saved-resource-batch");ShopQueue.TestHandler=new FakeStore();Files.RunningProbe=_=>false;
using(var batchWorker=new QueueWorker(batchRoot))
{
 string id=Guid.NewGuid().ToString("N");Files.Save(Path.Combine(batchRoot,"MCE/commands",id+".json"),new UpdateCommand{Id=id,Action="resource-batch",ResourcePackId="",ResourceCommands=new(){new UpdateCommand{Id=Guid.NewGuid().ToString("N"),Action="shop",Shop=new ShopRequest{Operation="preview-install",Namespace="Team",Package="Mod",Version="1.0.0"}}}});
 batchWorker.ProcessCommands();
 Check(batchWorker.Current.Jobs.Count==1&&batchWorker.Current.Jobs[0].Install,"legacy resource batch items become queue jobs");
 for(int i=0;i<8;i++){await batchWorker.Run(true);if(batchWorker.Current.Jobs.All(j=>!UpdateStates.Pending(j.State)||j.State==UpdateStates.PendingRestart))break;await Task.Delay(1200);}
 Check(File.Exists(Path.Combine(batchRoot,"plugins/Team-Mod/mod.txt")),"legacy resource batch completes through the install core");
 Check(!File.Exists(Path.Combine(batchRoot,"MCE/resource-batch.json")),"no batch executor state is written any more");
}
ShopQueue.TestHandler=null;
// Real worker command ingress, rather than direct ShopQueue-only tests.
string commandRoot=Root("worker-command");using(var worker=new QueueWorker(commandRoot)){
 string commandId=Guid.NewGuid().ToString("N");Files.Save(Path.Combine(commandRoot,"MCE/commands",commandId+".json"),new UpdateCommand{Id=commandId,Action="shop",Shop=new ShopRequest{Operation="preview-uninstall",Namespace="Team",Package="Missing",Version="1.0.0"}});worker.ProcessCommands();
 Check(worker.Current.Jobs.Count==1&&worker.Current.Jobs[0].Uninstall,"production worker turns a store uninstall into a queue job");
 await worker.Run(true);var missing=worker.Current.Jobs[0];Check(missing.State==UpdateStates.Failed&&missing.Error.Length>0,"uninstalling a package that is not installed fails with a concrete reason");
}
string parallelRoot=Root("independent-failure-isolation");ShopQueue.TestHandler=new FakeStore();
using(var parallelWorker=new QueueWorker(parallelRoot)){
 string requestId=Guid.NewGuid().ToString("N"),bad=Guid.NewGuid().ToString("N"),good=Guid.NewGuid().ToString("N");
 Files.Save(Path.Combine(parallelRoot,"MCE/commands",requestId+".json"),new UpdateCommand{Id=requestId,Action="update-batch",ResourceCommands=new(){new(){Id=bad,Action="shop",Shop=new(){Operation="preview-uninstall",Namespace="Team",Package="Missing",Version="1.0.0"}},new(){Id=good,Action="shop",Shop=new(){Operation="preview-install",Namespace="Team",Package="Mod",Version="1.0.0"}}}});parallelWorker.ProcessCommands();
 // Both items are queue jobs now. Mod depends on Lib, which is auto-queued first; the missing uninstall fails on its own.
 for(int i=0;i<10;i++){await parallelWorker.Run(true);if(parallelWorker.Current.Jobs.Any(j=>j.Id==good&&!UpdateStates.Pending(j.State)||j.Id==good&&j.State==UpdateStates.PendingRestart))break;await Task.Delay(1200);}
 Check(File.Exists(Path.Combine(parallelRoot,"plugins/Team-Mod/mod.txt")),"failed independent uninstall never stops another install");
 var failed=parallelWorker.Current.Jobs.Single(j=>j.Id==bad);var succeeded=parallelWorker.Current.Jobs.Single(j=>j.Id==good);
 Check(failed.State==UpdateStates.Failed&&succeeded.State==UpdateStates.PendingRestart,"per-operation failure and success are persisted separately");
}
string overlapRoot=Root("parallel-downloads");var overlapHandler=new OverlapStore();ShopQueue.TestHandler=overlapHandler;Files.RunningProbe=_=>true;
using(var parallelWorker=new QueueWorker(overlapRoot)){
 string id=Guid.NewGuid().ToString("N");Files.Save(Path.Combine(overlapRoot,"MCE/commands",id+".json"),new UpdateCommand{Id=id,Action="update-batch",ResourceCommands=new(){new(){Id=Guid.NewGuid().ToString("N"),Action="shop",Shop=new(){Operation="preview-install",Namespace="Team",Package="Core",Version="1.0.0"}},new(){Id=Guid.NewGuid().ToString("N"),Action="shop",Shop=new(){Operation="preview-install",Namespace="Team",Package="Lib",Version="1.0.0"}}}});parallelWorker.ProcessCommands();
 await parallelWorker.Run(true);
 Check(overlapHandler.Maximum>=2,"independent package downloads actually overlap");Check(File.Exists(Path.Combine(overlapRoot,"core/Test.dll")),"a brand-new core package installs without waiting for exit because nothing loaded is touched");
}
Files.RunningProbe=_=>false;ShopQueue.TestHandler=null;
Console.WriteLine($"PASS {checks} shop checks; fixtures: {fixtures}");
sealed class UpdateFixtureStore(string assemblyPath):HttpMessageHandler
{
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){string url=request.RequestUri!.AbsoluteUri;string version=url.Contains("2.0.0")?"2.0.0":"1.0.0";if(url.Contains("/api/"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{version_number=version,download_url="https://example.test/Update-"+version+".zip",dependencies=Array.Empty<string>()}))});using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true)){using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open()))writer.Write(JsonSerializer.Serialize(new{name="Update",version_number=version,dependencies=Array.Empty<string>()}));using var output=zip.CreateEntry("Provider.dll").Open();output.Write(File.ReadAllBytes(assemblyPath));}return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(stream.ToArray())});}
}
sealed class OverlapStore:HttpMessageHandler
{
 readonly HttpMessageInvoker inner=new(new FakeStore());int active;public int Maximum;
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){int count=Interlocked.Increment(ref active);Maximum=Math.Max(Maximum,count);try{await Task.Delay(70,token);return await inner.SendAsync(request,token);}finally{Interlocked.Decrement(ref active);}}
}
sealed class FakeStore:HttpMessageHandler
{
 public int Downloads;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
 {
  string url=request.RequestUri!.AbsoluteUri;
  if(url.Contains("/api/experimental/package/")){bool root=url.Contains("/Mod/");string name=root?"Mod":url.Contains("/Core/")?"Core":"Lib";var payload=JsonSerializer.Serialize(new{version_number="1.0.0",download_url="https://example.test/"+name+".zip",dependencies=root?new[]{"Team-Lib-1.0.0"}:Array.Empty<string>()});return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(payload)});}
  Downloads++;bool isMod=url.EndsWith("Mod.zip"),isCore=url.EndsWith("Core.zip");using var ms=new MemoryStream();using(var zip=new ZipArchive(ms,ZipArchiveMode.Create,true)){void Add(string path,string value){using var writer=new StreamWriter(zip.CreateEntry(path).Open());writer.Write(value);}Add("manifest.json",JsonSerializer.Serialize(new{name=isMod?"Mod":isCore?"Core":"Lib",version_number="1.0.0",dependencies=isMod?new[]{"Team-Lib-1.0.0"}:Array.Empty<string>()}));Add(isMod?"mod.txt":isCore?"BepInEx/core/Test.dll":"patchers/Team-Lib/hook.txt","payload");}
  var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(ms.ToArray())};return Task.FromResult(response);
 }
}
