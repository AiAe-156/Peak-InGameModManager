#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BepInEx;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

/// <summary>No directory watchers or recursive background polling. Explicit writes invalidate views;
/// four fixed metadata file stamps are checked on demand at most once every two seconds.</summary>
internal static class OverviewPageCache
{
    // Unity Mono can implement FileSystemWatcher as a recursive 750ms directory poll.
    // Never instantiate one: watch only fixed metadata files with throttled stat calls.
    private static long _nextProbe;
    private static FileStamp _indexStamp, _appliedStamp, _legacyStamp, _informationStamp;
    private static string _appliedPath = "", _legacyPath = "", _informationPath = "";
    private readonly struct FileStamp : IEquatable<FileStamp>
    {
        private readonly bool _exists;
        private readonly long _length, _written;
        internal FileStamp(string path)
        {
            var file = new FileInfo(path); _exists = file.Exists;
            _length = _exists ? file.Length : 0; _written = _exists ? file.LastWriteTimeUtc.Ticks : 0;
        }
        public bool Equals(FileStamp other) => _exists == other._exists && _length == other._length && _written == other._written;
    }
    private static string _plugins = "", _config = "", _data = "";
    private static int _inventory, _content, _configuration, _environment;
    private static string _packIndex = "";
    private static volatile bool _environmentReliable;
    internal static int Environment { get { ProbeMetadata(); return Volatile.Read(ref _environment); } }
    internal static bool EnvironmentReliable => _environmentReliable;
    private static bool _started;
    private static volatile bool _reliable;
    private static OverviewPack? _header;
    private static int _headerRevision = -1;
    internal static int Inventory => Volatile.Read(ref _inventory);
    internal static int Content => Volatile.Read(ref _content);
    internal static int Configuration => Volatile.Read(ref _configuration);
    internal static bool Reliable => _reliable;

    internal static void Initialize()
    {
        if (_started) return; _started = true; _reliable = true;
        _plugins = FullRoot(Paths.BepInExRootPath);
        _config = FullRoot(Paths.ConfigPath);
        _data = FullRoot(Path.Combine(Paths.ConfigPath, ModConfigEnhance.Updating.ManagerIdentity.ConfigDirectory));
        _packIndex = Path.GetFullPath(ModConfigEnhance.Updating.ManagerIdentity.Runtime(Paths.BepInExRootPath, "packs", "index.json"));
        _appliedPath = Path.Combine(Updating.MceDirectories.Runtime(_data), "applied-profile.json");
        _legacyPath = Path.Combine(Updating.MceDirectories.Legacy(_data), "state.json");
        _informationPath = Path.Combine(_data, "pack-information.json");
        _environmentReliable = true;
        ProbeMetadata();
    }
    private static void ProbeMetadata()
    {
        if (!_started) return;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long next = Interlocked.Read(ref _nextProbe);
        if (now < next || Interlocked.CompareExchange(ref _nextProbe, now + System.Diagnostics.Stopwatch.Frequency * 2, next) != next) return;
        try
        {
            var index = new FileStamp(_packIndex);
            if (!index.Equals(_indexStamp)) { _indexStamp = index; Interlocked.Increment(ref _environment); Interlocked.Increment(ref _inventory); Interlocked.Increment(ref _content); }
            var applied = new FileStamp(_appliedPath); var legacy = new FileStamp(_legacyPath); var information = new FileStamp(_informationPath);
            if (!applied.Equals(_appliedStamp) || !legacy.Equals(_legacyStamp) || !information.Equals(_informationStamp))
            { _appliedStamp = applied; _legacyStamp = legacy; _informationStamp = information; Interlocked.Increment(ref _content); }
            _environmentReliable = true;
        }
        catch (IOException) { _environmentReliable = false; }
        catch (UnauthorizedAccessException) { _environmentReliable = false; }
    }
    private static string FullRoot(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    internal static void ChangedPath(string path)
    {
        string full = Path.GetFullPath(path);
        string name = Path.GetFileName(full);
        if (full.StartsWith(_data, StringComparison.OrdinalIgnoreCase))
        {
            string relative = full.Substring(_data.Length).Replace('\\', '/');
            if (relative.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".lastvalid", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("profiles", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("data/runtime/ui.json", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("data/runtime/applied-profile.json", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("data/runtime/sources.json", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("data/legacy/", StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref _content);
            return;
        }
        if (full.StartsWith(_config, StringComparison.OrdinalIgnoreCase))
        {
            if (name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) Interlocked.Increment(ref _configuration);
            return;
        }
        if (full.StartsWith(_plugins, StringComparison.OrdinalIgnoreCase))
        {
            if (name.IndexOf(".dll", StringComparison.OrdinalIgnoreCase) >= 0 || name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || name.Equals("manifest.json.old", StringComparison.OrdinalIgnoreCase) || !Path.HasExtension(name)) Interlocked.Increment(ref _inventory);
            if (ModConfigEnhance.Updating.ManagerIdentity.ContainsPackFolder(full)) Interlocked.Increment(ref _content);
        }
    }
    private static long _nextExternalRefresh;
    internal static void OnPageEnter()
    {
        Initialize(); ProbeMetadata();
        // External DLL/config edits are checked by the existing page-entry scan, never
        // by an always-running recursive watcher. Fast settings round-trips keep cache.
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (now < _nextExternalRefresh) return;
        _nextExternalRefresh = now + System.Diagnostics.Stopwatch.Frequency * 10;
        Interlocked.Increment(ref _inventory); Interlocked.Increment(ref _configuration);
    }
    internal static void Invalidate() { Interlocked.Increment(ref _inventory); Interlocked.Increment(ref _content); Interlocked.Increment(ref _environment); }
    internal static void ConfigurationSaved() { Interlocked.Increment(ref _configuration); Interlocked.Increment(ref _content); }
    internal static OverviewPack Header(bool force = false)
    {
        Initialize(); ProbeMetadata(); int revision = Content;
        if (!force && _header != null && _headerRevision == revision && _reliable) return _header;
        var header = new OverviewPack();
        try
        {
            // Read only the pack object, stopping before the large configuration snapshot.
            foreach (string file in new[] { _informationPath, _appliedPath, _legacyPath })
            {
                if (!File.Exists(file)) continue;
                using var source = File.OpenText(file); using var reader = new JsonTextReader(source);
                while (reader.Read())
                    if (reader.TokenType == JsonToken.PropertyName && (string?)reader.Value == "pack" && reader.Depth <= 2)
                    { reader.Read(); header = JsonSerializer.CreateDefault().Deserialize<OverviewPack>(reader) ?? header; break; }
                break;
            }
        }
        catch (Exception ex) { Plugin.Log.LogDebug("[页面缓存] 入口信息读取失败：" + ex.Message); if (_header != null) return _header; }
        _headerRevision = revision; return _header = header;
    }
    internal static void Dispose()
    {
        _nextProbe = 0; _nextExternalRefresh = 0; _reliable = false;
        _indexStamp = _appliedStamp = _legacyStamp = _informationStamp = default;
        _started = false; _header = null; _environmentReliable = false; Interlocked.Increment(ref _environment);
    }
}
