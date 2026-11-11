using System.Text.Json;
using ModConfigEnhance.Updating;
namespace MCE.UpdateWorker;

internal static class WorkerRecovery
{
    internal static string BuildIdentity => typeof(WorkerRecovery).Assembly.ManifestModule.ModuleVersionId.ToString("N");
    internal static void ValidateIndex(string root)
    {
        string path = ManagerIdentity.Runtime(root, "packs", "index.json"); Files.RejectLinks(root, path);
        if (!File.Exists(path)) return;
        using var document = JsonDocument.Parse(File.ReadAllText(path)); var index = document.RootElement;
        WorkerProtocol.RequireProtocol(index.TryGetProperty("SchemaVersion", out var schema) ? schema.GetInt32() : 1,
            index.TryGetProperty("MinimumWorkerProtocol", out var minimum) ? minimum.GetInt32() : 1);
    }
    internal sealed record Entry(string Kind, string Id, string Journal, string Phase, int SourceProcessId);
    static void Checked(string root, string path) { if (!Files.Within(ManagerIdentity.Runtime(root), path)) throw new IOException("Recovery path outside management tree."); Files.RejectLinks(root, path); }
    internal static List<Entry> Scan(string root)
    {
        Files.ValidateRoot(root); ValidateIndex(root); var entries = new List<Entry>();
        string home = ManagerIdentity.Runtime(root); Checked(root, Path.Combine(home, "queue.json"));
        string queue = Path.Combine(home, "queue.json");
        if (File.Exists(queue))
        {
            var snapshot = Files.Read<UpdateSnapshot>(queue); if (snapshot.SchemaVersion != 1) throw new IOException("Unsupported queue schema.");
            foreach (var job in snapshot.Jobs)
            {
                if (!PackStorage.Id(job.Id)) throw new IOException("Invalid recovery job ID.");
                string journal = Path.Combine(home, "jobs", job.Id, "transaction.json"); Checked(root, journal);
                if (!File.Exists(journal))
                {
                    if (job.RecoveryRequired || job.State is UpdateStates.RollbackWaiting or UpdateStates.RollingBack or UpdateStates.Deploying)
                        throw new IOException("Update recovery journal missing; original queue retained.");
                    continue;
                }
                var plan = ReadJournal<Transaction>(journal, "Phase", "Root", "PackageRoot", "JobId", "Originals", "Desired", "Candidates", "Parameters");
                if (!(PackageRules.HasUnfinishedWrites(plan.Phase) || plan.RollbackRequested)) continue;
                foreach (var original in plan.Originals.Concat(plan.Parameters))
                {
                    string backups = Path.Combine(home, "backups", job.Id);
                    if (!Files.Within(backups, original.Backup)) throw new IOException("Update backup outside selected job.");
                    Checked(root, original.Backup);
                }
                Files.ValidateJob(root, job); Installer.ValidateBackups(plan);
                if (plan.JobId != job.Id || !Files.Compare.Equals(Path.GetFullPath(plan.Root), root) || !Files.Compare.Equals(Path.GetFullPath(plan.PackageRoot), Path.GetFullPath(job.PackageRoot))) throw new IOException("Recovery installation mismatch.");
                entries.Add(new("update", job.Id, journal, plan.Phase, job.SourceProcessId));
            }
        }
        string packTask = Path.Combine(home, "pack-task.json"); Checked(root, packTask);
        if (File.Exists(packTask))
        {
            var task = Files.Read<PackTask>(packTask);
            if (!PackStorage.Id(task.Id) || task.SchemaVersion != 1) throw new IOException("Invalid pack task.");
            string journal = Path.Combine(home, "packs", ".transactions", task.Id, "switch.json"); Checked(root, journal);
            if (File.Exists(journal))
            {
                var plan = ReadJournal<PackSwitchPlan>(journal, "Phase", "Id", "Before", "After", "Backup");
                if (plan.Id != task.Id || plan.SchemaVersion is not (1 or 2)) throw new IOException("Pack journal identity/schema mismatch.");
                if (plan.Phase is not ("installed" or "rolled-back")) entries.Add(new("pack", task.Id, journal, plan.Phase, plan.SourceProcessId));
            }
            else if (task.State is "switching" or "recovery-required") throw new IOException("Pack recovery journal missing; originals retained.");
        }
        string shopTask = Path.Combine(home, "shop", "task.json"); Checked(root, shopTask);
        if (File.Exists(shopTask))
        {
            var task = Files.Read<ShopTask>(shopTask);
            if (!PackStorage.Id(task.Id) || task.SchemaVersion != 1) throw new IOException("Invalid shop task.");
            string journal = Path.Combine(home, "shop", "tasks", task.Id, "transaction.json"); Checked(root, journal);
            if (File.Exists(journal))
            {
                var plan = ReadJournal<ShopQueue.ShopTransaction>(journal, "Phase", "Before", "After", "PackBefore", "PackAfter", "Placed", "Removed");
                if (plan.Phase is not ("installed" or "rolled-back") && task.State is "committing" or "recovery-required") entries.Add(new("shop", task.Id, journal, plan.Phase, task.Request.SourceProcessId));
            }
            else if (task.State is "committing" or "recovery-required") throw new IOException("Shop recovery journal missing; originals retained.");
        }
        string operations=Path.Combine(home,"operations");
        if(Directory.Exists(operations))foreach(string directory in Directory.GetDirectories(operations)){
            string taskPath=Path.Combine(directory,"shop","task.json");if(!File.Exists(taskPath))continue;
            var task=Files.Read<ShopTask>(taskPath);if(!PackStorage.Id(task.Id)||!PackStorage.Id(Path.GetFileName(directory)))throw new IOException("Invalid operation recovery identity.");
            string journal=Path.Combine(directory,"shop","tasks",task.Id,"transaction.json");Checked(root,journal);
            if(task.State is "committing" or "recovery-required"){
                if(!File.Exists(journal))throw new IOException("Operation recovery journal missing.");
                var plan=ReadJournal<ShopQueue.ShopTransaction>(journal,"Phase","Before","After","PackBefore","PackAfter","Placed","Removed");
                if(plan.Phase is not ("installed" or "rolled-back"))entries.Add(new("operation",Path.GetFileName(directory),journal,plan.Phase,task.Request.SourceProcessId));
            }
        }
        string enablement = Path.Combine(home, "enablement-recovery.bin"); Checked(root, enablement);
        if (File.Exists(enablement)) throw new IOException("Existing enablement journal requires its dedicated recovery; this entry never auto-recovers it or consumes commands.");
        return entries;
    }
    internal static T ReadJournal<T>(string path, params string[] required)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (string field in required) if (!document.RootElement.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) throw new IOException("Damaged journal; missing " + field + ": " + path);
        string phase = document.RootElement.GetProperty("Phase").GetString() ?? "";
        if (phase is not ("prepared" or "placing" or "side-by-side" or "committing" or "rolling-back" or "withdrawing" or "installed" or "rolled-back")) throw new IOException("Unsupported recovery journal phase: " + phase);
        return Files.Read<T>(path);
    }
}

