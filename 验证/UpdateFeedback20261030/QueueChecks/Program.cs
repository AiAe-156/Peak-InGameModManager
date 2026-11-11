using System.Diagnostics;
using System.Reflection;
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;

int passed=0;
void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
void Reject(Action action,string name){try{action();}catch(IOException){Check(true,name);return;}throw new Exception("Not rejected: "+name);}
void Junction(string alias,string target){using var p=Process.Start(new ProcessStartInfo("cmd.exe", "/c mklink /J \""+alias+"\" \""+target+"\""){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true})!;p.WaitForExit();if(p.ExitCode!=0)throw new Exception(p.StandardError.ReadToEnd());}
string sandbox=Path.GetFullPath(args[0]);Directory.CreateDirectory(sandbox);
string physical=Path.Combine(sandbox,"physical");Directory.CreateDirectory(physical);
string alias=Path.Combine(sandbox,"a");Junction(alias,physical);
try {
string game=Path.Combine(alias,"g");string bepin=Path.Combine(game,"BepInEx");Directory.CreateDirectory(Path.Combine(bepin,"plugins","Test"));
BepInEx.Paths.BepInExRootPath=bepin;
string source=Path.Combine(AppContext.BaseDirectory,"Tools","Updater");Directory.CreateDirectory(source);
foreach(string name in new[]{WorkerProtocol.Executable,"Mono.Cecil.LICENSE.txt","Runtime-LICENSE.TXT","Runtime-THIRD-PARTY-NOTICES.TXT"})File.WriteAllText(Path.Combine(source,name),"sandbox fixture");WorkerProtocol.WriteManifest(source);
// Direct protocol test keeps source AND cache beneath an actual ancestor junction.
string bundle=Path.Combine(game,"release","Tools","Updater");Directory.CreateDirectory(bundle);
foreach(var f in Directory.GetFiles(source))File.Copy(f,Path.Combine(bundle,Path.GetFileName(f)));
string toolsRoot=Path.Combine(bepin,"MCE","tools");
string cache=WorkerProtocol.PrepareTools(bundle,toolsRoot,sourceBoundary:game,cacheBoundary:game);
Check(Directory.Exists(cache),"PrepareTools accepts real Steam ancestor junction");
Check(WorkerProtocol.ValidateTools(cache,boundary:game).Length==64,"validated immutable cache");
Check(WorkerProtocol.PrepareTools(bundle,toolsRoot,sourceBoundary:game,cacheBoundary:game)==cache,"cache reuse through ancestor junction");
Reject(()=>WorkerProtocol.RejectLinks(game,Path.Combine(sandbox,"outside")),"outside explicit boundary rejected");
Reject(()=>WorkerProtocol.RejectLinks(alias,game),"boundary junction itself rejected");
string link=Path.Combine(game,"internal-link");Junction(link,physical);
try { Reject(()=>WorkerProtocol.RejectLinks(game,Path.Combine(link,"child")),"internal junction rejected including nonexistent leaf"); } finally { Directory.Delete(link); }
string nested=Path.Combine(bundle,"linked");Junction(nested,physical);
try { Reject(()=>WorkerProtocol.ValidateTools(bundle,boundary:game),"linked dependency directory rejected"); } finally { Directory.Delete(nested); }
string linkedTools=Path.Combine(game,"linked-tools");Junction(linkedTools,toolsRoot);
try { Reject(()=>WorkerProtocol.PrepareTools(bundle,linkedTools,sourceBoundary:game,cacheBoundary:game),"linked cache root rejected before copy"); } finally { Directory.Delete(linkedTools); }
File.AppendAllText(Path.Combine(bundle,WorkerProtocol.Executable),"tamper");
Reject(()=>WorkerProtocol.PrepareTools(bundle,toolsRoot,sourceBoundary:game,cacheBoundary:game),"damaged source manifest still blocks preparation");
File.Copy(Path.Combine(source,WorkerProtocol.Executable),Path.Combine(bundle,WorkerProtocol.Executable),true);
string home=Path.Combine(bepin,"MCE");
// Installed-host source and cache use explicit GAME boundary in production; assembly is a test fixture outside game,
// so move the fixture installation boundary to the test project ancestor only for production Post tests.
string productionGame=Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
string productionBepin=Path.Combine(productionGame,"BepInEx-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(productionBepin,"plugins","Test"));
BepInEx.Paths.BepInExRootPath=productionBepin;string productionHome=Path.Combine(productionBepin,"MCE");Directory.CreateDirectory(productionHome);
using var workerLock=new FileStream(Path.Combine(productionHome,"worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
var repo=new OverviewRepository();var mod=new OverviewMod{PackageRoot=Path.Combine(productionBepin,"plugins","Test")};
Check(OverviewUpdateQueue.AuthorVersion(repo,mod)=="1.0.0","author fixture pins installed version");
string feedback="";OverviewUpdateQueue.EnqueueAll(repo,new[]{mod},s=>feedback=s);
string commands=Path.Combine(productionHome,"commands");var commandFiles=Directory.GetFiles(commands,"*.json");
Check(commandFiles.Length==1,"production EnqueueAll publishes exactly one command");
var command=JsonConvert.DeserializeObject<UpdateCommand>(File.ReadAllText(commandFiles[0]))!;
Check(command.Job!.TargetVersion=="2.0.0"&&!command.Job.AllowDowngrade,"latest wins over author pin without batch downgrade");
Check(feedback.Contains("1.0.0 → 2.0.0"),"added target included in feedback");
mod.HasUpdate=false;OverviewUpdateQueue.EnqueueAll(repo,new[]{mod},s=>feedback=s);Check(Directory.GetFiles(commands).Length==1&&feedback.Contains("No confirmed update"),"no green-dot entry skipped with reason");
mod.HasUpdate=true;mod.LatestVersion="0.9.0";OverviewUpdateQueue.EnqueueAll(repo,new[]{mod},s=>feedback=s);Check(Directory.GetFiles(commands).Length==1&&feedback.Contains("never downgrade"),"stale lower latest skipped with reason");
mod.LatestVersion="2.0.0";repo.Allow=false;OverviewUpdateQueue.EnqueueAll(repo,new[]{mod},s=>feedback=s);Check(Directory.GetFiles(commands).Length==1&&feedback.Contains("许可"),"update permission remains enforced");repo.Allow=true;
mod.PackageRoot=Path.Combine(productionBepin,"core","MonoDetour");Directory.CreateDirectory(mod.PackageRoot);OverviewUpdateQueue.EnqueueAll(repo,new[]{mod},s=>feedback=s);Check(Directory.GetFiles(commands).Length==1&&feedback.Contains("Core"),"component skipped with preview guidance and no core write command");
mod.PackageRoot=Path.Combine(productionBepin,"plugins","Test");
string queueFixture=Path.Combine(productionHome,"queue.json");
File.WriteAllText(queueFixture,JsonConvert.SerializeObject(new UpdateSnapshot{Jobs=new(){new UpdateJob{Id=Guid.NewGuid().ToString("N"),PackageRoot=mod.PackageRoot,State=UpdateStates.Queued}}}));
OverviewUpdateQueue.EnqueueAll(repo,new[]{mod},x=>feedback=x);Check(Directory.GetFiles(commands).Length==1&&feedback.Contains("已在更新队列"),"pending same-package job skipped visibly");
File.WriteAllText(queueFixture,JsonConvert.SerializeObject(new UpdateSnapshot{Jobs=new(){new UpdateJob{Id=Guid.NewGuid().ToString("N"),PackageRoot=mod.PackageRoot,State=UpdateStates.Failed}}}));
OverviewUpdateQueue.EnqueueAll(repo,new[]{mod},x=>feedback=x);Check(Directory.GetFiles(commands).Length==1&&feedback.Contains("重试或移除"),"failed same-package job requires retry instead of duplicate publication");
File.WriteAllText(queueFixture,JsonConvert.SerializeObject(new UpdateSnapshot()));
// Read the actual production parser, not a restatement of its rules.
var read=typeof(OverviewUpdateQueue).GetMethod("ReadQueue",BindingFlags.NonPublic|BindingFlags.Static)!;
string failure=Path.Combine(productionHome,"worker-error.json");File.WriteAllText(failure,JsonConvert.SerializeObject(new{Message="old Steam failure",UpdatedUtc="2026-10-28T13:31:25Z"}));File.SetLastWriteTimeUtc(failure,DateTime.UtcNow);
object Read()=>read.Invoke(null,new object[]{productionHome})!;
string Field(object o,string n)=>(string)o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(o)!;
File.WriteAllText(queueFixture,JsonConvert.SerializeObject(new UpdateSnapshot{WorkerProtocol=2,WorkerBuild="verified",Error="old snapshot error"}));
var result=Read();Check(Field(result,"Error")==""&&Field(result,"HistoricalError").Contains("old snapshot error"),"old snapshot error without this-session startup evidence stays historical");
File.WriteAllText(queueFixture,JsonConvert.SerializeObject(new UpdateSnapshot()));result=Read();Check(Field(result,"Error")==""&&Field(result,"HistoricalError").Contains("old Steam failure"),"old error with fresh mtime is history, not current red error");
var attempt=DateTime.UtcNow;typeof(OverviewUpdateQueue).GetField("_workerAttemptUtc",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,attempt);
File.WriteAllText(failure,JsonConvert.SerializeObject(new{Message="new startup failed",UpdatedUtc=attempt.AddSeconds(1).ToString("O")}));result=Read();Check(Field(result,"Error")=="new startup failed","failed launch after this-session attempt is current");
string queue=Path.Combine(productionHome,"queue.json");
File.WriteAllText(queue,JsonConvert.SerializeObject(new UpdateSnapshot{WorkerProtocol=2,WorkerBuild="verified",Revision=1}));File.SetLastWriteTimeUtc(queue,attempt.AddSeconds(2));result=Read();Check(Field(result,"Error")==""&&Field(result,"HistoricalError").Contains("new startup failed"),"successful protocol2 startup supersedes earlier launch failure");
Check(File.Exists(failure),"diagnostic file is never deleted");
File.WriteAllText(queue,JsonConvert.SerializeObject(new UpdateSnapshot{WorkerProtocol=2,WorkerBuild="verified",Error="current command rejected"}));File.SetLastWriteTimeUtc(queue,attempt.AddSeconds(3));result=Read();Check(Field(result,"Error")=="current command rejected","current command error is not hidden");
string script=RecoveryLauncher.Write(home,cache);Check(File.ReadAllText(script).Contains("$boundary=[IO.Path]::GetDirectoryName($script:root)"),"recovery PowerShell shares explicit game boundary");
using(var ps=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -Command \"& { $ErrorActionPreference='Stop'; . '"+script+"' -Headless; $null=Find-RecoveryTools; Write-Output 'recovery-pass' }\""){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true})!){string output=ps.StandardOutput.ReadToEnd();string error=ps.StandardError.ReadToEnd();ps.WaitForExit();Check(ps.ExitCode==0&&error.Length==0&&output.Contains("recovery-pass"),"production recovery template accepts real ancestor junction: "+error);}
Console.WriteLine($"ALL {passed} CHECKS PASSED; sandbox only; no real worker/update executed.");
} finally { Directory.Delete(alias); }
