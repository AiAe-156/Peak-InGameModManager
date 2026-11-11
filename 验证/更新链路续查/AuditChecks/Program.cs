using MCE.UpdateWorker;
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
using System.Text.Json;
using System.Text.Json.Nodes;
var home=Path.Combine(AppContext.BaseDirectory,"audit-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(home);
var results=new List<object>();
void Result(string name,bool defect,string actual){results.Add(new{name,defect,actual});Console.WriteLine((defect?"REPRO ":"PASS ")+name+": "+actual);}
BepInEx.Paths.ConfigPath=Path.Combine(home,"config");OverviewUpdateQueue.Root=Path.Combine(home,"BepInEx/MCE");Directory.CreateDirectory(OverviewUpdateQueue.Root);
ResourceOperationDraft.Stage(new UpdateCommand{Action="shop",Shop=new(){Namespace="Team",Package="Mod",Version="1.0.0",Operation="preview-install"}});
string accepted=Guid.NewGuid().ToString("N");ResourceOperationDraft.BeginSubmission(accepted);
var receipt=Path.Combine(OverviewUpdateQueue.Root,"command-receipts",accepted+".json");Directory.CreateDirectory(Path.GetDirectoryName(receipt)!);File.WriteAllText(receipt,"{\"Status\":\"accepted\"}");
bool staleBlocked=false;try{ResourceOperationDraft.Stage(new(){Action="shop",Shop=new(){Namespace="Team",Package="Other",Version="1.0.0"}});}catch(InvalidOperationException){staleBlocked=true;}
Result("accepted draft permits new staging without Count read",staleBlocked,$"Stage blocked={staleBlocked}, Snapshot count={ResourceOperationDraft.Snapshot().Count}");
Result("Snapshot excludes accepted old operations",ResourceOperationDraft.Snapshot().Any(c=>c.Shop?.Package=="Mod"),"count="+ResourceOperationDraft.Snapshot().Count);
_ = ResourceOperationDraft.Count;
ResourceOperationDraft.Clear();
ResourceOperationDraft.Stage(new(){Action="shop",Shop=new(){Namespace="Team",Package="Pending",Version="1.0.0"}});
string pending=Guid.NewGuid().ToString("N");ResourceOperationDraft.BeginSubmission(pending);
Result("unacknowledged submission remains pending",!ResourceOperationDraft.AwaitingAcknowledgement||ResourceOperationDraft.Snapshot().Count!=1,"pending state preserved");
bool pendingBlocked=false;try{ResourceOperationDraft.Remove(ResourceOperationDraft.Snapshot()[0].Id);}catch(InvalidOperationException){pendingBlocked=true;}Result("unacknowledged submission cannot be removed",!pendingBlocked,"remove blocked="+pendingBlocked);
File.WriteAllText(Path.Combine(OverviewUpdateQueue.Root,"command-receipts",pending+".json"),"{\"Status\":\"accepted\"}");
Result("Snapshot reconciles acknowledgement without Count read",ResourceOperationDraft.Snapshot().Count!=0||ResourceOperationDraft.AwaitingAcknowledgement,"accepted state reconciled");
var cancelled=ShopQueueProjection.ToJob(new ShopTask{Id=Guid.NewGuid().ToString("N"),State="cancelled",Request=new(){Namespace="Team",Package="Mod"}});
Result("cancelled shop operation projects stopped",cancelled.State!=UpdateStates.Stopped,"projected state="+cancelled.State);
string root=Path.Combine(home,"worker/BepInEx");Directory.CreateDirectory(Path.Combine(root,"plugins"));MceSeed.Add(root);Files.RunningProbe=_=>false;
using(var worker=new QueueWorker(root)){
 string current=Guid.NewGuid().ToString("N"),stale=Guid.NewGuid().ToString("N");
 void Post(UpdateCommand cmd){Files.Save(Path.Combine(root,"MCE/commands",Guid.NewGuid().ToString("N")+".json"),cmd);worker.ProcessCommands();}
 Post(new(){Id=current,Action="resource-batch",ResourceCommands=new(){new(){Id=Guid.NewGuid().ToString("N"),Action="shop",Shop=new(){Operation="preview-uninstall",Namespace="Team",Package="Missing",Version="1.0.0"}}}});
 foreach(string action in new[]{"resource-retry","resource-skip","resource-ignore-dependencies"}){
  string control=Guid.NewGuid().ToString("N");Post(new(){Id=control,Action=action,ResourceBatchId=stale});
  Result("stale "+action+" rejected",!File.Exists(Path.Combine(root,"MCE/command-rejections",control+".json")),"identity rejection persisted");
 }
 Post(new(){Id=Guid.NewGuid().ToString("N"),Action="resource-cancel",ResourceBatchId=stale});
 var batch=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"MCE/resource-batch.json")))!;
 Result("stale batch cancel leaves current batch unchanged",batch["Completed"]!.GetValue<bool>(),"current batch completed="+batch["Completed"]);
 Post(new(){Id=Guid.NewGuid().ToString("N"),Action="resource-cancel",ResourceBatchId=current});
 batch=JsonNode.Parse(File.ReadAllText(Path.Combine(root,"MCE/resource-batch.json")))!;
 Result("matching batch cancel succeeds",!batch["Completed"]!.GetValue<bool>(),"completed="+batch["Completed"]);
}
string independentRoot=Path.Combine(home,"independent/BepInEx");Directory.CreateDirectory(Path.Combine(independentRoot,"plugins"));MceSeed.Add(independentRoot);
using(var worker=new QueueWorker(independentRoot)){
 string activeId=Guid.NewGuid().ToString("N"),updateId=Guid.NewGuid().ToString("N");
 void PostIndependent(UpdateCommand cmd){Files.Save(Path.Combine(independentRoot,"MCE/commands",Guid.NewGuid().ToString("N")+".json"),cmd);worker.ProcessCommands();}
 PostIndependent(new(){Id=activeId,Action="resource-batch",ResourceCommands=new(){new(){Id=Guid.NewGuid().ToString("N"),Action="shop",Shop=new(){Operation="preview-uninstall",Namespace="Team",Package="Missing",Version="1.0.0"}}}});
 PostIndependent(new(){Id=updateId,Action="update-batch",ResourceCommands=new(){new(){Id=Guid.NewGuid().ToString("N"),Action="shop",Shop=new(){Operation="preview-install",Namespace="Team",Package="Mod",Version="1.0.0"}}}});
 Result("update click durable while other batch pending",!Directory.Exists(Path.Combine(independentRoot,"MCE/operations")),"independent package intents persisted");
 Result("update click acknowledged without config save",!File.Exists(Path.Combine(independentRoot,"MCE/command-receipts",updateId+".json")),"receipt persisted");
 PostIndependent(new(){Id=Guid.NewGuid().ToString("N"),Action="resource-cancel",ResourceBatchId=activeId});
 Result("updates do not overwrite legacy resource batch",JsonNode.Parse(File.ReadAllText(Path.Combine(independentRoot,"MCE/resource-batch.json")))!["Id"]!.GetValue<string>()!=activeId,"separate operation records");
}
// Correlated single-operation warnings are shared by both UI entrances.
OverviewUpdateQueue.Root=Path.Combine(home,"review");Directory.CreateDirectory(Path.Combine(OverviewUpdateQueue.Root,"shop"));
string reviewId=Guid.NewGuid().ToString("N"),operationId=Guid.NewGuid().ToString("N"),taskId=Guid.NewGuid().ToString("N");
File.WriteAllText(Path.Combine(OverviewUpdateQueue.Root,"resource-batch.json"),JsonSerializer.Serialize(new{Id=reviewId,Next=0,Completed=false,TaskId=taskId,Commands=new[]{new UpdateCommand{Id=operationId,Shop=new(){Namespace="Team",Package="Provider",Operation="preview-uninstall",Version="1.0.0"}}}}));
var warningTask=new ShopTask{Id=taskId,OperationId=operationId,Uninstall=true,DependencyWarnings=new(){"Consumer requires Provider"}};
File.WriteAllText(Path.Combine(OverviewUpdateQueue.Root,"shop/task.json"),JsonSerializer.Serialize(warningTask));
var impact=SavedResourceReview.ReadImpact(reviewId);Result("single uninstall warning correlated and visible",impact.Warnings.Count!=1||impact.BatchId!=reviewId||impact.Token.Length!=64,"warnings="+impact.Warnings.Count);
Result("batch details include package and version",!SavedResourceReview.Details(reviewId).Contains("Team-Provider 1.0.0"),"detail list available");
warningTask.OperationId=Guid.NewGuid().ToString("N");File.WriteAllText(Path.Combine(OverviewUpdateQueue.Root,"shop/task.json"),JsonSerializer.Serialize(warningTask));Result("unrelated shop warnings excluded",SavedResourceReview.ReadImpact(reviewId).Warnings.Count!=0,"warning correlation enforced");
File.WriteAllText(Path.Combine(home,"results.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("REPORT "+home);
if(results.Any(r=>(bool)r.GetType().GetProperty("defect")!.GetValue(r)!))throw new Exception("Regression detected");
namespace BepInEx{internal static class Paths{internal static string ConfigPath="";}}
namespace ModConfigEnhance{internal static class Loc{internal static string T(string a,string b,string c)=>a;internal static string LocalizeMessage(string s)=>s;}
internal enum UiActionRole{Danger}}
namespace ModConfigEnhance.Overview{
internal static class PackLibrary{internal static bool Managed=>false;internal static string Workspace=>"";}
internal static class OverviewUpdateQueue{internal static string Root="";internal static void Send(string a,string b,string resourceBatchId=""){}}
internal static class OverviewUi{internal static void ShowText(string a,string b){}internal static void ConfirmText(string a,string b,Action c,ModConfigEnhance.UiActionRole d,string e){}}
internal static class OverviewRepository{internal static void AtomicWrite(string path,string content){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,content);}}
internal static class ThunderstoreUpdateService{internal static string HomeUrl(string a,string b)=>"https://example.invalid/"+a+"/"+b;}
}
