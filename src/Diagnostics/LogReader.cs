#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ModConfigEnhance.Diagnostics;

internal static class LogParser
{
    internal static int ReadPrefix(Stream stream, byte[] bytes)
    {
        int offset = 0; while (offset < bytes.Length) { int count = stream.Read(bytes, offset, bytes.Length - offset); if (count == 0) break; offset += count; } return offset;
    }
    private static readonly Regex Header = new(@"^\[\s*(?<level>Fatal|Error|Warning|Warn|Info|Message|Debug)\s*:?\s*(?<source>[^\]]*)\]\s*(?<body>.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Timed = new(@"^\[(?<time>\d{2}:\d{2}:\d{2}(?:\.\d+)?)\]\s*(?<body>.*)$", RegexOptions.Compiled);
    internal static MceLogRecord Parse(string line, string epoch, long sequence)
    {
        var r = new MceLogRecord { Epoch = epoch, Sequence = sequence, Message = line };
        var time = Timed.Match(line); if (time.Success) { line = time.Groups["body"].Value; if (TimeSpan.TryParse(time.Groups["time"].Value, out var span)) r.Time = new DateTimeOffset(DateTime.Today.Add(span)); }
        var m = Header.Match(line);
        if (m.Success)
        {
            r.Category = m.Groups["source"].Value.Trim(); r.Message = m.Groups["body"].Value;
            r.Level = m.Groups["level"].Value.ToLowerInvariant() switch { "fatal" => MceLogLevel.Fatal, "error" => MceLogLevel.Error, "warning" or "warn" => MceLogLevel.Warning, "debug" => MceLogLevel.Debug, _ => MceLogLevel.Info };
        }
        else if (Regex.IsMatch(line, @"^(?:[\w.]+Exception\b|Exception:|ERROR[: ])")) r.Level = MceLogLevel.Error;
        else if (line.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase)) r.Level = MceLogLevel.Warning;
        return r;
    }
    internal static bool Continuation(string line) => line.StartsWith("  at ", StringComparison.Ordinal) || line.StartsWith(" at ", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal) || line.StartsWith("Rethrow as ", StringComparison.Ordinal) || line.StartsWith("UnityEngine.", StringComparison.Ordinal) || line.StartsWith("\t", StringComparison.Ordinal) || line.IndexOf(" (at ", StringComparison.Ordinal) >= 0;
    internal static string Plain(MceLogRecord r) => (r.Time == default ? "" : "[" + r.Time.ToString("HH:mm:ss.fff") + "] ") + (r.Level == MceLogLevel.Unknown ? "" : "[" + r.Level + "] ") + (r.Category.Length == 0 ? "" : "[" + r.Category + "] ") + (r.SubjectName.Length == 0 ? "" : r.SubjectName + ": ") + r.Message + (r.StackTrace.Length == 0 ? "" : "\n" + r.StackTrace);
}

/// <summary>Bounded view cache. Raw export uses separate complete file snapshots.</summary>
internal sealed class LogFileReader : IDisposable
{
    private FileStream? _stream;
    private readonly string _path;
    private readonly Decoder _decoder = new UTF8Encoding(false, false).GetDecoder();
    private readonly List<MceLogRecord> _records = new();
    private string _partial = "", _epoch = Guid.NewGuid().ToString("N");
    private long _sequence, _viewStart;
    private DateTime _created;
    private byte[] _prefix = Array.Empty<byte>();
    private bool _first = true;
    internal long TotalRecords => _sequence;
    internal LogFileReader(string path) => _path = path;
    internal MceLogRecord[] Read(bool earlier = false)
    {
        if (!File.Exists(_path)) throw new FileNotFoundException("日志文件不存在", _path);
        var info = new FileInfo(_path);
        bool reset = _stream == null || info.CreationTimeUtc != _created || info.Length < _stream.Position;
        if (!reset && _prefix.Length > 0)
        {
            using var probe = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] check = new byte[_prefix.Length]; int n = LogParser.ReadPrefix(probe, check); reset = n != check.Length || !check.SequenceEqual(_prefix);
        }
        long earlierEnd = earlier ? _viewStart : 0;
        if (reset || earlier)
        {
            _stream?.Dispose(); _stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _created = info.CreationTimeUtc; _records.Clear(); _partial = ""; _decoder.Reset(); _epoch = Guid.NewGuid().ToString("N"); _sequence = 0;
            _prefix = new byte[Math.Min(128, (int)Math.Min(info.Length, 128))]; LogParser.ReadPrefix(_stream, _prefix); _stream.Position = 0;
            long start = earlier ? Math.Max(0, earlierEnd - 2 * 1024 * 1024) : _first ? Math.Max(0, _stream.Length - 2 * 1024 * 1024) : 0;
            _stream.Position = start;
            if (start > 0) { int b; while ((b = _stream.ReadByte()) >= 0 && b != 10) { } }
            _viewStart = _stream.Position;
            _first = false;
        }
        byte[] bytes = new byte[65536]; char[] chars = new char[65536]; int count; long budget = earlier ? Math.Max(0, earlierEnd - _stream!.Position) : 2L * 1024 * 1024;
        while (budget > 0 && (count = _stream!.Read(bytes, 0, (int)Math.Min(bytes.Length, budget))) > 0)
        {
            budget -= count; int n = _decoder.GetChars(bytes, 0, count, chars, 0); string chunk = _partial + new string(chars, 0, n); string[] lines = chunk.Split('\n'); _partial = lines[^1];
            for (int i = 0; i < lines.Length - 1; i++) Add(lines[i].TrimEnd('\r'));
            if (_partial.Length > 262144) { Add(_partial); _partial = ""; }
        }
        // Keep only the bounded view. Export always rereads the complete source.
        if (_records.Count > MceLogApi.MaximumRecords) _records.RemoveRange(0, _records.Count - MceLogApi.MaximumRecords);
        var result = _records.Select(r => r.Copy()).ToList();
        if (_partial.Length > 0) result.Add(LogParser.Parse(_partial, _epoch, _sequence + 1));
        return result.ToArray();
    }
    private void Add(string line)
    {
        if (line.Length == 0 && _records.Count > 0 && _records[^1].Level is MceLogLevel.Error or MceLogLevel.Fatal) return;
        if (LogParser.Continuation(line) && _records.Count > 0) { var last = _records[^1]; if (last.StackTrace.Length < 262144) last.StackTrace += (last.StackTrace.Length == 0 ? "" : "\n") + line; }
        else {
            var record = LogParser.Parse(line, _epoch, ++_sequence);
            // Poll time is not emission time (a read can contain delayed or historical lines).
            // Untimed file entries stay unknown; Runtime retains each captured event's time.
            _records.Add(record); if (_records.Count > MceLogApi.MaximumRecords + 256) _records.RemoveRange(0, 256);
        }
    }
    public void Dispose() => _stream?.Dispose();
}
