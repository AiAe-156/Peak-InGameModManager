using System.IO.Compression;
using MCE.UpdateWorker;
using ModConfigEnhance.Updating;
using Mono.Cecil;

string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
Directory.CreateDirectory(fixtures); int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
void Reject(Action action, string message) { try { action(); } catch (IOException) { checks++; return; } throw new Exception(message); }
string Root(string name) { string root = Path.Combine(fixtures, name, "BepInEx"); Directory.CreateDirectory(Path.Combine(root, "plugins")); return root; }
void Write(string root, string path, string text) { string file = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); }
void Plugin(string root, string path, string guid, string version)
{
    string target = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("fixture" + Guid.NewGuid().ToString("N"), new Version(version)), "fixture", ModuleKind.Dll);
    var module = assembly.MainModule; var type = new TypeDefinition("Fixture", "Plugin", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object); module.Types.Add(type);
    var attribute = new TypeReference("BepInEx", "BepInPlugin", module, new AssemblyNameReference("BepInEx", new Version(5, 4, 0, 0)));
    var constructor = new MethodReference(".ctor", module.TypeSystem.Void, attribute) { HasThis = true };
    foreach (int ignored in new[] { 1, 2, 3 }) constructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
    var instance = new CustomAttribute(constructor);
    foreach (string value in new[] { guid, guid, version }) instance.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, value));
    type.CustomAttributes.Add(instance); assembly.Write(target);
}
// Store-style previews are read-only until the player approves the exact download list: preview, then replay with that list.
async Task<PackTask> ApprovedPreview(PackStorage target, PackRequest request, UpdateNetworkOptions network, HttpMessageHandler handler)
{
    var first = await PackImports.Preview(target, request, network, handler);
    return first.PendingDownloads.Count == 0 ? first : await PackImports.Preview(target, request, network, handler, first.PendingDownloads);
}
void PluginDep(string root, string path, string guid, string version, string dependsOn)
{
    string target = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("fixture" + Guid.NewGuid().ToString("N"), new Version(version)), "fixture", ModuleKind.Dll);
    var module = assembly.MainModule; var type = new TypeDefinition("Fixture", "Plugin", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object); module.Types.Add(type);
    var bepinex = new AssemblyNameReference("BepInEx", new Version(5, 4, 0, 0));
    var plugin = new MethodReference(".ctor", module.TypeSystem.Void, new TypeReference("BepInEx", "BepInPlugin", module, bepinex)) { HasThis = true };
    foreach (int ignored in new[] { 1, 2, 3 }) plugin.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
    var identity = new CustomAttribute(plugin);
    foreach (string value in new[] { guid, guid, version }) identity.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, value));
    type.CustomAttributes.Add(identity);
    var dependency = new MethodReference(".ctor", module.TypeSystem.Void, new TypeReference("BepInEx", "BepInDependency", module, bepinex)) { HasThis = true };
    dependency.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
    var hard = new CustomAttribute(dependency); hard.ConstructorArguments.Add(new CustomAttributeArgument(module.TypeSystem.String, dependsOn));
    type.CustomAttributes.Add(hard); assembly.Write(target);
}
var storage = new PackStorage(Root("main"));
Check(PackStorage.StableId("Test.Pack") == PackStorage.StableId("test.pack"), "publication identity must be stable");
Check(!PackStorage.ManagedPath("core/a.dll") && !PackStorage.ManagedPath("config/com.aiae.modconfigenhance.cfg"), "core and global settings excluded");
Check(PackStorage.ManagedPath("config/example.cfg"), "ordinary config owned by pack");
var incoming = Path.Combine(fixtures, "incoming");
Write(incoming, "BepInEx/plugins/Pack/MCEPack/release.json", "{\"packId\":\"test.pack\",\"name\":\"测试包\",\"version\":\"1.0.0\"}");
Write(incoming, "BepInEx/plugins/Pack/MCEPack/profiles.json", "{\"schemaVersion\":1,\"profiles\":[]}");
Write(incoming, "BepInEx/config/example.cfg", "value=first");
Write(incoming, "BepInEx/config/com.aiae.modconfigenhance.cfg", "private=true");
Plugin(incoming, "BepInEx/plugins/Feature/mod.dll", "feature.one", "1.0.0");
var preview = await PackImports.Preview(storage, new PackRequest { Source = incoming }, new());
Check(preview.State == "preview" && preview.Proposed.Problems.Count == 0, "valid local pack can be previewed");
Check(!File.Exists(storage.IndexPath) && Directory.GetFiles(Path.Combine(storage.Root, "plugins")).Length == 0, "preview must not register or install files");
Check(preview.Proposed.Files.All(f => !f.Path.Contains("com.aiae.modconfigenhance.cfg")), "global user config remains outside pack");
PackImports.Commit(storage, preview); var index = storage.Read();
Check(index.Packs.Count == 1 && index.ActivePackId == "", "import creates an inactive inventory entry");
storage.Validate(index.Packs[0]); checks++;
Write(storage.Content(index.Packs[0]), "plugins/unregistered.txt", "unknown");
Reject(() => storage.Validate(index.Packs[0]), "unknown inventory files must block activation");
string duplicate = Path.Combine(fixtures, "duplicate");
Plugin(duplicate, "plugins/A/mod.dll", "duplicate.guid", "1.0.0"); Plugin(duplicate, "plugins/B/mod.dll", "duplicate.guid", "1.0.0");
var duplicates = await PackImports.Preview(storage, new PackRequest { Source = duplicate }, new());
Check(duplicates.Proposed.Problems.Any(p => p.Contains("重复插件身份")), "duplicate GUID is reported");
string core = Path.Combine(fixtures, "core"); Write(core, "BepInEx/core/BepInEx.dll", "untrusted core");
var blocked = await PackImports.Preview(storage, new PackRequest { Source = core }, new());
Check(blocked.Proposed.Problems.Any(p => p.Contains("核心兼容")) && blocked.Proposed.Files.All(p => !p.Path.StartsWith("core/")), "core is compatibility-only");
Check(!File.Exists(Path.Combine(storage.Root, "core", "BepInEx.dll")), "core never written");
string unsafeZip = Path.Combine(fixtures, "unsafe.zip");
using (var zip = ZipFile.Open(unsafeZip, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("../escape.dll").Open()); writer.Write("escape"); }
Reject(() => PackImports.Extract(unsafeZip, Path.Combine(fixtures, "unsafe-out")), "archive traversal must fail");
var broken = new PackStorage(Root("broken")); Write(broken.Home, "index.json", "{\"SchemaVersion\":2}");
Reject(() => broken.Read(), "corrupt or unknown index schema must fail closed");
Files.RunningProbe = _ => false;
var switching = new PackStorage(Root("switching"));
Write(switching.Root, "plugins/Host/host.txt", "fixed host");
Plugin(switching.Root, "plugins/A/mod.dll", "pack.a", "1.0.0"); Write(switching.Root, "config/a.cfg", "value=A1");
var hosts = new List<PackHostPlugin> { new() { Guid = "com.aiae.modconfigenhance", Version = "2.0.0" } };
var adoption = PackTransactions.Preview(switching, new PackRequest { Operation = "preview-adopt", HostPlugins = hosts, HostRoots = new() { "plugins/Host" } });
Check(adoption.UnknownConfigurations.Count == 0 && adoption.Report.Files.Any(f => f.Path == "config/a.cfg"), "first temporary save includes configs automatically without an ownership checkbox");
Check(adoption.Report.Verdict == "ready" && adoption.Report.Kind == "snapshot", "temporary save has a valid structured preview");
adoption.Request.Operation = "confirm-adopt";
adoption.Request.ConfirmedConfigFiles = adoption.UnknownConfigurations;
var adopted = PackTransactions.Prepare(switching, adoption); PackTransactions.Commit(switching, adopted);
string aId = switching.Read().ActivePackId;
Check(aId == adoption.Proposed.Id && File.Exists(Path.Combine(switching.Root, "config", "a.cfg")), "adoption registers without moving current environment");
string bSource = Path.Combine(fixtures, "pack-b"); Plugin(bSource, "plugins/B/mod.dll", "pack.b", "1.0.0"); Write(bSource, "config/b.cfg", "value=B1");
var bImport = await PackImports.Preview(switching, new PackRequest { Source = bSource, HostPlugins = hosts }, new()); PackImports.Commit(switching, bImport);
string bId = bImport.Proposed.Id;
PackTask SwitchPreview(string id) => PackTransactions.Preview(switching, new PackRequest { Operation = "preview-switch", PackId = id, HostPlugins = hosts });
void Switch(string id)
{
    var task = SwitchPreview(id); task.Request.Operation = "confirm-switch"; task.Request.ConfirmedConfigFiles = task.UnknownConfigurations;
    var plan = PackTransactions.Prepare(switching, task); PackTransactions.Commit(switching, plan);
}
// Simulate a completed update and native settings saved on exit, then verify they are retained in the old pack's new snapshot.
Plugin(switching.Root, "plugins/A/mod.dll", "pack.a", "1.1.0"); Write(switching.Root, "config/a.cfg", "value=A-final");
Switch(bId);
Check(!File.Exists(Path.Combine(switching.Root, "config", "a.cfg")) && File.ReadAllText(Path.Combine(switching.Root, "config", "b.cfg")) == "value=B1", "A configs removed before B activation");
Check(!File.Exists(Path.Combine(switching.Root, "plugins", "A", "mod.dll")) && File.Exists(Path.Combine(switching.Root, "plugins", "B", "mod.dll")), "one plugin environment at a time");
Write(switching.Root, "config/b.cfg", "value=B-final"); Switch(aId);
Check(File.ReadAllText(Path.Combine(switching.Root, "config", "a.cfg")) == "value=A-final" && !File.Exists(Path.Combine(switching.Root, "config", "b.cfg")), "A-B-A restores final per-pack configs");
Check(Packages.ReadPlugins(Path.Combine(switching.Root, "plugins", "A", "mod.dll")).Single().Version == "1.1.0", "updated plugin version returns with old pack");
Check(File.ReadAllText(Path.Combine(switching.Root, "plugins", "Host", "host.txt")) == "fixed host", "host resources survive switches");
var faultTask = SwitchPreview(bId); faultTask.Request.Operation = "confirm-switch";
var faulty = PackTransactions.Prepare(switching, faultTask);
PackTransactions.StepProbe = step => { if (step.StartsWith("placed:")) throw new IOException("injected failure"); };
Reject(() => PackTransactions.Commit(switching, faulty), "deployment failure injected"); PackTransactions.StepProbe = null;
PackTransactions.Rollback(switching, faulty);
PackTransactions.Rollback(switching, faulty); checks++; // interrupted rollback can be resumed safely
Check(switching.Read().ActivePackId == aId && File.ReadAllText(Path.Combine(switching.Root, "config", "a.cfg")) == "value=A-final"
    && !File.Exists(Path.Combine(switching.Root, "plugins", "B", "mod.dll")), "failed switch restores identity and old files");
