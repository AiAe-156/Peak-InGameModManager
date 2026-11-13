#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ModConfigEnhance.Updating;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>A non-modal, frame-bound queue. Hiding the view never stops the worker.</summary>
internal sealed class UpdateQueueView : IDisposable
{
    private readonly RectTransform _frame, _anchor, _root, _panel, _content;
    private readonly ScrollRect _scroll;
    private readonly Button _queueTab, _historyTab, _all, _selected, _network, _close, _save, _clear;
    private readonly TextMeshProUGUI _heading, _empty, _notice;
    private readonly Action<string> _status;
    private readonly List<Row> _rows = new();
    private readonly List<RectTransform> _headers = new();
    private UpdateSnapshot _snapshot = new();
    private GameObject? _returnFocus;
    private bool _history;
    private string _structure = "", _actionLayoutKey = "";
    private int _openFrame;
    private float _width, _height;
    private bool _embedded;
    private Action? _networkRequested;
    private readonly Action<string>? _review;
    internal bool IsOpen => _root != null && _root.gameObject.activeSelf;
    /// <summary>1.1.5: the store embeds this list; its update-all / update-selected buttons belong to the overview.</summary>
    internal void HideUpdateButtons() { _all.gameObject.SetActive(false); _selected.gameObject.SetActive(false); _width = 0; Layout(); }
    internal Transform Root => _root;

    private sealed class Row
    {
        internal string Id = "";
        internal RectTransform Root = null!, Fill = null!;
        internal TextMeshProUGUI Name = null!, Versions = null!, State = null!, Amount = null!, Speed = null!;
        /// <summary>Width both text lines share; <see cref="LayoutLines"/> places the columns from their actual text.</summary>
        internal float LineWidth;
        internal readonly List<Button> Buttons = new();
    }

    internal UpdateQueueView(RectTransform frame, RectTransform anchor, Action all, Action selected, Action<string> status, Action? network = null, Action? save = null, Action<string>? review = null)
    {
        _frame = frame; _anchor = anchor; _status = status; _networkRequested = network; _review = review;
        _root = OverviewUi.Rect("MCE_UpdateQueue", frame); _root.gameObject.SetActive(false);
        var border = OverviewUi.Panel("Border", _root, OverviewUi.Gold); OverviewUi.Fill(border);
        _panel = OverviewUi.Panel("Paper", border, OverviewUi.Brown); OverviewUi.Fill(_panel, 2);
        var pointer = OverviewUi.Rect("AnchorPointer", _root);
        pointer.anchorMin = pointer.anchorMax = new Vector2(1, 1); pointer.pivot = new Vector2(0, .5f);
        pointer.anchoredPosition = new Vector2(-1, -25); pointer.sizeDelta = new Vector2(12, 22);
        pointer.gameObject.AddComponent<UpdateQueuePointer>().color = OverviewUi.Gold;
        _heading = OverviewUi.Text(_panel, "", 20);
        _close = AddButton(_panel, Loc.T("Collapse", "收起", "收起"), () => Close(), 64, UiActionRole.Neutral);
        _queueTab = AddButton(_panel, "", () => Switch(false), 140, UiActionRole.Edit);
        _historyTab = AddButton(_panel, "", () => Switch(true), 140, UiActionRole.Neutral);
        _all = AddButton(_panel, "", () => { Close(false); all(); }, 122, UiActionRole.Commit);
        _selected = AddButton(_panel, "", () => { Close(false); selected(); }, 122, UiActionRole.Commit);
        _network = AddButton(_panel, "", Network, 108, UiActionRole.Edit);
        _save = AddButton(_panel, Loc.LocalizeMessage("Start updates"), () => { Close(false); save?.Invoke(); }, 122, UiActionRole.Commit);
        _save.gameObject.SetActive(save != null);
        _clear = AddButton(_panel, "", () => Command("clear-finished", ""), 90, UiActionRole.Neutral);
        _notice = OverviewUi.Text(_panel, "", 12); _notice.overflowMode = TextOverflowModes.Ellipsis;
        _notice.textWrappingMode = TextWrappingModes.Normal; _notice.raycastTarget = true;
        _notice.gameObject.AddComponent<OverviewHeaderClick>().Clicked = _ => OverviewUi.ShowText(Loc.T("Update queue notice", "更新队列提示", "更新佇列提示"), _notice.text + (OverviewUpdateQueue.HistoricalError.Length > 0 ? "\n\n" + Loc.T("Historical worker diagnostic (not a current failure):", "历史助手诊断（不代表当前失败）：", "歷史助手診斷（不代表目前失敗）：") + "\n" + OverviewUpdateQueue.HistoricalError : ""));
        _scroll = OverviewUi.Rect("QueueScroll", _panel).gameObject.AddComponent<ScrollRect>();
        _scroll.horizontal = false; _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 40;
        var viewport = OverviewUi.Rect("Viewport", _scroll.transform); OverviewUi.Fill(viewport); viewport.offsetMax = new Vector2(-15, 0);
        viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        _content = OverviewUi.Rect("Jobs", viewport); _content.anchorMin = new Vector2(0, 1); _content.anchorMax = Vector2.one;
        _content.pivot = new Vector2(0, 1); _content.sizeDelta = Vector2.zero;
        _scroll.viewport = viewport; _scroll.content = _content;
        ManagementUi.Scrollbar((RectTransform)_scroll.transform, _scroll);
        _empty = OverviewUi.Text(_content, "", 16); _empty.textWrappingMode = TextWrappingModes.Normal;
        Layout();
    }

    internal UpdateQueueView(RectTransform host, Action all, Action selected, Action<string> status, Action network, Action<string>? review = null)
        : this(host, host, all, selected, status, review: review)
    { _embedded = true; _networkRequested = network; _width = _height = 0; Layout(); }

