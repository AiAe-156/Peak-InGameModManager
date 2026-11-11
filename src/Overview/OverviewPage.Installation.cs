#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewPage
{
    private string _installationExpected = "", _installationSeen = "";
    private float _installationPoll, _installationStarted;
    private string _installationPreviousTask = "";
    private void TrackInstallationPreview(string operation)
    {
        _installationExpected = operation; _installationSeen = ""; _installationStarted = Time.unscaledTime;
        try { _installationPreviousTask = JsonConvert.DeserializeObject<ShopTask>(File.ReadAllText(Path.Combine(OverviewUpdateQueue.Root, "shop", "task.json")))?.Id ?? ""; }
        catch { _installationPreviousTask = ""; }
    }
    private void OpenLocalImport()
    {
        string selected = "";
        IntroductionFilePicker.TrySelect("mod", out string? path);
        if (!string.IsNullOrEmpty(path)) selected = path;
        OverviewUi.Ask(Loc.T("Import local mod", "导入本地模组", "匯入本機模組"), new[] {
            (Loc.T("DLL, ZIP or folder path", "DLL、ZIP 或文件夹路径", "DLL、ZIP 或資料夾路徑"), selected),
            (Loc.T("Independent package folder", "独立模组文件夹名称", "獨立模組資料夾名稱"), Path.GetFileNameWithoutExtension(selected))
        }, values => ManagementAction(() => {
            OverviewUpdateQueue.SendLocalImport(new LocalImportRequest { SourcePath = values[0].Trim().Trim('"'), PackageFolder = values[1].Trim() });
            SetStatus(Loc.LocalizeMessage("Local import queued independently. Follow progress in Downloads and operations."));
        }));
    }
    /// <summary>2.3.0: one organizer — whole mod folders move, names unchanged, into folders that mirror the (nested)
    /// group tree; each overview row goes where the user sees it grouped.</summary>
    private void PreviewFolderLayout()
    {
        var applied = new OverviewRepository(appliedView: true);
        var rows = new OverviewCatalogue().Scan(applied);
        var request = new LayoutQueueRequest {
            Mode = InstallationLayoutMode.GroupOnly,
            AppliedProfileId = applied.Profiles.AppliedId,
            UngroupedFolder = Loc.T("Ungrouped", "未分组", "未分組"),
            Groups = applied.State.groups.Select(g => new LayoutQueueGroup { Id = g.id, Name = g.name, ParentId = g.parentId ?? "" }).ToList(),
            Rows = rows.Select(m => new LayoutQueueRow { Guids = m.PluginGuids.Concat(new[] { m.Guid }).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                GroupId = m.Rule.groupId ?? "", PackageRoot = m.PackageRoot, Version = m.DisplayVersion, Loaded = m.Loaded }).ToList()
        };
        request.AppliedSnapshotPath = Path.Combine(applied.Profiles.RuntimeDirectory, "applied-profile.json");
        if (!File.Exists(request.AppliedSnapshotPath)) throw new IOException("Applied profile snapshot missing; apply a profile first.");
        request.AppliedSnapshotHash = WorkerProtocol.Hash(request.AppliedSnapshotPath);
        TrackInstallationPreview("layout-preview"); OverviewUpdateQueue.SendLayout(request);
        ManagementStatus(Loc.T("Preparing folder preview…", "正在准备目录预览……", "正在準備目錄預覽……"));
    }
    private void PollInstallationPreview()
    {
        if (_installationExpected.Length == 0 || Time.unscaledTime < _installationPoll) return;
        _installationPoll = Time.unscaledTime + .5f;
        string file = Path.Combine(OverviewUpdateQueue.Root, "shop", "task.json");
        try
        {
            // A preview command waits in the worker's inbox while updates are running, so the deadline only counts
            // from when the worker actually starts preparing; before that the status line says it is waiting.
            if (Time.unscaledTime - _installationStarted > 60 && _installationSeen == "")
            {
                ManagementStatus(Loc.T("Waiting for running updates to finish before preparing the preview…", "正在等待当前更新结束后再准备预览……", "正在等待目前更新結束後再準備預覽……"));
                _installationStarted = Time.unscaledTime;
            }
            if (!File.Exists(file)) return;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var task = JsonConvert.DeserializeObject<ShopTask>(reader.ReadToEnd());
            if (OverviewUpdateQueue.LastError.Length > 0)
            { _installationExpected = ""; throw new IOException(OverviewUpdateQueue.LastError); }
            if (task == null || task.Id == _installationPreviousTask || task.Request.Operation != _installationExpected || task.Id == _installationSeen) return;
            if (task.State == "failed") { _installationExpected = ""; OverviewUi.ShowText(task.Message, task.Error); return; }
            if (task.State == "cancelled") { _installationExpected = ""; ManagementStatus(Loc.LocalizeMessage(task.Message)); return; }
            // The worker publishes each preview phase into the task, so the click no longer looks frozen.
            if (task.State != "ready")
            { if (!string.IsNullOrEmpty(task.Message)) ManagementStatus(task.Message); return; }
            _installationSeen = task.Id; _installationExpected = "";
            // A plan with blockers can never be confirmed; close it so it does not sit "ready", then show why.
            if (task.Blockers.Count > 0) CancelInstallationPreview(task);
            ShowInstallationReview(task);
        }
        catch (Exception error) { SetStatus(Loc.LocalizeMessage(error.Message)); }
    }

    private static ShopTask? ReadStoreTask()
    {
        string file = Path.Combine(OverviewUpdateQueue.Root, "shop", "task.json");
        if (!File.Exists(file)) return null;
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return JsonConvert.DeserializeObject<ShopTask>(reader.ReadToEnd());
    }

    /// <summary>Opened from the queue row of a folder-organization or local-import task.</summary>
    internal void ShowInstallationReview(string id)
    {
        try
        {
            var task = ReadStoreTask();
            if (task == null || task.Id != id || task.Dismissed)
            { SetStatus(Loc.T("This task has ended or was replaced by a newer preview.", "该任务已结束，或已被新的预览替换。", "該工作已結束，或已被新的預覽取代。")); return; }
            ShowInstallationReview(task);
        }
        catch (Exception error) { SetStatus(Loc.LocalizeMessage(error.Message)); }
    }

    /// <summary>One window for every state of a folder/import task: confirm or clear a ready plan, stop one being prepared,
    /// or look at a finished one and clear it or start over. "Back" keeps the task; the queue row reopens this window.</summary>
    private void ShowInstallationReview(ShopTask task)
    {
        bool layout = task.Request.Operation.StartsWith("layout-", StringComparison.Ordinal);
        bool finished = task.State is "completed" or "failed" or "cancelled";
        bool review = ShopQueueProjection.IsReview(task);
        bool confirmable = review && task.Blockers.Count == 0;
        string title = layout ? Loc.T("Folder organization", "目录整理", "目錄整理") : Loc.T("Local mod import", "本地模组导入", "本機模組匯入");
        string state = review ? (confirmable ? Loc.T("Preview ready · awaiting confirmation", "预览完成 · 等待确认", "預覽完成 · 等待確認") : Loc.T("Needs attention", "需要处理", "需要處理"))
            : task.State switch {
                "completed" => Loc.T("Done", "已完成", "已完成"),
                "failed" => Loc.T("Failed", "失败", "失敗"),
                "cancelled" => Loc.T("Stopped", "已停止", "已停止"),
                _ => Loc.T("Preparing the preview", "正在准备预览", "正在準備預覽")
            };
        var body = new List<string> { Loc.T("Status: ", "状态：", "狀態：") + state };
        string message = Loc.LocalizeMessage(task.Message) ?? "";
        if (message.Length > 0) body.Add(message);
        if (task.Error.Length > 0) body.Add(Loc.LocalizeMessage(task.Error) ?? task.Error);
        if (task.Blockers.Count > 0) body.Add(Loc.T("Blocked:", "无法继续：", "無法繼續：") + "\n" + string.Join("\n", task.Blockers));
        bool drawTree = layout && task.Tree.Count > 0;
        if (!drawTree && (task.Tree.Count > 0 || task.Details.Count > 0 || task.Desired.Count > 0)) body.Add(LayoutPreviewText(task));
        if (confirmable && !drawTree) body.Add(Loc.T("The work is queued as a job and runs after PEAK exits; follow it in Downloads and operations.", "该操作会作为任务入队，在游戏退出后执行；进度见“下载与操作”。", "該操作會作為工作入列，在遊戲結束後執行；進度見「下載與操作」。"));
        // 2.3.1: the folder tree gets nearly the whole screen; the plain-text variants keep their size.
        var panel = OverviewUi.CreateModalPanel("MCE_InstallationReview", drawTree ? 1680 : 860, drawTree ? 1300 : 560); if (panel == null) return;
        LayoutReviewView? tree = null;
        OverviewUi.SaveDialogHeader(panel, title, drawTree && confirmable
            ? Loc.T("Confirm to organize after PEAK exits (progress in Downloads and operations), clear the task, or go back.", "确认后在游戏退出时整理（进度见“下载与操作”），也可清除任务或返回。", "確認後在遊戲結束時整理（進度見「下載與操作」），也可清除工作或返回。")
            : confirmable
            ? Loc.T("Confirm to organize, clear the task, or go back and decide later.", "可确认整理、清除任务，或返回稍后再决定。", "可確認整理、清除工作，或返回稍後再決定。")
            : finished ? Loc.T("Clear this task from the queue or start over.", "可从队列清除此任务，或重新开始。", "可從佇列清除此工作，或重新開始。")
            : review ? Loc.T("This plan cannot be confirmed; clear it and preview again after fixing the items above.", "此计划无法确认；处理上面的问题后清除并重新预览。", "此計畫無法確認；處理上面的問題後清除並重新預覽。")
            : Loc.T("The helper is still analysing; you can stop it.", "更新助手仍在分析，可以停止。", "更新助手仍在分析，可以停止。"), compactSummary: drawTree, dividerY: drawTree ? 84 : 109);
        // A layout plan is drawn as a framed tree; everything else (and plans from before the tree existed) stays text.
        if (drawTree) tree = LayoutReviewView.Show(panel, task.Tree, body.Select((line, index) => (line, index > 0 && (task.Error.Length > 0 || task.Blockers.Count > 0) && line != message)).ToList(), confirmable);
        else OverviewUi.SaveDialogBody(panel, string.Join("\n\n", body), attention: !finished);
        var buttons = new List<(string Label, Action Action, UiActionRole Role)>();
        if (confirmable) buttons.Add((layout ? Loc.T("Organize", "确认整理", "確認整理") : Loc.T("Import", "确认导入", "確認匯入"), () => ManagementAction(() => {
            if (!layout) OverviewUpdateQueue.SendLocalImport(new LocalImportRequest { Operation = "confirm", PreviewId = task.Id, PlanToken = task.PlanToken });
            else OverviewUpdateQueue.SendLayout(new LayoutQueueRequest { Operation = "confirm", PreviewId = task.Id, PlanToken = task.PlanToken, RecycleEmpty = tree?.RecycleSelection() ?? new List<string>() });
            OverviewUi.CloseModal();
            SetStatus(Loc.T("Queued. Progress is in Downloads and operations.", "已加入队列，进度见“下载与操作”。", "已加入佇列，進度見「下載與操作」。"));
        }), UiActionRole.Commit));
        if (finished) buttons.Add((layout ? Loc.T("Organize again", "重新整理", "重新整理") : Loc.T("Import again", "重新导入", "重新匯入"), () => {
            OverviewUi.CloseModal(); if (layout) OpenManagement(ManagementTab.Tools); else OpenLocalImport();
        }, UiActionRole.Edit));
        if (review || finished) buttons.Add((Loc.T("Clear task", "清除任务", "清除工作"), () => {
            OverviewUi.CloseModal(); DismissInstallationTask(task.Id);
        }, UiActionRole.Neutral));
        if (!review && !finished) buttons.Add((Loc.T("Stop", "停止", "停止"), () => {
            OverviewUi.CloseModal(); StopInstallationTask(task);
        }, UiActionRole.Neutral));
        buttons.Add((Loc.T("Back", "返回", "返回"), OverviewUi.CloseModal, UiActionRole.Neutral));
        // Right-aligned: Back sits at the far right, the primary action furthest left.
        float right = 20; Button? back = null;
        for (int i = buttons.Count - 1; i >= 0; i--)
        {
            float width = i == buttons.Count - 1 ? 100 : 140;
            var button = OverviewUi.SaveDialogAction(panel, buttons[i].Label, buttons[i].Action, right, width, buttons[i].Role);
            if (i == buttons.Count - 1) back = button;
            right += width + 10;
        }
        if (back != null) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);
    }

    private void DismissInstallationTask(string id)
    {
        OverviewUpdateQueue.Send("shop-dismiss", id);
        SetStatus(OverviewUpdateQueue.LastError.Length > 0 ? Loc.LocalizeMessage(OverviewUpdateQueue.LastError)
            : Loc.T("Clearing the task; no folders were changed.", "正在清除任务，未改动任何目录。", "正在清除工作，未變更任何目錄。"));
    }

    /// <summary>A folder preview being analysed closes through the layout cancel; a ready local import through its
    /// cancel; a local import still staging through the preparation stop.</summary>
    internal static void StopInstallationTask(ShopTask task)
    {
        try
        {
            if (task.Request.Operation.StartsWith("layout-", StringComparison.Ordinal)) OverviewUpdateQueue.SendLayout(new LayoutQueueRequest { Operation = "cancel", PreviewId = task.Id });
            else if (ShopQueueProjection.IsReview(task)) OverviewUpdateQueue.SendLocalImport(new LocalImportRequest { Operation = "cancel", PreviewId = task.Id });
            else OverviewUpdateQueue.Send("stop", task.Id);
        }
        catch (Exception error) { Plugin.Log.LogWarning("[目录整理] 停止任务失败：" + error.Message); }
    }

    /// <summary>Closing the review dialog without confirming used to leave the preview "ready" in the worker, which the
    /// queue shows as "preparing files" forever with no way to dismiss it (and which kept the helper starting).</summary>
    private static void CancelInstallationPreview(ShopTask task)
    {
        try
        {
            if (task.Request.Operation == "layout-preview") OverviewUpdateQueue.SendLayout(new LayoutQueueRequest { Operation = "cancel", PreviewId = task.Id });
            else if (task.Request.Operation == "local-preview") OverviewUpdateQueue.SendLocalImport(new LocalImportRequest { Operation = "cancel", PreviewId = task.Id });
        }
        catch (Exception error) { Plugin.Log.LogWarning("[目录整理] 关闭预览失败：" + error.Message); }
    }

    /// <summary>The plugins tree the plan produces, drawn from the worker's structured result so each package
    /// shows where it lands, where it came from, and which source the manager knows it by. Falls back to the flat
    /// detail lines for a plan produced before the tree existed.</summary>
    private string LayoutPreviewText(ShopTask task)
    {
        if (task.Tree.Count == 0)
            return string.Join("\n", task.Details.Take(50)) + "\n\n" + string.Join("\n", task.Desired.Take(60).Select(f => "→ " + f.Path));
        var lines = new List<string> { "plugins/" };
        var groups = task.Tree.GroupBy(n => n.Group, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key.Length == 0 ? 0 : 1).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
        for (int index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            bool lastGroup = index == groups.Count - 1;
            if (group.Key.Length > 0) lines.Add((lastGroup ? "└─ " : "├─ ") + group.Key + "/");
            string indent = group.Key.Length > 0 ? lastGroup ? "    " : "│   " : "";
            var nodes = group.OrderBy(n => n.Folder, StringComparer.OrdinalIgnoreCase).ToList();
            for (int node = 0; node < nodes.Count; node++)
                lines.Add(indent + (node == nodes.Count - 1 ? "└─ " : "├─ ") + nodes[node].Folder + "/  " + LayoutNodeNote(nodes[node]));
        }
        return string.Join("\n", lines);
    }

    private static string LayoutNodeNote(LayoutTreeNode node)
    {
        if (node.Disposition == InstallationLayoutDisposition.Deferred || node.Disposition == InstallationLayoutDisposition.Rejected)
            return Loc.T("skipped", "跳过", "跳過") + (node.Reasons.Length > 0 ? "（" + node.Reasons + "）" : "");
        string source = node.Source switch
        {
            LayoutSourceKind.Local => Loc.T("local", "本地", "本機"),
            LayoutSourceKind.Store => Loc.T("store-confirmed", "商店已确认", "商店已確認"),
            _ => Loc.T("unverified", "待定", "待定")
        };
        if (node.Disposition == InstallationLayoutDisposition.Unchanged) return Loc.T("already in place", "原地未动", "原地未動") + " · " + source;
        if (node.Disposition == InstallationLayoutDisposition.Move) return Loc.T("moves in from ", "移入自 ", "移入自 ") + node.OriginalPath + " · " + source;
        return node.Disposition.ToString();
    }
}
