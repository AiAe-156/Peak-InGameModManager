using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal sealed class PackQueue
{
    readonly PackStorage storage;
    internal PackQueue(string root)
    {
        storage = new PackStorage(root);
        storage.Read();
        var task = Read();
        if (task?.State is "preparing" or "saving")
        {
            task.Error = task.State == "saving" ? "上次临时快照保存被中断，当前游戏文件未被替换，请重新保存。" : "上次导入准备被中断，请重新预览。";
            task.State = "failed"; Files.Save(storage.TaskPath, task);
        }
    }
    PackTask? Read()
    {
        if (!File.Exists(storage.TaskPath)) return null;
        var task = Files.Read<PackTask>(storage.TaskPath);
        if (task.SchemaVersion != 1 || !PackStorage.Id(task.Id) || task.Request == null || task.Proposed == null) throw new IOException("整合包任务损坏，请先恢复备份。");
        return task;
    }
    internal bool BlocksUpdates { get { storage.Read(); return Read()?.BlocksUpdates == true; } }
    internal bool Watching => Read()?.State is "requested" or "waiting-exit" or "switching" or "recovery-required";
    internal bool UpdateBlocksEnvironment(UpdateJob job)
    {
        string path = ManagerIdentity.Runtime(storage.Root, "jobs", job.Id, "transaction.json");
        var plan = File.Exists(path) ? Files.Read<Transaction>(path) : null;
        string? phase = plan?.Phase;
        if (plan?.RollbackRequested == true || job.RollbackRequested || phase != null && PackageRules.HasUnfinishedWrites(phase)) return true;
        return job.RecoveryRequired || UpdateStates.BlocksPublication(job)
            && (job.State != UpdateStates.PendingRestart || phase != "installed");
    }
    /// <summary>Reject through the command error channel without replacing an active switch/recovery task.</summary>
    internal void Handle(PackRequest request, UpdateSnapshot updates)
    {
        try { HandleCore(request, updates); }
        catch (Exception error) when (request.Operation.StartsWith("preview-", StringComparison.Ordinal) || request.Operation.StartsWith("confirm-", StringComparison.Ordinal) || request.Operation.StartsWith("maintenance-", StringComparison.Ordinal))
        {
            var current = Read();
            if (current?.BlocksUpdates == true || current?.State is "requested" or "preparing") throw;
            if (current != null && request.Operation.StartsWith("confirm-", StringComparison.Ordinal) && current.State == "preview") { current.Error = error.Message; current.Message = "没有执行：" + error.Message; Files.Save(storage.TaskPath, current); }
            else Files.Save(storage.TaskPath, new PackTask { Id = Guid.NewGuid().ToString("N"), Request = request, State = "failed", Error = error.Message, Message = "没有开始：" + error.Message });
            throw;
        }
    }
    void HandleCore(PackRequest request, UpdateSnapshot updates)
    {
        var current = Read();
        if (request.Operation is "retry-switch" or "retry-recovery")
        {
            if (current == null || current.Id != request.PreviewId || current.Request.Operation != "confirm-switch") throw new IOException("重试任务已失效。");
            if (request.Operation == "retry-recovery")
            {
                if (current.State != "recovery-required") throw new IOException("当前没有待恢复事务。");
                StartAttempt(current); current.Error = "";
                current.Message = "正在重试恢复原环境；游戏运行时等待退出。"; Files.Save(storage.TaskPath, current); return;
            }
            if (current.State != "failed") throw new IOException("请先完成原环境恢复，再重试切换。");
            string oldPlan = PackTransactions.PlanPath(storage, current.Id);
            if (File.Exists(oldPlan))
            {
                var previous = Files.Read<PackSwitchPlan>(oldPlan);
                if (previous.Phase != "rolled-back") throw new IOException("原事务尚未回滚完成，不能开始新的切换。");
            }
            if (updates.Jobs.Any(UpdateBlocksEnvironment)) throw new IOException("更新任务尚未完成，暂不能重试切换。");
            var preview = PackTransactions.Preview(storage, new PackRequest { Operation = "preview-switch", PackId = current.Proposed.Id, HostPlugins = current.Request.HostPlugins, GameLanguage = current.Request.GameLanguage, SaveCurrentBeforeSwitch = current.Request.SaveCurrentBeforeSwitch });
            StartAttempt(preview);
            preview.Request.Operation = "confirm-switch"; preview.State = "waiting-exit"; preview.Message = "已重新检查，等待退出后重试切换。";
            Files.Save(storage.TaskPath, preview); return;
        }
        if (request.Operation == "cancel")
        {
            if (current == null || current.Id != request.PreviewId || current.State != "waiting-exit"
                || File.Exists(PackTransactions.PlanPath(storage, current.Id))) throw new IOException("当前任务已开始提交，不能取消。");
            EndAttempt(current); current.State = "cancelled"; current.Message = "已取消退出后切换。"; Files.Save(storage.TaskPath, current); return;
        }
        if (request.Operation == "complete-profile")
        {
            if (current == null || current.Id != request.PreviewId || current.State != "preview" || !current.SingleProfile) throw new IOException("单方案导入记录已失效。");
            current.State = "completed"; current.Message = "个人方案已追加到当前整合包。"; Files.Save(storage.TaskPath, current); return;
        }
        if (current?.BlocksUpdates == true || current?.State is "requested" or "preparing") throw new IOException("整合包切换正在等待退出或恢复，请先处理该任务。");
        if (request.Operation is "preview-switch" or "confirm-switch" or "preview-adopt" or "confirm-adopt" or "preview-adopt-environment" or "confirm-adopt-environment" or "preview-upgrade-environment" or "confirm-upgrade-environment")
        {
            // Only a finalized transaction may waive pending-restart; candidates still need finalization.
            var open = updates.Jobs.Where(UpdateBlocksEnvironment).ToList();
            if (open.Count > 0) throw new IOException("还有 " + open.Count + " 个更新没有完成（" + string.Join("、", open.Take(3).Select(j => j.DisplayName)) + "），请先在“更新”里完成或撤销，再接管或切换整合包。");
        }
        if (request.Operation is "maintenance-preview" or "maintenance-execute")
        {
            if (updates.Jobs.Any(UpdateBlocksEnvironment)) throw new IOException("更新队列仍有未完成任务，请先完成或撤销后再清理。");
            var maintenance = request.Maintenance ?? throw new IOException("缺少维护操作。");
            if (request.Operation == "maintenance-preview")
            {
                var preview = PackMaintenance.Preview(storage, maintenance);
                Files.Save(storage.TaskPath, new PackTask { Id = Guid.NewGuid().ToString("N"), State = "maintenance-preview", Request = request, Maintenance = preview, Message = "维护预览已完成，尚未删除任何文件。" });
            }
            else
            {
                if (current?.State != "maintenance-preview" || current.Maintenance?.Id != maintenance.PreviewId) throw new IOException("维护预览已失效，请重新查看范围。");
                var result = PackMaintenance.Execute(storage, maintenance);
                Files.Save(storage.TaskPath, new PackTask { Id = current.Id, State = result.Success ? "completed" : "failed", Request = request, Message = result.Message, Error = string.Join("；", result.Failures) });
            }
            return;
        }
        if (request.Operation == "confirm-download")
        {
            // The worker replays the stored preview request. The confirmation may not bring a source, a URL or its own list.
            if (current == null || current.State != "preview" || current.Id != request.PreviewId || current.Request.Operation != "preview-import") throw new IOException("预览已失效，请重新预览并确认。");
            if (current.PendingDownloads.Count == 0) throw new IOException("该预览没有待同意的下载。");
            if (!request.Approved || request.ApprovedPlan.Length == 0 || request.ApprovedPlan != current.DownloadPlan || PackImports.PlanHash(current.PendingDownloads) != current.DownloadPlan)
                throw new IOException("下载同意与原预览的依赖清单不匹配，请重新预览。");
            if (request.Source.Length > 0 && request.Source != current.Request.Source) throw new IOException("确认命令不能更改导入来源。");
            current.Request.SourceProcessId = request.SourceProcessId;
            current.Request.Approved = true; current.Request.ApprovedPlan = current.DownloadPlan;
            current.State = "requested"; current.Message = "正在下载已同意的 " + current.PendingDownloads.Count + " 个依赖……"; current.Error = "";
            Files.Save(storage.TaskPath, current); return;
        }
        if (request.Operation.StartsWith("confirm-", StringComparison.Ordinal))
        {
            if (current == null || current.State != "preview" || current.Id != request.PreviewId) throw new IOException("预览已失效，请重新预览并确认。");
            if (current.PendingDownloads.Count > 0) throw new IOException("还有依赖等待同意下载，请先点击“同意下载并补齐”。");
            string expected = current.Request.Operation.Replace("preview-", "confirm-");
            if (request.Operation != expected) throw new IOException("确认操作与预览不匹配。");
            current.Request.SourceProcessId = request.SourceProcessId;
            current.Request.Operation = request.Operation;
            current.Request.ConfirmedConfigFiles = request.ConfirmedConfigFiles;
            current.Request.SaveCurrentBeforeSwitch = request.SaveCurrentBeforeSwitch;
            if (request.PackLanguage.Length > 0 && current.Report.Languages.Any(l => l.Key == request.PackLanguage)) current.Proposed.Language = request.PackLanguage;
            // Scope/host ownership is frozen by preview. Only the loaded versions can be refreshed.
            current.Request.HostPlugins = request.HostPlugins;
            if (current.SingleProfile) throw new IOException("单方案请在游戏内确认追加。");
            StartAttempt(current);
            current.State = request.Operation is "confirm-import" or "confirm-adopt" ? "requested" : "waiting-exit";
            current.Message = request.Operation == "confirm-import" ? "正在存入整合包库……"
                : request.Operation == "confirm-adopt" ? "正在保存临时整合包「" + current.Proposed.Name + "」。"
                : "等待游戏退出，随后切换整合包。";
            Files.Save(storage.TaskPath, current); return;
        }
        if (request.Operation == "set-language")
        {
            var index = storage.Read(); var pack = index.Packs.SingleOrDefault(p => p.Id == request.PackId) ?? throw new IOException("整合包不存在。");
            var target = pack.PendingUpdate ?? pack;
            if (!PackTransactions.ScanLanguages(storage, target).Any(l => l.Key == request.PackLanguage)) throw new IOException("该整合包未提供所选语言文件。");
            target.Language = request.PackLanguage; storage.Save(index);
            Files.Save(storage.TaskPath, new PackTask { Id = Guid.NewGuid().ToString("N"), State = "completed", Request = request, Proposed = target, Message = "整合包语言文件选择已保存。" }); return;
        }
        if (request.Operation is not ("preview-import" or "preview-switch" or "preview-adopt" or "preview-adopt-environment" or "preview-upgrade-environment" or "personal-copy")) throw new IOException("未知整合包操作。");
        Files.Save(storage.TaskPath, new PackTask { Id = Guid.NewGuid().ToString("N"), Request = request, State = "requested" });
    }
    static void StartAttempt(PackTask task) { task.AttemptId = Guid.NewGuid().ToString("N"); task.AttemptStartedUtc = DateTime.UtcNow.ToString("O"); task.AttemptEndedUtc = ""; }
    static void EndAttempt(PackTask task) { if (task.AttemptStartedUtc.Length > 0) task.AttemptEndedUtc = DateTime.UtcNow.ToString("O"); }
    static string CompletedMessage(PackTask task) => task.Request.Operation == "confirm-upgrade-environment"
        ? "当前包已明确升级为完整环境；旧 revision 保留，真实文件未被替换。"
        : task.Request.Operation is "confirm-adopt" or "confirm-adopt-environment"
        ? "临时整合包「" + task.Proposed.Name + "」保存完成，已加入整合包库。当前游戏文件未被替换；现在可以切换到其他整合包。"
        : "已切换到整合包「" + task.Proposed.Name + "」，下次启动游戏生效。";
    internal async Task Pump(UpdateSnapshot updates)
    {
        var task = Read(); if (task == null) return;
        try
        {
            if (task.State == "requested")
            {
                if ((task.Request.Operation is "preview-switch" or "preview-adopt" or "confirm-adopt" or "preview-adopt-environment" or "confirm-adopt-environment" or "preview-upgrade-environment" or "confirm-upgrade-environment")
                    && updates.Jobs.Any(UpdateBlocksEnvironment)) throw new IOException("更新文件尚未最终整理，禁止捕获或切换环境。");
                if (task.Request.Operation == "personal-copy")
                {
                    task.Proposed = PackPersonalCopy.Save(storage, task.Request); task.State = "completed";
                    task.Message = "个人副本「" + task.Proposed.Name + "」保存完成，原包和当前游戏环境未改变。"; Files.Save(storage.TaskPath, task);
                }
                else if (task.Request.Operation == "preview-import")
                {
                    // Consent is single-use: the replayed preview carries the approved list, and its result never keeps the flag.
                    var approved = task.Request.Approved && task.Request.ApprovedPlan == task.DownloadPlan && PackImports.PlanHash(task.PendingDownloads) == task.DownloadPlan ? task.PendingDownloads.ToList() : null;
                    if (task.Request.Approved && approved == null) throw new IOException("下载同意与原预览的依赖清单不匹配，请重新预览。");
                    task.Request.Approved = false; task.Request.ApprovedPlan = "";
                    var replay = await PackImports.Preview(storage, task.Request, updates.Network, null, approved);
                    // The replay is a new preview; drop the old disposable staging so approved downloads are not kept twice.
                    if (approved != null && replay.Id != task.Id && task.StagingDirectory.Length > 0 && Files.Within(Path.Combine(storage.Home, ".imports"), task.StagingDirectory))
                        try { Directory.Delete(task.StagingDirectory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                else if (task.Request.Operation is "preview-switch" or "preview-adopt" or "preview-adopt-environment" or "preview-upgrade-environment") PackTransactions.Preview(storage, task.Request);
                else if (task.Request.Operation == "confirm-import") PackImports.Commit(storage, task);
                else if (task.Request.Operation == "confirm-adopt")
                {
                    task.State = "saving"; Files.Save(storage.TaskPath, task);
                    PackTransactions.SaveTemporary(storage, task);
                    task.State = "completed"; task.Message = CompletedMessage(task); EndAttempt(task); Files.Save(storage.TaskPath, task);
                }
                return;
            }
            if (task.State is not ("waiting-exit" or "switching" or "recovery-required")) return;
            if (Files.GameRunning(new UpdateJob { SourceProcessId = task.Request.SourceProcessId })) return;
            string planPath = PackTransactions.PlanPath(storage, task.Id);
            if (File.Exists(planPath))
            {
                var plan = Files.Read<PackSwitchPlan>(planPath);
                if (plan.Phase == "installed") { PackTransactions.ValidateCompleted(storage, plan); task.State = "completed"; task.Message = CompletedMessage(task); }
                else { PackTransactions.Rollback(storage, plan); task.State = "failed"; task.Message = "已恢复切换前的环境，请重新预览后再试。"; }
                EndAttempt(task); Files.Save(storage.TaskPath, task); return;
            }
            if (task.State == "recovery-required" || task.State == "switching") throw new IOException("切换记录缺失，保留现状，请恢复事务备份。");
            if (updates.Jobs.Any(UpdateBlocksEnvironment)) throw new IOException("更新文件尚未最终整理，禁止切换环境。");
            task.State = "switching"; task.Message = "游戏已退出，正在保存旧包并校验目标文件……"; Files.Save(storage.TaskPath, task);
            var prepared = PackTransactions.Prepare(storage, task);
            task.State = "switching"; task.Message = prepared.Adopting ? "正在保存临时整合包「" + task.Proposed.Name + "」……" : "正在保存当前整合包并切换……"; Files.Save(storage.TaskPath, task);
            PackTransactions.Commit(storage, prepared);
            task.State = "completed"; task.Message = CompletedMessage(task); EndAttempt(task); Files.Save(storage.TaskPath, task);
        }
        catch (Exception error)
        {
            task.Error = error.Message + "\nException: " + error.GetType().FullName + " (0x" + error.HResult.ToString("X8") + ")\n" + error.StackTrace;
            string planPath = PackTransactions.PlanPath(storage, task.Id);
            if (File.Exists(planPath))
            {
                task.State = "recovery-required";
                try
                {
                    var plan = Files.Read<PackSwitchPlan>(planPath);
                    if (!Files.GameRunning(new UpdateJob { SourceProcessId = task.Request.SourceProcessId }))
                    { PackTransactions.Rollback(storage, plan); task.State = "failed"; task.Message = "切换失败，已恢复切换前的完整环境。可以查看原因后重试。"; }
                }
                catch (Exception recovery) { task.Error += "\n恢复尚未完成：" + recovery.Message; }
            }
            else { task.State = "failed"; task.Message = "准备阶段失败，未替换当前环境，可以查看原因后重试。"; }
            if (task.State == "recovery-required") task.Message = "恢复尚未完成，禁止继续切换。退出游戏后可重试恢复；事务备份已保留。";
            EndAttempt(task);
            Files.Save(storage.TaskPath, task);
        }
    }
}
