#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Rolls back only files written by the current profile metadata commit.</summary>
internal sealed class ProfileMetadataCheckpoint
{
    private readonly Dictionary<string, byte[]?> _before = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]?> _after = new(StringComparer.OrdinalIgnoreCase);
    internal ProfileMetadataCheckpoint(IEnumerable<string> paths)
    {
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase)) _before[path] = Read(path);
    }
    private static byte[]? Read(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
    internal void CaptureWritten()
    {
        foreach (string path in _before.Keys) _after[path] = Read(path);
    }
    internal void Restore()
    {
        var errors = new List<string>();
        foreach (var file in _before)
        {
            try
            {
                byte[]? current = Read(file.Key);
                if (_after.TryGetValue(file.Key, out var written) && !Same(current, written))
                    throw new IOException("Metadata changed externally; retained: " + file.Key);
                if (Same(current, file.Value)) continue;
                if (file.Value == null) { if (File.Exists(file.Key)) File.Delete(file.Key); }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file.Key)!);
                    string temporary = file.Key + ".recover-" + Guid.NewGuid().ToString("N");
                    File.WriteAllBytes(temporary, file.Value);
                    if (File.Exists(file.Key)) File.Replace(temporary, file.Key, null); else File.Move(temporary, file.Key);
                }
            }
            catch (Exception error) { errors.Add(error.Message); }
        }
        if (errors.Count > 0) throw new IOException(string.Join("\n", errors));
    }
    private static bool Same(byte[]? a, byte[]? b) => a == null ? b == null : b != null && a.SequenceEqual(b);
}
