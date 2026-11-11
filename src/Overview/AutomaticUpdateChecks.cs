#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace ModConfigEnhance.Overview;
internal sealed class AutomaticUpdateChecks : MonoBehaviour
{
    private CancellationTokenSource? _cancel;
    private Task<ThunderstoreUpdateService.CheckProgress>? _task;
    private float _next, _poll;
    private static readonly object StateGate = new();
    private static long _stateRevision;
    private static bool _isChecking;
    private static DateTime _lastSuccessUtc, _nextCheckUtc;
    private static string _lastError = "";
    private static ThunderstoreUpdateService.CheckProgress? _lastReport;
    // Building the repository reloads the profile store and scans every installed assembly — the whole cost of a
    // check. A check cancelled by leaving a safe scene used to discard that and pay for it again on the next
    // attempt, so the preparation is kept only across retries and dropped as soon as one completes normally.
    private static (OverviewRepository Repo, System.Collections.Generic.IReadOnlyList<OverviewMod> Mods)? _prepared;
    internal static long StateRevision => Interlocked.Read(ref _stateRevision);
    internal static bool IsChecking { get { lock (StateGate) return _isChecking; } }
    internal static DateTime LastSuccessUtc { get { lock (StateGate) return _lastSuccessUtc; } }
    internal static DateTime NextCheckUtc { get { lock (StateGate) return _nextCheckUtc; } }
    internal static string LastError { get { lock (StateGate) return _lastError; } }
    internal static ThunderstoreUpdateService.CheckProgress? LastReport { get { lock (StateGate) return _lastReport?.Copy(); } }
    internal static string StatusText()
    {
        lock (StateGate)
        {
            string text = _isChecking ? Loc.T("Automatic update check in progress.", "正在自动检查更新。", "正在自動檢查更新。")
                : OverviewOptions.AutomaticUpdates.Value ? Loc.T("Automatic checks run every 30 minutes in the main menu or Airport.", "每30分钟在主菜单或机场候机厅自动检查更新。", "每30分鐘在主選單或機場候機廳自動檢查更新。")
                : Loc.T("Automatic checks are off.", "自动检查已关闭。", "自動檢查已關閉。");
            if (_lastReport != null) text += "\n" + _lastReport.Summary;
            if (!_isChecking && _lastError.Length > 0)
                text += "\n" + Loc.T("Check failed: ", "检查失败：", "檢查失敗：") + Loc.LocalizeMessage(_lastError);
            if (_lastSuccessUtc != default) text += "\n" + Loc.F("Last successful check: {0}", "最近成功检查：{0}", "最近成功檢查：{0}", _lastSuccessUtc.ToLocalTime().ToString("g"));
            if (!_isChecking && OverviewOptions.AutomaticUpdates.Value && _nextCheckUtc != default)
                text += "\n" + Loc.F("Next automatic check: {0} (when in a safe scene)", "下次自动检查：{0}（处于安全场景时）", "下次自動檢查：{0}（處於安全場景時）", _nextCheckUtc.ToLocalTime().ToString("g"));
            return text;
        }
    }
    internal static bool SafeScene => Allowed(SceneManager.GetActiveScene().name);
    internal static bool Allowed(string scene) => scene.Equals("Title",StringComparison.OrdinalIgnoreCase)||scene.Equals("MainMenu",StringComparison.OrdinalIgnoreCase)||scene.Equals("Airport",StringComparison.OrdinalIgnoreCase);
    private void Update()
    {
        // Always observe completion, including after leaving an allowed scene.
        if (_task != null && _task.IsCompleted)
        {
            var report = _task.IsCanceled ? new ThunderstoreUpdateService.CheckProgress { Cancelled = true, FinishedUtc = DateTime.UtcNow }
                : _task.IsFaulted ? new ThunderstoreUpdateService.CheckProgress { Error = _task.Exception?.GetBaseException().Message ?? "Automatic update check failed.", FinishedUtc = DateTime.UtcNow }
                : _task.GetAwaiter().GetResult();
            float delay = RetryDelay(report);
            _next = Time.unscaledTime + delay;
            bool settled = !report.Cancelled && report.TransientFailed == 0 && report.Error.Length == 0;
            if (settled) _prepared = null;   // the next scheduled check starts from a fresh reload
            lock (StateGate) {
                _isChecking = false; _lastReport = report; _lastError = report.Error.Length > 0 ? report.Error : report.TransientFailed > 0 ? "Some package checks failed. See the affected version entries." : "";
                _nextCheckUtc = DateTime.UtcNow.AddSeconds(delay);
                if (settled) _lastSuccessUtc = report.FinishedUtc;
            }
            Interlocked.Increment(ref _stateRevision);
            _task = null; _cancel?.Dispose(); _cancel = null;
        }
        if (!SafeScene || !OverviewOptions.AutomaticUpdates.Value) { if (_cancel != null && !_cancel.IsCancellationRequested) _cancel.Cancel(); return; }
        if (_task != null) return;
        if(Time.unscaledTime<_next||Time.unscaledTime<_poll)return;_poll=Time.unscaledTime+10;
        try{
            if(PackLibrary.Task()?.BlocksUpdates==true)return;
            if(_prepared==null)
            {
                // The check reports on what is installed, so the applied view is the right one — it also skips the
                // draft/recovery writes the editable view performs. The reload already scanned, so reuse its rows
                // instead of scanning the table a second time on the main thread.
                var repo=new OverviewRepository(appliedView:true);
                _prepared=(repo,repo.ReloadInstalled);
            }
            var (preparedRepo,preparedMods)=_prepared.Value;
            _cancel=new CancellationTokenSource();
            lock (StateGate) { _isChecking = true; _nextCheckUtc = default; _lastReport = null; }
            Interlocked.Increment(ref _stateRevision);
            // The whole check runs synchronously up to its first real await, and with a fresh catalogue cache there is
            // none: component scan, manifest hashing and source matching all landed in this frame (300–490 ms hitches).
            var token=_cancel.Token;
            _task=Task.Run(()=>Run(preparedRepo,preparedMods,token),token);
        }catch(Exception e){
            _next=Time.unscaledTime+60;
            lock (StateGate) { _isChecking = false; _lastError = e.Message; _lastReport = new ThunderstoreUpdateService.CheckProgress { Error = e.Message, FinishedUtc = DateTime.UtcNow }; _nextCheckUtc = DateTime.UtcNow.AddSeconds(60); }
            Interlocked.Increment(ref _stateRevision);
            Plugin.Log.LogWarning("[Auto update check] "+e.Message);
        }
    }
    // Only transient failures (network, timeouts) earn the one-minute retry. A deprecated or missing package is a settled
    // answer from the store; retrying it every minute re-ran the whole check for as long as the menu stayed open.
    internal static float RetryDelay(ThunderstoreUpdateService.CheckProgress report) => report.Cancelled ? 10 : report.TransientFailed > 0 || report.Error.Length > 0 ? 60 : 1800;
    private static async Task<ThunderstoreUpdateService.CheckProgress> Run(OverviewRepository repo,System.Collections.Generic.IReadOnlyList<OverviewMod> mods,CancellationToken token)
    {
        using var service = new ThunderstoreUpdateService(repo, TimeSpan.FromMinutes(30));
        try {
            await service.CheckAsync(mods, token, (_, _) => {
                lock (StateGate) _lastReport = service.LastProgress;
                Interlocked.Increment(ref _stateRevision);
            }).ConfigureAwait(false);
            var pack = await service.CheckPackAsync(repo.State.pack, token).ConfigureAwait(false);
            var report = service.LastReport;
            if (pack?.Error.Length > 0) report.Error = pack.Error;
            report.FinishedUtc = DateTime.UtcNow;
            return report;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { var report = service.LastReport; report.Cancelled = true; report.FinishedUtc = DateTime.UtcNow; return report; }
        catch (Exception e) { Plugin.Log.LogWarning("[Auto update check] " + e.Message); var report = service.LastReport; report.Error = e.Message; report.FinishedUtc = DateTime.UtcNow; return report; }
    }
    private void OnDestroy(){_cancel?.Cancel();lock (StateGate) _isChecking = false;Interlocked.Increment(ref _stateRevision);if(_task==null)_cancel?.Dispose();else{var c=_cancel;_=_task.ContinueWith(_=>c?.Dispose());}}
}
