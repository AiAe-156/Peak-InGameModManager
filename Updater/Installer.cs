using System.Text.Json;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

/// <summary>Executes plans: move what we replace or remove into the job backup, copy the new files, verify, record. Older journals roll back through the same logic.</summary>
internal static class Installer
{
    internal static bool IsV3(Transaction plan) => plan.Installer == InstallPlanner.Version3;
    internal static Action<string>? StepProbe = null;

    internal static void Save(UpdateJob job, Transaction plan) => Transactions.Save(job, plan);

    internal static bool TouchesRunningGame(Transaction plan, UpdateJob job, IReadOnlyCollection<string> loadedAssemblies)
    {
        if (!Files.GameRunning(job)) return false;
        if (plan.RequiresExit) return true;
        var loaded = new HashSet<string>(loadedAssemblies.Select(Physical), Files.Compare);
        return plan.Originals.Concat(plan.Retired).Concat(plan.Desired).Any(f => loaded.Contains(Physical(f.Target)));
    }

    internal static void Commit(UpdateJob job, Transaction plan, Func<IDisposable>? sharedLease = null)
    {
        if (plan.Layout != null) { CommitLayout(job, plan, sharedLease); return; }
        if (!IsV3(plan)) throw new IOException("该任务由旧版助手准备，请点击重试重新规划。");
        UpdateWriteMarker.Write(plan.Root, job.Id, Display(job));
        try { CommitCore(job, plan, sharedLease); }
        finally { UpdateWriteMarker.Clear(plan.Root); }
    }

    static string Display(UpdateJob job) => job.DisplayName.Length > 0 ? job.DisplayName : job.Package;

    static void CommitCore(UpdateJob job, Transaction plan, Func<IDisposable>? sharedLease)
    {
        if (plan.Phase == "installed") { Verify(plan); return; }
        if (plan.Phase is "rolling-back" or "rolled-back") throw new IOException("该事务已回退，请重新开始更新。");
        foreach (var desired in plan.Desired)
            if (!desired.Written && (!File.Exists(desired.Source) || Files.Hash(desired.Source) != desired.Hash)) throw new IOException("准备文件已变化，请重试：" + desired.Source);
        plan.Phase = "committing"; Save(job, plan);
        string backups = job.BackupDirectory;
        foreach (var parameter in plan.Parameters)
        {
            if (parameter.Backup.Length != 0 || !File.Exists(parameter.Target)) continue;
            parameter.Backup = BackupPath(backups, "parameters", parameter.Target);
            Directory.CreateDirectory(Path.GetDirectoryName(parameter.Backup)!); File.Copy(parameter.Target, parameter.Backup, false);
            parameter.Hash = Files.Hash(parameter.Backup); Save(job, plan);
        }
        foreach (var original in plan.Originals.Concat(plan.Retired))
        {
            if (original.Written) continue;
            if (File.Exists(original.Target))
            {
                if (original.Backup.Length == 0) original.Backup = BackupPath(backups, plan.Retired.Contains(original) ? "retired" : "original", original.Target);
                Files.MoveAside(original.Target, original.Backup);
                StepProbe?.Invoke("backup:" + original.Target);
            }
            original.Written = true; Save(job, plan);
        }
        foreach (var desired in plan.Desired)
        {
            if (desired.Written && Matches(desired.Target, desired.Hash)) continue;
            if (File.Exists(desired.Target) && !Matches(desired.Target, desired.Hash)) Files.MoveAside(desired.Target, BackupPath(backups, "conflict", desired.Target));
            if (!File.Exists(desired.Target)) Files.CopyAtomic(desired.Source, desired.Target);
            desired.Written = true; Save(job, plan);
            StepProbe?.Invoke("placed:" + desired.Target);
        }
        if (job.Uninstall) RemoveEmptyDirectories(plan.PackageRoot);
        Verify(plan);
        plan.Phase = "installed"; Save(job, plan);
        // Only the shared records (shop ledger, modpack index, catalog cache) need the cross-process lock; file copies of different packages run side by side.
        using (sharedLease?.Invoke()) Bookkeeping(job, plan, rollback: false);
        CleanStage(job, plan);
        WorkerLog.Info((job.Uninstall ? "uninstalled " : "installed ") + (job.DisplayName.Length > 0 ? job.DisplayName : job.Package) + " " + (job.Uninstall ? plan.OldVersion : plan.NewVersion) + ": " + plan.Desired.Count + " files, " + plan.Originals.Count + " moved, " + plan.Retired.Count + " retired, " + plan.Extras.Count + " kept");
    }

