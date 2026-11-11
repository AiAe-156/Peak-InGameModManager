#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>Groups update-history records into the batches the user started them in. Pure logic so the
/// record view's grouping can be checked without Unity.</summary>
internal static class UpdateHistoryBatches
{
    /// <summary>Records written before batches were tracked have no BatchId; ones created within this window of
    /// each other came from the same click.</summary>
    internal static readonly TimeSpan LegacyWindow = TimeSpan.FromMinutes(5);

    internal sealed class Batch
    {
        internal string Key = "";
        /// <summary>When the batch was started (UTC); null when no record carries a usable time.</summary>
        internal DateTime? StartedUtc;
        internal DateTime LatestUtc = DateTime.MinValue;
        internal readonly List<UpdateJob> Jobs = new();
    }

    internal static DateTime? Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc) ? utc : null;
    }

    static DateTime Activity(UpdateJob job) => Parse(job.UpdatedUtc) ?? Parse(job.CreatedUtc) ?? DateTime.MinValue;

    /// <summary>Newest batch (by start) first; inside a batch, the most recently finished record first.</summary>
    internal static List<Batch> Group(IEnumerable<UpdateJob> jobs)
    {
        var batches = new List<Batch>();
        var byKey = new Dictionary<string, Batch>(StringComparer.Ordinal);
        var legacy = new List<(DateTime? Created, UpdateJob Job)>();
        foreach (var job in jobs)
        {
            if (job.BatchId.Length == 0) { legacy.Add((Parse(job.CreatedUtc), job)); continue; }
            if (!byKey.TryGetValue(job.BatchId, out var batch)) { batch = new Batch { Key = job.BatchId }; byKey.Add(job.BatchId, batch); batches.Add(batch); }
            batch.Jobs.Add(job);
            var started = Parse(job.BatchCreatedUtc) ?? Parse(job.CreatedUtc);
            if (started != null && (batch.StartedUtc == null || started < batch.StartedUtc)) batch.StartedUtc = started;
        }
        // Legacy records: walk them oldest first and start a new batch whenever the gap to the batch's first record exceeds the window.
        Batch? current = null; DateTime anchor = DateTime.MinValue;
        foreach (var (created, job) in legacy.Where(l => l.Created != null).OrderBy(l => l.Created!.Value))
        {
            if (current == null || created!.Value - anchor > LegacyWindow)
            {
                anchor = created!.Value;
                current = new Batch { Key = "legacy:" + anchor.Ticks, StartedUtc = anchor }; batches.Add(current);
            }
            current.Jobs.Add(job);
        }
        var undated = legacy.Where(l => l.Created == null).Select(l => l.Job).ToList();
        if (undated.Count > 0) { var rest = new Batch { Key = "undated" }; rest.Jobs.AddRange(undated); batches.Add(rest); }
        foreach (var batch in batches)
        {
            batch.Jobs.Sort((a, b) => Activity(b).CompareTo(Activity(a)));
            batch.LatestUtc = batch.Jobs.Count == 0 ? DateTime.MinValue : Activity(batch.Jobs[0]);
        }
        // The header shows when a batch was started, so batches sort by that (newest first); undated ones go last.
        return batches.OrderBy(b => b.StartedUtc == null ? 1 : 0)
            .ThenByDescending(b => b.StartedUtc ?? DateTime.MinValue).ThenByDescending(b => b.LatestUtc).ToList();
    }
}