    internal void Open(UpdateSnapshot snapshot)
    {
        _snapshot = snapshot;
        _returnFocus = EventSystem.current?.currentSelectedGameObject;
        _root.gameObject.SetActive(true); _root.SetAsLastSibling(); _openFrame = Time.frameCount;
        Layout(); _structure = ""; Refresh(snapshot, OverviewUpdateQueue.LastError);
        if (GamepadSupport.IsGamepad) EventSystem.current?.SetSelectedGameObject(_queueTab.gameObject);
    }

    internal bool TryClose()
    {
        if (!IsOpen) return false;
        if (BackGuard.Allow()) Close(); // 1.1.5: B twice quickly to close
        return true;
    }

    internal void Close(bool restoreFocus = true)
    {
        if (!IsOpen) return;
        var selected = EventSystem.current?.currentSelectedGameObject;
        bool ownedSelection = selected != null && selected.transform.IsChildOf(_root);
        _root.gameObject.SetActive(false);
        if (restoreFocus && ownedSelection)
        {
            var target = _returnFocus != null && _returnFocus.activeInHierarchy ? _returnFocus : _anchor.gameObject;
            if (target.activeInHierarchy) EventSystem.current?.SetSelectedGameObject(target);
        }
        _returnFocus = null;
    }

    internal void Tick()
    {
        if (_embedded) return;
        if (!IsOpen || OverviewUi.HasDialog || Time.frameCount <= _openFrame) return;
        var mouse = Mouse.current;
        if (mouse == null || (!mouse.leftButton.wasPressedThisFrame && !mouse.rightButton.wasPressedThisFrame)) return;
        var canvas = _root.GetComponentInParent<Canvas>();
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 point = mouse.position.ReadValue();
        if (!RectTransformUtility.RectangleContainsScreenPoint(_root, point, camera)
            && !RectTransformUtility.RectangleContainsScreenPoint(_anchor, point, camera)) Close(false);
    }

    internal void Layout()
    {
        float anchorX = _anchor.anchoredPosition.x;
        float width = _embedded ? _frame.rect.width : Mathf.Min(520, Mathf.Max(240, anchorX - 28));
        float top = Mathf.Clamp(-_anchor.anchoredPosition.y + _anchor.rect.height * .5f - 25, 8, Mathf.Max(8, _frame.rect.height - 250));
        float height = _embedded ? _frame.rect.height : Mathf.Min(760, Mathf.Max(220, _frame.rect.height - top - 18));
        OverviewUi.Place(_root, _embedded ? 0 : Mathf.Max(10, anchorX - width - 12), _embedded ? 0 : top, width, height);
        if (_embedded) { _close.gameObject.SetActive(false); _root.Find("AnchorPointer")?.gameObject.SetActive(false); }
        if (Math.Abs(_width - width) < .5f && Math.Abs(_height - height) < .5f) return;
        _width = width; _height = height;
        OverviewUi.Place(_heading.rectTransform, 13, 8, width - 96, 32);
        OverviewUi.Place((RectTransform)_close.transform, width - 78, 9, 62, 28);
        _actionLayoutKey = ""; LayoutActions();
        OverviewUi.PlaceClipped(_notice, 12, 82, width - 28, 30);
        OverviewUi.Place((RectTransform)_scroll.transform, 12, 118, width - 28, Mathf.Max(24, height - 130));
        PublicationNavigation.Invalidate(_root);
        _structure = "";
    }

    private void LayoutActions()
    {
        var toolbar = new[] { _queueTab, _historyTab, _selected, _all, _network, _save, _clear }.Where(b => b.gameObject.activeSelf).ToArray();
        string key = string.Join("|", toolbar.Select(b => b.GetComponentInChildren<TMP_Text>(true).text));
        if (key == _actionLayoutKey) return; _actionLayoutKey = key;
        var preferred = toolbar.Select(b => Mathf.Clamp(b.GetComponentInChildren<TMP_Text>(true).GetPreferredValues().x + 36, 80, 190)).ToArray();
        var widths = QueueToolbarSizing.Widths(_width - 24, preferred, 8); float x = 12;
        for (int i = 0; i < toolbar.Length; i++) { OverviewUi.Place((RectTransform)toolbar[i].transform, x, 43, widths[i], 31); x += widths[i] + 8; }
        PublicationNavigation.Invalidate(_root);
    }

    /// <summary>1.1.0: LB / RB switch between the queue and the history.</summary>
    internal void StepTab() => Switch(!_history);
    private void Switch(bool history)
    {
        if (_history == history) return;
        _history = history; _scroll.verticalNormalizedPosition = 1; _structure = "";
        Refresh(_snapshot, OverviewUpdateQueue.LastError);
    }

