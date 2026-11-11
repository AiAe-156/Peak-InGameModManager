#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModConfigEnhance.Diagnostics;

public enum MceLogLevel { Unknown, Debug, Info, Warning, Error, Fatal }

/// <summary>Plain text only. Epoch changes whenever a producer clears or restarts its log.</summary>
public sealed class MceLogRecord
{
    public string Epoch { get; set; } = "";
    public long Sequence { get; set; }
    public DateTimeOffset Time { get; set; }
    public MceLogLevel Level { get; set; }
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string StackTrace { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public Dictionary<string, string> Fields { get; set; } = new();
    internal MceLogRecord Copy() => new() { Epoch = Epoch, Sequence = Sequence, Time = Time, Level = Level, Category = Category, Message = Message, StackTrace = StackTrace, SubjectId = SubjectId, SubjectName = SubjectName, Fields = new(Fields ?? new()) };
}

/// <summary>Optional integration: providers run on background threads and must return a quick copy of existing records; never call Unity here.</summary>
public static class MceLogApi
{
    public const int ApiVersion = 1;
    public const int MaximumRecords = 10000;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Source> Sources = new(StringComparer.Ordinal);
    internal sealed class Source
    {
        internal string Id = "", Name = "", File = "";
        internal Func<IReadOnlyList<MceLogRecord>>? Snapshot;
    }
    public static IDisposable RegisterFile(string pluginGuid, string sourceId, string displayName, string absolutePath)
    {
        if (!Path.IsPathRooted(absolutePath)) throw new ArgumentException("An absolute log path is required.");
        return Register(new Source { Id = Identity(pluginGuid, sourceId), Name = displayName, File = Path.GetFullPath(absolutePath) });
    }
    public static IDisposable RegisterSnapshot(string pluginGuid, string sourceId, string displayName, Func<IReadOnlyList<MceLogRecord>> snapshot)
        => Register(new Source { Id = Identity(pluginGuid, sourceId), Name = displayName, Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot)) });
    /// <summary>Reflection-friendly optional bridge. Copy an existing numbered plain-text log; epoch changes on clear. No Unity calls in either callback.</summary>
    public static IDisposable RegisterTextSnapshot(string pluginGuid, string sourceId, string displayName, Func<IReadOnlyDictionary<long, string>> snapshot, Func<string> epoch)
    {
        if (snapshot == null || epoch == null) throw new ArgumentNullException(nameof(snapshot));
        return RegisterSnapshot(pluginGuid, sourceId, displayName, () => { string generation = epoch(); return snapshot().OrderBy(p => p.Key).Select(p => LogParser.Parse(p.Value, generation, p.Key)).ToArray(); });
    }
    public static MceLogChannel CreateChannel(string pluginGuid, string sourceId, string displayName, int capacity = 2000)
    {
        var channel = new MceLogChannel(Math.Max(1, Math.Min(MaximumRecords, capacity)));
        channel.Registration = RegisterSnapshot(pluginGuid, sourceId, displayName, channel.Snapshot);
        return channel;
    }
    private static string Identity(string guid, string id)
    {
        if (string.IsNullOrWhiteSpace(guid) || string.IsNullOrWhiteSpace(id) || guid.Length > 200 || id.Length > 100) throw new ArgumentException("A plugin GUID and source ID are required.");
        return guid + "/" + id;
    }
    private static IDisposable Register(Source source)
    {
        lock (Gate) { if (Sources.Count >= 128) throw new InvalidOperationException("Too many log sources."); if (Sources.ContainsKey(source.Id)) throw new InvalidOperationException("Log source already registered: " + source.Id); Sources.Add(source.Id, source); }
        return new Registration(source);
    }
    internal static Source[] List() { lock (Gate) return Sources.Values.ToArray(); }
    internal static MceLogRecord[] Read(Source source)
    {
        var records = source.Snapshot?.Invoke() ?? Array.Empty<MceLogRecord>();
        return records.Skip(Math.Max(0, records.Count - MaximumRecords)).Where(r => r != null).Select(r => r.Copy()).ToArray();
    }
    private sealed class Registration : IDisposable
    {
        private readonly Source _source;
        internal Registration(Source source) => _source = source;
        public void Dispose() { lock (Gate) if (Sources.TryGetValue(_source.Id, out var source) && ReferenceEquals(source, _source)) Sources.Remove(_source.Id); }
    }
}

public sealed class MceLogChannel : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<MceLogRecord> _records = new();
    private readonly int _capacity;
    private string _epoch = Guid.NewGuid().ToString("N");
    private long _sequence;
    private bool _disposed;
    internal IDisposable? Registration;
    internal MceLogChannel(int capacity) => _capacity = capacity;
    public void Write(MceLogRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        lock (_gate)
        {
            if (_disposed) return;
            var copy = record.Copy(); copy.Epoch = _epoch; copy.Sequence = ++_sequence;
            if (copy.Time == default) copy.Time = DateTimeOffset.Now;
            if (copy.Message.Length > 262144) copy.Message = copy.Message.Substring(0, 262144);
            _records.Enqueue(copy); while (_records.Count > _capacity) _records.Dequeue();
        }
    }
    public void Clear() { lock (_gate) { _records.Clear(); _epoch = Guid.NewGuid().ToString("N"); _sequence = 0; } }
    internal IReadOnlyList<MceLogRecord> Snapshot() { lock (_gate) return _records.Select(r => r.Copy()).ToArray(); }
    public void Dispose() { lock (_gate) { _disposed = true; _records.Clear(); } Registration?.Dispose(); }
}
