using System.Globalization;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

/// <summary>Bounded disk use: finished work leaves the live queue after a week, stage trees go right after install, orphans after two weeks.</summary>
internal static class InstallStorage
{
    internal const int HistoryCap = 200;
    internal static readonly TimeSpan HistoryAge = TimeSpan.FromDays(7), RetentionAge = TimeSpan.FromDays(14), TemporaryAge = TimeSpan.FromDays(1);

    internal static int Prune(string home, UpdateSnapshot snapshot)
    {
        var now = DateTime.UtcNow; int moved = 0;
        string historyPath = Path.Combine(home, "history.json");
        List<UpdateJob> history;
        try { history = File.Exists(historyPath) ? Files.Read<List<UpdateJob>>(historyPath) : new List<UpdateJob>(); }
        catch (Exception) { history = new List<UpdateJob>(); }
        foreach (var job in snapshot.Jobs.ToArray())
        {
            bool finished = job.State == UpdateStates.Completed || job.State == UpdateStates.RolledBack || job.State == UpdateStates.Superseded || job.State == UpdateStates.Removed;
            if (!finished) continue;
            if (!DateTime.TryParse(job.UpdatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updated) || now - updated < HistoryAge) continue;
            snapshot.Jobs.Remove(job); history.Add(Slim(job)); moved++;
        }
        if (moved > 0)
        {
            while (history.Count > HistoryCap) history.RemoveAt(0);
            try { Files.Save(historyPath, history); } catch (IOException) { }
        }
        return moved;
    }

    // The throttle has to outlive the worker process: while any task is pending the game spawns a fresh worker
    // for every observe, so an in-memory timestamp made each of those launches sweep the whole storage again.
    // A stamp file is used instead — not *.json, so the maintenance record cleanup leaves it alone.
    private const string SweepStamp = "storage-sweep.stamp";
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);
    private static string SweepStampPath(string home) => Path.Combine(home, "maintenance", SweepStamp);
    internal static bool SweepDue(string home)
    {
        try { string path = SweepStampPath(home); return !File.Exists(path) || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > SweepInterval; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }
    internal static void MarkSwept(string home)
    {
        try
        {
            string path = SweepStampPath(home);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static void Sweep(string home, UpdateSnapshot snapshot)
    {
        var now = DateTime.UtcNow;
        var live = new HashSet<string>(snapshot.Jobs.Select(j => j.Id), StringComparer.OrdinalIgnoreCase);
        foreach (string area in new[] { "jobs", "backups" })
        {
            string directory = Path.Combine(home, area);
            if (!Directory.Exists(directory)) continue;
            foreach (string child in Directory.GetDirectories(directory))
            {
                if (live.Contains(Path.GetFileName(child))) continue;
                if (now - Directory.GetLastWriteTimeUtc(child) < RetentionAge) continue;
                InstallArchive.TryDeleteDirectory(child);
            }
        }
        foreach (var job in snapshot.Jobs)
        {
            bool settled = job.State == UpdateStates.PendingRestart || job.State == UpdateStates.Completed || job.State == UpdateStates.RolledBack || job.State == UpdateStates.Superseded || job.State == UpdateStates.Removed;
            if (!settled || !Directory.Exists(job.WorkDirectory)) continue;
            foreach (string child in Directory.GetDirectories(job.WorkDirectory))
                if (Path.GetFileName(child) is "new" or "old" or "consumer-validation" || Path.GetFileName(child).StartsWith("new.interrupted-", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(child).StartsWith("old.interrupted-", StringComparison.OrdinalIgnoreCase)) InstallArchive.TryDeleteDirectory(child);
            foreach (string file in Directory.GetFiles(job.WorkDirectory))
                if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".ready.json", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".download.json", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".unverified", StringComparison.OrdinalIgnoreCase)) InstallArchive.TryDeleteFile(file);
        }
        foreach (string temporary in Directory.GetFiles(home, "*.tmp"))
            if (now - File.GetLastWriteTimeUtc(temporary) > TemporaryAge) InstallArchive.TryDeleteFile(temporary);
        // Consumed command records only matter for de-duplicating recent submissions; the worker parses them all at start.
        foreach (string area in new[] { "processed", "command-receipts", "command-rejections", "update-requests-processed" })
        {
            string directory = Path.Combine(home, area);
            if (!Directory.Exists(directory)) continue;
            foreach (string file in Directory.GetFiles(directory))
                if (now - File.GetLastWriteTimeUtc(file) > HistoryAge) InstallArchive.TryDeleteFile(file);
        }
        // The game caches one tools copy per worker release; only the copy this process runs from is needed.
        string tools = Path.Combine(home, "tools");
        if (Directory.Exists(tools))
        {
            string self; try { self = FrameworkCompat.ProcessPath; } catch (Exception) { self = ""; }
            foreach (string child in Directory.GetDirectories(tools))
            {
                if (self.Length > 0 && Files.Within(child, self)) continue;
                if (now - Directory.GetLastWriteTimeUtc(child) < TemporaryAge) continue;
                InstallArchive.TryDeleteDirectory(child);
            }
        }
    }

    static UpdateJob Slim(UpdateJob job)
    {
        job.LoadedAssemblyPaths = new List<string>(); job.AssemblyPaths = new List<string>();
        return job;
    }
}
