#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Read-only filesystem evidence. No repository, rule, source policy or UI objects.</summary>
internal sealed class OverviewScanInventory
{
    private readonly struct FileStamp
    {
        private readonly string _path;
        private readonly bool _exists;
        private readonly long _size, _written, _created;
        internal FileStamp(string path)
        {
            _path = path;
            var info = new FileInfo(path); _exists = info.Exists;
            _size = _exists ? info.Length : 0;
            _written = _exists ? info.LastWriteTimeUtc.Ticks : 0;
            _created = _exists ? info.CreationTimeUtc.Ticks : 0;
        }
        internal bool Matches()
        {
            var current = new FileStamp(_path);
            return _exists == current._exists && _size == current._size && _written == current._written && _created == current._created;
        }
    }
    private readonly struct DirectoryStamp
    {
        private readonly string _path;
        private readonly bool _exists;
        private readonly long _written, _created;
        private readonly string[] _entries;
        internal DirectoryStamp(string path)
        {
            _path = path;
            var info = new DirectoryInfo(path); _exists = info.Exists;
            _written = _exists ? info.LastWriteTimeUtc.Ticks : 0;
            _created = _exists ? info.CreationTimeUtc.Ticks : 0;
            _entries = _exists ? Directory.GetFileSystemEntries(path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray() : Array.Empty<string>();
        }
        internal bool Matches()
        {
            var current = new DirectoryStamp(_path);
            return _exists == current._exists && _written == current._written && _created == current._created
                && _entries.SequenceEqual(current._entries, StringComparer.OrdinalIgnoreCase);
        }
    }
    private readonly List<FileStamp> _files = new();
    private readonly List<DirectoryStamp> _directories = new();
    private bool _complete = true;
    internal ReadOnlyCollection<string> Assemblies { get; }
    /// <summary>2.3.4: every manifest.json / manifest.json.old under the root, so a package without any assembly
    /// (a resource pack: bundles, textures) still becomes an overview row.</summary>
    internal ReadOnlyCollection<string> Manifests { get; }

    internal OverviewScanInventory(string root, Func<string, bool> isAssembly)
    {
        var assemblies = new List<string>(); var manifests = new List<string>();
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            try
            {
                // Capture before enumeration: a racing add/remove cannot validate an older listing.
                _directories.Add(new DirectoryStamp(directory));
                if (!Directory.Exists(directory)) continue;
                foreach (string path in Directory.GetFiles(directory))
                {
                    if (isAssembly(path)) { assemblies.Add(path); WatchFile(path); }
                    else if (Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(path).Equals("manifest.json.old", StringComparison.OrdinalIgnoreCase)) { manifests.Add(path); WatchFile(path); }
                }
                foreach (string child in Directory.GetDirectories(directory))
                {
                    string name = Path.GetFileName(child);
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0 || name.Equals("备份") ||
                        name.Equals("backup", StringComparison.OrdinalIgnoreCase) || name.Equals("backups", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
                    pending.Push(child);
                }
            }
            catch (IOException) { _complete = false; }
            catch (UnauthorizedAccessException) { _complete = false; }
        }
        Assemblies = assemblies.AsReadOnly(); Manifests = manifests.AsReadOnly();
    }

    // Missing manifests must be observed too: a nearer manifest can replace an ancestor.
    internal void WatchFile(string path)
    {
        try { _files.Add(new FileStamp(path)); }
        catch (IOException) { _complete = false; }
        catch (UnauthorizedAccessException) { _complete = false; }
    }
    internal bool IsCurrent()
    {
        if (!_complete) return false;
        try { return _directories.All(d => d.Matches()) && _files.All(f => f.Matches()); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
