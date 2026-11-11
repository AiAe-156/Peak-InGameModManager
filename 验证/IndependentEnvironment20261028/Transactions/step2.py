from pathlib import Path
p=Path('Updater/PackTransactions.cs');s=p.read_text(encoding='utf-8')
def r(a,b):
 global s
 assert s.count(a)==1,(a[:100],s.count(a))
 s=s.replace(a,b)
r('        if (adopting)\n        {\n            MigrateWorkspace(storage, old.Id);','''        if (runtimeLedger != null)
        {
            if (ledgerBefore != PackCompleteEnvironment.ReadLedgerText(storage, before.ActivePackId)) throw new IOException("准备期间 ledger 已改变，请重试；不会抹除归属。");
            WriteRuntimeState(storage, runtime, runtimeLedger);
            if (adopting || upgrading || task.Request.SaveCurrentBeforeSwitch)
            { old.EnvironmentVersion = 2; old.MinimumWorkerProtocol = 2; old.RevisionStateHash = runtime.RevisionStateHash; }
            plan.Ledgers.Add(new PackLedgerChange { PackId = old.Id, Before = PackCompleteEnvironment.ReadLedgerText(storage, old.Id), After = JsonSerializer.Serialize(runtimeLedger, Files.Json) });
            after.SchemaVersion = 2; after.MinimumWorkerProtocol = 2;
            if (adopting || upgrading) PackCompleteEnvironment.RequireComplete(storage, runtime);
        }
        StepProbe?.Invoke("snapshot-written");
        if (adopting || upgrading)
        {
            if (adopting) MigrateWorkspace(storage, old.Id);''')
r('            CheckRequirements(storage, next, task.Request.HostPlugins);\n            foreach (var file in next.Files) if (Host(after, file.Path)) throw new IOException("目标包试图覆盖固定宿主：" + file.Path);','''            if (environmentVersion == 2)
            {
                PackCompleteEnvironment.RequireComplete(storage, next);
                var targetLedger = new PackRevisionState(storage).Read(next)!;
                plan.Ledgers.Add(new PackLedgerChange { PackId = next.Id, Before = PackCompleteEnvironment.ReadLedgerText(storage, next.Id), After = JsonSerializer.Serialize(targetLedger, Files.Json) });
            }
            else
            {
                CheckRequirements(storage, next, task.Request.HostPlugins);
                foreach (var file in next.Files) if (Host(after, file.Path)) throw new IOException("目标包试图覆盖固定宿主：" + file.Path);
            }''')
r('        Stopped(plan.SourceProcessId); Files.Save(PlanPath(storage, task.Id), plan); return plan;','        Stopped(plan.SourceProcessId); Files.Save(PlanPath(storage, task.Id), plan); StepProbe?.Invoke("prepared"); return plan;')
r('    static void MigrateWorkspace(PackStorage storage, string id)','''    static void WriteRuntimeState(PackStorage storage, PackRecord runtime, ShopLedger ledger)
    {
        try { new PackRevisionState(storage).Write(runtime, ledger); }
        catch (IOException error) { throw new IOException("无法捕获完整环境：商店归属证据与当前文件不一致。请明确修复或放弃对应组件归属后重试；不会自动抹除 ledger。" + error.Message, error); }
    }
    static void MigrateWorkspace(PackStorage storage, string id)''')
r('        VerifyEnvironment(storage, plan.After, plan.Originals);','''        VerifyEnvironment(storage, plan.After, plan.Originals, plan.EnvironmentVersion);
        foreach (var ledger in plan.Ledgers) if (PackCompleteEnvironment.ReadLedgerText(storage, ledger.PackId) != ledger.Before) throw new IOException("准备后商店归属发生变化，停止切换且不覆盖 ledger。");''')
r('Stopped(plan.SourceProcessId); VerifyEnvironment(storage, plan.After, plan.Desired); Stopped(plan.SourceProcessId);\n        storage.Save(plan.After); plan.Phase = "installed"; Save(storage, plan);','''Stopped(plan.SourceProcessId); VerifyEnvironment(storage, plan.After, plan.Desired, plan.EnvironmentVersion); Stopped(plan.SourceProcessId);
        ApplyLedgers(storage, plan, false); StepProbe?.Invoke("before-index");
        storage.Save(plan.After); StepProbe?.Invoke("after-index"); plan.Phase = "installed"; Save(storage, plan);''')
