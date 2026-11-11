#nullable enable
using System;
using UnityEngine;
namespace ModConfigEnhance.Overview;
/// <summary>Unity lifecycle adapter for the directly tested production submission state machine.</summary>
internal sealed class NetworkUiSubmission : MonoBehaviour
{
    private readonly NetworkSubmissionState _state = new();
    internal bool Pending => _state.Pending;
    internal void Submit(string mode, string address, Action<string> status, Action? applied = null)
    {
        var baseline = OverviewUpdateQueue.Snapshot.Revision;
        _state.Submit(mode, address, baseline, Time.unscaledTime,
            posted => OverviewUpdateQueue.SetNetwork(mode, address, posted),
            (notice, error) => status(Message(notice, error)), applied);
    }
    private static string Message(NetworkSubmissionState.Notice notice, string error) => notice switch
    {
        NetworkSubmissionState.Notice.Applied => Loc.T("Network settings are now active.", "网络设置已生效。", "網路設定已生效。"),
        NetworkSubmissionState.Notice.SubmissionFailed => Loc.LocalizeMessage(error),
        NetworkSubmissionState.Notice.Waiting => Loc.T("Network settings are taking longer; still waiting for confirmation. You may submit again.", "网络设置确认较慢，仍在等待生效确认；可以重新提交。", "網路設定確認較慢，仍在等待生效確認；可以重新提交。"),
        NetworkSubmissionState.Notice.DispatchUncertain => Loc.T("Network command was saved, but worker startup was not confirmed; still waiting. You may submit again.", "网络命令已保存，但助手启动未确认；仍在等待，可以重新提交。", "網路命令已儲存，但助手啟動未確認；仍在等待，可以重新提交。"),
        _ => Loc.T("Submitting network settings; the previous mode remains active…", "正在提交网络设置，确认前仍使用原模式……", "正在提交網路設定，確認前仍使用原模式……")
    };
    private void Update()
    {
        if (!Pending) return;
        var saved = OverviewUpdateQueue.Snapshot;
        _state.Observe(saved.Revision, saved.Network.Mode, saved.Network.ProxyAddress,
            Time.unscaledTime, OverviewUpdateQueue.NetworkCommandProcessed);
    }
    internal void CancelObservation() => _state.CancelObservation();
    private void OnDisable() => CancelObservation();
    private void OnDestroy() => _state.Dispose();
}