    internal void Refresh(UpdateSnapshot snapshot, string lastError)
    {
        bool wasSave = _save.interactable, wasClear = _clear.interactable;
        _snapshot = snapshot;
        if (!IsOpen) return;
        int draftCount = ResourceOperationDraft.PendingUpdates().Count;
        bool submitting = ResourceOperationDraft.AwaitingAcknowledgement;
        var drafts = ResourceOperationDraft.PendingUpdates().Select(command => new UpdateJob {
            Id = "draft:" + command.Id, State = submitting ? "submitting" : "draft", DisplayName = ResourceOperationDraft.Label(command),
            InstalledVersion = command.Job?.InstalledVersion ?? "", TargetVersion = command.Job?.TargetVersion ?? command.Shop?.Version ?? ""
        }).ToArray();
        var visible = snapshot.Jobs.Concat(drafts).Where(job => Visible(job, _history))
            .OrderByDescending(job => _history ? job.UpdatedUtc : job.CreatedUtc, StringComparer.Ordinal).ToList();
        var key = new StringBuilder().Append(_history).Append('|').Append(_width).Append('|').Append(OverviewLanguageBridge.Revision);
        foreach (var job in visible) key.Append('|').Append(job.Id).Append(':').Append(job.State).Append(':').Append(job.Error.Length > 0);
        if (_structure != key.ToString())
        {
            _structure = key.ToString();
            Rebuild(visible);
        }
        foreach (var row in _rows)
        {
            var job = visible.FirstOrDefault(candidate => candidate.Id == row.Id);
            if (job != null) RefreshRow(row, job);
        }
        _heading.text = Loc.T("Downloads and operations", "下载与操作", "下載與操作");
        _queueTab.GetComponentInChildren<TMP_Text>(true).text = Loc.T("Update queue", "更新队列", "更新佇列") + " · " + snapshot.Jobs.Concat(drafts).Count(job => Visible(job, false));
        _historyTab.GetComponentInChildren<TMP_Text>(true).text = Loc.T("Update history", "更新记录", "更新紀錄") + " · " + snapshot.Jobs.Count(job => Visible(job, true));
        UiActionStyle.Set(_queueTab, _history ? UiActionRole.Neutral : UiActionRole.Edit);
        UiActionStyle.Set(_historyTab, _history ? UiActionRole.Edit : UiActionRole.Neutral);
        _all.GetComponentInChildren<TMP_Text>(true).text = Loc.T("Update all", "全部更新", "全部更新");
        _selected.GetComponentInChildren<TMP_Text>(true).text = Loc.T("Update selected", "更新选中", "更新選取");
        _network.GetComponentInChildren<TMP_Text>(true).text = Loc.T("Network", "网络设置", "網路設定");
        _close.GetComponentInChildren<TMP_Text>(true).text = Loc.T("Collapse", "收起", "收起");
        _save.interactable = draftCount > 0 && !submitting;
        _save.GetComponentInChildren<TMP_Text>(true).text = Loc.LocalizeMessage("Start updates");
        _clear.GetComponentInChildren<TMP_Text>(true).text = Loc.T("Clear", "清空", "清空");
        _clear.interactable = snapshot.Jobs.Any(job => !job.Hidden && (job.State == UpdateStates.Failed || job.State == UpdateStates.Stopped || job.State == UpdateStates.Completed || job.State == UpdateStates.RolledBack || job.State == UpdateStates.Superseded || job.State == UpdateStates.PendingRestart));
        if (wasSave != _save.interactable || wasClear != _clear.interactable) PublicationNavigation.Invalidate(_root);
        LayoutActions();
        string error = Loc.LocalizeMessage(lastError) ?? "";
        _notice.text = error.Length > 0 ? error : OverviewUpdateQueue.Feedback.Length > 0 ? OverviewUpdateQueue.Feedback : OverviewUpdateQueue.HistoricalError.Length > 0 ? Loc.T("Historical worker diagnostic available · click to view (not a current failure).", "有历史助手诊断 · 点击查看（不代表当前失败）。", "有歷史助手診斷 · 點擊檢視（不代表目前失敗）。") : Loc.T("Downloads continue while this window is closed. Changes take effect after restart.", "收起后继续更新；文件已准备的项目在重启后生效。", "收起後繼續更新；檔案已備妥的項目於重新啟動後生效。");
        _notice.color = error.Length > 0 ? new Color(.96f, .59f, .43f) : OverviewUi.Muted;
        if (submitting) _notice.text = Loc.LocalizeMessage("Submitted; waiting for worker acknowledgement.") + (error.Length > 0 ? "\n" + error : "");
        else if (draftCount > 0) _notice.text = Loc.F("{0} pending updates. Start updates without saving configuration.", "{0} 项历史待办更新，可直接开始，无需保存配置。", "{0} 項歷史待辦更新，可直接開始，無需儲存設定。", draftCount)
            + (error.Length > 0 ? "\n" + error : "");
        _notice.richText = _notice.text.Contains("<color=#");
        _empty.text = _history ? Loc.T("No completed updates yet.", "暂时没有已更新的模组。", "暫時沒有已更新的模組。") : Loc.T("The queue is empty. Choose mods or update all available mods.", "队列为空。可选择模组更新，或加入全部可更新模组。", "佇列為空。可選取模組更新，或加入全部可更新模組。");
    }

    /// <summary>List membership and both tab counts share this so a badge can never disagree with the rows.</summary>
    private static bool Visible(UpdateJob job, bool history)
        => !job.Hidden && job.State != UpdateStates.Removed && UpdateStates.Recorded(job.State) == history;

    /// <summary>History rows are grouped by the batch that produced them (see <see cref="UpdateHistoryBatches"/>).
    /// A header entry carries no job; the queue tab has no headers. The first flag marks the very first header.</summary>
    private List<(bool First, string Header, UpdateJob? Job)> Grouped(IReadOnlyList<UpdateJob> jobs)
    {
        var entries = new List<(bool First, string Header, UpdateJob? Job)>();
        if (!_history)
        {
            foreach (var job in jobs) entries.Add((false, "", job));
            return entries;
        }
        bool first = true;
        foreach (var batch in UpdateHistoryBatches.Group(jobs))
        {
            string time = batch.StartedUtc is DateTime started ? started.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "";
            entries.Add((first, time.Length > 0
                ? Loc.F("{0} · {1} items", "{0} · {1} 项", "{0} · {1} 項", time, batch.Jobs.Count)
                : Loc.F("Earlier · {0} items", "更早 · {0} 项", "更早 · {0} 項", batch.Jobs.Count), null));
            first = false;
            foreach (var job in batch.Jobs) entries.Add((false, "", job));
        }
        return entries;
    }

