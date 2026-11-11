using System.Reflection;
using System.Text.Json;
using MCE.UpdateWorker;
using ModConfigEnhance.Updating;

string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
Directory.CreateDirectory(fixtures);
int checks = 0;
void Check(bool value, string description) { if (!value) throw new Exception("FAIL: " + description); checks++; }
void Reject(Action action, string description)
{
    bool rejected = false; try { action(); } catch (IOException) { rejected = true; }
    Check(rejected, description);
}
UpdateJob Clone(UpdateJob job) => JsonSerializer.Deserialize<UpdateJob>(JsonSerializer.Serialize(job))!;
UpdateJob Job(string id, string root, string state = UpdateStates.PendingRestart) => new()
{
    Id = id, Namespace = "AiAeT", Package = "ModConfigEnhance", InstallMode = "local-deployment", State = state,
    PackageRoot = root, InstalledVersion = "2.0.0", TargetVersion = "2.0.0", PluginGuids = new() { "test.mce" }
};
var previous = Job(Guid.NewGuid().ToString("N"), "F:\\fixture\\plugins\\MCE");
var next = Job(Guid.NewGuid().ToString("N"), previous.PackageRoot, UpdateStates.Queued);
bool Policy(UpdateJob? n = null, string? np = "prepared", UpdateJob? p = null, string? pp = "installed") => LocalDeploymentPolicy.CanSupersede(n ?? next, np, p ?? previous, pp);
Check(Policy(), "prepared local deployment follows installed pending-restart local deployment");
var caseRoot = Clone(next); caseRoot.PackageRoot = previous.PackageRoot.ToLowerInvariant() + "\\"; caseRoot.Namespace = "aiaet"; caseRoot.Package = "modconfigenhance";
Check(Policy(n: caseRoot), "identity and root comparison tolerate casing and trailing separators");
foreach (var state in new[] { UpdateStates.Queued, UpdateStates.Downloading, UpdateStates.Paused, UpdateStates.WaitingExit, UpdateStates.Deploying,
    UpdateStates.RollbackWaiting, UpdateStates.RollingBack, UpdateStates.Failed, UpdateStates.Completed, UpdateStates.Superseded })
{
    var other = Clone(previous); other.State = state;
    Check(!Policy(p: other), "previous state " + state + " cannot be superseded by policy");
}
foreach (string phase in new[] { "prepared", "placing", "side-by-side", "committing", "rolling-back", "withdrawing", "" })
    Check(!Policy(pp: phase), "previous transaction phase " + phase + " cannot be superseded");
Check(!Policy(pp: null) && !Policy(np: null) && !Policy(np: "installed") && !Policy(np: "committing"), "missing, unfinished or non-prepared next transaction is rejected");
foreach (bool changeNext in new[] { false, true })
{
    var other = Clone(changeNext ? next : previous); other.InstallMode = "exit-only";
    Check(!Policy(n: changeNext ? other : next, p: changeNext ? previous : other), "store task is rejected on " + (changeNext ? "next" : "previous"));
    other = Clone(changeNext ? next : previous); other.RollbackRequested = true;
    Check(!Policy(n: changeNext ? other : next, p: changeNext ? previous : other), "rollback-requested task is rejected on " + (changeNext ? "next" : "previous"));
    other = Clone(changeNext ? next : previous); other.RecoveryRequired = true;
    Check(!Policy(n: changeNext ? other : next, p: changeNext ? previous : other), "recovery task is rejected on " + (changeNext ? "next" : "previous"));
}
var identity = Clone(next); identity.Namespace = "AnotherAuthor"; Check(!Policy(n: identity), "different namespace rejected");
identity = Clone(next); identity.Package = "AnotherPackage"; Check(!Policy(n: identity), "different package rejected");
identity = Clone(next); identity.PackageRoot += "-other"; Check(!Policy(n: identity), "different installation root rejected");
identity = Clone(next); identity.TargetVersion = "2.1.0"; Check(Policy(n: identity), "successive local versions may supersede installed local versions");
Check(UpdateStates.History(UpdateStates.Superseded) && !UpdateStates.Pending(UpdateStates.Superseded) && !UpdateStates.Busy(UpdateStates.Superseded)
    && !UpdateStates.BlocksPublication(UpdateStates.Superseded), "superseded records remain history and no longer block later installation");