Plugin(switching.Root, "plugins/Unknown/unknown.dll", "unknown.guid", "1.0.0");
Check(SwitchPreview(bId).State == "preview", "new live DLL belongs to current environment without ownership prompt");
Write(switching.Root, "config/unknown.cfg", "value=unknown");
var unknownFile = Path.Combine(switching.Root, "plugins", "Unknown", "unknown.dll"); Files.MoveAside(unknownFile, Path.Combine(fixtures, "retired-unknown.dll"));
var configPreview = SwitchPreview(bId);
Check(configPreview.UnknownConfigurations.Count == 0, "new configs belong to current environment without ownership confirmation");
configPreview.Request.Operation = "confirm-switch";
var currentOwned = PackTransactions.Prepare(switching, configPreview);
Check(currentOwned.Originals.Any(f => f.Path == "config/unknown.cfg"), "exit-time inventory includes newly owned configuration");
var packQueue = new PackQueue(switching.Root);
Reject(() => packQueue.Handle(new PackRequest { Operation = "preview-switch", PackId = bId }, new UpdateSnapshot { Jobs = new() { new UpdateJob { State = UpdateStates.Queued } } }), "unfinished updates block switching");
Files.Save(switching.TaskPath, new PackTask { Id = Guid.NewGuid().ToString("N"), State = "waiting-exit" });
using (var worker = new QueueWorker(switching.Root))
{
    var command = new UpdateCommand { Id = Guid.NewGuid().ToString("N"), Action = "add", Job = new UpdateJob { Id = Guid.NewGuid().ToString("N") } };
    Files.Save(Path.Combine(switching.Root, "MCE", "commands", "check.json"), command); worker.ProcessCommands();
    Check(worker.Current.Jobs.Count == 0 && worker.Current.Error.Length == 0 && File.Exists(Path.Combine(switching.Root, "MCE", "commands", "check.json")), "pending switch defers update commands instead of rejecting them");
}
// Native enable/disable renames remain owned by their original pack.
var currentTask = Files.Read<PackTask>(switching.TaskPath); currentTask.State = "completed"; Files.Save(switching.TaskPath, currentTask);
string aDll = Path.Combine(switching.Root, "plugins", "A", "mod.dll"); File.Move(aDll, aDll + ".old");
Check(SwitchPreview(bId).State == "preview", "native disabled filename still belongs to current pack");
File.Move(aDll + ".old", aDll);
// A restart during a commit is detected before the next write; recovery waits for exit.
var restartTask = SwitchPreview(bId); restartTask.Request.Operation = "confirm-switch"; restartTask.Request.ConfirmedConfigFiles = restartTask.UnknownConfigurations;
var restarting = PackTransactions.Prepare(switching, restartTask); bool running = false;
Files.RunningProbe = _ => running;
PackTransactions.StepProbe = _ => running = true;
Reject(() => PackTransactions.Commit(switching, restarting), "process restart interrupts commit");
Reject(() => PackTransactions.Rollback(switching, restarting), "rollback never modifies a running environment");
running = false; PackTransactions.StepProbe = null; PackTransactions.Rollback(switching, restarting);
Check(switching.Read().ActivePackId == aId && File.Exists(aDll), "interrupted commit restores old environment after exit");
// Explicit pack identity can be updated in inventory while the current copy remains loaded.
string dataOnly = Path.Combine(fixtures, "data-only");
Write(dataOnly, "plugins/MCEPack/release.json", "{\"schemaVersion\":1,\"packId\":\"data.pack\",\"name\":\"Data pack\",\"defaultProfile\":\"p\"}");
Write(dataOnly, "plugins/MCEPack/profiles.json", "{\"schemaVersion\":1,\"profiles\":[{\"id\":\"p\",\"path\":\"profiles/p\"}]}");
Write(dataOnly, "plugins/MCEPack/profiles/p/profile.json", "{\"schemaVersion\":1,\"mods\":[{\"guid\":\"pack.a\",\"pluginVersion\":\"1.1.0\",\"enabled\":false}]}");
Write(dataOnly, "plugins/MCEPack/profiles/p/config-values.json", "{\"schemaVersion\":1,\"values\":[{\"configFile\":\"a.cfg\",\"section\":\"Main\",\"key\":\"Speed\",\"serializedValue\":\"42\"}]}");
Write(dataOnly, "config/a.cfg", "# preserve comment\n[Main]\nSpeed = old\nOther = keep\n");
var dataPreview = await PackImports.Preview(switching, new PackRequest { Source = dataOnly, HostPlugins = hosts }, new());
Check(dataPreview.Proposed.Problems.Count == 0 && dataPreview.Proposed.Files.Any(f => f.Path == "plugins/A/mod.dll.old"), "data-only import reuses exact local plugin and projects default disabled state");
string projected = File.ReadAllText(Path.Combine(dataPreview.StagingDirectory, "content", "config", "a.cfg"));
Check(projected.Contains("Speed = 42") && projected.Contains("Other = keep") && projected.Contains("# preserve comment"), "default snapshot overlays raw cfg and retains unrecorded settings");
PackImports.Commit(switching, dataPreview);
var updateIndex = switching.Read(); var existing = updateIndex.Packs.Single(p => p.Id == dataPreview.Proposed.Id); updateIndex.ActivePackId = existing.Id; switching.Save(updateIndex);
var secondData = await PackImports.Preview(switching, new PackRequest { Source = dataOnly, HostPlugins = hosts }, new()); PackImports.Commit(switching, secondData);
var pendingData = switching.Read().Packs.Single(p => p.Id == existing.Id);
Check(pendingData.Content == existing.Content && pendingData.PendingUpdate?.Content == secondData.Proposed.Content, "active pack import stages a pending revision without replacing current identity");
// Offline store responses exercise exact dependency versions, conflict detection and cycles.
byte[] Archive(string name, string version)
{
    string directory = Path.Combine(fixtures, "store-" + name + "-" + version);
    Plugin(directory, "mod.dll", "store." + name, version); Write(directory, "manifest.json", "{\"name\":\"" + name + "\",\"version_number\":\"" + version + "\"}");
    string archive = directory + ".zip"; ZipFile.CreateFromDirectory(directory, archive); return File.ReadAllBytes(archive);
}
var archives = new Dictionary<string, byte[]> { ["A-1.0.0"] = Archive("A", "1.0.0"), ["B-1.0.0"] = Archive("B", "1.0.0"), ["B-2.0.0"] = Archive("B", "2.0.0") };
string storeSource = Path.Combine(fixtures, "store-source"); Write(storeSource, "manifest.json", "{\"dependencies\":[\"Team-A-1.0.0\"]}");
var graph = new Dictionary<string, string[]> { ["A-1.0.0"] = new[] { "Team-B-1.0.0" } };
var storePreview = await ApprovedPreview(storage, new PackRequest { Source = storeSource }, new(), new FakeStore(graph, archives));
Check(storePreview.Proposed.Problems.Count == 0 && storePreview.Proposed.Files.Count(f => f.Path.EndsWith("mod.dll")) == 2, "fixed-version recursive store dependencies resolve offline");
Write(storeSource, "manifest.json", "{\"dependencies\":[\"Team-A-1.0.0\",\"Team-B-2.0.0\"]}");
var versionConflict = await ApprovedPreview(storage, new PackRequest { Source = storeSource }, new(), new FakeStore(graph, archives));
Check(versionConflict.Proposed.Problems.Count==0 && versionConflict.Proposed.Files.Any(f=>f.Path.Contains("Team-B")&&f.Path.EndsWith("mod.dll")), "author root B2 lock satisfies third-party B1 minimum");
Write(storeSource,"manifest.json","{\"dependencies\":[\"Team-B-1.0.0\",\"Team-B-2.0.0\"]}");
var rootConflict=await ApprovedPreview(storage,new PackRequest {Source=storeSource},new(),new FakeStore(graph,archives));
Check(rootConflict.Proposed.Problems.Any(p=>p.Contains("版本锁冲突")),"different explicit root locks remain a conflict");
graph["B-1.0.0"] = new[] { "Team-A-1.0.0" };
Write(storeSource, "manifest.json", "{\"dependencies\":[\"Team-A-1.0.0\"]}");
var cycle = await ApprovedPreview(storage, new PackRequest { Source = storeSource }, new(), new FakeStore(graph, archives));
Check(cycle.Proposed.Problems.Any(p => p.Contains("循环")), "dependency cycles are reported without recursion overflow");
PackImports.Commit(storage, cycle);
Reject(() => storage.Validate(cycle.Proposed), "incomplete dependency pack can be stored but cannot activate");
string fullLocal = Path.Combine(fixtures, "full-local");
Plugin(fullLocal, "plugins/B/mod.dll", "store.B", "1.0.0"); Write(fullLocal, "plugins/B/manifest.json", "{\"namespace\":\"Team\",\"name\":\"B\",\"version_number\":\"1.0.0\"}");
Write(fullLocal, "manifest.json", "{\"dependencies\":[\"Team-B-1.0.0\"]}");
var inlineDependency = await ApprovedPreview(storage, new PackRequest { Source = fullLocal }, new(), new FakeStore(new(), new()));
Check(inlineDependency.Proposed.Problems.Count == 0 && inlineDependency.Proposed.Files.Count(f => f.Path.EndsWith("mod.dll")) == 1, "complete local packs satisfy declared dependencies without downloading duplicate copies");
var corruptStorage = new PackStorage(Root("corrupt-transaction"));
var corruptTask = new PackTask { Id = Guid.NewGuid().ToString("N"), State = "waiting-exit" };
Files.Save(corruptStorage.TaskPath, corruptTask); Write(corruptStorage.Home, ".transactions/" + corruptTask.Id + "/switch.json", "{broken");
var corruptQueue = new PackQueue(corruptStorage.Root); await corruptQueue.Pump(new());
Check(Files.Read<PackTask>(corruptStorage.TaskPath).State == "recovery-required" && corruptQueue.BlocksUpdates, "damaged transaction blocks further updates and switches");
Reject(() => corruptQueue.Handle(new PackRequest { Operation = "preview-import", Source = incoming }, new()), "damaged transaction cannot be bypassed by starting a different pack action");
var replacement = await PackImports.Preview(storage, new PackRequest { Source = incoming }, new());
Write(Path.Combine(replacement.StagingDirectory, "content"), "plugins/unreviewed.dll", "unexpected");
string priorRevision = storage.Read().Packs.Single(p => p.Id == replacement.Proposed.Id).Content;
Reject(() => PackImports.Commit(storage, replacement), "modified staging cannot overwrite an existing inventory entry");
Check(storage.Read().Packs.Single(p => p.Id == replacement.Proposed.Id).Content == priorRevision, "failed inventory replacement preserves old revision");
var ownershipStorage = new PackStorage(Root("update-ownership")); string ownedId = Guid.NewGuid().ToString("N");
ownershipStorage.Save(new PackIndex { ActivePackId = ownedId, Packs = new() { new PackRecord { Id = ownedId } }, ActiveFiles = new() { new PackFile { Path = "plugins/Feature/old.dll", Hash = "old" } } });
var ownership = new Transaction { Originals = new() { new FileChange { Target = Path.Combine(ownershipStorage.Root, "plugins", "Feature", "old.dll"), Hash = "old" } },
    Desired = new() { new FileChange { Target = Path.Combine(ownershipStorage.Root, "plugins", "Feature", "new.dll"), Hash = "new" }, new FileChange { Target = Path.Combine(ownershipStorage.Root, "plugins", "Feature", "new-resource.txt"), Hash = "resource" } } };
