#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Diagnostics;

/// <summary>View-only aggregation, with bounded revisable records and retained counters for older occurrences.</summary>
internal sealed class LogRepeatIndex
{
    internal sealed class Group
    {
        internal string Key = "";
        internal MceLogRecord First = new(), Latest = new();
        internal long Count, Order, LatestOrder;
        internal long ArchivedCount;
        internal MceLogRecord? ArchivedFirst, ArchivedLatest;
        internal readonly SortedDictionary<long, MceLogRecord> Members = new();
        internal bool RepeatedError => Count > 1 && First.Level is MceLogLevel.Error or MceLogLevel.Fatal;
        internal string Summary => "重复 x" + Count + " · 首次 " + Time(First.Time) + " · 最近 " + Time(Latest.Time);
        internal string CopyText => Summary + "\n" + LogParser.Plain(Latest);
        private static string Time(DateTimeOffset value) => value == default ? "-" : value.ToString("HH:mm:ss.fff");
        internal void Refresh()
        {
            Count = ArchivedCount + Members.Count;
            First = ArchivedFirst ?? Members.FirstOrDefault().Value ?? new();
            Latest = Members.LastOrDefault().Value ?? ArchivedLatest ?? new();
        }
    }
    private readonly Dictionary<string, Group> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<long, string> _seen = new();
    private string _epoch = "";
    private long _order;
    internal static string Key(MceLogRecord r) => string.Join("\u001f", new[] { r.Category, r.Level.ToString(), r.SubjectId, r.SubjectName, r.Message, r.StackTrace });
    internal IReadOnlyList<Group> Groups => _groups.Values.OrderBy(g => g.Order).ToArray();
    internal static string SnapshotStamp(IReadOnlyList<MceLogRecord> records)
    {
        // Include every record, not just endpoints: providers can revise a middle record in place.
        // Folded field by field instead of hashing one concatenated string (plus the joined key) per record:
        // on an active log that built thousands of short-lived strings on every refresh, and the refresh runs
        // on a timer while the log window is open.
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (var r in records)
            {
                FoldText(ref hash, r.Category);
                FoldNumber(ref hash, (long)r.Level);
                FoldText(ref hash, r.SubjectId);
                FoldText(ref hash, r.SubjectName);
                FoldText(ref hash, r.Message);
                FoldText(ref hash, r.StackTrace);
                FoldText(ref hash, r.Epoch);
                FoldNumber(ref hash, r.Sequence);
                FoldNumber(ref hash, r.Time.Ticks);
            }
            return records.Count + ":" + hash;
        }
    }
    private static void FoldText(ref ulong hash, string? text)
    {
        if (text == null) return;
        foreach (char c in text) { hash ^= c; hash *= 1099511628211UL; }
    }
    private static void FoldNumber(ref ulong hash, long value)
    {
        for (int i = 0; i < 8; i++) { hash ^= (byte)(value >> (i * 8)); hash *= 1099511628211UL; }
    }
    internal void Clear() { _groups.Clear(); _seen.Clear(); _epoch = ""; _order = 0; }
    internal void Update(IReadOnlyList<MceLogRecord> records)
    {
        if (records.Count == 0) { Clear(); return; }
        string epoch = records[records.Count - 1].Epoch;
        if (_epoch != epoch) { Clear(); _epoch = epoch; }
        foreach (var record in records)
        {
            string key = Key(record);
            if (_seen.TryGetValue(record.Sequence, out var previous))
            {
                if (previous == key && _groups.TryGetValue(key, out var unchanged))
                { unchanged.Members[record.Sequence] = record.Copy(); unchanged.Refresh(); continue; }
                if (_groups.TryGetValue(previous, out var old)) { old.Members.Remove(record.Sequence); old.Refresh(); old.LatestOrder = old.Latest.Sequence; if (old.Count == 0) _groups.Remove(previous); }
            }
            _seen[record.Sequence] = key;
            if (!_groups.TryGetValue(key, out var group)) _groups.Add(key, group = new Group { Key = key, Order = ++_order });
            group.Members[record.Sequence] = record.Copy(); group.LatestOrder = Math.Max(group.LatestOrder, record.Sequence); group.Refresh();
        }
        long min = records.Min(r => r.Sequence);
        foreach (long sequence in _seen.Keys.Where(k => k < min).OrderBy(k => k).ToArray())
        {
            if (_groups.TryGetValue(_seen[sequence], out var group) && group.Members.TryGetValue(sequence, out var record))
            {
                group.ArchivedFirst ??= record; group.ArchivedLatest = record; group.ArchivedCount++; group.Members.Remove(sequence); group.Refresh();
            }
            _seen.Remove(sequence);
        }
        foreach (var group in _groups.Values.OrderBy(g => g.Order).Take(Math.Max(0, _groups.Count - MceLogApi.MaximumRecords)).ToArray()) _groups.Remove(group.Key);
    }
}