    private void Rebuild(IReadOnlyList<UpdateJob> jobs)
    {
        PublicationNavigation.Preserve(_root);
        foreach (var row in _rows) { row.Root.gameObject.SetActive(false); UnityEngine.Object.Destroy(row.Root.gameObject); }
        foreach (var header in _headers) if (header != null) UnityEngine.Object.Destroy(header.gameObject);
        _rows.Clear(); _headers.Clear();
        float width = _width - 28, y = 0;
        foreach (var entry in Grouped(jobs))
        {
            if (entry.Header.Length > 0)
            {
                y += entry.First ? 0 : 8;
                var header = OverviewUi.Text(_content, entry.Header, 11);
                header.color = OverviewUi.Muted; header.overflowMode = TextOverflowModes.Ellipsis; header.raycastTarget = false;
                OverviewUi.PlaceClipped(header, 8, y, width - 20, 16);
                _headers.Add(header.rectTransform);
                y += 18;
                continue; // a header entry has no job; falling through used to throw and leave the record tab empty
            }
            if (entry.Job is not UpdateJob job) continue;
            var row = new Row { Id = job.Id };
            row.Root = OverviewUi.Panel("Job_" + job.Id, _content, ModConfigLook.Surface(new Color(.10f, .11f, .10f, .72f)));
            // An installed-awaiting-restart job keeps the compact, action-light shape of a record row while it stays in the queue.
            bool recordActions = UpdateStates.Recorded(job.State) || job.State == UpdateStates.PendingRestart;
            bool showProgress = !recordActions && job.State is not ("draft" or "submitting");
            float rowHeight = recordActions ? 76 : 88, buttonsY = recordActions ? 48 : 55;
            OverviewUi.Place(row.Root, 0, y, width - 4, rowHeight); y += rowHeight + 5;
            // 两行：第一行名称占满、右端时间或百分比；第二行状态 → 版本紧随其后，下载速度靠右。各列按实际文字宽度占位，空列不占位。
            float lineWidth = width - 28;
            row.Amount = OverviewUi.Text(row.Root, "", 11); row.Amount.alignment = TextAlignmentOptions.MidlineRight; row.Amount.color = OverviewUi.Muted;
            row.Name = OverviewUi.Text(row.Root, JobName(job), 14);
            row.Versions = OverviewUi.Text(row.Root, "", 11); row.Versions.color = OverviewUi.Muted;
            row.Speed = OverviewUi.Text(row.Root, "", 10); row.Speed.color = OverviewUi.Muted; row.Speed.alignment = TextAlignmentOptions.MidlineRight;
            row.State = OverviewUi.Text(row.Root, "", 13); row.State.textWrappingMode = TextWrappingModes.NoWrap;
            row.LineWidth = lineWidth;
            row.State.raycastTarget = true;
            row.State.gameObject.AddComponent<OverviewHeaderClick>().Clicked = _ => ShowDetails(row.Id);
            var track = OverviewUi.Rect("Progress", row.Root); OverviewUi.Place(track, 8, 48, width - 25, 3);
            track.gameObject.SetActive(showProgress);
            track.gameObject.AddComponent<Image>().color = ModConfigLook.Surface(new Color(.04f, .05f, .04f, .8f));
            row.Fill = OverviewUi.Rect("Fill", track); row.Fill.gameObject.AddComponent<Image>().color = OverviewUi.Green;
            var actions = new List<(string Label, string Command, UiActionRole Role)>();
            if (!recordActions)
            {
                if (job.State == UpdateStates.Paused) actions.Add((Loc.T("Continue", "继续", "繼續"), "resume", UiActionRole.Commit));
                else if (job.State == UpdateStates.Stopped || job.State == UpdateStates.Failed) actions.Add((Loc.T("Retry", "重试", "重試"), "retry", UiActionRole.Commit));
                else if (job.State != UpdateStates.WaitingExit && job.State != UpdateStates.RollbackWaiting) actions.Add((Loc.T("Pause", "暂停", "暫停"), "pause", UiActionRole.Edit));
                if (!job.RecoveryRequired && job.State != UpdateStates.Stopped && job.State != UpdateStates.Failed) actions.Add((Loc.T("Stop", "停止", "停止"), "stop", UiActionRole.Neutral));
            }
            else if (job.State != UpdateStates.RolledBack && job.State != UpdateStates.Superseded && !string.IsNullOrWhiteSpace(job.BackupDirectory)) actions.Add((Loc.T("Roll back", "回退", "還原"), "rollback", UiActionRole.Edit));
            actions.Add(_history ? (Loc.T("Clear record", "清除记录", "清除記錄"), "remove", UiActionRole.Neutral) : (Loc.T("Remove from queue", "移出队列", "移出佇列"), "remove", UiActionRole.Neutral));
            actions.Add((Loc.T("Homepage", "主页下载", "首頁下載"), "homepage", UiActionRole.Neutral));
            if(job.Id.StartsWith("operation:",StringComparison.Ordinal)){
                actions.Clear();actions.Add((Loc.LocalizeMessage("Details"),"operation-details",UiActionRole.Edit));
                if(job.State==UpdateStates.Failed){actions.Add((Loc.LocalizeMessage("Retry"),"operation-retry",UiActionRole.Commit));actions.Add((Loc.T("Remove from queue","移出队列","移出佇列"),"operation-remove",UiActionRole.Neutral));}
                else actions.Add((Loc.LocalizeMessage("Stop"),"operation-cancel",UiActionRole.Neutral));
            }
            if (job.State == "draft") { actions.Clear(); actions.Add((Loc.LocalizeMessage("Remove from queue"), "remove-draft", UiActionRole.Neutral)); }
            if (job.State == "submitting") { actions.Clear(); actions.Add((Loc.LocalizeMessage("Details"), "draft-details", UiActionRole.Neutral)); }
            // Folder organization / local import has no store page: its row opens its own review window instead.
            if(IsInstallationTask(job)){actions.Clear();
                bool ended=job.State is UpdateStates.Completed or UpdateStates.Failed or UpdateStates.Stopped;
                if(job.State==UpdateStates.Paused){actions.Add((Loc.T("Review and confirm","查看并确认","檢視並確認"),"installation-review",UiActionRole.Commit));actions.Add((Loc.T("Clear task","清除任务","清除工作"),"installation-dismiss",UiActionRole.Neutral));}
                else if(ended){actions.Add((Loc.T("View","查看","檢視"),"installation-review",UiActionRole.Edit));actions.Add((Loc.T("Clear task","清除任务","清除工作"),"installation-dismiss",UiActionRole.Neutral));}
                else{actions.Add((Loc.T("View progress","查看进度","檢視進度"),"installation-review",UiActionRole.Edit));actions.Add((Loc.T("Stop","停止","停止"),"installation-stop",UiActionRole.Neutral));}}
            else if(job.StoreOperation){actions.Clear();
                actions.Add((Loc.T("Store page","商店页面","商店頁面"),"store-page",UiActionRole.Edit));
                actions.Add((Loc.T("Open folder","打开目录","開啟目錄"),"open-folder",UiActionRole.Neutral));
                actions.Add((Loc.T("Browser","浏览器","瀏覽器"),"homepage",UiActionRole.Neutral));}
            float gap = 5, buttonWidth = (width - 22 - (actions.Count - 1) * gap) / actions.Count;
            for (int i = 0; i < actions.Count; i++)
            {
                string command = actions[i].Command, jobId = job.Id;
                var button = AddButton(row.Root, actions[i].Label, () => Command(command, jobId), buttonWidth, actions[i].Role);
                button.interactable = command is "homepage" or "store-page" or "open-folder" or "installation-review" or "installation-dismiss" || job.State != UpdateStates.Deploying && job.State != UpdateStates.RollingBack;
                OverviewUi.Place((RectTransform)button.transform, 8 + i * (buttonWidth + gap), buttonsY, buttonWidth, 22);
                row.Buttons.Add(button);
            }
            _rows.Add(row); RefreshRow(row, job);
        }
        _empty.gameObject.SetActive(jobs.Count == 0); OverviewUi.Place(_empty.rectTransform, 8, 12, width - 20, 90);
        _content.sizeDelta = new Vector2(0, Mathf.Max(90, y + 8));
        ConfigureNavigation();
    }

