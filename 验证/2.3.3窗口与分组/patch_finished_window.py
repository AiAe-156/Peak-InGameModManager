import io
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Updater")


def edit(name, pairs):
    path = os.path.join(ROOT, name)
    s = io.open(path, encoding="utf-8", newline="").read()
    nl = "\r\n" if "\r\n" in s else "\n"
    for a, b in pairs:
        a = a.replace("\n", nl)
        b = b.replace("\n", nl)
        n = s.count(a)
        assert n == 1, (name, n, a[:80])
        s = s.replace(a, b)
    io.open(path, "w", encoding="utf-8", newline="").write(s)


# QueueWorker: remember which jobs this process moved/installed/removed/rolled back files for, so the end of the run can
# tell the user what happened. Work done while the game was closing used to finish with no sign at all.
edit("QueueWorker.cs", [
    ("    void State(UpdateJob job, string state, string message, string error = \"\")\n"
     "    {\n"
     "        lock (gate)\n"
     "        {\n"
     "            ReadPlan(job); string previous = job.State;",
     "    /// <summary>Jobs whose files this process changed (deploy or rollback), for the end-of-run notice.</summary>\n"
     "    internal readonly HashSet<string> WroteFiles = new(StringComparer.Ordinal);\n"
     "    void State(UpdateJob job, string state, string message, string error = \"\")\n"
     "    {\n"
     "        lock (gate)\n"
     "        {\n"
     "            if (state is UpdateStates.Deploying or UpdateStates.RollingBack) WroteFiles.Add(job.Id);\n"
     "            ReadPlan(job); string previous = job.State;"),
    ("    void PublishStatus()\n"
     "    {\n"
     "        if (ForegroundStatus == null) return;",
     "    /// <summary>The finished state of every job this run wrote files for, with the same text the window cards use.</summary>\n"
     "    internal WorkerStatusSnapshot FinishedSummary()\n"
     "    {\n"
     "        var view = new WorkerStatusSnapshot { Root = root, UpdatedUtc = DateTime.UtcNow, Idle = true };\n"
     "        lock (gate) foreach (var job in snapshot.Jobs.Where(j => WroteFiles.Contains(j.Id))) view.Jobs.Add(View(job));\n"
     "        view.Failed = view.Jobs.Count(j => j.Failed); view.Done = view.Jobs.Count(j => j.Done); view.Waiting = view.Jobs.Count(j => j.Waiting);\n"
     "        return view;\n"
     "    }\n"
     "    void PublishStatus()\n"
     "    {\n"
     "        if (ForegroundStatus == null) return;"),
    ("        foreach (var job in jobs)\n"
     "        {\n"
     "            bool busy = UpdateStates.Busy(job.State) || job.State == UpdateStates.Queued;\n"
     "            view.Jobs.Add(new JobView\n",
     "        foreach (var job in jobs) view.Jobs.Add(View(job));\n"
     "        view.Jobs.AddRange(IndependentViews());\n"
     "        view.Active = view.Jobs.Count(j => j.Busy); view.Waiting = view.Jobs.Count(j => j.Waiting); view.Failed = view.Jobs.Count(j => j.Failed); view.Done = view.Jobs.Count(j => j.Done);\n"
     "        view.Idle = active == null && preparation == null && view.Active == 0;\n"
     "        view.Notice = notice.Length > 0 ? WorkerLanguage.Localize(notice) : \"\";\n"
     "        ForegroundStatus(view);\n"
     "    }\n"
     "    static JobView View(UpdateJob job)\n"
     "    {\n"
     "            bool busy = UpdateStates.Busy(job.State) || job.State == UpdateStates.Queued;\n"
     "            return new JobView\n"),
    ("                CanRetry = job.State == UpdateStates.Failed || job.State == UpdateStates.Stopped || job.State == UpdateStates.Paused, CanRemove = !UpdateStates.Busy(job.State)\n"
     "            });\n"
     "        }\n"
     "        view.Jobs.AddRange(IndependentViews());\n"
     "        view.Active = view.Jobs.Count(j => j.Busy); view.Waiting = view.Jobs.Count(j => j.Waiting); view.Failed = view.Jobs.Count(j => j.Failed); view.Done = view.Jobs.Count(j => j.Done);\n"
     "        view.Idle = active == null && preparation == null && view.Active == 0;\n"
     "        view.Notice = notice.Length > 0 ? WorkerLanguage.Localize(notice) : \"\";\n"
     "        ForegroundStatus(view);\n"
     "    }",
     "                CanRetry = job.State == UpdateStates.Failed || job.State == UpdateStates.Stopped || job.State == UpdateStates.Paused, CanRemove = !UpdateStates.Busy(job.State)\n"
     "            };\n"
     "    }"),
])

