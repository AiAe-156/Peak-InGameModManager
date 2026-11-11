using MCE.UpdateWorker;
using ModConfigEnhance.Updating;
using Mono.Cecil;
using System.Text.Json;

string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtures); int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
void Reject(Action action, string message) { try { action(); } catch (IOException) { checks++; Console.WriteLine("PASS " + message); return; } throw new Exception(message); }
void Write(string root, string path, string text) { string file = Files.Child(root, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, text); }
PackStorage Store(string name) { string root = Path.Combine(fixtures, name, "BepInEx"); Directory.CreateDirectory(root); return new(root); }
void Assembly(string root, string path, string name, string version, string? reference = null)
{
    string file = Files.Child(root, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, Version.Parse(version)), name, ModuleKind.Dll);
    if (reference != null) a.MainModule.AssemblyReferences.Add(new AssemblyNameReference(reference, new Version(1, 0, 0, 0)));
    if (name == "ModConfigEnhance") {
        var ctor = new MethodReference(".ctor", a.MainModule.TypeSystem.Void, new TypeReference("BepInEx", "BepInPlugin", a.MainModule, new AssemblyNameReference("BepInEx",new Version(1,0,0)))) { HasThis=true };
        for(int i=0;i<3;i++) ctor.Parameters.Add(new ParameterDefinition(a.MainModule.TypeSystem.String));
        var attribute=new CustomAttribute(ctor);foreach(string v in new[]{"com.aiae.modconfigenhance","MCE","2.0.0"})attribute.ConstructorArguments.Add(new(a.MainModule.TypeSystem.String,v));
        var type=new TypeDefinition("Fixture","Host",TypeAttributes.Public|TypeAttributes.Class);type.CustomAttributes.Add(attribute);a.MainModule.Types.Add(type);
        a.MainModule.AssemblyReferences.Clear();
    }
    a.Write(file);
}
void Management(string root) {
 foreach(string name in new[]{"BepInEx","BepInEx.Preloader","0Harmony"}) Assembly(root,"core/"+name+".dll",name,"1.0.0");
 Assembly(root,"plugins/MCE/ModConfigEnhance.dll","ModConfigEnhance","2.0.0");
}
ShopLedger Ledger(PackRecord pack, string key, string path) => new() { Revision = 5, Packages = new() { new() { Key = key, Version = "1.0.0", Files = new() { PackCompleteEnvironment.Clone(pack.Files.Single(f => f.Path == path)) } } } };
PackRecord Record(PackStorage s, string name, bool complete)
{
    var p = new PackRecord { Id = Guid.NewGuid().ToString("N"), Content = "revisions/" + Guid.NewGuid().ToString("N"), Name = name, EnvironmentVersion = complete ? 2 : 1, MinimumWorkerProtocol = complete ? 2 : 1 };
    Write(s.Content(p), "plugins/Feature/notes.md", "# " + name);
    if (complete) { Management(s.Content(p)); Assembly(s.Content(p), "core/loader.dll", "Loader", name == "A" ? "1.0.0" : "2.0.0"); Write(s.Content(p), "plugins/Host/host.txt", name); Write(s.Content(p), "core/only-" + name + ".txt", name); }
    p.Files = PackStorage.Inventory(s.Content(p));
    if (complete) new PackRevisionState(s).Write(p, Ledger(p, name, "core/loader.dll"));
    return p;
}
void Install(PackStorage s, PackRecord p) { PackStorage.CopyTree(s.Content(p), s.Root); Files.Save(PackCompleteEnvironment.LedgerPath(s, p.Id), new PackRevisionState(s).Read(p)!); }
Files.RunningProbe = _ => false;
PackCompleteEnvironment.ManagementProtocolCheck = (_, _) => { }; // isolated explicit E-hook simulation
var s = Store("switch"); var a = Record(s, "A", true); var b = Record(s, "B", true);
s.Save(new PackIndex { ActivePackId = a.Id, Packs = new() { a, b }, ActiveFiles = a.Files, HostRoots = new() { "plugins/Host" } }); Install(s, a);
Check(PackTransactions.Live(s, s.Read(), 2).Any(f => f.Path == "plugins/Host/host.txt") && PackTransactions.Live(s, s.Read()).All(f => !f.Path.StartsWith("core/")), "full Live includes host/core; legacy overload unchanged");
PackSwitchPlan Prepare(string id, bool save = true)
{
    var t = PackTransactions.Preview(s, new PackRequest { Operation = "preview-switch", PackId = id, SaveCurrentBeforeSwitch = save }); t.Request.Operation = "confirm-switch"; return PackTransactions.Prepare(s, t);
}
void Switch(string id, bool save = true) { var plan = Prepare(id, save); PackTransactions.Commit(s, plan); }
Switch(b.Id);
Check(!File.Exists(Files.Child(s.Root, "core/only-A.txt")) && File.Exists(Files.Child(s.Root, "core/only-B.txt")), "A old-only core removed on B switch");
Check(File.ReadAllText(Files.Child(s.Root, "plugins/Host/host.txt")) == "B", "host content is switched, not fixed");
Check(Files.Read<ShopLedger>(PackCompleteEnvironment.LedgerPath(s, b.Id)).Packages.Single().Key == "B", "target paired ledger synchronized");
Switch(a.Id);
Check(File.Exists(Files.Child(s.Root, "core/only-A.txt")) && !File.Exists(Files.Child(s.Root, "core/only-B.txt")), "B to A old core restored");
string baseline = s.Read().Packs.Single(p => p.Id == a.Id).Content;
Assembly(s.Root, "core/loader.dll", "Loader", "3.0.0"); Write(s.Root, "core/manual.txt", "unowned manual addition");
var runtimeLedger = Files.Read<ShopLedger>(PackCompleteEnvironment.LedgerPath(s, a.Id)); runtimeLedger.Revision++;
runtimeLedger.Packages.Single().Files.Single().Hash = Files.Hash(Files.Child(s.Root, "core/loader.dll")); Files.Save(PackCompleteEnvironment.LedgerPath(s, a.Id), runtimeLedger);
Switch(b.Id, false); var unsaved = s.Read().Packs.Single(p => p.Id == a.Id);
Check(unsaved.Content == baseline && unsaved.UnsavedEnvironment != null, "direct switch keeps saved baseline and paired unsaved revision");
Check(new PackRevisionState(s).Read(unsaved.UnsavedEnvironment!)!.Revision == runtimeLedger.Revision, "unsaved revision captures current ledger");
// Tamper mutable workspace: returning must use unsaved revision state, not this latest workspace.
Files.Save(PackCompleteEnvironment.LedgerPath(s, a.Id), new ShopLedger { Revision = 900 });
Switch(a.Id);
Check(File.Exists(Files.Child(s.Root, "core/manual.txt")) && Files.Read<ShopLedger>(PackCompleteEnvironment.LedgerPath(s, a.Id)).Revision == runtimeLedger.Revision, "return restores unsaved core plus matching ledger, manual addition unowned allowed");
Switch(b.Id); var promoted = s.Read().Packs.Single(p => p.Id == a.Id);
Check(promoted.UnsavedEnvironment == null && promoted.Content != baseline && promoted.Files.Any(f => f.Path == "core/manual.txt"), "save-and-switch promotes runtime pair");
Switch(a.Id);
foreach (string step in new[] { "prepared", "removed:", "placed:", "before-ledger:", "ledger:", "before-index", "after-index" })
{
    Files.Save(PackCompleteEnvironment.LedgerPath(s, b.Id), new ShopLedger { Revision = 800 }); // guarantees a target-ledger write in this fault fixture
    string oldCore = Files.Hash(Files.Child(s.Root, "core/loader.dll")); string oldLedger = File.ReadAllText(PackCompleteEnvironment.LedgerPath(s, a.Id));
    PackSwitchPlan? plan = null;
    if (step != "prepared") plan = Prepare(b.Id, false);
    PackTransactions.StepProbe = probe => { if (probe.StartsWith(step, StringComparison.Ordinal)) throw new IOException("injected " + step); };
    if (step == "prepared")
    {
        var task = PackTransactions.Preview(s, new PackRequest { Operation = "preview-switch", PackId = b.Id }); task.Request.Operation = "confirm-switch";
        Reject(() => PackTransactions.Prepare(s, task), "prepare journal crash"); plan = Files.Read<PackSwitchPlan>(PackTransactions.PlanPath(s, task.Id));
    }
    else Reject(() => PackTransactions.Commit(s, plan!), "fault at " + step);
    PackTransactions.StepProbe = null;
    // Reload persisted plan exactly as worker startup does.
    plan = Files.Read<PackSwitchPlan>(PackTransactions.PlanPath(s, plan!.Id));
    PackTransactions.Rollback(s, plan); PackTransactions.Rollback(s, plan);
    Check(s.Read().ActivePackId == a.Id && Files.Hash(Files.Child(s.Root, "core/loader.dll")) == oldCore && File.ReadAllText(PackCompleteEnvironment.LedgerPath(s, a.Id)) == oldLedger, "crash rollback restores files/index/ledger: " + step);
}
// A crash before journal publication leaves live/index untouched and the new pair unreferenced.
{
    string before = File.ReadAllText(s.IndexPath), live = Files.Hash(Files.Child(s.Root, "core/loader.dll"));
    var task = PackTransactions.Preview(s, new PackRequest { Operation = "preview-switch", PackId = b.Id }); task.Request.Operation = "confirm-switch";
    PackTransactions.StepProbe = step => { if (step == "snapshot-written") throw new IOException("pre-journal crash"); };
    Reject(() => PackTransactions.Prepare(s, task), "snapshot-before-journal interruption"); PackTransactions.StepProbe = null;
    Check(File.ReadAllText(s.IndexPath) == before && Files.Hash(Files.Child(s.Root, "core/loader.dll")) == live && !File.Exists(PackTransactions.PlanPath(s, task.Id)), "prepare interruption never mutates live or index");
}
// Rollback can recover originals even when incoming revision-state is lost after a commit crash.
{
    var plan = Prepare(b.Id); PackTransactions.StepProbe = step => { if (step.StartsWith("placed:")) throw new IOException("crash"); };
    Reject(() => PackTransactions.Commit(s, plan), "target-state-loss rollback fixture"); PackTransactions.StepProbe = null;
    var target = plan.After.Packs.Single(p => p.Id == b.Id); string path = new PackRevisionState(s).PathFor(target), text = File.ReadAllText(path); File.Delete(path);
    PackTransactions.Rollback(s, Files.Read<PackSwitchPlan>(PackTransactions.PlanPath(s, plan.Id)));
    Check(s.Read().ActivePackId == a.Id, "target state loss does not prevent original environment rollback"); File.WriteAllText(path, text);
}
// Actual durable index replace failure, not just the step hook.
{
    var plan = Prepare(b.Id); using (var held = new FileStream(s.IndexPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        Reject(() => PackTransactions.Commit(s, plan), "actual index write failure");
    PackTransactions.Rollback(s, Files.Read<PackSwitchPlan>(PackTransactions.PlanPath(s, plan.Id)));
    Check(s.Read().ActivePackId == a.Id, "index failure rollback restores active identity");
}
{
    var plan = Prepare(b.Id); PackTransactions.StepProbe = probe => { if (probe.StartsWith("placed:")) throw new IOException("commit crash"); };
    Reject(() => PackTransactions.Commit(s, plan), "prepare interrupted rollback fixture"); PackTransactions.StepProbe = null;
    PackTransactions.StepProbe = probe => { if (probe == "rollback-ledgers") throw new IOException("rollback crash"); };
    Reject(() => PackTransactions.Rollback(s, plan), "rollback interruption"); PackTransactions.StepProbe = null;
    PackTransactions.Rollback(s, Files.Read<PackSwitchPlan>(PackTransactions.PlanPath(s, plan.Id)));
    Check(s.Read().ActivePackId == a.Id, "rollback resumes after crash");
}
var legacy = Record(s, "legacy", false); var ix = s.Read(); ix.Packs.Add(legacy); s.Save(ix);
Reject(() => Prepare(legacy.Id), "full to legacy blocked rather than inheriting full core");
PackCompleteEnvironment.ManagementProtocolCheck = null; Reject(() => Prepare(b.Id), "missing management protocol hook fails closed"); PackCompleteEnvironment.ManagementProtocolCheck = (_, _) => { };
{
    b = s.Read().Packs.Single(p => p.Id == b.Id);
    string state = new PackRevisionState(s).PathFor(b), saved = File.ReadAllText(state); File.Delete(state);
    Reject(() => Prepare(b.Id), "missing target paired state rejected at activation"); File.WriteAllText(state, saved);
    string payload = Files.Child(s.Content(b), "core/loader.dll"), backup = payload + ".save"; File.Move(payload, backup);
    Reject(() => Prepare(b.Id), "missing target payload rejected at activation"); File.Move(backup, payload);
}
// Ownership mismatch is explicit and never deletes ledger. Unknown additions alone already passed above.
{
    string text = File.ReadAllText(PackCompleteEnvironment.LedgerPath(s, a.Id));
    Assembly(s.Root, "core/loader.dll", "Loader", "8.0.0"); Reject(() => Prepare(b.Id), "owned external change requires explicit ownership repair");
    Check(File.ReadAllText(PackCompleteEnvironment.LedgerPath(s, a.Id)) == text, "failed capture does not wipe ledger");
    var active = s.Read().Packs.Single(p => p.Id == a.Id); Files.CopyAtomic(Files.Child(s.Content(active), "core/loader.dll"), Files.Child(s.Root, "core/restored.tmp")); File.Move(Files.Child(s.Root, "core/restored.tmp"), Files.Child(s.Root, "core/loader.dll"), true);
}
// Actual ledger rename failure and target-only dependency validation.
{
    Files.Save(PackCompleteEnvironment.LedgerPath(s, b.Id), new ShopLedger { Revision = 801 });
    var plan = Prepare(b.Id);
    using (var held = new FileStream(PackCompleteEnvironment.LedgerPath(s, b.Id), FileMode.Open, FileAccess.Read, FileShare.Read))
        Reject(() => PackTransactions.Commit(s, plan), "actual ledger write failure");
    PackTransactions.Rollback(s, Files.Read<PackSwitchPlan>(PackTransactions.PlanPath(s, plan.Id)));
    Check(s.Read().ActivePackId == a.Id && Files.Read<ShopLedger>(PackCompleteEnvironment.LedgerPath(s, b.Id)).Revision == 801, "ledger failure restores files/index/previous target ledger");
    var dependency = Record(s, "dependency", true);
    Assembly(s.Content(dependency), "plugins/Feature/consumer.dll", "Consumer", "1.0.0", "OnlyLiveCore");
    dependency.Files = PackStorage.Inventory(s.Content(dependency)); dependency.Content = "revisions/" + Guid.NewGuid().ToString("N");
    PackStorage.CopyTree(s.Content(s.Read().Packs.Single(p => p.Id == b.Id)), s.Content(dependency));
    Assembly(s.Content(dependency), "plugins/Feature/consumer.dll", "Consumer", "1.0.0", "OnlyLiveCore");
    dependency.Files = PackStorage.Inventory(s.Content(dependency)); dependency.RevisionStateHash = "";
    new PackRevisionState(s).Write(dependency, PackRevisionState.EmptyLedger());
    Assembly(s.Root, "core/OnlyLiveCore.dll", "OnlyLiveCore", "1.0.0");
    Check(PackCompleteEnvironment.CheckDependencies(s, dependency).Any(i => i.Contains("OnlyLiveCore")), "target dependency cannot borrow live core");
    File.Delete(Files.Child(s.Root, "core/OnlyLiveCore.dll"));
}
// Revision-specific personal copy and delete-mod must ignore mutable workspace ledger.
{
    Files.Save(PackCompleteEnvironment.LedgerPath(s, b.Id), new ShopLedger { Revision = 999 });
    string drafts = Path.Combine(s.Home, ".editor-drafts", Guid.NewGuid().ToString("N")); Write(drafts, "plugins/Feature/notes.md", "# personal");
    var copy = PackPersonalCopy.Save(s, new PackRequest { PackId = b.Id, Source = drafts });
    Check(new PackRevisionState(s).Read(copy)!.Packages.Single().Key == "B" && Files.Read<ShopLedger>(PackCompleteEnvironment.LedgerPath(s, copy.Id)).Packages.Single().Key == "B", "personal copy reads original paired ledger not workspace latest");
    var preview = PackMaintenance.Preview(s, new MaintenanceRequest { Operation = MaintenanceOperations.DeleteMod, PackId = copy.Id, ModFiles = new() { "plugins/Feature/notes.md" } });
    Check(preview.CanExecute, "complete delete-mod preview succeeds");
    PackMaintenance.Execute(s, new MaintenanceRequest { Operation = preview.Operation, PackId = copy.Id, ModFiles = preview.ModFiles, PreviewId = preview.Id });
    var edited = s.Read().Packs.Single(p => p.Id == copy.Id);
    Check(new PackRevisionState(s).Read(edited)!.Packages.Single().Key == "B" && File.Exists(new PackRevisionState(s).PathFor(copy)), "delete-mod creates paired state and retains old revision-state");
    Check(PackMaintenance.Protects(PackMaintenance.ReferencedRevisions(s, s.Read()), Path.GetDirectoryName(new PackRevisionState(s).PathFor(edited))!), "maintenance protects referenced revision-state");
}
// Explicit legacy upgrade captures live only for current, preserves original revision and no init migration.
{
    var m = Store("migration"); var old = Record(m, "old", false); string original = old.Content;
    m.Save(new PackIndex { ActivePackId = old.Id, Packs = new() { old }, ActiveFiles = old.Files }); PackStorage.CopyTree(m.Content(old), m.Root);
    Management(m.Root); Write(m.Root, "core/current.txt", "current full core"); string before = File.ReadAllText(m.IndexPath); _ = new PackQueue(m.Root);
    Check(File.ReadAllText(m.IndexPath) == before && m.Read().Packs.Single().EnvironmentVersion == 1, "initialization never migrates real index");
    Reject(() => PackTransactions.Preview(m, new PackRequest { Operation = "preview-upgrade-environment", PackId = b.Id }), "noncurrent old package cannot borrow live environment");
    var q = new PackQueue(m.Root); q.Handle(new PackRequest { Operation = "preview-upgrade-environment", PackId = old.Id }, new()); await q.Pump(new());
    var task = Files.Read<PackTask>(m.TaskPath); q.Handle(new PackRequest { Operation = "confirm-upgrade-environment", PreviewId = task.Id }, new());
    Files.RunningProbe = _ => true; await q.Pump(new()); Check(m.Read().Packs.Single().Content == original, "upgrade waits for explicit game exit");
    Files.RunningProbe = _ => false; await q.Pump(new()); var upgraded = m.Read().Packs.Single();
    Check(upgraded.EnvironmentVersion == 2 && upgraded.Content != original && Directory.Exists(m.Content(old)) && upgraded.Files.Any(f => f.Path == "core/current.txt"), "explicit upgrade captures full live and preserves old revision");
}
// Explicit complete adoption uses the same exit-time paired transaction.
{
    var m = Store("full-adopt"); Management(m.Root); Write(m.Root, "core/new.txt", "loader"); Write(m.Root, "plugins/Host/host.txt", "host");
    var q = new PackQueue(m.Root); q.Handle(new PackRequest { Operation = "preview-adopt-environment", SnapshotName = "Full" }, new()); await q.Pump(new());
    var task = Files.Read<PackTask>(m.TaskPath); q.Handle(new PackRequest { Operation = "confirm-adopt-environment", PreviewId = task.Id }, new()); await q.Pump(new());
    var saved = m.Read().Packs.Single();
    Check(saved.EnvironmentVersion == 2 && saved.Files.Count == 6 && new PackRevisionState(m).Read(saved)!.Packages.Count == 0, "full adoption captures core/host with explicit empty ledger");
}
// Legacy transaction resumes under mixed schema2 inventory, no full-core writes.
{
    var m = Store("legacy-recovery"); var old = Record(m, "old", false); var next = Record(m, "next", false);
    m.Save(new PackIndex { ActivePackId = old.Id, Packs = new() { old, next }, ActiveFiles = old.Files, HostRoots = new() { "plugins/Host" }, HostPlugins = new() { new() { Guid = "com.aiae.modconfigenhance", Version = "2.0.0" } } }); PackStorage.CopyTree(m.Content(old), m.Root);
    var t = PackTransactions.Preview(m, new PackRequest { Operation = "preview-switch", PackId = next.Id, HostPlugins = m.Read().HostPlugins }); t.Request.Operation = "confirm-switch"; var plan = PackTransactions.Prepare(m, t);
    PackTransactions.Rollback(m, Files.Read<PackSwitchPlan>(PackTransactions.PlanPath(m, plan.Id)));
    Check(m.Read().ActivePackId == old.Id && plan.SchemaVersion == 1, "schema1 unfinished plan legacy recovery preserved");
}
Console.WriteLine("PASS " + checks + " transaction checks. Fixtures: " + fixtures);
