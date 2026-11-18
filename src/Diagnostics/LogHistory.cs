#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace ModConfigEnhance.Diagnostics;

/// <summary>
/// 同步自 MCE 1.1.0（实测修正）：保存最近几次启动的日志，给日志窗口的「本次／上次／上上次／3 次前」用。
/// BepInEx 每次启动都会覆盖 LogOutput.log、Unity 只留一份 Player-prev.log，管理器载入时上一次的 BepInEx 日志已经没了，
/// 所以本次运行中自己把两个日志文件的新增内容每 2 秒追加到 <c>Loc.RuntimeDirectory/log-history/&lt;启动时间&gt;/</c>（IGMM 即 <c>config/InGameModManager/data/runtime/log-history/</c>）
/// （后台计时器线程，原样复制字节；退出游戏时再补一次）。当机时最多丢最后约 2 秒。
/// 保留：启动时只留最近 <see cref="KeepPrevious"/> 次的文件夹，再建本次的，所以磁盘上最多 4 份（本次 + 前 3 次）。
/// 大小：每个文件超过 <see cref="MaxFileBytes"/> 时砍掉前半段（保留最新的内容），最坏 4 × 2 × 10 MB。
/// 只读写管理器自己的 runtime 目录；不联网、不启动进程。
/// </summary>
internal static class LogHistory
{
    internal const int KeepPrevious = 3;
    internal const long MaxFileBytes = 10L * 1024 * 1024;
    internal const string BepInExFile = "BepInEx.log", GameFile = "Player.log";
    private const int IntervalMs = 2000;
    private const string StampFormat = "yyyyMMdd-HHmmss";
    private static readonly Regex SessionName = new(@"^\d{8}-\d{6}(-\d+)?$", RegexOptions.Compiled);
    private static readonly object Gate = new();
    private static Timer? _timer;
    private static Tail[] _tails = Array.Empty<Tail>();
    private static string _previousUnityLog = "";
    internal static string Root => Path.Combine(Loc.RuntimeDirectory, "log-history");
    internal static string CurrentDirectory { get; private set; } = "";

