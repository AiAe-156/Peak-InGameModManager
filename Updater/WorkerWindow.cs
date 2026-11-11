#if NETFRAMEWORK
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MCE.UpdateWorker;

internal sealed class WorkerWindow : IDisposable
{
    static readonly Brush WindowBackground = Frozen(19, 25, 36), PanelBackground = Frozen(28, 37, 51), ChipBackground = Frozen(24, 33, 46);
    static readonly Brush Accent = Frozen(77, 176, 229), Caution = Frozen(222, 198, 124), Muted = Frozen(150, 162, 178);
    static readonly Brush Danger = Brushes.Salmon, Good = Brushes.LightGreen, Idle = Brushes.LightSteelBlue;
    static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(60);

    readonly string root;
    readonly Thread thread;
    readonly ManualResetEventSlim ready = new(false);
    readonly Dictionary<string, Card> cards = new(StringComparer.Ordinal);
    Dispatcher? dispatcher;
    Window? window;
    TextBlock? summary, banner, statusLine;
    ProgressBar? overall;
    StackPanel? list;
    ScrollViewer? scroll;
    Button? stopButton, retryAllButton, clearButton;
    DispatcherTimer? timer;
    Exception? startupError;
    WorkerStatusSnapshot latest = new();
    string signature = "";
    DateTime signatureAt = DateTime.UtcNow;
    volatile bool stop, disposed, autoMinimized, finished;
    readonly ManualResetEventSlim closed = new(false);
    DateTime closeAt;
    volatile Action<WorkerControlRequest>? control;

