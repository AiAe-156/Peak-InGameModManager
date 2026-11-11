#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Pure single-row layout, also used by the focused overflow checks.</summary>
internal static class ToolbarLayout
{
    internal sealed class Item
    {
        internal string Id = "";
        internal int Group, CollapseOrder;
        internal float Width;
    }
    internal sealed class Result
    {
        internal readonly Dictionary<string, (float X, float Width)> Visible = new();
        internal readonly HashSet<string> Hidden = new();
        internal readonly List<float> Separators = new();
        internal float HelpX, MoreX;
    }
    internal static Result Arrange(float width, float helpWidth, float moreWidth, IReadOnlyList<Item> source)
    {
        var result = new Result { HelpX = Math.Max(0, width - helpWidth) };
        var items = source.Select(i => new Item { Id = i.Id, Group = i.Group, Width = i.Width, CollapseOrder = i.CollapseOrder }).ToList();
        float Used() { float x = 0; int group = -1; foreach (var i in items.Where(i => !result.Hidden.Contains(i.Id))) { if (group >= 0) x += group == i.Group ? 8 : 32; x += i.Width; group = i.Group; } return x; }
        float limit = Math.Max(0, result.HelpX - 12);
        if (Used() > limit)
        {
            var profile = items.FirstOrDefault(i => i.Id == "profile"); if (profile != null) profile.Width = Math.Min(profile.Width, 160);
        }
        if (Used() > limit)
        {
            limit = Math.Max(0, result.HelpX - 8 - moreWidth - 12);
            foreach (var item in items.Where(i => i.Id != "profile").OrderBy(i => i.CollapseOrder))
            { if (Used() <= limit) break; result.Hidden.Add(item.Id); }
            var profile = items.FirstOrDefault(i => i.Id == "profile"); if (profile != null && Used() > limit) profile.Width = Math.Max(0, profile.Width - (Used() - limit));
        }
        result.MoreX = Math.Max(0, result.HelpX - 8 - moreWidth);
        float left = 0; int previous = -1;
        foreach (var item in items.Where(i => !result.Hidden.Contains(i.Id)))
        {
            if (previous >= 0) { if (previous != item.Group) result.Separators.Add(left + 12); left += previous == item.Group ? 8 : 32; }
            result.Visible[item.Id] = (left, item.Width); left += item.Width; previous = item.Group;
        }
        return result;
    }
}
