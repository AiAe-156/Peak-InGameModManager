#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>Launches the cached standalone viewer; viewing never starts queue processing.</summary>
internal static class PackSwitchProgressWindow
{
    internal static void ShowRecovery() => RecoveryLauncher.Show(PackLibrary.Home);
    internal static void Show(string previewId, bool recoveryOnly = false)
    {
        if (recoveryOnly) { ShowRecovery(); return; }
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || !PackLibrary.ValidId(previewId)) return;
        // Prepare the complete immutable tools cache before the pack can remove its host files.
        string tools = OverviewUpdateQueue.PrepareWorkerTools();
        string viewer = Path.Combine(tools, "MCE.Desktop.exe");
        WorkerProtocol.RejectLinks(Path.GetDirectoryName(BepInEx.Paths.BepInExRootPath.TrimEnd('\\', '/'))!, viewer);
        if (!File.Exists(viewer)) throw new IOException("MCE progress viewer is missing. Reinstall the complete Tools/Updater bundle; recovery remains available.");
        string root = BepInEx.Paths.BepInExRootPath.TrimEnd('\\', '/');
        if (root.IndexOf('"') >= 0) throw new IOException("Invalid progress root.");
        string language = Loc.T("en-us", "zh-cn", "zh-tw");
        Process.Start(new ProcessStartInfo(viewer, "--root \"" + root + "\" --task " + previewId + " --language " + language)
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = tools })?.Dispose();
    }
}
