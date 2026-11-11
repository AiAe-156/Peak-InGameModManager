using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MCE.Desktop;

// Deliberately small read projections of protocol2. Unknown worker fields are ignored.
// No install, migration, cancellation or recovery writes are implemented by this viewer.
internal sealed class TaskState
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = "";
    public string State { get; set; } = "";
    public string AttemptId { get; set; } = "";
    public string AttemptStartedUtc { get; set; } = "";
    public string AttemptEndedUtc { get; set; } = "";
    public string Message { get; set; } = "";
    public string Error { get; set; } = "";
    public object[] Originals { get; set; } = Array.Empty<object>();
    public object[] Desired { get; set; } = Array.Empty<object>();
    public RequestState Request { get; set; } = new();
    public PackState Proposed { get; set; } = new();
}
internal sealed class RequestState { public string Operation { get; set; } = ""; }
internal sealed class PackState { public string Id { get; set; } = ""; public string Name { get; set; } = ""; }
internal sealed class PlanState
{
    public string Phase { get; set; } = "";
    public object[] Removed { get; set; } = Array.Empty<object>();
    public object[] Placed { get; set; } = Array.Empty<object>();
    public object[] Originals { get; set; } = Array.Empty<object>();
    public object[] Desired { get; set; } = Array.Empty<object>();
    public int Done => (Removed?.Length ?? 0) + (Placed?.Length ?? 0);
    public LedgerState After { get; set; } = new();
    public int Total => (Originals?.Length ?? 0) + (Desired?.Length ?? 0);
}
internal sealed class LedgerState { public PackageState[] Packages { get; set; } = Array.Empty<PackageState>(); }
internal sealed class PackageState { public string Key { get; set; } = ""; public object[] Files { get; set; } = Array.Empty<object>(); }
internal static class Protocol
{
    internal static bool ValidId(string id) => Regex.IsMatch(id ?? "", "\\A[a-fA-F0-9]{32}\\z");
    internal static JavaScriptSerializer Json() => new() { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 128 };
    internal static void NoLinks(string path)
    {
        for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked path rejected: " + path);
    }
    internal static T Read<T>(string path)
    {
        NoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 32 * 1024 * 1024) throw new IOException("Status exceeds the safe read limit.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Json().Deserialize<T>(reader.ReadToEnd());
    }
    internal static TaskState ReadTask(string home, string expected = "")
    {
        string shop = Path.Combine(home, "shop", "task.json");
        if (expected.Length > 0 && File.Exists(shop))
        {
            var installation = Read<TaskState>(shop);
            if (installation != null && installation.Id == expected)
            {
                if (installation.SchemaVersion != 1 || !ValidId(installation.Id) || installation.Request == null)
                    throw new IOException("Invalid installation task.");
                installation.Proposed = new PackState { Name = installation.Request.Operation.StartsWith("layout-", StringComparison.Ordinal) ? "Folder organization" : "Mod installation" };
                return installation;
            }
        }
        var task = Read<TaskState>(Path.Combine(home, "pack-task.json"));
        if (task == null || task.SchemaVersion != 1 || !ValidId(task.Id) || task.Request == null || task.Proposed == null)
            throw new IOException("Unsupported or damaged pack task. No command was submitted.");
        return task;
    }
    internal static string Quote(string value)
    {
        // All arguments here are absolute paths without embedded quotes, or allowlisted values.
        if (value.IndexOf('"') >= 0 || value.Any(char.IsControl)) throw new ArgumentException("Invalid launch argument.");
        return "\"" + value.TrimEnd('\\', '/') + "\"";
    }
    internal static string Redact(string value, string root)
    {
        value = Regex.Replace(value ?? "", Regex.Escape(root), "<BepInEx>", RegexOptions.IgnoreCase);
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) value = Regex.Replace(value, Regex.Escape(profile), "<User>", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"(?i)\b(?:https?|socks5?)://[^\s<>""']+", "<url redacted>");
        value = Regex.Replace(value, @"(?im)\b(token|password|passwd|secret|authorization|api[_-]?key|cookie|proxy)\b[""']?\s*[:=]\s*[^\r\n]+", "$1=<redacted>");
        return value;
    }
    static string Hash(string path) { using var sha = SHA256.Create(); using var file = File.OpenRead(path); return Hex(sha.ComputeHash(file)); }
    static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    internal static void ValidateTools(string tools)
    {
        // Same protocol2 inventory envelope as WorkerProtocol. Validate before ANY retry launch.
        NoLinks(tools);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<string>(); pending.Push(tools);
        while (pending.Count > 0)
        {
            string current = pending.Pop(); NoLinks(current);
            foreach (string dir in Directory.GetDirectories(current)) { NoLinks(dir); pending.Push(dir); }
            foreach (string file in Directory.GetFiles(current))
            {
                NoLinks(file);
                string relative = file.Substring(tools.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
                if (relative == "worker-protocol.manifest") continue;
                if (relative.Any(c => char.IsControl(c) || c == '|' || c == ':') || relative.Split('/').Any(p => p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ")))
                    throw new IOException("Invalid tool path.");
                if (relative.Split('/').Any(p => new[] { "bin", "obj", "tests", "test", ".git" }.Contains(p, StringComparer.OrdinalIgnoreCase)) || new[] { ".pdb", ".cs", ".csproj", ".log", ".tmp", ".partial" }.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase))
                    throw new IOException("Build residue in tool cache.");
                files.Add(relative, Hash(file));
            }
        }
        if (!files.ContainsKey("MCE.UpdateWorker.exe") || !files.ContainsKey("Mono.Cecil.LICENSE.txt")) throw new IOException("Incomplete worker cache.");
        string manifest = Path.Combine(tools, "worker-protocol.manifest"); NoLinks(manifest);
        string[] lines = File.ReadAllLines(manifest);
        string payload = string.Join("\n", files.Select(p => p.Key + "|" + p.Value));
        using var digest = SHA256.Create();
        if (lines.Length < 3 || lines[0] != "MCE-WORKER|2" || lines[1] != "build|" + Hex(digest.ComputeHash(Encoding.UTF8.GetBytes(payload))) || string.Join("\n", lines.Skip(2)) != payload)
            throw new IOException("Damaged or incompatible worker cache. No command submitted.");
    }
    internal static string Retry(string root, string tools, string expected, string expectedState)
    {
        string home = Path.Combine(root, "MCE");
        ValidateTools(tools);
        var task = ReadTask(home); // Revalidate at click, not the last UI snapshot.
        if (task.Id != expected || task.State != expectedState || task.Request.Operation != "confirm-switch" || (task.State != "failed" && task.State != "recovery-required"))
            throw new IOException("Task changed. No retry submitted.");
        string id = Guid.NewGuid().ToString("N");
        var command = new { SchemaVersion = 2, MinimumWorkerProtocol = 2, Id = id, Action = "pack", Pack = new { Operation = task.State == "recovery-required" ? "retry-recovery" : "retry-switch", PreviewId = expected } };
        string dir = Path.Combine(home, "commands"); NoLinks(dir); Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, DateTime.UtcNow.Ticks + "_" + id + ".json");
        string temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(file, new UTF8Encoding(false))) writer.Write(Json().Serialize(command));
        File.Move(temp, path);
        // A startup failure does NOT remove a queued request: another worker may consume it.
        try
        {
            using var worker = Process.Start(new ProcessStartInfo(Path.Combine(tools, "MCE.UpdateWorker.exe"), "--root " + Quote(root) + " --protocol 2")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = tools });
            if (worker == null) throw new IOException("Worker did not start.");
        }
        catch (Exception e) { throw new IOException("Retry was queued but worker launch failed. Do not submit again; check status or use recovery. " + e.Message); }
        return id;
    }
}
