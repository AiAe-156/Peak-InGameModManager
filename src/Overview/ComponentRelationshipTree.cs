#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>Display projection only. Preserve the collector's evidence and uninstall policy.</summary>
internal static class ComponentRelationshipTree
{
    internal sealed class Row
    {
        internal string Id = "", Path = "", Parent = "", ParentId = "";
        internal int Depth;
        internal bool Cycle, Shared, Missing, Truncated, Optional, HasChildren;
    }

    private sealed class Index
    {
        internal readonly HashSet<string> Installed;
        internal readonly Dictionary<string,(string Id,bool Hard)[]> Forward,Reverse;
        internal Index(ComponentDependencyGraph graph,Func<string,string> canonical)
        {
            var links=graph.Edges.Select(e=>(From:canonical(e.From),To:canonical(e.To),e.Hard))
                .Where(e=>!e.From.Equals(e.To,StringComparison.OrdinalIgnoreCase))
                .GroupBy(e=>(e.From.ToUpperInvariant(),e.To.ToUpperInvariant()))
                .Select(g=>(g.First().From,g.First().To,Hard:g.Any(e=>e.Hard))).ToArray();
            Installed=new HashSet<string>(graph.Components.Select(c=>canonical(c.Id)),StringComparer.OrdinalIgnoreCase);
            Dictionary<string,(string Id,bool Hard)[]> Direction(bool reverse)=>links
                .GroupBy(e=>reverse?e.To:e.From,StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g=>g.Key,g=>g.Select(e=>(Id:reverse?e.From:e.To,e.Hard))
                    .OrderBy(e=>e.Id,StringComparer.OrdinalIgnoreCase).ToArray(),StringComparer.OrdinalIgnoreCase);
            Forward=Direction(false);Reverse=Direction(true);
        }
    }
    private static readonly ConditionalWeakTable<ComponentDependencyGraph,Index> Indices=new();
    internal static int IndexBuilds { get; private set; }
    internal static IReadOnlyList<Row> Build(ComponentDependencyGraph graph, string root, bool reverse,
        int maxDepth = 16, int maxEntries = 512, Func<string, string>? canonical = null)
    {
        canonical ??= id => id;
        string owner = canonical(root);
        // Graphs are immutable display generations. UI canonicalization is stable within a generation.
        var index=Indices.GetValue(graph,g=>{ IndexBuilds++;return new Index(g,canonical); });
        var installed=index.Installed;
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {owner};
        var result=new List<Row>();
        maxDepth=Math.Max(1,Math.Min(32,maxDepth));maxEntries=Math.Max(1,Math.Min(2048,maxEntries));
        var neighbours=reverse?index.Reverse:index.Forward;
        void Visit(string parent, string path, int depth, HashSet<string> ancestors)
        {
            if (!neighbours.TryGetValue(parent, out var children)) return;
            foreach (var child in children)
            {
                if (result.Count >= maxEntries) { result[result.Count - 1].Truncated = true; return; }
                bool cycle = ancestors.Contains(child.Id), shared = !seen.Add(child.Id);
                string branch = path + "/" + child.Id.Length + ":" + child.Id;
                bool hasChildren = neighbours.ContainsKey(child.Id);
                var row = new Row { Id = child.Id, Depth = depth, Path = branch, Parent = path, ParentId = parent,
                    Cycle = cycle, Shared = shared, Missing = !installed.Contains(child.Id), Optional = !child.Hard,
                    HasChildren = hasChildren && !cycle && !shared, Truncated = hasChildren && depth >= maxDepth && !cycle && !shared };
                result.Add(row);
                if (cycle || shared || depth >= maxDepth) continue;
                var next = new HashSet<string>(ancestors, StringComparer.OrdinalIgnoreCase) { child.Id };
                Visit(child.Id, branch, depth + 1, next);
            }
        }
        Visit(owner, "", 1, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { owner });
        return result;
    }
}
