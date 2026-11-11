#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModConfigEnhance.Updating;

/// <summary>Ordinary updates may follow a cached package, but never write loader/game/runtime data.</summary>
public static class UpdateWritePolicy
{
    private static readonly string[] Reserved = { "core", "config", "patchers", "plugins", ManagerIdentity.RuntimeDirectory, ManagerIdentity.LegacyRuntimeDirectory, "cache", "interop", "unhollowed", "monomod" };
    private static bool Within(string root, string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static bool PackageRootAllowed(string root, string packageRoot)
    {
        if (!Within(root, packageRoot)) return false;
        string relative = Path.GetRelativePath(root, packageRoot).Replace('\\', '/');
        string[] parts = relative.Split('/');
        if (parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.EndsWith(" ", StringComparison.Ordinal) || p.EndsWith(".", StringComparison.Ordinal) || p.Contains(':'))) return false;
        string first = parts[0];
        // plugins/ (including the plugins root itself, where some packages keep their manifest), core/ and patchers/
        // are all legitimate package roots: the installer routes each file by its archive area and protects the loader set.
        if (first.Equals("plugins", StringComparison.OrdinalIgnoreCase)) return true;
        if (first.Equals("core", StringComparison.OrdinalIgnoreCase) || first.Equals("patchers", StringComparison.OrdinalIgnoreCase)) return true;
        return !Reserved.Contains(first, StringComparer.OrdinalIgnoreCase);
    }
    public static void RequireTarget(string root, string packageRoot, string target)
    {
        if (!PackageRootAllowed(root, packageRoot)) throw new IOException("Update package root is protected: " + packageRoot);
        if (Within(packageRoot, target)) return;
        if (Within(Path.Combine(root, "patchers"), target)) return;
        throw new IOException("Ordinary update target is outside its package/patchers boundary: " + target);
    }
    public static void RequireUnownedRename(IEnumerable<string> ownedPaths, string root, string from, string to)
    {
        foreach (string path in ownedPaths)
            {
                string owned = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
                if (!Within(root, owned)) throw new IOException("Invalid shop ownership path; enablement blocked.");
                if (string.Equals(owned, Path.GetFullPath(from), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(owned, Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Store-owned enablement blocked: crash-safe ownership synchronization is unavailable. No files were renamed.");
            }
    }
    public static void RequirePayloadPath(string relative)
    {
        string first = relative.Replace('\\', '/').Split('/')[0];
        if (first.Equals("core", StringComparison.OrdinalIgnoreCase)) throw new IOException("Core libraries are read-only; install them manually: " + relative);
    }
}

// Shared, dependency-free messages used by the Mono plugin and the external worker.
public static class UpdateStates
{
    public const string Queued = "queued", Resolving = "resolving", Downloading = "downloading", Paused = "paused", Verifying = "verifying",
        Preparing = "preparing", WaitingExit = "waiting-exit", Deploying = "deploying", PendingRestart = "pending-restart", Completed = "completed",
        Stopped = "stopped", Failed = "failed", Removed = "removed", RollingBack = "rolling-back", RollbackWaiting = "rollback-waiting", RolledBack = "rolled-back", Superseded = "superseded";
    public static bool History(string state) => state == PendingRestart || state == Completed || state == RolledBack || state == Superseded;
    /// <summary>What belongs in the history view: an installed-awaiting-restart job stays in the queue until a restart confirms it.</summary>
    public static bool Recorded(string state) => state == Completed || state == RolledBack || state == Superseded;
    public static bool Busy(string state) => state == Resolving || state == Downloading || state == Verifying || state == Preparing || state == Deploying || state == RollingBack;
    public static bool Pending(string state) => state != Completed && state != RolledBack && state != Removed && state != Stopped && state != Failed && state != Paused && state != Superseded;
    public static bool BlocksPublication(string state) => Pending(state) || state == PendingRestart || state == Paused;
    public static bool BlocksPublication(UpdateJob job) => job.RecoveryRequired || BlocksPublication(job.State);
}

public static class PackageRules
{
    public static bool IsLoader(string identity) => identity.Equals("BepInEx-BepInExPack", StringComparison.OrdinalIgnoreCase)
        || identity.Equals("BepInEx-BepInExPack_PEAK", StringComparison.OrdinalIgnoreCase);
    public static bool HasUnfinishedWrites(string phase) => phase == "placing" || phase == "side-by-side"
        || phase == "committing" || phase == "rolling-back" || phase == "withdrawing";
}

public sealed class UpdateNetworkOptions
{
    public string Mode { get; set; } = "system";
    public string ProxyAddress { get; set; } = "";
}

public sealed class UpdateJob
{
    public string OperationId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Package { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PackageRoot { get; set; } = "";
    public string InstalledVersion { get; set; } = "";
    public string TargetVersion { get; set; } = "";
    public string RunningVersion { get; set; } = "";
    public string Homepage { get; set; } = "";
    public string State { get; set; } = UpdateStates.Queued;
    public string Message { get; set; } = "";
    public string Error { get; set; } = "";
    public string CreatedUtc { get; set; } = "";
    public string UpdatedUtc { get; set; } = "";
    public string WorkDirectory { get; set; } = "";
    public string BackupDirectory { get; set; } = "";
    public string InstallMode { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string ETag { get; set; } = "";
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
    public double Progress { get; set; }
    /// <summary>Wall-clock download time and the bytes it moved (excludes a reused cache or resumed prefix), for the row's speed/time note.</summary>
    public double DownloadSeconds { get; set; }
    public long DownloadTransferredBytes { get; set; }
    public int SourceProcessId { get; set; }
    public bool Enabled { get; set; } = true;
    public bool AllowDowngrade { get; set; }
    public bool Reinstall { get; set; }
    public bool RestoreParameters { get; set; }
    public bool RollbackRequested { get; set; }
    public bool Hidden { get; set; }
    public bool StoreOperation { get; set; }
    public bool RecoveryRequired { get; set; }
    public List<string> PluginGuids { get; set; } = new();
    public List<string> AssemblyPaths { get; set; } = new();
    public List<string> LoadedAssemblyPaths { get; set; } = new();
    public List<string> ConfigurationFiles { get; set; } = new();
    public bool Install { get; set; }
    public bool Uninstall { get; set; }
    public bool Layout { get; set; }
    public string LayoutPreviewId { get; set; } = "";
    public string LocalSource { get; set; } = "";
    public bool AutoDependency { get; set; }
    public string RequestedBy { get; set; } = "";
    /// <summary>Submission batch this job came in with (update-batch / resource-batch / single add), for grouping the record view.</summary>
    public string BatchId { get; set; } = "";
    public string BatchCreatedUtc { get; set; } = "";
    public List<string> Warnings { get; set; } = new();
}

public sealed class UpdateCommand
{
    public int MinimumWorkerProtocol { get; set; } = 1;
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Action { get; set; } = "";
    public string JobId { get; set; } = "";
    public UpdateJob? Job { get; set; }
    public List<UpdateCommand>? ResourceCommands { get; set; }
    public string ResourcePackId { get; set; } = "";
    public string ResourceBatchId { get; set; } = "";
    public PackRequest? Pack { get; set; }
    public ShopRequest? Shop { get; set; }
    public LocalImportRequest? LocalImport { get; set; }
    public LayoutQueueRequest? Layout { get; set; }
    public UpdateNetworkOptions? Network { get; set; }
    public bool RestoreParameters { get; set; }
    public int SourceProcessId { get; set; }
    public List<string> LoadedAssemblyPaths { get; set; } = new();
}

public sealed class UpdateSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public string WorkerVersion { get; set; } = "1.0.0";
    public int WorkerProtocol { get; set; } = 1;
    public string WorkerBuild { get; set; } = "";
    public string ResourceCapability { get; set; } = "";
    public string Error { get; set; } = "";
    public int WorkerProcessId { get; set; }
    public UpdateNetworkOptions Network { get; set; } = new();
    public List<string> LoadedAssemblyPaths { get; set; } = new();
    public List<UpdateJob> Jobs { get; set; } = new();
}

/// <summary>Cross-process ownership of installation writes. Uses the worker's actual exclusive handle,
/// not the existence of its lock file. Kept in this shared source for isolated worker consumers.</summary>
public sealed class InstallationWriteLease : IDisposable
{
    readonly FileStream handle;
    readonly string journal;
    readonly string installationRoot, gameBoundary;
    bool disposed;
    public sealed class Rename
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }
    public sealed class Rewrite
    {
        public string Path { get; set; } = "";
        public byte[] Before { get; set; } = Array.Empty<byte>();
        public byte[] After { get; set; } = Array.Empty<byte>();
    }
    public sealed class BusyException : IOException
    { public BusyException(IOException inner) : base("Installation write lock is busy; retry later.", inner) { } }
    public sealed class WorkerAlreadyRunningException : IOException
    { public WorkerAlreadyRunningException(IOException inner) : base("The update worker is already running.", inner) { } }
    public static async System.Threading.Tasks.Task<InstallationWriteLease> WaitAsync(string root, System.Threading.CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return Acquire(root); }
            catch (BusyException) { await System.Threading.Tasks.Task.Delay(100, token).ConfigureAwait(false); }
        }
    }
    public static InstallationWriteLease Acquire(string root) => new(root, true);
    public static InstallationWriteLease AcquireWithoutRecovery(string root) => new(root, false);
    public static InstallationWriteLease AcquireWorkerInstance(string root) => new(root, false, "worker-instance.lock");
    InstallationWriteLease(string root, bool recover, string lockName = "worker.lock")
    {
        if (!Path.IsPathFullyQualified(root)) throw new IOException("Installation root must be an absolute path.");
        installationRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        gameBoundary = Path.GetDirectoryName(installationRoot) ?? throw new IOException("Installation root has no selected game boundary.");
        string home = ManagerIdentity.Runtime(installationRoot);
        RejectLinks(home); Directory.CreateDirectory(home);
        string lockPath = Path.Combine(home, lockName); RejectLinks(lockPath);
        try { handle = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
        {
            if (lockName == "worker-instance.lock") throw new WorkerAlreadyRunningException(error);
            throw new BusyException(error);
        }
        journal = Path.Combine(home, "enablement-recovery.bin");
        try { if (recover) Recover(); } catch { handle.Dispose(); throw; }
    }
    void RejectLinks(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("Installation write path must be absolute: " + path);
        string full = Path.GetFullPath(path);
        if (!full.Equals(installationRoot, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(installationRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Installation write path is outside the selected installation: " + path);
        // The selected game can live below a Steam library alias. Validate the game
        // boundary itself and every child, without walking unrelated ancestors.
        for (string? part = full; part != null; part = Path.GetDirectoryName(part))
        {
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Installation write path contains a link: " + part);
            if (part.Equals(gameBoundary, StringComparison.OrdinalIgnoreCase)) break;
        }
    }
    static string Hash(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var input = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }
    void Atomic(string path, byte[] bytes)
    {
        RejectLinks(path);
        string temp = path + ".mce-" + Guid.NewGuid().ToString("N") + ".tmp";
        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
    }
    public void Prepare(IEnumerable<Rename> renames, IEnumerable<Rewrite> rewrites)
    {
        if (disposed || File.Exists(journal)) throw new IOException("Enablement recovery is still pending.");
        var moves = renames.ToList(); var writes = rewrites.ToList();
        if (moves.Count == 0 && writes.Count == 0) return;
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true))
        {
            writer.Write("MCE enablement 1"); writer.Write(moves.Count);
            foreach (var move in moves)
            {
                RejectLinks(move.From); RejectLinks(move.To);
                if (File.Exists(move.To) || Directory.Exists(move.To)) throw new IOException("Rename destination exists: " + move.To);
                writer.Write(Path.GetFullPath(move.From)); writer.Write(Path.GetFullPath(move.To)); writer.Write(Hash(move.From));
            }
            writer.Write(writes.Count);
            foreach (var write in writes)
            {
                RejectLinks(write.Path);
                if (!File.ReadAllBytes(write.Path).SequenceEqual(write.Before)) throw new IOException("Metadata changed: " + write.Path);
                writer.Write(Path.GetFullPath(write.Path));
                writer.Write(write.Before.Length); writer.Write(write.Before);
                writer.Write(write.After.Length); writer.Write(write.After);
            }
        }
        Atomic(journal, memory.ToArray());
    }
    (System.Collections.Generic.List<(string From, string To, string Hash)> Moves, System.Collections.Generic.List<Rewrite> Writes) ReadJournal()
    {
        RejectLinks(journal);
        using var reader = new BinaryReader(File.OpenRead(journal));
        if (reader.ReadString() != "MCE enablement 1") throw new IOException("Unsupported enablement recovery journal.");
        int Count() { int count = reader.ReadInt32(); if (count < 0 || count > 100000) throw new IOException("Invalid recovery count."); return count; }
        byte[] Bytes() { int length = reader.ReadInt32(); if (length < 0 || length > 128 * 1024 * 1024) throw new IOException("Invalid recovery data."); var bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException(); return bytes; }
        var moves = new List<(string From, string To, string Hash)>();
        int count = Count(); for (int i = 0; i < count; i++) moves.Add((reader.ReadString(), reader.ReadString(), reader.ReadString()));
        var writes = new List<Rewrite>(); count = Count();
        for (int i = 0; i < count; i++) writes.Add(new Rewrite { Path = reader.ReadString(), Before = Bytes(), After = Bytes() });
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new IOException("Unexpected recovery data.");
        foreach (var move in moves) { RejectLinks(move.From); RejectLinks(move.To); }
        foreach (var write in writes) RejectLinks(write.Path);
        return (moves, writes);
    }
    public void Apply()
    {
        if (!File.Exists(journal)) return;
        var state = ReadJournal();
        foreach (var move in state.Moves)
        {
            RejectLinks(move.From); RejectLinks(move.To);
            if (Hash(move.From) != move.Hash) throw new IOException("Enablement source changed: " + move.From);
            File.Move(move.From, move.To);
        }
        foreach (var write in state.Writes)
        {
            RejectLinks(write.Path);
            if (!File.ReadAllBytes(write.Path).SequenceEqual(write.Before)) throw new IOException("Metadata changed: " + write.Path);
            Atomic(write.Path, write.After);
        }
    }
    public void Recover()
    {
        if (!File.Exists(journal)) return;
        var state = ReadJournal();
        // Preflight every entry before restoring anything; never overwrite external edits.
        foreach (var move in state.Moves)
        {
            RejectLinks(move.From); RejectLinks(move.To);
            bool from = File.Exists(move.From), to = File.Exists(move.To);
            if (from == to || Hash(from ? move.From : move.To) != move.Hash)
                throw new IOException("Enablement recovery needs manual review: " + move.From);
        }
        foreach (var write in state.Writes)
        {
            RejectLinks(write.Path); var bytes = File.ReadAllBytes(write.Path);
            if (!bytes.SequenceEqual(write.Before) && !bytes.SequenceEqual(write.After)) throw new IOException("Recovery metadata changed: " + write.Path);
        }
        foreach (var write in state.Writes.AsEnumerable().Reverse())
            if (!File.ReadAllBytes(write.Path).SequenceEqual(write.Before)) Atomic(write.Path, write.Before);
        foreach (var move in state.Moves.AsEnumerable().Reverse()) if (File.Exists(move.To)) File.Move(move.To, move.From);
        File.Delete(journal);
    }
    public void Commit() { if (disposed) throw new ObjectDisposedException(nameof(InstallationWriteLease)); RejectLinks(journal); File.Delete(journal); }
    // Dispose releases ownership only. A crash/abandoned Apply is recovered by the next owner.
    public void Dispose() { if (!disposed) { disposed = true; handle.Dispose(); } }
}