    internal WorkerWindow(string root)
    {
        this.root = root;
        thread = new Thread(Run) { IsBackground = true, Name = "MCE worker window" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(10))) throw new IOException("Progress window could not be opened; worker processing was not started.");
        if (startupError != null) throw new IOException("Progress window could not be opened; worker processing was not started. " + startupError.Message, startupError);
    }

    internal bool StopRequested => stop;
    internal Action<WorkerControlRequest>? Control { get => control; set => control = value; }

    /// <summary>Active work keeps the window normal; while every task only waits for the game to exit it parks in the taskbar. Never steals focus.</summary>
    internal void SetAttention(bool active)
    {
        var target = dispatcher;
        if (target == null || target.HasShutdownStarted || disposed) return;
        try
        {
            target.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (window == null) return;
                if (!active && window.WindowState != WindowState.Minimized) { window.WindowState = WindowState.Minimized; autoMinimized = true; }
                else if (active && autoMinimized && window.WindowState == WindowState.Minimized) { window.WindowState = WindowState.Normal; autoMinimized = false; }
            }));
        }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) { }
    }

    internal void Publish(WorkerStatusSnapshot snapshot)
    {
        var target = dispatcher;
        if (target == null || target.HasShutdownStarted || disposed) return;
        try { target.BeginInvoke(DispatcherPriority.Normal, new Action(() => Apply(snapshot))); }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; stop = true;
        var target = dispatcher;
        if (target != null && !target.HasShutdownStarted)
        {
            try { target.BeginInvoke(DispatcherPriority.Send, new Action(() => { timer?.Stop(); window?.Close(); })); } catch (Exception) { }
            if (!thread.Join(TimeSpan.FromSeconds(3)))
            {
                try { target.BeginInvoke(DispatcherPriority.Send, new Action(target.InvokeShutdown)); } catch (Exception) { }
                thread.Join(TimeSpan.FromSeconds(2));
            }
        }
        else thread.Join(TimeSpan.FromSeconds(1));
        ready.Dispose(); closed.Dispose();
    }

    // No Application object: the worker is a console process and must keep owning process lifetime; a bare Dispatcher loop is enough for one window.
    void Run()
    {
        try
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            window = Build();
            window.Show();
            timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => RefreshBanner();
            timer.Start();
            ready.Set();
            Dispatcher.Run();
        }
        catch (Exception error)
        {
            startupError = error;
            ready.Set();
        }
    }

    static string T(string english, string simplified, string traditional) => WorkerLanguage.T(english, simplified, traditional);
    static string TitleText => T("MCE operations", "MCE 模组操作", "MCE 模組操作");

    Window Build()
    {
        var shell = new Window
        {
            Title = TitleText, Width = 880, Height = 540, MinWidth = 720, MinHeight = 420, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = WindowBackground, Foreground = Brushes.WhiteSmoke,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"), FontSize = 14
        };
        var layout = new Grid { Margin = new Thickness(14) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            layout.RowDefinitions.Add(new RowDefinition { Height = height });
        shell.Content = layout;

        var title = new TextBlock { Text = TitleText, FontSize = 19, FontWeight = FontWeights.SemiBold };
        Add(layout, title, 0);
        summary = new TextBlock { Margin = new Thickness(0, 4, 0, 6), Foreground = Muted, TextWrapping = TextWrapping.Wrap, Text = T("Starting…", "正在启动…", "正在啟動…") };
        Add(layout, summary, 1);
        overall = new ProgressBar { Height = 4, Maximum = 100, Foreground = Accent, Background = PanelBackground, BorderThickness = new Thickness(0), Visibility = Visibility.Collapsed };
        Add(layout, overall, 2);
        banner = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(10, 5, 10, 5), Background = Caution, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
            Text = T("No progress for 60 seconds. Review the task or stop after the current safe step.", "已超过60秒没有进度变化，请检查任务或点击停止。", "已超過60秒沒有進度變化，請檢查工作或點擊停止。")
        };
        Add(layout, banner, 3);
        list = new StackPanel();
        scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 6, 0, 0), Content = list };
        Add(layout, scroll, 4);
        statusLine = new TextBlock { Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        Add(layout, statusLine, 5);

        var footer = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        Add(layout, footer, 6);
        retryAllButton = Make(footer, T("Retry all failed", "重试全部失败项", "重試全部失敗項"), () => Raise("retry-all", ""));
        retryAllButton.IsEnabled = false;
        clearButton = Make(footer, T("Clear finished", "清空已结束", "清空已結束"), () => Raise("clear-finished", ""));
        clearButton.IsEnabled = false;
        stopButton = Make(footer, T("Stop after current safe step", "完成当前安全步骤后停止", "完成目前安全步驟後停止"), RequestStop);
        Make(footer, T("Open data folder", "打开数据目录", "開啟資料目錄"), () => OpenFolder(Path.Combine(root, "MCE")));
        Make(footer, T("Copy details", "复制详情", "複製詳情"), () => { try { Clipboard.SetText(Report()); Status(T("Copied.", "已复制。", "已複製。")); } catch (Exception e) { Status(e.Message); } });

        shell.Closing += (_, e) => { if (!disposed && !finished) { e.Cancel = true; RequestStop(); } };
        shell.Closed += (_, _) => closed.Set();
        shell.Closed += (_, _) => { timer?.Stop(); try { dispatcher?.InvokeShutdown(); } catch (Exception) { } };
        return shell;
    }

    static void Add(Grid grid, UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }

    Button Make(Panel parent, string caption, Action action)
    {
        var button = new Button { Content = caption, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6), MinHeight = 28 };
        button.Click += (_, _) => { try { action(); } catch (Exception e) { Status(e.Message); } };
        parent.Children.Add(button);
        return button;
    }

    /// <summary>Turns the window into the end-of-run result: done / failed counts, the cards for each job, a close button
    /// and an automatic close. Raised to the front: the game is gone, so this is the only place the user can see it.</summary>
    internal void ShowFinished(WorkerStatusSnapshot snapshot)
    {
        var target = dispatcher;
        if (target == null || target.HasShutdownStarted || disposed) return;
        try
        {
            target.Invoke(new Action(() =>
            {
                if (window == null) return;
                finished = true;
                Apply(snapshot);
                bool failed = snapshot.Failed > 0;
                window.Title = (failed ? T("Finished with problems", "已完成，有问题", "已完成，有問題") : T("Finished", "已完成", "已完成")) + " · " + TitleText;
                summary!.Text = (failed
                    ? T("Finished after the game exited. Some tasks did not complete; see below.", "退出游戏后的任务已执行完毕，部分未完成，详情见下方。", "結束遊戲後的工作已執行完畢，部分未完成，詳情見下方。")
                    : T("Finished after the game exited. Changes take effect next launch.", "退出游戏后的任务已全部完成，下次启动生效。", "結束遊戲後的工作已全部完成，下次啟動生效。")) + "  " + Describe(snapshot);
                summary.Foreground = failed ? Danger : Good;
                if (stopButton != null) { stopButton.IsEnabled = true; stopButton.Content = T("Close", "关闭", "關閉"); stopButton.Click += (_, _) => window?.Close(); }
                closeAt = DateTime.UtcNow + (failed ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(20));
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Topmost = true; window.Activate(); window.Topmost = false;
            }));
        }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) { }
    }
    internal void WaitClosedOrTimeout(TimeSpan limit) => closed.Wait(limit);

    void RequestStop()
    {
        if (finished) return;
        stop = true;
        if (stopButton == null) return;
        stopButton.IsEnabled = false;
        stopButton.Content = T("Stopping after the current safe step…", "将在当前安全步骤后停止…", "將在目前安全步驟後停止…");
    }

    void Raise(string action, string jobId)
    {
        var handler = control;
        if (handler == null) { Status(T("Not connected to the worker yet.", "尚未连接到后台处理。", "尚未連接到背景處理。")); return; }
        handler(new WorkerControlRequest { Action = action, JobId = jobId });
    }

    void Status(string text) { if (statusLine != null) statusLine.Text = text; }

    void OpenFolder(string path)
    {
        if (!Directory.Exists(path)) { Status(T("Folder not found: ", "目录不存在：", "目錄不存在：") + path); return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) { Status(e.Message); }
    }

    void Apply(WorkerStatusSnapshot snapshot)
    {
        if (window == null || list == null || disposed) return;
        latest = snapshot;
        summary!.Text = Describe(snapshot);
        var busy = snapshot.Jobs.Where(j => j.Busy).ToList();
        if (snapshot.Idle || busy.Count == 0) overall!.Visibility = Visibility.Collapsed;
        else
        {
            overall!.Visibility = Visibility.Visible;
            bool measurable = busy.Any(j => j.Total > 0);
            overall.IsIndeterminate = !measurable;
            if (measurable) overall.Value = busy.Average(j => Math.Max(0, Math.Min(1, j.Progress))) * 100;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < snapshot.Jobs.Count; index++)
        {
            var job = snapshot.Jobs[index];
            seen.Add(job.Id);
            if (!cards.TryGetValue(job.Id, out var card)) { card = BuildCard(job.Id); cards[job.Id] = card; }
            int at = list.Children.IndexOf(card.Frame);
            if (at < 0) list.Children.Insert(Math.Min(index, list.Children.Count), card.Frame);
            else if (at != index) { list.Children.RemoveAt(at); list.Children.Insert(Math.Min(index, list.Children.Count), card.Frame); }
            UpdateCard(card, job);
        }
        foreach (string id in cards.Keys.Where(k => !seen.Contains(k)).ToList()) { list.Children.Remove(cards[id].Frame); cards.Remove(id); }
        retryAllButton!.IsEnabled = snapshot.Jobs.Any(j => j.Failed && j.CanRetry);
        clearButton!.IsEnabled = snapshot.Jobs.Any(j => j.Failed || j.Done);
        if (snapshot.Notice.Length > 0) Status(WorkerLanguage.Localize(snapshot.Notice));
        RefreshBanner();
    }

    string Describe(WorkerStatusSnapshot snapshot)
    {
        if (snapshot.Jobs.Count == 0)
            return snapshot.Idle ? T("No active tasks. This window closes on its own.", "当前没有任务，窗口会自动关闭。", "目前沒有工作，視窗會自動關閉。") : T("Waiting for tasks…", "等待任务…", "等待工作…");
        var parts = new List<string>();
        if (snapshot.Active > 0) parts.Add(snapshot.Active + " " + T("running", "项进行中", "項進行中"));
        if (snapshot.Waiting > 0) parts.Add(snapshot.Waiting + " " + T("waiting for exit", "项等待退出", "項等待結束"));
        if (snapshot.Failed > 0) parts.Add(snapshot.Failed + " " + T("failed", "项失败", "項失敗"));
        if (snapshot.Done > 0) parts.Add(snapshot.Done + " " + T("done", "项完成", "項完成"));
        if (parts.Count == 0) parts.Add(snapshot.Jobs.Count + " " + T("tasks", "项任务", "項工作"));
        return string.Join(" · ", parts);
    }

    void RefreshBanner()
    {
        if (banner == null) return;
        if (finished)
        {
            int seconds = (int)Math.Max(0, Math.Ceiling((closeAt - DateTime.UtcNow).TotalSeconds));
            Status(T("Closes by itself in ", "窗口将在 ", "視窗將在 ") + seconds + T(" s.", " 秒后自动关闭。", " 秒後自動關閉。"));
            banner.Visibility = Visibility.Collapsed; return;
        }
        var snapshot = latest;
        string current = string.Join("|", snapshot.Jobs.Select(j => j.Id + ":" + j.State + ":" + j.Progress.ToString("F3", CultureInfo.InvariantCulture) + ":" + j.Downloaded));
        if (current != signature) { signature = current; signatureAt = DateTime.UtcNow; }
        bool stalled = snapshot.Jobs.Any(j => j.Busy) && DateTime.UtcNow - signatureAt > StallAfter;
        banner.Visibility = stalled ? Visibility.Visible : Visibility.Collapsed;
    }

    sealed class Card
    {
        internal Border Frame = null!, Chip = null!;
        internal TextBlock Name = null!, Version = null!, ChipText = null!, Bytes = null!, Message = null!, ErrorText = null!;
        internal ProgressBar Bar = null!;
        internal TextBox DetailsBox = null!;
        internal StackPanel WarningPanel = null!;
        internal Button Retry = null!, Remove = null!, Folder = null!, DetailsToggle = null!, WarningsToggle = null!;
        internal bool DetailsOpen, WarningsOpen, Busy;
        internal string Kind = "update", PackageRoot = "", Details = "", WarningsKey = "";
    }

    Card BuildCard(string id)
    {
        var card = new Card();
        var content = new StackPanel();
        card.Frame = new Border { Background = PanelBackground, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(9), Child = content };

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Orientation = Orientation.Horizontal };
        card.Name = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        card.Version = new TextBlock { Foreground = Muted, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(card.Name); heading.Children.Add(card.Version);
        head.Children.Add(heading);
        card.ChipText = new TextBlock { FontSize = 11, Padding = new Thickness(7, 2, 7, 2) };
        card.Chip = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11), Background = ChipBackground, Child = card.ChipText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(card.Chip, 1);
        head.Children.Add(card.Chip);
        content.Children.Add(head);

        card.Bar = new ProgressBar { Height = 4, Maximum = 100, Margin = new Thickness(0, 6, 0, 0), Foreground = Accent, Background = ChipBackground, BorderThickness = new Thickness(0) };
        content.Children.Add(card.Bar);
        card.Bytes = new TextBlock { Foreground = Muted, FontSize = 11, Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed };
        content.Children.Add(card.Bytes);
        card.Message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        content.Children.Add(card.Message);
        card.ErrorText = new TextBlock { Foreground = Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
        content.Children.Add(card.ErrorText);
        card.DetailsBox = new TextBox
        {
            IsReadOnly = true, IsReadOnlyCaretVisible = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 150,
            Background = ChipBackground, Foreground = Brushes.Gainsboro, BorderThickness = new Thickness(0), Padding = new Thickness(10), FontFamily = new FontFamily("Consolas"), FontSize = 12,
            Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed
        };
        content.Children.Add(card.DetailsBox);
        card.WarningPanel = new StackPanel { Margin = new Thickness(0, 3, 0, 0), Visibility = Visibility.Collapsed };
        content.Children.Add(card.WarningPanel);

        var buttons = new WrapPanel { Margin = new Thickness(0, 5, 0, -6) };
        card.Retry = Make(buttons, T("Retry", "重试", "重試"), () => Raise(card.Kind == "operation" ? "operation-retry" : "retry", id));
        card.Remove = Make(buttons, T("Remove", "移出队列", "移出佇列"), () => Raise(card.Kind == "operation" ? (card.Busy ? "operation-cancel" : "operation-remove") : "remove", id));
        card.Folder = Make(buttons, T("Open folder", "打开目录", "開啟目錄"), () => OpenFolder(card.PackageRoot));
        card.DetailsToggle = Make(buttons, T("Details", "详情", "詳情"), () => { card.DetailsOpen = !card.DetailsOpen; card.DetailsBox.Visibility = card.DetailsOpen ? Visibility.Visible : Visibility.Collapsed; });
        card.DetailsToggle.Visibility = Visibility.Collapsed;
        card.WarningsToggle = Make(buttons, "", () => { card.WarningsOpen = !card.WarningsOpen; card.WarningPanel.Visibility = card.WarningsOpen ? Visibility.Visible : Visibility.Collapsed; });
        card.WarningsToggle.Visibility = Visibility.Collapsed;
        content.Children.Add(buttons);
        return card;
    }

    void UpdateCard(Card card, JobView job)
    {
        card.Kind = job.Kind; card.PackageRoot = job.PackageRoot; card.Busy = job.Busy;
        card.Name.Text = job.Name.Length > 0 ? job.Name : job.Id;
        bool fresh = job.Kind == "install" || job.Kind == "dependency" || job.Kind == "import" || job.FromVersion.Length == 0 || job.FromVersion == "0.0.0";
        card.Version.Text = job.Kind == "uninstall" ? T("uninstall ", "卸载 ", "解除安裝 ") + job.FromVersion : fresh ? T("install ", "安装 ", "安裝 ") + job.ToVersion : job.FromVersion + " → " + job.ToVersion;
        var tone = job.Failed ? Danger : job.Done ? Good : job.Busy ? Accent : job.Waiting ? Idle : Muted;
        card.Chip.BorderBrush = tone; card.ChipText.Foreground = tone;
        card.ChipText.Text = job.StateText.Length > 0 ? job.StateText : WorkerLanguage.StateText(job.State);

        if (job.Done || job.Failed) card.Bar.Visibility = Visibility.Collapsed;
        else
        {
            card.Bar.Visibility = Visibility.Visible;
            card.Bar.IsIndeterminate = job.Busy && job.Total <= 0;
            if (!card.Bar.IsIndeterminate) card.Bar.Value = Math.Max(0, Math.Min(1, job.Progress)) * 100;
        }
        if (job.Total > 0) { card.Bytes.Visibility = Visibility.Visible; card.Bytes.Text = HumanBytes(job.Downloaded) + " / " + HumanBytes(job.Total); }
        else card.Bytes.Visibility = Visibility.Collapsed;

        string message = WorkerLanguage.Localize(job.Message);
        card.Message.Text = message; card.Message.Visibility = message.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        string error = WorkerLanguage.Localize(job.Error);
        if (error.Length > 0) { card.ErrorText.Text = error.Split('\n')[0]; card.ErrorText.Visibility = Visibility.Visible; }
        else card.ErrorText.Visibility = Visibility.Collapsed;
        string details = job.Details.Length > 0 ? WorkerLanguage.Localize(job.Details) : error;
        if (details != card.Details) { card.Details = details; card.DetailsBox.Text = details; }
        card.DetailsToggle.Visibility = details.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        card.DetailsBox.Visibility = details.Length > 0 && card.DetailsOpen ? Visibility.Visible : Visibility.Collapsed;

        string warningsKey = string.Join("\n", job.Warnings);
        if (warningsKey != card.WarningsKey)
        {
            card.WarningsKey = warningsKey;
            card.WarningPanel.Children.Clear();
            foreach (string warning in job.Warnings)
                card.WarningPanel.Children.Add(new TextBlock { Text = "• " + WorkerLanguage.Localize(warning), Foreground = Caution, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
            card.WarningsToggle.Content = T(job.Warnings.Count + " notes", job.Warnings.Count + " 条提示", job.Warnings.Count + " 條提示");
        }
        card.WarningsToggle.Visibility = job.Warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        card.WarningPanel.Visibility = job.Warnings.Count > 0 && card.WarningsOpen ? Visibility.Visible : Visibility.Collapsed;

        card.Retry.IsEnabled = job.CanRetry;
        card.Remove.IsEnabled = job.CanRemove;
        card.Folder.IsEnabled = job.PackageRoot.Length > 0 && Directory.Exists(job.PackageRoot);
    }

    string Report()
    {
        var text = new StringBuilder();
        text.AppendLine(TitleText).AppendLine(summary?.Text ?? "");
        foreach (var job in latest.Jobs)
        {
            text.AppendLine().AppendLine("■ " + job.Name + "  " + job.FromVersion + " → " + job.ToVersion + "  [" + (job.StateText.Length > 0 ? job.StateText : WorkerLanguage.StateText(job.State)) + "]");
            if (job.Message.Length > 0) text.AppendLine(WorkerLanguage.Localize(job.Message));
            if (job.Error.Length > 0) text.AppendLine(WorkerLanguage.Localize(job.Error));
            if (job.Details.Length > 0) text.AppendLine(WorkerLanguage.Localize(job.Details));
            foreach (string warning in job.Warnings) text.AppendLine("- " + WorkerLanguage.Localize(warning));
        }
        return text.ToString();
    }

    static string HumanBytes(long value)
    {
        if (value < 1024) return value.ToString(CultureInfo.InvariantCulture) + " B";
        double kilo = value / 1024.0;
        if (kilo < 1024) return kilo.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        double mega = kilo / 1024.0;
        if (mega < 1024) return mega.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        return (mega / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
    }

    static Brush Frozen(byte r, byte g, byte b) { var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush; }
}
#endif
