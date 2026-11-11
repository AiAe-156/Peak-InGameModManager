// Standalone harness for the mods.yml follow-up write. It links the real ManagerProfileSync and only supplies
// the two things that are not available outside the game: BepInEx's chosen root and the manager log.
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;

namespace BepInEx
{
    internal static class Paths
    {
        internal static string BepInExRootPath = "";
    }
}

namespace ModConfigEnhance.Overview
{
    // 2.3.2: ManagerProfileSync writes mods.yml through the repository's atomic writer.
    internal static class OverviewRepository
    {
        internal static void AtomicWrite(string file, string content)
        {
            string temporary = file + ".tmp"; File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temporary, file, file + ".bak"); else File.Move(temporary, file);
        }
    }
    internal static class Diagnostics
    {
        internal static class ManagerLog
        {
            internal static void Info(string message) => Console.WriteLine("[info] " + message);
            internal static void Warn(string message) => Console.WriteLine("[warn] " + message);
        }
    }
}

internal static class Harness
{
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        BepInEx.Paths.BepInExRootPath = Path.Combine(root, "BepInEx");
        // --existing: keep the pending file already on disk, so a file the *worker* serialised can be read back.
        bool keep = args.Contains("--existing");
        if (!keep)
        {
            var pending = new ManagerSyncPending
            {
                Records =
                {
                    new ManagerSyncRecord { Key = "AiAeT-BetterPeakVoiceFix", Action = ManagerSyncAction.Version, Version = "9.9.9" },
                    new ManagerSyncRecord { Key = "AtomicStudio-Profile_Cards", Action = ManagerSyncAction.Remove },
                    new ManagerSyncRecord { Key = "Nobody-NotInstalled", Action = ManagerSyncAction.Remove }
                }
            };
            Directory.CreateDirectory(Path.Combine(root, "BepInEx", "MCE"));
            File.WriteAllText(ManagerProfileSync.PendingPath, JsonConvert.SerializeObject(pending, Formatting.Indented));
        }
        ManagerProfileSync.RunningProbe = () => false; // The manager may well be running on this machine; the gate itself is production behaviour.
        string configured = args.Length > 2 ? args[2] : "";
        Console.WriteLine("resolved profile: '" + ManagerProfileSync.Resolve(configured) + "'");
        ManagerProfileSync.Apply(configured);
        Console.WriteLine("pending still present: " + File.Exists(ManagerProfileSync.PendingPath));
        return 0;
    }
}
