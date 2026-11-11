#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Colour families for nested groups (2.3.0). A top-level group takes a family's base colour, one family per
/// top-level group while families last; a child group takes an unused shade from its parent's family (blue → sky blue,
/// grey blue …), so a subtree reads as one colour family. Used for new groups and for first-run smart grouping. Pure logic.</summary>
internal static class GroupPalette
{
    /// <summary>Each family: base first, then shades. Bases match the editor's existing swatches where they overlap.</summary>
    internal static readonly string[][] Families =
    {
        new[] { "#7CC7E6", "#9ED8F0", "#6FA3C7", "#8EA9BF", "#5DB3D9" }, // blue: sky, steel, grey blue, deep sky
        new[] { "#69B87B", "#8FCB98", "#5E9E6B", "#A5BE78", "#7FB59A" }, // green
        new[] { "#DDC374", "#E8D598", "#C9AA5C", "#E0B29B", "#CFBE8A" }, // gold
        new[] { "#B091C5", "#C7AEDA", "#9579AD", "#BB91AA", "#A79BC9" }, // purple
        new[] { "#C77772", "#D99A95", "#B0605C", "#DEA775", "#C98A86" }, // red
        new[] { "#82B5AC", "#A3CAC3", "#6E9C94", "#A1C2CA", "#8FB8A8" }, // teal
        new[] { "#A4AEC3", "#BFC6D5", "#8399B8", "#B5B2A1", "#9AA4B6" }, // slate
        new[] { "#C6A793", "#D8C0B0", "#AE8F7B", "#D4A88A", "#BFA08C" }, // brown
    };

    private static readonly StringComparer Hex = StringComparer.OrdinalIgnoreCase;

    internal static int FamilyOf(string? color)
    {
        if (string.IsNullOrEmpty(color)) return -1;
        for (int f = 0; f < Families.Length; f++) if (Families[f].Contains(color, Hex)) return f;
        return -1;
    }

    /// <summary>A base colour no top-level group uses yet; cycles when every family is taken.</summary>
    internal static string NextTopColor(IEnumerable<string?> usedTopColors)
    {
        var usedFamilies = new HashSet<int>(usedTopColors.Select(FamilyOf).Where(f => f >= 0));
        for (int f = 0; f < Families.Length; f++) if (!usedFamilies.Contains(f)) return Families[f][0];
        int count = usedTopColors.Count();
        return Families[count % Families.Length][0];
    }

    /// <summary>A shade from the parent's family that no sibling uses yet. A parent colour outside every family (a custom
    /// colour) has no family to draw from, so the child keeps the parent's colour.</summary>
    internal static string NextChildColor(string? parentColor, IEnumerable<string?> siblingColors)
    {
        int family = FamilyOf(parentColor);
        if (family < 0) return string.IsNullOrEmpty(parentColor) ? Families[0][0] : parentColor!;
        var used = new HashSet<string>(siblingColors.Where(c => !string.IsNullOrEmpty(c))!, Hex) { parentColor! };
        foreach (string shade in Families[family].Skip(1)) if (!used.Contains(shade)) return shade;
        foreach (string shade in Families[family]) if (!used.Contains(shade)) return shade;
        return Families[family][1 + used.Count % (Families[family].Length - 1)];
    }

    /// <summary>Default colour for a new group under <paramref name="parentId"/> ("" = top level).</summary>
    internal static string ForNewGroup(IReadOnlyList<OverviewGroup> groups, string parentId)
    {
        var tree = new GroupTree(groups);
        if (string.IsNullOrEmpty(parentId) || !tree.Contains(parentId)) return NextTopColor(tree.Roots.Select(g => g.color));
        return NextChildColor(tree.Get(parentId)!.color, tree.Children(parentId).Select(g => g.color));
    }
}
