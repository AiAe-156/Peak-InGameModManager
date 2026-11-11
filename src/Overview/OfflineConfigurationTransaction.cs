#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ModConfigEnhance.Overview;

/// <summary>Prepared offline cfg edits. Never loads a plugin or constructs a ConfigFile.</summary>
internal sealed class OfflineConfigurationTransaction
{
    private readonly string _path;
    private readonly Action _validatePath;
    private readonly List<ConfigurationDocument.Edit> _edits;
    private byte[]? _before, _written;
    private bool _existed, _started;
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;
    private const long MaximumBytes = 16 * 1024 * 1024;
    internal string Path => _path;
    internal bool HasChanges { get; }

    internal OfflineConfigurationTransaction(string path, IEnumerable<(string Section, string Key, string Value)> values, Action validatePath, IEnumerable<(string Section, string Key, bool Exists, string Value)>? expected = null)
    {
        _path = path; _validatePath = validatePath; _validatePath();
        byte[] bytes = ReadBytes();
        (_encoding, _preamble) = DetectEncoding(bytes);
        var document = new ConfigurationDocument(Decode(bytes));
        var baselines = expected?.ToList();
        _edits = values.Select(v => {
            var key = new ConfigurationDocument.Key(v.Section, v.Key);
            bool exists = document.TryGet(key, out string baseline);
            var captured = baselines?.Where(b => b.Section == v.Section && b.Key == v.Key).ToList();
            return new ConfigurationDocument.Edit { Key = key, Value = v.Value,
                BaselineExists = captured?.Count > 0 ? captured[0].Exists : exists, Baseline = captured?.Count > 0 ? captured[0].Value : baseline };
        }).ToList();
        // Also validates duplicate/invalid edits before any mutation.
        document.Conflicts(_edits);
        HasChanges = _edits.Any(e => !document.TryGet(e.Key, out string current) || current != e.Value);
    }

    internal void OverrideConflicts()
    {
        _validatePath();
        var current = new ConfigurationDocument(Decode(ReadBytes()));
        foreach (var edit in _edits)
        {
            edit.BaselineExists = current.TryGet(edit.Key, out string baseline); edit.Baseline = baseline;
        }
    }

    internal void Validate()
    {
        _validatePath();
        var document = new ConfigurationDocument(Decode(ReadBytes()));
        document.Merge(_edits);
        if (File.Exists(_path) && (File.GetAttributes(_path) & FileAttributes.ReadOnly) != 0)
            throw new IOException("Configuration file is read-only: " + _path);
    }

    internal void Apply(string backupDirectory)
    {
        if (!HasChanges || _started) return;
        Validate(); _validatePath();
        _existed = File.Exists(_path); _before = ReadBytes();
        string merged = new ConfigurationDocument(Decode(_before)).Merge(_edits);
        byte[] content = _encoding.GetBytes(merged);
        var bytes = _preamble.Concat(content).ToArray();
        Directory.CreateDirectory(backupDirectory);
        if (_existed) File.WriteAllBytes(System.IO.Path.Combine(backupDirectory, Guid.NewGuid().ToString("N") + ".offline.bak"), _before);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        string temporary = _path + ".mce-save-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            _validatePath();
            if (File.Exists(_path) != _existed || !ReadBytes().SequenceEqual(_before))
                throw new IOException("Configuration changed while saving; retry: " + _path);
            // Same-directory replacement leaves either the old or new complete document.
            if (_existed) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
            _written = bytes; _started = true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal void Rollback()
    {
        if (!_started) return;
        _validatePath();
        if (!File.Exists(_path) || !ReadBytes().SequenceEqual(_written!))
            throw new IOException("Configuration changed after saving; recovery retained the external content: " + _path);
        if (_existed) File.WriteAllBytes(_path, _before!);
        else File.Delete(_path);
        _started = false;
    }

    private byte[] ReadBytes()
    {
        if (!File.Exists(_path)) return Array.Empty<byte>();
        if (new FileInfo(_path).Length > MaximumBytes) throw new IOException("Configuration exceeds 16 MiB: " + _path);
        return File.ReadAllBytes(_path);
    }

    private string Decode(byte[] bytes)
    {
        var (encoding, preamble) = DetectEncoding(bytes);
        if (encoding.CodePage != _encoding.CodePage || bytes.Length > 0 && !preamble.SequenceEqual(_preamble))
            throw new IOException("Configuration encoding changed; refresh: " + _path);
        return encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
    }

    private static (Encoding Encoding, byte[] Preamble) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 4 && (bytes[0] == 0xff && bytes[1] == 0xfe && bytes[2] == 0 && bytes[3] == 0 || bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xfe && bytes[3] == 0xff))
            throw new InvalidDataException("UTF-32 configuration requires explicit conversion to UTF-8.");
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf) return (new UTF8Encoding(false, true), new byte[] { 0xef, 0xbb, 0xbf });
        if (bytes.Length >= 2 && bytes[0] == 0xff && bytes[1] == 0xfe) return (new UnicodeEncoding(false, false, true), new byte[] { 0xff, 0xfe });
        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff) return (new UnicodeEncoding(true, false, true), new byte[] { 0xfe, 0xff });
        return (new UTF8Encoding(false, true), Array.Empty<byte>());
    }
}
