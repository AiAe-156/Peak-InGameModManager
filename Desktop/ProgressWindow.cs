using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MCE.Desktop;
internal sealed class ProgressWindow : Window
{
    readonly string root, home, tools, language;
    string expected, notice = "", pendingAttempt = "", pendingPack = "";
    bool polling, submitting, pending, readOk;
    DateTime queued;
    TaskState? current;
    readonly TextBlock heading = new(), summary = new(), phase = new(), hint = new();
    readonly TextBox details = new();
    readonly ProgressBar bar = new();
    readonly Button retry = new(), recover = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    string T(string en, string cn, string tw) => language == "zh-cn" ? cn : language == "zh-tw" ? tw : en;

    internal ProgressWindow(string root, string tools, string id, string language)
    {
        this.root = root; home = Path.Combine(root, "MCE"); this.tools = tools; expected = id; this.language = language;
        Title = T("In-Game Mod Manager · Modpack switch", "游戏内模组管理器 · 整合包切换", "遊戲內模組管理器 · 整合包切換");
        Width = 880; Height = 640; MinWidth = 720; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(19, 25, 36));
        Foreground = Brushes.WhiteSmoke; FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"); FontSize = 14;
        var layout = new Grid { Margin = new Thickness(26) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            layout.RowDefinitions.Add(new RowDefinition { Height = height });
        Content = layout;
        heading.FontSize = 25; heading.FontWeight = FontWeights.SemiBold; heading.TextWrapping = TextWrapping.Wrap;
        Add(layout, heading, 0);
        summary.Margin = new Thickness(0, 12, 0, 14); summary.TextWrapping = TextWrapping.Wrap; Add(layout, summary, 1);
        bar.Height = 8; bar.Maximum = 100; bar.Foreground = new SolidColorBrush(Color.FromRgb(77, 176, 229)); Add(layout, bar, 2);
        phase.Margin = new Thickness(0, 12, 0, 12); phase.TextWrapping = TextWrapping.Wrap; phase.Foreground = Brushes.LightSteelBlue; Add(layout, phase, 3);
        details.IsReadOnly = true; details.AcceptsReturn = true; details.TextWrapping = TextWrapping.Wrap;
        details.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        details.Background = new SolidColorBrush(Color.FromRgb(28, 37, 51)); details.Foreground = Brushes.Gainsboro;
        details.BorderThickness = new Thickness(0); details.Padding = new Thickness(14); Add(layout, details, 4);
        var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) }; Add(layout, buttons, 5);
        Button Make(string caption, Action action)
        {
            var b = new Button { Content = caption, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 8), MinHeight = 36 };
            b.Click += (_, _) => { try { action(); } catch (Exception e) { ShowError(e); } }; buttons.Children.Add(b); return b;
        }
        retry = Make(T("Recheck and retry", "重新检查并重试", "重新檢查並重試"), Retry); retry.IsEnabled = false;
        Make(T("Copy details", "复制详情", "複製詳情"), () => Clipboard.SetText(Report()));
        Make(T("Export details", "导出详情", "匯出詳情"), Export);
        Make(T("Backups", "切换备份", "切換備份"), OpenBackups);
        recover = Make(T("Recovery only…", "仅恢复既有事务…", "僅復原既有交易…"), Recovery);
        hint.Text = T("Viewing does not start a worker. Closing does not cancel a queued or running task. Diagnostics are redacted.", "查看不会启动后台处理；关闭不会取消已排队或正在运行的任务。诊断内容已脱敏。", "檢視不會啟動背景處理；關閉不會取消已排隊或執行中的工作。診斷內容已去識別化。");
        hint.FontSize = 12; hint.Foreground = Brushes.LightSlateGray; hint.TextWrapping = TextWrapping.Wrap; Add(layout, hint, 6);
        heading.Text = T("Reading switch status…", "正在读取切换状态…", "正在讀取切換狀態…");
        timer.Tick += async (_, _) => await Poll();
        Loaded += async (_, _) => { await Poll(); timer.Start(); };
        Closing += OnClosing;
        Closed += (_, _) => timer.Stop();
    }
    static void Add(Grid grid, UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }
    string Safe(string value) => Protocol.Redact(value, root);
    string Report() => Safe(Title + "\r\n" + heading.Text + "\r\n" + summary.Text + "\r\n" + phase.Text + "\r\n" + details.Text);
    void ShowError(Exception error) { notice = Safe(error.GetType().Name + ": " + error.Message); details.Text = notice; }
    async Task Poll()
    {
        if (polling || submitting) return;
        polling = true;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                var task = Protocol.ReadTask(home, expected);
                bool installation = task.Request.Operation.StartsWith("local-", StringComparison.Ordinal) || task.Request.Operation.StartsWith("layout-", StringComparison.Ordinal);
                string planPath = installation ? Path.Combine(home, "shop", "tasks", task.Id, "transaction.json") : Path.Combine(home, "packs", ".transactions", task.Id, "switch.json");
                Protocol.NoLinks(planPath);
                var plan = File.Exists(planPath) ? Protocol.Read<PlanState>(planPath) : null;
                if (installation && plan != null) { plan.Originals = task.Originals; plan.Desired = task.Desired; }
                return new { Task = task, Plan = plan };
            });
            var task = snapshot.Task;
            // Retry-switch creates a new preview ID. Only follow while explicitly awaiting a
            // retry, for the same target pack and a fresh attempt. Unrelated tasks are never adopted.
            if (task.Id != expected)
            {
                if (pending && task.Request.Operation == "confirm-switch" && task.Proposed.Id == pendingPack && task.AttemptId.Length > 0 && task.AttemptId != pendingAttempt)
                    expected = task.Id;
                else throw new IOException(T("The task changed. Return to the manager; no action is available in this window.", "任务记录已变化，请返回管理器查看；本窗口不会处理新任务。", "工作記錄已變更，請返回管理器檢視；本視窗不會處理新工作。"));
            }
            current = task; readOk = true;
            if (pending && task.AttemptId.Length > 0 && task.AttemptId != pendingAttempt) { pending = false; notice = ""; }
            string state = task.State;
            heading.Text = state switch
            {
                "waiting-exit" => T("Waiting for PEAK to exit", "等待 PEAK 退出", "等待 PEAK 結束"),
                "committing" => T("Installing / organizing files", "正在安装／整理文件", "正在安裝／整理檔案"),
                "switching" or "saving" => T("Saving and switching", "正在保存并切换", "正在儲存並切換"),
                "completed" => T("Complete · You may start PEAK", "已完成 · 可以启动 PEAK", "已完成 · 可以啟動 PEAK"),
                "failed" => T("Task failed", "任务失败", "工作失敗"),
                "recovery-required" => T("Recovery incomplete · Do not start PEAK", "恢复未完成 · 请勿启动 PEAK", "復原未完成 · 請勿啟動 PEAK"),
                "cancelled" => T("Cancelled", "已取消", "已取消"),
                _ => T("Processing request", "正在处理请求", "正在處理請求")
            };
            heading.Foreground = state == "recovery-required" || state == "failed" ? Brushes.Salmon : state == "completed" ? Brushes.LightGreen : Brushes.WhiteSmoke;
            string elapsed = "—";
            if (DateTimeOffset.TryParse(task.AttemptStartedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var begin))
            {
                var end = DateTimeOffset.TryParse(task.AttemptEndedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var finished) ? finished : DateTimeOffset.UtcNow;
                elapsed = ((long)Math.Max(0, (end - begin).TotalSeconds)).ToString(CultureInfo.InvariantCulture) + " s";
            }
            summary.Text = Safe(task.Proposed.Name) + "\n" + T("Elapsed: ", "已用时：", "已用時：") + elapsed;
            var plan = snapshot.Plan;
            bool counted = plan != null && plan.Total > 0;
            bar.IsIndeterminate = !counted && state != "completed" && state != "failed" && state != "recovery-required" && state != "cancelled";
            bar.Value = state == "completed" ? 100 : counted ? Math.Min(99, 100.0 * plan!.Done / plan.Total) : 0;
            phase.Text = plan == null ? T("Preparation · No transaction file yet", "准备阶段 · 尚无事务文件", "準備階段 · 尚無交易檔案") :
                T("Transaction: ", "事务阶段：", "交易階段：") + plan.Phase + (counted ? "  ·  " + plan.Done + " / " + plan.Total + T(" file operations", " 次文件处理", " 次檔案處理") : "");
            if (plan?.Phase == "rolled-back") phase.Text += "\n" + T("Original environment restored. Backups retained.", "原环境已恢复，备份已保留。", "原環境已復原，備份已保留。");
            if (pending && (DateTime.UtcNow - queued).TotalSeconds > 15)
                notice = T("Retry has not been acknowledged. It may still be queued. Do not submit duplicates; check the manager/worker status or use recovery only.", "尚未收到重试确认，请求可能仍在队列中。请勿重复提交；检查管理器／后台状态或使用仅恢复入口。", "尚未收到重試確認，請求可能仍在佇列中。請勿重複提交；檢查管理器／背景狀態或使用僅復原入口。");
            // Worker diagnostics remain original-language evidence, not a misleading generic translation.
            string text = Safe(task.Message + "\r\n" + task.Error + (notice.Length > 0 ? "\r\n\r\n" + notice : ""));
            if (details.Text != text) { int start = details.SelectionStart, length = details.SelectionLength; details.Text = text; details.Select(Math.Min(start, text.Length), Math.Min(length, Math.Max(0, text.Length - start))); }
            retry.Content = state == "recovery-required" ? T("Retry recovery", "重试恢复原环境", "重試復原原環境") : T("Recheck and retry", "重新检查并重试", "重新檢查並重試");
            retry.IsEnabled = !pending && !submitting && task.Request.Operation == "confirm-switch" && (state == "failed" || state == "recovery-required");
        }
        catch (Exception e)
        {
            readOk = false; retry.IsEnabled = false; bar.IsIndeterminate = false;
            heading.Text = T("Status unavailable · No action taken", "状态读取失败 · 未执行操作", "狀態讀取失敗 · 未執行操作");
            ShowError(e);
        }
        finally { polling = false; }
    }
    async void Retry()
    {
        if (!readOk || current == null || pending || submitting) return;
        string state = current.State;
        string prompt = state == "recovery-required"
            ? T("Retry restoring the original environment? PEAK must exit first. Backups are retained.", "确认重试恢复原环境？必须先退出 PEAK；备份会保留。", "確認重試復原原環境？必須先結束 PEAK；備份會保留。")
            : T("Recheck and retry switching to this modpack after PEAK exits? This queues an explicit worker command.", "确认重新检查并在 PEAK 退出后重试切换到此整合包？这将提交后台命令。", "確認重新檢查並在 PEAK 結束後重試切換至此整合包？這將提交背景命令。");
        if (MessageBox.Show(this, prompt + "\n" + Safe(current.Proposed.Name), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        // Lock before background validation/publish. A launch error may occur AFTER enqueue.
        submitting = true; pending = true; queued = DateTime.UtcNow;
        pendingAttempt = current.AttemptId; pendingPack = current.Proposed.Id;
        retry.IsEnabled = false; recover.IsEnabled = false;
        try { await Task.Run(() => Protocol.Retry(root, tools, expected, state)); notice = T("Retry submitted; waiting for acknowledgment…", "已提交重试，等待后台确认…", "已提交重試，等待背景確認…"); }
        catch (Exception e) { ShowError(e); } // Keep pending: never silently resubmit after uncertain publication.
        finally { submitting = false; recover.IsEnabled = true; await Poll(); }
    }
    void Export()
    {
        var dialog = new SaveFileDialog { Filter = "Text (*.txt)|*.txt", FileName = "MCE-switch-diagnostics.txt", DefaultExt = ".txt", AddExtension = true };
        if (dialog.ShowDialog(this) == true) File.WriteAllText(dialog.FileName, Report(), new UTF8Encoding(false));
    }
    void OpenBackups()
    {
        if (!Protocol.ValidId(expected)) return;
        string path = Path.Combine(home, "packs", ".transactions", expected); Protocol.NoLinks(path);
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(T("No switch backup directory yet.", "尚未生成切换备份目录。", "尚未產生切換備份目錄。"));
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Process.Start(new ProcessStartInfo(explorer, Protocol.Quote(path)) { UseShellExecute = false })?.Dispose();
    }
    void Recovery()
    {
        if (MessageBox.Show(this, T("Open the recovery-only launcher? It lists existing unfinished transactions and asks for confirmation before recovery. It does not process the normal queue.", "打开仅恢复入口？它只列出既有未完成事务，恢复前还需明确确认，不处理正常命令队列。", "開啟僅復原入口？它只列出既有未完成交易，復原前仍需明確確認，不處理一般命令佇列。"), Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        string script = Path.Combine(home, "MCE-Recovery.ps1"); Protocol.NoLinks(script);
        if (!File.Exists(script)) throw new FileNotFoundException("Prepared recovery launcher not found.");
        string bootstrap = "& ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + Convert.ToBase64String(Encoding.UTF8.GetBytes(script)) + "')))";
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        Process.Start(new ProcessStartInfo(powershell, "-NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap))) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
    }
    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (submitting) { e.Cancel = true; return; }
        if (pending || !readOk || current?.State is "requested" or "waiting-exit" or "switching" or "saving" or "committing" or "recovery-required")
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
        }
    }
}