    private static void RefreshRow(Row row, UpdateJob job)
    {
        row.Name.text = JobName(job);
        row.Versions.text = job.State is "draft" or "submitting" ? "" : VersionLine(job);
        bool recorded = UpdateStates.Recorded(job.State);
        string state = job.State == UpdateStates.Failed && job.RollbackRequested ? Loc.T("Rollback failed", "回退失败", "還原失敗")
            : job.StoreOperation && job.Namespace.Length == 0 && job.State == UpdateStates.Paused ? Loc.T("Preview ready · awaiting confirmation", "预览完成 · 等待确认", "預覽完成 · 等待確認")
            : StateText(job.State);
        // 历史条目的工作日志（如“已在新的游戏进程核对生效状态”）对玩家无意义，点开详情仍可查看。
        string detail = recorded && string.IsNullOrWhiteSpace(job.Error) ? "" : Loc.LocalizeMessage(!string.IsNullOrWhiteSpace(job.Error) ? job.Error : job.Message) ?? "";
        if (detail == state) detail = "";
        if (!recorded && job.Warnings.Count > 0) detail += (detail.Length > 0 ? " · " : "") + Loc.F("{0} notes", "{0} 条提示", "{0} 條提示", job.Warnings.Count);
        row.State.text = state + (detail.Length > 0 ? " · " + detail : "");
        row.State.richText = row.State.text.Contains("<color=#");
        row.State.color = job.State == UpdateStates.Failed || job.Error.Length > 0 ? new Color(.96f, .59f, .43f) : job.State == UpdateStates.Completed || job.State == UpdateStates.RolledBack || job.State == UpdateStates.PendingRestart ? OverviewUi.Green : OverviewUi.Ivory;
        row.Speed.text = DownloadNote(job);
        row.Amount.text = recorded ? UpdatedAt(job) : job.TotalBytes > 0 ? Math.Min(100, Math.Max(0, job.Progress * 100)).ToString("0") + "%" : "";
        LayoutLines(row);
        OverviewPage.SetUpdateProgress(row.Fill, job.Progress);
    }

    /// <summary>Places both text lines from what they actually say, so an empty column takes no room and the name keeps the whole first line.</summary>
    private static void LayoutLines(Row row)
    {
        const float left = 8, gap = 10;
        float right = left + row.LineWidth;
        float Width(TextMeshProUGUI text) => text.text.Length == 0 ? 0 : Mathf.Ceil(text.GetPreferredValues(text.text, Mathf.Infinity, Mathf.Infinity).x) + 2;
        float amount = Width(row.Amount);
        row.Amount.gameObject.SetActive(amount > 0);
        if (amount > 0) OverviewUi.PlaceClipped(row.Amount, right - amount, 3, amount, 18);
        OverviewUi.PlaceClipped(row.Name, left, 3, Mathf.Max(48, row.LineWidth - (amount > 0 ? amount + gap : 0)), 18);
        float speed = Width(row.Speed);
        row.Speed.gameObject.SetActive(speed > 0);
        if (speed > 0) OverviewUi.PlaceClipped(row.Speed, right - speed, 25, speed, 18);
        float available = row.LineWidth - (speed > 0 ? speed + gap : 0);
        float version = Width(row.Versions);
        // The state shrinks first (it is clickable for the full text); the version keeps its full width when it fits at all.
        float state = Mathf.Min(Width(row.State), Mathf.Max(60, available - (version > 0 ? version + gap : 0)));
        OverviewUi.PlaceClipped(row.State, left, 25, Mathf.Max(60, state), 18);
        bool showVersion = version > 0 && state + gap + version <= available + .5f;
        row.Versions.gameObject.SetActive(showVersion);
        if (showVersion) OverviewUi.PlaceClipped(row.Versions, left + state + gap, 26, version, 18);
    }

