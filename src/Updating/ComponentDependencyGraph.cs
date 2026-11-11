#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Updating;

/// <summary>Immutable-by-convention evidence snapshot. IDs are package keys or file paths, never invented plugin GUIDs.</summary>
public sealed class ComponentDependencyGraph
{
    public const string MceGuid = ManagerIdentity.Guid;
    /// <summary>What MCE itself runs on: the ModConfig host it hard-depends on, plus the two PEAKLib assemblies it
    /// references. Deliberately a short, fixed list — the graph walk used to decide this, and it gave MCE a hard
    /// edge to every assembly it could not resolve ("possible provider"), which put unrelated mods under
    /// protection and made them impossible to uninstall.</summary>
    public static readonly string[] MceRequiredIdentities =
    {
        "com.github.PEAKModding.PEAKLib.ModConfig",
        "com.github.PEAKModding.PEAKLib.Core",
        "com.github.PEAKModding.PEAKLib.UI"
    };
    /// <summary>2.3.9 (user decision): the protected whitelist — MCE plus <see cref="MceRequiredIdentities"/>. These
    /// cannot be enabled/disabled or uninstalled from MCE; everything else can.</summary>
    public static bool IsProtected(string guid) => !string.IsNullOrEmpty(guid)
        && (ManagerIdentity.IsManagerGuid(guid) || MceRequiredIdentities.Contains(guid, Compare));
    internal static IReadOnlyList<string> MceRequiredBy(Component component)
    {
        var required = new List<string>();
        foreach (string identity in MceRequiredIdentities)
            if (component.PluginGuids.Contains(identity, Compare) || component.Assemblies.Contains(identity, Compare)) required.Add(identity);
        return required;
    }
    public sealed class Component
    {
        public string Id = "", PackageKey = "";
        public readonly List<string> Locations = new List<string>();
        public readonly List<string> PluginGuids = new List<string>();
        public readonly List<string> Assemblies = new List<string>();
    }
    public sealed class Edge
    {
        public string From = "", To = "", Evidence = "";
        public bool Hard = true;
    }
    public sealed class Capability
    {
        public bool CanUninstall;
        // This policy does not veto updates; final dependency/version validation still applies.
        public bool CanUpdate = true;
        public bool Unknown;
        public bool VerificationRequired;
        public string Reason = "";
        public IReadOnlyList<string> ProtectionPath = Array.Empty<string>();
    }
    public sealed class TreeEntry
    {
        public string Id = "", Evidence = "";
        public int Depth;
        public bool Cycle, Shared, Missing, Truncated;
    }
    public readonly List<Component> Components = new List<Component>();
    public readonly List<string[]> SharedFileOwners = new List<string[]>();
    public readonly List<Edge> Edges = new List<Edge>();
    public readonly List<string> Failures = new List<string>();
    public sealed class FailureScope
    {
        public string Node = "", Path = "", Reason = "";
    }
    public readonly List<FailureScope> FailureScopes = new List<FailureScope>();
    public void AddFailure(string reason, string node = "", string path = "")
    {
        Failures.Add(reason);
        FailureScopes.Add(new FailureScope { Node = node, Path = path, Reason = reason });
    }
    static readonly StringComparer Compare = StringComparer.OrdinalIgnoreCase;

