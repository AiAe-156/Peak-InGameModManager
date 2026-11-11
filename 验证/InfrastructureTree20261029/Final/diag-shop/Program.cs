using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using MCE.UpdateWorker;
using ModConfigEnhance.Updating;
int checks=0;
void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);checks++;}
void Reject(Action action,string message){bool rejected=false;try{action();}catch(IOException){rejected=true;}Check(rejected,message);}
Files.RunningProbe=_=>false;
PackCompleteEnvironment.ManagementProtocolCheck=(_,_)=>{};
var fake=new FakeStore();ShopQueue.TestHandler=fake;
string fixtures=Path.Combine(AppContext.BaseDirectory,"fixtures-"+Guid.NewGuid().ToString("N"));
string Root(string name,bool complete=true){string root=Path.Combine(fixtures,name,"BepInEx");Directory.CreateDirectory(root);MceSeed.Add(root);var store=new PackStorage(root);var pack=new PackRecord{Id=Guid.NewGuid().ToString("N"),Content="revisions/"+Guid.NewGuid().ToString("N"),EnvironmentVersion=complete?2:1,MinimumWorkerProtocol=complete?2:1};Directory.CreateDirectory(store.Content(pack));if(complete){new PackRevisionState(store).Write(pack,new());Files.Save(Path.Combine(store.Workspace(pack.Id),"shop-installed.json"),new ShopLedger());}store.Save(new PackIndex{ActivePackId=pack.Id,Packs=new(){pack}});return root;}
async Task<ShopTask> Preview(ShopQueue q,string name,string version="1.0.0",bool uninstall=false){q.Handle(new(){Operation=uninstall?"preview-uninstall":"preview-install",Namespace="Team",Package=name,Version=version});await q.Pump(new());return q.Read()!;}
async Task<ShopTask> Approve(ShopQueue q,ShopTask t){q.Handle(new(){Operation="approve-download",PreviewId=t.Id,PlanToken=t.PlanToken});await q.Pump(new());return q.Read()!;}
async Task<ShopTask> Commit(ShopQueue q,ShopTask t,bool uninstall=false){q.Handle(new(){Operation=uninstall?"confirm-uninstall":"confirm-install",PreviewId=t.Id,PlanToken=t.PlanToken});await q.Pump(new());return q.Read()!;}
ShopLedger Ledger(string root){var store=new PackStorage(root);return Files.Read<ShopLedger>(Path.Combine(store.Workspace(store.Read().ActivePackId),"shop-installed.json"));}
string root=Root("batch");var queue=new ShopQueue(root);var original=new PackStorage(root).Read().Packs.Single();string stateHash=original.RevisionStateHash;int downloads=fake.Downloads;
var t=await Preview(queue,"Mod");Check(t.State=="preview"&&t.Packages.Count==3,"core + patcher dependency graph resolved");Check(fake.Downloads==downloads,"preview no archive download");t=await Approve(queue,t);Check(t.State=="ready"&&t.Blockers.Count==0,"core + patcher staged as one batch");Check(!File.Exists(Path.Combine(root,"core/Team-Core/Library.dll")),"prepare never writes installed core");t=await Commit(queue,t);Check(t.State=="completed","batch committed");Check(Ledger(root).Packages.Count==3,"all ownership in current-pack ledger");Check(new PackStorage(root).Read().ActiveFiles.Any(f=>f.Path.StartsWith("core/")),"ActiveFiles includes core");Check(original.RevisionStateHash==stateHash&&new PackRevisionState(new(root)).Read(original)!.Packages.Count==0,"saved revision-state remains immutable");
t=await Preview(queue,"Core","2.0.0");Check(t.Action==ComponentAction.Upgrade,"upgrade action exact target");t=await Approve(queue,t);Check(t.Blockers.Count==0,"core upgrade plan validates final graph");t=await Commit(queue,t);Check(t.State=="completed"&&Ledger(root).Packages.Single(p=>p.Key=="Team-Core").Version=="2.0.0","core upgraded");
t=await Preview(queue,"Core","1.0.0");Check(t.Action==ComponentAction.Downgrade,"downgrade explicit");t=await Approve(queue,t);t=await Commit(queue,t);Check(t.State=="completed","permitted downgrade commits");
t=await Preview(queue,"Core");Check(t.Action==ComponentAction.Reinstall,"same-version reinstall explicit");t=await Approve(queue,t);t=await Commit(queue,t);Check(t.State=="completed","same-version reinstall commits");
t=await Preview(queue,"Core",uninstall:true);Check(t.Blockers.Any(b=>b.Contains("Team-Mod")),"reverse dependency blocks core uninstall");
t=await Preview(queue,"Mod",uninstall:true);t=await Commit(queue,t,true);Check(t.State=="completed","remove root retaining dependencies");
t=await Preview(queue,"Core",uninstall:true);Check(t.Blockers.Count==0,"unused ledger core uninstall preview");t=await Commit(queue,t,true);Check(t.State=="completed"&&!File.Exists(Path.Combine(root,"core/Team-Core/Library.dll")),"ledger core uninstalled exact files");
// Legacy index schema2 is not proof of an active complete record.
string legacy=Root("legacy",false);var legacyStore=new PackStorage(legacy);var legacyIndex=legacyStore.Read();legacyIndex.SchemaVersion=2;legacyIndex.MinimumWorkerProtocol=2;legacyStore.Save(legacyIndex);var lq=new ShopQueue(legacy);t=await Preview(lq,"Core");t=await Approve(lq,t);Check(t.State=="failed"&&t.Error.Contains("Complete the current pack"),"schema2 legacy active record cannot authorize core");
// Pre-existing core ownership requires original official archive consent.
string ext=Root("external");Directory.CreateDirectory(Path.Combine(ext,"core/Team-Core"));File.WriteAllBytes(Path.Combine(ext,"core/Team-Core/Library.dll"),FakeStore.Payload("1.0.0"));File.WriteAllText(Path.Combine(ext,"core/Team-Core/manifest.json"),FakeStore.Manifest("Core","1.0.0",Array.Empty<string>()));File.WriteAllText(Path.Combine(ext,"core/Team-Core/notes.txt"),"user notes");var eq=new ShopQueue(ext);downloads=fake.Downloads;t=await Preview(eq,"Core","2.0.0");Check(t.VerificationPackages.Single().Version=="1.0.0"&&fake.Downloads==downloads,"old official ZIP and URL visible before consent, no download");t=await Approve(eq,t);Check(t.State=="ready"&&t.Blockers.Count==0,"official old files mapped and compared");Check(t.PreviousPackages.Single().Files.All(f=>!f.Path.EndsWith("notes.txt")),"extra ordinary file not claimed");t=await Commit(eq,t);Check(t.State=="completed"&&File.ReadAllText(Path.Combine(ext,"core/Team-Core/notes.txt"))=="user notes","upgrade preserves extra ordinary material");
string stale=Root("stale");var sq=new ShopQueue(stale);t=await Preview(sq,"Mod");var ss=new PackStorage(stale);ss.Save(ss.Read());Reject(()=>sq.Handle(new(){Operation="approve-download",PreviewId=t.Id,PlanToken=t.PlanToken}),"index revision expiry invalidates consent");
string tamper=Root("tampered");Directory.CreateDirectory(Path.Combine(tamper,"core/Team-Core"));File.WriteAllBytes(Path.Combine(tamper,"core/Team-Core/Library.dll"),new byte[]{1,2});File.WriteAllText(Path.Combine(tamper,"core/Team-Core/manifest.json"),FakeStore.Manifest("Core","1.0.0",Array.Empty<string>()));var tq=new ShopQueue(tamper);t=await Preview(tq,"Core","2.0.0");t=await Approve(tq,t);Check(t.State=="failed"&&File.ReadAllBytes(Path.Combine(tamper,"core/Team-Core/Library.dll")).Length==2,"modified core blocked and retained");
string rollback=Root("rollback");var rq=new ShopQueue(rollback);t=await Preview(rq,"Mod");t=await Approve(rq,t);ShopQueue.StepProbe=_=>throw new IOException("injected crash");t=await Commit(rq,t);ShopQueue.StepProbe=null;Check(t.State=="failed"&&!File.Exists(Path.Combine(rollback,"core/Team-Core/Library.dll")),"batch failure restores core and patcher");Check(Ledger(rollback).Packages.Count==0&&new PackStorage(rollback).Read().ActiveFiles.Count==0,"rollback restores ledger and ActiveFiles");
// Same-hash references are legal under A ValidateLedger; uninstall must not remove another owner.
//
//
//
// Original-version uninstall also requires consent, then a distinct final confirmation.
string old=Root("old-uninstall");Directory.CreateDirectory(Path.Combine(old,"core/Team-Core"));File.WriteAllBytes(Path.Combine(old,"core/Team-Core/Library.dll"),FakeStore.Payload("1.0.0"));File.WriteAllText(Path.Combine(old,"core/Team-Core/manifest.json"),FakeStore.Manifest("Core","1.0.0",Array.Empty<string>()));var oq=new ShopQueue(old);downloads=fake.Downloads;t=await Preview(oq,"Core",uninstall:true);Check(t.State=="preview"&&t.Uninstall&&fake.Downloads==downloads,"old core uninstall requests explicit verification download consent");t=await Approve(oq,t);Check(t.State=="uninstall-preview"&&t.Blockers.Count==0,"verified old core uninstall still needs final confirmation");t=await Commit(oq,t,true);Check(t.State=="completed"&&!File.Exists(Path.Combine(old,"core/Team-Core/Library.dll")),"verified old core uninstall removes only exact official files");
string unknown=Root("unknown-runtime");Directory.CreateDirectory(Path.Combine(unknown,"core/Team-Core"));File.WriteAllBytes(Path.Combine(unknown,"core/Team-Core/Library.dll"),FakeStore.Payload("1.0.0"));File.WriteAllText(Path.Combine(unknown,"core/Team-Core/manifest.json"),FakeStore.Manifest("Core","1.0.0",Array.Empty<string>()));File.WriteAllBytes(Path.Combine(unknown,"core/Team-Core/unknown.dll"),new byte[]{9});var uq=new ShopQueue(unknown);t=await Preview(uq,"Core","2.0.0");t=await Approve(uq,t);Check(t.State=="failed"&&t.Error.Contains("Unknown core runtime"),"unknown core runtime blocks claiming rather than directory hashing");
string cross=Root("cross-pack");var cq=new ShopQueue(cross);t=await Preview(cq,"Mod");var cs=new PackStorage(cross);var ci=cs.Read();var other=new PackRecord{Id=Guid.NewGuid().ToString("N"),Content="revisions/"+Guid.NewGuid().ToString("N"),EnvironmentVersion=2,MinimumWorkerProtocol=2};Directory.CreateDirectory(cs.Content(other));new PackRevisionState(cs).Write(other,new());ci.Packs.Add(other);ci.ActivePackId=other.Id;cs.Save(ci);Reject(()=>cq.Handle(new(){Operation="approve-download",PreviewId=t.Id,PlanToken=t.PlanToken}),"cross-pack preview cannot authorize downloads or writes");Check(new PackRevisionState(cs).Read(other)!.Packages.Count==0,"other pack immutable ledger unchanged");
// Crash after core placement: game restart suspends rollback, reconstructed helper then restores all.
string crash=Root("crash-recovery");var crq=new ShopQueue(crash);t=await Preview(crq,"Mod");t=await Approve(crq,t);ShopQueue.StepProbe=step=>{if(step.StartsWith("placed:core/")){Files.RunningProbe=_=>true;throw new IOException("crash after core write");}};t=await Commit(crq,t);ShopQueue.StepProbe=null;Check(t.State=="recovery-required","restart suspends recovery without further live writes");Files.RunningProbe=_=>false;crq=new ShopQueue(crash);await crq.Pump(new());Check(crq.Read()!.State=="failed"&&Ledger(crash).Packages.Count==0&&!File.Exists(Path.Combine(crash,"core/Team-Core/Library.dll")),"new helper recovers core + patcher transaction from journal");
// Final assembly references include new core and patcher together, not the old core only.
string graph=Root("final-graph"),stage=Path.Combine(graph,"MCE/test-stage");
void Assembly(string file,string name,int version,string? reference=null,int required=1){Directory.CreateDirectory(Path.GetDirectoryName(file)!);using var a=Mono.Cecil.AssemblyDefinition.CreateAssembly(new(name,new Version(version,0)),name,Mono.Cecil.ModuleKind.Dll);if(reference!=null)a.MainModule.AssemblyReferences.Add(new(reference,new Version(required,0)));a.Write(file);}
Assembly(Path.Combine(stage,"core/Lib.dll"),"Lib",2);Assembly(Path.Combine(stage,"patchers/Patcher.dll"),"Patcher",1,"Lib",2);
var desired=new List<PackFile>{new(){Path="core/Lib.dll",Hash=Files.Hash(Path.Combine(stage,"core/Lib.dll"))},new(){Path="patchers/Patcher.dll",Hash=Files.Hash(Path.Combine(stage,"patchers/Patcher.dll"))}};
Check(ShopDependencyValidation.Check(graph,stage,new List<PackFile>(),desired).Count==0,"same-batch core satisfies new patcher assembly minimum");Assembly(Path.Combine(stage,"core/Lib.dll"),"Lib",1);desired[0].Hash=Files.Hash(Path.Combine(stage,"core/Lib.dll"));Check(ShopDependencyValidation.Check(graph,stage,new List<PackFile>(),desired).Any(i=>i.Contains("Lib")),"final graph blocks core lower than same-batch patcher requirement");
checks+=await LedgerLossChecks.Run(Path.Combine(fixtures,"ledger-loss"));
Console.WriteLine(checks+" passed");
sealed class FakeStore:HttpMessageHandler
{
 internal int Downloads;
 internal static byte[] Payload(string version)=>NativePeFixture.Bytes("native-core-fixture-"+version);
 internal static string Manifest(string name,string version,string[] deps)=>JsonSerializer.Serialize(new{name,version_number=version,dependencies=deps});
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
 {
  string path=request.RequestUri!.AbsolutePath;HttpContent body;
  if(path.StartsWith("/zip/")){
   Downloads++;var parts=path.Split('/');string name=parts[2],version=parts[3];using var memory=new MemoryStream();
   using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)){
    void Add(string name,byte[] data){using var s=zip.CreateEntry(name).Open();s.Write(data);}
    Add("manifest.json",Encoding.UTF8.GetBytes(Manifest(name,version,Dependencies(name))));
    Add(name=="Core"?"core/Team-Core/Library.dll":name=="Patcher"?"patchers/Team-Patcher/hook.txt":"mod.txt",name=="Core"?Payload(version):Encoding.UTF8.GetBytes("fixture "+version));
   }
   body=new ByteArrayContent(memory.ToArray());
  }else{
   var parts=path.Split('/',StringSplitOptions.RemoveEmptyEntries);string name=parts[^2],version=parts[^1];
   body=new StringContent(JsonSerializer.Serialize(new{name,@namespace="Team",version_number=version,download_url="https://fake.test/zip/"+name+"/"+version,dependencies=Dependencies(name)}));
  }
  return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=body});
 }
 static string[] Dependencies(string name)=>name=="Mod"?new[]{"Team-Core-1.0.0","Team-Patcher-1.0.0"}:Array.Empty<string>();
 protected override void Dispose(bool disposing){}
}