    /// <summary>主线程调用一次（<see cref="RuntimeLogCapture.Start"/>）。失败只记一条警告，日志窗口照常（历史项显示「没有记录」）。</summary>
    internal static void Start()
    {
        if (_timer != null) return;
        try
        {
            string bep = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            string game = Application.consoleLogPath ?? "";
            if (game.Length > 0) _previousUnityLog = Path.Combine(Path.GetDirectoryName(game) ?? "", "Player-prev.log");
            Directory.CreateDirectory(Root);
            foreach (var old in Sessions().Skip(KeepPrevious))
            {
                try { Directory.Delete(old, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            string name = DateTime.Now.ToString(StampFormat, CultureInfo.InvariantCulture), dir = Path.Combine(Root, name);
            for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(Root, name + "-" + n);
            Directory.CreateDirectory(dir); CurrentDirectory = dir;
            _tails = game.Length > 0
                ? new[] { new Tail(bep, Path.Combine(dir, BepInExFile)), new Tail(game, Path.Combine(dir, GameFile)) }
                : new[] { new Tail(bep, Path.Combine(dir, BepInExFile)) };
            _timer = new Timer(_ => Pump(), null, IntervalMs, IntervalMs);
            Application.quitting += Stop;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Plugin.Log.LogWarning("[日志历史] 无法建立 runtime/log-history，本次不保存历史日志：" + e.Message);
        }
    }
    /// <summary>退出游戏时最后补一次，然后停计时器。</summary>
    internal static void Stop()
    {
        var timer = _timer; _timer = null; timer?.Dispose();
        lock (Gate) foreach (var tail in _tails) tail.Pump(); // 等计时器那一轮做完再补最后一次
    }
    private static void Pump()
    {
        if (!Monitor.TryEnter(Gate)) return; // 上一轮还没做完（大文件）就跳过这一轮
        try { foreach (var tail in _tails) tail.Pump(); }
        finally { Monitor.Exit(Gate); }
    }
    /// <summary>历史文件夹，新的在前（文件夹名就是启动时间，按名字排即按时间排）。</summary>
    private static string[] Sessions()
    {
        try
        {
            if (!Directory.Exists(Root)) return Array.Empty<string>();
            return Directory.GetDirectories(Root).Where(d => SessionName.IsMatch(Path.GetFileName(d)))
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
    /// <summary>往前数第 <paramref name="back"/> 次（1 = 上次）的文件夹；不含本次。</summary>
    private static string? Previous(int back)
    {
        if (back < 1 || back > KeepPrevious) return null;
        var previous = Sessions().Where(d => !string.Equals(d, CurrentDirectory, StringComparison.OrdinalIgnoreCase)).ToArray();
        return back <= previous.Length ? previous[back - 1] : null;
    }
    /// <summary>那一次的启动时间；没有记录时 null。</summary>
    internal static DateTime? SessionTime(int back)
    {
        var dir = Previous(back); if (dir == null) return null;
        string name = Path.GetFileName(dir); if (name.Length > 15) name = name.Substring(0, 15);
        return DateTime.TryParseExact(name, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;
    }
    /// <summary>Unity 自己留的上一次 Player 日志（存在才返回）。</summary>
    internal static string? PreviousUnityLog() => _previousUnityLog.Length > 0 && System.IO.File.Exists(_previousUnityLog) ? _previousUnityLog : null;
    /// <summary>往前数第 <paramref name="back"/> 次的 BepInEx／游戏日志副本；没有时 null。
    /// 上次的游戏日志没有副本时（例如刚装这一版），退回 Unity 的 Player-prev.log。</summary>
    internal static string? File(int back, bool game)
    {
        var dir = Previous(back);
        string? path = dir == null ? null : Path.Combine(dir, game ? GameFile : BepInExFile);
        if (path != null && System.IO.File.Exists(path)) return path;
        return game && back == 1 ? PreviousUnityLog() : null;
    }

    /// <summary>把一个正在写的日志文件的新增字节追加到副本。源文件被重建（变短或创建时间变了）时从头再跟。</summary>
    private sealed class Tail
    {
        private readonly string _source, _target;
        private long _position;
        private DateTime _created;
        private readonly byte[] _buffer = new byte[65536];
        internal Tail(string source, string target) { _source = source; _target = target; }
        internal void Pump()
        {
            try
            {
                if (!System.IO.File.Exists(_source)) return;
                var info = new FileInfo(_source);
                if (info.Length < _position || _created != default && info.CreationTimeUtc != _created) _position = 0;
                _created = info.CreationTimeUtc;
                if (info.Length == _position) return;
                long size;
                using (var source = new FileStream(_source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var target = new FileStream(_target, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete))
                {
                    source.Position = _position;
                    long budget = 8L * 1024 * 1024; int count;
                    while (budget > 0 && (count = source.Read(_buffer, 0, (int)Math.Min(_buffer.Length, budget))) > 0) { target.Write(_buffer, 0, count); budget -= count; }
                    _position = source.Position;
                    target.Flush(); size = target.Length;
                }
                if (size > MaxFileBytes) Trim();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        /// <summary>只留后半段（从一个换行之后开始，UTF-8 不会被切坏），前面加一行说明。</summary>
        private void Trim()
        {
            byte[] keep;
            using (var stream = new FileStream(_target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long start = stream.Length - MaxFileBytes / 2; stream.Position = start;
                int b; while ((b = stream.ReadByte()) >= 0 && b != '\n') { }
                keep = new byte[stream.Length - stream.Position];
                LogParser.ReadPrefix(stream, keep);
            }
            byte[] note = Encoding.UTF8.GetBytes("[Info   :" + Plugin.PluginName + "] Log history: earlier lines exceeded " + MaxFileBytes / 1024 / 1024 + " MB and were removed.\n");
            string temp = _target + ".tmp";
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { output.Write(note, 0, note.Length); output.Write(keep, 0, keep.Length); }
            // 近期审查：原来先删后改名，两步之间当机会把整份副本丢掉；File.Replace 是一步换（失败时才退回旧做法）。
            try { System.IO.File.Replace(temp, _target, null); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            { System.IO.File.Delete(_target); System.IO.File.Move(temp, _target); }
        }
    }
}
