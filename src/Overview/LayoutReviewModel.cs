#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>Shapes a layout preview's node list into what the review window draws (2.3.0): one card per destination
/// group folder (nested by folder depth, so nested groups appear as nested cards), then "stays where it is", then
/// "empty folders". Category folders that get renamed are shown in their card's header. Pure logic, checked without Unity.</summary>
internal static class LayoutReviewModel
{
    internal enum Kind { Move, Extracted, InPlace, Ungrouped, Stays, Skipped, Empty }

    internal sealed class Entry
    {
        internal LayoutTreeNode Node = null!;
        internal Kind Kind;
        internal string Folder = "", From = "", To = "";
        internal List<string> Reasons = new();
        internal List<string> Notes = new();
    }

    internal sealed class Section
    {
        /// <summary>Destination folder relative to plugins/ ("" = plugins/ itself), or a special key.</summary>
        internal string Key = "", Title = "";
        internal int Depth = 1;
        internal bool Stays, EmptyFolders, Ungrouped;
        /// <summary>The category folder this group folder is renamed from, when it is.</summary>
        internal string RenamedFrom = "";
        internal readonly List<Entry> Entries = new();
        internal int Count(Kind kind) => Entries.Count(e => e.Kind == kind);
        internal int Mods => Entries.Sum(e => Math.Max(1, e.Node.ModCount));
        /// <summary>Cards where something changes start open; a card where everything is already in place starts folded.</summary>
        internal bool OpenByDefault => Stays || EmptyFolders || RenamedFrom.Length > 0 || Entries.Any(e => e.Kind is Kind.Move or Kind.Extracted or Kind.Ungrouped);
    }

    internal sealed class Summary
    {
        internal int Move, Extracted, InPlace, Ungrouped, Stays, Empty, Renamed, Folders, Mods;
        internal int Changes => Move + Extracted + Renamed;
    }

    static string Parent(string path) { int at = path.LastIndexOf('/'); return at < 0 ? "" : path.Substring(0, at); }
    static string Leaf(string path) => path.Substring(path.LastIndexOf('/') + 1);
    static string UnderPlugins(string path) => path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) ? path.Substring("plugins/".Length) : path == "plugins" ? "" : path;

    internal static Kind Classify(LayoutTreeNode node)
    {
        if (node.Kind == LayoutNodeKind.Empty) return Kind.Empty;
        if (node.Disposition is InstallationLayoutDisposition.Deferred or InstallationLayoutDisposition.Rejected) return Kind.Skipped;
        if (node.Notes.Contains("NoGroupKnown")) return Kind.Stays;
        if (node.Notes.Contains("NestedExtracted")) return Kind.Extracted;
        if (node.Disposition == InstallationLayoutDisposition.Move && !node.Notes.Contains("CarriedByFolderRename")) return node.Ungrouped ? Kind.Ungrouped : Kind.Move;
        return node.Ungrouped ? Kind.Ungrouped : Kind.InPlace;
    }

    internal static List<Section> Build(IEnumerable<LayoutTreeNode> nodes, out Summary summary)
    {
        var all = nodes.ToList();
        var cards = new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);
        Section? stays = null, empties = null;
        var totals = new Summary();
        Section Card(string key)
        {
            if (cards.TryGetValue(key, out var card)) return card;
            card = new Section { Key = key, Title = key.Length == 0 ? "" : Leaf(key) + "/", Depth = key.Length == 0 ? 1 : key.Count(c => c == '/') + 1 };
            cards.Add(key, card);
            // Every ancestor folder gets a card too, so a nested group is always drawn inside its parent.
            if (key.Contains('/')) Card(Parent(key));
            return card;
        }
        foreach (var container in all.Where(n => n.Kind == LayoutNodeKind.Container))
        {
            var card = Card(UnderPlugins(container.FinalPath));
            card.RenamedFrom = UnderPlugins(container.OriginalPath);
            totals.Renamed++;
        }
        foreach (var node in all.Where(n => n.Kind != LayoutNodeKind.Container))
        {
            var kind = Classify(node);
            var entry = new Entry { Node = node, Kind = kind, Folder = Leaf(node.FinalPath.Length > 0 ? node.FinalPath : node.OriginalPath), From = node.OriginalPath, To = node.FinalPath,
                Reasons = node.Reasons.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).Where(r => r.Length > 0).ToList(),
                Notes = node.Notes.Where(n => n is not ("ManagerMovedLast" or "CarriedByFolderRename" or "NestedExtracted")).ToList() };
            switch (kind)
            {
                case Kind.Move: totals.Move++; break;
                case Kind.Extracted: totals.Extracted++; break;
                case Kind.InPlace: totals.InPlace++; break;
                case Kind.Ungrouped: totals.Ungrouped++; break;
                case Kind.Empty: totals.Empty++; break;
                default: totals.Stays++; break;
            }
            if (kind != Kind.Empty) { totals.Folders++; totals.Mods += Math.Max(1, node.ModCount); }
            if (kind == Kind.Empty) { entry.Folder = UnderPlugins(node.OriginalPath); (empties ??= new Section { Key = "~empty", EmptyFolders = true }).Entries.Add(entry); continue; }
            if (kind is Kind.Stays or Kind.Skipped) { entry.Folder = Leaf(node.OriginalPath); (stays ??= new Section { Key = "~stays", Stays = true }).Entries.Add(entry); continue; }
            var target = Card(UnderPlugins(Parent(node.FinalPath)));
            if (kind == Kind.Ungrouped) target.Ungrouped = true;
            target.Entries.Add(entry);
        }
        // Folder order: depth-first by path, plugins/ root last among folders; inside a card changes first.
        var ordered = cards.Values.Where(c => c.Key.Length > 0).OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).ToList();
        if (cards.TryGetValue("", out var rootCard) && rootCard.Entries.Count > 0) ordered.Add(rootCard);
        foreach (var card in ordered)
            card.Entries.Sort((a, b) => Rank(a.Kind).CompareTo(Rank(b.Kind)) is int c && c != 0 ? c : StringComparer.OrdinalIgnoreCase.Compare(a.Folder, b.Folder));
        if (stays != null) { stays.Entries.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Folder, b.Folder)); ordered.Add(stays); }
        if (empties != null) { empties.Entries.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Folder, b.Folder)); ordered.Add(empties); }
        summary = totals;
        return ordered;
    }

    /// <summary>Whether a card is inside a folded ancestor card.</summary>
    internal static bool HiddenUnder(Section card, ICollection<string> folded)
    {
        if (card.Stays || card.EmptyFolders || card.Key.Length == 0) return false;
        for (string parent = Parent(card.Key); parent.Length > 0; parent = Parent(parent)) if (folded.Contains(parent)) return true;
        return false;
    }

    /// <summary>Cards a card contains (directly or deeper), for the header count.</summary>
    internal static int SubtreeMods(Section card, IEnumerable<Section> all)
        => card.Mods + all.Where(c => !c.Stays && !c.EmptyFolders && c.Key.Length > card.Key.Length && card.Key.Length > 0 && c.Key.StartsWith(card.Key + "/", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Mods);

    static int Rank(Kind kind) => kind switch { Kind.Move => 0, Kind.Extracted => 1, Kind.Ungrouped => 2, _ => 3 };
}
