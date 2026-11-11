#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ModConfigEnhance.Updating;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewPage
{
    private Button _updatesButton = null!;
    private TextMeshProUGUI _updatesName = null!, _updatesStage = null!;
    private RectTransform _updatesBar = null!, _updatesTrack = null!;
    private UpdateQueueView? _updatesView;
    private float _updatesPollAt, _updatesWidth;
    /// <summary>null = no baseline yet (see <see cref="PollUpdates"/>).</summary>
    private string? _updateInventoryKey;
    private static string UpdateInventoryKey() => string.Join("|", OverviewUpdateQueue.Snapshot.Jobs
        .Where(job => UpdateStates.History(job.State) || job.State == UpdateStates.WaitingExit)
        .Select(job => job.Id + ":" + job.State + ":" + job.TargetVersion));
    private readonly Dictionary<string, string> _observedUpdateStates = new();
    private bool _updateStatesObserved;
    private string _updateResult = "";
    private float _updateResultUntil;

    private void ObserveUpdateResults()
    {
        foreach (var job in OverviewUpdateQueue.Snapshot.Jobs)
        {
            bool changed = !_observedUpdateStates.TryGetValue(job.Id, out var previous) || previous != job.State;
            _observedUpdateStates[job.Id] = job.State;
            if (!_updateStatesObserved || !changed) continue;
            if (job.State == UpdateStates.Completed || job.State == UpdateStates.RolledBack || job.State == UpdateStates.Failed || job.State == UpdateStates.PendingRestart)
            {
                string state = job.State == UpdateStates.Failed && job.RollbackRequested ? Loc.T("Rollback failed", "回退失败", "還原失敗") : UpdateQueueView.StateTextPlain(job.State);
                _updateResult = UpdateQueueView.JobName(job) + " · " + state;
                _updateResultUntil = Time.unscaledTime + 9;
                ManagementStatus(_updateResult);
            }
        }
        _updateStatesObserved = true;
    }

    private void BuildUpdateTools()
    {
        _updatesButton = OverviewUi.Button(_frame, "", ToggleUpdateQueue, 220, 46, UiActionRole.Overview);
        foreach (var text in _updatesButton.GetComponentsInChildren<TMP_Text>(true)) text.gameObject.SetActive(false);
        _updatesName = OverviewUi.Text(_updatesButton.transform, "", 14);
        _updatesName.alignment = TextAlignmentOptions.Midline;
        _updatesName.margin = Vector4.zero;
        _updatesName.rectTransform.localRotation = Quaternion.identity;
        _updatesName.rectTransform.localScale = Vector3.one;
        _updatesName.overflowMode = TextOverflowModes.Ellipsis;
        _updatesStage = OverviewUi.Text(_updatesButton.transform, "", 11);
        _updatesStage.color = OverviewUi.Muted;
        _updatesStage.alignment = TextAlignmentOptions.Midline;
        _updatesStage.margin = Vector4.zero;
        _updatesStage.rectTransform.localRotation = Quaternion.identity;
        _updatesStage.rectTransform.localScale = Vector3.one;
        _updatesStage.overflowMode = TextOverflowModes.Ellipsis;
        _updatesTrack = OverviewUi.Rect("UpdateProgress", _updatesButton.transform);
        _updatesTrack.gameObject.AddComponent<Image>().color = ModConfigLook.Surface(new Color(.05f, .06f, .05f, .7f));
        _updatesBar = OverviewUi.Rect("Fill", _updatesTrack);
        _updatesBar.gameObject.AddComponent<Image>().color = OverviewUi.Green;
        SetUpdateProgress(_updatesBar, 0);
        GamepadSupport.MarkSelectable(_updatesButton.gameObject, null, (RectTransform)_updatesButton.transform);
        RefreshUpdateMini();
    }

    private void ToggleUpdateQueue()
    { if (!CloseUpdateQueue()) OpenUpdateQueue(); }

    private void OpenUpdateQueue()
    {
        _updatesView ??= new UpdateQueueView(_frame, (RectTransform)_updatesButton.transform,
            QueueAllUpdates,
            () => _list?.QueueSelectedUpdates(), ManagementStatus, () => OpenManagement(ManagementTab.Network), () => OverviewUpdateQueue.StartPendingUpdates(), ShowInstallationReview);
        _updatesView.Open(OverviewUpdateQueue.Snapshot);
    }

    private void QueueAllUpdates()
    {
        var mods = _list?.Mods ?? Array.Empty<OverviewMod>();
        var components = InfrastructureOverviewModel.Create(InfrastructurePackages.Results).Where(p => p.HasUpdate).ToArray();
        void StageAll()
        {
            var componentKeys = new HashSet<string>(components.Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
            var updates = new List<UpdateCommand>(); var failures = new List<string>();
            foreach (var component in components)
            {
                try {
                    if (!_repo.GetGlobalUpdateAllowed()) throw new InvalidOperationException("Update checks disabled");
                    var target = InfrastructurePackages.Results.First(p => p.Key.Equals(component.Key, StringComparison.OrdinalIgnoreCase));
                    updates.Add(OverviewUpdateQueue.ComponentUpdateCommand(target, component.LatestVersion));
                } catch (Exception error) { failures.Add(component.Key + ": " + Loc.LocalizeMessage(error.Message)); }
            }
            OverviewUpdateQueue.EnqueueAll(_repo, mods.Where(m => !componentKeys.Contains(m.SourceKey)).ToArray(), ManagementStatus, updates);
            string message = OverviewUpdateQueue.Feedback + (failures.Count > 0 ? "\n" + string.Join("\n", failures) : "");
            OverviewUpdateQueue.SetFeedback(message, ManagementStatus);
            RefreshHeader();
            OverviewUi.ShowText(Loc.LocalizeMessage("Update queue notice"), message);
        }
        if (components.Length == 0) { StageAll(); return; }
        OverviewUi.ConfirmText(Loc.T("Update all", "全部更新", "全部更新"),
            Loc.LocalizeMessage("If a component update fails, dependent mods may stop working. Start downloading updates now?"),
            StageAll, confirmLabel: Loc.LocalizeMessage("Start updates"));
    }

    // Returns the right edge still available for pack information.
    private float LayoutUpdateTools(float width, float tabRight)
    {
        if (_updatesButton == null) return width - 18;
        _updatesWidth = Mathf.Min(220, Mathf.Max(54, width - tabRight - 34));
        float x = Mathf.Max(tabRight + 12, width - 18 - _updatesWidth);
        OverviewUi.Place((RectTransform)_updatesButton.transform, x, 8, _updatesWidth, 46);
        // The ribbon's notched ends and stitched edges are decoration, not text space.
        float inset = Mathf.Clamp(_updatesWidth * .12f, 12, 24);
        float contentWidth = Mathf.Max(0, _updatesWidth - inset * 2);
        OverviewUi.PlaceClipped(_updatesName, inset, 8, contentWidth, 16);
        OverviewUi.PlaceClipped(_updatesStage, inset, 24, contentWidth, 12);
        OverviewUi.Place(_updatesTrack, inset, 36, contentWidth, 2);
        _updatesView?.Layout();
        var tabs = new[] { _back, _listTab.GetComponent<Button>(), _introTab.GetComponent<Button>(), _hotkeyTab.GetComponent<Button>(), _updatesButton }
            .Where(button => button != null && button.gameObject.activeSelf).ToList();
        for (int i = 0; i < tabs.Count; i++)
        {
            var navigation = tabs[i].navigation;
            navigation.mode = Navigation.Mode.Explicit;
            navigation.selectOnLeft = tabs[(i + tabs.Count - 1) % tabs.Count];
            navigation.selectOnRight = tabs[(i + 1) % tabs.Count];
            navigation.selectOnDown = _profileChoice;
            tabs[i].navigation = navigation;
            GamepadSupport.MarkSelectable(tabs[i].gameObject, null, (RectTransform)tabs[i].transform);
        }
        return x - 12;
    }

    private void PollUpdates()
    {
        if (Time.unscaledTime >= _updatesPollAt)
        {
            _updatesPollAt = Time.unscaledTime + .2f;
            OverviewUpdateQueue.Poll();
            OverviewUpdateQueue.Reconcile();
            RefreshUpdateMini();
            _updatesView?.Refresh(OverviewUpdateQueue.Snapshot, OverviewUpdateQueue.LastError);
            ObserveUpdateResults();
            // 2.3.7: the reload exists for "an update finished while the page was open". The first fingerprint
            // seen (empty key, or before any real queue read) is only the baseline: the list was just scanned from
            // disk on entry, so it already shows every job that had finished. Treating that first comparison as a
            // change reloaded the whole list ~0.2s after every first entry, rebuilding it while it was still
            // appearing (FrameDoctor 2026-11-09: two identical BuildModLayers passes).
            if (OverviewUpdateQueue.HasRead)
            {
                string inventory = UpdateInventoryKey();
                if (_updateInventoryKey == null) _updateInventoryKey = inventory;
                else if (_list != null && _tab == PageTab.Mods && !_list.IsEditingText && inventory != _updateInventoryKey)
                {
                    _updateInventoryKey = inventory; _list.Reload();
                }
            }
        }
        _updatesView?.Tick();
    }

    private void RefreshUpdateMini()
    {
        if (_updatesName == null) return;
        var jobs = OverviewUpdateQueue.Snapshot.Jobs;
        int draftCount = ResourceOperationDraft.PendingUpdates().Count;
        int pending = draftCount + jobs.Count(job => job.State is UpdateStates.Queued or UpdateStates.Resolving or UpdateStates.Downloading or UpdateStates.Verifying or UpdateStates.Preparing);
        int downloaded = jobs.Count(job => job.State == UpdateStates.PendingRestart);
        int waitingExit = jobs.Count(job => job.State == UpdateStates.WaitingExit);
        int errors = jobs.Count(job => job.State == UpdateStates.Failed);
        if (!_updatesName.enableAutoSizing) { _updatesName.enableAutoSizing = true; _updatesName.fontSizeMin = 9; _updatesName.fontSizeMax = 14; _updatesName.textWrappingMode = TextWrappingModes.NoWrap; }
        if (pending + downloaded + waitingExit + errors > 0)
        {
            _updatesName.text = Loc.F("Updatable {0} · Downloaded {1} · Waiting exit {2} · Errors {3}",
                "本次可更新 {0} · 已下载 {1} · 等待退出 {2} · 错误 {3}",
                "本次可更新 {0} · 已下載 {1} · 等待結束 {2} · 錯誤 {3}", pending, downloaded, waitingExit, errors);
            _updatesStage.text = draftCount > 0 ? Loc.LocalizeMessage(ResourceOperationDraft.AwaitingAcknowledgement ? "Submitted; waiting for worker acknowledgement." : "Ready to start updates")
                : jobs.Any(job => UpdateStates.Busy(job.State)) ? Loc.T("Downloading", "正在下载", "正在下載")
                : waitingExit > 0 ? Loc.T("Exit the game to deploy", "退出游戏后部署", "結束遊戲後部署")
                : downloaded > 0 ? Loc.T("Restart to apply", "重启后生效", "重新啟動後生效")
                : Loc.T("View queue", "查看队列", "查看佇列");
            var active = jobs.FirstOrDefault(job => UpdateStates.Busy(job.State)) ?? jobs.FirstOrDefault(job => job.State == UpdateStates.WaitingExit);
            SetUpdateProgress(_updatesBar, active != null ? active.Progress : downloaded > 0 ? 1 : 0);
            if (_updateResultUntil > Time.unscaledTime) _updatesStage.text = _updateResult;
            Markup();
            return;
        }
        _updatesName.text = Loc.T("Updates", "更新", "更新");
        bool idle = !jobs.Any(job => job.State is UpdateStates.Paused or UpdateStates.Stopped or UpdateStates.RollbackWaiting or UpdateStates.RollingBack or UpdateStates.Deploying);
        _updatesStage.text = idle ? Loc.T("Up to date / view queue", "空闲 · 查看队列", "閒置 · 查看佇列") : Loc.T("View queue", "查看队列", "查看佇列");
        SetUpdateProgress(_updatesBar, 0);
        if (_updateResultUntil > Time.unscaledTime) _updatesStage.text = _updateResult;
        Markup();
    }

    /// <summary>OverviewUi.Text 默认关掉 richText（src/Overview/OverviewUi.cs:91），按内容开启，与 OverviewHotkeyView 的既有做法一致。</summary>
    private void Markup()
    {
        _updatesName.richText = _updatesName.text.Contains("<color=#");
        _updatesStage.richText = _updatesStage.text.Contains("<color=#");
    }

    internal static void SetUpdateProgress(RectTransform fill, double progress)
    {
        fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(Mathf.Clamp01((float)progress), 1);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
    }

    private bool CloseUpdateQueue() => _updatesView?.TryClose() == true;
    private void DisposeUpdateTools() { _updatesView?.Dispose(); _updatesView = null; }
}
