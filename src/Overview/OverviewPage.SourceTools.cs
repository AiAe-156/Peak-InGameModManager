#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewPage
{
    private Button _sourceTools = null!;
    private OverviewIconGraphic _sourceToolsRefresh = null!;
    private Button _sourceToolsRefreshButton = null!;
    private RefreshIconFeedback _refreshIconFeedback = null!;
    private TextMeshProUGUI _sourceToolsCaret = null!;
    private string AutomaticUpdateHint() => Loc.T("Automatic update checks every 30 minutes, only in the main menu or Airport. Results are cached; nothing is downloaded or installed automatically.",
        "自动检查更新开关：每30分钟在主菜单或机场候机厅后台检查，缓存结果；不会自动下载或安装。",
        "自動檢查更新：每30分鐘在主選單或機場候機廳背景檢查並快取，不自動下載或安裝。") + "\n\n" + AutomaticUpdateChecks.StatusText();
    private bool _sourceToolsBusy;
    private void BuildSourceToolsIcon()
    {
        var hit = OverviewUi.Rect("UpdateCycleHit", _sourceTools.transform);
        var background = hit.gameObject.AddComponent<Image>(); background.color = Color.clear; background.raycastTarget = true;
        _sourceToolsRefreshButton = hit.gameObject.AddComponent<Button>(); _sourceToolsRefreshButton.targetGraphic = background;
        _sourceToolsRefreshButton.onClick.AddListener(() => { if (_sourceToolsBusy) { SetStatus(_tab != PageTab.Mods ? Loc.T("Update checks run from the mod list.", "检查更新需在模组列表页进行。", "檢查更新需在模組列表頁進行。") : Loc.T("An update check is already running.", "正在检查更新，请稍候。", "正在檢查更新，請稍候。")); return; } ManagementAction(() => { _list?.CheckUpdates(true); }); });
        AttachToolHint(hit.gameObject, UpdateCheckHint);
        GamepadSupport.MarkSelectable(hit.gameObject, null, hit);
        var icon = OverviewUi.Rect("UpdateCycle", hit); OverviewUi.Fill(icon, 6);
        _sourceToolsRefresh = icon.gameObject.AddComponent<OverviewIconGraphic>();
        _sourceToolsRefresh.Kind = "refresh"; _sourceToolsRefresh.color = OverviewUi.Ivory; _sourceToolsRefresh.raycastTarget = false;
        _refreshIconFeedback = hit.gameObject.AddComponent<RefreshIconFeedback>(); _refreshIconFeedback.Bind(_sourceToolsRefresh);
        _sourceToolsCaret = OverviewUi.Text(_sourceTools.transform, OverviewUi.Caret, 12);
        _sourceToolsCaret.alignment = TextAlignmentOptions.Midline;
    }
    private void LayoutSourceTools(float width)
    {
        var caption = _sourceTools.GetComponentInChildren<TextMeshProUGUI>(true);
        caption.margin = new Vector4(12, 0, 56, 0); caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.textWrappingMode = TextWrappingModes.NoWrap; caption.overflowMode = TextOverflowModes.Ellipsis;
        OverviewUi.Place((RectTransform)_sourceToolsRefreshButton.transform, width - 54, 1, 32, 32);
        _sourceToolsCaret.text = OverviewUi.Caret; OverviewUi.PlaceClipped(_sourceToolsCaret, width - 19, 0, 12, 34);
        RefreshSourceToolsIcon();
    }
    private void RefreshSourceToolsIcon()
    {
        if (_sourceToolsRefresh == null) return;
        // Stays focusable; while a check runs the click only reports it (1.1.5: a non-interactable button cannot hold the focus).
        _sourceToolsRefreshButton.interactable = true;
        _sourceToolsBusy = !(_tab == PageTab.Mods && _list != null && !_list.IsCheckingUpdates && !AutomaticUpdateChecks.IsChecking);
        Color color = AutomaticUpdateChecks.IsChecking || _list?.IsCheckingUpdates == true ? OverviewUi.Green : OverviewUi.Ivory;
        _refreshIconFeedback.SetAppearance(!_sourceToolsBusy, color);
    }
    private void SourceTools()
    {
        _list?.HideAllHints(); ClearToolHint();
        SourceToolOption Action(string label, Action invoke, bool enabled = true, bool check = false, Func<string>? hint = null)
            => new() { Entry = new UiMenuEntry { Label = label, Enabled = enabled, Checked = check, Invoke = () => ManagementAction(invoke) }, Hint = hint };
        SourceToolOption Switch(string label, Func<bool> read, Action<bool> write, Func<string>? hint = null)
            => new() { Entry = new UiMenuEntry { Label = label }, ReadValue = read, WriteValue = value => ManagementAction(() => write(value)), Hint = hint };
        var groups = new[] {
            new SourceToolGroup(T("Source", "来源"), OverviewUi.Blue, new[] {
                Action(T("Find all missing sources", "批量查找缺失来源"), () => _list?.BatchFindSources(), _list != null),
                Switch(T("Show source labels", "显示来源标签"), () => OverviewOptions.ShowSourceLabels.Value,
                    value => { OverviewOptions.ShowSourceLabels.Value = value; _list?.RefreshTagFilter(); })
            }),
            new SourceToolGroup(T("Updates", "更新"), OverviewUi.Green, new[] {
                Action(T("Check all updates", "全局检查更新"), () => _list?.CheckUpdates(true), _tab == PageTab.Mods && _list != null,
                    hint: UpdateCheckHint),
                Switch(T("Automatic update checks", "自动检查更新"), () => OverviewOptions.AutomaticUpdates.Value,
                    value => { OverviewOptions.AutomaticUpdates.Value = value; RefreshHeader(); }, AutomaticUpdateHint),
                Action(T("Global update permission", "全局更新检查许可"), () => _list?.SetGlobalAllowed(!_repo.GetGlobalUpdateAllowed()),
                    _list != null, _repo.GetGlobalUpdateAllowed())
            }),
            new SourceToolGroup(Loc.T("Change history", "变更记录", "變更記錄"), OverviewUi.Gold, new[] {
                Action(T("Review update changes", "查看更新变动"), () => OverviewUi.ShowText(T("Update changes", "更新变动"), ChangeLedger.Summary())),
                Action(T("Mark all notification badges as read", "所有小角标标记为已读"), () => {
                    ChangeLedger.Clear(); ManagementStatus(T("All unread notification badges marked as read. Update availability and unsaved drafts are retained.", "所有未读小角标已标记为已读；可更新提示和未保存草稿保留。"));
                })
            })
        };
        _toolbarPopup = OverviewUi.SourceToolsDropdown(ToolAnchor(_sourceTools), groups, AttachToolHint);
    }
}
internal sealed partial class OverviewListView
{
    internal bool IsCheckingUpdates => _checking;
    private int _sourceFilter = -1;
    internal void SetSourceFilter(int value) { _sourceFilter = value; Render(); }
    internal async void BatchFindSources()
    {
        if (_sourceRetrying || _disposed) return;
        var targets = _mods.ToArray();
        var before = SourceIdentificationBatch.CaptureBefore(targets);
        string context = ModSources.Context;
        _sourceRetrying = true;
        var panel = OverviewUi.CreateModalPanel("MCE_SourceBatchProgress", 580, 280); if (panel == null) { _sourceRetrying = false; return; }
        var progress = OverviewUi.Text(panel, T("Matching all missing sources…", "正在批量匹配缺失来源……"), 22); OverviewUi.Fill(progress.rectTransform, 24);
        var cancel = OverviewUi.Button(panel, T("Cancel", "取消"), OverviewUi.CloseModal, 100, 34, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)cancel.transform, 24, 16);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); panel.gameObject.AddComponent<SourceSearchLifetime>().Cancellation = cancellation;
        try
        {
            _status(T("Reading store catalog…", "正在读取商店目录……"));
            var catalog = await ModSources.Catalog(cancellation.Token, true);
            if (_disposed || !_opened || panel == null || cancellation.IsCancellationRequested) return;
            await ModSources.Coordinator.WaitAsync(cancellation.Token);
            try { await ModSources.Discover(targets, cancellation.Token, true, catalog); }
            finally { ModSources.Coordinator.Release(); }
            if (_disposed || !_opened || panel == null || cancellation.IsCancellationRequested) return;
            if (context != ModSources.Context) return;
            var result = new SourceIdentificationBatch(targets, before, catalog, context);
            Reload(); SourceUi.BatchResults(_repo, result, Reload, _status);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { _status(e.Message); if (panel != null) { progress.text = e.Message; OverviewUi.PlaceClipped(progress, 24, 24, panel.rect.width - 48, panel.rect.height - 100); } }
        finally { _sourceRetrying = false; }
    }
}