    private void Command(string action, string id)
    {
        if (action == "clear-finished") { try { OverviewUpdateQueue.Send("clear-finished", ""); _status(Loc.T("Clearing finished entries…", "正在清空已结束的记录…", "正在清空已結束的記錄…")); } catch (Exception error) { _status(Loc.LocalizeMessage(error.Message)); } return; }
        if(action is "operation-retry" or "operation-cancel" or "operation-remove"){OverviewUpdateQueue.Send(action,id.Substring("operation:".Length));return;}
        if (action is "operation-details" or "draft-details") { ShowDetails(id); return; }
        if (action == "remove-draft")
        {
            try { ResourceOperationDraft.Remove(id.Substring("draft:".Length)); Refresh(_snapshot, OverviewUpdateQueue.LastError); }
            catch (Exception error) { OverviewUi.ShowText(Loc.LocalizeMessage("Update queue notice"), Loc.LocalizeMessage(error.Message)); }
            return;
        }
        var job = _snapshot.Jobs.FirstOrDefault(candidate => candidate.Id == id); if (job == null) return;
        if(action=="store-page"){OpenStorePage(job);return;}
        if(action=="installation-review"){Review(job.Id);return;}
        if(action=="installation-dismiss"){
            OverviewUpdateQueue.Send("shop-dismiss",job.Id);
            _status(OverviewUpdateQueue.LastError.Length>0?Loc.LocalizeMessage(OverviewUpdateQueue.LastError):Loc.T("Clearing the task; no files were changed.","正在清除任务，未改动任何文件。","正在清除工作，未變更任何檔案。"));
            return;
        }
        if(action=="installation-stop"){
            try{
                var task=Newtonsoft.Json.JsonConvert.DeserializeObject<ShopTask>(System.IO.File.ReadAllText(System.IO.Path.Combine(OverviewUpdateQueue.Root,"shop","task.json")));
                if(task==null||task.Id!=job.Id)return;
                OverviewPage.StopInstallationTask(task);
                _status(Loc.T("Stopping; no files were changed.","正在停止，未改动任何文件。","正在停止，未變更任何檔案。"));
            }catch(Exception error){_status(Loc.LocalizeMessage(error.Message));}
            return;
        }
        if (action == "open-folder") { OpenPackageFolder(job); return; }
        if (action == "homepage")
        {
            string address = job.Homepage;
            if (string.IsNullOrWhiteSpace(address)) address = ThunderstoreUpdateService.HomeUrl(job.Namespace, job.Package);
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")) Application.OpenURL(address);
            else _status(Loc.T("The mod homepage is unavailable.", "此模组暂时没有可用的主页地址。", "此模組暫時沒有可用的首頁位址。"));
            return;
        }
        if (action == "rollback")
        {
            OverviewUi.Ask(Loc.T("Roll back update", "回退更新", "還原更新") + " · " + JobName(job), new[] {
                (Loc.T("Also restore pre-update parameters (otherwise keep current values)", "同时恢复更新前参数（不勾选则保留当前参数）", "同時還原更新前參數（未勾選則保留目前參數）"), "false")
            }, values => OverviewUpdateQueue.Send("rollback", id, bool.TryParse(values[0], out bool restore) && restore));
            return;
        }
        OverviewUpdateQueue.Send(action, id);
    }

    /// <summary>A folder-organization or local-import task: a store-queue job with no package identity.</summary>
    internal static bool IsInstallationTask(UpdateJob job) => job.StoreOperation && job.Namespace.Length == 0;

    private void Review(string id)
    {
        if (_review == null) { _status(Loc.T("Open the mod overview to review this task.", "请在模组概览中查看此任务。", "請在模組概覽中檢視此工作。")); return; }
        Close(false); _review(id);
    }

    /// <summary>商店按钮优先打开游戏内商店里该包的详情页；商店列表里没有它（本地安装、已下架等）就退回打开安装目录。</summary>
    private void OpenStorePage(UpdateJob job)
    {
        // Without a package identity an empty source would match an unrelated local mod and open its store page.
        if (IsInstallationTask(job)) { Review(job.Id); return; }
        try
        {
            var repo = new OverviewRepository();
            var mod = new OverviewCatalogue().Scan(repo).FirstOrDefault(candidate =>
                string.Equals(candidate.SourceNamespace, job.Namespace, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.SourceName, job.Package, StringComparison.OrdinalIgnoreCase));
            if (mod != null) { Close(false); ShopWindow.OpenPackage(repo, mod); return; }
        }
        catch (Exception error) { _status(Loc.LocalizeMessage(error.Message)); }
        OpenPackageFolder(job);
    }

    private void OpenPackageFolder(UpdateJob job)
    {
        if (string.IsNullOrWhiteSpace(job.PackageRoot)) { _status(Loc.T("No folder is recorded for this task.", "该任务没有记录安装目录。", "該工作沒有記錄安裝目錄。")); return; }
        try { PackFileShortcuts.Open(job.PackageRoot, true); }
        catch (Exception error) { _status(Loc.LocalizeMessage(error.Message)); }
    }

