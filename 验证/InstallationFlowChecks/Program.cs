using MCE.UpdateWorker;
using ModConfigEnhance.Updating;
using Mono.Cecil;
using System.Text.Json;

int checks = 0;
void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
string home = Path.Combine(Path.GetTempPath(), "mce-checks", "IF-" + Guid.NewGuid().ToString("N").Substring(0,8)); Directory.CreateDirectory(home);
string Root(string name) { string p = Path.Combine(home, name, "BepInEx"); Directory.CreateDirectory(Path.Combine(p, "plugins")); return p; }
string Make(string name, string guid) {
    string p = Path.Combine(home, name + ".dll");
    using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, new Version(1,0)), name, ModuleKind.Dll);
    var m = a.MainModule; var t = new TypeDefinition("Fixture", "Plugin", TypeAttributes.Public, m.TypeSystem.Object); m.Types.Add(t);
    var scope = new AssemblyNameReference("BepInEx", new Version(5,4,23,0)); m.AssemblyReferences.Add(scope);
    var ctor = new MethodReference(".ctor", m.TypeSystem.Void, new TypeReference("BepInEx", "BepInPlugin", m, scope)) { HasThis = true };
    for (int i=0;i<3;i++) ctor.Parameters.Add(new ParameterDefinition(m.TypeSystem.String));
    var attr = new CustomAttribute(ctor); foreach(string value in new[]{guid,name,"1.0.0"}) attr.ConstructorArguments.Add(new CustomAttributeArgument(m.TypeSystem.String,value)); t.CustomAttributes.Add(attr); a.Write(p); return p;
}
Files.RunningProbe = _ => false;
string root = Root("install"); string source = Make("Example", "test.example"); var queue = new ShopQueue(root);
queue.HandleLocalImport(new LocalImportRequest { SourcePath=source, PackageFolder="Example" }); var task = queue.Read()!;
Check(task.State=="ready" && task.Blockers.Count==0,"local preview ready: "+string.Join(";",task.Blockers));
Check(!File.Exists(Path.Combine(root,"plugins","Example","Example.dll")),"preview never installs");
Check(task.Packages.Single().Key.StartsWith("local:"),"local provenance not forged");
queue.HandleLocalImport(new LocalImportRequest {Operation="confirm",PreviewId=task.Id,PlanToken=task.PlanToken});
Files.RunningProbe=_=>true; await queue.Pump(new()); Check(queue.Read()!.State=="waiting-exit","wait game exit");
Files.RunningProbe=_=>false; await queue.Pump(new()); Check(queue.Read()!.State=="completed","local commit: "+queue.Read()!.Error);
Check(File.Exists(Path.Combine(root,"plugins","Example","Example.dll")),"DLL placed");
string ledgerPath=Path.Combine(root,"MCE","shop","installed-unmanaged.json"); var ledger=Files.Read<ShopLedger>(ledgerPath); Check(ledger.Packages.Count==1,"ownership committed");
// Folder organization: preview is a read-only plan, confirm becomes a move job that runs in the queue.
using (var layoutWorker = new QueueWorker(root))
{
    void Post(LayoutQueueRequest request){string id=Guid.NewGuid().ToString("N");Files.Save(Path.Combine(root,"MCE","commands",id+".json"),new UpdateCommand{Id=id,Action="layout",Layout=request});layoutWorker.ProcessCommands();}
    Post(new LayoutQueueRequest { Mode=InstallationLayoutMode.GroupAndTranslatedOuterFolder,
        Groups=new(){new(){Id="tools",Name="工具"}},Mods=new(){new(){Guid="test.example",GroupId="tools",Name="示例模组"}} });
    task=Files.Read<ShopTask>(Path.Combine(root,"MCE","shop","task.json"));
    Check(task.State=="ready"&&task.Blockers.Count==0,"layout ready: "+string.Join(";",task.Blockers));
    string target=task.Desired.Single().Path; Check(target.Contains("工具/")&&target.Contains("示例模组--"),"group folder uses its name; translated outer folder stays hashed");
    Post(new LayoutQueueRequest{Operation="confirm",PreviewId=task.Id,PlanToken=task.PlanToken});
    var moveJob=layoutWorker.Current.Jobs.Single(j=>j.Layout);
    Check(moveJob.Layout&&moveJob.TargetVersion=="1","layout confirm becomes a move job");
    await layoutWorker.Run(true);
    Check(moveJob.State==UpdateStates.PendingRestart,moveJob.State+" "+moveJob.Error);
    Check(File.Exists(Files.Child(root,target))&&!File.Exists(Path.Combine(root,"plugins","Example","Example.dll")),"move preserves filename and removes source");
    ledger=Files.Read<ShopLedger>(ledgerPath); Check(ledger.Packages.Single().Files.Single().Path==target,"ledger paths updated");
    var stablePackage=ledger.Packages.Single();Check(stablePackage.LayoutGroupFolder.Length>0&&stablePackage.LayoutOuterFolder.Length>0,"stable folder mapping persisted");
    Post(new LayoutQueueRequest{Mode=InstallationLayoutMode.GroupAndTranslatedOuterFolder,Groups=new(){new(){Id="tools",Name="Tools"}},Mods=new(){new(){Guid="test.example",GroupId="tools",Name="Example Mod"}}});
    Check(Files.Read<ShopTask>(Path.Combine(root,"MCE","shop","task.json")).Desired.Count==0,"language changes do not rename stable folders");
    Post(new LayoutQueueRequest{Operation="confirm",PreviewId="none",PlanToken=""});
    Check(layoutWorker.Current.Jobs.Count(j=>j.Layout)==1,"a stale confirm does not create a second move job");
}
string faultRoot=Root("fault"); var faulty=new ShopQueue(faultRoot); faulty.HandleLocalImport(new(){SourcePath=source,PackageFolder="Fault"}); task=faulty.Read()!;
faulty.HandleLocalImport(new(){Operation="confirm",PreviewId=task.Id,PlanToken=task.PlanToken}); ShopQueue.StepProbe=_=>throw new IOException("Injected after write"); await faulty.Pump(new()); ShopQueue.StepProbe=null;
Check(faulty.Read()!.State=="failed","rollback finished"); Check(!File.Exists(Path.Combine(faultRoot,"plugins","Fault","Example.dll")),"fault removed new file");
var journal=Files.Read<ShopQueue.ShopTransaction>(Path.Combine(faultRoot,"MCE","shop","tasks",task.Id,"transaction.json"));Check(journal.Phase=="rolled-back","durable rollback journal");
queue.HandleLocalImport(new(){SourcePath=source,PackageFolder="Duplicate"}); task=queue.Read()!;Check(task.Blockers.Any(b=>b.Contains("Duplicate plugin GUID")),"duplicate identity blocks");
string tamperRoot=Root("tamper"); var tamper=new ShopQueue(tamperRoot);tamper.HandleLocalImport(new(){SourcePath=source,PackageFolder="Tamper"});task=tamper.Read()!;
File.AppendAllText(Path.Combine(tamperRoot,"MCE","shop","tasks",task.Id,"content",task.Desired.Single().Path.Replace('/',Path.DirectorySeparatorChar)),"changed");
bool rejected=false;try{tamper.HandleLocalImport(new(){Operation="confirm",PreviewId=task.Id,PlanToken=task.PlanToken});}catch(IOException){rejected=true;}Check(rejected,"changed staging rejects confirmation");
string occupiedRoot=Root("occupied"); Directory.CreateDirectory(Path.Combine(occupiedRoot,"plugins","Taken")); File.WriteAllText(Path.Combine(occupiedRoot,"plugins","Taken","foreign.txt"),"keep");
var occupied=new ShopQueue(occupiedRoot);occupied.HandleLocalImport(new(){SourcePath=source,PackageFolder="Taken"});Check(occupied.Read()!.Blockers.Count==0&&occupied.Read()!.Details.Any(d=>d.Contains("already exists")),"existing unowned folder merges with backup instead of blocking");
string invalidRoot=Root("invalid");var invalid=new ShopQueue(invalidRoot);invalid.HandleLocalImport(new(){SourcePath=source,PackageFolder="plugins"});Check(invalid.Read()!.Blockers.Count>0,"original invalid-folder reason preserved");
Check(!invalid.Read()!.Blockers.Any(b=>b.Contains("path is empty")),"no misleading empty staging error");
// A folder that already exists is merged: a conflicting file is backed up, unrelated files stay, and the import proceeds.
string raceRoot=Root("race");var race=new ShopQueue(raceRoot);race.HandleLocalImport(new(){SourcePath=source,PackageFolder="Race"});task=race.Read()!;
Directory.CreateDirectory(Path.Combine(raceRoot,"plugins","Race"));File.WriteAllText(Path.Combine(raceRoot,"plugins","Race","foreign.txt"),"retained");File.WriteAllText(Path.Combine(raceRoot,"plugins","Race","Example.dll"),"older-installed-copy");
using(var raceWorker=new QueueWorker(raceRoot)){string confirmId=Guid.NewGuid().ToString("N");Files.Save(Path.Combine(raceRoot,"MCE","commands",confirmId+".json"),new UpdateCommand{Id=confirmId,Action="local-import",LocalImport=new(){Operation="confirm",PreviewId=task.Id,PlanToken=task.PlanToken}});raceWorker.ProcessCommands();var raceJob=raceWorker.Current.Jobs.Single(j=>j.LocalSource.Length>0);await raceWorker.Run(true);
 Check(raceJob.State==UpdateStates.PendingRestart,raceJob.State+" "+raceJob.Error);}
Check(File.ReadAllText(Path.Combine(raceRoot,"plugins","Race","Example.dll"))!="older-installed-copy","existing file replaced by the import");
Check(File.ReadAllText(Path.Combine(raceRoot,"plugins","Race","foreign.txt"))=="retained","raced foreign file preserved");
Files.RunningProbe=null; Console.WriteLine("PASS "+checks+" installation flow checks; "+home);
