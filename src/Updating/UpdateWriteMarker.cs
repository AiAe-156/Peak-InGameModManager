#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ModConfigEnhance.Updating;

/// <summary>
/// A marker the update worker drops while it is moving files, so the game can wait instead of starting
/// mid-write (a write that races the loader either fails the update or makes a plugin load the old copy).
/// Shared by the plugin (which waits) and the worker (which writes). Plain key=value text on purpose:
/// no JSON dependency is available on both sides. Never throws.
/// </summary>
public static class UpdateWriteMarker
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(5);

    public static string PathFor(string root) => ManagerIdentity.Runtime(root, "updating.lock");

    public static void Write(string root, string job, string name)
    {
        try
        {
            string path = PathFor(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            int pid = 0;
            try { using var process = System.Diagnostics.Process.GetCurrentProcess(); pid = process.Id; }
            catch (Exception) { }
            File.WriteAllLines(path, new[]
            {
                "job=" + job,
                "name=" + name.Replace("\r", " ").Replace("\n", " "),
                "pid=" + pid.ToString(CultureInfo.InvariantCulture),
                "started=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            });
        }
        catch (Exception) { }
    }

    public static void Clear(string root)
    {
        try { File.Delete(PathFor(root)); }
        catch (Exception) { }
    }

    public static string Describe(string root)
    {
        try
        {
            var values = Read(root);
            return values.TryGetValue("name", out var name) && name.Length > 0 ? name : "";
        }
        catch (Exception) { return ""; }
    }

    /// <summary>True while another process is mid-write. A record whose process is gone (crash) or that is
    /// older than <see cref="MaximumAge"/> is stale: it is removed instead of blocking startup.</summary>
    public static bool IsActive(string root)
    {
        try
        {
            var values = Read(root);
            if (values.Count == 0) return false;
            string path = PathFor(root);
            bool alive = false;
            if (values.TryGetValue("pid", out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) && pid > 0)
            {
                try { using var process = System.Diagnostics.Process.GetProcessById(pid); alive = !process.HasExited; }
                catch (ArgumentException) { alive = false; }
                catch (InvalidOperationException) { alive = false; }
            }
            bool fresh = values.TryGetValue("started", out var started)
                && DateTime.TryParse(started, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
                && DateTime.UtcNow - when < MaximumAge;
            if (!alive || !fresh) { File.Delete(path); return false; }
            return true;
        }
        catch (Exception) { return false; }
    }

    static Dictionary<string, string> Read(string root)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string path = PathFor(root);
        if (!File.Exists(path)) return values;
        foreach (string line in File.ReadAllLines(path).Take(16))
        {
            int split = line.IndexOf('=');
            if (split <= 0) continue;
            values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
        }
        return values;
    }
}
