#nullable enable
using System;
using System.IO;
using System.Linq;
using ModConfigEnhance.Updating;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

internal static class ShopGroupingPersistence
{
    internal static void Bind(string home, string taskId, OverviewRepository repo)
    {
        Directory.CreateDirectory(home);
        ProfilePairPersistence.Atomic(Path.Combine(home, "group-target-" + SafeId(taskId) + ".json"),
            new JObject { ["pack"] = PackLibrary.Active?.Id ?? "", ["root"] = Path.GetFullPath(repo.Profiles.RootDirectory), ["profile"] = repo.ViewedProfileId }.ToString());
    }
    private static string SafeId(string id)
    {
        if (id.Length == 0 || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..")) throw new InvalidDataException("Invalid shop task id.");
        return id;
    }
    internal static bool Apply(string home, ShopTask task, OverviewRepository repo)
    {
        if (task.State != "completed" || task.Request.Operation != "confirm-install" ||
            task.Request.GroupId.Length == 0 && task.Request.NewGroupName.Length == 0 ||
            (PackLibrary.Active?.Id ?? "") != task.ActivePackId) return false;
        string marker = Path.Combine(home, "group-applied-" + SafeId(task.Id) + ".txt");
        if (File.Exists(marker)) return false;
        string target = Path.Combine(home, "group-target-" + SafeId(task.Id) + ".json");
        // Pre-fix tasks had no profile binding: the installation's applied profile is the only stable target.
        string profile = repo.Profiles.AppliedId;
        if (File.Exists(target))
        {
            var binding = JObject.Parse(File.ReadAllText(target));
            if (binding.Value<string>("pack") != task.ActivePackId || !string.Equals(binding.Value<string>("root"), Path.GetFullPath(repo.Profiles.RootDirectory), StringComparison.OrdinalIgnoreCase)) return false;
            profile = binding.Value<string>("profile") ?? throw new InvalidDataException("Missing grouping profile target.");
        }
        string group = task.Request.NewGroupName.Length > 0 ? "shop-" + task.Id : task.Request.GroupId;
        var package = task.Packages.FirstOrDefault(p => p.Key == task.Request.Namespace + "-" + task.Request.Package)
            ?? throw new InvalidDataException("The completed installation has no root package.");
        repo.PersistInstallationGrouping(profile, state =>
        {
            if (task.Request.NewGroupName.Length > 0 && !state.groups.Any(g => g.id == group))
                state.groups.Add(new OverviewGroup { id = group, name = task.Request.NewGroupName, color = task.Request.NewGroupColor, order = state.groups.Count });
            if (!state.groups.Any(g => g.id == group)) throw new IOException("The selected installation group no longer exists.");
            foreach (string guid in package.PluginGuids)
            {
                var rule = state.mods.FirstOrDefault(m => m.id == guid);
                if (rule == null) { rule = new OverviewModRule { id = guid }; state.mods.Add(rule); }
                rule.groupId = group;
            }
        });
        Directory.CreateDirectory(home);
        ProfilePairPersistence.Atomic(marker, profile + "\n" + group);
        return true;
    }
}
