using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

/// <summary>Records what a manager-profile install implies for mods.yml. The worker has no YAML parser and the
/// file belongs to the manager, so it only writes the intent; the plugin applies it on its next launch, where a
/// failed write is retried instead of lost. Plain local installations record nothing.</summary>
internal static class ManagerProfileSync
{
    internal const string FileName = "manager-sync.json";
    internal const int Limit = 512;
    internal static string PathFor(string root) => ManagerIdentity.Runtime(root, FileName);

    internal static void Record(string root, UpdateJob job, Transaction plan, bool rollback)
    {
        // A manager profile lists store packages by namespace-name; local imports are not in it at all.
        if (job.LocalSource.Length > 0 || job.Namespace.Length == 0 || job.Package.Length == 0) return;
        if (Files.ManagerProfileRoot(root).Length == 0) return;
        bool removing = job.Uninstall ? !rollback : rollback && (job.Install || plan.OldVersion.Length == 0);
        string version = job.Uninstall || rollback ? plan.OldVersion : plan.NewVersion;
        if (!removing && version.Length == 0) return;
        string key = job.Namespace + "-" + job.Package;
        string path = PathFor(root);
        var pending = File.Exists(path) ? Files.Read<ManagerSyncPending>(path) : new ManagerSyncPending();
        if (pending.SchemaVersion != 1) pending = new ManagerSyncPending();
        pending.Records.RemoveAll(r => Files.Compare.Equals(r.Key, key));
        if (pending.Records.Count >= Limit) pending.Records.RemoveRange(0, pending.Records.Count - (Limit - 1));
        pending.Records.Add(new ManagerSyncRecord { Key = key, Action = removing ? ManagerSyncAction.Remove : ManagerSyncAction.Version, Version = version });
        Files.Save(path, pending);
        WorkerLog.Info("manager profile follow-up recorded: " + key + " " + (removing ? "remove" : version));
    }
}