    public Capability GetCapability(string id)
    {
        var target = Components.FirstOrDefault(c => Compare.Equals(c.Id, id) || (c.PackageKey.Length > 0 && Compare.Equals(c.PackageKey, id)));
        var paths = new Dictionary<string, List<string>>(Compare);
        var pending = new Queue<string>();
        foreach (var seed in Components.Where(c => c.PluginGuids.Any(ManagerIdentity.IsManagerGuid)))
            if (!paths.ContainsKey(seed.Id)) { paths[seed.Id] = new List<string> { seed.Id }; pending.Enqueue(seed.Id); }
        bool noSeed = pending.Count == 0;
        var unresolved = new List<string>();
        bool unknown = Failures.Count > FailureScopes.Count || FailureScopes.Any(f => f.Node.Length == 0) || pending.Count == 0;
        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            if (FailureScopes.Any(f => Compare.Equals(f.Node, current))) unknown = true;
            foreach (var edge in Edges.Where(e => e.Hard && Compare.Equals(e.From, current)))
            {
                if (!Components.Any(c => Compare.Equals(c.Id, edge.To))) { unknown = true; unresolved.Add(current + " → " + edge.To + " (" + edge.Evidence + ")"); continue; }
                if (paths.ContainsKey(edge.To)) continue;
                paths[edge.To] = paths[current].Concat(new[] { edge.To }).ToList(); pending.Enqueue(edge.To);
            }
        }
        // Co-owner veto only: do not traverse a different package's private requirements.
        bool sharedChanged;
        do
        {
            sharedChanged = false;
            foreach (var owners in SharedFileOwners)
            {
                var requiredOwner = owners.FirstOrDefault(o => paths.ContainsKey(o));
                if (requiredOwner == null) continue;
                foreach (var owner in owners)
                    if (!paths.ContainsKey(owner)) { paths[owner] = paths[requiredOwner].Concat(new[] { owner }).ToList(); sharedChanged = true; }
            }
        } while (sharedChanged);
        // Protection is the fixed list above, never the walk's result: the walk still feeds the unknown/evidence
        // diagnostics below, but it must not decide what may be uninstalled.
        if (target != null)
        {
            var required = MceRequiredBy(target);
            if (required.Count > 0)
                return new Capability { Reason = "MCE required component: " + string.Join(" → ", required), ProtectionPath = required };
        }
        if (target == null || noSeed)
            return new Capability { Unknown = true, Reason = "MCE dependency protection is unknown; uninstall refused. " + (noSeed ? "MCE GUID seed missing or unreadable. " : "") + string.Join("; ", Failures.Concat(unresolved).Take(6)) };
        // A file node or a failed target manifest/assembly is not a verified package.
        // Missing knowledge elsewhere must not manufacture identity for this target.
        bool scopedUnknown = FailureScopes.Any(f => Compare.Equals(f.Node, target.Id) || Compare.Equals(f.Node, target.PackageKey));
        if (target.PackageKey.Length == 0 || !Compare.Equals(target.Id, target.PackageKey) || target.Locations.Count == 0 || scopedUnknown)
            return new Capability { Unknown = true, Reason = "Target component identity or metadata is unverified; uninstall refused. "
                + string.Join("; ", FailureScopes.Where(f => Compare.Equals(f.Node, target.Id)).Select(f => f.Reason).Take(4)) };
        bool targetClosureUnknown = false;
        var targetVisited = new HashSet<string>(Compare); var targetPending = new Queue<string>(); targetPending.Enqueue(target.Id);
        while (targetPending.Count > 0)
        {
            string current = targetPending.Dequeue(); if (!targetVisited.Add(current)) continue;
            if (FailureScopes.Any(f => Compare.Equals(f.Node, current))) targetClosureUnknown = true;
            foreach (var edge in Edges.Where(e => e.Hard && Compare.Equals(e.From, current)))
                if (Components.Any(c => Compare.Equals(c.Id, edge.To))) targetPending.Enqueue(edge.To); else targetClosureUnknown = true;
        }
        bool globalUnknown = Failures.Count > FailureScopes.Count || FailureScopes.Any(f => f.Node.Length == 0);
        bool ordinary = target.Locations.Count > 0 && target.Locations.All(location => location.Replace('\\', '/').StartsWith("plugins/", StringComparison.OrdinalIgnoreCase));
        // An identified ordinary package can reach the existing verification preview
        // despite unrelated evidence gaps. This is advisory, not final file ownership:
        // the worker still re-collects and performs its normal checks at each boundary.
        bool verification = unknown || globalUnknown || targetClosureUnknown || FailureScopes.Count > 0;
        return new Capability { CanUninstall = true, Unknown = verification, VerificationRequired = verification,
            Reason = verification ? "Verification-only uninstall preview is available; file ownership and reverse dependencies must pass the worker's current checks." : "" };
    }

    /// <summary>Reverse consumers, with evidence and explicit cycle/shared/missing/budget markers.</summary>
    public IReadOnlyList<TreeEntry> ReverseTree(string id, int maxDepth = 16, int maxEntries = 256)
    {
        maxDepth = Math.Max(0, Math.Min(64, maxDepth)); maxEntries = Math.Max(1, Math.Min(4096, maxEntries));
        var result = new List<TreeEntry>(); var seen = new HashSet<string>(Compare);
        void Visit(string node, int depth, HashSet<string> ancestors, string evidence)
        {
            if (result.Count >= maxEntries) { result[result.Count - 1].Truncated = true; return; }
            bool cycle = ancestors.Contains(node), shared = !seen.Add(node);
            var entry = new TreeEntry { Id = node, Depth = depth, Evidence = evidence, Cycle = cycle, Shared = shared,
                Missing = !Components.Any(c => Compare.Equals(c.Id, node)) };
            result.Add(entry);
            var incoming = Edges.Where(e => Compare.Equals(e.To, node)).OrderBy(e => e.From, Compare).ToList();
            if (cycle || shared) return;
            if (depth >= maxDepth) { entry.Truncated = incoming.Count > 0; return; }
            var next = new HashSet<string>(ancestors, Compare) { node };
            foreach (var edge in incoming) Visit(edge.From, depth + 1, next, edge.Evidence + (edge.Hard ? " [hard]" : " [soft]"));
        }
        Visit(id, 0, new HashSet<string>(Compare), "target"); return result;
    }
}