    /// <summary>Synchronous wait for the cross-process write lease, for use inside commit paths that already run on a worker task.</summary>
    internal static InstallationWriteLease Wait(string root, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return InstallationWriteLease.Acquire(root); }
            catch (InstallationWriteLease.BusyException) { Thread.Sleep(100); }
        }
    }

    /// <summary>Folder organization: same-volume directory renames, journaled one by one so a crash resumes or rolls back.</summary>
    internal static void CommitLayout(UpdateJob job, Transaction plan, Func<IDisposable>? sharedLease)
    {
        if (plan.Phase == "installed") { VerifyLayout(plan); return; }
        UpdateWriteMarker.Write(plan.Root, job.Id, Display(job));
        try { CommitLayoutCore(job, plan, sharedLease); }
        finally { UpdateWriteMarker.Clear(plan.Root); }
    }

    static void CommitLayoutCore(UpdateJob job, Transaction plan, Func<IDisposable>? sharedLease)
    {
        if (plan.Phase == "installed") { VerifyLayout(plan); return; }
        // Nothing has moved yet: every file must still be exactly where the preview saw it.
        foreach (var unit in plan.Layout!.Units)
            foreach (var file in unit.Files)
            {
                string oldPath = Files.Child(plan.Root, LayoutPaths.OldPath(unit, file));
                if (!File.Exists(oldPath)) throw new IOException("预览后文件已不存在，请重新预览：" + oldPath);
                if (Files.Hash(oldPath) != file.Hash) throw new IOException("目录内容在预览后发生变化，请重新预览：" + oldPath);
            }
        plan.Phase = "moving"; plan.CreatedDirectories ??= []; plan.ReplacedEmptyDirectories ??= []; Save(job, plan);
        foreach (var move in plan.Moves)
        {
            if (move.Done) continue;
            string from = Files.Child(plan.Root, move.From), to = Files.Child(plan.Root, move.To);
            Files.RejectLinks(plan.Root, from);
            if (!Directory.Exists(from)) throw new IOException("源目录已不存在：" + move.From);
            if (File.Exists(to) || Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any()) throw new IOException("目标位置已被占用：" + move.To);
            // Record what this move creates or replaces before touching the disk, so a rollback (even after a crash)
            // removes only folders the organizer made and restores an empty folder it reused.
            bool replaced = Directory.Exists(to);
            if (replaced && !plan.ReplacedEmptyDirectories.Contains(move.To, Files.Compare)) plan.ReplacedEmptyDirectories.Add(move.To);
            for (string parent = Parent(move.To); parent.Contains('/'); parent = Parent(parent))
                if (!Directory.Exists(Files.Child(plan.Root, parent)) && !plan.CreatedDirectories.Contains(parent, Files.Compare)) plan.CreatedDirectories.Add(parent);
            Save(job, plan);
            if (replaced) Directory.Delete(to);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            Directory.Move(from, to);
            move.Done = true; Save(job, plan);
            WorkerLog.Info("moved " + move.From + " → " + move.To);
        }
        VerifyLayout(plan);
        plan.Phase = "installed"; Save(job, plan);
        LayoutBookkeeping(job, plan, rollback: false, sharedLease);
        CleanStage(job, plan);
        WorkerLog.Info("folder organization: " + plan.Moves.Count + " directories moved");
        // 2.3.1: the empty folders the user ticked go to the Recycle Bin only now, once every move succeeded (some of
        // them only became empty through those moves). Each is checked again; a folder that is no longer empty or
        // safe stays and is reported, it never fails the organization that already happened.
        if (plan.RecycleAfter.Count > 0)
        {
            var skipped = RecycleBin.SendAll(plan.Root, plan.RecycleAfter);
            foreach (string line in skipped) job.Warnings.Add("空文件夹未放进回收站：" + line);
            plan.RecycleAfter.Clear(); Save(job, plan);
        }
    }

    internal static void RollbackLayout(UpdateJob job, Transaction plan, Func<IDisposable>? sharedLease)
    {
        if (plan.Phase == "rolled-back") return;
        UpdateWriteMarker.Write(plan.Root, job.Id, Display(job));
        try { RollbackLayoutCore(job, plan, sharedLease); }
        finally { UpdateWriteMarker.Clear(plan.Root); }
    }

    static void RollbackLayoutCore(UpdateJob job, Transaction plan, Func<IDisposable>? sharedLease)
    {
        if (plan.Phase == "rolled-back") return;
        plan.Phase = "rolling-back"; Save(job, plan);
        foreach (var move in plan.Moves.AsEnumerable().Reverse())
        {
            if (!move.Done) continue;
            string from = Files.Child(plan.Root, move.From), to = Files.Child(plan.Root, move.To);
            if (!Directory.Exists(to)) { move.Done = false; continue; }
            if (File.Exists(from) || Directory.Exists(from) && Directory.EnumerateFileSystemEntries(from).Any()) throw new IOException("原位置已被占用，未回退：" + move.From);
            if (Directory.Exists(from)) Directory.Delete(from);
            Files.RejectLinks(plan.Root, to);
            Directory.CreateDirectory(Path.GetDirectoryName(from)!);
            Directory.Move(to, from);
            move.Done = false; Save(job, plan);
            WorkerLog.Info("moved back " + move.To + " → " + move.From);
        }
        LayoutBookkeeping(job, plan, rollback: true, sharedLease);
        // Folders this commit created and the rollback emptied go too, deepest first. A folder that was there before
        // (an empty group folder the user made) stays even when empty; an empty folder a move reused comes back.
        foreach (string relative in (plan.CreatedDirectories ?? []).OrderByDescending(d => d.Count(c => c == '/')))
            try { string empty = Files.Child(plan.Root, relative); if (Directory.Exists(empty) && !Directory.EnumerateFileSystemEntries(empty).Any()) Directory.Delete(empty); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (string relative in plan.ReplacedEmptyDirectories ?? [])
            try { Directory.CreateDirectory(Files.Child(plan.Root, relative)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        plan.RollbackRequested = false; job.RollbackRequested = false; plan.Phase = "rolled-back"; Save(job, plan);
        WorkerLog.Info("folder organization rolled back");
    }

    static string Parent(string relative) { int at = relative.LastIndexOf('/'); return at < 0 ? "" : relative.Substring(0, at); }

    /// <summary>Later moves may run inside folders an earlier move produced (a renamed category folder, then a package
    /// leaving it), so a move's own destination can legitimately be gone again. What must hold is the end state: every
    /// moved package is at its final place, and the last move into each destination left it there.</summary>
    static void VerifyLayout(Transaction plan)
    {
        if (plan.Layout != null && plan.Layout.Units.Count > 0)
        {
            foreach (var unit in plan.Layout.Units)
                if (!Directory.Exists(Files.Child(plan.Root, unit.NewRoot))) throw new IOException("目标目录不存在：" + unit.NewRoot);
            return;
        }
        foreach (var move in plan.Moves)
        {
            if (!Directory.Exists(Files.Child(plan.Root, move.To))) throw new IOException("目标目录不存在：" + move.To);
            if (Directory.Exists(Files.Child(plan.Root, move.From))) throw new IOException("源目录仍然存在：" + move.From);
        }
    }

    /// <summary>The modpack file table only knows paths, so the moves are expressed as old→new for it; ledger entries are only updated when one exists.</summary>
    static void LayoutBookkeeping(UpdateJob job, Transaction plan, bool rollback, Func<IDisposable>? sharedLease)
    {
        var journal = plan.Layout!;
        plan.Originals = journal.Units.SelectMany(u => u.Files.Select(f => new FileChange { Target = Files.Child(plan.Root, LayoutPaths.OldPath(u, f)), Hash = f.Hash })).ToList();
        plan.Desired = journal.Units.SelectMany(u => u.Files.Select(f => new FileChange { Target = Files.Child(plan.Root, f.Path), Hash = f.Hash })).ToList();
        using (sharedLease?.Invoke())
        {
            try { PackTransactions.RecordUpdate(plan.Root, plan, rollback); }
            catch (Exception e) { job.Warnings.Add("整合包文件表登记未完成（不影响整理）：" + e.Message); WorkerLog.Warn("pack index bookkeeping failed (layout): " + e.Message); }
            try
            {
                var storage = new PackStorage(plan.Root); string active = storage.Read().ActivePackId;
                string path = active.Length > 0 ? Path.Combine(storage.Workspace(active), "shop-installed.json") : Path.Combine(plan.Root, "MCE", "shop", "installed-unmanaged.json");
                if (File.Exists(path))
                {
                    var ledger = Files.Read<ShopLedger>(path); bool changed = false;
                    foreach (var unit in journal.Units.Where(u => u.Key.Length > 0))
                    {
                        var package = ledger.Packages.FirstOrDefault(p => Files.Compare.Equals(p.Key, unit.Key));
                        if (package == null) continue;
                        package.ClaimRoot = rollback ? unit.OldRoot : unit.NewRoot;
                        package.LayoutGroupId = rollback ? package.LayoutGroupId : unit.GroupId;
                        package.LayoutGroupFolder = rollback ? package.LayoutGroupFolder : unit.GroupFolder;
                        package.LayoutOuterFolder = rollback ? package.LayoutOuterFolder : unit.OuterFolder;
                        package.Files = unit.Files.Select(f => new PackFile { Path = rollback ? LayoutPaths.OldPath(unit, f) : f.Path, Hash = f.Hash }).ToList();
                        changed = true;
                    }
                    if (changed) { ledger.Revision++; Files.Save(path, ledger); }
                }
            }
            catch (Exception e) { job.Warnings.Add("商店账本登记未完成（目录已移动）：" + e.Message); WorkerLog.Warn("ledger bookkeeping failed (layout): " + e.Message); }
        }
    }

    internal static void Verify(Transaction plan)
    {
        foreach (var desired in plan.Desired)
            if (!Matches(desired.Target, desired.Hash)) throw new IOException("安装文件与清单不一致：" + desired.Target);
        foreach (var retired in plan.Retired)
            if (File.Exists(retired.Target)) throw new IOException("旧副本仍留在加载位置：" + retired.Target);
    }

    /// <summary>Every recorded backup must still exist before a rollback moves anything; applies to journals of any age.</summary>
    internal static void ValidateBackups(Transaction plan)
    {
        foreach (var item in plan.Originals.Concat(plan.Retired).Concat(plan.Parameters))
            if (item.Backup.Length > 0 && !File.Exists(item.Backup)) throw new IOException("回退备份不完整：" + item.Target);
    }

    internal static void Rollback(UpdateJob job, Transaction plan, UpdateCommand? command, IReadOnlyCollection<string> loadedAssemblies, Func<IDisposable>? sharedLease = null)
    {
        if (plan.Layout != null) { RollbackLayout(job, plan, sharedLease); return; }
        if (plan.Phase == "rolled-back") return;
        UpdateWriteMarker.Write(plan.Root, job.Id, Display(job));
        try { RollbackCore(job, plan, command, loadedAssemblies, sharedLease); }
        finally { UpdateWriteMarker.Clear(plan.Root); }
    }

    static void RollbackCore(UpdateJob job, Transaction plan, UpdateCommand? command, IReadOnlyCollection<string> loadedAssemblies, Func<IDisposable>? sharedLease)
    {
        ValidateBackups(plan);
        bool running = Files.GameRunning(job);
        if (running)
        {
            var loaded = new HashSet<string>((command?.LoadedAssemblyPaths ?? Enumerable.Empty<string>()).Concat(loadedAssemblies).Select(Physical), Files.Compare);
            bool touches = plan.RequiresExit || plan.Desired.Concat(plan.Originals).Concat(plan.Retired).Concat(plan.Candidates).Any(f => loaded.Contains(Physical(f.Target)));
            if (touches || job.RestoreParameters) throw new Transactions.NeedExitException();
        }
        plan.Phase = "rolling-back"; Save(job, plan);
        string backups = job.BackupDirectory;
        // Side-by-side candidates only exist in journals written by earlier releases.
        foreach (var candidate in plan.Candidates)
        {
            if (!File.Exists(candidate.Target)) continue;
            Files.MoveAside(candidate.Target, BackupPath(backups, "withdrawn", candidate.Target)); candidate.Written = false; Save(job, plan);
        }
        foreach (var desired in plan.Desired)
        {
            if (!File.Exists(desired.Target)) continue;
            var original = plan.Originals.FirstOrDefault(f => Files.Compare.Equals(f.Target, desired.Target));
            if (original != null && Matches(desired.Target, original.Hash)) continue;
            Files.MoveAside(desired.Target, BackupPath(backups, "withdrawn", desired.Target));
            desired.Written = false; Save(job, plan);
        }
        foreach (var original in plan.Originals.Concat(plan.Retired))
        {
            if (original.Backup.Length == 0 || !File.Exists(original.Backup)) continue;
            if (File.Exists(original.Target))
            {
                if (Matches(original.Target, original.Hash)) continue;
                Files.MoveAside(original.Target, BackupPath(backups, "conflict", original.Target));
            }
            Files.CopyAtomic(original.Backup, original.Target);
            original.Written = false; Save(job, plan);
        }
        if (job.RestoreParameters)
            foreach (var parameter in plan.Parameters.Where(p => p.Backup.Length != 0 && File.Exists(p.Backup)))
            {
                if (File.Exists(parameter.Target)) Files.MoveAside(parameter.Target, BackupPath(backups, "current-parameters", parameter.Target));
                Files.CopyAtomic(parameter.Backup, parameter.Target);
            }
        using (sharedLease?.Invoke()) Bookkeeping(job, plan, rollback: true);
        plan.RollbackRequested = false; job.RollbackRequested = false; plan.Phase = "rolled-back"; Save(job, plan);
        WorkerLog.Info("rolled back " + (job.DisplayName.Length > 0 ? job.DisplayName : job.Package) + " to " + plan.OldVersion);    }

    /// <summary>Shop ledger and modpack index stay informed, but never decide whether an install happened.</summary>
    static void Bookkeeping(UpdateJob job, Transaction plan, bool rollback)
    {
        try { RecordLedger(job, plan, rollback); }
        catch (Exception e) { job.Warnings.Add("商店账本登记未完成（不影响安装）：" + e.Message); WorkerLog.Warn("ledger bookkeeping failed: " + e.Message); }
        try { if (IsV3(plan)) ManagerProfileSync.Record(plan.Root, job, plan, rollback); }
        catch (Exception e) { job.Warnings.Add("管理器档案同步未登记（不影响安装）：" + e.Message); WorkerLog.Warn("manager profile follow-up failed: " + e.Message); }
        try { PackTransactions.RecordUpdate(plan.Root, plan, rollback); }
        catch (Exception e) { job.Warnings.Add("整合包文件表登记未完成（不影响安装）：" + e.Message); WorkerLog.Warn("pack index bookkeeping failed: " + e.Message); }
        if (!rollback && !job.Uninstall && job.LocalSource.Length == 0)
        {
            try { Packages.SyncCatalogue(plan.Root, job, plan); }
            catch (Exception e) { job.Warnings.Add("商店目录缓存未刷新：" + e.Message); WorkerLog.Warn("store catalogue refresh failed: " + e.Message); }
        }
    }

    static void RecordLedger(UpdateJob job, Transaction plan, bool rollback)
    {
        if (!IsV3(plan))
        {
            // Journals from earlier releases carry both ledger snapshots; restoring the earlier one is all a rollback needs.
            if (rollback && plan.Ledger != null && plan.Ledger.Path.Length > 0) Files.Save(plan.Ledger.Path, plan.Ledger.Before);
            return;
        }
        var storage = new PackStorage(plan.Root); string active = storage.Read().ActivePackId;
        string path = active.Length > 0 ? Path.Combine(storage.Workspace(active), "shop-installed.json") : Path.Combine(plan.Root, "MCE", "shop", "installed-unmanaged.json");
        Files.RejectLinks(plan.Root, path);
        var ledger = File.Exists(path) ? Files.Read<ShopLedger>(path) : new ShopLedger();
        if (ledger.SchemaVersion != 1) throw new IOException("账本架构版本不受支持。");
        string key = job.LocalSource.Length > 0 ? "local:" + job.Package : job.Namespace + "-" + job.Package;
        string Relative(string target) => Path.GetRelativePath(plan.Root, target).Replace('\\', '/');
        var package = ledger.Packages.FirstOrDefault(p => Files.Compare.Equals(p.Key, key));
        bool removing = job.Uninstall ? !rollback : rollback && (job.Install || plan.OldVersion.Length == 0);
        if (removing) { if (package != null) ledger.Packages.Remove(package); }
        else
        {
            if (package == null) { package = new ShopPackage { Key = key, InstalledAsDependency = job.AutoDependency }; ledger.Packages.Add(package); }
            bool previous = job.Uninstall || rollback;
            package.Version = previous ? plan.OldVersion : plan.NewVersion; package.Dependencies = plan.Dependencies.ToList();
            package.Files = (previous ? plan.Originals : plan.Desired).Select(f => new PackFile { Path = Relative(f.Target), Hash = f.Hash }).ToList();
            package.External = false; package.VerificationRequired = false;
        }
        ledger.Revision++;
        Files.Save(path, ledger);
    }

    static void RemoveEmptyDirectories(string packageRoot)
    {
        if (!Directory.Exists(packageRoot)) return;
        foreach (string directory in Directory.GetDirectories(packageRoot, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            try { if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { if (!Directory.EnumerateFileSystemEntries(packageRoot).Any()) Directory.Delete(packageRoot); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static void CleanStage(UpdateJob job, Transaction plan)
    {
        if (plan.NewTree.Length > 0 && Files.Within(job.WorkDirectory, plan.NewTree)) InstallArchive.TryDeleteDirectory(plan.NewTree);
        InstallArchive.TryDeleteFile(plan.NewTree + ".ready.json");
        foreach (string file in Directory.Exists(job.WorkDirectory) ? Directory.GetFiles(job.WorkDirectory) : Array.Empty<string>())
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".download.json", StringComparison.OrdinalIgnoreCase)) InstallArchive.TryDeleteFile(file);
        InstallArchive.TryDeleteDirectory(Path.Combine(job.WorkDirectory, "consumer-validation"));
    }

    static bool Matches(string path, string hash) => File.Exists(path) && Files.Hash(path) == hash;
    static string BackupPath(string backups, string section, string target) => Path.Combine(backups, section, Guid.NewGuid().ToString("N") + "_" + Path.GetFileName(target));
    static string Physical(string path) { try { return Files.PhysicalPath(path); } catch { return Path.GetFullPath(path); } }
}