internal sealed partial class QueueWorker
{
    /// <summary>Never constructs a normal worker, calls Run/Pump, applies controls or consumes commands.</summary>
    internal static void RecoveryOnly(string root, string? selector, bool statusOnly)
    {
        Files.ValidateRoot(root);
        using var lease = InstallationWriteLease.AcquireWithoutRecovery(root);
        var entries = WorkerRecovery.Scan(root);
        if (selector != null)
        {
            entries = entries.Where(e => selector == e.Kind + ":" + e.Id || Files.Compare.Equals(selector, e.Journal)).ToList();
            if (entries.Count != 1) throw new IOException("Select an existing supported journal using kind:32hexId or its exact absolute path.");
        }
        foreach (var entry in entries)
        {
            bool running = Files.GameRunning(new UpdateJob { SourceProcessId = entry.SourceProcessId });
            Console.WriteLine(entry.Kind + ":" + entry.Id + " " + entry.Phase + (running ? " waiting-exit" : " recoverable") + " " + entry.Journal);
            if (statusOnly || running) continue;
            WorkerTargetProtocol.RequireCachedExecution(root);
            if (entry.Kind == "pack")
            {
                var storage = new PackStorage(root); var plan = Files.Read<PackSwitchPlan>(entry.Journal);
                PackTransactions.Rollback(storage, plan);
                string path = ManagerIdentity.Runtime(root, "pack-task.json"); var task = Files.Read<PackTask>(path);
                task.State = "failed"; task.Message = "Existing interrupted transaction recovered; preview again to retry."; Files.Save(path, task);
            }
            else if (entry.Kind == "shop") ShopQueue.RecoverExistingOnly(root, entry.Id);
            else if(entry.Kind=="operation")ShopQueue.RecoverIndependentOnly(root,entry.Id);
            else
            {
                string path = ManagerIdentity.Runtime(root, "queue.json"); var snapshot = Files.Read<UpdateSnapshot>(path);
                var job = snapshot.Jobs.Single(j => j.Id == entry.Id);
                job.WorkDirectory = ManagerIdentity.Runtime(root, "jobs", job.Id); job.BackupDirectory = ManagerIdentity.Runtime(root, "backups", job.Id);
                var plan = Files.Read<Transaction>(entry.Journal); Installer.Rollback(job, plan, null, Array.Empty<string>());
                job.State = UpdateStates.RolledBack; job.RecoveryRequired = false; snapshot.Revision++; Files.Save(path, snapshot);
            }
        }
        if (entries.Count == 0) Console.WriteLine("No existing unfinished supported transactions. New commands were not processed.");
    }
}

internal sealed partial class ShopQueue
{
    ShopQueue(string root, bool recoveryOnly) { storage = new PackStorage(root); }
    internal static void RecoverIndependentOnly(string root,string id)
    {
        var queue=new ShopQueue(root,ManagerIdentity.Runtime(root,"operations",id,"shop"),executeIndependent:true);
        var task=queue.Read()??throw new IOException("Missing operation recovery task.");
        var plan=WorkerRecovery.ReadJournal<ShopTransaction>(Path.Combine(queue.Work(task),"transaction.json"),"Phase","Before","After","PackBefore","PackAfter","Placed","Removed");
        queue.Rollback(task,plan);task.State="failed";task.Message="Existing transaction recovered; preview again to retry.";queue.Save(task);
    }
    internal static void RecoverExistingOnly(string root, string id)
    {
        var queue = new ShopQueue(root, true); var task = queue.Read() ?? throw new IOException("Missing shop task.");
        if (task.Id != id || task.State is not ("committing" or "recovery-required")) throw new IOException("Shop recovery selection changed.");
        var plan = WorkerRecovery.ReadJournal<ShopTransaction>(Path.Combine(queue.Work(task), "transaction.json"), "Phase", "Before", "After", "PackBefore", "PackAfter", "Placed", "Removed");
        queue.Rollback(task, plan); task.State = "failed"; task.Message = "Existing transaction recovered; preview again to retry."; queue.Save(task);
    }
}
