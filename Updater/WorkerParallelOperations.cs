using ModConfigEnhance.Updating;
namespace MCE.UpdateWorker;
internal sealed partial class QueueWorker
{
    readonly Dictionary<string,ShopQueue> operationQueues = new(StringComparer.Ordinal);
    readonly SemaphoreSlim operationSlots = new(4);
    string OperationsHome => Path.Combine(home,"operations");

    /// <summary>Install/update items (normal jobs and store preview-install requests) become queue jobs; uninstall and local import keep their review flow.</summary>
    void AcceptIndependentOperations(UpdateCommand command)
    {
        var commands=command.ResourceCommands??throw new IOException("Missing update request.");
        List<InstalledPackage>? inventory=null;
        foreach(var item in commands)
        {
            if(!Guid.TryParseExact(item.Id,"N",out _))throw new IOException("Invalid update request.");
            item.SourceProcessId=command.SourceProcessId;item.LoadedAssemblyPaths=command.LoadedAssemblyPaths;
            var job=item.Job;
            if(job==null&&item.Shop!=null&&item.Shop.Operation=="preview-install"){inventory??=InstallInventory.Scan(root);job=JobFromShop(item,inventory);RemoveFinishedOperationsFor(item.Shop.Namespace,item.Shop.Package);}
            if(job==null&&item.Shop!=null&&item.Shop.Operation=="preview-uninstall"){inventory??=InstallInventory.Scan(root);job=UninstallJobFromShop(item,inventory);RemoveFinishedOperationsFor(item.Shop.Namespace,item.Shop.Package);}
            if(job!=null)
            {
                job.OperationId=item.Id;job.SourceProcessId=command.SourceProcessId;
                if(job.BatchId.Length==0){job.BatchId=command.Id;job.BatchCreatedUtc=DateTime.UtcNow.ToString("O");}
                try{AddJob(job);}
                catch(DeferredCommandException){WriteAdd(job,command.SourceProcessId);}
                catch(Exception error){WorkerLog.Error("batch item rejected ("+job.Package+")",error);job.State=UpdateStates.Failed;job.Error=UpdateMessages.Encode(error.Message);job.Message=UpdateMessages.Encode("更新未完成，缓存与回退内容已保留。");SetDirectories(job);lock(gate){if(!snapshot.Jobs.Any(j=>j.Id==job.Id))snapshot.Jobs.Add(job);Save();}}
                continue;
            }
            if(item.Shop!=null)item.Shop.SourceProcessId=command.SourceProcessId;
            if(item.LocalImport!=null)item.LocalImport.SourceProcessId=command.SourceProcessId;
            Files.Save(Path.Combine(OperationsHome,item.Id,"request.json"),item);
        }
    }
    UpdateJob JobFromShop(UpdateCommand item,List<InstalledPackage> inventory)
    {
        var shop=item.Shop!;string key=shop.Namespace+"-"+shop.Package;
        var installed=InstallInventory.Find(key,inventory);
        return new UpdateJob{Id=item.Id,Namespace=shop.Namespace,Package=shop.Package,DisplayName=shop.Package,PackageRoot=installed?.Root??Path.Combine(root,"plugins",key),
            InstalledVersion=installed?.Version??"0.0.0",TargetVersion=shop.Version,Install=installed==null,Enabled=true,StoreOperation=true,OperationId=item.Id,SourceProcessId=item.SourceProcessId,
            Reinstall=installed!=null&&installed.Version==shop.Version};
    }
    /// <summary>Uninstall jobs need an installed folder; a missing one fails at execution with a plain message instead of rejecting the whole batch.</summary>
    UpdateJob UninstallJobFromShop(UpdateCommand item,List<InstalledPackage> inventory)
    {
        var shop=item.Shop!;string key=shop.Namespace+"-"+shop.Package;
        var installed=InstallInventory.Find(key,inventory);
        return new UpdateJob{Id=item.Id,Namespace=shop.Namespace,Package=shop.Package,DisplayName=shop.Package,PackageRoot=installed?.Root??Path.Combine(root,"plugins",key),
            InstalledVersion=installed?.Version??shop.Version,TargetVersion="",Uninstall=true,Enabled=true,StoreOperation=true,OperationId=item.Id,SourceProcessId=item.SourceProcessId};
    }
    void WriteAdd(UpdateJob job,int sourceProcessId)=>Files.Save(Path.Combine(home,"commands",Guid.NewGuid().ToString("N")+".json"),
        new UpdateCommand{Id=Guid.NewGuid().ToString("N"),Action="add",Job=job,SchemaVersion=2,MinimumWorkerProtocol=WorkerProtocol.Current,SourceProcessId=sourceProcessId});
    /// <summary>A retried legacy store install leaves the review-task path and becomes a queue job like any other install.</summary>
    bool TryConvertRetry(string dir,UpdateCommand command)
    {
        string requestPath=Path.Combine(dir,"request.json");if(!File.Exists(requestPath))return false;
        UpdateCommand request;try{request=Files.Read<UpdateCommand>(requestPath);}catch{return false;}
        if(request.Shop==null||request.Shop.Operation!="preview-install")return false;
        string taskPath=Path.Combine(dir,"shop","task.json");
        if(File.Exists(taskPath)){try{if(Files.Read<ShopTask>(taskPath).BlocksWrites)return false;}catch{return false;}}
        var job=JobFromShop(new UpdateCommand{Id=Guid.NewGuid().ToString("N"),Shop=request.Shop,SourceProcessId=command.SourceProcessId},InstallInventory.Scan(root));
        job.OperationId=command.JobId;
        try{AddJob(job);}catch(DeferredCommandException){WriteAdd(job,command.SourceProcessId);}
        lock(operationQueues)operationQueues.Remove(command.JobId);
        InstallArchive.TryDeleteDirectory(dir);
        WorkerLog.Info("operation "+command.JobId.Substring(0,8)+" retried as queue job "+job.Id.Substring(0,8));
        return true;
    }
    bool OperationFinished(string dir)
    {
        if(File.Exists(Path.Combine(dir,"error.json")))return true;
        string task=Path.Combine(dir,"shop","task.json");if(!File.Exists(task))return false;
        try{return Files.Read<ShopTask>(task).State is "failed" or "completed" or "cancelled";}catch{return true;}
    }
    void RemoveFinishedOperationsFor(string ns,string package)
    {
        if(!Directory.Exists(OperationsHome))return;
        foreach(string dir in Directory.GetDirectories(OperationsHome))
        {
            try
            {
                string requestPath=Path.Combine(dir,"request.json");if(!File.Exists(requestPath)||!OperationFinished(dir))continue;
                var request=Files.Read<UpdateCommand>(requestPath);
                if(request.Shop==null||!Files.Compare.Equals(request.Shop.Namespace,ns)||!Files.Compare.Equals(request.Shop.Package,package))continue;
                lock(operationQueues)operationQueues.Remove(Path.GetFileName(dir));
                InstallArchive.TryDeleteDirectory(dir);
            }
            catch(Exception){}
        }
    }
    void RemoveIndependentOperation(string id)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new IOException("Invalid operation identity.");
        string dir=Path.Combine(OperationsHome,id);if(!Directory.Exists(dir))return;
        string task=Path.Combine(dir,"shop","task.json");
        if(File.Exists(task)){ShopTask state;try{state=Files.Read<ShopTask>(task);}catch{state=new ShopTask();}if(state.BlocksWrites)throw new DeferredCommandException("该操作仍在执行，完成后再移除。");}
        lock(operationQueues)operationQueues.Remove(id);
        InstallArchive.TryDeleteDirectory(dir);
        WorkerLog.Info("operation "+id.Substring(0,8)+" removed");
    }
    /// <summary>Everything that is not running or waiting leaves the visible queue; backups, journals and the PendingRestart state stay for recovery and next-launch confirmation.</summary>
    void ClearFinished()
    {
        int hidden=0;string now=DateTime.UtcNow.ToString("O");
        lock(gate)
        {
            foreach(var job in snapshot.Jobs)
            {
                if(job.Hidden||job.RecoveryRequired)continue;
                bool terminal=job.State==UpdateStates.Failed||job.State==UpdateStates.Stopped||job.State==UpdateStates.Completed||job.State==UpdateStates.RolledBack||job.State==UpdateStates.Superseded||job.State==UpdateStates.Removed||job.State==UpdateStates.PendingRestart;
                if(!terminal)continue;
                if(job.State==UpdateStates.Failed||job.State==UpdateStates.Stopped){job.State=UpdateStates.Removed;job.Message=UpdateMessages.Encode("已移除队列记录，保留缓存与备份。");}
                job.Hidden=true;job.UpdatedUtc=now;hidden++;
            }
            snapshot.Error="";Save();
        }
        DismissFinishedShopTask();
        int operations=0;
        if(Directory.Exists(OperationsHome))foreach(string dir in Directory.GetDirectories(OperationsHome))if(OperationFinished(dir)){lock(operationQueues)operationQueues.Remove(Path.GetFileName(dir));InstallArchive.TryDeleteDirectory(dir);operations++;}
        try{string path=Path.Combine(home,"resource-batch.json");if(File.Exists(path)){var batch=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) as System.Text.Json.Nodes.JsonObject;if(batch!=null&&batch["Completed"]?.GetValue<bool>()!=true){batch["Completed"]=true;File.WriteAllText(path,batch.ToJsonString(Files.Json),new System.Text.UTF8Encoding(false));}}}catch(Exception){}
        WorkerLog.Info("clear-finished: hidden "+hidden+" jobs, removed "+operations+" operations");
    }
    /// <summary>A confirmed local import copies its staged BepInEx tree into the job and installs through the same core as store packages.</summary>
    /// <summary>Folder organization: the preview task stays read-only; the confirmed plan becomes a job that renames directories after exit.</summary>
    void PreviewLayout(LayoutQueueRequest request)
    {
        var previous=shop.Read();
        if(previous?.BlocksWrites==true)throw new DeferredCommandException("等待当前安装或恢复完成后继续目录整理。");
        if(request.Rows.Count==0&&request.Mode==InstallationLayoutMode.Disabled)throw new IOException("请选择启用的整理方式。");
        var index=new PackStorage(root).Read();var ledger=shop.ReadLedgerTolerant();
        var task=new ShopTask{Id=Guid.NewGuid().ToString("N"),ActivePackId=index.ActivePackId,IndexRevision=index.Revision,LedgerRevision=ledger.Revision,CreatedUtc=DateTime.UtcNow.ToString("O"),
            Request=new ShopRequest{Operation="layout-preview",SourceProcessId=request.SourceProcessId,AppliedSnapshotPath=request.AppliedSnapshotPath,AppliedSnapshotHash=request.AppliedSnapshotHash},State="preparing-layout"};
        shop.WriteTask(task);
        // 2.3.0 requests carry overview rows and use the group-folder organizer (no renaming, nested groups, nested-package extraction).
        Func<string,LayoutQueueRequest,ShopLedger,Action<string>?,LayoutPlan> build=request.Rows.Count>0?GroupLayoutPlanner.Build:LayoutPlanner.Build;
        var plan=build(root,request,ledger, message => {
            // Publish each phase so the game can show progress; the preview used to look frozen from the click
            // until the finished plan appeared.
            task.Message=message;task.Details.Add(message);
            try { shop.WriteTask(task); } catch (IOException) { }
        });
        task.Details.AddRange(plan.Details);task.Blockers.AddRange(plan.Blockers);
        task.Originals.AddRange(plan.OldPaths);task.Desired.AddRange(plan.NewPaths);task.Tree.AddRange(plan.Tree);
        Directory.CreateDirectory(shop.WorkDirectory(task));LayoutPlanner.Save(shop.WorkDirectory(task),plan);
        task.State="ready";task.Message="Review folder changes; every move waits for the game to exit.";task.PlanToken=shop.TaskToken(task);shop.WriteTask(task);
        WorkerLog.Info("layout preview "+task.Id.Substring(0,8)+": "+plan.Moves.Count+" moves, "+task.Blockers.Count+" blockers");
    }
    /// <summary>A preview the user backed out of, or one older than this launch, is closed instead of sitting in the
    /// queue as "preparing files" forever. Only an unconfirmed layout preview is touched; nothing was written yet.</summary>
    /// <summary>Only the game that asked for a folder/local preview can show its review dialog and confirm it. Once that
    /// process is gone the preview can never be confirmed, so it is closed rather than left in the queue indefinitely.</summary>
    void CloseOrphanedPreview()
    {
        try
        {
            var task=shop.ReadTask();
            if(task==null||task.State!="ready"||task.Request.Operation is not ("layout-preview" or "local-preview"))return;
            // Without a recorded requester there is nothing to prove it is gone; leave it for an explicit cancel.
            if(task.Request.SourceProcessId<=0||Files.ProcessAlive(task.Request.SourceProcessId))return;
            task.State="cancelled";task.Message=task.Request.Operation=="layout-preview"?"Folder preview closed; no folders were changed.":"Import cancelled; installed files unchanged.";
            shop.WriteTask(task);
            WorkerLog.Info(task.Request.Operation+" "+task.Id.Substring(0,8)+" closed: requesting game process has exited");
        }
        catch(Exception error){WorkerLog.Warn("orphaned preview check skipped: "+error.Message);}
    }
    /// <summary>Folder organization and local import keep a single store task file; clearing it from the queue only marks it,
    /// so the review copy under shop/tasks stays. A ready preview is closed first (no folders change); a running install is refused.</summary>
    void DismissShopTask(string id)
    {
        var task=shop.ReadTask();
        if(task==null||task.Id!=id||task.Dismissed)return;
        if(task.BlocksWrites)throw new IOException("该任务仍在执行，完成后再清除。");
        bool review=task.Request.Operation is "layout-preview" or "local-preview";
        if(review&&task.State is not ("completed" or "failed" or "cancelled"))
        {
            task.State="cancelled";
            task.Message=task.Request.Operation=="layout-preview"?"Folder preview closed; no folders were changed.":"Import cancelled; installed files unchanged.";
        }
        task.Dismissed=true;shop.WriteTask(task);
        WorkerLog.Info("store task "+id.Substring(0,Math.Min(8,id.Length))+" dismissed ("+task.Request.Operation+")");
    }
    /// <summary>"Clear" also drops a finished folder/import task, which otherwise stayed listed forever.</summary>
    void DismissFinishedShopTask()
    {
        try
        {
            var task=shop.ReadTask();
            if(task==null||task.Dismissed||task.BlocksWrites||!task.Request.Operation.StartsWith("layout-",StringComparison.Ordinal)&&!task.Request.Operation.StartsWith("local-",StringComparison.Ordinal))return;
            if(task.State is not ("completed" or "failed" or "cancelled"))return;
            task.Dismissed=true;shop.WriteTask(task);
        }
        catch(Exception error){WorkerLog.Error("clear-finished: store task not cleared",error);}
    }
    void CancelLayout(LayoutQueueRequest request)
    {
        var task=shop.ReadTask();
        if(task==null||task.Request.Operation!="layout-preview"||task.State is "completed" or "failed" or "cancelled")return;
        if(request.PreviewId.Length>0&&task.Id!=request.PreviewId)return;
        task.State="cancelled";task.Message="Folder preview closed; no folders were changed.";shop.WriteTask(task);
        WorkerLog.Info("layout preview "+task.Id.Substring(0,8)+" cancelled");
    }
    void ConvertLayout(LayoutQueueRequest request)
    {
        var task=shop.ReadTask();
        if(task==null||task.Id!=request.PreviewId||task.PlanToken!=request.PlanToken||task.PlanToken!=shop.TaskToken(task)||task.State!="ready"||task.Request.Operation!="layout-preview")throw new IOException("整理预览已变化，请重新预览。");
        if(task.Blockers.Count>0)throw new IOException(string.Join("\n",task.Blockers));
        var plan=LayoutPlanner.Read(shop.WorkDirectory(task))??throw new IOException("整理计划缺失，请重新预览。");
        // 2.3.1: only folders this preview listed as empty can be ticked for the Recycle Bin. A folder that only becomes
        // empty through the moves is listed under its original path; the moves have rewritten it by then.
        var empties=plan.Tree.Where(n=>n.Kind==LayoutNodeKind.Empty).ToList();
        plan.RecycleAfter=request.RecycleEmpty.Select(path=>empties.FirstOrDefault(n=>Files.Compare.Equals(n.OriginalPath,path)))
            .Where(n=>n!=null).Select(n=>n!.FinalPath.Length>0?n.FinalPath:n.OriginalPath).Distinct(Files.Compare).ToList();
        if(plan.Moves.Count==0&&plan.RecycleAfter.Count==0)throw new IOException("没有可整理的目录。");
        // The preview deliberately skips hashing the whole plugin tree. Collect the hashes here, for exactly the
        // files this job will move, so they describe the content at the moment it is confirmed.
        foreach(var unit in plan.Journal.Units)
            foreach(var file in unit.Files)
                file.Hash=Files.Hash(Files.Child(root,LayoutPaths.OldPath(unit,file)));
        var job=new UpdateJob{Id=Guid.NewGuid().ToString("N"),Namespace="layout",Package="layout",DisplayName="目录整理",Layout=true,LayoutPreviewId=task.Id,
            PackageRoot=Path.Combine(root,"plugins"),TargetVersion=plan.Moves.Count.ToString(),Enabled=true,SourceProcessId=request.SourceProcessId};
        SetDirectories(job);Directory.CreateDirectory(job.WorkDirectory);LayoutPlanner.Save(job.WorkDirectory,plan);
        try{AddJob(job);}catch(DeferredCommandException){WriteAdd(job,request.SourceProcessId);}
        shop.CompleteLayoutHandoff(task,job.Id,plan.Moves.Count);
    }
    UpdateJob LocalImportJob(ShopQueue queue,ShopTask task,int sourceProcessId)
    {
        string content=queue.ContentDirectory(task);
        string? folder=null;
        foreach(string area in new[]{"plugins","patchers"}){string dir=Path.Combine(content,area);if(!Directory.Exists(dir))continue;var first=Directory.GetDirectories(dir).FirstOrDefault();if(first!=null){folder=Path.GetFileName(first);break;}}
        if(folder==null)throw new IOException("本地包没有可安装的目录。");
        string packageRoot=Path.Combine(root,"plugins",folder);
        var existing=InstallInventory.Scan(root).FirstOrDefault(p=>Files.Compare.Equals(p.Root,packageRoot));
        string version="";try{string manifest=Path.Combine(content,"plugins",folder,"manifest.json");if(File.Exists(manifest)){using var document=System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));if(document.RootElement.TryGetProperty("version_number",out var v))version=v.GetString()??"";}}catch(Exception){}
        var job=new UpdateJob{Id=Guid.NewGuid().ToString("N"),Namespace="local",Package=folder,DisplayName=folder,PackageRoot=packageRoot,InstalledVersion=existing?.Version??"0.0.0",TargetVersion=version,Install=existing==null,Enabled=true,SourceProcessId=sourceProcessId,OperationId=task.OperationId};
        SetDirectories(job);string local=Path.Combine(job.WorkDirectory,"local");InstallArchive.TryDeleteDirectory(local);Directory.CreateDirectory(job.WorkDirectory);PackStorage.CopyTree(content,local);
        job.LocalSource=local;return job;
    }
    bool IndependentWatching => Directory.Exists(OperationsHome)&&Directory.GetDirectories(OperationsHome).Any(d=>{
        string state=Path.Combine(d,"shop","task.json");return !File.Exists(Path.Combine(d,"error.json"))&&(!File.Exists(state)||Files.Read<ShopTask>(state).State is not ("completed" or "failed" or "cancelled"));});
    async Task PumpIndependentOperations(CancellationToken token)
    {
        if(!Directory.Exists(OperationsHome))return;
        await Task.WhenAll(Directory.GetDirectories(OperationsHome).Select(async directory=>{
            await operationSlots.WaitAsync(token);
            try {
                string id=Path.GetFileName(directory),requestPath=Path.Combine(directory,"request.json");if(!File.Exists(requestPath)||File.Exists(Path.Combine(directory,"error.json")))return;
                ShopQueue queue;
                lock(operationQueues){if(!operationQueues.TryGetValue(id,out queue!))operationQueues[id]=queue=new ShopQueue(root,Path.Combine(directory,"shop"),executeIndependent:true);}
                var command=Files.Read<UpdateCommand>(requestPath);var task=queue.Read();
                if(task==null){if(command.LocalImport!=null){command.LocalImport.OperationId=id;queue.HandleLocalImport(command.LocalImport);}else{command.Shop!.OperationId=id;queue.Handle(command.Shop);}return;}
                if(task.State is "failed" or "completed" or "cancelled")return;
                if(task.State=="preview")queue.Handle(new ShopRequest{Operation="approve-download",PreviewId=task.Id,PlanToken=task.PlanToken,SourceProcessId=command.SourceProcessId});
                else if(task.State=="ready"&&command.LocalImport!=null)
                {
                    if(task.Blockers.Count>0){Files.Save(Path.Combine(directory,"error.json"),new{Error=string.Join("\n",task.Blockers)});return;}
                    var job=LocalImportJob(queue,task,command.SourceProcessId);
                    try{AddJob(job);}catch(DeferredCommandException){WriteAdd(job,command.SourceProcessId);}
                    queue.CompleteLocalImportHandoff(task,job.Id);return;
                }
                else if(task.State is "ready" or "uninstall-preview"){
                    if(task.DependencyWarnings.Count>0&&!task.Request.IgnoreDependencyWarnings)return;
                    queue.Handle(new ShopRequest{Operation=task.Uninstall?"confirm-uninstall":"confirm-install",PreviewId=task.Id,PlanToken=task.PlanToken,SourceProcessId=command.SourceProcessId,
                        GroupId=command.Shop?.GroupId??"",NewGroupName=command.Shop?.NewGroupName??"",NewGroupColor=command.Shop?.NewGroupColor??"#7CC7E6"});
                }
                await queue.Pump(snapshot.Network,token);
            } catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception error){Files.Save(Path.Combine(directory,"error.json"),new{Error=error.Message});}
            finally{operationSlots.Release();}
        }));
    }
    List<JobView> IndependentViews()
    {
        var views=new List<JobView>();
        if(!Directory.Exists(OperationsHome))return views;
        foreach(string directory in Directory.GetDirectories(OperationsHome))
        {
            string id=Path.GetFileName(directory);string file=Path.Combine(directory,"shop","task.json");
            string? requestError=null;try{string errorPath=Path.Combine(directory,"error.json");if(File.Exists(errorPath))requestError=Files.Read<Dictionary<string,string>>(errorPath).TryGetValue("Error",out var e)?e:"";}catch{}
            if(!File.Exists(file))
            {
                views.Add(new JobView{Id=id,Kind="operation",Name=id.Substring(0,8),State=requestError!=null?"failed":"queued",StateText=WorkerLanguage.StateText(requestError!=null?"failed":UpdateStates.Queued),Error=requestError!=null?WorkerLanguage.Localize(requestError):"",Details=requestError??"",Busy=requestError==null,Failed=requestError!=null,CanRetry=requestError!=null,CanRemove=true});
                continue;
            }
            ShopTask task;try{task=Files.Read<ShopTask>(file);}catch{continue;}
            string error=task.Error.Length>0?task.Error:requestError??"";
            string firstLine=error.Replace("\r\n","\n").Split('\n').FirstOrDefault(l=>l.Trim().Length>0)??"";
            bool failed=task.State=="failed"||requestError!=null,done=task.State=="completed",busy=!failed&&!done&&task.State!="cancelled"&&task.State!="waiting-exit";
            views.Add(new JobView{Id=id,Kind="operation",Name=task.Request.Namespace+"-"+task.Request.Package+(task.Uninstall?" ("+WorkerLanguage.T("uninstall","卸载","解除安裝")+")":""),ToVersion=task.Request.Version,
                State=task.State,StateText=WorkerLanguage.StateText(task.State),Message=WorkerLanguage.Localize(task.Message),Error=WorkerLanguage.Localize(firstLine),Details=error,
                Progress=task.TotalBytes>0?FrameworkCompat.Clamp((double)task.DownloadedBytes/task.TotalBytes,0,1):0,Downloaded=task.DownloadedBytes,Total=task.TotalBytes,
                Warnings=task.DependencyWarnings.Select(WorkerLanguage.Localize).ToList(),Busy=busy,Failed=failed,Done=done,Waiting=task.State=="waiting-exit",CanRetry=failed,CanRemove=task.State is not ("committing" or "recovery-required")});
        }
        return views;
    }
    void ControlIndependentOperation(UpdateCommand command)
    {
        if(!Guid.TryParseExact(command.JobId,"N",out _))throw new IOException("Invalid operation identity.");
        string dir=Path.Combine(OperationsHome,command.JobId);
        if(command.Action=="operation-retry"&&TryConvertRetry(dir,command))return;
        if(command.Action=="operation-retry"&&File.Exists(Path.Combine(dir,"error.json"))){File.Delete(Path.Combine(dir,"error.json"));lock(operationQueues)operationQueues.Remove(command.JobId);return;}
        ShopQueue queue;lock(operationQueues){if(!operationQueues.TryGetValue(command.JobId,out queue!))operationQueues[command.JobId]=queue=new ShopQueue(root,Path.Combine(dir,"shop"),executeIndependent:true);}
        var task=queue.Read()??throw new IOException("Operation not found.");
        if(command.Action=="operation-ignore")queue.Handle(new ShopRequest{Operation="confirm-uninstall",PreviewId=task.Id,PlanToken=task.PlanToken,SourceProcessId=command.SourceProcessId,IgnoreDependencyWarnings=true,DependencyWarningToken=command.ResourceBatchId});
        else if(command.Action=="operation-retry" && task.State=="failed"){File.Delete(Path.Combine(dir,"error.json"));File.Delete(Path.Combine(dir,"shop","task.json"));lock(operationQueues)operationQueues.Remove(command.JobId);}
        else if(command.Action=="operation-cancel"){preparingCancellation?.Cancel();queue.CancelPreparation();}
    }
}
