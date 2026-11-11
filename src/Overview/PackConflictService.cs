#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

internal static class PackConflictService
{
    private static readonly Dictionary<string, (long Ticks, long Length, string Version, PackConflictFile? File, string Error)> Cache = new(StringComparer.OrdinalIgnoreCase);
    internal static IReadOnlyList<PackConflictMatch> Evaluate(OverviewRepository repo, IReadOnlyList<OverviewMod> mods)
    {
        return EvaluateInstalled(PublishedPack.Packs, mods, repo.PlannedEnabled, guid => Chainloader.PluginInfos.Values.Any(info =>
            info.Metadata.GUID.Equals(guid, StringComparison.OrdinalIgnoreCase) && info.Instance != null), repo.State.ignoredPackConflicts);
    }

    // Injectable state readers keep all three meanings distinct and make the actual evaluation testable without Unity.
    internal static IReadOnlyList<PackConflictMatch> EvaluateInstalled(IEnumerable<PublishedPack> packs, IReadOnlyList<OverviewMod> mods,
        Func<OverviewMod, bool> planned, Func<string, bool> loaded, IEnumerable<string>? ignored = null)
    {
        var results = new List<PackConflictMatch>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var ignoredSet = new HashSet<string>(ignored ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        foreach (var pack in packs.Where(p => p.Valid && !string.IsNullOrWhiteSpace(p.Id)))
        {
            var file = InstalledFile(pack);
            if (file == null) continue;
            string name = pack.Release.Value<string>("displayName") ?? pack.Release.Value<string>("name") ?? pack.Id;
            foreach (var rule in file.rules)
            {
                var members = rule.members.Select(member =>
                {
                    var candidates = mods.Where(m => m.Guid.Equals(member.guid, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (candidates.Count == 0) candidates = mods.Where(m => m.PluginGuids.Contains(member.guid, StringComparer.OrdinalIgnoreCase)).ToList();
                    var mod = candidates.OrderByDescending(m => m.Loaded).ThenByDescending(m => m.TargetEnabled).FirstOrDefault();
                    return new PackConflictMemberState { Member = member, Mod = mod, Unit = mod == null ? "guid:" + member.guid : ToggleUnit(mod),
                        Planned = candidates.Any(planned), Loaded = loaded(member.guid), NextLaunch = candidates.Any(m => m.TargetEnabled) };
                }).ToList();
                var match = BuildMatch(pack.Id, name, rule, members);
                match.Ignored = ignoredSet.Contains(match.Identity);
                if (match.Any && identities.Add(match.Identity)) results.Add(match);
            }
        }
        return results;
    }

    private static PackConflictFile? InstalledFile(PublishedPack pack)
    {
        string path = Path.Combine(pack.Root, PackConflictStore.FileName);
        if (!File.Exists(path)) { Cache.Remove(path); return null; }
        try
        {
            var info = new FileInfo(path);
            if (Cache.TryGetValue(path, out var prior) && prior.Ticks == info.LastWriteTimeUtc.Ticks && prior.Length == info.Length && prior.Version == pack.Version) return prior.File;
            var file = PackConflictStore.ReadPublished(pack);
            Cache[path] = (info.LastWriteTimeUtc.Ticks, info.Length, pack.Version, file, "");
            return file;
        }
        catch (Exception e)
        {
            string stamp = e.Message;
            if (!Cache.TryGetValue(path, out var prior) || prior.Error != stamp)
                Plugin.Log.LogWarning("[整合包冲突预设] " + pack.Id + "：" + e.Message);
            Cache[path] = (0, 0, pack.Version, null, stamp);
            return null;
        }
    }

    internal static PackConflictMatch BuildMatch(string packId, string packName, PackConflictRule rule, List<PackConflictMemberState> members)
    {
        return new PackConflictMatch { PackId = packId, PackName = packName, Rule = rule, Members = members, Identity = Identity(packId, rule),
            Planned = Triggered(rule, members, m => m.Planned), Loaded = Triggered(rule, members, m => m.Loaded), NextLaunch = Triggered(rule, members, m => m.NextLaunch) };
    }

    internal static bool Triggered(PackConflictRule rule, IReadOnlyList<PackConflictMemberState> members, Func<PackConflictMemberState, bool> enabled)
    {
        if (members.Count < 2 || members.Select(m => m.Unit).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2) return false;
        if (rule.mode == PackConflictRule.All) return members.All(enabled);
        return rule.mode == PackConflictRule.AnyTwo && members.Where(enabled).Select(m => m.Unit).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() == 2;
    }

    internal static string ToggleUnit(OverviewMod mod)
    {
        if (!string.IsNullOrWhiteSpace(mod.PackageRoot)) return "package:" + mod.PackageRoot.Replace('\\', '/').TrimEnd('/');
        if (mod.AssemblyPaths.Count > 0) return "assembly:" + mod.AssemblyPaths[0].Replace('\\', '/');
        return "guid:" + mod.Guid;
    }

    internal static string Identity(string packId, PackConflictRule rule)
    {
        string reason = (rule.reason ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        string content = JsonConvert.SerializeObject(new { pack = packId.Trim().ToLowerInvariant(), rule = rule.id.Trim().ToLowerInvariant(),
            members = rule.members.Select(m => m.guid.Trim().ToLowerInvariant()).Distinct().OrderBy(g => g, StringComparer.Ordinal).ToArray(), mode = rule.mode, reason });
        return PackConflictStore.Digest(content);
    }

    internal static void Ignore(OverviewRepository repo, PackConflictMatch match)
    {
        if (!repo.State.ignoredPackConflicts.Contains(match.Identity, StringComparer.Ordinal)) repo.State.ignoredPackConflicts.Add(match.Identity);
        repo.SaveUiState();
        match.Ignored = true;
    }
}
