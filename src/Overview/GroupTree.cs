#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Nested groups (2.3.0). Groups form a forest through <see cref="OverviewGroup.parentId"/>, at most
/// <see cref="MaxDepth"/> levels deep; <see cref="OverviewGroup.order"/> orders siblings only. A parent that no longer
/// exists, a cycle, or a chain deeper than the limit is read as a top-level group, so stale data never hides a group.
/// Pure logic: every view that lists groups (overview, sidebar, hotkeys, selectors, logs, review) asks this class for
/// order, depth, path and counts instead of sorting the flat list by <c>order</c>.</summary>
internal sealed class GroupTree
{
    internal const int MaxDepth = 3;
    private static readonly StringComparer Ids = StringComparer.Ordinal;

    private readonly Dictionary<string, OverviewGroup> _byId = new(Ids);
    private readonly Dictionary<string, string> _parent = new(Ids);
    private readonly Dictionary<string, List<OverviewGroup>> _children = new(Ids);
    private readonly List<OverviewGroup> _roots = new();
    private readonly List<OverviewGroup> _flat = new();
    private readonly Dictionary<string, int> _position = new(Ids), _depth = new(Ids);

    internal IReadOnlyList<OverviewGroup> Roots => _roots;
    /// <summary>Every group, depth first: a group, then its children in sibling order.</summary>
    internal IReadOnlyList<OverviewGroup> Flat => _flat;

    internal GroupTree(IEnumerable<OverviewGroup> groups)
    {
        foreach (var group in groups) if (group != null && !string.IsNullOrEmpty(group.id) && !_byId.ContainsKey(group.id)) _byId.Add(group.id, group);
        foreach (var group in _byId.Values) _parent[group.id] = EffectiveParent(group);
        foreach (var group in _byId.Values)
        {
            string parent = _parent[group.id];
            if (parent.Length == 0) _roots.Add(group);
            else (_children.TryGetValue(parent, out var list) ? list : _children[parent] = new List<OverviewGroup>()).Add(group);
        }
        Sort(_roots);
        foreach (var list in _children.Values) Sort(list);
        foreach (var root in _roots) Walk(root, 1);
    }

