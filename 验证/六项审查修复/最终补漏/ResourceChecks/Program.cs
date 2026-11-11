using System.Net;using System.Net.Http.Headers;using System.IO.Compression;using System.Text.Json;using System.Text.Json.Nodes;using System.Reflection;
using MCE.UpdateWorker;using ModConfigEnhance.Updating;
int count=0;void Check(bool yes,string text){if(!yes)throw new Exception(text);count++;Console.WriteLine("OK "+text);}
string sandbox=Path.Combine(AppContext.BaseDirectory,"sandbox-"+Guid.NewGuid().ToString("N"));
string Root(string name){string r=Path.Combine(sandbox,name,"BepInEx");Directory.CreateDirectory(Path.Combine(r,"plugins"));MceSeed.Add(r);return r;}
void Post(string r,UpdateCommand c){c.Id=Guid.NewGuid().ToString("N");Files.Save(Path.Combine(r,"MCE/commands",DateTime.UtcNow.Ticks+"_"+c.Id+".json"),c);}
async Task Until(Func<bool> test){var end=DateTime.UtcNow.AddSeconds(8);while(!test()&&DateTime.UtcNow<end)await Task.Delay(10);Check(test(),"bounded async boundary reached");}
Task Pump(QueueWorker w)=>(Task)typeof(QueueWorker).GetMethod("PumpResourceBatch",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(w,new object[]{CancellationToken.None})!;
ShopQueue Shop(QueueWorker w)=>(ShopQueue)typeof(QueueWorker).GetField("shop",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(w)!;
JsonNode Batch(string r)=>JsonNode.Parse(File.ReadAllText(Path.Combine(r,"MCE/resource-batch.json")))!;
Files.RunningProbe=_=>false;
string pauseRoot=Root("pause-resume");string source=Path.Combine(pauseRoot,"MCE/Provider.dll");Directory.CreateDirectory(Path.GetDirectoryName(source)!);FixturePlugin.Write(source,"test.provider");string package=Path.Combine(pauseRoot,"plugins/Update");Directory.CreateDirectory(package);File.Copy(source,Path.Combine(package,"Provider.dll"));File.WriteAllText(Path.Combine(package,"manifest.json"),JsonSerializer.Serialize(new{name="Update",version_number="1.0.0",dependencies=Array.Empty<string>()}));
var rangeStore=new RangeStore(source);ShopQueue.TestHandler=rangeStore;
using(var w=new QueueWorker(pauseRoot)){
 string id=Guid.NewGuid().ToString("N");Post(pauseRoot,new(){Action="add",Job=new(){Id=id,Namespace="Team",Package="Update",InstalledVersion="1.0.0",TargetVersion="2.0.0",PackageRoot=package,Enabled=true,PluginGuids=new(){"test.provider"}}});
 var running=w.Run(true);await Until(()=>rangeStore.Partial.Task.IsCompleted);Post(pauseRoot,new(){Action="pause",JobId=id});await running.WaitAsync(TimeSpan.FromSeconds(10));Check(w.Current.Jobs.Single().State==UpdateStates.Paused,"real QueueWorker pause applied after network cancellation");string fragment=Path.Combine(pauseRoot,"MCE/jobs",id,"new.zip.partial");Check(new FileInfo(fragment).Length==128,"pause retains exact downloaded fragment");Check(File.ReadAllText(Path.Combine(package,"manifest.json")).Contains("1.0.0"),"pause does not deploy target");
 Post(pauseRoot,new(){Action="resume",JobId=id});await w.Run(true).WaitAsync(TimeSpan.FromSeconds(10));Check(rangeStore.RangeFrom==128&&rangeStore.IfRange=="\"same\"","resume uses exact Range and If-Range");Check(w.Current.Jobs.Single().State==UpdateStates.PendingRestart,"resumed production transaction installed successfully: "+w.Current.Jobs.Single().Error);Check(File.ReadAllText(Path.Combine(package,"manifest.json")).Contains("2.0.0"),"resumed actual target manifest committed");}
// Stop a deliberately uncooperative old response, drain it, then create a new task.
string staleRoot=Root("stale-generation");var late=new LateStore();ShopQueue.TestHandler=late;
using(var w=new QueueWorker(staleRoot)){
 Post(staleRoot,new(){Action="shop",Shop=new(){Operation="preview-install",Namespace="Team",Package="Mod",Version="1.0.0"}});var run=w.Run(true);await Until(()=>late.Entered.Task.IsCompleted);var old=Shop(w).Read()!;Post(staleRoot,new(){Action="stop",JobId=old.Id});await Until(()=>Shop(w).Read()?.State=="cancelled");late.Release.TrySetResult();await run.WaitAsync(TimeSpan.FromSeconds(10));Check(Shop(w).Read()!.State=="cancelled","late successful old resolve cannot replace cancelled task");
 ShopQueue.TestHandler=new FakeStore();Post(staleRoot,new(){Action="shop",Shop=new(){Operation="preview-install",Namespace="Team",Package="Lib",Version="1.0.0"}});await w.Run(true);var next=Shop(w).Read()!;Check(next.Id!=old.Id&&next.Request.Package=="Lib"&&next.State=="preview","new generation remains authoritative after old cancellation");}
// Persist a real committed transaction but deliberately never call batch advancement,
// then construct a new QueueWorker from the exact on-disk crash image.
foreach(bool journalOnly in new[]{false,true}){
 string root=Root("commit-crash-"+journalOnly);ShopQueue.TestHandler=new FakeStore();string local=Path.Combine(root,"MCE/local");Directory.CreateDirectory(local);FixturePlugin.Write(Path.Combine(local,"Independent.dll"));
 // Install a real manifest consumer/provider first, then batch independent install+provider removal.
 var q=new ShopQueue(root);q.Handle(new(){Operation="preview-install",Namespace="Team",Package="Mod",Version="1.0.0"});await q.Pump(new());var t=q.Read()!;q.Handle(new(){Operation="approve-download",PreviewId=t.Id,PlanToken=t.PlanToken});await q.Pump(new());t=q.Read()!;q.Handle(new(){Operation="confirm-install",PreviewId=t.Id,PlanToken=t.PlanToken});await q.Pump(new());
 string firstTask="",before="";
 using(var w=new QueueWorker(root)){
 Post(root,new(){Action="resource-batch",ResourceCommands=new(){new(){Id=Guid.NewGuid().ToString("N"),Action="shop",Shop=new(){Operation="preview-uninstall",Namespace="Team",Package="Lib",Version="1.0.0"}},new(){Id=Guid.NewGuid().ToString("N"),Action="local-import",LocalImport=new(){SourcePath=local,PackageFolder="Independent"}}}});w.ProcessCommands();await Pump(w);var b=Batch(root);Post(root,new(){Action="resource-ignore-dependencies",JobId=b["ConfirmationToken"]!.GetValue<string>()});w.ProcessCommands();
 for(int i=0;i<12;i++){await Pump(w);if(Shop(w).Read()?.State=="waiting-exit")break;await Shop(w).Pump(new());}
 t=Shop(w).Read()!;Check(t.State=="waiting-exit","real batch reached commit boundary");firstTask=t.Id;before=File.ReadAllText(Path.Combine(root,"MCE/resource-batch.json"));await Shop(w).Pump(new());Check(Shop(w).Read()!.State=="completed","real production commit completed before batch acknowledgement");Check(before==File.ReadAllText(Path.Combine(root,"MCE/resource-batch.json")),"fault image retains old Next and ExpectedEnvironment");
 if(journalOnly){t=Shop(w).Read()!;t.State="committing";Files.Save(Path.Combine(root,"MCE/shop/task.json"),t);}
 }
 int writes=0;ShopQueue.StepProbe=_=>writes++;
 using(var restarted=new QueueWorker(root)){for(int i=0;i<15;i++)await restarted.Run(true);var done=Batch(root);Check(done["Completed"]!.GetValue<bool>()&&done["Error"]!.GetValue<string>()=="","restart advances committed batch without stale environment deadlock: "+done["Error"]);Check(Directory.Exists(Path.Combine(root,"plugins/Independent"))&&!File.Exists(Path.Combine(root,"patchers/Team-Lib/hook.txt")),"restart completes exact remaining operation");Check(Shop(restarted).Read()!.Id!=firstTask,"completed operation not redispatched");}
 ShopQueue.StepProbe=null;
}
// Capability gate preserves pack protocol2 while rejecting pre-fix tools/instances.
string tools=Path.Combine(sandbox,"tools");Directory.CreateDirectory(tools);
foreach(string required in new[]{WorkerProtocol.Executable,"Mono.Cecil.LICENSE.txt","Runtime-LICENSE.TXT","Runtime-THIRD-PARTY-NOTICES.TXT"})File.WriteAllText(Path.Combine(tools,required),"fixture");
WorkerProtocol.WriteManifest(tools);WorkerProtocol.RequireResourceTools(tools);Check(true,"matching complete resource-capable tools accepted");
string marker=Path.Combine(tools,WorkerProtocol.ResourceCapabilityFile);File.WriteAllText(marker,"older-capability");WorkerProtocol.WriteManifest(tools);File.WriteAllText(marker,"older-capability");
// Make an otherwise valid old declaration without the new capability marker.
File.Delete(marker);var files=Directory.GetFiles(tools).Where(f=>Path.GetFileName(f)!=WorkerProtocol.ManifestName).OrderBy(f=>Path.GetFileName(f),StringComparer.Ordinal).Select(f=>Path.GetFileName(f)+"|"+WorkerProtocol.Hash(f));string payload=string.Join(Environment.NewLine,files).Replace(Environment.NewLine,((char)10).ToString());string digest;using(var sha=System.Security.Cryptography.SHA256.Create())digest=Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();File.WriteAllText(Path.Combine(tools,WorkerProtocol.ManifestName),"MCE-WORKER|2"+(char)10+"build|"+digest+(char)10+payload+(char)10);
WorkerProtocol.ValidateTools(tools);bool refused=false;try{WorkerProtocol.RequireResourceTools(tools);}catch(IOException){refused=true;}Check(refused,"valid legacy protocol2 bundle fails new resource capability gate");
refused=false;try{WorkerProtocol.RequireResourceWorker(new(){WorkerProtocol=2,WorkerBuild="old-build"},true);}catch(IOException){refused=true;}Check(refused,"running old protocol2 worker rejected before publishing new resource commands");
WorkerProtocol.RequireResourceWorker(new(){ResourceCapability=WorkerProtocol.ResourceCapability},true);WorkerProtocol.RequireResourceWorker(new(),false);Check(WorkerProtocol.Current==2,"matching running worker and stopped legacy worker accepted without pack protocol migration");
Console.WriteLine("PASS "+count+" final-gap production checks; sandbox="+sandbox);
namespace BepInEx{internal static class Paths{internal static string ConfigPath="";}}
namespace ModConfigEnhance{internal static class Loc{internal static string T(string a,string b,string c)=>a;}}
namespace ModConfigEnhance.Overview{internal static class PackLibrary{internal static bool Managed=>false;internal static string Workspace=>"";}internal static class OverviewUpdateQueue{internal static string Root="";}internal static class OverviewRepository{internal static void AtomicWrite(string p,string s){Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,s);}}}
