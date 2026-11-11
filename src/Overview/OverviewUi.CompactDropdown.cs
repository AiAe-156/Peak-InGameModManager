#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace ModConfigEnhance.Overview;
internal static partial class OverviewUi
{
    /// <summary>Simple selectors/actions share the source menu's compact, anchored row renderer.</summary>
    internal static RectTransform? CompactDropdown(RectTransform anchor, string title, IReadOnlyList<UiMenuEntry> entries, RectTransform? owner = null)
    {
        var groups = new List<SourceToolGroup>();
        var rows = new List<SourceToolOption>(); string label = title;
        void Flush()
        {
            if (rows.Count == 0) return;
            groups.Add(new SourceToolGroup(label, Ivory, rows.ToArray())); rows.Clear();
        }
        foreach (var entry in entries)
        {
            if (entry.Header) { Flush(); label = entry.Label; continue; }
            rows.Add(new SourceToolOption { Entry = entry, Hint = () => entry.DisabledReason.Length > 0 ? entry.Label + "\n" + entry.DisabledReason : entry.Label });
        }
        Flush();
        return SourceToolsDropdown(anchor, groups, (target, hint) => { }, owner);
    }
}
