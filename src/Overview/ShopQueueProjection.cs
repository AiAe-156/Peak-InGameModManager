#nullable enable
using System;
using ModConfigEnhance.Updating;
namespace ModConfigEnhance.Overview;
internal static class ShopQueueProjection
{
    internal static bool IsReview(ShopTask task) => task.State == "ready" && task.Request.Operation is "layout-preview" or "local-preview";
    internal static UpdateJob ToJob(ShopTask task)
    {
        // A layout/local preview that finished its analysis is waiting for the user's review, not preparing anything; showing it
        // as Preparing made it look stuck and counted it as pending work, which kept the helper being started.
        string state=IsReview(task)?UpdateStates.Paused:task.State switch {"cancelled"=>UpdateStates.Stopped,"completed"=>UpdateStates.Completed,"failed"=>UpdateStates.Failed,"recovery-required"=>UpdateStates.RollbackWaiting,"committing"=>UpdateStates.Deploying,"waiting-exit"=>UpdateStates.WaitingExit,"downloading"=>UpdateStates.Downloading,"requested"=>UpdateStates.Resolving,_=>UpdateStates.Preparing};
        return new UpdateJob{Id=task.Id,Namespace=task.Request.Namespace,Package=task.Request.Package,DisplayName=task.Request.Operation.StartsWith("local-",StringComparison.Ordinal)?Loc.T("Local mod import","本地模组导入","本機模組匯入"):task.Request.Operation.StartsWith("layout-",StringComparison.Ordinal)?Loc.T("Folder organization","目录整理","目錄整理"):(task.Request.Operation.Contains("uninstall")?Loc.T("Uninstall: ","卸载：","解除安裝："):Loc.T("Store: ","商店：","商店："))+task.Request.Namespace+"-"+task.Request.Package,TargetVersion=task.Request.Version,State=state,Message=task.Message,Error=task.Error,CreatedUtc=task.CreatedUtc,UpdatedUtc=task.CreatedUtc,DownloadedBytes=task.DownloadedBytes,TotalBytes=task.TotalBytes,Progress=task.TotalBytes>0?(double)task.DownloadedBytes/task.TotalBytes:0,StoreOperation=true,RecoveryRequired=task.State=="recovery-required",Homepage=task.Request.Namespace.Length==0?"":ThunderstoreUpdateService.HomeUrl(task.Request.Namespace,task.Request.Package)};
    }
}
