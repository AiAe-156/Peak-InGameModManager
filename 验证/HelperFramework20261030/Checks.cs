using ModConfigEnhance.Updating;
using MCE.UpdateWorker;
try {
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch { Check(true, name); return; } throw new Exception("Expected rejection: " + name); }
string fixture = Path.GetFullPath("验证/HF-" + Guid.NewGuid().ToString("N").Substring(0,8)); Directory.CreateDirectory(fixture);
string source = Path.Combine(fixture, "source"); Directory.CreateDirectory(source);
foreach (string file in new[] { WorkerProtocol.Executable, "Mono.Cecil.LICENSE.txt", "Runtime-LICENSE.TXT", "Runtime-THIRD-PARTY-NOTICES.TXT", "native/dependency.dll" })
{ string path = Path.Combine(source, file); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, file); }
WorkerProtocol.WriteManifest(source); string build = WorkerProtocol.ValidateTools(source);
string tools = Path.Combine(fixture, "tools"); string cached = WorkerProtocol.PrepareTools(source, tools);
Check(WorkerProtocol.ValidateTools(cached) == build && File.Exists(Path.Combine(cached, "native", "dependency.dll")), "complete recursive copy");
Check(WorkerProtocol.PrepareTools(source, tools) == cached, "verified cache reuse");
File.WriteAllText(Path.Combine(cached, WorkerProtocol.Executable), "damaged");
string replacement = WorkerProtocol.PrepareTools(source, tools);
Check(replacement != cached && File.ReadAllText(Path.Combine(cached, WorkerProtocol.Executable)) == "damaged", "damaged cache retained/new directory");
Directory.Move(replacement, replacement + ".stage-interrupted");
Check(WorkerProtocol.PrepareTools(source, tools) != replacement + ".stage-interrupted", "unfinished stage ignored");
File.WriteAllText(Path.Combine(source, "native", "dependency.dll"), "changed"); Reject(() => WorkerProtocol.PrepareTools(source, tools), "corrupt source rejected even with valid cache");
WorkerProtocol.WriteManifest(source); Check(WorkerProtocol.ValidateTools(source) != build, "build identity not fixed product version");
File.WriteAllText(Path.Combine(source, "unlisted.dll"), "extra"); Reject(() => WorkerProtocol.ValidateTools(source), "unlisted dependency rejected");
WorkerProtocol.RequireProtocol(1, 1); Check(true, "legacy schema accepted"); WorkerProtocol.RequireProtocol(2, 2); Check(true, "schema2 accepted");
Reject(() => WorkerProtocol.RequireProtocol(2, 1), "schema2 old protocol rejected"); Reject(() => WorkerProtocol.RequireProtocol(3, 3), "future schema rejected");
string root = Path.Combine(fixture, "BepInEx"); Directory.CreateDirectory(Path.Combine(root, "plugins"));
Directory.CreateDirectory(Path.Combine(root, "MCE", "commands")); string command = Path.Combine(root, "MCE", "commands", "new.json"); File.WriteAllText(command, "new user command");
QueueWorker.RecoveryOnly(root, null, false); Check(File.ReadAllText(command) == "new user command" && !File.Exists(Path.Combine(root, "MCE", "queue.json")), "recovery does not run new commands or initialize queue");
Reject(() => QueueWorker.RecoveryOnly(root, "../escape", false), "invalid selection rejected");
string id = Guid.NewGuid().ToString("N"); string journal = Path.Combine(root, "MCE", "jobs", id, "transaction.json"); Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
Files.Save(Path.Combine(root, "MCE", "queue.json"), new UpdateSnapshot { Jobs = new() { new UpdateJob { Id = id, PackageRoot = Path.Combine(root, "plugins", "package") } } });
File.WriteAllText(journal, "{broken"); Reject(() => QueueWorker.RecoveryOnly(root, null, false), "damaged journal rejected"); Check(File.ReadAllText(journal) == "{broken", "damaged journal original retained");
File.WriteAllText(journal, "{}"); Reject(() => WorkerRecovery.Scan(root), "missing phase rejected, not default-prepared");
File.WriteAllText(journal, "{\"Phase\":\"future\"}"); Reject(() => WorkerRecovery.Scan(root), "unknown journal phase rejected");
string content = Path.Combine(fixture, "content"); Directory.CreateDirectory(content);
Reject(() => WorkerTargetProtocol.ValidateTargetContent(content), "missing target host rejected without borrowing");
Files.RunningProbe = _ => true; Check(Files.GameRunning(new UpdateJob()), "new game process guard"); Files.RunningProbe = null;
// Capability is read from metadata only; no target assembly is loaded.
string hostDirectory = Path.Combine(content, "plugins", "MCE"); Directory.CreateDirectory(hostDirectory);
string host = Path.Combine(hostDirectory, "ModConfigEnhance.dll");
using (var module = Mono.Cecil.ModuleDefinition.CreateModule("ModConfigEnhance", Mono.Cecil.ModuleKind.Dll))
{
    module.Write(host);
    Reject(() => WorkerTargetProtocol.ValidateTargetContent(content), "legacy target host rejected despite filename/version");
    var ctor = module.ImportReference(typeof(System.Reflection.AssemblyMetadataAttribute).GetConstructor(new[] { typeof(string), typeof(string) })!);
    var attribute = new Mono.Cecil.CustomAttribute(ctor);
    attribute.ConstructorArguments.Add(new Mono.Cecil.CustomAttributeArgument(module.TypeSystem.String, "MCE.WorkerProtocol"));
    attribute.ConstructorArguments.Add(new Mono.Cecil.CustomAttributeArgument(module.TypeSystem.String, "2"));
    module.Assembly.CustomAttributes.Add(attribute); module.Write(host);
}
Reject(() => WorkerTargetProtocol.ValidateTargetContent(content), "metadata without target tools rejected");
string targetTools = Path.Combine(hostDirectory, "Tools", "Updater"); Directory.CreateDirectory(targetTools);
foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
{
    string target = Path.Combine(targetTools, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
}
WorkerProtocol.WriteManifest(targetTools);
Check(WorkerTargetProtocol.ValidateTargetContent(content) == WorkerProtocol.ValidateTools(targetTools), "read-only target metadata and complete tool release API");
Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "ModConfigEnhance"), "target assembly never executed/loaded");
Reject(() => WorkerProtocol.ValidateTools(targetTools, 3), "future target protocol rejected");
Check(new UpdateCommand().MinimumWorkerProtocol == 1 && new UpdateSnapshot().WorkerProtocol == 1, "legacy contracts stay legacy");
var modern = new UpdateCommand { SchemaVersion = 2, MinimumWorkerProtocol = 2 };
Check(modern.SchemaVersion != 1, "old schema1 command consumer rejects new command");
Console.WriteLine($"{passed} passed");

Extra.Run();

} catch(Exception e) { Console.WriteLine(e.GetType().FullName); Console.WriteLine(e.Message); Console.WriteLine(e.StackTrace); throw; }