    private void ShowDetails(string id)
    {
        try {
            if(id.StartsWith("operation:",StringComparison.Ordinal)){
                string operation=id.Substring("operation:".Length);
                var task=Newtonsoft.Json.JsonConvert.DeserializeObject<ShopTask>(System.IO.File.ReadAllText(System.IO.Path.Combine(OverviewUpdateQueue.Root,"operations",operation,"shop","task.json")));
                Close(false);
                if(task?.DependencyWarnings.Count>0){
                    string content=string.Join("\n",task.DependencyWarnings.Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal));
                    using var sha=System.Security.Cryptography.SHA256.Create();string token=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content))).Replace("-", "").ToLowerInvariant();
                    OverviewUi.ConfirmText(Loc.LocalizeMessage("Review impact"),content,()=>OverviewUpdateQueue.Send("operation-ignore",operation,resourceBatchId:token));
                }else OverviewUi.ShowText(task?.Request.Package??"",Loc.LocalizeMessage(task?.Error.Length>0?task.Error:task?.Message??""));return;
            }
            if (id.StartsWith("draft:", StringComparison.Ordinal))
            {
                var command = ResourceOperationDraft.Snapshot().FirstOrDefault(c => c.Id == id.Substring("draft:".Length));
                if (command != null) { Close(false); OverviewUi.ShowText(Loc.LocalizeMessage("Update queue notice"), ResourceOperationDraft.Label(command) + "\n\n" + StateText(ResourceOperationDraft.AwaitingAcknowledgement ? "submitting" : "draft")); }
                return;
            }
        } catch (Exception error) { _status(Loc.LocalizeMessage(error.Message)); return; }
        Close(false);
        var job = _snapshot.Jobs.FirstOrDefault(candidate => candidate.Id == id); if (job == null) return;
        if(IsInstallationTask(job)){_review?.Invoke(id);return;}
        if(job.StoreOperation){OpenStorePage(job);return;}
        OverviewUi.ShowText(JobName(job), VersionLine(job) + "\n\n" + StateTextPlain(job.State)
            + (job.Message.Length > 0 ? "\n" + Loc.LocalizeMessage(job.Message) : "") + (job.Error.Length > 0 ? "\n\n" + Loc.LocalizeMessage(job.Error) : "")
            + (job.Warnings.Count > 0 ? "\n\n" + Loc.T("Notes", "提示", "提示") + "\n" + string.Join("\n", job.Warnings.Select(w => "· " + Loc.LocalizeMessage(w))) : ""));
    }

    private void Network()
    {
        if (_networkRequested != null) { _networkRequested(); return; }
        var options = new List<(string Label, string Value)> {
            (Loc.T("System proxy (default)", "使用系统代理（默认）", "使用系統代理（預設）"), "system"),
            (Loc.T("Direct connection", "直接连接", "直接連線"), "direct"),
            (Loc.T("Custom proxy", "自定义代理", "自訂代理"), "custom")
        };
        OverviewUi.Choose(Loc.T("Download network", "更新下载网络", "更新下載網路"), options, _snapshot.Network.Mode, mode => {
            if (mode != "custom") { OverviewUpdateQueue.SetNetwork(mode, ""); return; }
            string previous = _snapshot.Network.ProxyAddress;
            if (Uri.TryCreate(previous, UriKind.Absolute, out var stored) && stored.UserInfo.Length > 0) previous = new UriBuilder(stored) { UserName = "", Password = "" }.Uri.AbsoluteUri;
            OverviewUi.AskValidated(Loc.T("Custom download proxy", "自定义下载代理", "自訂下載代理"), new[] {
                (Loc.T("Proxy address, for example http://127.0.0.1:7897", "代理地址，例如 http://127.0.0.1:7897", "代理位址，例如 http://127.0.0.1:7897"), previous)
            }, values => Uri.TryCreate(values[0].Trim(), UriKind.Absolute, out var proxy) && (proxy.Scheme == "http" || proxy.Scheme == "https") && proxy.UserInfo.Length == 0
                ? null : Loc.T("Enter an HTTP(S) proxy address without a username or password.", "请填写不包含用户名和密码的 HTTP(S) 代理地址。", "請填寫不含使用者名稱和密碼的 HTTP(S) 代理位址。"),
                values => OverviewUpdateQueue.SetNetwork("custom", values[0].Trim()));
        });
    }

    private static Button AddButton(Transform parent, string title, Action action, float width, UiActionRole role)
    {
        var button = OverviewUi.Button(parent, title, action, width, 30, role);
        var caption = button.GetComponentInChildren<TextMeshProUGUI>(true);
        caption.enableAutoSizing = true; caption.fontSizeMin = 10; caption.fontSizeMax = 14;
        caption.overflowMode = TextOverflowModes.Ellipsis;
        return button;
    }

    private void ConfigureNavigation()
    {
        if (_embedded) PublicationNavigation.Attach(_root);
        else PublicationNavigation.AttachBoundary(_root);
    }

    /// <summary>"1.2 MB/s | 3.4 秒" from what this download actually moved; empty for a reused cache, folder jobs and old records.</summary>
    internal static string DownloadNote(UpdateJob job)
    {
        if (job.Layout || job.DownloadTransferredBytes <= 0 || job.DownloadSeconds <= 0.05) return "";
        double rate = job.DownloadTransferredBytes / job.DownloadSeconds;
        string speed = rate >= 1024 * 1024 ? (rate / (1024 * 1024)).ToString(rate >= 10 * 1024 * 1024 ? "0" : "0.0") + " MB/s"
            : Math.Max(1, rate / 1024).ToString("0") + " KB/s";
        int whole = (int)Math.Round(job.DownloadSeconds);
        string time = job.DownloadSeconds < 10 ? job.DownloadSeconds.ToString("0.0") + Loc.T("s", " 秒", " 秒")
            : whole < 60 ? whole + Loc.T("s", " 秒", " 秒")
            : (whole / 60) + Loc.T("m", " 分 ", " 分 ") + (whole % 60).ToString("00") + Loc.T("s", " 秒", " 秒");
        return speed + " | " + time;
    }

    /// <summary>历史条目显示本地时间的更新日期；记录缺时间时留空。</summary>
    private static string UpdatedAt(UpdateJob job)
    {
        string raw = string.IsNullOrWhiteSpace(job.UpdatedUtc) ? job.CreatedUtc : job.UpdatedUtc;
        return DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var utc)
            ? utc.ToLocalTime().ToString("MM-dd HH:mm") : "";
    }

    internal static string JobName(UpdateJob job) => (job.Uninstall ? Loc.T("Uninstall: ", "卸载：", "解除安裝：") : job.LocalSource.Length > 0 ? Loc.T("Local import: ", "本地导入：", "本機匯入：") : job.Layout ? Loc.T("Organize: ", "整理：", "整理：") : "") + (string.IsNullOrWhiteSpace(job.DisplayName) ? job.Package : job.DisplayName);

    /// <summary>解析目标版本前目标列为空，旧脏数据也可能没写入——空目标不渲染半截箭头。</summary>
    /// <summary>只有两侧版本都存在且不同时才画箭头；相同或缺失时只显示一个版本号，完全没有版本信息就留白。</summary>
    internal static string VersionLine(UpdateJob job)
    {
        if (job.Layout) return job.TargetVersion + " " + Loc.T("folders", "个目录", "個目錄");
        string installed = job.InstalledVersion.Trim(), target = job.TargetVersion.Trim();
        if (installed.Length == 0 && target.Length == 0) return "";
        if (installed.Length == 0 || target.Length == 0) return installed.Length > 0 ? installed : target;
        if (string.Equals(installed, target, StringComparison.OrdinalIgnoreCase)) return installed;
        bool rollback = job.RollbackRequested || job.State == UpdateStates.RollingBack || job.State == UpdateStates.RollbackWaiting || job.State == UpdateStates.RolledBack;
        return rollback ? target + " → " + installed : installed + " → " + target;
    }

    private static readonly string GreenCheck = "<color=#" + ColorUtility.ToHtmlStringRGB(OverviewUi.Green) + ">✓</color>";
    private static string Checked(string text) => text + " " + GreenCheck;
    /// <summary>Popups and status lines are plain TMP texts: drop the markup and keep the glyph.</summary>
    private static string Plain(string text) => text.Replace(GreenCheck, "✓");
    internal static string StateTextPlain(string state) => Plain(StateText(state));

    internal static string StateText(string state) => state switch {
        "draft" => Loc.LocalizeMessage("Ready to start updates"),
        "submitting" => Loc.LocalizeMessage("Submitted; waiting for worker acknowledgement."),
        UpdateStates.Queued => Loc.T("Queued", "等待更新", "等待更新"),
        UpdateStates.Resolving => Loc.T("Reading package", "读取更新包", "讀取更新套件"),
        UpdateStates.Downloading => Loc.T("Downloading", "正在下载", "正在下載"),
        UpdateStates.Paused => Loc.T("Paused", "已暂停", "已暫停"),
        UpdateStates.Verifying => Loc.T("Verifying", "正在检查下载", "正在檢查下載"),
        UpdateStates.Preparing => Loc.T("Preparing files", "正在准备文件", "正在準備檔案"),
        UpdateStates.WaitingExit => Checked(Loc.T("Downloaded · deploys after the game exits", "下载成功 · 等待退出游戏后部署", "下載成功 · 等待結束遊戲後部署")),
        UpdateStates.Deploying => Loc.T("Installing", "正在安装", "正在安裝"),
        UpdateStates.PendingRestart => Loc.T("Downloaded ", "下载成功 ", "下載成功 ") + GreenCheck + Loc.T(" · deployed ", " · 已部署 ", " · 已部署 ") + GreenCheck + Loc.T(" · restart to apply", " · 等待重启生效", " · 等待重新啟動生效"),
        UpdateStates.Completed => Checked(Loc.T("Applied", "已生效", "已生效")),
        UpdateStates.Superseded => Loc.T("Replaced by a later local build", "已由后续本机安装接替", "已由後續本機安裝接替"),
        UpdateStates.Stopped => Loc.T("Stopped", "已停止", "已停止"),
        UpdateStates.Failed => Loc.T("Failed · retry available", "更新失败 · 可重试", "更新失敗 · 可重試"),
        UpdateStates.RollingBack => Loc.T("Rolling back", "正在回退", "正在還原"),
        UpdateStates.RollbackWaiting => Loc.T("Roll back after exit", "退出游戏后回退", "離開遊戲後還原"),
        UpdateStates.RolledBack => Checked(Loc.T("Rolled back · original restored", "已回退 · 原状已恢复", "已還原 · 原狀已復原")),
        _ => state
    };

    public void Dispose()
    {
        Close(false);
        if (_root != null) { _root.gameObject.SetActive(false); UnityEngine.Object.Destroy(_root.gameObject); }
        _rows.Clear();
    }
}

internal sealed class UpdateQueuePointer : MaskableGraphic
{
    public override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var rect = rectTransform.rect;
        vh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMax, rect.center.y), color, Vector2.zero);
        vh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
        vh.AddTriangle(0, 1, 2);
    }
}