Check(UpdateStates.BlocksPublication(new UpdateJob { State = UpdateStates.Superseded, RecoveryRequired = true }), "any recovery-required record still blocks publication");

Fixture MakeFixture(string name, int previousCount = 1)
{
    string root = Path.Combine(fixtures, name, "BepInEx"), package = Path.Combine(root, "plugins", "MCE");
    Directory.CreateDirectory(package);
    string target = Path.Combine(package, "feature.txt"); File.WriteAllText(target, "old installed payload");
    var incoming = Job(Guid.NewGuid().ToString("N"), package, UpdateStates.Queued);
    incoming.WorkDirectory = Path.Combine(root, "MCE", "jobs", incoming.Id); incoming.BackupDirectory = Path.Combine(root, "MCE", "backups", incoming.Id);
    Directory.CreateDirectory(incoming.WorkDirectory);
    string prepared = Path.Combine(incoming.WorkDirectory, "new-feature.txt"); File.WriteAllText(prepared, "new prepared payload");
    var jobs = new List<UpdateJob>(); var plans = new List<Transaction>(); string firstBackup = "";
    for (int i = 0; i < previousCount; i++)
    {
        var prior = Job(Guid.NewGuid().ToString("N"), package);
        prior.WorkDirectory = Path.Combine(root, "MCE", "jobs", prior.Id); prior.BackupDirectory = Path.Combine(root, "MCE", "backups", prior.Id);
        Directory.CreateDirectory(prior.BackupDirectory);
        string backup = Path.Combine(prior.BackupDirectory, "backup-marker.txt"); File.WriteAllText(backup, "old installed payload");
        if (i == 0) firstBackup = backup;
        var plan = new Transaction { Root = root, PackageRoot = package, JobId = prior.Id, OldVersion = "1.9.0", NewVersion = "2.0.0", Phase = "installed", StoreVerified = false,
            Desired = new() { new() { Target = target, Source = backup, Hash = Files.Hash(target), Written = true } } };
        Files.Save(Transactions.PathFor(prior), plan); jobs.Add(prior); plans.Add(plan);
    }
    var preparedPlan = new Transaction { Root = root, PackageRoot = package, JobId = incoming.Id, OldVersion = "2.0.0", NewVersion = "2.0.0", Phase = "prepared", StoreVerified = false,
        Originals = new() { new() { Target = target, Backup = firstBackup, Hash = Files.Hash(target) } },
        Desired = new() { new() { Source = prepared, Target = target, Hash = Files.Hash(prepared) } } };
    Files.Save(Transactions.PathFor(incoming), preparedPlan);
    Files.Save(Path.Combine(root, "MCE", "queue.json"), new UpdateSnapshot { Jobs = jobs });
    return new(root, incoming, jobs, preparedPlan, plans, target, firstBackup);
}
void Handle(QueueWorker worker, UpdateCommand command)
{
    try { typeof(QueueWorker).GetMethod("Handle", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(worker, new object[] { command }); }
    catch (TargetInvocationException e) when (e.InnerException != null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
}
UpdateCommand Add(Fixture f) => new() { Id = Guid.NewGuid().ToString("N"), Action = "add", Job = f.Next };

var good = MakeFixture("valid-follow-up");
using (var worker = new QueueWorker(good.Root))
{
    Handle(worker, Add(good));
    Check(worker.Current.Jobs.Count == 2 && worker.Current.Jobs.Single(j => j.Id == good.Previous[0].Id).State == UpdateStates.Superseded
        && worker.Current.Jobs.Single(j => j.Id == good.Next.Id).State == UpdateStates.Queued, "real Handle supersedes installed predecessor and queues prepared local task");
    Check(File.ReadAllText(good.Target) == "old installed payload" && File.ReadAllText(good.Backup) == "old installed payload", "real Handle preserves installed file and rollback backup before execution");
    Reject(() => Handle(worker, new() { Id = Guid.NewGuid().ToString("N"), Action = "rollback", JobId = good.Previous[0].Id }), "real Handle refuses rollback from superseded historical record");
    Check(File.ReadAllText(good.Target) == "old installed payload", "rejected historical rollback leaves installed files unchanged");
}
Check(Files.Read<UpdateSnapshot>(Path.Combine(good.Root, "MCE", "queue.json")).Jobs.Single(j => j.Id == good.Previous[0].Id).State == UpdateStates.Superseded, "real Handle persists superseded history in queue.json");

var changedInstalled = MakeFixture("changed-installed-file"); File.WriteAllText(changedInstalled.Target, "external user edit");
using (var worker = new QueueWorker(changedInstalled.Root))
{
    Reject(() => Handle(worker, Add(changedInstalled)), "real Handle verifies old installed bytes and rejects changed installed file");
    Check(worker.Current.Jobs.Count == 1 && worker.Current.Jobs[0].State == UpdateStates.PendingRestart && File.ReadAllText(changedInstalled.Target) == "external user edit", "failed installed verification preserves predecessor and external edit");
}
var changedPrepared = MakeFixture("changed-prepared-file"); File.WriteAllText(changedPrepared.NextPlan.Desired[0].Source, "modified staged content");
using (var worker = new QueueWorker(changedPrepared.Root))
{
    Reject(() => Handle(worker, Add(changedPrepared)), "real Handle validates prepared sources before superseding");
    Check(worker.Current.Jobs.Count == 1 && worker.Current.Jobs[0].State == UpdateStates.PendingRestart, "bad prepared source keeps predecessor active");
}
var unfinished = MakeFixture("unfinished-predecessor"); unfinished.PreviousPlans[0].Phase = "side-by-side"; Files.Save(Transactions.PathFor(unfinished.Previous[0]), unfinished.PreviousPlans[0]);
using (var worker = new QueueWorker(unfinished.Root)) Reject(() => Handle(worker, Add(unfinished)), "real Handle rejects unfinished predecessor transaction");
var store = MakeFixture("store-predecessor"); store.Previous[0].InstallMode = "exit-only"; Files.Save(Path.Combine(store.Root, "MCE", "queue.json"), new UpdateSnapshot { Jobs = store.Previous });
using (var worker = new QueueWorker(store.Root)) Reject(() => Handle(worker, Add(store)), "real Handle keeps ordinary store predecessor blocked");
var multi = MakeFixture("two-installed-predecessors", 2);
using (var worker = new QueueWorker(multi.Root))
{
    Handle(worker, Add(multi)); Check(worker.Current.Jobs.Where(j => j.Id != multi.Next.Id).All(j => j.State == UpdateStates.Superseded), "real Handle verifies and supersedes every eligible installed predecessor");
}
var multiBad = MakeFixture("bad-second-predecessor", 2); multiBad.PreviousPlans[1].Desired[0].Hash = new string('0', 64); Files.Save(Transactions.PathFor(multiBad.Previous[1]), multiBad.PreviousPlans[1]);
using (var worker = new QueueWorker(multiBad.Root))
{
    Reject(() => Handle(worker, Add(multiBad)), "real Handle checks all predecessors before state mutation");
    Check(worker.Current.Jobs.Count == 2 && worker.Current.Jobs.All(j => j.State == UpdateStates.PendingRestart), "one invalid predecessor prevents partial superseding");
}
Console.WriteLine($"PASS: {checks} local deployment policy and real QueueWorker.Handle checks. Fixtures: {fixtures}");

internal sealed record Fixture(string Root, UpdateJob Next, List<UpdateJob> Previous, Transaction NextPlan, List<Transaction> PreviousPlans, string Target, string Backup);
