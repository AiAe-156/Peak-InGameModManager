#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>First-run smart grouping (2.3.0): turns the folder layout under plugins/ into a group tree, without touching
/// any folder. Every category folder (a folder that is not a mod and holds mods) becomes a group named after it, nested
/// as the folders are, at most three levels (deeper category folders fold into the third level). A mod joins the group
/// of the category folder it sits in; a mod nested in another mod's folder joins its outer mod's group; mods directly
/// in plugins/ stay ungrouped. Colours: a family per top-level group, shades of it below. Pure logic.</summary>
internal static class SmartGrouping
{
    internal sealed class Proposal
    {
        internal readonly List<OverviewGroup> Groups = new();
        /// <summary>Package folder (relative, '/' separated) → group id.</summary>
        internal readonly Dictionary<string, string> PackageGroup = new(StringComparer.OrdinalIgnoreCase);
        internal bool Any => Groups.Count > 0;
    }

    /// <summary>Whether the plugins folder has any category folder, i.e. the first-run offer is worth showing.</summary>
    internal static bool HasCategories(IEnumerable<FolderEntry> entries) => GroupFolderPlanner.Classify(entries).Containers.Count > 0;

    internal static Proposal Propose(IEnumerable<FolderEntry> entries, Func<string>? newId = null)
    {
        newId ??= () => Guid.NewGuid().ToString("N");
        var list = entries.ToList();
        var cls = GroupFolderPlanner.Classify(list);
        var proposal = new Proposal();
        var groupOfFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, OverviewGroup>(StringComparer.Ordinal);
        string Parent(string path) => GroupFolderPlanner.Parent(path);
        // Containers only count when they actually hold a mod somewhere below.
        foreach (string folder in cls.Containers.OrderBy(f => f.Count(c => c == '/')).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            string parentFolder = Parent(folder);
            string parentGroup = groupOfFolder.TryGetValue(parentFolder, out var pg) ? pg : "";
            int depth = parentGroup.Length == 0 ? 1 : Depth(byId, parentGroup) + 1;
            if (depth > GroupTree.MaxDepth) { groupOfFolder[folder] = parentGroup; continue; }
            var siblings = proposal.Groups.Where(g => g.parentId == parentGroup).ToList();
            var group = new OverviewGroup
            {
                id = newId(), name = GroupFolderPlanner.Leaf(folder), parentId = parentGroup, order = siblings.Count,
                color = parentGroup.Length == 0 ? GroupPalette.NextTopColor(proposal.Groups.Where(g => g.parentId.Length == 0).Select(g => g.color))
                    : GroupPalette.NextChildColor(byId[parentGroup].color, siblings.Select(g => g.color))
            };
            proposal.Groups.Add(group); byId[group.id] = group; groupOfFolder[folder] = group.id;
        }
        // Mods: the group of the nearest category folder above; nested mods inherit their outer mod's group.
        foreach (string package in cls.Packages.OrderBy(p => p.Count(c => c == '/')))
        {
            string outer = cls.OuterOf.TryGetValue(package, out var o) ? o : "";
            if (outer.Length > 0) { if (proposal.PackageGroup.TryGetValue(outer, out var og)) proposal.PackageGroup[package] = og; continue; }
            for (string folder = Parent(package); folder.Length > "plugins".Length; folder = Parent(folder))
                if (groupOfFolder.TryGetValue(folder, out var g)) { if (g.Length > 0) proposal.PackageGroup[package] = g; break; }
        }
        // Drop groups that ended up with neither mods nor subgroups with mods.
        // Keep a group when it or any group below it holds a mod (its ancestors are kept by the same walk).
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in proposal.PackageGroup.Values.Distinct())
            for (string cursor = id; cursor.Length > 0 && byId.TryGetValue(cursor, out var group) && keep.Add(cursor); cursor = group.parentId) { }
        proposal.Groups.RemoveAll(g => !keep.Contains(g.id));
        foreach (var parent in proposal.Groups.Select(g => g.parentId).Distinct().ToList())
        {
            var siblings = proposal.Groups.Where(g => g.parentId == parent).ToList();
            for (int i = 0; i < siblings.Count; i++) siblings[i].order = i;
        }
        return proposal;
    }

    private static int Depth(Dictionary<string, OverviewGroup> byId, string id)
    {
        int depth = 0;
        for (string cursor = id; cursor.Length > 0 && byId.TryGetValue(cursor, out var group) && depth < 10; cursor = group.parentId) depth++;
        return depth;
    }
}
