#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewListView
{
    // Main-thread capture from the full catalogue, never the filtered/virtualized row set.
    // Infrastructure is the first existing overview group; missing rule IDs use the same
    // Ungrouped caption as BuildGroupHeader and sort after the configured groups.
    internal static (string Id, string Name)[] CaptureCheckProgressGroups(
        IEnumerable<(string Id, string Name, int Order)> groups, IEnumerable<string> fullModGroupIds,
        string infrastructureTitle, string ungroupedTitle)
    {
        var result = new List<(string Id, string Name)> { ("__infrastructure", infrastructureTitle) };
        var seen = new HashSet<string>(StringComparer.Ordinal) { "__infrastructure" };
        foreach (var group in groups.OrderBy(g => g.Order))
            if (seen.Add(group.Id)) result.Add((group.Id, group.Name));
        foreach (string id in fullModGroupIds)
            if (seen.Add(id)) result.Add((id, ungroupedTitle));
        return result.ToArray();
    }

    // Counts and summary come from the same immutable service snapshot. Keep full text;
    // the status label handles width-based ellipsis, never a character-count truncation.
    internal static string FormatCheckProgress(string summary, IReadOnlyList<(string Id, string Name)> groups,
        Func<string, (int Done, int Total)?> groupProgress, bool completed = false)
    {
        var details = new List<string>();
        foreach (var group in groups)
        {
            var count = groupProgress(group.Id);
            if (count.HasValue) details.Add(SingleLine(group.Name) + "_" + count.Value.Done + "/" + count.Value.Total);
        }
        string text = SingleLine(summary);
        if (details.Count == 0) return text;
        // Insert immediately after the overall N/N, before the localized result suffix.
        int end = -1;
        for (int i = 1; i + 1 < text.Length; i++)
            if (text[i] == '/' && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1]))
            {
                end = i + 2;
                while (end < text.Length && char.IsDigit(text[end])) end++;
                break;
            }
        if (end < 0) end = text.Length;
        return text.Insert(end, " (" + string.Join(", ", details) + ")");
    }

    internal static string SingleLine(string text) => text.Replace("\r\n", " · ").Replace("\r", " · ")
        .Replace("\n", " · ").Replace('\u2028', ' ').Replace('\u2029', ' ');
}
