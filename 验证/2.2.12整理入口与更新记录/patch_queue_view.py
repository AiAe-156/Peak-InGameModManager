from pathlib import Path
import sys
root = Path(sys.argv[1])
def sub(path, old, new, count=1):
    p = root / path; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (path, old[:60], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
v = 'src/Overview/UpdateQueueView.cs'
sub(v, '    private Action? _networkRequested;\n', '    private Action? _networkRequested;\n    private readonly Action<string>? _review;\n')
sub(v, 'Action<string> status, Action? network = null, Action? save = null)\n    {\n        _frame = frame; _anchor = anchor; _status = status; _networkRequested = network;',
       'Action<string> status, Action? network = null, Action? save = null, Action<string>? review = null)\n    {\n        _frame = frame; _anchor = anchor; _status = status; _networkRequested = network; _review = review;')
old = '''            // A folder/local preview waiting for review has nothing to show on a store page; offer to close it instead.
            if(job.StoreOperation&&job.Namespace.Length==0){actions.Clear();
                if(job.State is not (UpdateStates.Completed or UpdateStates.Failed or UpdateStates.Stopped))actions.Add((Loc.T("Close preview","关闭预览","關閉預覽"),"preview-cancel",UiActionRole.Neutral));}'''
new = '''            // Folder organization / local import has no store page: its row opens its own review window instead.
            if(IsInstallationTask(job)){actions.Clear();
                bool ended=job.State is UpdateStates.Completed or UpdateStates.Failed or UpdateStates.Stopped;
                if(job.State==UpdateStates.Paused){actions.Add((Loc.T("Review and confirm","查看并确认","檢視並確認"),"installation-review",UiActionRole.Commit));actions.Add((Loc.T("Clear task","清除任务","清除工作"),"installation-dismiss",UiActionRole.Neutral));}
                else if(ended){actions.Add((Loc.T("View","查看","檢視"),"installation-review",UiActionRole.Edit));actions.Add((Loc.T("Clear task","清除任务","清除工作"),"installation-dismiss",UiActionRole.Neutral));}
                else{actions.Add((Loc.T("View progress","查看进度","檢視進度"),"installation-review",UiActionRole.Edit));actions.Add((Loc.T("Stop","停止","停止"),"installation-stop",UiActionRole.Neutral));}}'''
sub(v, old, new)
sub(v, 'button.interactable = command is "homepage" or "store-page" or "open-folder" ||',
       'button.interactable = command is "homepage" or "store-page" or "open-folder" or "installation-review" or "installation-dismiss" ||')
old = '''        if(action=="preview-cancel"){
            try{
                var task=Newtonsoft.Json.JsonConvert.DeserializeObject<ShopTask>(System.IO.File.ReadAllText(System.IO.Path.Combine(OverviewUpdateQueue.Root,"shop","task.json")));
                if(task==null||task.Id!=job.Id)return;
                if(task.Request.Operation.StartsWith("layout-",StringComparison.Ordinal))OverviewUpdateQueue.SendLayout(new LayoutQueueRequest{Operation="cancel",PreviewId=job.Id});
                else OverviewUpdateQueue.SendLocalImport(new LocalImportRequest{Operation="cancel",PreviewId=job.Id});
                _status(Loc.T("Closing the preview; no files were changed.","正在关闭预览，未改动任何文件。","正在關閉預覽，未變更任何檔案。"));
            }catch(Exception error){_status(Loc.LocalizeMessage(error.Message));}
            return;
        }'''
new = '''        if(action=="installation-review"){Review(job.Id);return;}
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
        }'''
sub(v, old, new)
old = '''    /// <summary>商店按钮优先打开游戏内商店里该包的详情页；商店列表里没有它（本地安装、已下架等）就退回打开安装目录。</summary>
    private void OpenStorePage(UpdateJob job)
    {
        try'''
new = '''    /// <summary>A folder-organization or local-import task: a store-queue job with no package identity.</summary>
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
        try'''
sub(v, old, new)
sub(v, '        if(job.StoreOperation){OpenStorePage(job);return;}\n        OverviewUi.ShowText(',
       '        if(IsInstallationTask(job)){_review?.Invoke(id);return;}\n        if(job.StoreOperation){OpenStorePage(job);return;}\n        OverviewUi.ShowText(')
# page wiring
sub('src/Overview/OverviewPage.Updates.cs',
    'ManagementStatus, () => OpenManagement(ManagementTab.Network), () => OverviewUpdateQueue.StartPendingUpdates());',
    'ManagementStatus, () => OpenManagement(ManagementTab.Network), () => OverviewUpdateQueue.StartPendingUpdates(), ShowInstallationReview);')
print('ok')