r('        storage.Save(plan.Before); plan.Phase = "rolled-back"; Save(storage, plan);','''        ApplyLedgers(storage, plan, true); StepProbe?.Invoke("rollback-ledgers");
        storage.Save(plan.Before); plan.Phase = "rolled-back"; Save(storage, plan);''')
r('if (plan.SchemaVersion != 1 || !PackStorage.Id(plan.Id) || plan.Before == null || plan.After == null || plan.Before.SchemaVersion != 1 || plan.After.SchemaVersion != 1','if (plan.SchemaVersion is not (1 or 2) || plan.EnvironmentVersion is not (1 or 2) || plan.SchemaVersion == 1 && plan.EnvironmentVersion != 1 || !PackStorage.Id(plan.Id) || plan.Before == null || plan.After == null || plan.Before.SchemaVersion is not (1 or 2) || plan.After.SchemaVersion is not (1 or 2)')
r('        ValidateHosts(storage, plan.After);','''        if (plan.EnvironmentVersion == 1) ValidateHosts(storage, plan.After);
        var ledgerIds = new HashSet<string>(Files.Compare);
        foreach (var ledger in plan.Ledgers)
        {
            if (plan.SchemaVersion != 2 || !PackStorage.Id(ledger.PackId) || !ledgerIds.Add(ledger.PackId) || !plan.After.Packs.Any(p => p.Id == ledger.PackId)) throw new IOException("ledger 事务身份无效。");
            if (JsonSerializer.Deserialize<ShopLedger>(ledger.After, Files.Json)?.SchemaVersion != 1) throw new IOException("ledger 事务损坏。");
        }
        if (plan.EnvironmentVersion == 2 && (plan.SchemaVersion != 2 || plan.Ledgers.Count == 0)) throw new IOException("完整切换缺少配套 ledger 事务。");''')
r('            if (!plan.Before.HostRoots.ToHashSet(Files.Compare).SetEquals(plan.After.HostRoots))','            if (plan.EnvironmentVersion == 1 && !plan.Before.HostRoots.ToHashSet(Files.Compare).SetEquals(plan.After.HostRoots))')
r('if (!PackStorage.ManagedPath(file.Path) || Host(plan.After, file.Path))','if (!PackStorage.ManagedPath(file.Path, plan.EnvironmentVersion) || plan.EnvironmentVersion == 1 && Host(plan.After, file.Path))')
r('    static void VerifyEnvironment(PackStorage storage, PackIndex index, IReadOnlyList<PackFile> expected)\n    {\n        var actual = Live(storage, index);','''    static void ApplyLedgers(PackStorage storage, PackSwitchPlan plan, bool rollback)
    {
        foreach (var ledger in plan.Ledgers)
        {
            string? current = PackCompleteEnvironment.ReadLedgerText(storage, ledger.PackId);
            string? desired = rollback ? ledger.Before : ledger.After;
            if (current != ledger.Before && current != ledger.After) throw new IOException("事务 ledger 已被外部修改，保留并停止恢复：" + ledger.PackId);
            if (current == desired) continue;
            StepProbe?.Invoke((rollback ? "rollback-ledger:" : "before-ledger:") + ledger.PackId);
            string path = PackCompleteEnvironment.LedgerPath(storage, ledger.PackId);
            if (desired == null) { if (File.Exists(path)) File.Delete(path); }
            else
            {
                string temporary = path + ".ledger-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(temporary, desired);
                File.Move(temporary, path, true);
            }
            StepProbe?.Invoke((rollback ? "restored-ledger:" : "ledger:") + ledger.PackId);
        }
    }
    static void VerifyEnvironment(PackStorage storage, PackIndex index, IReadOnlyList<PackFile> expected, int environmentVersion)
    {
        var actual = Live(storage, index, environmentVersion);''')
r('            if (PackStorage.ManagedPath(relative) && !Host(index, relative))','''            int environmentVersion = index.Packs.Single(p => p.Id == index.ActivePackId).EnvironmentVersion;
            if (PackStorage.ManagedPath(relative, environmentVersion) && (environmentVersion == 2 || !Host(index, relative)))''')
p.write_text(s,encoding='utf-8')
