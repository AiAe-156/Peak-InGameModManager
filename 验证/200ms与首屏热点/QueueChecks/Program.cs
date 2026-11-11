using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
partial class Harness {
 static readonly object Gate=new(),CommandGate=new();static long _commandEpoch;static string _readContext="env";
 sealed class ObserveResult {internal string Context="",Error="";internal long Epoch;internal bool Executed;internal double PrepareMs,PublishMs,LaunchMs;}
 class UpdateCommand {public string Id="id";}
 enum Formatting {Indented}
 static class JsonConvert {internal static string SerializeObject(object c,Formatting f)=>"{}";}
 static readonly ManualResetEventSlim preparing=new(),release=new();static bool fail,block;static int prepares,launches;static string prepared="validated-tools";
 static string PrepareWorkerTools(string root,string bepRoot,string source){Interlocked.Increment(ref prepares);preparing.Set();if(block)release.Wait();if(fail)throw new IOException("invalid protocol/manifest");return prepared;}
 static void EnsureWorker(string root,string bepRoot,string tools){if(tools!=prepared)throw new Exception("wrong tools");launches++;}
 static int passed;static void Check(bool ok,string title){if(!ok)throw new Exception(title);passed++;Console.WriteLine("PASS "+title);}
 static void Main(){
 string root=Path.Combine(AppContext.BaseDirectory,"sandbox_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
 block=true;var task=Task.Run(()=>Observe(root,"bep","source","env",0,new UpdateCommand()));Check(preparing.Wait(5000),"background entered safety preparation");
 bool entered=Monitor.TryEnter(Gate,100);if(entered)Monitor.Exit(Gate);Check(entered,"UI Gate remains available during slow tool validation");
 Check(!Monitor.TryEnter(CommandGate),"command publication serialized during observation");release.Set();var result=task.GetAwaiter().GetResult();
 Check(result.Executed&&result.Error==""&&prepares==1&&launches==1,"validated tools reused without second preparation");
 Check(Directory.GetFiles(Path.Combine(root,"commands"),"*.json").Length==1&&Directory.GetFiles(Path.Combine(root,"commands"),"*.tmp").Length==0,"one atomic command publication");
 _commandEpoch=1;var stale=Observe(root,"bep","source","env",0,new UpdateCommand());Check(!stale.Executed&&prepares==1&&launches==1,"superseded explicit epoch skipped before preparation");
 _readContext="other";var context=Observe(root,"bep","source","env",1,new UpdateCommand());Check(!context.Executed&&prepares==2&&launches==1&&Directory.GetFiles(Path.Combine(root,"commands"),"*.json").Length==1,"stale environment discarded before publication");
 _readContext="env";fail=true;var broken=Observe(root,"bep","source","env",1,new UpdateCommand());Check(broken.Executed&&broken.Error=="invalid protocol/manifest"&&launches==1&&Directory.GetFiles(Path.Combine(root,"commands"),"*.json").Length==1,"validation failure preserved and prevents launch/publication");
 Check(result.Context=="env"&&result.Epoch==0,"completion carries context and epoch for main-thread rejection");
 Console.WriteLine($"RESULT {passed} exact-production Observe tests passed; sandbox={root}");
 }
}
