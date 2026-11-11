using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal sealed partial class QueueWorker : IDisposable
{
    readonly string root, home, snapshotPath;
    readonly InstallationWriteLease singleton;
    readonly PackQueue packs;
    readonly ShopQueue shop;
    readonly object gate = new();
    readonly HashSet<string> seen = new(StringComparer.Ordinal);
    readonly HashSet<string> deferredLogged = new(StringComparer.Ordinal);
    readonly Dictionary<string, DateTime> deferUntil = new(StringComparer.Ordinal);
    readonly List<UpdateJob> activeJobs = new();
    UpdateSnapshot snapshot;
    Task? active;
    CancellationTokenSource? cancellation;
    DateTime progressAt, idleSince = DateTime.UtcNow, foregroundAt;
    internal static Action<WorkerStatusSnapshot>? ForegroundStatus = null;
    internal Func<bool>? StopRequested = null;

    /// <summary>Thrown by Handle when a command is valid but cannot run yet; the file stays in commands/ and is retried on the next loop.</summary>
    internal sealed class DeferredCommandException : Exception { internal DeferredCommandException(string reason) : base(reason) { } }

    internal QueueWorker(string root)
    {
        WorkerTargetProtocol.Initialize();
        WorkerRecovery.ValidateIndex(root);
        Files.ValidateRoot(root); this.root = root; home = Path.Combine(root, "MCE");
        Directory.CreateDirectory(home); Files.RejectLinks(Directory.GetParent(root)!.FullName, home);
        WorkerLog.Initialize(home);
        if (UpdateWriteMarker.IsActive(root)) WorkerLog.Warn("a previous write marker was still active at startup");
        UpdateWriteMarker.Clear(root);
        singleton = InstallationWriteLease.AcquireWorkerInstance(root);

        packs = new PackQueue(root); shop = new ShopQueue(root);
        CloseOrphanedPreview();
        snapshotPath = Path.Combine(home, "queue.json");
        snapshot = File.Exists(snapshotPath) ? Files.Read<UpdateSnapshot>(snapshotPath) : new();
        if (snapshot.SchemaVersion != 1) throw new IOException("更新队列版本不受支持，保留原队列。");
        snapshot.ResourceCapability = WorkerProtocol.ResourceCapability; snapshot.WorkerProtocol = WorkerProtocol.Current; snapshot.WorkerBuild = WorkerRecovery.BuildIdentity;
        snapshot.WorkerVersion = typeof(QueueWorker).Assembly.GetName().Version!.ToString(3); snapshot.WorkerProcessId = FrameworkCompat.ProcessId; snapshot.Error = "";
        Directory.CreateDirectory(Path.Combine(home, "commands")); Directory.CreateDirectory(Path.Combine(home, "processed"));
        foreach (var file in Directory.GetFiles(Path.Combine(home, "processed"), "*.json"))
            try { seen.Add(Files.Read<UpdateCommand>(file).Id); } catch { }
        foreach (var job in snapshot.Jobs)
        {
            if (!Regex.IsMatch(job.Id, "^[a-fA-F0-9]{32}$")) throw new IOException("队列任务编号无效。");
            SetDirectories(job);
            if (job.LoadedAssemblyPaths.Count > 0)
            {
                if (snapshot.LoadedAssemblyPaths.Count == 0) snapshot.LoadedAssemblyPaths = job.LoadedAssemblyPaths;
                job.LoadedAssemblyPaths = new List<string>();
            }
            var existingPlan = ReadPlan(job);
            if (existingPlan?.RollbackRequested == true || existingPlan?.Phase is "committing" or "rolling-back" or "withdrawing")
                job.State = UpdateStates.RollbackWaiting;
            if (job.RecoveryRequired && job.State is UpdateStates.Stopped or UpdateStates.Paused or UpdateStates.Removed or UpdateStates.Failed)
                job.State = UpdateStates.RollbackWaiting;
            if (UpdateStates.Busy(job.State))
            {
                var plan = ReadPlan(job);
                job.State = plan?.Phase is "committing" or "rolling-back" or "withdrawing" ? UpdateStates.RollbackWaiting : UpdateStates.Queued;
                job.Message = UpdateMessages.Encode("正在恢复中断的任务。");
            }
        }
        int archived = InstallStorage.Prune(home, snapshot);
        WorkerLog.Info("worker " + snapshot.WorkerVersion + " pid=" + snapshot.WorkerProcessId + " root=" + root + " jobs=" + snapshot.Jobs.Count + " archived=" + archived);
        try { using var recoveryWrites = InstallationWriteLease.Acquire(root); foreach (var job in snapshot.Jobs.ToArray()) ApplySavedControl(job); }
        catch (InstallationWriteLease.BusyException) { /* Run retries recovery/control work after the current writer releases. */ }
        Save();
    }
    static readonly AsyncLocal<bool> preparingContext = new();
    Task? preparation;
    CancellationTokenSource? preparingCancellation;
    public void Dispose() { cancellation?.Cancel(); preparingCancellation?.Cancel();
        try { Task.WhenAll(new[] { active, preparation }.Where(t => t != null).Cast<Task>()).GetAwaiter().GetResult(); } finally { cancellation?.Dispose(); preparingCancellation?.Dispose(); singleton.Dispose(); } }
    void SetDirectories(UpdateJob job)
    {
        job.WorkDirectory = Path.Combine(home, "jobs", job.Id);
        job.BackupDirectory = Path.Combine(home, "backups", job.Id);
    }
    readonly StateChangePublication publication = new();
    void Save()
    {
        lock (gate) publication.Publish(() => JsonSerializer.Serialize(snapshot, Files.Json), () => {
            var previous = snapshot.Revision;
            try { snapshot.Revision++; Files.Save(snapshotPath, snapshot); }
            catch { snapshot.Revision = previous; throw; }
        });
    }
    /// <summary>Jobs whose files this process changed (deploy or rollback), for the end-of-run notice.</summary>
    internal readonly HashSet<string> WroteFiles = new(StringComparer.Ordinal);
    void State(UpdateJob job, string state, string message, string error = "")
    {
        lock (gate)
        {
            if (state is UpdateStates.Deploying or UpdateStates.RollingBack) WroteFiles.Add(job.Id);
            ReadPlan(job); string previous = job.State;
            job.State = state; job.Message = UpdateMessages.Encode(message); job.Error = UpdateMessages.Encode(error); job.UpdatedUtc = DateTime.UtcNow.ToString("O"); Save();
            WorkerLog.Info((job.DisplayName.Length > 0 ? job.DisplayName : job.Package) + " " + job.InstalledVersion + "→" + job.TargetVersion + " [" + previous + " → " + state + "] " + message + (error.Length > 0 ? " | " + error.Replace("\r\n", "\n").Split('\n')[0] : ""));
        }
    }
    void Warn(UpdateJob job, string text) { lock (gate) if (!job.Warnings.Contains(text)) job.Warnings.Add(text); }
    /// <summary>Up to four jobs run side by side while any of them may serialize the whole queue in Save(). A list that
    /// another task enumerates must never be appended to outside the gate ("Collection was modified"), so planning and
    /// bookkeeping write into a private list and the result is merged here, under the lock.</summary>
    T WithPrivateWarnings<T>(UpdateJob job, Func<T> work)
    {
        List<string> shared; lock (gate) { shared = job.Warnings; job.Warnings = new List<string>(shared); }
        var scratch = job.Warnings;
        try { return work(); }
        finally { lock (gate) { foreach (string text in scratch) if (!shared.Contains(text)) shared.Add(text); job.Warnings = shared; } }
    }
    void WithPrivateWarnings(UpdateJob job, Action work) => WithPrivateWarnings<object?>(job, () => { work(); return null; });
    internal UpdateSnapshot Current => snapshot;

    internal async Task Run(bool once)
    {
        while (true)
        {
            if (StopRequested?.Invoke() == true) { cancellation?.Cancel(); preparingCancellation?.Cancel(); await Task.WhenAll(new[] { active, preparation }.Where(t => t != null).Cast<Task>()); break; }
            if (DateTime.UtcNow >= foregroundAt) { foregroundAt = DateTime.UtcNow.AddSeconds(1); PublishStatus(); }
            ProcessCommands();
            bool preparedThisTurn = false;
            if (preparation?.IsCompleted == true)
            { await preparation; preparation = null; preparingCancellation?.Dispose(); preparingCancellation = null; preparedThisTurn = true; }
            if (preparation != null) { await Task.Delay(50); continue; }
            if (active == null && !preparedThisTurn)
            {
                preparingCancellation = new CancellationTokenSource(); var prepareToken = preparingCancellation.Token;
                preparation = Task.Run(async () => {
                    try { preparingContext.Value = true; await PumpIndependentOperations(prepareToken); prepareToken.ThrowIfCancellationRequested(); await shop.Pump(snapshot.Network, prepareToken); }
                    catch (OperationCanceledException) when (prepareToken.IsCancellationRequested) { }
                });
                await Task.Delay(20);
                if (!preparation.IsCompleted) continue;
                await preparation; preparation = null; preparingCancellation.Dispose(); preparingCancellation = null;
            }
            if (active != null && active.IsCompleted)
            {
                await active; active = null; cancellation?.Dispose(); cancellation = null;
                foreach (var finished in activeJobs.ToArray())
                {
                    try { using var controlWrites = InstallationWriteLease.Acquire(root); ApplySavedControl(finished); }
                    catch (InstallationWriteLease.BusyException) { /* saved control remains durable */ }
                }
                foreach (var interrupted in snapshot.Jobs.Where(j => UpdateStates.Busy(j.State)).ToArray())
                {
                    try { using var writes = InstallationWriteLease.Acquire(root); ApplySavedControl(interrupted); if (UpdateStates.Busy(interrupted.State)) State(interrupted, UpdateStates.Queued, "沿用原目标版本继续处理。"); }
                    catch (InstallationWriteLease.BusyException) { }
                }
                activeJobs.Clear();
                if (once) break;
            }
            if (active == null)
            {
                if (shop.BlocksWrites) { if (once) break; await Task.Delay(500); continue; }
                try { using var packWrites = InstallationWriteLease.Acquire(root); foreach (var controlled in snapshot.Jobs.ToArray()) ApplySavedControl(controlled); await packs.Pump(snapshot); }
                catch (InstallationWriteLease.BusyException) { await Task.Delay(50); continue; }
                if (packs.BlocksUpdates) { await Task.Delay(500); continue; }
                var afterExit = new List<(UpdateJob Job, Transaction Plan)>();
                foreach (var job in snapshot.Jobs.Where(j => j.State == UpdateStates.WaitingExit || j.State == UpdateStates.PendingRestart || j.State == UpdateStates.RollbackWaiting).ToList())
                {
                    var plan = ReadPlan(job);
                    if (plan == null) { if (job.State != UpdateStates.PendingRestart) State(job, UpdateStates.Failed, "找不到更新事务，请重试。"); continue; }
                    if (job.State == UpdateStates.PendingRestart && plan.Phase == "installed") continue;
                    if (job.State == UpdateStates.RollbackWaiting ? Files.GameRunning(job) : Installer.TouchesRunningGame(plan, job, snapshot.LoadedAssemblyPaths)) continue;
                    if (job.State != UpdateStates.RollbackWaiting) { afterExit.Add((job, plan)); continue; }
                    try
                    {
                        using var writes = await InstallationWriteLease.WaitAsync(root, CancellationToken.None);
                        State(job, UpdateStates.RollingBack, "正在退出后恢复旧版本。"); WithPrivateWarnings(job, () => Installer.Rollback(job, plan, null, snapshot.LoadedAssemblyPaths));
                        State(job, plan.RemoveAfterRollback ? UpdateStates.Removed : UpdateStates.RolledBack, "已恢复旧文件和原启停状态。");
                    }
                    catch (Transactions.NeedExitException) { }
                    catch (Exception error) { RecoverFailure(job, error); }
                }
                // Everything that waited for the game to exit installs together once it has; packages only serialize on the shared records.
                if (afterExit.Count > 0)
                {
                    await Task.WhenAll(afterExit.Select(entry => Task.Run(() =>
                    {
                        var (job, plan) = entry;
                        try
                        {
                            if (plan.Phase == "installed") return;
                            var dependency = DependencyGate(job, plan, enqueue: false);
                            if (dependency.Waiting) { if (job.Message != UpdateMessages.Encode(dependency.Message)) State(job, job.State, dependency.Message); return; }
                            State(job, UpdateStates.Deploying, job.Layout ? "正在移动目录。" : job.Uninstall ? "正在移除文件。" : "正在安装已验证的新版本。");
                            WithPrivateWarnings(job, () => Installer.Commit(job, plan, () => Installer.Wait(root, CancellationToken.None))); job.Progress = 1;
                            State(job, UpdateStates.PendingRestart, "");
                        }
                        catch (Transactions.NeedExitException) { }
                        catch (Exception error) { RecoverFailure(job, error); }
                    })));
                }
                var ready = snapshot.Jobs.Where(j => j.State == UpdateStates.Queued && (!deferUntil.TryGetValue(j.Id, out var until) || DateTime.UtcNow >= until)).OrderBy(j => j.UpdatedUtc, StringComparer.Ordinal).Take(4).ToArray();
                if (ready.Length > 0)
                {
                    idleSince = DateTime.UtcNow; cancellation = new CancellationTokenSource(); var token = cancellation.Token;
                    activeJobs.AddRange(ready);
                    active = Task.Run(() => Task.WhenAll(ready.Select(j => Execute(j, token))));
                }
            }
            bool needsWatch = IndependentWatching || shop.Watching || packs.Watching || snapshot.Jobs.Any(j => j.State == UpdateStates.Queued || j.State == UpdateStates.WaitingExit || j.State == UpdateStates.RollbackWaiting
                || j.State == UpdateStates.PendingRestart && ReadPlan(j)?.Phase != "installed");
            if (active == null && !needsWatch)
            {
                if (InstallStorage.SweepDue(home)) { InstallStorage.MarkSwept(home); try { InstallStorage.Sweep(home, snapshot); WorkerLog.Info("storage sweep done"); } catch (Exception error) { WorkerLog.Warn("storage sweep: " + error.Message); } }
                if (once || DateTime.UtcNow - idleSince > TimeSpan.FromSeconds(2)) break;
            }
            else idleSince = DateTime.UtcNow;

            await Task.Delay(200);
        }
        PublishStatus();
        snapshot.WorkerProcessId = 0; Save();
        WorkerLog.Info("worker exit" + (StopRequested?.Invoke() == true ? " (stop requested)" : " (idle)"));
    }

    internal void ProcessCommands()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(home, "commands"), "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            UpdateCommand? validated = null;
            bool handled = false;
            try
            {
                Files.RejectLinks(home, file);
                var command = Files.Read<UpdateCommand>(file);
                if (command.SchemaVersion is not (1 or 2) || command.MinimumWorkerProtocol < 1 || command.MinimumWorkerProtocol > WorkerProtocol.Current || command.SchemaVersion == 2 && command.MinimumWorkerProtocol != WorkerProtocol.Current || !Regex.IsMatch(command.Id, "^[a-fA-F0-9]{32}$")) throw new IOException("更新命令格式不受支持。");
                validated = command;
                if (seen.Contains(command.Id))
                {
                    if (!File.Exists(Path.Combine(home, "command-receipts", command.Id + ".json")) && !File.Exists(Path.Combine(home, "command-rejections", command.Id + ".json")))
                        Files.Save(Path.Combine(home, "command-receipts", command.Id + ".json"), new { command.Id, Status = "historical-unknown", Error = "Historical processed command has no durable outcome. It was not replayed. Review operation history before explicitly discarding this submission." });
                }
                else
                {
                    Handle(command); handled = true; seen.Add(command.Id);
                    lock (gate) { snapshot.Error = ""; Save(); }
                    Files.Save(Path.Combine(home, "command-receipts", command.Id + ".json"), new { command.Id, Status = "accepted" });
                    WorkerLog.Info("command " + command.Action + " " + command.Id.Substring(0, 8) + (command.JobId.Length > 0 ? " job=" + command.JobId.Substring(0, Math.Min(8, command.JobId.Length)) : "") + " accepted");
                }
            }
            catch (DeferredCommandException deferred)
            {
                lock (gate) { if (validated != null) { var waiting = snapshot.Jobs.FirstOrDefault(j => j.Id == validated.JobId); if (waiting != null && waiting.Message.Length == 0) waiting.Message = UpdateMessages.Encode(deferred.Message); } }
                if (validated != null && deferredLogged.Add(validated.Id)) WorkerLog.Info("command " + validated.Action + " " + validated.Id.Substring(0, 8) + " deferred: " + deferred.Message);
                continue;
            }
            catch (Exception error)
            {
                string rejectedId = validated?.Id ?? "";
                if (!handled && Regex.IsMatch(rejectedId, "^[a-fA-F0-9]{32}$"))
                { seen.Add(rejectedId); Files.Save(Path.Combine(home, "command-rejections", rejectedId + ".json"), new { Id = rejectedId, Error = error.Message }); }
                lock (gate) { snapshot.Error = UpdateMessages.Encode(error.Message); Save(); }
                WorkerLog.Error("command " + (validated?.Action ?? "?") + " " + (rejectedId.Length >= 8 ? rejectedId.Substring(0, 8) : rejectedId) + " rejected", error);
            }
            Files.MoveAside(file, Path.Combine(home, "processed", Path.GetFileName(file)));
        }
    }
    void Handle(UpdateCommand command)
    {
        WorkerRecovery.ValidateIndex(root);
        idleSince = DateTime.UtcNow;
        if (command.LoadedAssemblyPaths.Count > 0) lock (gate) snapshot.LoadedAssemblyPaths = command.LoadedAssemblyPaths;
        if (command.Action == "update-batch") { AcceptIndependentOperations(command); return; }
        if (command.Action == "clear-finished") { ClearFinished(); return; }
        if (command.Action == "shop-dismiss") { DismissShopTask(command.JobId); return; }
        if (command.Action == "operation-remove") { RemoveIndependentOperation(command.JobId); return; }
        if (command.Action is "operation-ignore" or "operation-retry" or "operation-cancel") { ControlIndependentOperation(command); return; }
        if (command.Action == "resource-batch") { AcceptIndependentOperations(command); return; }
        if (command.Action is "resource-retry" or "resource-skip" or "resource-cancel" or "resource-ignore-dependencies") { CloseLegacyBatch(); return; }
        if (preparation != null && !preparingContext.Value && command.Action is not ("network" or "pause" or "stop" or "resource-cancel" or "resource-ignore-dependencies" or "add" or "retry" or "resume" or "remove" or "observe")) throw new DeferredCommandException("资源准备进行中，稍后自动继续。");
        if (command.Action == "shop" || command.Action == "local-import" || command.Action == "layout")
        {
            // Store installs and uninstalls are queue jobs too; only local import, folder organization and the review-only steps stay on the store queue.
            if (command.Action == "shop" && command.Shop != null && command.Shop.Operation is "preview-install" or "preview-uninstall" or "confirm-install" or "confirm-uninstall" or "approve-download")
            {
                var inventory = InstallInventory.Scan(root);
                var converted = new UpdateCommand { Id = Guid.NewGuid().ToString("N"), Shop = command.Shop, SourceProcessId = command.SourceProcessId };
                var job = command.Shop.Operation.Contains("uninstall") ? UninstallJobFromShop(converted, inventory) : JobFromShop(converted, inventory);
                job.OperationId = job.Id; job.SourceProcessId = command.SourceProcessId;
                job.BatchId = command.Id; job.BatchCreatedUtc = DateTime.UtcNow.ToString("O");
                if (command.LoadedAssemblyPaths.Count > 0) lock (gate) snapshot.LoadedAssemblyPaths = command.LoadedAssemblyPaths;
                try { AddJob(job); WorkerLog.Info("store " + command.Shop.Operation + " " + command.Shop.Namespace + "-" + command.Shop.Package + " → job " + job.Id.Substring(0, 8) + " root=" + job.PackageRoot); }
                catch (DeferredCommandException) { WriteAdd(job, command.SourceProcessId); }
                catch (Exception error)
                {
                    WorkerLog.Error("store " + command.Shop.Operation + " " + command.Shop.Namespace + "-" + command.Shop.Package + " rejected", error);
                    job.State = UpdateStates.Failed; job.Error = UpdateMessages.Encode(error.Message); job.Message = UpdateMessages.Encode("更新未完成，缓存与回退内容已保留。");
                    SetDirectories(job); lock (gate) { if (!snapshot.Jobs.Any(j => j.Id == job.Id)) snapshot.Jobs.Add(job); Save(); }
                }
                return;
            }
            if (command.Action == "local-import" && command.LocalImport?.Operation == "confirm")
            {
                var task = shop.Read();
                if (task == null || task.Id != command.LocalImport.PreviewId || task.PlanToken != command.LocalImport.PlanToken || task.State != "ready" || task.Request.Operation != "local-preview") throw new IOException("Local import preview expired or changed; analyze again.");
                if (task.Blockers.Count > 0) throw new IOException(string.Join("\n", task.Blockers));
                var job = LocalImportJob(shop, task, command.SourceProcessId);
                try { AddJob(job); }
                catch (DeferredCommandException) { WriteAdd(job, command.SourceProcessId); }
                shop.CompleteLocalImportHandoff(task, job.Id);
                return;
            }
            // Dropping an unconfirmed preview writes nothing to the installation, so it never waits behind updates.
            if (command.Action == "layout" && command.Layout?.Operation == "cancel") { CancelLayout(command.Layout); return; }
            if (command.Action == "local-import" && command.LocalImport?.Operation == "cancel") { shop.HandleLocalImport(command.LocalImport); return; }
            if (active != null || packs.BlocksUpdates || snapshot.Jobs.Any(j => j.RecoveryRequired || UpdateStates.Pending(j.State) && j.State != UpdateStates.PendingRestart || j.State == UpdateStates.PendingRestart && ReadPlan(j)?.Phase != "installed")) throw new DeferredCommandException("等待当前更新完成后继续商店操作。");
            try
            {
                if (command.Action == "layout") { if ((command.Layout ?? throw new IOException("Missing layout request.")).Operation == "confirm") ConvertLayout(command.Layout); else if (command.Layout.Operation == "preview") PreviewLayout(command.Layout); else throw new IOException("Unsupported layout operation."); return; }
                else if (command.Action == "local-import") shop.HandleLocalImport(command.LocalImport ?? throw new IOException("Missing local import request."));
                else shop.Handle(command.Shop ?? throw new IOException("Missing store request."));
            }
            catch (Exception error) { if (command.Action is "layout" or "local-import") shop.FailPreparation(error); throw; }
            return;
        }
        if (command.Action is "pause" or "stop" && preparation != null && command.JobId == shop.Read()?.Id)
        { preparingCancellation?.Cancel(); shop.CancelPreparation(); return; }
        if (shop.BlocksWrites && command.Action is not ("network" or "observe" or "pause" or "stop")) throw new DeferredCommandException("商店安装或恢复进行中，稍后自动继续。");
        if (command.Action == "pack") { packs.Handle(command.Pack ?? throw new IOException("缺少整合包请求。"), snapshot); return; }
        if (packs.BlocksUpdates && command.Action is not ("network" or "observe")) throw new DeferredCommandException("整合包切换或恢复进行中，稍后自动继续。");
        if (command.Action == "add")
        {
            var job = command.Job ?? throw new IOException("更新任务为空。");
            job.SourceProcessId = command.SourceProcessId;
            if (job.BatchId.Length == 0) { job.BatchId = command.Id; job.BatchCreatedUtc = DateTime.UtcNow.ToString("O"); }
            AddJob(job);
            return;
        }
        if (command.Action == "network")
        {
            var network = command.Network ?? throw new IOException("网络设置为空。");
            using var validate = new StoreClient(network);
            lock (gate) { snapshot.Network = network; snapshot.Error = ""; Save(); } return;
        }
        if (command.Action == "observe") { Observe(command); return; }
        var target = snapshot.Jobs.FirstOrDefault(j => j.Id == command.JobId) ?? throw new IOException("更新任务不存在。");
        Directory.CreateDirectory(target.WorkDirectory);
        Files.Save(Path.Combine(target.WorkDirectory, "control.json"), command);
        if (active != null && !active.IsCompleted && UpdateStates.Busy(target.State))
        {
            if (target.State != UpdateStates.Deploying && target.State != UpdateStates.RollingBack) cancellation?.Cancel();
            return;
        }
        try { using var controlWrites = InstallationWriteLease.Acquire(root); ApplySavedControl(target); }
        catch (InstallationWriteLease.BusyException) { /* Durable control is accepted and retried by Run, not rejected. */ }
    }

    /// <summary>Accepts a normal or store-originated job. Same target already queued → no-op; an older unfinished job for the same folder is superseded.</summary>
    void AddJob(UpdateJob job)
    {
        if (!Regex.IsMatch(job.Id, "^[a-fA-F0-9]{32}$") || job.LocalSource.Length == 0 && (!Regex.IsMatch(job.Namespace, "^[A-Za-z0-9_]+$") || !Regex.IsMatch(job.Package, "^[A-Za-z0-9_]+$"))) throw new IOException("任务的商店身份无效。");
        if (!job.Layout) Files.ValidateJob(root, job);
        if (!job.Layout && !job.Install && !job.Uninstall && InstallInventory.ParseVersion(job.InstalledVersion) == null) throw new IOException("当前商店包版本未知。");
        if (!job.Install && job.TargetVersion.Length != 0 && InstallInventory.CompareVersions(job.TargetVersion, job.InstalledVersion) < 0 && !job.AllowDowngrade) throw new IOException("回退必须单独确认。");
        lock (gate)
        {
            if (snapshot.Jobs.Any(j => j.Id == job.Id)) return;
            var blockers = snapshot.Jobs.Where(j => Files.Compare.Equals(j.PackageRoot, job.PackageRoot) && UpdateStates.BlocksPublication(j)).ToList();
            if (blockers.Any(b => b.TargetVersion == job.TargetVersion && b.Uninstall == job.Uninstall && (b.State == UpdateStates.Queued || UpdateStates.Busy(b.State) || b.State == UpdateStates.WaitingExit))) return;
            if (blockers.Any(b => UpdateStates.Busy(b.State) || b.RecoveryRequired)) throw new DeferredCommandException("同一模组的更新正在进行，完成后自动继续。");
            SetDirectories(job);
            job.LoadedAssemblyPaths = new List<string>();
            job.State = UpdateStates.Queued; job.Error = ""; job.Warnings = new List<string>();
            job.CreatedUtc = job.CreatedUtc.Length > 0 ? job.CreatedUtc : DateTime.UtcNow.ToString("O"); job.UpdatedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(job.WorkDirectory);
            foreach (var previous in blockers)
            {
                previous.State = UpdateStates.Superseded;
                previous.Message = UpdateMessages.Encode("已由后续本机安装接替，保留回退备份。"); previous.UpdatedUtc = job.UpdatedUtc;
            }
            // A fresh attempt for the same package takes the place of its earlier failed or stopped records.
            string key = job.Namespace + "-" + job.Package;
            foreach (var stale in snapshot.Jobs.Where(j => !j.Hidden && j.Id != job.Id && !j.RecoveryRequired && (j.State == UpdateStates.Failed || j.State == UpdateStates.Stopped)
                && (Files.Compare.Equals(j.PackageRoot, job.PackageRoot) || Files.Compare.Equals(j.Namespace + "-" + j.Package, key))).ToArray())
            { stale.State = UpdateStates.Removed; stale.Hidden = true; stale.Message = UpdateMessages.Encode("已由新的更新任务接替。"); stale.UpdatedUtc = job.UpdatedUtc; }
            snapshot.Jobs.Add(job); snapshot.Error = ""; Save();
            WorkerLog.Info("queued " + (job.DisplayName.Length > 0 ? job.DisplayName : job.Package) + " " + job.InstalledVersion + "→" + (job.TargetVersion.Length > 0 ? job.TargetVersion : "latest") + (job.Install ? " (install)" : "") + (job.AutoDependency ? " (dependency)" : "") + " root=" + job.PackageRoot);
        }
    }

    void ApplySavedControl(UpdateJob job)
    {
        string path = Path.Combine(job.WorkDirectory, "control.json");
        if (!File.Exists(path)) return;
        var command = Files.Read<UpdateCommand>(path);
        if (command.SchemaVersion is not (1 or 2) || command.MinimumWorkerProtocol < 1 || command.MinimumWorkerProtocol > WorkerProtocol.Current
            || command.SchemaVersion == 2 && command.MinimumWorkerProtocol != WorkerProtocol.Current)
            throw new IOException("Saved control requires an incompatible worker protocol; original retained.");
        var plan = ReadPlan(job);
        if ((command.Action == "stop" || command.Action == "pause") && job.RecoveryRequired)
        {
            State(job, job.State, "文件恢复尚未完成，请完成回退后再停止或移除。");
            Files.MoveAside(path, Path.Combine(job.WorkDirectory, "control-history", command.Id + ".json"));
            return;
        }
        switch (command.Action)
        {
            case "pause":
                if (!UpdateStates.History(job.State)) State(job, UpdateStates.Paused, "下载已暂停，保留已下载片段。");
                break;
            case "resume": case "retry":
                if (job.State == UpdateStates.Paused || job.State == UpdateStates.Stopped || job.State == UpdateStates.Failed)
                {
                    if (plan?.RollbackRequested == true || plan?.Phase is "committing" or "rolling-back" or "withdrawing") State(job, UpdateStates.RollbackWaiting, "等待继续完成回退。");
                    else if (plan?.Phase == "installed") State(job, UpdateStates.PendingRestart, "新版文件已安装，等待重启核对。");
                    else
                    {
                        if (command.Action == "retry" && plan != null && plan.Phase == "prepared") { InstallArchive.TryDeleteFile(Transactions.PathFor(job)); job.RecoveryRequired = false; }
                        job.Warnings = new List<string>(); deferUntil.Remove(job.Id);
                        State(job, UpdateStates.Queued, command.Action == "retry" ? "重新检查并安装。" : "沿用原目标版本继续处理。");
                    }
                }
                break;
            case "stop":
                if (!UpdateStates.History(job.State)) State(job, UpdateStates.Stopped, "本次尝试已停止，缓存与记录保留。");
                break;
            case "remove":
                if (UpdateStates.History(job.State)) { job.Hidden = true; Save(); }
                else if (plan == null || plan.Phase == "prepared") State(job, UpdateStates.Removed, "已移除队列记录，保留缓存与备份。");
                else RequestRollback(job, plan, command, true);
                break;
            case "rollback":
                if (job.State == UpdateStates.Superseded) throw new IOException("请从最新的本机安装记录发起回退，旧备份仍保留。");
                if (plan == null) throw new IOException("没有可回退的文件事务。");
                RequestRollback(job, plan, command, false); break;
            default: throw new IOException("不支持的队列操作：" + command.Action);
        }
        Files.MoveAside(path, Path.Combine(job.WorkDirectory, "control-history", command.Id + ".json"));
    }
    void RequestRollback(UpdateJob job, Transaction plan, UpdateCommand command, bool remove)
    {
        job.RestoreParameters = command.RestoreParameters; job.RollbackRequested = true; plan.RemoveAfterRollback = remove; plan.RollbackRequested = true; Transactions.Save(job, plan);
        try
        {
            State(job, UpdateStates.RollingBack, "正在撤销本次更新。"); WithPrivateWarnings(job, () => Installer.Rollback(job, plan, command, snapshot.LoadedAssemblyPaths));
            State(job, remove ? UpdateStates.Removed : UpdateStates.RolledBack, "已恢复更新前文件；个人参数按所选方式处理。");
        }
        catch (Transactions.NeedExitException) { State(job, UpdateStates.RollbackWaiting, "将在游戏退出后回退。"); }
        catch (Exception error) { State(job, UpdateStates.Failed, "回退未完成，原文件与备份保留。", error.Message); }
    }

    async Task Execute(UpdateJob job, CancellationToken token)
    {
        try
        {
            if (!job.Layout) Files.ValidateJob(root, job);
            var plan = ReadPlan(job);
            if (plan == null)
            {
                if (job.Layout)
                {
                    State(job, UpdateStates.Verifying, "正在核对要移动的目录。");
                    var preview = LayoutPlanner.Read(job.WorkDirectory) ?? throw new IOException("整理计划缺失，请重新预览。");
                    plan = new Transaction { Root = root, PackageRoot = Path.Combine(root, "plugins"), JobId = job.Id, Installer = InstallPlanner.Version3, Phase = "prepared", RequiresExit = true, StoreVerified = false, Moves = preview.Moves, Layout = preview.Journal, RecycleAfter = preview.RecycleAfter.ToList() };
                    Transactions.Save(job, plan); Save();
                }
                else if (job.Uninstall)
                {
                    State(job, UpdateStates.Verifying, "正在整理要移除的文件。");
                    plan = WithPrivateWarnings(job, () => InstallPlanner.PlanUninstall(root, job, snapshot.LoadedAssemblyPaths));
                    Transactions.Save(job, plan); Save();
                }
                else if (job.LocalSource.Length > 0)
                {
                    State(job, UpdateStates.Verifying, "正在检查本地包内容。");
                    if (!Directory.Exists(job.LocalSource)) throw new IOException("本地包内容不存在：" + job.LocalSource);
                    var local = InstallArchive.InventoryTree(job.LocalSource);
                    plan = WithPrivateWarnings(job, () => InstallPlanner.Plan(new InstallPlanInput { Root = root, Job = job, Stage = job.LocalSource, Entries = local, LoadedAssemblies = snapshot.LoadedAssemblyPaths }));
                    Transactions.Save(job, plan); Save();
                }
                else
                {
                State(job, UpdateStates.Resolving, job.Install ? "正在读取商店版本。" : "正在读取指定的商店版本。");
                using var store = new StoreClient(snapshot.Network, ShopQueue.TestHandler);
                var version = await store.Resolve(job, job.TargetVersion, token);
                token.ThrowIfCancellationRequested();
                job.TargetVersion = version.Version; job.DownloadUrl = version.DownloadUrl;
                if (!job.Install)
                {
                    if (InstallInventory.CompareVersions(job.TargetVersion, job.InstalledVersion) < 0 && !job.AllowDowngrade) throw new IOException("商店版本比本地旧，回退需要单独确认。");
                    if (job.TargetVersion == job.InstalledVersion && !job.Reinstall) { State(job, UpdateStates.Stopped, "当前已经是目标版本。"); return; }
                }
                Save();
                State(job, UpdateStates.Downloading, "正在下载新版。");
                var clock = System.Diagnostics.Stopwatch.StartNew(); long first = -1;
                job.DownloadSeconds = 0; job.DownloadTransferredBytes = 0;
                string zip = await store.Download(job.DownloadUrl, Path.Combine(job.WorkDirectory, "new.zip"), (bytes, total, text) => {
                    // The first report of an attempt is its starting offset (0, or the resumed prefix); only what follows was transferred now.
                    if (first < 0 || bytes < first) first = bytes;
                    job.DownloadSeconds = clock.Elapsed.TotalSeconds; job.DownloadTransferredBytes = Math.Max(0, bytes - first);
                    Progress(job, bytes, total, text);
                }, token);
                job.DownloadSeconds = clock.Elapsed.TotalSeconds; Save();
                WorkerLog.Debug("downloaded " + job.Package + " " + job.TargetVersion + " (" + (new FileInfo(zip).Length / 1024) + " KB)");
                token.ThrowIfCancellationRequested();
                State(job, UpdateStates.Verifying, "正在检查包内容。");
                string stage = Path.Combine(job.WorkDirectory, "new");
                InstallArchive.TryDeleteDirectory(stage);
                var entries = InstallArchive.Extract(zip, stage);
                plan = WithPrivateWarnings(job, () => InstallPlanner.Plan(new InstallPlanInput { Root = root, Job = job, Stage = stage, Entries = entries, LoadedAssemblies = snapshot.LoadedAssemblyPaths }));
                Transactions.Save(job, plan); Save();
                WorkerLog.Debug("plan " + job.Package + ": entries=" + entries.Count + " desired=" + plan.Desired.Count + " originals=" + plan.Originals.Count + " retired=" + plan.Retired.Count + " extras=" + plan.Extras.Count + " requiresExit=" + plan.RequiresExit + " deps=" + string.Join(",", plan.Dependencies));
                }
            }
            var dependency = DependencyGate(job, plan, enqueue: true);
            if (dependency.Waiting) { deferUntil[job.Id] = DateTime.UtcNow.AddSeconds(3); State(job, UpdateStates.Queued, dependency.Message); return; }
            token.ThrowIfCancellationRequested();
            if (Installer.TouchesRunningGame(plan, job, snapshot.LoadedAssemblyPaths))
            {
                job.InstallMode = "after-exit"; job.Progress = 1;
                State(job, UpdateStates.WaitingExit, plan.RequiresExit ? "新版涉及运行中的文件，退出游戏后安装。" : "目标文件正在使用，退出游戏后安装。");
                return;
            }
            using var installationWrite = Installer.IsV3(plan) ? null : await InstallationWriteLease.WaitAsync(root, token);
            token.ThrowIfCancellationRequested();
            State(job, UpdateStates.Deploying, job.Layout ? "正在移动目录。" : job.Uninstall ? "正在移除文件。" : "正在安装已验证的新版本。"); WithPrivateWarnings(job, () => Installer.Commit(job, plan, () => Installer.Wait(root, token))); job.Progress = 1;
            State(job, UpdateStates.PendingRestart, "");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { RecoverFailure(job, error); }
    }

    (bool Waiting, string Message) DependencyGate(UpdateJob job, Transaction plan, bool enqueue)
    {
        var parseNotes = new List<string>();
        var requirements = InstallDependencies.Parse(plan.Dependencies, parseNotes);
        foreach (string note in parseNotes) Warn(job, note);
        if (requirements.Count == 0) return (false, "");
        var inventory = InstallInventory.Scan(root);
        foreach (var requirement in requirements)
        {
            var installed = InstallDependencies.Satisfied(requirement, inventory);
            if (installed != null)
            {
                if (!InstallInventory.IsEnabled(root, installed)) Warn(job, "依赖已停用：" + requirement.Key + "（不影响安装）");
                WorkerLog.Debug("dependency " + requirement.Key + " >= " + requirement.Minimum + " satisfied by " + installed.Version + " at " + installed.Root);
                continue;
            }
            lock (gate)
            {
                var queued = snapshot.Jobs.LastOrDefault(j => j.Id != job.Id && Files.Compare.Equals(j.Namespace + "-" + j.Package, requirement.Key) && j.State != UpdateStates.Removed && !j.Hidden);
                if (queued != null)
                {
                    if (queued.State == UpdateStates.PendingRestart || queued.State == UpdateStates.Completed) continue;
                    if (queued.State == UpdateStates.Failed || queued.State == UpdateStates.Stopped || queued.State == UpdateStates.RolledBack || queued.State == UpdateStates.Superseded)
                    { Warn(job, "依赖 " + requirement.Key + " 未能安装（" + WorkerLanguage.StateText(queued.State) + "），继续安装本模组。"); continue; }
                    return (true, "等待依赖安装完成：" + requirement.Key);
                }
                if (!enqueue) return (true, "等待依赖：" + requirement.Key);
                if (Depth(job) >= 6 || snapshot.Jobs.Count(j => j.AutoDependency && !UpdateStates.History(j.State)) >= 32)
                { Warn(job, "依赖 " + requirement.Key + " 未自动安装（层级或数量超限），继续安装本模组。"); continue; }
                var dependency = InstallDependencies.JobFor(root, requirement, InstallInventory.Find(requirement.Key, inventory), job);
                SetDirectories(dependency); Directory.CreateDirectory(dependency.WorkDirectory);
                snapshot.Jobs.Add(dependency); Save();
                WorkerLog.Info("dependency " + requirement.Key + " >= " + requirement.Minimum + " queued for " + job.Package);
                return (true, "已自动加入依赖：" + requirement.Key + "，等待其安装完成。");
            }
        }
        return (false, "");
    }
    int Depth(UpdateJob job)
    {
        int depth = 0; string parent = job.RequestedBy;
        while (parent.Length > 0 && depth < 16) { depth++; parent = snapshot.Jobs.FirstOrDefault(j => j.Id == parent)?.RequestedBy ?? ""; }
        return depth;
    }

    void Progress(UpdateJob job, long downloaded, long total, string message)
    {
        lock (gate)
        {
            job.DownloadedBytes = downloaded; job.TotalBytes = total; job.Progress = total > 0 ? FrameworkCompat.Clamp((double)downloaded / total, 0, 1) : 0; job.Message = UpdateMessages.Encode(message);
            if (DateTime.UtcNow >= progressAt) { progressAt = DateTime.UtcNow.AddMilliseconds(1000); Save(); }
        }
    }
    Transaction? ReadPlan(UpdateJob job)
    {
        string file = Transactions.PathFor(job); var plan = File.Exists(file) ? Files.Read<Transaction>(file) : null;
        job.RecoveryRequired = plan != null && (PackageRules.HasUnfinishedWrites(plan.Phase) || plan.RollbackRequested);
        return plan;
    }
    /// <summary>The saved-resource batch executor is gone; a leftover batch file is closed so the game stops showing it, and its items are expected as jobs.</summary>
    void CloseLegacyBatch()
    {
        string path = Path.Combine(home, "resource-batch.json");
        if (!File.Exists(path)) throw new IOException("该批次已不存在；请直接在更新队列里操作。");
        try
        {
            var batch = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) as System.Text.Json.Nodes.JsonObject ?? throw new IOException("批次记录无法读取。");
            batch["Completed"] = true; File.WriteAllText(path, batch.ToJsonString(Files.Json), new System.Text.UTF8Encoding(false));
            WorkerLog.Info("legacy resource batch closed");
        }
        catch (Exception error) { WorkerLog.Warn("legacy resource batch close: " + error.Message); throw new IOException("旧批次记录关闭失败：" + error.Message); }
    }
    void RecoverFailure(UpdateJob job, Exception error)
    {
        WorkerLog.Error((job.DisplayName.Length > 0 ? job.DisplayName : job.Package) + " failed", error);
        var plan = ReadPlan(job);
        if (plan?.Phase == "committing" && !Files.GameRunning(job))
        {
            try { WithPrivateWarnings(job, () => Installer.Rollback(job, plan, null, snapshot.LoadedAssemblyPaths)); }
            catch (Exception recovery) { State(job, UpdateStates.RollbackWaiting, "安装中断，等待恢复文件。", error.Message + "；" + recovery.Message); return; }
        }
        State(job, UpdateStates.Failed, "更新未完成，缓存与回退内容已保留。", error.Message);
    }
    void Observe(UpdateCommand command)
    {
        foreach (var job in snapshot.Jobs.Where(j => j.State == UpdateStates.PendingRestart).ToList())
        {
            if (command.SourceProcessId <= 0 || command.SourceProcessId == job.SourceProcessId || !Files.ProcessAlive(command.SourceProcessId)) continue;
            var plan = ReadPlan(job); if (plan?.Phase != "installed") continue;
            var plugins = plan.Desired.Where(f => f.Plugin).ToList();
            // Once a later move, rename, disable or install has changed what this job wrote, no launch can ever
            // confirm it; close it instead of waiting forever (the game used to re-ask every 10 seconds for days).
            if (!job.Uninstall && plugins.Any(p => !File.Exists(p.Target) || Files.Hash(p.Target) != p.Hash))
            { State(job, UpdateStates.Superseded, "安装后文件已被其它操作移动、改名或替换，不再等待重启核对；回退备份仍保留。"); continue; }
            bool loaded = plugins.Count != 0 && plugins.All(p => command.LoadedAssemblyPaths.Any(path => Files.Compare.Equals(Files.PhysicalPath(path), Files.PhysicalPath(p.Target))) && File.Exists(p.Target) && Files.Hash(p.Target) == p.Hash);
            // Disabled packages are complete when a new launch observes none of their assemblies loaded. A plugin
            // installed under a disabled name (any ".dll.*") is disabled too, whatever the job's Enabled flag says.
            if (!job.Enabled || plugins.Count != 0 && plugins.All(p => PluginFileNames.OriginalPath(p.Target) != p.Target))
                loaded = !command.LoadedAssemblyPaths.Any(p => Files.Within(Files.PhysicalDirectory(job.PackageRoot), Files.PhysicalPath(p)));
            // An uninstall is complete when none of the removed assemblies is loaded any more.
            if (job.Uninstall) loaded = !plan.Originals.Any(o => o.Plugin && command.LoadedAssemblyPaths.Any(p => Files.Compare.Equals(Files.PhysicalPath(p), Files.PhysicalPath(o.Target))));
            // Packages without any BepInPlugin (libraries, core parts) cannot be observed; the file check above already passed at install.
            if (plugins.Count == 0 && plan.Desired.All(f => File.Exists(f.Target))) loaded = true;
            if (loaded) State(job, UpdateStates.Completed, "");
        }
    }

    /// <summary>The finished state of every job this run wrote files for, with the same text the window cards use.</summary>
    internal WorkerStatusSnapshot FinishedSummary()
    {
        var view = new WorkerStatusSnapshot { Root = root, UpdatedUtc = DateTime.UtcNow, Idle = true };
        lock (gate) foreach (var job in snapshot.Jobs.Where(j => WroteFiles.Contains(j.Id))) view.Jobs.Add(View(job));
        view.Failed = view.Jobs.Count(j => j.Failed); view.Done = view.Jobs.Count(j => j.Done); view.Waiting = view.Jobs.Count(j => j.Waiting);
        return view;
    }
    void PublishStatus()
    {
        if (ForegroundStatus == null) return;
        var view = new WorkerStatusSnapshot { Root = root, UpdatedUtc = DateTime.UtcNow };
        UpdateJob[] jobs; string notice;
        lock (gate) { jobs = snapshot.Jobs.Where(j => !j.Hidden && j.State != UpdateStates.Removed).ToArray(); notice = snapshot.Error; }
        foreach (var job in jobs) view.Jobs.Add(View(job));
        view.Jobs.AddRange(IndependentViews());
        view.Active = view.Jobs.Count(j => j.Busy); view.Waiting = view.Jobs.Count(j => j.Waiting); view.Failed = view.Jobs.Count(j => j.Failed); view.Done = view.Jobs.Count(j => j.Done);
        view.Idle = active == null && preparation == null && view.Active == 0;
        view.Notice = notice.Length > 0 ? WorkerLanguage.Localize(notice) : "";
        ForegroundStatus(view);
    }
    static JobView View(UpdateJob job)
    {
            bool busy = UpdateStates.Busy(job.State) || job.State == UpdateStates.Queued;
            return new JobView
            {
                Id = job.Id, Kind = job.Layout ? "layout" : job.Uninstall ? "uninstall" : job.LocalSource.Length > 0 ? "import" : job.Install ? (job.AutoDependency ? "dependency" : "install") : "update",
                Name = job.DisplayName.Length > 0 ? job.DisplayName : job.Package, FromVersion = job.Install ? "" : job.InstalledVersion, ToVersion = job.TargetVersion,
                State = job.State, StateText = job.Layout && job.State == UpdateStates.PendingRestart ? WorkerLanguage.T("Folders organized", "目录已整理", "目錄已整理") : job.Uninstall && job.State == UpdateStates.PendingRestart ? WorkerLanguage.T("Removed · takes effect next launch", "已移除 · 下次启动生效", "已移除 · 下次啟動生效") : WorkerLanguage.StateText(job.State),
                Message = WorkerLanguage.Localize(job.Message), Error = WorkerLanguage.Localize(job.Error), Details = job.Error,
                PackageRoot = job.PackageRoot, Progress = job.Progress, Downloaded = job.DownloadedBytes, Total = job.TotalBytes,
                Warnings = job.Warnings.Select(WorkerLanguage.Localize).ToList(),
                Busy = busy, Failed = job.State == UpdateStates.Failed, Waiting = job.State == UpdateStates.WaitingExit || job.State == UpdateStates.RollbackWaiting || job.State == UpdateStates.Paused,
                Done = job.State == UpdateStates.Completed || job.State == UpdateStates.PendingRestart || job.State == UpdateStates.RolledBack || job.State == UpdateStates.Superseded,
                CanRetry = job.State == UpdateStates.Failed || job.State == UpdateStates.Stopped || job.State == UpdateStates.Paused, CanRemove = !UpdateStates.Busy(job.State)
            };
    }

    internal void HandleControl(WorkerControlRequest request)
    {
        try
        {
            if (request.Action == "retry-all")
            {
                UpdateJob[] failed; lock (gate) failed = snapshot.Jobs.Where(j => j.State == UpdateStates.Failed || j.State == UpdateStates.Stopped).ToArray();
                foreach (var job in failed) WriteControl("retry", job.Id);
                foreach (var operation in IndependentViews().Where(v => v.Failed)) WriteControl("operation-retry", operation.Id);
                return;
            }
            if (request.Action.Length == 0 || request.JobId.Length == 0) return;
            WriteControl(request.Action, request.JobId);
        }
        catch (Exception error) { lock (gate) { snapshot.Error = UpdateMessages.Encode(error.Message); } }
    }
    void WriteControl(string action, string jobId) => Files.Save(Path.Combine(home, "commands", Guid.NewGuid().ToString("N") + ".json"),
        new UpdateCommand { Id = Guid.NewGuid().ToString("N"), Action = action, JobId = jobId, SchemaVersion = 2, MinimumWorkerProtocol = WorkerProtocol.Current });
}
