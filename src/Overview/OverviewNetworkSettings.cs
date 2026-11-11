#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed class OverviewNetworkSettings : IDisposable
{
    private readonly Action<string> _status;
    private readonly RectTransform _host;
    private readonly TMP_InputField _address;
    private readonly TextMeshProUGUI _modeText, _result;
    private readonly Button _test;
    private readonly RectTransform _modeAnchor;
    private readonly NetworkUiSubmission _submission;
    private string _mode, _initial;
    private CancellationTokenSource? _cancellation;
    private Process? _probe;
    private bool _disposed, _busy;
    internal bool HasDirty => Signature() != _initial;
    private string Signature() => _mode + "\n" + (_mode == "custom" ? _address.text.Trim() : "");
    internal OverviewNetworkSettings(RectTransform host, Action<string> status)
    {
        _host = host; _status = status;
        _submission = host.gameObject.AddComponent<NetworkUiSubmission>();
        var network = OverviewUpdateQueue.Snapshot.Network; _mode = network.Mode;
        var form = new ManagementForm(host, Run);
        form.Text(Loc.T("Download network", "更新下载网络", "更新下載網路"), true);
        form.Text(Loc.T("Test the entered settings; save them separately to use for downloads.", "测试当前填写的设置；保存后用于后续下载。", "測試目前填寫的設定；儲存後用於後續下載。"));
        var modeButton = form.Button(ModeText(), ChooseMode); _modeText = modeButton.GetComponentInChildren<TextMeshProUGUI>(true);
        _modeAnchor = (RectTransform)modeButton.transform;
        string address = network.ProxyAddress;
        if (Uri.TryCreate(address, UriKind.Absolute, out var prior) && prior.UserInfo.Length > 0) address = new UriBuilder(prior) { UserName = "", Password = "" }.Uri.AbsoluteUri;
        _address = form.Field(Loc.T("Custom HTTP(S) proxy address", "自定义 HTTP(S) 代理地址", "自訂 HTTP(S) Proxy 位址"), address);
        OverviewUi.SetPlaceholder(_address, "http://127.0.0.1:7890");
        _address.onValueChanged.AddListener(_ => Invalidate()); _address.interactable = _mode == "custom";
        _test = form.Button(Loc.T("Test connection", "测试连接", "測試連線"), Test);
        _result = OverviewUi.Text(form.Content, Loc.T("Not tested", "未测试", "未測試"), 19); _result.textWrappingMode = TextWrappingModes.Normal;
        float y = -((RectTransform)_test.transform).anchoredPosition.y + 55;
        OverviewUi.Place(_result.rectTransform, 0, y, form.Width, 180); form.Content.sizeDelta = new Vector2(0, y + 190);
        form.Action(Loc.T("Save network settings", "保存网络设置", "儲存網路設定"), () => {
            Validate(); string submitted = Signature();
            _submission.Submit(_mode, _address.text.Trim(), _status, () => _initial = submitted);
        });
        form.Action(Loc.T("Restore saved settings", "恢复已保存设置", "還原已儲存設定"), () => {
            CancelProbe(); var saved = OverviewUpdateQueue.Snapshot.Network; _mode = saved.Mode;
            _address.SetTextWithoutNotify(saved.ProxyAddress); _modeText.text = ModeText(); _address.interactable = _mode == "custom"; _initial = Signature(); Invalidate();
        }, UiActionRole.Neutral);
        _initial = Signature();
    }
    private string ModeText() => _mode == "direct" ? Loc.T("Direct connection", "直接连接", "直接連線") : _mode == "custom"
        ? Loc.T("Custom proxy", "自定义代理", "自訂 Proxy") : Loc.T("System proxy (default)", "系统代理（默认）", "系統 Proxy（預設）");
    private void ChooseMode() => WindowPopover.Menu(_host, _modeAnchor, Loc.T("Connection mode (draft)", "连接方式（待保存）", "連線方式（待儲存）"), new[] {
        (Loc.T("System proxy", "系统代理", "系統 Proxy"), "system"), (Loc.T("Direct connection", "直接连接", "直接連線"), "direct"),
        (Loc.T("Custom proxy", "自定义代理", "自訂 Proxy"), "custom") }, _mode, value => { _mode = value; _modeText.text = ModeText(); _address.interactable = value == "custom"; Invalidate(); });
    private void Run(Action action) { try { action(); } catch (Exception error) { _status(Loc.LocalizeMessage(error.Message)); if (_result != null) _result.text = Loc.LocalizeMessage(error.Message); } }
    private void Validate()
    {
        if (_mode == "custom" && (!Uri.TryCreate(_address.text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "http" && uri.Scheme != "https" || uri.UserInfo.Length > 0))
            throw new ArgumentException(Loc.T("Enter an HTTP(S) proxy address without credentials.", "请填写不含用户名和密码的 HTTP(S) 代理地址。", "請填寫不含使用者名稱和密碼的 HTTP(S) Proxy 位址。"));
    }
    private void Invalidate()
    {
        CancelProbe(); if (_result != null) _result.text = Loc.T("Not tested", "未测试", "未測試");
    }
    private async void Test()
    {
        if (_busy || _disposed) return;
        try { Validate(); } catch (Exception error) { _result.text = Loc.LocalizeMessage(error.Message); return; }
        _busy = true; _test.interactable = false;
        var cancellation = _cancellation = new CancellationTokenSource();
        try
        {
            _result.text = Loc.T("Testing store and download connections…", "正在测试商店与下载连接……", "正在測試商店與下載連線……");
            string id = Guid.NewGuid().ToString("N"), directory = OverviewProfileAssets.CheckedPath(BepInEx.Paths.BepInExRootPath, ModConfigEnhance.Updating.ManagerIdentity.RuntimeDirectory + "/probes"); Directory.CreateDirectory(directory);
            var request = new ProbeRequest { Network = new UpdateNetworkOptions { Mode = _mode, ProxyAddress = _mode == "custom" ? _address.text.Trim() : "" }, Namespace = "PEAKModding", Package = "PEAKLib_Core" };
            OverviewRepository.AtomicWrite(Path.Combine(directory, id + ".request.json"), JsonConvert.SerializeObject(request));
            _probe = OverviewUpdateQueue.StartProbe(id);
            string output = Path.Combine(directory, id + ".result.json"); var timer = Stopwatch.StartNew();
            ProbeResult? result = null;
            while (timer.Elapsed < TimeSpan.FromSeconds(30))
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (File.Exists(output))
                {
                    try { result = JsonConvert.DeserializeObject<ProbeResult>(File.ReadAllText(output)); } catch (IOException) { }
                    if (result?.Complete == true) break;
                }
                if (_probe.HasExited) throw new IOException(Loc.T("The connection test exited without a result.", "连接测试已结束，但未返回结果。", "連線測試已結束，但未傳回結果。"));
                await Task.Delay(200, cancellation.Token);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (result?.Complete != true) throw new TimeoutException(Loc.T("Connection test timed out.", "连接测试超时。", "連線測試逾時。"));
            if (_disposed || _host == null) return;
            _result.text = Line(Loc.T("Store information", "商店版本信息", "商店版本資訊"), result.Store) + "\n\n" + Line(Loc.T("Mod download", "模组下载连接", "模組下載連線"), result.Download);
            _status(result.Store.Success && result.Download.Success ? Loc.T("Connection test succeeded.", "连接测试通过。", "連線測試通過。") : Loc.T("Connection test failed; see the result.", "连接测试失败，原因已显示。", "連線測試失敗，原因已顯示。"));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposed && _result != null) _result.text = Loc.LocalizeMessage(error.Message); }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation)) { StopProcess(); _cancellation = null; _busy = false; if (!_disposed && _test != null) _test.interactable = true; }
            cancellation.Dispose();
        }
    }
    private static string Line(string label, ProbeEndpoint endpoint) => label + " · " + (endpoint.Success
        ? Loc.T("Passed", "通过", "通過") + " · " + endpoint.Milliseconds + " ms"
        : Loc.T("Failed: ", "失败：", "失敗：") + Loc.LocalizeMessage(endpoint.Error));
    private void StopProcess() { if (_probe != null) { try { if (!_probe.HasExited) _probe.Kill(); } catch (InvalidOperationException) { } finally { _probe.Dispose(); _probe = null; } } }
    private void CancelProbe() { _cancellation?.Cancel(); StopProcess(); _busy = false; if (_test != null) _test.interactable = true; }
    public void Dispose() { _disposed = true; CancelProbe(); _submission.CancelObservation(); if (_submission != null) UnityEngine.Object.Destroy(_submission); _host.GetComponent<WindowPopover>()?.Close(); }
}
