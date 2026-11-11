#nullable enable
using System.Collections.Generic;

namespace ModConfigEnhance.Updating
{
    public static class ManagerSyncAction
    {
        public const string Version = "version", Remove = "remove";
    }

    /// <summary>One mods.yml follow-up an install implies: the package version to record, or the entry to drop
    /// after a removal. The worker only records the intent; the plugin owns the YAML write.</summary>
    public sealed class ManagerSyncRecord
    {
        public string Key { get; set; } = "";
        public string Action { get; set; } = ManagerSyncAction.Version;
        public string Version { get; set; } = "";
    }

    /// <summary>Pending follow-ups, newest last. One entry per package key; a later record replaces an earlier
    /// one so an update then a removal collapses to the removal.</summary>
    public sealed class ManagerSyncPending
    {
        public int SchemaVersion { get; set; } = 1;
        public List<ManagerSyncRecord> Records { get; set; } = new();
    }
}