# Program: if this run wrote files, the user gets told, even when the game was still closing while the work ran and no
# progress window could be shown. The window then stays as a result view and closes on its own.
edit("Program.cs", [
    ("    try { await worker.Run(args.Contains(\"--once\")); }\n"
     "    finally { lock (windowGate) { foreground?.Dispose(); foreground = null; } }",
     "    try\n"
     "    {\n"
     "        await worker.Run(args.Contains(\"--once\"));\n"
     "        // Tell the user what this run changed on disk. Work that waited for the game to exit runs while PEAK is still\n"
     "        // shutting down (no window is opened over a fullscreen game), so it used to finish without any sign at all.\n"
     "        var finished = worker.FinishedSummary();\n"
     "        if (finished.Jobs.Count > 0 && !Files.PeakRunning() && !args.Contains(\"--once\"))\n"
     "        {\n"
     "            WorkerWindow? summary;\n"
     "            lock (windowGate) { summary = foreground; foreground = null; }\n"
     "            try\n"
     "            {\n"
     "                summary ??= new WorkerWindow(root) { Control = worker.HandleControl };\n"
     "                WorkerLog.Info(\"finished summary shown: \" + finished.Done + \" done, \" + finished.Failed + \" failed\");\n"
     "                summary.ShowFinished(finished);\n"
     "                summary.WaitClosedOrTimeout(finished.Failed > 0 ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(20));\n"
     "            }\n"
     "            catch (Exception error) { WorkerLog.Error(\"finished summary could not be shown\", error); }\n"
     "            finally { summary?.Dispose(); }\n"
     "        }\n"
     "    }\n"
     "    finally { lock (windowGate) { foreground?.Dispose(); foreground = null; } }"),
])

# WorkerWindow: a result view. Title and summary say it is done, the stop button becomes a close button, closing the
# window really closes it, and a countdown shows when it will go away by itself (failures keep it open longer).
edit("WorkerWindow.cs", [
    ("    volatile bool stop, disposed, autoMinimized;",
     "    volatile bool stop, disposed, autoMinimized, finished;\n"
     "    readonly ManualResetEventSlim closed = new(false);\n"
     "    DateTime closeAt;"),
    ("        shell.Closing += (_, e) => { if (!disposed) { e.Cancel = true; RequestStop(); } };",
     "        shell.Closing += (_, e) => { if (!disposed && !finished) { e.Cancel = true; RequestStop(); } };\n"
     "        shell.Closed += (_, _) => closed.Set();"),
    ("    void RequestStop()\n"
     "    {\n"
     "        stop = true;",
     "    /// <summary>Turns the window into the end-of-run result: done / failed counts, the cards for each job, a close button\n"
     "    /// and an automatic close. Raised to the front: the game is gone, so this is the only place the user can see it.</summary>\n"
     "    internal void ShowFinished(WorkerStatusSnapshot snapshot)\n"
     "    {\n"
     "        var target = dispatcher;\n"
     "        if (target == null || target.HasShutdownStarted || disposed) return;\n"
     "        try\n"
     "        {\n"
     "            target.Invoke(new Action(() =>\n"
     "            {\n"
     "                if (window == null) return;\n"
     "                finished = true;\n"
     "                Apply(snapshot);\n"
     "                bool failed = snapshot.Failed > 0;\n"
     "                window.Title = (failed ? T(\"Finished with problems\", \"已完成，有问题\", \"已完成，有問題\") : T(\"Finished\", \"已完成\", \"已完成\")) + \" · \" + TitleText;\n"
     "                summary!.Text = (failed\n"
     "                    ? T(\"Finished after the game exited. Some tasks did not complete; see below.\", \"退出游戏后的任务已执行完毕，部分未完成，详情见下方。\", \"結束遊戲後的工作已執行完畢，部分未完成，詳情見下方。\")\n"
     "                    : T(\"Finished after the game exited. Changes take effect next launch.\", \"退出游戏后的任务已全部完成，下次启动生效。\", \"結束遊戲後的工作已全部完成，下次啟動生效。\")) + \"  \" + Describe(snapshot);\n"
     "                summary.Foreground = failed ? Danger : Good;\n"
     "                if (stopButton != null) { stopButton.IsEnabled = true; stopButton.Content = T(\"Close\", \"关闭\", \"關閉\"); stopButton.Click += (_, _) => window?.Close(); }\n"
     "                closeAt = DateTime.UtcNow + (failed ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(20));\n"
     "                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;\n"
     "                window.Topmost = true; window.Activate(); window.Topmost = false;\n"
     "            }));\n"
     "        }\n"
     "        catch (TaskCanceledException) { }\n"
     "        catch (InvalidOperationException) { }\n"
     "    }\n"
     "    internal void WaitClosedOrTimeout(TimeSpan limit) => closed.Wait(limit);\n"
     "\n"
     "    void RequestStop()\n"
     "    {\n"
     "        if (finished) return;\n"
     "        stop = true;"),
    ("    void RefreshBanner()\n"
     "    {\n"
     "        if (banner == null) return;",
     "    void RefreshBanner()\n"
     "    {\n"
     "        if (banner == null) return;\n"
     "        if (finished)\n"
     "        {\n"
     "            int seconds = (int)Math.Max(0, Math.Ceiling((closeAt - DateTime.UtcNow).TotalSeconds));\n"
     "            Status(T(\"Closes by itself in \", \"窗口将在 \", \"視窗將在 \") + seconds + T(\" s.\", \" 秒后自动关闭。\", \" 秒後自動關閉。\"));\n"
     "            banner.Visibility = Visibility.Collapsed; return;\n"
     "        }"),
    ("        ready.Dispose();\n    }",
     "        ready.Dispose(); closed.Dispose();\n    }"),
])
print("ok")
