using System.Globalization;
using System.Text;

namespace MCE.UpdateWorker;

/// <summary>Daily worker log under MCE/logs so failures can be read from disk instead of screenshots. Never throws.</summary>
internal static class WorkerLog
{
    static readonly object gate = new();
    static readonly TimeSpan Retention = TimeSpan.FromDays(14);
    static string? directory;

    internal static void Initialize(string home)
    {
        lock (gate)
        {
            try
            {
                directory = Path.Combine(home, "logs"); Directory.CreateDirectory(directory);
                foreach (string file in Directory.GetFiles(directory, "worker-*.log"))
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > Retention) InstallArchive.TryDeleteFile(file);
            }
            catch (Exception) { directory = null; }
        }
    }

    internal static string? Directory_ => directory;
    /// <summary>Technical layer; the plugin turns it on from its own config (default off) and passes --debug.</summary>
    internal static bool DebugEnabled { get; set; }
    internal static void Info(string message) => Write("INFO", message);
    internal static void Warn(string message) => Write("WARN", message);
    internal static void Error(string message, Exception? error = null) => Write("ERROR", error == null ? message : message + " :: " + error);
    internal static void Debug(string message) { if (DebugEnabled) Write("DEBUG", message); }

    static void Write(string level, string message)
    {
        string? target; lock (gate) target = directory;
        if (target == null) return;
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + level + "] " + message.Replace("\r\n", "\n").Replace("\n", "\n    ") + Environment.NewLine;
        lock (gate)
        {
            try { File.AppendAllText(Path.Combine(target, "worker-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log"), line, new UTF8Encoding(false)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