    private static void Sort(List<OverviewGroup> list) => list.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : Ids.Compare(a.id, b.id));

    private void Walk(OverviewGroup group, int depth)
    {
        _position[group.id] = _flat.Count; _depth[group.id] = depth; _flat.Add(group);
        if (_children.TryGetValue(group.id, out var list)) foreach (var child in list) Walk(child, depth + 1);
    }

    /// <summary>The parent this group really hangs under: missing parents, cycles and over-deep chains fall back to the top level.</summary>
    private string EffectiveParent(OverviewGroup group)
    {
        string parent = group.parentId ?? "";
        if (parent.Length == 0 || parent == group.id || !_byId.ContainsKey(parent)) return "";
        var seen = new HashSet<string>(Ids) { group.id };
        int depth = 1; string cursor = parent;
        while (cursor.Length > 0)
        {
            if (!seen.Add(cursor) || !_byId.TryGetValue(cursor, out var next)) return "";
            if (++depth > MaxDepth) return "";
            cursor = next.parentId ?? "";
            if (cursor.Length > 0 && !_byId.ContainsKey(cursor)) cursor = "";
        }
        return parent;
    }

    internal bool Contains(string id) => _byId.ContainsKey(id);
    internal OverviewGroup? Get(string id) => _byId.TryGetValue(id ?? "", out var group) ? group : null;
    internal string Parent(string id) => _parent.TryGetValue(id ?? "", out var parent) ? parent : "";
    internal IReadOnlyList<OverviewGroup> Children(string id) => _children.TryGetValue(id ?? "", out var list) ? list : Array.Empty<OverviewGroup>();
    /// <summary>1 for a top-level group; 0 for an unknown id (ungrouped).</summary>
    internal int Depth(string id) => _depth.TryGetValue(id ?? "", out int depth) ? depth : 0;
    /// <summary>Position in <see cref="Flat"/>; unknown ids sort after every group.</summary>
    internal int Position(string id) => _position.TryGetValue(id ?? "", out int position) ? position : int.MaxValue;

    /// <summary>Root first, ending with the group itself.</summary>
    internal List<OverviewGroup> Chain(string id)
    {
        var chain = new List<OverviewGroup>();
        for (string cursor = id ?? ""; cursor.Length > 0 && _byId.TryGetValue(cursor, out var group); cursor = Parent(cursor)) chain.Insert(0, group);
        return chain;
    }

    /// <summary>Ancestors only, nearest last.</summary>
    internal List<OverviewGroup> Ancestors(string id) { var chain = Chain(id); if (chain.Count > 0) chain.RemoveAt(chain.Count - 1); return chain; }

    internal string Path(string id, string separator = " › ") => string.Join(separator, Chain(id).Select(g => g.name));

    /// <summary>The group and every group below it.</summary>
    internal HashSet<string> Subtree(string id)
    {
        var result = new HashSet<string>(Ids);
        if (!_byId.ContainsKey(id ?? "")) return result;
        var pending = new Stack<string>(); pending.Push(id!);
        while (pending.Count > 0) { string next = pending.Pop(); if (!result.Add(next)) continue; foreach (var child in Children(next)) pending.Push(child.id); }
        return result;
    }

    /// <summary>How many levels the subtree under (and including) this group spans.</summary>
    internal int Height(string id)
    {
        int best = 0;
        foreach (string member in Subtree(id)) best = Math.Max(best, Depth(member) - Depth(id) + 1);
        return best;
    }

    /// <summary>Whether <paramref name="child"/> may hang under <paramref name="parent"/> ("" = top level): no cycle and
    /// the whole moved subtree stays within <see cref="MaxDepth"/>. The reason is a short key for the caller to word.</summary>
    internal bool CanAttach(string child, string parent, out string reason)
    {
        reason = "";
        if (!_byId.ContainsKey(child ?? "")) { reason = "missing"; return false; }
        if (string.IsNullOrEmpty(parent)) return true;
        if (!_byId.ContainsKey(parent)) { reason = "missing"; return false; }
        if (parent == child || Subtree(child!).Contains(parent)) { reason = "cycle"; return false; }
        if (Depth(parent) + Height(child!) > MaxDepth) { reason = "depth"; return false; }
        return true;
    }

    /// <summary>A collapsed group hides its own members and every group below it; a row is hidden when its group or any ancestor is collapsed.</summary>
    internal bool HiddenByCollapse(string groupId, ICollection<string> collapsed)
    {
        foreach (var group in Chain(groupId)) if (collapsed.Contains(group.id)) return true;
        return false;
    }

    /// <summary>A header is hidden when an ancestor (not itself) is collapsed.</summary>
    internal bool HeaderHidden(string groupId, ICollection<string> collapsed)
    {
        foreach (var group in Ancestors(groupId)) if (collapsed.Contains(group.id)) return true;
        return false;
    }

    /// <summary>Members of the group's whole subtree, given each member's group id.</summary>
    internal int SubtreeCount<T>(string id, IEnumerable<T> members, Func<T, string> groupOf)
    {
        var subtree = Subtree(id);
        return members.Count(m => subtree.Contains(groupOf(m) ?? ""));
    }

    /// <summary>Sort key for a member row: its group's tree position, members of a group before that group's children,
    /// ungrouped last. Rows inside one group keep their own order as the secondary key.</summary>
    internal int RowKey(string groupId) => Position(groupId);

    // ---- edits ----

    /// <summary>Deleting a group moves its direct members and its child groups up one level: children take the deleted
    /// group's parent and its place among siblings; members move to that parent (ungrouped when it was top level).</summary>
    internal static void Remove(List<OverviewGroup> groups, IEnumerable<OverviewModRule> rules, string id, ICollection<string>? collapsed = null)
    {
        var tree = new GroupTree(groups);
        var target = tree.Get(id); if (target == null) return;
        string parent = tree.Parent(id);
        var children = tree.Children(id).ToList();
        // New sibling order: everything before the deleted group, its children in their order, everything after.
        var siblings = (parent.Length == 0 ? tree.Roots : tree.Children(parent)).ToList();
        int at = siblings.IndexOf(target);
        var order = siblings.Take(at).Concat(children).Concat(siblings.Skip(at + 1)).ToList();
        groups.RemoveAll(g => g.id == id);
        foreach (var child in children) child.parentId = parent;
        foreach (var rule in rules) if (rule.groupId == id) rule.groupId = parent;
        collapsed?.Remove(id);
        for (int i = 0; i < order.Count; i++) order[i].order = i;
    }

    /// <summary>Attach or detach; the moved group goes to the end of its new siblings. Returns false (unchanged) when not allowed.</summary>
    internal static bool Attach(List<OverviewGroup> groups, string id, string parent, out string reason)
    {
        var tree = new GroupTree(groups);
        if (!tree.CanAttach(id, parent, out reason)) return false;
        var group = tree.Get(id)!;
        string old = tree.Parent(id);
        group.parentId = parent ?? "";
        var siblings = groups.Where(g => g != group && new GroupTree(groups).Parent(g.id) == group.parentId).ToList();
        group.order = siblings.Count == 0 ? 0 : siblings.Max(g => g.order) + 1;
        Renumber(groups, old);
        return true;
    }

    /// <summary>Re-number one sibling list 0..n in its current order.</summary>
    internal static void Renumber(List<OverviewGroup> groups, string parent)
    {
        var tree = new GroupTree(groups);
        var siblings = string.IsNullOrEmpty(parent) ? tree.Roots.ToList() : tree.Children(parent).ToList();
        for (int i = 0; i < siblings.Count; i++) siblings[i].order = i;
    }

    /// <summary>Swap a group with its neighbour among siblings.</summary>
    internal static bool MoveAmongSiblings(List<OverviewGroup> groups, string id, int direction)
    {
        var tree = new GroupTree(groups);
        string parent = tree.Parent(id);
        var siblings = (parent.Length == 0 ? tree.Roots : tree.Children(parent)).ToList();
        int index = siblings.FindIndex(g => g.id == id), target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= siblings.Count) return false;
        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        for (int i = 0; i < siblings.Count; i++) siblings[i].order = i;
        return true;
    }

    /// <summary>Next order for a new group under this parent.</summary>
    internal static int NextOrder(List<OverviewGroup> groups, string parent)
    {
        var tree = new GroupTree(groups);
        var siblings = string.IsNullOrEmpty(parent) ? tree.Roots : tree.Children(parent);
        return siblings.Count == 0 ? 0 : siblings.Max(g => g.order) + 1;
    }

    /// <summary>Whether any group hangs under another; exports with nesting are written with schema version 2.</summary>
    internal static bool HasNesting(IEnumerable<OverviewGroup>? groups) => groups != null && groups.Any(g => !string.IsNullOrEmpty(g?.parentId));

    /// <summary>The schema version a state with these groups must be written with: 2 when any group is nested, else 1
    /// so older versions keep reading flat data. Older versions accept only 1, so they refuse nested data on import.</summary>
    internal static int SchemaFor(IEnumerable<OverviewGroup>? groups) => HasNesting(groups) ? 2 : 1;
    internal static bool SupportedSchema(int version) => version == 1 || version == 2;
}