PackTransactions.RecordUpdate(ownershipStorage.Root, ownership);
Check(ownershipStorage.Read().ActiveFiles.Any(p => p.Path.EndsWith("new-resource.txt")) && ownershipStorage.Read().ActiveFiles.All(p => !p.Path.EndsWith("old.dll")), "updates record renamed plugins and new resources as current-pack ownership");
PackTransactions.RecordUpdate(ownershipStorage.Root, ownership, true);
Check(ownershipStorage.Read().ActiveFiles.Single().Path.EndsWith("old.dll"), "update rollback restores original pack ownership");
// Verify the exact existing Newtonsoft signing format, including Unicode paths.
string signed = Path.Combine(fixtures, "signed"); Write(signed, "release.json", "{\"signed\":true}"); Write(signed, "目录/图片.txt", "signed content");
var signedFiles = new Newtonsoft.Json.Linq.JArray();
foreach (var pair in Files.Inventory(signed).OrderBy(p => p.Key, StringComparer.Ordinal))
    signedFiles.Add(new Newtonsoft.Json.Linq.JObject { ["path"] = pair.Key.Replace('\\', '/'), ["sha256"] = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Files.Child(signed, pair.Key)))) });
using (var rsa = System.Security.Cryptography.RSA.Create(2048))
{
    var key = rsa.ExportParameters(false);
    var signature = new Newtonsoft.Json.Linq.JObject { ["algorithm"] = "RSA-SHA256-PKCS1", ["publicKey"] = new Newtonsoft.Json.Linq.JObject { ["Exponent"] = Convert.ToBase64String(key.Exponent!), ["Modulus"] = Convert.ToBase64String(key.Modulus!) }, ["files"] = signedFiles,
        ["signature"] = Convert.ToBase64String(rsa.SignData(System.Text.Encoding.UTF8.GetBytes(signedFiles.ToString(Newtonsoft.Json.Formatting.None)), System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1)) };
    Write(signed, "signature.json", signature.ToString());
}
PackSignature.Verify(signed); checks++;
Write(signed, "目录/图片.txt", "tampered"); Reject(() => PackSignature.Verify(signed), "changed signed author data cannot be imported");
Files.RunningProbe = _ => false;
// Real-world shape: Core wants M>=2, its own dependency Fix wants M 1. The higher version must win without a conflict.
{
    var more = new Dictionary<string, byte[]>(archives) { ["Core-1.0.0"] = Archive("Core", "1.0.0"), ["Fix-1.0.0"] = Archive("Fix", "1.0.0"), ["M-1.0.0"] = archives["B-1.0.0"], ["M-2.0.0"] = archives["B-2.0.0"] };
    var deps = new Dictionary<string, string[]> { ["Core-1.0.0"] = new[] { "Team-Fix-1.0.0", "Team-M-2.0.0" }, ["Fix-1.0.0"] = new[] { "Team-M-1.0.0" } };
    string mixed = Path.Combine(fixtures, "store-mixed"); Plugin(mixed, "plugins/Own/own.dll", "own.mod", "1.0.0"); Write(mixed, "manifest.json", "{\"dependencies\":[\"Team-Core-1.0.0\"]}");
    var higher = await ApprovedPreview(storage, new PackRequest { Source = mixed }, new(), new FakeStore(deps, more));
    Check(higher.Proposed.Problems.Count == 0, "two components asking for different versions resolve to the higher one");
    var m = higher.Report.Lines.Single(l => l.Section == "download" && l.Key == "Team-M");
    Check(m.Title.EndsWith("2.0.0") && m.Detail.Contains("Core") && m.Detail.Contains("Fix") && m.Detail.Contains("最高版本"), "the sources of both requirements are named");
    Check(higher.Report.Kind == "store" && higher.Report.KindText.Contains("联网"), "store-style pack says it needs the network");
    Write(mixed, "manifest.json", "{\"dependencies\":[\"Team-Core-1.0.0\",\"Team-M-1.0.0\"]}");
    var pinned = await ApprovedPreview(storage, new PackRequest { Source = mixed }, new(), new FakeStore(deps, more));
    var pinnedProblem = pinned.Report.Lines.Single(l => l.Section == "problem");
    Check(pinnedProblem.Detail.Contains("Core") && pinnedProblem.Detail.Contains("作者") && pinnedProblem.Hint.Length > 0, "an author pin that is too low names the component that needs more");
    Check(!pinnedProblem.Ignorable, "version conflicts cannot be ignored");
}
// Optional live probe: MCE_PROBE_ZIP=<path> prints what the player would see for a real pack (needs the network).
if (Environment.GetEnvironmentVariable("MCE_PROBE_ZIP") is { Length: > 0 } probeZip)
{
    var probeStorage = new PackStorage(Root("probe"));
    foreach (string sub in new[] { "MonoDetour-MonoDetour", "MonoDetour-MonoDetour_BepInEx_5", "AutoHookGenPatcher-1.1.1" }) PackStorage.CopyTree(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../BepInEx/core", sub)), Path.Combine(probeStorage.Root, "core", sub));
    foreach (string dll in Directory.GetFiles(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../BepInEx/core")), "*.dll")) { Directory.CreateDirectory(Path.Combine(probeStorage.Root, "core")); File.Copy(dll, Path.Combine(probeStorage.Root, "core", Path.GetFileName(dll)), true); }
    var live = await PackImports.Preview(probeStorage, new PackRequest { Source = probeZip }, new());
    if (Environment.GetEnvironmentVariable("MCE_PROBE_AUDIT") == "1")
    {
        Check(live.Proposed.Problems.Count == 0, "real Thunderstore pack imports without blockers");
        string prepared = Path.Combine(live.StagingDirectory, "content"); int compared = 0;
        using (var originalZip = ZipFile.OpenRead(probeZip))
        foreach (var entry in originalZip.Entries.Where(e => e.FullName.StartsWith("BepInEx/config/") && e.Name.Length > 0))
        {
            string expected = entry.FullName[8..]; using var source = entry.Open(); using var bytes = new MemoryStream(); source.CopyTo(bytes);
            Check(File.ReadAllBytes(Files.Child(prepared, expected)).SequenceEqual(bytes.ToArray()), "real pack config preserved byte-for-byte: " + expected); compared++;
        }
        Check(compared == 5, "all five upstream modpack config files were preserved");
        var paths = Files.Inventory(prepared); var actualPlugins = paths.Keys.Where(Packages.IsAssembly).SelectMany(f => Packages.ReadPlugins(Files.Child(prepared, f))).ToList();
        Check(actualPlugins.Count >= 5 && actualPlugins.GroupBy(p => p.Guid).All(g => g.Count() == 1), "all declared mods present with no duplicate plugin identities");
        Check(paths.Keys.All(p => !p.StartsWith("core/", StringComparison.OrdinalIgnoreCase)), "import never copies loader/core files into managed content");
        PackImports.Commit(probeStorage, live); probeStorage.Validate(probeStorage.Read().Packs.Single());
        string exported = Path.Combine(fixtures, "real-pack-content-export.zip"); ZipFile.CreateFromDirectory(probeStorage.Content(live.Proposed), exported);
        string unpacked = Path.Combine(fixtures, "real-pack-content-reimport"); PackImports.Extract(exported, unpacked);
        Check(Files.Inventory(unpacked).OrderBy(p => p.Key).SequenceEqual(Files.Inventory(prepared).OrderBy(p => p.Key)), "stored-content ZIP round-trip has identical paths and file bytes");
        var offlineReimport = await ApprovedPreview(probeStorage, new PackRequest { Source = exported }, new(), new FakeStore(new(), new()));
        Console.WriteLine("REIMPORT problems: " + string.Join(" | ", offlineReimport.Proposed.Problems));
        Check(offlineReimport.Proposed.Problems.Count == 0, "exported stored-content package reimports offline through production importer");
        Check(offlineReimport.Proposed.Files.OrderBy(f => f.Path).Select(f => f.Path + "|" + f.Hash).SequenceEqual(live.Proposed.Files.OrderBy(f => f.Path).Select(f => f.Path + "|" + f.Hash)), "second import preserves the complete managed file set and bytes");
        File.WriteAllText(Path.Combine(fixtures, "real-pack-location.txt"), probeStorage.Root);
        Console.WriteLine("REAL AUDIT files=" + paths.Count + ", plugins=" + actualPlugins.Count + ", configs=" + compared + ", export=" + exported);
    }
    Console.WriteLine("PROBE verdict=" + live.Report.Verdict + " | " + live.Report.KindText);
    foreach (var fvp in live.Report.FolderVersions.Take(8)) Console.WriteLine("PROBE folder " + fvp.Key + " = " + fvp.Value); Console.WriteLine("PROBE folders with a version: " + live.Report.FolderVersions.Count);
    foreach (var l in live.Report.Lines.Where(l => l.Section != "mod")) Console.WriteLine("PROBE [" + l.Section + "/" + l.State + "] " + l.Group + " | " + l.Title + " | " + l.Detail + (l.Hint.Length > 0 ? " | " + l.Hint : ""));
}
// Demo/profile-format shipped inside a mod package is documentation and must not be mistaken for the pack's own profile.
{
    string withDemo = Path.Combine(fixtures, "with-demo");
    Plugin(withDemo, "plugins/Own/own.dll", "own.demo", "1.0.0");
    Write(withDemo, "plugins/Own/Demo/profile-format/profiles/example/profile.json", "{\"mods\":[{\"guid\":\"example.plugin.guid\",\"enabled\":true}]}");
    Write(withDemo, "plugins/Own/Demo/profile-format/profiles/example/config-values.json", "{}");
    var demo = await PackImports.Preview(storage, new PackRequest { Source = withDemo }, new());
    Check(demo.Proposed.Problems.Count == 0 && !demo.Proposed.Files.Any(f => f.Path.Contains("profile-format")), "bundled Demo profile examples are not treated as pack data");
    // 2026-10-28 FinalChecks: without an explicit MCEEnvironment.json the pack is legacy even when every file is inside; "offline/complete environment" is no longer claimed from file completeness.
    Check(demo.Report.KindText.Contains("legacy") && !demo.Report.KindText.StartsWith("类型：独立完整环境") && !demo.Report.KindText.Contains("离线"), "a pack with every file inside but no environment description is labelled legacy, not complete/offline");
    Check(demo.Proposed.EnvironmentVersion == 1 && demo.Proposed.MinimumWorkerProtocol == 1 && !PackEnvironmentPolicy.IsComplete(demo.Proposed), "a pack without an environment description stays EnvironmentVersion 1");
    // Counterexample: file completeness alone (core + host + plugin present) never creates a complete environment.
    string coreOnlyFull = Path.Combine(fixtures, "files-complete-no-description");
    Plugin(coreOnlyFull, "plugins/Own/own.dll", "own.nodesc", "1.0.0"); Write(coreOnlyFull, "core/BepInEx.dll", "core-file"); Write(coreOnlyFull, "core/0Harmony.dll", "harmony-file"); Write(coreOnlyFull, "patchers/p.dll", "patcher-file");
    var noDescription = await PackImports.Preview(storage, new PackRequest { Source = coreOnlyFull }, new());
    Check(noDescription.Proposed.EnvironmentVersion == 1 && !PackEnvironmentPolicy.IsComplete(noDescription.Proposed) && !noDescription.Report.KindText.StartsWith("类型：独立完整环境") && noDescription.Report.KindText.Contains("legacy"), "file completeness without MCEEnvironment.json is never classified as a complete environment");
    // Counterexample: an explicit description is mandatory and must be valid; a v1 description is refused instead of silently treated as legacy-or-complete.
    string badDescription = Path.Combine(fixtures, "bad-environment-description");
    Plugin(badDescription, "plugins/Own/own.dll", "own.baddesc", "1.0.0");
    Write(badDescription, "MCEEnvironment.json", "{\"SchemaVersion\":2,\"EnvironmentVersion\":1,\"MinimumWorkerProtocol\":2,\"Files\":[],\"Ledger\":{\"SchemaVersion\":1,\"Revision\":0,\"Packages\":[]}}");
    bool badRejected = false; try { var bad = await PackImports.Preview(storage, new PackRequest { Source = badDescription }, new()); badRejected = bad.Proposed.Problems.Count > 0 && !PackEnvironmentPolicy.IsComplete(bad.Proposed); } catch (IOException) { badRejected = true; }
    Check(badRejected, "an invalid explicit environment description is rejected, never imported as complete");
    string dataOnlyPack = Path.Combine(fixtures, "data-only-kind");
    Write(dataOnlyPack, "MCEPack/release.json", "{\"packId\":\"kind.test\",\"name\":\"K\",\"version\":\"1\"}"); Write(dataOnlyPack, "MCEPack/profiles.json", "{\"schemaVersion\":1,\"profiles\":[]}");
    var kind = await PackImports.Preview(storage, new PackRequest { Source = dataOnlyPack }, new());
    Check(kind.Report.Kind == "data" && kind.Report.KindText.Contains("作者资料包"), "an author-data-only pack is labelled as such");
}
// Pending restart does not block. A second rejected request must retain the original requested task.
{
    var queue = new PackQueue(switching.Root);
    // Post-fix (CommercialAudit 2026-10-28): only a pending-restart update whose durable transaction is finalized ("installed") may be waived.
    string restartId = Guid.NewGuid().ToString("N");
    var restart = new UpdateSnapshot { Jobs = { new UpdateJob { Id = restartId, DisplayName = "已装好待核对", State = UpdateStates.PendingRestart } } };
    var unfinalized = new UpdateSnapshot { Jobs = { new UpdateJob { Id = Guid.NewGuid().ToString("N"), DisplayName = "未定稿的并行候选", State = UpdateStates.PendingRestart } } };
    try { queue.Handle(new PackRequest { Operation = "preview-switch", PackId = bId, HostPlugins = hosts, SourceProcessId = 0 }, unfinalized); Check(false, "a pending-restart update without a finalized transaction must block"); } catch (IOException) { checks++; }
    Files.Save(Path.Combine(switching.Root, "MCE", "jobs", restartId, "transaction.json"), new Transaction { JobId = restartId, Phase = "installed" });
    queue.Handle(new PackRequest { Operation = "preview-switch", PackId = bId, HostPlugins = hosts, SourceProcessId = 0 }, restart);
    Check(Files.Read<PackTask>(switching.TaskPath).State == "requested", "a pending-restart update with a finalized (installed) transaction does not block previewing a switch");
    string requestedTask = File.ReadAllText(switching.TaskPath);
    var open = new UpdateSnapshot { Jobs = { new UpdateJob { Id = Guid.NewGuid().ToString("N"), DisplayName = "正在下载的更新", State = UpdateStates.Downloading } } };
    try { queue.Handle(new PackRequest { Operation = "preview-switch", PackId = bId, HostPlugins = hosts }, open); Check(false, "unfinished update must block"); } catch (IOException) { checks++; }
    var refused = Files.Read<PackTask>(switching.TaskPath);
    Check(refused.State == "requested" && File.ReadAllText(switching.TaskPath) == requestedTask, "a refused request retains the original queued task unchanged");
}
// Language files: grouped by the shared naming standard, and the game language picks the matching group automatically.
{
    var langs = Path.Combine(fixtures, "lang-pack");
    foreach (string name in new[] { "en-us.txt", "zh-cn.txt", "zh-cn_补翻_20261010.txt", "zh-tw.txt", "zh-cn-old.txt" }) Write(langs, "MCEPack/language/" + name, "// display: " + name + "\nMCE_SEARCH=x");
    Write(langs, "MCEPack/release.json", "{\"packId\":\"lang.test\",\"name\":\"L\",\"version\":\"1\"}"); Write(langs, "MCEPack/profiles.json", "{\"schemaVersion\":1,\"profiles\":[]}");
    var groups = PackLanguages.Scan(Path.Combine(langs, "MCEPack/language"));
    Check(groups.Select(g => g.Key).OrderBy(x => x).SequenceEqual(new[] { "en-us", "zh-cn", "zh-cn-old", "zh-tw" }), "language files are grouped by the shared naming standard");
    Check(groups.Single(g => g.Key == "zh-cn").Files.Count == 2, "a supplement file joins its base group");
    Check(PackLanguages.Choose(groups, "zh-tw")!.Key == "zh-tw" && PackLanguages.Choose(groups, "zh-cn")!.Key == "zh-cn" && PackLanguages.Choose(groups, "fr-fr")!.Key == "en-us", "the game language chooses the pure group, falling back to English");
    var picked = await PackImports.Preview(storage, new PackRequest { Source = langs, GameLanguage = "zh-tw" }, new());
    Check(picked.Proposed.Language == "zh-tw" && picked.Report.Languages.Count == 4, "the preview records the language chosen from the game language");
    var other = await PackImports.Preview(storage, new PackRequest { Source = langs, GameLanguage = "en-us" }, new());
    Check(other.Proposed.Language == "en-us", "another game language chooses its own group");
    // Full chain: preview → the player picks another group → confirm → stored with that group.
    var chain = new PackStorage(Root("language-chain")); var chainQueue = new PackQueue(chain.Root);
    var chainPreview = await PackImports.Preview(chain, new PackRequest { Operation = "preview-import", Source = langs, GameLanguage = "zh-cn" }, new());
    Check(chainPreview.Proposed.Language == "zh-cn", "chain starts from the automatic choice");
    chainQueue.Handle(new PackRequest { Operation = "confirm-import", PreviewId = chainPreview.Id, PackLanguage = "en-us" }, new UpdateSnapshot());
    PackImports.Commit(chain, Files.Read<PackTask>(chain.TaskPath));
    Check(chain.Read().Packs.Single().Language == "en-us", "the language picked in the preview is stored with the pack");
}
// Folder versions for the file tree, and a plain refusal when switching before the current setup was recorded.
{
    string withManifest = Path.Combine(fixtures, "folder-version");
    Plugin(withManifest, "plugins/Group/Own/own.dll", "own.fv", "1.2.3"); Write(withManifest, "plugins/Group/Own/manifest.json", "{\"name\":\"Own\",\"version_number\":\"9.9.9\"}");
    Plugin(withManifest, "plugins/Group/Bare/bare.dll", "bare.fv", "0.4.0");
    var fv = await PackImports.Preview(storage, new PackRequest { Source = withManifest }, new());
    Check(fv.Report.FolderVersions.TryGetValue("plugins/Group/Own", out var own) && own == "9.9.9", "the file tree gets a folder's version from its manifest");
    Check(fv.Report.FolderVersions.TryGetValue("plugins/Group/Bare", out var bare) && bare == "0.4.0", "a folder without a manifest falls back to the plugin version");
    var fresh = new PackStorage(Root("not-recorded")); Write(fresh.Root, "plugins/X/x.txt", "x");
    try { PackTransactions.Preview(fresh, new PackRequest { Operation = "preview-switch", PackId = new string('a', 32) }); Check(false, "switch before recording must be refused"); }
    catch (IOException refused) { Check(refused.Message.Contains("保存当前配置为临时整合包") && !refused.Message.Contains("接管"), "the refusal says to record the current setup, in plain words"); }
}
// --- readable preview report ---
{
    var okLine = preview.Report; // first fixture preview: valid local pack
    Check(okLine.Verdict == "ready" && okLine.Headline.Contains("未发现问题") && okLine.FileCount == preview.Proposed.Files.Count, "valid pack reports a plain-language ready verdict");
    Check(okLine.Files.All(f => f.Group.Length > 0 && f.Path.Length > 0) && okLine.Files.Any(f => f.Group == "模组文件"), "report files carry a group for the tree");
    Check(okLine.Lines.Any(l => l.Section == "mod" && l.Group == "Feature"), "mods are grouped by the author's folder");
    var bad = PackReportText.Problem("商店依赖未补齐：已选择版本低于传递依赖要求：MonoDetour-MonoDetour_BepInEx_5 0.6.6 < 0.6.7");
    Check(bad.Title.Contains("MonoDetour BepInEx 5") && bad.Detail.Contains("0.6.7") && bad.Hint.Length > 0 && !bad.Title.Contains("传递依赖"), "version conflict is explained without jargon");
    Check(PackReportText.Problem("缺少预装加载器：BepInEx-BepInExPack_PEAK-5.4.2403").Title.Contains("BepInEx"), "missing loader is explained");
    Check(PackReportText.Problem("缺少模组：some.guid").Hint.Length > 0, "missing mod gets a what-to-do hint");
    Check(duplicates.Report.Verdict == "attention" && duplicates.Report.Lines.Any(l => l.Section == "problem" && l.Title.Contains("重复") ), "problem pack reports attention with a readable card");
    Check(storePreview.Report.Lines.Count(l => l.Section == "download") >= 2 && storePreview.Report.DownloadCount >= 2, "downloaded dependencies are listed once each");
    Check(storePreview.Report.Lines.Where(l => l.Section == "download").GroupBy(l => l.Title).All(g => g.Count() == 1), "no repeated dependency rows");
}
{
    // No player-facing line may carry the old developer wording.
    string[] banned = { "加载器要求", "仅检查，不写入", "传递依赖", "仅核对本机", "不部署", "未补齐", "库存", "替你", "你的电脑", "你现在", "怎么办", "MCE 不会改动它" };
    var everything = new[] { preview, duplicates, storePreview, versionConflict, cycle }.SelectMany(x => x.Report.Lines.SelectMany(l => new[] { l.Title, l.Detail, l.Hint }).Concat(new[] { x.Report.Headline }));
    Check(preview.Report.Kind == "data" || preview.Report.Kind == "full", "pack type is always stated");
    Check(everything.All(s => banned.All(b => !s.Contains(b))), "preview text uses neutral wording");
    Check(cycle.Report.Lines.Any(l => l.Section == "problem" && l.Hint.Length > 0), "dependency loop explains who must fix it");
}
// Named temporary save: preview while running, final capture after exit, and an explicit completion result.
{
    var temp = new PackStorage(Root("named-temporary")); var queue = new PackQueue(temp.Root);
    Write(temp.Root, "plugins/Host/host.txt", "fixed");
    Plugin(temp.Root, "plugins/Feature/feature.dll", "temporary.feature", "1.0.0");
    Write(temp.Root, "config/feature.cfg", "value=before-exit");
    var request = new PackRequest { Operation = "preview-adopt", SnapshotName = "  测试用临时整合包  ", HostPlugins = hosts, HostRoots = new() { "plugins/Host" } };
    queue.Handle(request, new()); await queue.Pump(new());
    var saving = Files.Read<PackTask>(temp.TaskPath);
    Check(saving.Proposed.Name == "测试用临时整合包" && saving.Report.Name == saving.Proposed.Name, "the entered temporary pack name survives preview");
    Check(saving.UnknownConfigurations.Count == 0 && saving.Report.Files.Count == 2, "initial config files are included in preview with no ownership confirmation");
    queue.Handle(new PackRequest { Operation = "confirm-adopt", PreviewId = saving.Id, HostPlugins = hosts }, new());
    Files.RunningProbe = _ => true; await queue.Pump(new());
    Check(Files.Read<PackTask>(temp.TaskPath).State == "completed" && temp.Read().ActivePackId.Length > 0, "snapshot completes while the game is still running");
    Write(temp.Root, "config/feature.cfg", "value=after-save");
    Files.RunningProbe = _ => false;
    var result = Files.Read<PackTask>(temp.TaskPath); var saved = temp.Read().Packs.Single();
    Check(result.State == "completed" && result.Message.Contains("测试用临时整合包") && result.Message.Contains("保存完成"), "completion names the saved temporary pack and explicitly reports success");
    Check(File.ReadAllText(Path.Combine(temp.Content(saved), "config/feature.cfg")) == "value=before-exit", "snapshot preserves the point-in-time version instead of waiting for exit");
    Check(File.ReadAllText(Path.Combine(temp.Root, "config/feature.cfg")) == "value=after-save", "saving never replaces active game files");
    Check(saved.Report?.Files.Count == 2, "snapshot report is stored for later browsing without an original ZIP");
    var another = PackTransactions.Preview(temp, new PackRequest { Operation = "preview-adopt", SnapshotName = "第二份快照", HostPlugins = hosts, HostRoots = new() { "plugins/Host" } });
    another.Request.Operation = "confirm-adopt"; PackTransactions.SaveTemporary(temp, another);
    Check(temp.Read().Packs.Count == 2 && temp.Read().ActivePackId == saved.Id, "a later named snapshot does not change the active pack identity");
    var emptyName = PackTransactions.Preview(new PackStorage(Root("default-temp-name")), new PackRequest { Operation = "preview-adopt", HostPlugins = hosts, HostRoots = new() { "plugins/Host" } });
    Check(emptyName.Proposed.Name == "临时整合包", "legacy requests get a readable default temporary pack name");
    Reject(() => PackTransactions.Preview(new PackStorage(Root("invalid-temp-name")), new PackRequest { Operation = "preview-adopt", SnapshotName = "bad\nname", HostPlugins = hosts, HostRoots = new() { "plugins/Host" } }), "temporary pack names reject control characters");
}
// Detail loading must return immediately, deduplicate work, and keep revisions and selections independent.
{
    var cache = new ModConfigEnhance.Overview.PackDetailsCache<string>();
    using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
    int calls = 0, uiThread = Environment.CurrentManagedThreadId, workerThread = uiThread;
    var slow = cache.Get("A:revision1", () => { Interlocked.Increment(ref calls); workerThread = Environment.CurrentManagedThreadId; started.Set(); release.Wait(); return "A"; });
    Check(started.Wait(3000) && !slow.IsCompleted, "cold detail loading starts in the background without waiting for the scan");
    Check(workerThread != uiThread, "assembly scanning runs off the caller/UI thread");
    var again = cache.Get("A:revision1", () => "must not run");
    Check(ReferenceEquals(slow, again), "repeat selection shares the in-flight load");
    var selectedB = cache.Get("B:revision1", () => "B");
    Check(await selectedB == "B" && !slow.IsCompleted, "a slow previous selection does not block the newly selected pack");
    release.Set(); Check(await slow == "A" && await selectedB == "B", "late completion of A cannot overwrite the result for B");
    Check(ReferenceEquals(cache.Get("A:revision1", () => "wrong"), slow) && calls == 1, "returning to a loaded pack reuses its cached result");
    Check(await cache.Get("A:revision2", () => "A2") == "A2", "updating one revision causes only that revision to reload");
    cache.Invalidate("A:revision1"); Check(await cache.Get("A:revision1", () => "refreshed") == "refreshed" && ReferenceEquals(cache.Get("B:revision1", () => "wrong"), selectedB), "manual refresh invalidates only the selected revision");
}
// Personal edits create an independent library record and preserve the original/signed material.
{
    var ownStore = new PackStorage(Root("personal-copy"));
    string source = Path.Combine(fixtures, "personal-source");
    Write(source, "MCEPack/release.json", "{\"schemaVersion\":1,\"packId\":\"personal.test\",\"name\":\"Author\",\"signed\":false}");
    Write(source, "MCEPack/profiles.json", "{\"schemaVersion\":1,\"profiles\":[]}");
    Write(source, "MCEPack/introduction.md", "# Original"); Write(source, "config/own.cfg", "[Main]\nValue = original\n");
    var personalImport = await PackImports.Preview(ownStore, new PackRequest { Source = source }, new()); PackImports.Commit(ownStore, personalImport);
    var original = ownStore.Read().Packs.Single(); string originalRoot = ownStore.Content(original);
    var before = Files.Inventory(originalRoot);
    string drafts = Path.Combine(ownStore.Home, ".editor-drafts", Guid.NewGuid().ToString("N"));
    Write(drafts, "config/own.cfg", "[Main]\nValue = changed\n");
    var copy = PackPersonalCopy.Save(ownStore, new PackRequest { PackId = original.Id, Source = drafts, SnapshotName = "Edited personal copy" });
    Check(copy.Id != original.Id && copy.PublicationId == "" && ownStore.Read().Packs.Count == 2, "editing creates a separate personal library record");
    Check(File.ReadAllText(Path.Combine(ownStore.Content(copy), "config/own.cfg")).Contains("changed"), "personal copy contains staged edits");
    Check(Files.Inventory(originalRoot).OrderBy(x => x.Key).SequenceEqual(before.OrderBy(x => x.Key)) && ownStore.Read().ActivePackId == "", "author original and active environment are unchanged");
    ownStore.Validate(copy); checks++;
    Reject(() => PackPersonalCopy.Save(ownStore, new PackRequest { PackId = original.Id, Source = source }), "personal copy rejects drafts outside editor workspace");
    string illegal = Path.Combine(ownStore.Home, ".editor-drafts", Guid.NewGuid().ToString("N")); Write(illegal, original.AuthorRoot + "/release.json", "{}");
    Reject(() => PackPersonalCopy.Save(ownStore, new PackRequest { PackId = original.Id, Source = illegal }), "personal copy does not accept structural author records as text edits");
}
// ---- Download consent: Preview never downloads; only the exact, previously shown list may be replayed.
{
    var consentStorage = new PackStorage(Root("consent"));
    var consentStore = new FakeStore(new() { ["A-1.0.0"] = new[] { "Team-B-1.0.0" } }, archives);
    string consentSource = Path.Combine(fixtures, "consent-source");
    Plugin(consentSource, "plugins/Own/own.dll", "consent.own", "1.0.0"); Write(consentSource, "manifest.json", "{\"dependencies\":[\"Team-A-1.0.0\"]}");
    var consentRequest = new PackRequest { Operation = "preview-import", Source = consentSource };
    var waiting = await PackImports.Preview(consentStorage, consentRequest, new(), consentStore);
    string downloads = Path.Combine(consentStorage.Root, "MCE", "downloads");
    Check(waiting.State == "preview" && waiting.PendingDownloads.Count == 2 && waiting.PendingDownloads.All(d => d.Url.StartsWith("https://fixture.test/")), "a store pack preview lists every dependency waiting for consent");
    Check(consentStore.ZipDownloads == 0, "preview downloads zero ZIPs without consent");
    Check(!Directory.Exists(downloads) || Directory.GetFiles(downloads).Length == 0, "preview leaves the download cache empty");
    Check(waiting.DownloadPlan.Length > 0 && waiting.DownloadPlan == PackImports.PlanHash(waiting.PendingDownloads) && waiting.Report.DownloadCount == 2, "the consent plan hash matches the displayed list");
    Check(!waiting.Proposed.Files.Any(f => f.Path.Contains("Team-")), "unapproved dependencies add no files to the proposed pack");
    Reject(() => PackImports.Commit(consentStorage, waiting), "an unapproved preview cannot be committed");
    Check(consentStorage.Read().Packs.Count == 0, "failed commit registers nothing");
    var consentQueue = new PackQueue(consentStorage.Root);
    Reject(() => consentQueue.Handle(new PackRequest { Operation = "confirm-import", PreviewId = waiting.Id }, new UpdateSnapshot()), "confirm-import is refused while downloads await consent");
    void Restore() => Files.Save(consentStorage.TaskPath, waiting);
    Restore();
    Reject(() => consentQueue.Handle(new PackRequest { Operation = "confirm-download", PreviewId = waiting.Id, Approved = true, ApprovedPlan = "ABCDEF" }, new UpdateSnapshot()), "consent for a different plan hash is refused");
    Reject(() => consentQueue.Handle(new PackRequest { Operation = "confirm-download", PreviewId = waiting.Id, Approved = false, ApprovedPlan = waiting.DownloadPlan }, new UpdateSnapshot()), "a confirmation without Approved=true is refused");
    Reject(() => consentQueue.Handle(new PackRequest { Operation = "confirm-download", PreviewId = waiting.Id, Approved = true, ApprovedPlan = waiting.DownloadPlan, Source = Path.Combine(fixtures, "other-source") }, new UpdateSnapshot()), "consent cannot swap the import source");
    Reject(() => consentQueue.Handle(new PackRequest { Operation = "confirm-download", PreviewId = new string('a', 32), Approved = true, ApprovedPlan = waiting.DownloadPlan }, new UpdateSnapshot()), "consent for another preview id is refused");
    var untouched = Files.Read<PackTask>(consentStorage.TaskPath);
    Check(untouched.State == "preview" && !untouched.Request.Approved && untouched.Request.ApprovedPlan.Length == 0, "refused consent never marks the stored preview approved");
    Restore();
    var tampered = Files.Read<PackTask>(consentStorage.TaskPath); tampered.PendingDownloads[0].Url = "https://evil.test/swapped.zip"; Files.Save(consentStorage.TaskPath, tampered);
    Reject(() => consentQueue.Handle(new PackRequest { Operation = "confirm-download", PreviewId = waiting.Id, Approved = true, ApprovedPlan = waiting.DownloadPlan }, new UpdateSnapshot()), "a download list edited after preview no longer matches its plan");
    Check(consentStore.ZipDownloads == 0, "rejected consent attempts download zero ZIPs");
    Restore();
    // Replays with a list that differs from what Preview resolved are rejected before any ZIP is requested.
    var swapped = waiting.PendingDownloads.Select(d => new PackDownloadItem { Key = d.Key, Version = d.Version, Url = "https://evil.test/" + d.Key + ".zip" }).ToList();
    var replaySwapped = await PackImports.Preview(consentStorage, consentRequest, new(), consentStore, swapped);
    Check(replaySwapped.Proposed.Problems.Any(p => p.Contains("依赖清单与已同意的预览不一致")) && replaySwapped.PendingDownloads.Count == 0 && consentStore.ZipDownloads == 0, "an approved list with changed sources is rejected without touching ZIPs");
    var replayShort = await PackImports.Preview(consentStorage, consentRequest, new(), consentStore, waiting.PendingDownloads.Where(d => d.Key != "Team-B").ToList());
    Check(replayShort.Proposed.Problems.Any(p => p.Contains("依赖清单与已同意的预览不一致")) && consentStore.ZipDownloads == 0, "an approved list missing a dependency is rejected before any download");
    var replayEmpty = await PackImports.Preview(consentStorage, consentRequest, new(), consentStore, new List<PackDownloadItem>());
    Check(replayEmpty.Proposed.Problems.Any(p => p.Contains("依赖清单与已同意的预览不一致")) && consentStore.ZipDownloads == 0, "an empty approval list approves nothing");
    Restore();
    consentQueue.Handle(new PackRequest { Operation = "confirm-download", PreviewId = waiting.Id, Approved = true, ApprovedPlan = waiting.DownloadPlan }, new UpdateSnapshot());
    var accepted = Files.Read<PackTask>(consentStorage.TaskPath);
    Check(accepted.State == "requested" && accepted.Request.Approved && accepted.Request.ApprovedPlan == waiting.DownloadPlan && accepted.Request.Source == consentSource, "a matching confirmation queues the replay with the original source");
    Check(consentStore.ZipDownloads == 0, "queuing the confirmation does not itself download");
    var approvedReplay = await PackImports.Preview(consentStorage, accepted.Request, new(), consentStore, accepted.PendingDownloads);
    Check(approvedReplay.State == "preview" && approvedReplay.PendingDownloads.Count == 0 && approvedReplay.Proposed.Problems.Count == 0 && consentStore.ZipDownloads == 2, "approved replay downloads each listed ZIP exactly once");
    Check(approvedReplay.Proposed.Files.Count(f => f.Path.EndsWith("mod.dll")) == 2 && Directory.GetFiles(downloads, "*.zip").Length == 2, "approved packages are cached and part of the proposal");
    PackImports.Commit(consentStorage, approvedReplay);
    Check(consentStorage.Read().Packs.Count == 1 && Files.Read<PackTask>(consentStorage.TaskPath).State == "completed", "the approved replay can be committed");
    consentStorage.Validate(consentStorage.Read().Packs.Single()); checks++;
}

// ---- Language: a temporary save captures personal dictionaries into the snapshot workspace and groups them automatically.
{
    var langStorage = new PackStorage(Root("snapshot-language")); string langRoot = langStorage.Root;
    Write(langRoot, "plugins/Host/host.txt", "fixed"); Plugin(langRoot, "plugins/Feature/feature.dll", "lang.feature", "1.0.0"); Write(langRoot, "config/feature.cfg", "value=1");
    string live = "config/ModConfigEnhance/language/";
    Write(langRoot, live + "zh-cn.txt", "// display: 简体\nMCE_SEARCH=搜索"); Write(langRoot, live + "zh-cn_补翻_20261010.txt", "MCE_EXTRA=补"); Write(langRoot, live + "en-us.txt", "MCE_SEARCH=Search");
    Write(langRoot, live + "_private.txt", "secret"); Write(langRoot, live + "notes.md", "not a dictionary");
    var langHosts = new List<PackHostPlugin> { new() { Guid = "com.aiae.modconfigenhance", Version = "2.0.0" } };
    PackTask LangPreview(string name, string game) => PackTransactions.Preview(langStorage, new PackRequest { Operation = "preview-adopt", SnapshotName = name, GameLanguage = game, HostPlugins = langHosts, HostRoots = new() { "plugins/Host" } });
    var langOne = LangPreview("语言快照一", "zh-cn");
    Check(langOne.Report.Languages.Select(l => l.Key).OrderBy(k => k).SequenceEqual(new[] { "en-us", "zh-cn" }) && langOne.Report.Languages.Single(l => l.Key == "zh-cn").Files.Count == 2, "preview groups live dictionaries automatically; supplements join their base group");
    Check(langOne.Proposed.Language == "zh-cn", "the game language chooses the matching group when no manual selection exists");
    Check(!langOne.Report.Languages.Any(l => l.Files.Any(f => f.StartsWith("_") || f.EndsWith(".md"))), "underscore and non-txt files never become language groups");
    Check(LangPreview("语言快照一", "zh-tw").Proposed.Language == "en-us", "an unavailable game language falls back to English");
    langOne.Request.Operation = "confirm-adopt"; PackTransactions.SaveTemporary(langStorage, langOne);
    var langPack = langStorage.Read().Packs.Single(); string workspaceLanguage = Path.Combine(langStorage.Workspace(langPack.Id), "language");
    Check(Directory.GetFiles(workspaceLanguage).Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(new[] { "en-us.txt", "zh-cn.txt", "zh-cn_补翻_20261010.txt" }), "the snapshot workspace holds exactly the personal dictionaries, minus private and non-txt files");
    Check(File.ReadAllText(Path.Combine(workspaceLanguage, "zh-cn.txt")) == File.ReadAllText(Path.Combine(langRoot, "config/ModConfigEnhance/language/zh-cn.txt")), "dictionary bytes are copied unchanged");
    Check(langPack.Language == "zh-cn" && langPack.Report!.Languages.Select(l => l.Key).OrderBy(k => k).SequenceEqual(new[] { "en-us", "zh-cn" }), "the saved pack records the chosen group and its language report");
    Check(PackTransactions.ScanLanguages(langStorage, langPack).Select(l => l.Key).OrderBy(k => k).SequenceEqual(new[] { "en-us", "zh-cn" }) && PackTransactions.LanguageRoots(langStorage, langPack).Any(r => Files.Compare.Equals(Path.GetFullPath(r), Path.GetFullPath(workspaceLanguage))), "stored packs rediscover their languages from the workspace");
    Check(langPack.Files.All(f => !f.Path.Contains("language") && !f.Path.StartsWith("workspace")) && !Directory.Exists(Path.Combine(langStorage.Content(langPack), "config", "ModConfigEnhance")), "dictionaries stay outside the plugin inventory and signed content");
    langStorage.Validate(langPack); checks++;
    // A later snapshot must not inherit dictionaries the player removed, and the manual "Language file" choice beats the game language.
    File.Delete(Path.Combine(langRoot, "config/ModConfigEnhance/language/zh-cn_补翻_20261010.txt"));
    Write(langRoot, "config/com.aiae.modconfigenhance.cfg", "[3. State]\nLanguage file = en-us\n");
    var langTwo = LangPreview("语言快照二", "zh-cn");
    Check(langTwo.Proposed.Language == "en-us", "the player's selected language file wins over the game language");
    langTwo.Request.Operation = "confirm-adopt"; PackTransactions.SaveTemporary(langStorage, langTwo);
    var langSecond = langStorage.Read().Packs.Single(p => p.Id != langPack.Id); string secondWorkspace = Path.Combine(langStorage.Workspace(langSecond.Id), "language");
    Check(Directory.GetFiles(secondWorkspace).Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(new[] { "en-us.txt", "zh-cn.txt" }) && langSecond.Language == "en-us", "a later snapshot captures the current dictionaries only");
    Check(Directory.GetFiles(workspaceLanguage).Length == 3 && langStorage.Read().ActivePackId == langPack.Id, "the earlier snapshot workspace is untouched and the active pack is unchanged");
    Check(File.ReadAllText(Path.Combine(langRoot, "config/ModConfigEnhance/language/zh-cn.txt")).Contains("搜索"), "saving never alters the live dictionaries");
}

// ---- Maintenance: temporary fixtures only; every plan is previewed, then re-validated at execution.
{
    var mStorage = new PackStorage(Root("maintenance"));
    async Task<PackRecord> MStore(string name, Action<string> build)
    {
        string source = Path.Combine(fixtures, "maint-src-" + name); build(source);
        var imported = await PackImports.Preview(mStorage, new PackRequest { Source = source }, new());
        Check(imported.Proposed.Problems.Count == 0 && imported.PendingDownloads.Count == 0, "maintenance fixture imports cleanly: " + name);
        PackImports.Commit(mStorage, imported); return mStorage.Read().Packs.Single(p => p.Id == imported.Proposed.Id);
    }
    void Deps(string source) { Plugin(source, "plugins/Lib/lib.dll", "maint.lib", "1.0.0"); PluginDep(source, "plugins/User/user.dll", "maint.user", "1.0.0", "maint.lib"); Plugin(source, "plugins/Other/other.dll", "maint.other", "1.0.0"); }
    var current = await MStore("current", s => Plugin(s, "plugins/Cur/cur.dll", "maint.cur", "1.0.0"));
    var spare = await MStore("spare", s => Plugin(s, "plugins/Spare/spare.dll", "maint.spare", "1.0.0"));
    var depsOne = await MStore("deps1", Deps); var depsTwo = await MStore("deps2", Deps); var depsThree = await MStore("deps3", Deps);
    var mIndex = mStorage.Read(); mIndex.ActivePackId = current.Id; mStorage.Save(mIndex);
    Write(mStorage.DirectoryFor(spare.Id), "workspace/note.txt", "personal workspace");
    MaintenanceResult Execute(MaintenancePreview preview, Action<MaintenanceRequest>? change = null)
    {
        var request = new MaintenanceRequest { Operation = preview.Operation, PreviewId = preview.Id, PackId = preview.PackId, ModFiles = preview.ModFiles.ToList(), Scopes = preview.Scopes.ToList() };
        change?.Invoke(request); return PackMaintenance.Execute(mStorage, request);
    }
    // Current pack: neither deletion nor mod removal may be executed.
    var activeDelete = PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.DeletePack, PackId = current.Id });
    Check(activeDelete.Blockers.Any(b => b.Code == "active-pack") && !activeDelete.CanExecute, "the current pack cannot be deleted (preview blocks it)");
    Reject(() => Execute(activeDelete), "executing a blocked current-pack preview is refused");
    var activeMod = PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.DeleteMod, PackId = current.Id, ModFiles = new() { "plugins/Cur/cur.dll" } });
    Check(activeMod.Blockers.Any(b => b.Code == "active-pack") && !activeMod.CanExecute, "mods of the current pack cannot be removed");
    Reject(() => Execute(activeMod), "executing a blocked current-pack mod removal is refused");
    Check(mStorage.Read().Packs.Any(p => p.Id == current.Id) && File.Exists(Path.Combine(mStorage.Content(current), "plugins/Cur/cur.dll")), "refused current-pack operations leave the pack untouched");
    // Cache whitelist.
    string downloadsDir = Path.Combine(mStorage.Root, "MCE", "downloads"), viewDir = Path.Combine(mStorage.Home, ".view-cache"), transDir = Path.Combine(mStorage.Home, ".transactions");
    foreach (string name in new[] { "a.zip", "b.part", "c.tmp", "keep.txt", "sub/inner.zip" }) Write(downloadsDir, name, "download " + name);
    foreach (string name in new[] { "a.json", "sub/b.tmp", "c.bin", "keep.dat" }) Write(viewDir, name, "view " + name);
    string finished = Guid.NewGuid().ToString("N"), rolled = Guid.NewGuid().ToString("N"), unfinished = Guid.NewGuid().ToString("N"), bare = Guid.NewGuid().ToString("N");
    Files.Save(Path.Combine(transDir, finished, "switch.json"), new PackSwitchPlan { Id = finished, Phase = "installed" }); Write(transDir, finished + "/backup.bin", "x");
    Files.Save(Path.Combine(transDir, rolled, "switch.json"), new PackSwitchPlan { Id = rolled, Phase = "rolled-back" });
    Files.Save(Path.Combine(transDir, unfinished, "switch.json"), new PackSwitchPlan { Id = unfinished, Phase = "prepared" });
    Write(transDir, bare + "/data.bin", "no plan"); Write(transDir, "not-an-id/switch.json", "{}");
    var cleanAll = new MaintenanceRequest { Operation = MaintenanceOperations.CleanCache, Scopes = new() { "transactions", "downloads", "view-cache" } };
    var cachePreview = PackMaintenance.Preview(mStorage, cleanAll);
    Check(cachePreview.Blockers.Count == 0 && cachePreview.Items.Count(i => i.Scope == "downloads") == 3 && cachePreview.Items.Count(i => i.Scope == "view-cache") == 1 && cachePreview.Items.Count(i => i.Scope == "transactions") == 2, "the preview lists only whitelisted cache items");
    Check(cachePreview.Items.Single(i => i.Scope == "view-cache").FileCount == 2 && cachePreview.Warnings.Count >= 2, "non-cache files are skipped with a visible warning");
    Reject(() => PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.CleanCache, Scopes = new() { "pack-trash" } }), "pack-trash is not a cache scope");
    Reject(() => PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.CleanCache, Scopes = new() { "../../" } }), "free-form scopes are rejected");
    Reject(() => PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.CleanCache }), "an empty scope list is rejected");
    Reject(() => Execute(cachePreview, r => r.Scopes = new() { "downloads" }), "execution cannot change the previewed scopes");
    Check(File.Exists(Path.Combine(downloadsDir, "a.zip")) && Directory.Exists(Path.Combine(transDir, finished)), "a refused execution deletes nothing");
    var cacheResult = Execute(cachePreview);
    Check(cacheResult.Success && cacheResult.RemovedItems == 6 && cacheResult.FreedBytes > 0, "whitelisted caches are cleaned");
    Check(new[] { "a.zip", "b.part", "c.tmp" }.All(n => !File.Exists(Path.Combine(downloadsDir, n))) && File.Exists(Path.Combine(downloadsDir, "keep.txt")) && File.Exists(Path.Combine(downloadsDir, "sub/inner.zip")), "downloads: only .zip/.part/.tmp files in the cache root are removed");
    Check(!File.Exists(Path.Combine(viewDir, "a.json")) && !File.Exists(Path.Combine(viewDir, "sub/b.tmp")) && File.Exists(Path.Combine(viewDir, "c.bin")) && File.Exists(Path.Combine(viewDir, "keep.dat")), "view cache: only .json/.tmp files are removed");
    Check(!Directory.Exists(Path.Combine(transDir, finished)) && !Directory.Exists(Path.Combine(transDir, rolled)) && Directory.Exists(Path.Combine(transDir, unfinished)) && Directory.Exists(Path.Combine(transDir, bare)) && Directory.Exists(Path.Combine(transDir, "not-an-id")), "transactions: only installed/rolled-back plans are removed");
    Check(mStorage.Read().Packs.Count == 5 && mStorage.Read().Packs.All(p => Directory.Exists(mStorage.Content(p))), "cache cleaning never touches pack content or the index");
    Reject(() => Execute(cachePreview), "a preview can be used only once");
    // A change after the preview invalidates it.
    Write(downloadsDir, "x1.zip", "one"); var stale = PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.CleanCache, Scopes = new() { "downloads" } });
    Write(downloadsDir, "x2.zip", "two");
    Reject(() => Execute(stale), "a file added after the preview invalidates it");
    Check(File.Exists(Path.Combine(downloadsDir, "x1.zip")) && File.Exists(Path.Combine(downloadsDir, "x2.zip")), "a stale preview deletes nothing");
    string taskText = File.Exists(mStorage.TaskPath) ? File.ReadAllText(mStorage.TaskPath) : "";
    Files.Save(mStorage.TaskPath, new PackTask { Id = Guid.NewGuid().ToString("N"), State = "waiting-exit" });
    Check(PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.CleanCache, Scopes = new() { "downloads" } }).Blockers.Any(b => b.Code == "pending-task"), "a pending pack task blocks cache cleaning");
    if (taskText.Length > 0) File.WriteAllText(mStorage.TaskPath, taskText); else File.Delete(mStorage.TaskPath);
    var freshClean = Execute(PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.CleanCache, Scopes = new() { "downloads" } }));
    Check(freshClean.Success && !File.Exists(Path.Combine(downloadsDir, "x1.zip")), "a fresh preview after the change can be executed");
    // Deleting a pack moves it to the recoverable trash.
    string spareDirectory = mStorage.DirectoryFor(spare.Id);
    var spareStale = PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.DeletePack, PackId = spare.Id });
    Check(spareStale.CanExecute && spareStale.Items.Single().Kind == "pack" && spareStale.Warnings.Any(w => w.Contains("工作区")), "a non-current pack can be previewed for deletion");
    Write(spareDirectory, "workspace/changed-after-preview.txt", "late");
    Reject(() => Execute(spareStale), "pack content changed after the preview blocks deletion");
    Check(mStorage.Read().Packs.Any(p => p.Id == spare.Id) && Directory.Exists(spareDirectory), "the stale pack deletion changed nothing");
    var spareDelete = PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.DeletePack, PackId = spare.Id });
    Reject(() => Execute(spareDelete, r => r.PackId = depsOne.Id), "execution cannot retarget the previewed pack");
    var deleted = Execute(spareDelete);
    string trashEntry = Path.Combine(mStorage.Home, ".trash", spareDelete.Id);
    Check(deleted.Success && deleted.Message.Contains("回收区") && !mStorage.Read().Packs.Any(p => p.Id == spare.Id) && !Directory.Exists(spareDirectory), "deleting a pack removes it from the library and moves its directory");
    Check(File.Exists(Path.Combine(trashEntry, "trash.json")) && File.Exists(Path.Combine(trashEntry, "pack", "workspace", "note.txt")) && File.Exists(Path.Combine(trashEntry, "pack", "workspace", "changed-after-preview.txt")) && Directory.Exists(Path.Combine(trashEntry, "pack", "revisions")), "the whole pack, workspace included, is kept in the trash with its record");
    Check(mStorage.Read().ActivePackId == current.Id && mStorage.Read().Packs.Count == 4, "deleting a spare pack leaves the current pack active");
    Reject(() => Execute(spareDelete), "a delete-pack preview is single use");
    // Permanent purge needs its own confirmation.
    string orphan = Path.Combine(mStorage.Home, ".trash", Guid.NewGuid().ToString("N")); Write(orphan, "stray.txt", "no record");
    var purge = PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.PurgePackTrash });
    Check(purge.CanExecute && purge.Items.Count == 1 && purge.Warnings.Any(w => w.Contains("缺少有效记录")), "purge lists recorded trash only and keeps unrecognised folders");
    Reject(() => Execute(purge), "permanent deletion without confirmation is refused");
    Check(Directory.Exists(trashEntry), "the unconfirmed purge deleted nothing");
    Reject(() => PackMaintenance.Execute(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.PurgePackTrash, PreviewId = purge.Id, PackId = spare.Id, PermanentConfirmed = true }), "purge cannot change the previewed entry");
    var purged = Execute(purge, r => r.PermanentConfirmed = true);
    Check(purged.Success && !Directory.Exists(trashEntry) && Directory.Exists(orphan), "a confirmed purge removes the recorded entry only");
    Check(PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.PurgePackTrash, PackId = spare.Id }).Blockers.Any(b => b.Code == "missing"), "purging an entry that no longer exists is blocked");
    Check(Directory.Exists(mStorage.Content(current)) && mStorage.Read().Packs.Count == 4, "purge leaves library packs alone");
    // Mod removal and dependencies.
    MaintenancePreview ModPreview(PackRecord pack, params string[] files) => PackMaintenance.Preview(mStorage, new MaintenanceRequest { Operation = MaintenanceOperations.DeleteMod, PackId = pack.Id, ModFiles = files.ToList() });
    PackRecord Reload(PackRecord pack) => mStorage.Read().Packs.Single(p => p.Id == pack.Id);
    var blockedLib = ModPreview(depsOne, "plugins/Lib/lib.dll");
    Check(!blockedLib.CanExecute && blockedLib.Blockers.Any(b => b.Code == "dependency" && b.Message.Contains("maint.lib")), "removing a mod that a remaining mod hard-depends on is blocked");
    Reject(() => Execute(blockedLib), "a dependency-blocked removal cannot be executed");
    Check(Reload(depsOne).Files.Count == 3 && Reload(depsOne).Content == depsOne.Content, "the blocked removal changed nothing");
    Check(ModPreview(depsOne, "plugins/Nope/x.dll").Blockers.Any(b => b.Code == "not-in-pack") && ModPreview(depsOne, "plugins/Lib/lib.dll", "config/x.cfg").Blockers.Any(b => b.Code == "scope"), "files outside the pack list or outside plugins/ are blocked");
    Reject(() => ModPreview(depsOne, "plugins/../escape.dll"), "path traversal is rejected before planning");
    Check(ModPreview(depsOne, depsOne.Files.Select(f => f.Path).ToArray()).Blockers.Any(b => b.Code == "all-files"), "emptying the pack must be a pack deletion instead");
    var dependent = ModPreview(depsOne, "plugins/User/user.dll");
    Check(dependent.CanExecute && dependent.Warnings.Any(w => w.Contains("非代码资源")), "removing the dependent (not the dependency) is allowed");
    var removedDependent = Execute(dependent); var depsAfter = Reload(depsOne);
    Check(removedDependent.Success && removedDependent.NewRevision.StartsWith("revisions/") && depsAfter.Content == removedDependent.NewRevision && depsAfter.Files.Count == 2 && depsAfter.Files.All(f => !f.Path.Contains("User")), "removal writes a new revision without the removed file");
    Check(File.Exists(Path.Combine(mStorage.Content(depsOne), "plugins/User/user.dll")), "the previous revision stays on disk");
    mStorage.Validate(depsAfter); checks++;
    Check(ModPreview(depsAfter, "plugins/Lib/lib.dll").CanExecute, "once the dependent is gone the dependency can be removed");
    var both = ModPreview(depsTwo, "plugins/Lib/lib.dll", "plugins/User/user.dll");
    Check(both.CanExecute && both.Blockers.Count == 0, "removing a mod together with everything that depends on it is allowed");
    Check(Execute(both).Success && Reload(depsTwo).Files.Select(f => f.Path).SequenceEqual(new[] { "plugins/Other/other.dll" }), "dependency and dependent are removed together");
    var changing = ModPreview(depsThree, "plugins/Other/other.dll"); mStorage.Save(mStorage.Read());
    Reject(() => Execute(changing), "a library change after the preview invalidates a mod removal");
    Check(Reload(depsThree).Files.Count == 3, "the invalidated removal changed nothing");
    Check(Execute(ModPreview(depsThree, "plugins/Other/other.dll")).Success && Reload(depsThree).Files.Count == 2, "a fresh preview after the change succeeds");
}
// UI-to-worker maintenance protocol preserves the preview/execute boundary.
{
    var managed = new PackStorage(Root("maintenance-queue"));
    Directory.CreateDirectory(Path.Combine(managed.Root, "MCE", "downloads"));
    File.WriteAllText(Path.Combine(managed.Root, "MCE", "downloads", "test.zip"), "cache");
    var queue = new PackQueue(managed.Root);
    queue.Handle(new PackRequest { Operation = "maintenance-preview", Maintenance = new MaintenanceRequest { Operation = MaintenanceOperations.CleanCache, Scopes = new() { MaintenanceScopes.Downloads } } }, new());
    var task = Files.Read<PackTask>(managed.TaskPath);
    Check(task.State == "maintenance-preview" && task.Maintenance?.CanExecute == true, "maintenance UI receives an executable preview without deleting files");
    Check(File.Exists(Path.Combine(managed.Root, "MCE", "downloads", "test.zip")), "preview leaves downloads in place");
    queue.Handle(new PackRequest { Operation = "maintenance-execute", Maintenance = new MaintenanceRequest { Operation = MaintenanceOperations.CleanCache, PreviewId = task.Maintenance!.Id, Scopes = new() { MaintenanceScopes.Downloads } } }, new());
    Check(Files.Read<PackTask>(managed.TaskPath).State == "completed" && !File.Exists(Path.Combine(managed.Root, "MCE", "downloads", "test.zip")), "confirmed maintenance executes and reports completion");
}
// Storage-browser item buttons must only remove the exact selected project, not the whole category.
{
    var one = new PackStorage(Root("selected-cleanup"));
    string downloads = Path.Combine(one.Root, "MCE", "downloads"); Directory.CreateDirectory(downloads);
    File.WriteAllText(Path.Combine(downloads, "one.zip"), "one"); File.WriteAllText(Path.Combine(downloads, "two.zip"), "two");
    var request = new MaintenanceRequest { Operation = MaintenanceOperations.CleanCache, Scopes = new() { MaintenanceScopes.Downloads }, SelectedPaths = new() { "MCE/downloads/one.zip" } };
    var selectedPreview = PackMaintenance.Preview(one, request);
    Check(selectedPreview.Items.Count == 1 && selectedPreview.TotalFiles == 1 && selectedPreview.Items[0].Path.EndsWith("one.zip"), "storage item preview includes only the selected download");
    request.PreviewId = selectedPreview.Id;
    Reject(() => PackMaintenance.Execute(one, new MaintenanceRequest { Operation = request.Operation, PreviewId = selectedPreview.Id, Scopes = request.Scopes, SelectedPaths = new() { "MCE/downloads/two.zip" } }), "confirmation cannot swap selected cleanup project");
    PackMaintenance.Execute(one, request);
    Check(!File.Exists(Path.Combine(downloads, "one.zip")) && File.Exists(Path.Combine(downloads, "two.zip")), "deleting one project preserves its sibling");
    var escaped = PackMaintenance.Preview(one, new MaintenanceRequest { Operation = request.Operation, Scopes = request.Scopes, SelectedPaths = new() { "../core/BepInEx.dll" } });
    Check(!escaped.CanExecute && escaped.Blockers.Any(b => b.Code == "not-in-preview"), "storage selection cannot introduce an arbitrary path");
}
// Whole-category cleanup is an explicit plan, not an arbitrary root selection.
{
    var category = new PackStorage(Root("whole-category-cleanup"));
    string downloads = Path.Combine(category.Root, "MCE", "downloads"); Directory.CreateDirectory(downloads);
    File.WriteAllText(Path.Combine(downloads, "one.zip"), "one"); File.WriteAllText(Path.Combine(downloads, "two.zip"), "two"); File.WriteAllText(Path.Combine(downloads, "keep.txt"), "keep");
    var request = new MaintenanceRequest { Operation = MaintenanceOperations.CleanCache, Scopes = new() { MaintenanceScopes.Downloads }, WholeCategory = true };
    var categoryPreview = PackMaintenance.Preview(category, request);
    Check(categoryPreview.Items.Count == 2 && categoryPreview.Warnings.Count > 0 && categoryPreview.WholeCategory, "category preview lists safe candidates and retained items");
    request.PreviewId = categoryPreview.Id;
    Reject(() => PackMaintenance.Execute(category, new MaintenanceRequest { Operation = request.Operation, PreviewId = categoryPreview.Id, Scopes = request.Scopes }), "category confirmation cannot change to item mode");
    PackMaintenance.Execute(category, request);
    Check(Directory.GetFiles(downloads).Select(Path.GetFileName).SequenceEqual(new[] { "keep.txt" }), "category cleanup preserves non-cache content");
    Reject(() => PackMaintenance.Preview(category, new MaintenanceRequest { Operation = request.Operation, WholeCategory = true, Scopes = request.Scopes, SelectedPaths = new() { "MCE/downloads" } }), "category mode rejects root-path injection");
}
// Direct switch keeps the explicit saved baseline but automatically restores the unsaved runtime on return.
{
    if (switching.Read().ActivePackId != aId) Switch(aId);
    string savedContent = switching.Read().Packs.Single(p => p.Id == aId).Content;
    Write(switching.Root, "plugins/NewSinceSave/extra.txt", "added"); Write(switching.Root, "config/a.cfg", "unsaved-A");
    var direct = SwitchPreview(bId); direct.Request.Operation = "confirm-switch"; direct.Request.SaveCurrentBeforeSwitch = false;
    var directPlan = PackTransactions.Prepare(switching, direct); PackTransactions.Commit(switching, directPlan);
    var record = switching.Read().Packs.Single(p => p.Id == aId);
    Check(record.Content == savedContent && record.UnsavedEnvironment != null, "direct switch preserves explicit saved baseline");
    Check(!File.Exists(Path.Combine(switching.Root, "plugins/NewSinceSave/extra.txt")), "unsaved added files do not leak into target pack");
    Check(PackMaintenance.ReferencedRevisions(switching, switching.Read()).Contains(switching.Content(record.UnsavedEnvironment!)), "unsaved runtime protected from cleanup");
    Switch(aId);
    Check(File.ReadAllText(Path.Combine(switching.Root, "config/a.cfg")) == "unsaved-A" && File.Exists(Path.Combine(switching.Root, "plugins/NewSinceSave/extra.txt")), "return automatically restores unsaved configuration and added files");
    Check(switching.Read().Packs.Single(p => p.Id == aId).UnsavedEnvironment != null, "return keeps unsaved marker");
    Switch(bId);
    var saved = switching.Read().Packs.Single(p => p.Id == aId);
    Check(saved.UnsavedEnvironment == null && saved.Files.Any(f => f.Path == "plugins/NewSinceSave/extra.txt"), "save-and-switch promotes runtime to saved baseline");
    var rollbackDirect = SwitchPreview(aId); rollbackDirect.Request.Operation = "confirm-switch"; rollbackDirect.Request.SaveCurrentBeforeSwitch = false;
    var rollbackPlan = PackTransactions.Prepare(switching, rollbackDirect);
    PackTransactions.StepProbe = step => { if (step.StartsWith("placed:")) throw new IOException("direct rollback probe"); };
    Reject(() => PackTransactions.Commit(switching, rollbackPlan), "direct switch injected failure"); PackTransactions.StepProbe = null;
    PackTransactions.Rollback(switching, rollbackPlan);
    Check(switching.Read().ActivePackId == bId && switching.Read().Packs.Single(p => p.Id == bId).UnsavedEnvironment == null, "failed direct switch restores prior index and environment without unsaved marker leak");
}
// Actionable file errors and safe explicit retries after a completed rollback.
{
    string folder = Path.Combine(fixtures, "file-diagnostics"); Directory.CreateDirectory(folder);
    string source = Path.Combine(folder, "readonly.txt"), target = Path.Combine(folder, "copy.txt");
    File.WriteAllText(source, "readonly"); File.SetAttributes(source, File.GetAttributes(source) | FileAttributes.ReadOnly);
    Files.CopyAtomic(source, target); Check(File.ReadAllText(target) == "readonly", "readonly source can be safely flushed in a writable temporary copy");
    try { Files.CopyAtomic(source, target); Check(false, "existing target must reject"); }
    catch (IOException e) { Check(e.Message.Contains(source) && e.Message.Contains(target) && e.InnerException != null, "copy errors identify source target and system failure"); }
    Check(Directory.GetFiles(folder, "*.mce-part").Length == 0, "failed atomic copy removes only its own temporary file");
    var failed = SwitchPreview(aId); failed.Request.Operation = "confirm-switch"; failed.Request.SaveCurrentBeforeSwitch = false;
    failed.State = "failed"; Files.Save(switching.TaskPath, failed);
    new PackQueue(switching.Root).Handle(new PackRequest { Operation = "retry-switch", PreviewId = failed.Id }, new());
    var retried = Files.Read<PackTask>(switching.TaskPath);
    Check(retried.Id != failed.Id && retried.State == "waiting-exit" && !retried.Request.SaveCurrentBeforeSwitch, "retry revalidates new transaction and preserves save choice");
    Check(retried.AttemptId.Length == 32 && retried.AttemptStartedUtc.Length > 0 && retried.AttemptEndedUtc.Length == 0, "retry creates persisted fresh attempt timing");
    Reject(() => new PackQueue(switching.Root).Handle(new PackRequest { Operation = "retry-switch", PreviewId = failed.Id }, new()), "stale retry cannot replace active task");
}
// Shared progress readers must not break atomic journal replacement.
{
    string journal = Path.Combine(fixtures, "shared-progress.json"); Files.Save(journal, new { Count = 1 });
    using (var reader = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
    {
        Files.Save(journal, new { Count = 2 });
        Check(File.ReadAllText(journal).Contains("2"), "shared-delete progress reader permits atomic journal replacement");
    }
    var held = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read);
    var releaser = Task.Run(() => { Thread.Sleep(160); held.Dispose(); });
    Files.Save(journal, new { Count = 3 }); releaser.GetAwaiter().GetResult();
    Check(File.ReadAllText(journal).Contains("3"), "transient reader sharing conflict retried without losing journal");
}
Console.WriteLine("PASS " + checks + " pack library checks; fixtures: " + fixtures);

sealed class FakeStore(Dictionary<string, string[]> graph, Dictionary<string, byte[]> archives) : HttpMessageHandler
{
    public int ZipDownloads; // package ZIP requests actually made; metadata requests are not counted
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var uri = request.RequestUri!; var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        if (uri.AbsolutePath.EndsWith(".zip")) Interlocked.Increment(ref ZipDownloads);
        if (uri.AbsolutePath.EndsWith(".zip")) response.Content = new ByteArrayContent(archives[Path.GetFileNameWithoutExtension(uri.AbsolutePath)[5..]]);
        else
        {
            var parts = uri.AbsolutePath.Trim('/').Split('/'); string name = parts[4], version = parts[5], key = name + "-" + version;
            response.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { version_number = version, download_url = "https://fixture.test/Team-" + key + ".zip", dependencies = graph.GetValueOrDefault(key, Array.Empty<string>()) }));
        }
        return Task.FromResult(response);
    }
}
