using MCE.UpdateWorker;

try
{
    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
    Console.Error.Flush();
    int language = Array.IndexOf(args, "--language");
    if (language >= 0 && language + 1 < args.Length) WorkerLanguage.Set(args[language + 1]);
    WorkerLog.DebugEnabled = args.Contains("--debug");
    if(args.Length>=1&&args[0]=="--foreground-self-test"){
        using var view=new WorkerWindow("Isolated UI test");
        view.Publish(new WorkerStatusSnapshot{Root="Isolated UI test",Jobs={new JobView{Id="1",Name="Foreground window verification",ToVersion="1.0.0",State="downloading",StateText=WorkerLanguage.StateText("downloading"),Message="self-test",Progress=0.4,Downloaded=4,Total=10,Busy=true}},Active=1});
        await System.Threading.Tasks.Task.Delay(2000);return 0;
    }
    if (args.Length == 2 && args[0] == "--write-protocol-manifest")
    { ModConfigEnhance.Updating.WorkerProtocol.WriteManifest(Path.GetFullPath(args[1])); return 0; }
    if (args.Length == 2 && args[0] == "--validate-protocol-manifest")
    { Console.WriteLine(ModConfigEnhance.Updating.WorkerProtocol.ValidateTools(Path.GetFullPath(args[1]))); return 0; }
    if (args.Length == 1 && args[0] == "--protocol")
    { Console.WriteLine("MCE worker protocol=2 build=" + WorkerRecovery.BuildIdentity); return 0; }
    int index = Array.IndexOf(args, "--root");
    if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("用法：MCE.UpdateWorker.exe --root <BepInEx目录> [--protocol 2 --once | --recovery-status | --recover [--journal kind:id]] [--language zh-cn|zh-tw|en-us]");
    string root = Path.GetFullPath(args[index + 1]).TrimEnd('\\', '/');
    // Optional: the real game folder. Absent it the parent of --root is used, which is only correct for a
    // plain local installation; a manager profile lives elsewhere and would otherwise be mistaken for it.
    int gameRootIndex = Array.IndexOf(args, "--game-root");
    if (gameRootIndex >= 0)
    {
        if (gameRootIndex + 1 >= args.Length) throw new ArgumentException("--game-root 后需要游戏安装目录。");
        Files.GameRoot = Path.GetFullPath(args[gameRootIndex + 1]).TrimEnd('\\', '/');
        if (!Directory.Exists(Files.GameRoot)) throw new ArgumentException("--game-root 目录不存在：" + Files.GameRoot);
    }
    bool statusOnly = args.Contains("--recovery-status"), recover = args.Contains("--recover");
    if (statusOnly || recover)
    {
        if (statusOnly && recover || args.Contains("--probe") || args.Contains("--once")) throw new ArgumentException("Recovery mode cannot be combined with normal processing.");
        int selected = Array.IndexOf(args, "--journal");
        if (selected >= 0 && selected + 1 >= args.Length) throw new ArgumentException("--journal requires kind:id or exact journal path.");
        QueueWorker.RecoveryOnly(root, selected >= 0 ? args[selected + 1] : null, statusOnly); return 0;
    }
    WorkerRecovery.ValidateIndex(root);
    int handshake = Array.IndexOf(args, "--protocol");
    if (handshake < 0 || handshake + 1 >= args.Length || args[handshake + 1] != "2") throw new ArgumentException("Normal processing requires explicit --protocol 2.");
    WorkerTargetProtocol.RequireCachedExecution(root);
    int probeIndex = Array.IndexOf(args, "--probe");
    if (probeIndex >= 0)
    {
        if (probeIndex + 1 >= args.Length) throw new ArgumentException("--probe 后需要 32 位十六进制编号。");
        await NetworkProbe.Run(root, args[probeIndex + 1]);
        return 0;
    }
    WorkerTargetProtocol.Initialize();
    NativeConsole.FreeConsole();
    using var worker = new QueueWorker(root);
    WorkerLog.Info("args: " + string.Join(" ", args));
    // The window exists only while there is real work (downloading, installing, waiting for the game to exit);
    // heartbeat-only runs (observe, network, cleanup) stay invisible and exit on their own.
    WorkerWindow? foreground = null; var windowGate = new object();
    QueueWorker.ForegroundStatus = snapshot =>
    {
        // A window is only useful when it can be seen: while PEAK is running (fullscreen) the in-game queue is
        // the place to watch, so the standalone window appears when the game is closed and files are being moved.
        bool needed = snapshot.Jobs.Any(j => j.Busy || j.Waiting) && !Files.PeakRunning();
        lock (windowGate)
        {
            if (foreground == null && needed)
            {
                try { foreground = new WorkerWindow(root) { Control = worker.HandleControl }; WorkerLog.Info("window shown"); }
                catch (Exception error) { WorkerLog.Error("window could not be shown; continuing without it", error); return; }
            }
            if (foreground == null) return;
            foreground.Publish(snapshot);
        }
    };
    worker.StopRequested = () => { lock (windowGate) return foreground?.StopRequested == true; };
    try
    {
        await worker.Run(args.Contains("--once"));
        // Tell the user what this run changed on disk. Work that waited for the game to exit runs while PEAK is still
        // shutting down (no window is opened over a fullscreen game), so it used to finish without any sign at all.
        var finished = worker.FinishedSummary();
        if (finished.Jobs.Count > 0 && !Files.PeakRunning() && !args.Contains("--once"))
        {
            WorkerWindow? summary;
            lock (windowGate) { summary = foreground; foreground = null; }
            try
            {
                summary ??= new WorkerWindow(root) { Control = worker.HandleControl };
                WorkerLog.Info("finished summary shown: " + finished.Done + " done, " + finished.Failed + " failed");
                summary.ShowFinished(finished);
                summary.WaitClosedOrTimeout(finished.Failed > 0 ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(20));
            }
            catch (Exception error) { WorkerLog.Error("finished summary could not be shown", error); }
            finally { summary?.Dispose(); }
        }
    }
    finally { lock (windowGate) { foreground?.Dispose(); foreground = null; } }
    return 0;
}
catch (ModConfigEnhance.Updating.InstallationWriteLease.WorkerAlreadyRunningException)
{
    // Another launcher won the race. Its worker consumes the durable commands.
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    WorkerLog.Error("fatal", error);
    // The game relaunches the helper every 10 seconds while work is pending. A modal box raised before the instance lock
    // is taken would stack one stuck, hidden-behind-fullscreen process per launch, so at most one may ever be on screen;
    // the error is still recorded in worker-error.json and the log for the in-game queue to show.
    if (!args.Contains("--write-protocol-manifest") && !args.Contains("--validate-protocol-manifest"))
    {
        System.Threading.Mutex? dialog = null; bool owned = false;
        try { dialog = new System.Threading.Mutex(true, @"Local\MCE.UpdateWorker.FatalDialog", out owned); } catch (Exception) { }
        try { if (owned) System.Windows.Forms.MessageBox.Show(error.Message, "In-Game Mod Manager", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error); }
        finally { if (owned) dialog!.ReleaseMutex(); dialog?.Dispose(); }
    }
    try
    {
        int index = Array.IndexOf(args, "--root");
        if (!args.Contains("--probe") && !args.Contains("--recover") && !args.Contains("--recovery-status") && index >= 0 && index + 1 < args.Length)
        {
            string root = Path.GetFullPath(args[index + 1]).TrimEnd('\\', '/');
            if (Directory.Exists(Path.Combine(root, "plugins")))
            {
                string home = Path.Combine(root, "MCE"); Files.RejectLinks(Directory.GetParent(root)!.FullName, home);
                Files.Save(Path.Combine(home, "worker-error.json"), new { Message = ModConfigEnhance.Updating.UpdateMessages.Encode(error.Message), UpdatedUtc = DateTime.UtcNow.ToString("O") });
            }
        }
    }
    catch { }
    return 1;
}

// The worker is a console-subsystem exe so --protocol/--recovery-status stay scriptable; the interactive mode drops the console so only the WPF window shows.
static class NativeConsole
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    internal static extern bool FreeConsole();
}
