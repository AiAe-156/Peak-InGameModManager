using System.Text.Json;
using InstallerChecks;
using MCE.UpdateWorker;
using ModConfigEnhance.Updating;

int passed = 0, failed = 0;
void Check(string name, bool ok, string detail = "") { if (ok) passed++; else failed++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " — " + detail.Replace("\r", " ").Replace("\n", " | ") : "")); }
string home = Path.Combine(Path.GetTempPath(), "mce-installer-checks", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
Directory.CreateDirectory(home); Console.WriteLine("fixture home: " + home);
Files.RunningProbe = _ => false;

(string Root, string Core) NewRoot(string name)
{
    string root = Path.Combine(home, name, "BepInEx"); Directory.CreateDirectory(Path.Combine(root, "plugins")); Directory.CreateDirectory(Path.Combine(root, "config"));
    string core = Path.Combine(root, "core"); Directory.CreateDirectory(core); File.WriteAllBytes(Path.Combine(core, "BepInEx.dll"), Fixtures.BepInEx());
    MceSeed.Add(root); return (root, core);
}
string Installed(string root, string folder, string name, string version, byte[] dll, string dllName, params string[] dependencies)
{
    string package = Path.Combine(root, "plugins", folder); Directory.CreateDirectory(package);
    File.WriteAllBytes(Path.Combine(package, dllName), dll); File.WriteAllText(Path.Combine(package, "manifest.json"), Fixtures.Manifest(name, version, dependencies));
    return package;
}
void Post(string root, QueueWorker worker, UpdateCommand command)
{
    if (command.Id.Length == 0) command.Id = Guid.NewGuid().ToString("N");
    command.SchemaVersion = 2; command.MinimumWorkerProtocol = WorkerProtocol.Current;
    Files.Save(Path.Combine(root, "IGMM", "commands", Guid.NewGuid().ToString("N") + ".json"), command); worker.ProcessCommands();
}
async Task Drive(QueueWorker worker, int rounds = 6, int sleepMs = 1200)
{
    for (int i = 0; i < rounds; i++)
    {
        await worker.Run(true);
        if (worker.Current.Jobs.All(j => j.State != UpdateStates.Queued && !UpdateStates.Busy(j.State) && j.State != UpdateStates.WaitingExit)) break;
        await Task.Delay(sleepMs);
    }
}
UpdateJob Job(string ns, string name, string packageRoot, string installed, string target, params string[] guids)
    => new() { Id = Guid.NewGuid().ToString("N"), Namespace = ns, Package = name, DisplayName = name, PackageRoot = packageRoot, InstalledVersion = installed, TargetVersion = target, PluginGuids = guids.ToList(), Enabled = true, Install = installed == "0.0.0" };
UpdateJob Result(QueueWorker worker, UpdateJob job) => worker.Current.Jobs.Single(j => j.Id == job.Id);
string Hash(string path) => Files.Hash(path);
byte[] PackageZip(string name, string version, byte[] dll, string dllPath = "", params string[] dependencies)
    => Fixtures.Zip(("manifest.json", Fixtures.Text(Fixtures.Manifest(name, version, dependencies))), ("README.md", Fixtures.Text("# " + name)), (dllPath.Length > 0 ? dllPath : "plugins/" + name + ".dll", dll));

// S1/S2/S7/S11: localization DLL beside the mod, enum soft dependency, rollback, slim queue.
{
    var (root, core) = NewRoot("s1"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string package = Installed(root, "[美化]Foo-汉化-1.0.0", "Foo", "1.0.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.0.0"), "Foo.dll");
    File.WriteAllBytes(Path.Combine(package, "FooZh.dll"), Fixtures.Plugin(core, "FooZh", "team.foo.zh", "FooZh", "1.0.0"));
    string oldHash = Hash(Path.Combine(package, "Foo.dll")), zhHash = Hash(Path.Combine(package, "FooZh.dll"));
    store.Add("Team", "Foo", "1.1.0", PackageZip("Foo", "1.1.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.1.0", softDependency: "team.other")));
    using var worker = new QueueWorker(root);
    var job = Job("Team", "Foo", package, "1.0.0", "1.1.0", "team.foo", "team.foo.zh");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job, LoadedAssemblyPaths = new() { Path.Combine(root, "plugins", "Other", "Other.dll") } });
    await Drive(worker);
    var result = Result(worker, job);
    Check("S1 同目录带汉化 DLL 的更新成功", result.State == UpdateStates.PendingRestart, result.State + " " + result.Error);
    Check("S1 Foo.dll 已替换", Hash(Path.Combine(package, "Foo.dll")) != oldHash);
    Check("S1 汉化 DLL 原样保留", File.Exists(Path.Combine(package, "FooZh.dll")) && Hash(Path.Combine(package, "FooZh.dll")) == zhHash);
    Check("S1 包内文档写入", File.Exists(Path.Combine(package, "README.md")));
    Check("S1 旧文件进了备份", Directory.Exists(result.BackupDirectory) && Directory.GetFiles(result.BackupDirectory, "*Foo.dll", SearchOption.AllDirectories).Length == 1);
    Check("S1 插件集合变化只是提示不阻断", result.Warnings.Any(w => w.Contains("插件集合")), string.Join(" | ", result.Warnings));
    Check("S1 没有请求旧版 zip", !store.Requests.Any(r => r.Contains("/1.0.0")), string.Join(" | ", store.Requests));
    Check("S1 安装后 stage 已清理", !Directory.Exists(Path.Combine(result.WorkDirectory, "new")) && !File.Exists(Path.Combine(result.WorkDirectory, "new.zip")));
    var plugins = InstallMetadata.Read(root, Path.Combine(package, "Foo.dll"));
    Check("S2 枚举软依赖可解析（原 Cecil bug）", plugins.Count == 1 && plugins[0].Dependencies.Count == 1 && plugins[0].Dependencies[0].Guid == "team.other" && plugins[0].Dependencies[0].Soft, string.Join(",", plugins.SelectMany(p => p.Dependencies).Select(d => d.Guid + ":" + d.Soft)));
    var persisted = Files.Read<UpdateSnapshot>(Path.Combine(root, "IGMM", "queue.json"));
    Check("S11 queue.json 任务不再携带 LoadedAssemblyPaths", persisted.Jobs.All(j => j.LoadedAssemblyPaths.Count == 0) && persisted.LoadedAssemblyPaths.Count == 1);
    Post(root, worker, new UpdateCommand { Action = "rollback", JobId = job.Id });
    await Drive(worker);
    result = Result(worker, job);
    Check("S7 回退恢复旧 Foo.dll", result.State == UpdateStates.RolledBack && Hash(Path.Combine(package, "Foo.dll")) == oldHash, result.State + " " + result.Error);
    Check("S7 回退不碰汉化 DLL", Hash(Path.Combine(package, "FooZh.dll")) == zhHash);
}

// S3: dependency installed under a user-named folder is recognized by manifest, not folder name.
{
    var (root, core) = NewRoot("s3"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    Installed(root, "[功能]我的库-Lib", "Lib", "1.3.0", Fixtures.Plugin(core, "Lib", "team.lib", "Lib", "1.3.0"), "Lib.dll");
    string app = Installed(root, "Team-App", "App", "1.0.0", Fixtures.Plugin(core, "App", "team.app", "App", "1.0.0"), "App.dll", "Team-Lib-1.2.0");
    store.Add("Team", "App", "2.0.0", PackageZip("App", "2.0.0", Fixtures.Plugin(core, "App", "team.app", "App", "2.0.0"), "", "Team-Lib-1.2.0"), "Team-Lib-1.2.0");
    using var worker = new QueueWorker(root);
    var job = Job("Team", "App", app, "1.0.0", "2.0.0", "team.app");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job }); await Drive(worker);
    var result = Result(worker, job);
    Check("S3 中文目录下的依赖被识别，不自动入队", result.State == UpdateStates.PendingRestart && worker.Current.Jobs.Count == 1, result.State + " jobs=" + worker.Current.Jobs.Count + " " + result.Error);
}

// S4: missing dependency is queued automatically and installed first.
{
    var (root, core) = NewRoot("s4"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string app = Installed(root, "Team-App", "App", "1.0.0", Fixtures.Plugin(core, "App", "team.app", "App", "1.0.0"), "App.dll", "Team-Lib-1.2.0");
    store.Add("Team", "App", "2.0.0", PackageZip("App", "2.0.0", Fixtures.Plugin(core, "App", "team.app", "App", "2.0.0"), "", "Team-Lib-1.2.0"), "Team-Lib-1.2.0");
    store.Add("Team", "Lib", "1.2.0", PackageZip("Lib", "1.2.0", Fixtures.Plugin(core, "Lib", "team.lib", "Lib", "1.2.0")));
    using var worker = new QueueWorker(root);
    var job = Job("Team", "App", app, "1.0.0", "2.0.0", "team.app");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job }); await Drive(worker, rounds: 8);
    var result = Result(worker, job); var lib = worker.Current.Jobs.FirstOrDefault(j => j.Package == "Lib");
    Check("S4 缺失依赖自动入队", lib != null && lib.AutoDependency && lib.Install && lib.RequestedBy == job.Id, lib?.State ?? "none");
    Check("S4 依赖装到 plugins/Team-Lib", File.Exists(Path.Combine(root, "plugins", "Team-Lib", "Lib.dll")) && File.Exists(Path.Combine(root, "plugins", "Team-Lib", "manifest.json")));
    Check("S4 主模组随后安装成功", result.State == UpdateStates.PendingRestart, result.State + " " + result.Message + " " + result.Error);
}

// S5: core package whose local files differ from the store still updates, with backup.
{
    var (root, core) = NewRoot("s5"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    File.WriteAllBytes(Path.Combine(core, "MonoDetour.Core.dll"), Fixtures.Text("old-local-build"));
    string package = Path.Combine(root, "plugins", "Mono-MonoDetour"); Directory.CreateDirectory(package); File.WriteAllText(Path.Combine(package, "manifest.json"), Fixtures.Manifest("MonoDetour", "0.7.0"));
    store.Add("Mono", "MonoDetour", "0.7.1", Fixtures.Zip(("manifest.json", Fixtures.Text(Fixtures.Manifest("MonoDetour", "0.7.1"))), ("BepInEx/core/MonoDetour.Core.dll", Fixtures.Text("new-official-build"))));
    using var worker = new QueueWorker(root);
    var job = Job("Mono", "MonoDetour", package, "0.7.0", "0.7.1");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job }); await Drive(worker);
    var result = Result(worker, job);
    Check("S5 Core 包本地文件与官方不同也能更新", result.State == UpdateStates.PendingRestart && File.ReadAllText(Path.Combine(core, "MonoDetour.Core.dll")) == "new-official-build", result.State + " " + result.Error);
    Check("S5 原 core 文件进了备份", Directory.GetFiles(result.BackupDirectory, "*MonoDetour.Core.dll", SearchOption.AllDirectories).Length == 1);
}

// S6: loaded DLL waits for exit; a fresh install does not; after exit the waiting job commits.
{
    var (root, core) = NewRoot("s6"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string package = Installed(root, "Team-Foo", "Foo", "1.0.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.0.0"), "Foo.dll");
    string oldHash = Hash(Path.Combine(package, "Foo.dll"));
    store.Add("Team", "Foo", "1.1.0", PackageZip("Foo", "1.1.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.1.0")));
    store.Add("Team", "Bar", "1.0.0", PackageZip("Bar", "1.0.0", Fixtures.Plugin(core, "Bar", "team.bar", "Bar", "1.0.0")));
    Files.RunningProbe = _ => true;
    using var worker = new QueueWorker(root);
    var update = Job("Team", "Foo", package, "1.0.0", "1.1.0", "team.foo");
    Post(root, worker, new UpdateCommand { Action = "add", Job = update, LoadedAssemblyPaths = new() { Path.Combine(package, "Foo.dll") } });
    await worker.Run(true);
    Check("S6 已加载的模组更新 → 等待退出", Result(worker, update).State == UpdateStates.WaitingExit, Result(worker, update).State + " " + Result(worker, update).Error);
    var install = Job("Team", "Bar", Path.Combine(root, "plugins", "Team-Bar"), "0.0.0", "1.0.0");
    Post(root, worker, new UpdateCommand { Action = "add", Job = install }); await worker.Run(true);
    Check("S6 新模组安装不等退出", Result(worker, install).State == UpdateStates.PendingRestart && File.Exists(Path.Combine(root, "plugins", "Team-Bar", "Bar.dll")), Result(worker, install).State + " " + Result(worker, install).Error);
    Files.RunningProbe = _ => false;
    await worker.Run(true);
    Check("S6 退出后自动提交等待中的更新", Result(worker, update).State == UpdateStates.PendingRestart && Hash(Path.Combine(package, "Foo.dll")) != oldHash, Result(worker, update).State + " " + Result(worker, update).Error);
}

// S8/S9: four jobs in parallel, one 404, others unaffected; no old-version downloads anywhere.
{
    var (root, core) = NewRoot("s9"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    var jobs = new List<UpdateJob>();
    foreach (string name in new[] { "A", "B", "C" })
    {
        string package = Installed(root, "Team-" + name, name, "1.0.0", Fixtures.Plugin(core, name, "team." + name.ToLower(), name, "1.0.0"), name + ".dll");
        store.Add("Team", name, "1.1.0", PackageZip(name, "1.1.0", Fixtures.Plugin(core, name, "team." + name.ToLower(), name, "1.1.0")));
        jobs.Add(Job("Team", name, package, "1.0.0", "1.1.0", "team." + name.ToLower()));
    }
    string missing = Installed(root, "Team-Missing", "Missing", "1.0.0", Fixtures.Plugin(core, "Missing", "team.missing", "Missing", "1.0.0"), "Missing.dll");
    jobs.Add(Job("Team", "Missing", missing, "1.0.0", "1.1.0", "team.missing"));
    using var worker = new QueueWorker(root);
    foreach (var job in jobs) Post(root, worker, new UpdateCommand { Action = "add", Job = job });
    await Drive(worker);
    Check("S9 并发中三个成功", jobs.Take(3).All(j => Result(worker, j).State == UpdateStates.PendingRestart), string.Join(",", jobs.Take(3).Select(j => Result(worker, j).State)));
    var failure = Result(worker, jobs[3]);
    Check("S8 404 报错清晰且不拖累其他", failure.State == UpdateStates.Failed && failure.Error.Contains("404"), failure.State + " " + failure.Error);
    Check("S8 全程未请求任何旧版本", !store.Requests.Any(r => r.Contains("/1.0.0")), string.Join(" | ", store.Requests.Where(r => r.Contains("/1.0.0"))));
}

// S10: a busy write lock means waiting, never a failure.
{
    var (root, core) = NewRoot("s10"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string package = Installed(root, "Team-Foo", "Foo", "1.0.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.0.0"), "Foo.dll");
    store.Add("Team", "Foo", "1.1.0", PackageZip("Foo", "1.1.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.1.0")));
    using var worker = new QueueWorker(root);
    var job = Job("Team", "Foo", package, "1.0.0", "1.1.0", "team.foo");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job });
    var hold = InstallationWriteLease.Acquire(root);
    var run = worker.Run(true);
    await Task.Delay(1500);
    string held = Result(worker, job).State;
    Check("S10 写锁被占用时排队等待而非失败", held != UpdateStates.Failed && held != UpdateStates.PendingRestart, held);
    hold.Dispose(); await run;
    Check("S10 锁释放后完成安装", Result(worker, job).State == UpdateStates.PendingRestart, Result(worker, job).State + " " + Result(worker, job).Error);
}

// S12: disk governance removes orphans and stage leftovers.
{
    var (root, core) = NewRoot("s12");
    using var worker = new QueueWorker(root);
    string mce = Path.Combine(root, "IGMM");
    string orphanJob = Path.Combine(mce, "jobs", Guid.NewGuid().ToString("N")), orphanBackup = Path.Combine(mce, "backups", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(orphanJob, "new")); File.WriteAllText(Path.Combine(orphanJob, "new", "x.txt"), "x"); Directory.CreateDirectory(orphanBackup); File.WriteAllText(Path.Combine(orphanBackup, "y.bak"), "y");
    Directory.SetLastWriteTimeUtc(orphanJob, DateTime.UtcNow.AddDays(-20)); Directory.SetLastWriteTimeUtc(orphanBackup, DateTime.UtcNow.AddDays(-20));
    string temporary = Path.Combine(mce, "queue.json." + Guid.NewGuid().ToString("N") + ".tmp"); File.WriteAllText(temporary, "{}"); File.SetLastWriteTimeUtc(temporary, DateTime.UtcNow.AddDays(-3));
    InstallStorage.Sweep(mce, worker.Current);
    Check("S12 两周前的孤儿任务/备份目录被清理", !Directory.Exists(orphanJob) && !Directory.Exists(orphanBackup));
    Check("S12 残留 .tmp 被清理", !File.Exists(temporary));
}

// S13: retry discards a legacy prepared journal and re-plans with the new core.
{
    var (root, core) = NewRoot("s13"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string package = Installed(root, "Team-Foo", "Foo", "1.0.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.0.0"), "Foo.dll");
    store.Add("Team", "Foo", "1.1.0", PackageZip("Foo", "1.1.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.1.0")));
    using var worker = new QueueWorker(root);
    var job = Job("Team", "Foo", package, "1.0.0", "1.1.0", "team.foo");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job });
    var live = Result(worker, job); live.State = UpdateStates.Failed; live.Error = UpdateMessages.Encode("Failed to resolve assembly: 'BepInEx, Version=5.4.23.5'");
    Transactions.Save(live, new Transaction { Root = root, PackageRoot = package, JobId = job.Id, Phase = "prepared", OldVersion = "1.0.0", NewVersion = "1.1.0" });
    Post(root, worker, new UpdateCommand { Action = "retry", JobId = job.Id }); await Drive(worker);
    var result = Result(worker, job); var plan = Files.Read<Transaction>(Transactions.PathFor(result));
    Check("S13 重试丢弃旧事务并用新管线成功", result.State == UpdateStates.PendingRestart && plan.Installer == "v3", result.State + " installer=" + plan.Installer + " " + result.Error);
}

// S14: commands that cannot run yet are deferred, not rejected.
{
    var (root, core) = NewRoot("s14"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string package = Installed(root, "Team-Foo", "Foo", "1.0.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.0.0"), "Foo.dll");
    store.Add("Team", "Foo", "1.1.0", PackageZip("Foo", "1.1.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.1.0")));
    using var worker = new QueueWorker(root);
    var job = Job("Team", "Foo", package, "1.0.0", "1.1.0", "team.foo");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job });
    string shopId = Guid.NewGuid().ToString("N");
    string localZip = Path.Combine(home, "s14", "Other.zip"); File.WriteAllBytes(localZip, PackageZip("Other", "1.0.0", Fixtures.Plugin(core, "Other", "team.other", "Other", "1.0.0")));
    Post(root, worker, new UpdateCommand { Id = shopId, Action = "local-import", LocalImport = new LocalImportRequest { Operation = "preview", SourcePath = localZip, PackageFolder = "Other" } });
    string commands = Path.Combine(root, "IGMM", "commands"), rejections = Path.Combine(root, "IGMM", "command-rejections");
    Check("S14 更新进行中的本地导入预览被延后而不是拒绝", Directory.GetFiles(commands).Length == 1 && (!Directory.Exists(rejections) || Directory.GetFiles(rejections).Length == 0));
    await Drive(worker); worker.ProcessCommands();
    Check("S14 更新完成后延后的命令被处理", Directory.GetFiles(commands).Length == 0 && File.Exists(Path.Combine(root, "IGMM", "command-receipts", shopId + ".json")));
}

// S15: a store preview-install inside update-batch becomes a queue job (the Core component route).
{
    var (root, core) = NewRoot("s15"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    store.Add("Mono", "MonoDetour", "0.7.1", Fixtures.Zip(("manifest.json", Fixtures.Text(Fixtures.Manifest("MonoDetour", "0.7.1"))), ("BepInEx/core/MonoDetour.Core.dll", Fixtures.Text("official"))));
    using var worker = new QueueWorker(root);
    string itemId = Guid.NewGuid().ToString("N");
    Post(root, worker, new UpdateCommand { Action = "update-batch", ResourceCommands = new() { new UpdateCommand { Id = itemId, Action = "shop", Shop = new ShopRequest { Operation = "preview-install", Namespace = "Mono", Package = "MonoDetour", Version = "0.7.1" } } } });
    var converted = worker.Current.Jobs.FirstOrDefault(j => j.Id == itemId);
    Check("S15 商店安装项转为队列任务", converted != null && converted.Install && converted.Package == "MonoDetour", converted?.State ?? "none");
    await Drive(worker);
    Check("S15 Core 组件安装成功", converted != null && Result(worker, converted).State == UpdateStates.PendingRestart && File.Exists(Path.Combine(core, "MonoDetour.Core.dll")) && File.Exists(Path.Combine(root, "plugins", "Mono-MonoDetour", "manifest.json")), converted == null ? "none" : Result(worker, converted).State + " " + Result(worker, converted).Error);
}

// S16/S17/S18: queue hygiene — clear-finished, a fresh attempt replaces old failures, failed operations can be removed, and the worker writes a log.
{
    var (root, core) = NewRoot("s16"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string package = Installed(root, "Team-Foo", "Foo", "1.0.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.0.0"), "Foo.dll");
    store.Add("Team", "Foo", "1.1.0", PackageZip("Foo", "1.1.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.1.0")));
    using var worker = new QueueWorker(root);
    string mce = Path.Combine(root, "IGMM");
    var old = Job("Team", "Foo", package, "1.0.0", "1.1.0", "team.foo");
    Post(root, worker, new UpdateCommand { Action = "add", Job = old });
    Result(worker, old).State = UpdateStates.Failed; Result(worker, old).Error = "legacy failure";
    string failedOperation = Guid.NewGuid().ToString("N"), busyOperation = Guid.NewGuid().ToString("N");
    Directory.CreateDirectory(Path.Combine(mce, "operations", failedOperation, "shop")); Files.Save(Path.Combine(mce, "operations", failedOperation, "request.json"), new UpdateCommand { Id = failedOperation, Action = "shop", Shop = new ShopRequest { Operation = "preview-install", Namespace = "Mono", Package = "Core", Version = "1.0.0" } });
    Files.Save(Path.Combine(mce, "operations", failedOperation, "shop", "task.json"), new ShopTask { Id = Guid.NewGuid().ToString("N"), State = "failed", Error = "Original official file missing", Request = new ShopRequest { Namespace = "Mono", Package = "Core", Version = "1.0.0", Operation = "approve-download" } });
    Post(root, worker, new UpdateCommand { Action = "operation-remove", JobId = failedOperation });
    Check("S18 失败的操作可以移除", !Directory.Exists(Path.Combine(mce, "operations", failedOperation)));
    var fresh = Job("Team", "Foo", package, "1.0.0", "1.1.0", "team.foo");
    Post(root, worker, new UpdateCommand { Action = "add", Job = fresh });
    Check("S17 同包的新任务让旧失败项让位", Result(worker, old).Hidden && Result(worker, old).State == UpdateStates.Removed && Result(worker, fresh).State == UpdateStates.Queued, Result(worker, old).State + "/" + Result(worker, old).Hidden);
    await Drive(worker);
    Check("S17 新任务照常完成", Result(worker, fresh).State == UpdateStates.PendingRestart, Result(worker, fresh).State + " " + Result(worker, fresh).Error);
    // A live operation (fixture without journal, created after the loop so nothing pumps it) must survive both remove and clear.
    Directory.CreateDirectory(Path.Combine(mce, "operations", busyOperation, "shop")); Files.Save(Path.Combine(mce, "operations", busyOperation, "request.json"), new UpdateCommand { Id = busyOperation, Action = "shop", Shop = new ShopRequest { Operation = "preview-uninstall", Namespace = "Team", Package = "Gone", Version = "1.0.0" } });
    Files.Save(Path.Combine(mce, "operations", busyOperation, "shop", "task.json"), new ShopTask { Id = Guid.NewGuid().ToString("N"), State = "committing", Request = new ShopRequest { Namespace = "Team", Package = "Gone", Version = "1.0.0", Operation = "confirm-uninstall" } });
    Post(root, worker, new UpdateCommand { Action = "operation-remove", JobId = busyOperation });
    Check("S18 执行中的操作不能被移除（延后）", Directory.Exists(Path.Combine(mce, "operations", busyOperation)) && Directory.GetFiles(Path.Combine(mce, "commands")).Length == 1);
    foreach (string stale in Directory.GetFiles(Path.Combine(mce, "commands"))) File.Delete(stale);
    Post(root, worker, new UpdateCommand { Action = "clear-finished" });
    var persisted = Files.Read<UpdateSnapshot>(Path.Combine(mce, "queue.json"));
    Check("S16 一键清空隐藏已结束项并保留 pending-restart 状态", persisted.Jobs.All(j => j.Hidden) && persisted.Jobs.Single(j => j.Id == fresh.Id).State == UpdateStates.PendingRestart);
    Check("S16 清空后执行中的操作仍保留", Directory.Exists(Path.Combine(mce, "operations", busyOperation)));
    var logs = Directory.Exists(Path.Combine(mce, "logs")) ? Directory.GetFiles(Path.Combine(mce, "logs"), "worker-*.log") : Array.Empty<string>();
    Check("S19 worker 写日志且含状态流转", logs.Length == 1 && File.ReadAllText(logs[0]).Contains("queued Foo") && File.ReadAllText(logs[0]).Contains("clear-finished"), logs.Length.ToString());
}

// S20/S21/S22: uninstall as a job (whole folder to backup, dependents only noted, rollback restores), duplicate store commands collapse, local-source install.
{
    var (root, core) = NewRoot("s20"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string package = Installed(root, "[功能]Foo-汉化", "Foo", "1.0.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.0.0"), "Foo.dll");
    Directory.CreateDirectory(Path.Combine(package, "data")); File.WriteAllText(Path.Combine(package, "data", "cache.txt"), "runtime");
    Installed(root, "Team-Dep", "Dep", "1.0.0", Fixtures.Plugin(core, "Dep", "team.dep", "Dep", "1.0.0"), "Dep.dll", "Team-Foo-1.0.0");
    string bar = Installed(root, "Team-Bar", "Bar", "2.0.0", Fixtures.Plugin(core, "Bar", "team.bar", "Bar", "2.0.0"), "Bar.dll");
    using var worker = new QueueWorker(root);
    string itemId = Guid.NewGuid().ToString("N");
    Post(root, worker, new UpdateCommand { Action = "update-batch", ResourceCommands = new() { new UpdateCommand { Id = itemId, Action = "shop", Shop = new ShopRequest { Operation = "preview-uninstall", Namespace = "Team", Package = "Foo", Version = "1.0.0" } } } });
    var job = worker.Current.Jobs.Single(j => j.Id == itemId);
    Check("S20 卸载请求转为队列任务", job.Uninstall && job.PackageRoot == package, job.PackageRoot);
    await Drive(worker);
    job = worker.Current.Jobs.Single(j => j.Id == itemId);
    Check("S20 整个目录移入备份，目录消失", job.State == UpdateStates.PendingRestart && !Directory.Exists(package), job.State + " " + job.Error);
    Check("S20 备份里有 DLL、清单和运行时文件", Directory.GetFiles(job.BackupDirectory, "*Foo.dll", SearchOption.AllDirectories).Length == 1 && Directory.GetFiles(job.BackupDirectory, "*cache.txt", SearchOption.AllDirectories).Length == 1);
    Check("S20 被依赖只是提示不阻断", job.Warnings.Any(w => w.Contains("仍被依赖")), string.Join(" | ", job.Warnings));
    Post(root, worker, new UpdateCommand { Action = "rollback", JobId = itemId }); await Drive(worker);
    job = worker.Current.Jobs.Single(j => j.Id == itemId);
    Check("S20 卸载可回退", job.State == UpdateStates.RolledBack && File.Exists(Path.Combine(package, "Foo.dll")) && File.Exists(Path.Combine(package, "manifest.json")) && File.Exists(Path.Combine(package, "data", "cache.txt")), job.State + " " + job.Error);
    Post(root, worker, new UpdateCommand { Action = "shop", Shop = new ShopRequest { Operation = "preview-uninstall", Namespace = "Team", Package = "Bar", Version = "2.0.0" } });
    Post(root, worker, new UpdateCommand { Action = "shop", Shop = new ShopRequest { Operation = "confirm-uninstall", Namespace = "Team", Package = "Bar", Version = "2.0.0" } });
    Check("S21 商店直发的 preview/confirm 卸载合并为一个任务", worker.Current.Jobs.Count(j => j.Package == "Bar" && !j.Hidden) == 1, worker.Current.Jobs.Count(j => j.Package == "Bar").ToString());
    await Drive(worker);
    Check("S21 直发商店卸载完成", worker.Current.Jobs.Single(j => j.Package == "Bar" && !j.Hidden).State == UpdateStates.PendingRestart && !Directory.Exists(bar));
    string stage = Path.Combine(home, "s20", "local-content"); Directory.CreateDirectory(Path.Combine(stage, "plugins", "MyLocal"));
    File.WriteAllBytes(Path.Combine(stage, "plugins", "MyLocal", "MyLocal.dll"), Fixtures.Plugin(core, "MyLocal", "team.local", "MyLocal", "1.0.0"));
    File.WriteAllText(Path.Combine(stage, "plugins", "MyLocal", "manifest.json"), Fixtures.Manifest("MyLocal", "1.0.0"));
    var local = new UpdateJob { Id = Guid.NewGuid().ToString("N"), Namespace = "local", Package = "MyLocal", DisplayName = "MyLocal", PackageRoot = Path.Combine(root, "plugins", "MyLocal"), InstalledVersion = "0.0.0", TargetVersion = "1.0.0", Install = true, LocalSource = stage, Enabled = true };
    Post(root, worker, new UpdateCommand { Action = "add", Job = local }); await Drive(worker);
    Check("S22 本地包按 BepInEx 树安装且不重复包目录", Result(worker, local).State == UpdateStates.PendingRestart && File.Exists(Path.Combine(root, "plugins", "MyLocal", "MyLocal.dll")) && !Directory.Exists(Path.Combine(root, "plugins", "MyLocal", "MyLocal")), Result(worker, local).State + " " + Result(worker, local).Error);
}

// S23: local import — read-only preview, then confirm becomes an install job through the same core.
{
    var (root, core) = NewRoot("s23"); ShopQueue.TestHandler = null;
    string zip = Path.Combine(home, "s23", "Imported.zip"); File.WriteAllBytes(zip, PackageZip("Imported", "1.2.0", Fixtures.Plugin(core, "Imported", "team.imported", "Imported", "1.2.0")));
    using var worker = new QueueWorker(root);
    Post(root, worker, new UpdateCommand { Action = "local-import", LocalImport = new LocalImportRequest { Operation = "preview", SourcePath = zip, PackageFolder = "Imported" } });
    var preview = Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json"));
    Check("S23 本地导入预览就绪且无阻断", preview.State == "ready" && preview.Blockers.Count == 0, preview.State + " " + string.Join(" | ", preview.Blockers));
    Post(root, worker, new UpdateCommand { Action = "local-import", LocalImport = new LocalImportRequest { Operation = "confirm", PreviewId = preview.Id, PlanToken = preview.PlanToken } });
    var handoff = Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json"));
    var job = worker.Current.Jobs.FirstOrDefault(j => j.LocalSource.Length > 0);
    Check("S23 确认后转为本地源任务，预览任务标记完成", handoff.State == "completed" && job != null && job.Install && job.Package == "Imported", handoff.State + " jobs=" + worker.Current.Jobs.Count);
    await Drive(worker);
    Check("S23 本地导入安装完成", job != null && Result(worker, job).State == UpdateStates.PendingRestart && File.Exists(Path.Combine(root, "plugins", "Imported", "Imported.dll")) && File.Exists(Path.Combine(root, "plugins", "Imported", "manifest.json")), job == null ? "none" : Result(worker, job).State + " " + Result(worker, job).Error);
}

// S24/S25: folder organization — unregistered folders organize too, and a move can be rolled back.
{
    var (root, core) = NewRoot("s24");
    string unowned = Installed(root, "[功能]别人的包-ManualMod", "ManualMod", "1.0.0", Fixtures.Plugin(core, "ManualMod", "manual.mod", "ManualMod", "1.0.0"), "ManualMod.dll");
    using var worker = new QueueWorker(root);
    void PostLayout(LayoutQueueRequest request) { string id = Guid.NewGuid().ToString("N"); Files.Save(Path.Combine(root, "IGMM", "commands", id + ".json"), new UpdateCommand { Id = id, Action = "layout", Layout = request }); worker.ProcessCommands(); }
    PostLayout(new LayoutQueueRequest { Mode = InstallationLayoutMode.GroupOnly, Groups = new() { new LayoutQueueGroup { Id = "tools", Name = "Tools" } }, Mods = new() { new LayoutQueueMod { Guid = "manual.mod", GroupId = "tools" } } });
    var preview = Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json"));
    Check("S25 未登记为管理器安装的文件夹也可整理", preview.State == "ready" && preview.Blockers.Count == 0 && preview.Desired.Count > 0, preview.State + " " + string.Join(" | ", preview.Blockers) + " " + string.Join(" | ", preview.Details));
    Check("S25 预览给出分组目标路径（无现成目录时直接用分组名）", preview.Desired.All(p => p.Path.StartsWith("plugins/Tools/", StringComparison.OrdinalIgnoreCase)), preview.Desired.First().Path);
    string movedRoot = Path.Combine(root, Path.GetDirectoryName(preview.Desired.First().Path)!.Replace('/', Path.DirectorySeparatorChar));
    PostLayout(new LayoutQueueRequest { Operation = "confirm", PreviewId = preview.Id, PlanToken = preview.PlanToken });
    var job = worker.Current.Jobs.Single(j => j.Layout);
    await Drive(worker);
    Check("S24 整理任务移动目录", job.State == UpdateStates.PendingRestart && !Directory.Exists(unowned), job.State + " " + job.Error);
    Check("S24 目标目录与文件都在", Directory.Exists(movedRoot) && File.Exists(Path.Combine(movedRoot, "ManualMod.dll")), movedRoot);
    Post(root, worker, new UpdateCommand { Action = "rollback", JobId = job.Id }); await Drive(worker);
    Check("S24 整理可回退", job.State == UpdateStates.RolledBack && Directory.Exists(unowned) && File.Exists(Path.Combine(unowned, "ManualMod.dll")) && !Directory.Exists(movedRoot), job.State + " " + job.Error);
    Check("S24 回退后空的分组目录被清掉", !Directory.Exists(Path.Combine(root, "plugins", Path.GetFileName(Path.GetDirectoryName(movedRoot)!))));
}

// S26: a package whose manifest sits directly in plugins/ (the red-error case) updates in place.
{
    var (root, core) = NewRoot("s26"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    byte[] oldDll = Fixtures.Plugin(core, "Direct", "team.direct", "Direct", "1.0.0");
    File.WriteAllBytes(Path.Combine(root, "plugins", "Direct.dll"), oldDll);
    File.WriteAllText(Path.Combine(root, "plugins", "manifest.json"), Fixtures.Manifest("Direct", "1.0.0"));
    store.Add("Team", "Direct", "1.1.0", PackageZip("Direct", "1.1.0", Fixtures.Plugin(core, "Direct", "team.direct", "Direct", "1.1.0")));
    using var worker = new QueueWorker(root);
    var job = Job("Team", "Direct", Path.Combine(root, "plugins"), "1.0.0", "1.1.0", "team.direct");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job }); await Drive(worker);
    var result = Result(worker, job);
    Check("S26 plugins 根下的包可以更新（原红字根因）", result.State == UpdateStates.PendingRestart, result.State + " " + result.Error);
    Check("S26 新 DLL 已替换", !File.ReadAllBytes(Path.Combine(root, "plugins", "Direct.dll")).SequenceEqual(oldDll));
    Check("S26 旧 DLL 进了备份", Directory.GetFiles(result.BackupDirectory, "*Direct.dll", SearchOption.AllDirectories).Length == 1);
}

// S27: a same-named payload left in another core/patchers folder (typical after a manager-installed package is
// updated in the package's own layout) is retired, otherwise BepInEx loads it twice and refuses the second one.
{
    var (root, core) = NewRoot("s27"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    Directory.CreateDirectory(Path.Combine(core, "MonoDetour-Old")); File.WriteAllBytes(Path.Combine(core, "MonoDetour-Old", "payload.dll"), Fixtures.Text("old-copy"));
    Directory.CreateDirectory(Path.Combine(root, "patchers", "OldPack")); File.WriteAllBytes(Path.Combine(root, "patchers", "OldPack", "payload2.dll"), Fixtures.Text("old-patcher"));
    string package = Installed(root, "Mono-OldPack", "OldPack", "1.0.0", Fixtures.Plugin(core, "OldPack", "mono.oldpack", "OldPack", "1.0.0"), "OldPack.dll");
    store.Add("Mono", "OldPack", "1.1.0", Fixtures.Zip(
        ("manifest.json", Fixtures.Text(Fixtures.Manifest("OldPack", "1.1.0"))),
        ("BepInEx/core/payload.dll", Fixtures.Text("new-copy")),
        ("BepInEx/patchers/payload2.dll", Fixtures.Text("new-patcher"))));
    using var worker = new QueueWorker(root);
    var job = Job("Mono", "OldPack", package, "1.0.0", "1.1.0", "mono.oldpack");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job }); await Drive(worker);
    var result = Result(worker, job);
    Check("S27 更新成功", result.State == UpdateStates.PendingRestart, result.State + " " + result.Error);
    Check("S27 新副本落在包自带布局", File.Exists(Path.Combine(core, "payload.dll")) && File.ReadAllText(Path.Combine(core, "payload.dll")) == "new-copy");
    Check("S27 core 下同名旧副本被归档", !File.Exists(Path.Combine(core, "MonoDetour-Old", "payload.dll"))
        && Directory.GetFiles(result.BackupDirectory, "*payload.dll", SearchOption.AllDirectories).Length == 1, string.Join(" | ", result.Warnings));
    Check("S27 patchers 下同名旧副本被归档", !File.Exists(Path.Combine(root, "patchers", "OldPack", "payload2.dll"))
        && Directory.GetFiles(result.BackupDirectory, "*payload2.dll", SearchOption.AllDirectories).Length == 1);
    Check("S27 归档原因写入提示", result.Warnings.Any(w => w.Contains("同名旧副本")), string.Join(" | ", result.Warnings));
}

// S28: identical payload already installed + a same-named stale copy elsewhere under core/patchers → the stale
// copy is retired even though nothing needs writing (the exact state the real install was in).
{
    var (root, core) = NewRoot("s28"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    byte[] payload = Fixtures.Text("same-payload");
    File.WriteAllBytes(Path.Combine(core, "payload.dll"), payload);                       // already installed (flat)
    Directory.CreateDirectory(Path.Combine(core, "OldPackFolder"));
    File.WriteAllBytes(Path.Combine(core, "OldPackFolder", "payload.dll"), payload);      // stale same-named copy
    string package = Installed(root, "Mono-OldPack", "OldPack", "1.0.0", Fixtures.Plugin(core, "OldPack", "mono.oldpack", "OldPack", "1.0.0"), "OldPack.dll");
    store.Add("Mono", "OldPack", "1.1.0", Fixtures.Zip(
        ("manifest.json", Fixtures.Text(Fixtures.Manifest("OldPack", "1.1.0"))),
        ("BepInEx/core/payload.dll", payload)));
    using var worker = new QueueWorker(root);
    var job = Job("Mono", "OldPack", package, "1.0.0", "1.1.0", "mono.oldpack");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job }); await Drive(worker);
    var result = Result(worker, job);
    Check("S28 内容一致时也能归档同名旧副本", result.State == UpdateStates.PendingRestart
        && !File.Exists(Path.Combine(core, "OldPackFolder", "payload.dll"))
        && File.Exists(Path.Combine(core, "payload.dll"))
        && Directory.GetFiles(result.BackupDirectory, "*payload.dll", SearchOption.AllDirectories).Length == 1,
        result.State + " | " + string.Join(" | ", result.Warnings));
    Check("S28 不会碰别包的同名文档/清单", Directory.GetFiles(root, "manifest.json", SearchOption.AllDirectories).Length >= 2
        && Directory.GetFiles(result.BackupDirectory, "*manifest.json", SearchOption.AllDirectories).Length <= 1,
        string.Join(" | ", Directory.GetFiles(result.BackupDirectory, "*", SearchOption.AllDirectories).Select(Path.GetFileName)));
}

// S29: the "writing" marker exists while files are being moved, is cleared afterwards, and a stale record
// (process gone) never blocks a later start.
{
    var (root, core) = NewRoot("s29"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string package = Installed(root, "Team-Foo", "Foo", "1.0.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.0.0"), "Foo.dll");
    store.Add("Team", "Foo", "1.1.0", PackageZip("Foo", "1.1.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.1.0")));
    bool sawMarker = false;
    using var worker = new QueueWorker(root);
    Installer.StepProbe = step => { if (step.StartsWith("placed:")) sawMarker |= UpdateWriteMarker.IsActive(root); };
    var job = Job("Team", "Foo", package, "1.0.0", "1.1.0", "team.foo");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job }); await Drive(worker);
    Installer.StepProbe = null;
    Check("S29 写文件期间存在“正在写入”标记", sawMarker, Result(worker, job).State);
    Check("S29 提交完成后标记被清除", !UpdateWriteMarker.IsActive(root) && !File.Exists(UpdateWriteMarker.PathFor(root)));
    File.WriteAllLines(UpdateWriteMarker.PathFor(root), new[] { "job=dead", "name=ghost", "pid=999999", "started=" + DateTime.UtcNow.ToString("O") });
    Check("S29 死进程残留的标记被判过期并清掉", !UpdateWriteMarker.IsActive(root) && !File.Exists(UpdateWriteMarker.PathFor(root)), UpdateWriteMarker.Describe(root));
    UpdateWriteMarker.Write(root, "live", "me");
    Check("S29 活动标记被识别为正在写入", UpdateWriteMarker.IsActive(root), UpdateWriteMarker.Describe(root));
    UpdateWriteMarker.Clear(root);
}

// S30: a fork→upstream update changes the GUID (the real Foothold case). The superseded copy — same plugin type and
// same display name, nested in the package's old plugins/ layout — is retired so BepInEx does not load both; the
// localization companion (different name) stays; the retired copy is loaded so the job waits for exit; rollback restores it.
{
    var (root, core) = NewRoot("s30"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string package = Path.Combine(root, "plugins", "[功能]Foo-Forked"); Directory.CreateDirectory(Path.Combine(package, "plugins"));
    string forked = Path.Combine(package, "plugins", "Foo-Forked.dll");
    File.WriteAllBytes(forked, Fixtures.Plugin(core, "Foo-Forked", "Foo-Forked", "Foo?", "1.5.1"));
    File.WriteAllBytes(Path.Combine(package, "FooZh.dll"), Fixtures.Plugin(core, "FooZh", "team.foo.zh", "FooZh", "1.0.0"));
    File.WriteAllText(Path.Combine(package, "manifest.json"), Fixtures.Manifest("Foo", "1.6.1"));
    string forkedHash = Hash(forked), zhHash = Hash(Path.Combine(package, "FooZh.dll"));
    store.Add("Team", "Foo", "1.6.3", PackageZip("Foo", "1.6.3", Fixtures.Plugin(core, "Foo", "Foo", "Foo?", "1.6.3")));
    Files.RunningProbe = _ => true;
    using var worker = new QueueWorker(root);
    var job = Job("Team", "Foo", package, "1.6.1", "1.6.3", "Foo-Forked");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job, LoadedAssemblyPaths = new() { forked } });
    await worker.Run(true);
    Check("S30 被取代的旧副本已加载 → 等待退出", Result(worker, job).State == UpdateStates.WaitingExit, Result(worker, job).State + " " + Result(worker, job).Error);
    Files.RunningProbe = _ => false;
    await Drive(worker);
    var result = Result(worker, job);
    Check("S30 更新成功", result.State == UpdateStates.PendingRestart, result.State + " " + result.Error);
    Check("S30 新版 DLL 落在包根", File.Exists(Path.Combine(package, "Foo.dll")));
    Check("S30 GUID 不同的旧副本被归档（不再被加载）", !File.Exists(forked) && Directory.GetFiles(result.BackupDirectory, "*Foo-Forked.dll", SearchOption.AllDirectories).Length == 1, string.Join(" | ", result.Warnings));
    Check("S30 汉化伴随插件原样保留", File.Exists(Path.Combine(package, "FooZh.dll")) && Hash(Path.Combine(package, "FooZh.dll")) == zhHash);
    Check("S30 归档原因写入提示", result.Warnings.Any(w => w.Contains("已归档避免重复加载")), string.Join(" | ", result.Warnings));
    Check("S30 提示不再把已归档 GUID 写成原样保留", !result.Warnings.Any(w => w.Contains("本地另有 Foo-Forked")), string.Join(" | ", result.Warnings));
    Post(root, worker, new UpdateCommand { Action = "rollback", JobId = job.Id }); await Drive(worker);
    result = Result(worker, job);
    Check("S30 回退恢复旧副本", result.State == UpdateStates.RolledBack && File.Exists(forked) && Hash(forked) == forkedHash, result.State + " " + result.Error);
}

// S31: a category folder that holds packages is recognized as its group's folder — its packages stay put and a stray
// package moves in beside them instead of into a newly created hashed group folder.
{
    var (root, core) = NewRoot("s31");
    string inside = Installed(root, "1拓展模组/[功能]Foo-Foo", "Foo", "1.0.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.0.0"), "Foo.dll");
    string stray = Installed(root, "Bar-Bar", "Bar", "1.0.0", Fixtures.Plugin(core, "Bar", "team.bar", "Bar", "1.0.0"), "Bar.dll");
    string loose = Installed(root, "1拓展模组/[功能]Qux-Qux", "Qux", "1.0.0", Fixtures.Plugin(core, "Qux", "team.qux", "Qux", "1.0.0"), "Qux.dll");
    string tool = Installed(root, "Baz-Baz", "Baz", "1.0.0", Fixtures.Plugin(core, "Baz", "team.baz", "Baz", "1.0.0"), "Baz.dll");
    using var worker = new QueueWorker(root);
    void PostLayout(LayoutQueueRequest request) { string id = Guid.NewGuid().ToString("N"); Files.Save(Path.Combine(root, "IGMM", "commands", id + ".json"), new UpdateCommand { Id = id, Action = "layout", Layout = request }); worker.ProcessCommands(); }
    PostLayout(new LayoutQueueRequest
    {
        Mode = InstallationLayoutMode.GroupOnly,
        Groups = new() { new LayoutQueueGroup { Id = "folder-extension", Name = "1拓展模组" }, new LayoutQueueGroup { Id = "tools", Name = "Tools" } },
        Mods = new() { new LayoutQueueMod { Guid = "team.foo", GroupId = "folder-extension" }, new LayoutQueueMod { Guid = "team.bar", GroupId = "folder-extension" }, new LayoutQueueMod { Guid = "team.baz", GroupId = "tools" } }
    });
    var preview = Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json"));
    Check("S31 分类目录被识别为分组目录", preview.Details.Any(d => d.Contains("分组目录") && d.Contains("1拓展模组")), string.Join(" | ", preview.Details));
    Check("S31 已就位的包不动", preview.Details.Any(d => d.Contains("位置已符合") && d.Contains("[功能]Foo-Foo")), string.Join(" | ", preview.Details));
    Check("S31 散包搬进现有分类目录而非哈希目录", preview.Desired.Any(p => p.Path.StartsWith("plugins/1拓展模组/Bar-Bar/", StringComparison.OrdinalIgnoreCase)) && !preview.Desired.Any(p => p.Path.Contains("--")), string.Join(" | ", preview.Desired.Select(p => p.Path)));
    Check("S31 未分组的包原位不动", preview.Details.Any(d => d.Contains("未分组，保持原位") && d.Contains("Qux")) && !preview.Desired.Any(p => p.Path.Contains("Qux")), string.Join(" | ", preview.Details));
    Check("S31 无现成目录的分组用分组名新建", preview.Desired.Any(p => p.Path.StartsWith("plugins/Tools/Baz-Baz/", StringComparison.OrdinalIgnoreCase)), string.Join(" | ", preview.Desired.Select(p => p.Path)));
    Check("S31 预览无阻断", preview.Blockers.Count == 0, string.Join(" | ", preview.Blockers));
    PostLayout(new LayoutQueueRequest { Operation = "confirm", PreviewId = preview.Id, PlanToken = preview.PlanToken });
    var job = worker.Current.Jobs.Single(j => j.Layout);
    await Drive(worker);
    string container = Path.Combine(root, "plugins", "1拓展模组");
    Check("S31 整理后包落在分类目录下", job.State == UpdateStates.PendingRestart && !Directory.Exists(stray) && File.Exists(Path.Combine(container, "Bar-Bar", "Bar.dll")) && File.Exists(Path.Combine(inside, "Foo.dll")) && File.Exists(Path.Combine(loose, "Qux.dll")) && File.Exists(Path.Combine(root, "plugins", "Tools", "Baz-Baz", "Baz.dll")), job.State + " " + job.Error);
    Post(root, worker, new UpdateCommand { Action = "rollback", JobId = job.Id }); await Drive(worker);
    Check("S31 整理可回退", job.State == UpdateStates.RolledBack && File.Exists(Path.Combine(stray, "Bar.dll")) && !Directory.Exists(Path.Combine(container, "Bar-Bar")) && Directory.Exists(inside) && File.Exists(Path.Combine(tool, "Baz.dll")) && !Directory.Exists(Path.Combine(root, "plugins", "Tools")), job.State + " " + job.Error);
}

// S32: a pending-restart job whose installed plugin was later moved away can never be confirmed by a launch; observe
// closes it as superseded instead of leaving it pending (the game re-asked every 10 seconds for days). A job that is
// still intact keeps waiting, and one installed under a disabled ".dll.*" name completes once it is not loaded.
{
    var (root, core) = NewRoot("s32"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string moved = Installed(root, "Moved", "Moved", "1.0.0", Fixtures.Plugin(core, "Moved", "team.moved", "Moved", "1.0.0"), "Moved.dll");
    string intact = Installed(root, "Intact", "Intact", "1.0.0", Fixtures.Plugin(core, "Intact", "team.intact", "Intact", "1.0.0"), "Intact.dll");
    string disabled = Installed(root, "Off", "Off", "1.0.0", Fixtures.Plugin(core, "Off", "team.off", "Off", "1.0.0"), "Off.dll.b");
    foreach (var name in new[] { "Moved", "Intact", "Off" })
        store.Add("Team", name, "1.1.0", PackageZip(name, "1.1.0", Fixtures.Plugin(core, name, "team." + name.ToLowerInvariant(), name, "1.1.0")));
    using var worker = new QueueWorker(root);
    var jobs = new[] { Job("Team", "Moved", moved, "1.0.0", "1.1.0", "team.moved"), Job("Team", "Intact", intact, "1.0.0", "1.1.0", "team.intact"), Job("Team", "Off", disabled, "1.0.0", "1.1.0", "team.off") };
    foreach (var job in jobs) Post(root, worker, new UpdateCommand { Action = "add", Job = job });
    await Drive(worker);
    Check("S32 三项均安装待重启", jobs.All(j => Result(worker, j).State == UpdateStates.PendingRestart), string.Join(", ", jobs.Select(j => j.Package + "=" + Result(worker, j).State + " " + Result(worker, j).Error)));
    Directory.Move(moved, moved + "-elsewhere");
    using var launch = System.Diagnostics.Process.GetCurrentProcess();
    Post(root, worker, new UpdateCommand { Action = "observe", SourceProcessId = launch.Id, LoadedAssemblyPaths = new() { Path.Combine(root, "plugins", "Unrelated", "Unrelated.dll") } });
    Check("S32 文件已被移走的任务收尾为已接替", Result(worker, jobs[0]).State == UpdateStates.Superseded && UpdateMessages.TryDecode(Result(worker, jobs[0]).Message, out var term, out _) && term!.Id == "installed-files-changed-later", Result(worker, jobs[0]).State + " " + Result(worker, jobs[0]).Message);
    Check("S32 文件完好但未加载的任务继续等待", Result(worker, jobs[1]).State == UpdateStates.PendingRestart, Result(worker, jobs[1]).State);
    Check("S32 以停用名安装且未加载的任务视为完成", Result(worker, jobs[2]).State == UpdateStates.Completed, Result(worker, jobs[2]).State + " " + Result(worker, jobs[2]).Message);
}

// S33: a folder preview the user backed out of is closed by a cancel command even while an update is waiting for the
// game to exit; a preview whose requesting game process is gone is closed at the next worker start. Neither touches files.
// S34: a store download records its duration and transferred bytes for the row's speed | time note.
{
    var (root, core) = NewRoot("s33"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string a = Installed(root, "Foo-Foo", "Foo", "1.0.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.0.0"), "Foo.dll");
    using var launch = System.Diagnostics.Process.GetCurrentProcess();
    void PostLayout(QueueWorker w, LayoutQueueRequest request) { string id = Guid.NewGuid().ToString("N"); Files.Save(Path.Combine(root, "IGMM", "commands", id + ".json"), new UpdateCommand { Id = id, Action = "layout", Layout = request }); w.ProcessCommands(); }
    ShopTask Task() => Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json"));
    using (var worker = new QueueWorker(root))
    {
        PostLayout(worker, new LayoutQueueRequest { Mode = InstallationLayoutMode.GroupOnly, SourceProcessId = launch.Id,
            Groups = new() { new LayoutQueueGroup { Id = "tools", Name = "Tools" } }, Mods = new() { new LayoutQueueMod { Guid = "team.foo", GroupId = "tools" } } });
        var preview = Task();
        Check("S33 预览就绪", preview.State == "ready" && preview.Request.Operation == "layout-preview", preview.State + " " + preview.Error);
        // An update waiting for exit used to defer every layout command, including a cancel.
        store.Add("Team", "Foo", "1.1.0", PackageZip("Foo", "1.1.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.1.0")));
        var update = Job("Team", "Foo", a, "1.0.0", "1.1.0", "team.foo");
        Files.RunningProbe = _ => true; // the game is "running", so replacing the loaded Foo.dll waits for exit
        Post(root, worker, new UpdateCommand { Action = "add", Job = update, LoadedAssemblyPaths = new() { Path.Combine(a, "Foo.dll") } });
        // Run(true) keeps watching a waiting-exit job, so stop the loop once the job has reached that state.
        var stopAt = DateTime.UtcNow.AddSeconds(30);
        worker.StopRequested = () => Result(worker, update).State == UpdateStates.WaitingExit || DateTime.UtcNow > stopAt;
        await worker.Run(true);
        worker.StopRequested = null;
        var updated = Result(worker, update);
        Check("S33 更新处于等待退出", updated.State == UpdateStates.WaitingExit, updated.State + " " + updated.Error);
        Check("S34 下载记录耗时与字节", updated.DownloadSeconds > 0 && updated.DownloadTransferredBytes > 0 && updated.DownloadTransferredBytes <= updated.TotalBytes, updated.State + " s=" + updated.DownloadSeconds + " b=" + updated.DownloadTransferredBytes + "/" + updated.TotalBytes);
        PostLayout(worker, new LayoutQueueRequest { Operation = "cancel", PreviewId = preview.Id });
        Check("S33 有更新等待退出时取消仍立即生效", Task().State == "cancelled" && Directory.Exists(a), Task().State + " · " + updated.State);
        Check("S33 取消后命令收件箱清空", Directory.GetFiles(Path.Combine(root, "IGMM", "commands"), "*.json").Length == 0);
        Files.RunningProbe = _ => false;
    }
    using (var worker = new QueueWorker(root))
    {
        // Let the waiting update finish first; otherwise the new preview is deferred behind it.
        await Drive(worker);
        // A preview left behind by a game process that has since exited can never be confirmed.
        PostLayout(worker, new LayoutQueueRequest { Mode = InstallationLayoutMode.GroupOnly, SourceProcessId = int.MaxValue - 7,
            Groups = new() { new LayoutQueueGroup { Id = "tools", Name = "Tools" } }, Mods = new() { new LayoutQueueMod { Guid = "team.foo", GroupId = "tools" } } });
    }
    string orphan = Task().Id;
    Check("S33 残留预览在下次启动前仍为就绪", Task().State == "ready" && Task().Request.SourceProcessId == int.MaxValue - 7, Task().State + " pid=" + Task().Request.SourceProcessId);
    using (var worker = new QueueWorker(root))
        Check("S33 发起进程已退出的预览在助手启动时关闭", Task().Id == orphan && Task().State == "cancelled", Task().State);
}

// S35: a folder/import task can be cleared from the queue. A ready preview is closed then marked; "clear finished"
// marks a finished one; a task that blocks writes stays; and the dismiss is not held behind an update waiting for exit.
{
    var (root, core) = NewRoot("s35"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string a = Installed(root, "Foo-Foo", "Foo", "1.0.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.0.0"), "Foo.dll");
    using var launch = System.Diagnostics.Process.GetCurrentProcess();
    string taskFile = Path.Combine(root, "IGMM", "shop", "task.json");
    ShopTask Task() => Files.Read<ShopTask>(taskFile);
    void Send(QueueWorker w, UpdateCommand command) { command.Id = Guid.NewGuid().ToString("N"); Files.Save(Path.Combine(root, "IGMM", "commands", command.Id + ".json"), command); w.ProcessCommands(); }
    LayoutQueueRequest Preview() => new() { Mode = InstallationLayoutMode.GroupOnly, SourceProcessId = launch.Id,
        Groups = new() { new LayoutQueueGroup { Id = "tools", Name = "Tools" } }, Mods = new() { new LayoutQueueMod { Guid = "team.foo", GroupId = "tools" } } };
    using (var worker = new QueueWorker(root))
    {
        store.Add("Team", "Foo", "1.1.0", PackageZip("Foo", "1.1.0", Fixtures.Plugin(core, "Foo", "team.foo", "Foo", "1.1.0")));
        var update = Job("Team", "Foo", a, "1.0.0", "1.1.0", "team.foo");
        Send(worker, new UpdateCommand { Action = "layout", Layout = Preview() });
        var preview = Task();
        Check("S35 预览就绪", preview.State == "ready" && !preview.Dismissed, preview.State + " " + preview.Error);
        Files.RunningProbe = _ => true;
        Post(root, worker, new UpdateCommand { Action = "add", Job = update, LoadedAssemblyPaths = new() { Path.Combine(a, "Foo.dll") } });
        var stopAt = DateTime.UtcNow.AddSeconds(30);
        worker.StopRequested = () => Result(worker, update).State == UpdateStates.WaitingExit || DateTime.UtcNow > stopAt;
        await worker.Run(true);
        worker.StopRequested = null;
        Check("S35 更新处于等待退出", Result(worker, update).State == UpdateStates.WaitingExit, Result(worker, update).State);
        Send(worker, new UpdateCommand { Action = "shop-dismiss", JobId = preview.Id });
        Check("S35 就绪预览清除＝先关闭再标记（有更新等待也立即生效）", Task().Id == preview.Id && Task().State == "cancelled" && Task().Dismissed && Directory.Exists(a), Task().State + " dismissed=" + Task().Dismissed);
        Check("S35 清除后命令收件箱清空", Directory.GetFiles(Path.Combine(root, "IGMM", "commands"), "*.json").Length == 0);
        Files.RunningProbe = _ => false;
        await Drive(worker);
        // A cancelled (stopped) preview: "clear finished" marks it as well.
        Send(worker, new UpdateCommand { Action = "layout", Layout = Preview() });
        var second = Task();
        Send(worker, new UpdateCommand { Action = "layout", Layout = new LayoutQueueRequest { Operation = "cancel", PreviewId = second.Id } });
        Check("S35 第二份预览已停止且未标记", Task().Id == second.Id && Task().State == "cancelled" && !Task().Dismissed, Task().State);
        Send(worker, new UpdateCommand { Action = "clear-finished" });
        Check("S35 清空会标记已结束的整理任务", Task().Id == second.Id && Task().Dismissed, "dismissed=" + Task().Dismissed);
        // A task that blocks writes is refused and left untouched.
        var running = Task(); running.Id = Guid.NewGuid().ToString("N"); running.State = "committing"; running.Dismissed = false; Files.Save(taskFile, running);
        Send(worker, new UpdateCommand { Action = "shop-dismiss", JobId = running.Id });
        Check("S35 执行中的任务不被清除", Task().State == "committing" && !Task().Dismissed, Task().State + " dismissed=" + Task().Dismissed);
        Send(worker, new UpdateCommand { Action = "clear-finished" });
        Check("S35 清空不碰执行中的任务", !Task().Dismissed);
        running.State = "cancelled"; Files.Save(taskFile, running);
    }
}

// S36 (2.2.13): a package whose DLL sits in a subfolder ("plugins/Nested.dll", like most store packages) is confirmed and
// moved: the old-path lookup kept only the file name and failed every such confirm. A category folder named differently
// from its group is recognized by its packages' groups; the manager's own folder moves, and moves last; a ".dll.old" copy
// beside the ".dll" no longer keeps the package in place.
{
    var (root, core) = NewRoot("s36");
    string nested = Path.Combine(root, "plugins", "Nested-Nested");
    Directory.CreateDirectory(Path.Combine(nested, "plugins"));
    File.WriteAllBytes(Path.Combine(nested, "plugins", "Nested.dll"), Fixtures.Plugin(core, "Nested", "team.nested", "Nested", "1.0.0"));
    File.WriteAllText(Path.Combine(nested, "manifest.json"), Fixtures.Manifest("Nested", "1.0.0"));
    string a = Installed(root, "0核心&基础模组/Alpha-Alpha", "Alpha", "1.0.0", Fixtures.Plugin(core, "Alpha", "team.alpha", "Alpha", "1.0.0"), "Alpha.dll");
    string b = Installed(root, "0核心&基础模组/Beta-Beta", "Beta", "1.0.0", Fixtures.Plugin(core, "Beta", "team.beta", "Beta", "1.0.0"), "Beta.dll");
    string manager = Installed(root, "Manager-MCE", "IGMM", "1.0.0", Fixtures.Plugin(core, "IGMM", "com.aiae.ingamemodmanager", "IGMM", "1.0.0"), "MCE.dll");
    string doubled = Installed(root, "Doubled-Doubled", "Doubled", "1.0.0", Fixtures.Plugin(core, "Doubled", "team.doubled", "Doubled", "1.0.0"), "Doubled.dll");
    File.Copy(Path.Combine(doubled, "Doubled.dll"), Path.Combine(doubled, "Doubled.dll.old"));
    using var worker = new QueueWorker(root);
    void PostLayout(LayoutQueueRequest request) { string id = Guid.NewGuid().ToString("N"); Files.Save(Path.Combine(root, "IGMM", "commands", id + ".json"), new UpdateCommand { Id = id, Action = "layout", Layout = request }); worker.ProcessCommands(); }
    PostLayout(new LayoutQueueRequest
    {
        Mode = InstallationLayoutMode.GroupOnly,
        Groups = new() { new LayoutQueueGroup { Id = "core", Name = "0基础模组" }, new LayoutQueueGroup { Id = "tools", Name = "Tools" } },
        Mods = new() { new LayoutQueueMod { Guid = "team.alpha", GroupId = "core" }, new LayoutQueueMod { Guid = "team.beta", GroupId = "core" },
            new LayoutQueueMod { Guid = "com.aiae.ingamemodmanager", GroupId = "core" }, new LayoutQueueMod { Guid = "team.nested", GroupId = "tools" }, new LayoutQueueMod { Guid = "team.doubled", GroupId = "tools" } }
    });
    var preview = Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json"));
    Check("S36 预览就绪无阻断", preview.State == "ready" && preview.Blockers.Count == 0, preview.State + " " + preview.Error + " " + string.Join(" | ", preview.Blockers));
    Check("S36 改名的分类目录按包的分组识别", preview.Details.Any(d => d.Contains("分组目录") && d.Contains("0核心&基础模组")) && preview.Tree.Any(n => n.Folder == "Alpha-Alpha" && n.Disposition == InstallationLayoutDisposition.Unchanged), string.Join(" | ", preview.Details));
    Check("S36 管理器目录参与整理并标记", preview.Tree.Any(n => n.Manager && n.Disposition == InstallationLayoutDisposition.Move && n.Group == "0核心&基础模组"), string.Join(" | ", preview.Tree.Select(n => n.Folder + ":" + n.Disposition + ":" + n.Reasons)));
    Check("S36 .dll 与 .dll.old 同目录不再跳过", preview.Tree.Any(n => n.Folder == "Doubled-Doubled" && n.Disposition == InstallationLayoutDisposition.Move && n.Notes.Contains("DuplicatePluginId")), string.Join(" | ", preview.Tree.Select(n => n.Folder + ":" + n.Disposition + ":" + n.Reasons)));
    var plan = LayoutPlanner.Read(Path.Combine(root, "IGMM", "shop", "tasks", preview.Id))!;
    Check("S36 管理器目录最后移动", plan.Moves.Count == 4 && plan.Moves.Skip(2).All(m => preview.Tree.Any(t => t.Manager && t.OriginalPath == m.From)) && plan.Moves.Take(2).All(m => preview.Tree.Any(t => !t.Manager && t.OriginalPath == m.From)), string.Join(" | ", plan.Moves.Select(m => m.From)));
    PostLayout(new LayoutQueueRequest { Operation = "confirm", PreviewId = preview.Id, PlanToken = preview.PlanToken });
    var job = worker.Current.Jobs.SingleOrDefault(j => j.Layout);
    Check("S36 子目录里有 DLL 的包可以确认（原先确认即失败）", job != null && File.Exists(Path.Combine(root, "IGMM", "shop", "task.json")) && Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json")).State == "completed", job == null ? "no job · " + Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json")).Error : job.State);
    if (job != null)
    {
        await Drive(worker);
        string tools = Path.Combine(root, "plugins", "Tools");
        Check("S36 整理后文件都在新位置", job.State == UpdateStates.PendingRestart && File.Exists(Path.Combine(tools, "Nested-Nested", "plugins", "Nested.dll")) && File.Exists(Path.Combine(tools, "Doubled-Doubled", "Doubled.dll.old"))
            && File.Exists(Path.Combine(root, "plugins", "0核心&基础模组", "Manager-MCE", "MCE.dll")) && File.Exists(Path.Combine(a, "Alpha.dll")) && !Directory.Exists(nested) && !Directory.Exists(manager), job.State + " " + job.Error);
        Post(root, worker, new UpdateCommand { Action = "rollback", JobId = job.Id }); await Drive(worker);
        Check("S36 回退后子目录文件回到原处", job.State == UpdateStates.RolledBack && File.Exists(Path.Combine(nested, "plugins", "Nested.dll")) && File.Exists(Path.Combine(manager, "MCE.dll")) && File.Exists(Path.Combine(doubled, "Doubled.dll.old")) && File.Exists(Path.Combine(b, "Beta.dll")), job.State + " " + job.Error);
    }
}

// S37 (2.3.0): the group-folder organizer through the worker. Nested groups become a folder chain, a category folder is
// renamed to its group's name and its packages ride along, a nested package is extracted, a lone wrapper is emptied,
// the packaging layout stays, folder names never change, and the whole job rolls back.
{
    var (root, core) = NewRoot("s37");
    string P(string rel) => Path.Combine(root, "plugins", rel.Replace('/', Path.DirectorySeparatorChar));
    void Package(string rel, string name, string guid, string dll, string inside = "")
    {
        Directory.CreateDirectory(Path.Combine(P(rel), inside));
        File.WriteAllBytes(Path.Combine(P(rel), inside, dll), Fixtures.Plugin(core, name, guid, name, "1.0.0"));
        File.WriteAllText(Path.Combine(P(rel), "manifest.json"), Fixtures.Manifest(name, "1.0.0"));
    }
    Package("0核心&基础模组/A-A", "A", "team.a", "A.dll");
    Package("0核心&基础模组/B-B", "B", "team.b", "B.dll", "plugins");
    Package("TC", "TC", "team.tc", "TC.dll");
    Package("TC/Snosz-UBImGui", "UBImGui", "team.ub", "UBImGui.dll");
    Package("Wrap/Inner-Inner", "Inner", "team.inner", "Inner.dll");
    Directory.CreateDirectory(P("empty-left"));
    using var worker = new QueueWorker(root);
    void PostLayout(LayoutQueueRequest request) { string id = Guid.NewGuid().ToString("N"); Files.Save(Path.Combine(root, "IGMM", "commands", id + ".json"), new UpdateCommand { Id = id, Action = "layout", Layout = request }); worker.ProcessCommands(); }
    var groups = new List<LayoutQueueGroup> { new() { Id = "core", Name = "0基础模组" }, new() { Id = "play", Name = "3玩法模组" }, new() { Id = "tc", Name = "地形", ParentId = "play" }, new() { Id = "lib", Name = "依赖模组" } };
    LayoutQueueRow R(string guid, string group, string rel) => new() { Guids = { guid }, GroupId = group, PackageRoot = P(rel), Loaded = false, Version = "1.0.0" };
    PostLayout(new LayoutQueueRequest { Groups = groups, UngroupedFolder = "未分组", Rows = new() {
        R("team.a", "core", "0核心&基础模组/A-A"), R("team.b", "core", "0核心&基础模组/B-B"), R("team.tc", "tc", "TC"), R("team.ub", "lib", "TC/Snosz-UBImGui"), R("team.inner", "", "Wrap/Inner-Inner") } });
    var preview = Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json"));
    Check("S37 预览就绪", preview.State == "ready" && preview.Blockers.Count == 0, preview.State + " " + preview.Error + " " + string.Join(" | ", preview.Blockers));
    var plan = LayoutPlanner.Read(Path.Combine(root, "IGMM", "shop", "tasks", preview.Id))!;
    string Moves() => string.Join(" | ", plan.Moves.Select(m => m.From + "→" + m.To));
    Check("S37 分类文件夹改名为分组名", plan.Moves.Any(m => m.From == "plugins/0核心&基础模组" && m.To == "plugins/0基础模组"), Moves());
    Check("S37 嵌套分组成为文件夹层级", plan.Moves.Any(m => m.From == "plugins/TC" && m.To == "plugins/3玩法模组/地形/TC"), Moves());
    Check("S37 嵌套模组拆出且在外层之前", plan.Moves.FindIndex(m => m.From == "plugins/TC/Snosz-UBImGui") >= 0 && plan.Moves.FindIndex(m => m.From == "plugins/TC/Snosz-UBImGui") < plan.Moves.FindIndex(m => m.From == "plugins/TC"), Moves());
    Check("S37 未分组移入未分组文件夹", plan.Moves.Any(m => m.From == "plugins/Wrap/Inner-Inner" && m.To == "plugins/未分组/Inner-Inner"), Moves());
    Check("S37 打包结构 plugins/ 不拆", !plan.Moves.Any(m => m.From.Contains("B-B/")), Moves());
    Check("S37 空文件夹与拆后空壳列入预览", preview.Tree.Any(n => n.Kind == LayoutNodeKind.Empty && n.OriginalPath == "plugins/empty-left") && preview.Tree.Any(n => n.Kind == LayoutNodeKind.Empty && n.OriginalPath == "plugins/Wrap"),
        string.Join(",", preview.Tree.Where(n => n.Kind == LayoutNodeKind.Empty).Select(n => n.OriginalPath)));
    PostLayout(new LayoutQueueRequest { Operation = "confirm", PreviewId = preview.Id, PlanToken = preview.PlanToken });
    var job = worker.Current.Jobs.SingleOrDefault(j => j.Layout);
    Check("S37 确认生成整理任务", job != null, Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json")).Error);
    if (job != null)
    {
        await Drive(worker);
        Check("S37 整理后文件都在新位置、文件夹名不变", job.State == UpdateStates.PendingRestart
            && File.Exists(P("0基础模组/A-A/A.dll")) && File.Exists(P("0基础模组/B-B/plugins/B.dll"))
            && File.Exists(P("3玩法模组/地形/TC/TC.dll")) && File.Exists(P("依赖模组/Snosz-UBImGui/UBImGui.dll"))
            && File.Exists(P("未分组/Inner-Inner/Inner.dll")) && !Directory.Exists(P("0核心&基础模组")) && !Directory.Exists(P("TC")), job.State + " " + job.Error);
        Check("S37 空文件夹不被删除", Directory.Exists(P("empty-left")) && Directory.Exists(P("Wrap")));
        Post(root, worker, new UpdateCommand { Action = "rollback", JobId = job.Id }); await Drive(worker);
        Check("S37 整理可完整回退", job.State == UpdateStates.RolledBack && File.Exists(P("0核心&基础模组/A-A/A.dll")) && File.Exists(P("TC/Snosz-UBImGui/UBImGui.dll"))
            && File.Exists(P("TC/TC.dll")) && File.Exists(P("Wrap/Inner-Inner/Inner.dll")) && !Directory.Exists(P("0基础模组")) && !Directory.Exists(P("3玩法模组")), job.State + " " + job.Error);
    }
}

// S38 (2.3.1): ticked empty folders are recycled only after every move succeeded, only when the preview listed them,
// and a folder that is no longer empty is skipped; a rollback keeps a group folder the user already had.
{
    var (root, core) = NewRoot("s38");
    string P(string rel) => Path.Combine(root, "plugins", rel.Replace('/', Path.DirectorySeparatorChar));
    void Package(string rel, string name, string guid)
    {
        Directory.CreateDirectory(P(rel));
        File.WriteAllBytes(Path.Combine(P(rel), name + ".dll"), Fixtures.Plugin(core, name, guid, name, "1.0.0"));
        File.WriteAllText(Path.Combine(P(rel), "manifest.json"), Fixtures.Manifest(name, "1.0.0"));
    }
    Package("A-A", "A", "team.a");
    Directory.CreateDirectory(P("3玩法模组"));                  // the user's own, already empty group folder
    Directory.CreateDirectory(P("empty-one")); Directory.CreateDirectory(P("empty-two")); Directory.CreateDirectory(P("refill"));
    var recycled = new List<string>();
    MCE.UpdateWorker.RecycleBin.SendOverride = path => { recycled.Add(Path.GetFileName(path)); Directory.Delete(path, true); };
    try
    {
        using var worker = new QueueWorker(root);
        void PostLayout(LayoutQueueRequest request) { string id = Guid.NewGuid().ToString("N"); Files.Save(Path.Combine(root, "IGMM", "commands", id + ".json"), new UpdateCommand { Id = id, Action = "layout", Layout = request }); worker.ProcessCommands(); }
        PostLayout(new LayoutQueueRequest { Groups = new() { new() { Id = "play", Name = "3玩法模组" } }, UngroupedFolder = "未分组",
            Rows = new() { new() { Guids = { "team.a" }, GroupId = "play", PackageRoot = P("A-A"), Version = "1.0.0" } } });
        var preview = Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json"));
        Check("S38 预览列出空文件夹，不含会被移入模组的分组文件夹", preview.Tree.Where(n => n.Kind == LayoutNodeKind.Empty).Select(n => n.OriginalPath).OrderBy(x => x).SequenceEqual(new[] { "plugins/empty-one", "plugins/empty-two", "plugins/refill" }),
            string.Join(",", preview.Tree.Where(n => n.Kind == LayoutNodeKind.Empty).Select(n => n.OriginalPath)));
        // empty-two is not ticked; plugins/A-A is not an empty folder and must be ignored even if a request names it.
        PostLayout(new LayoutQueueRequest { Operation = "confirm", PreviewId = preview.Id, PlanToken = preview.PlanToken, RecycleEmpty = new() { "plugins/empty-one", "plugins/refill", "plugins/A-A" } });
        File.WriteAllText(Path.Combine(P("refill"), "note.dll"), "x");   // became non-empty after the preview
        var job = worker.Current.Jobs.SingleOrDefault(j => j.Layout);
        Check("S38 确认生成整理任务", job != null, Files.Read<ShopTask>(Path.Combine(root, "IGMM", "shop", "task.json")).Error);
        Check("S38 移动之前不放回收站", recycled.Count == 0 && Directory.Exists(P("empty-one")));
        if (job != null)
        {
            await Drive(worker);
            Check("S38 整理完成后放进回收站：只放勾选且预览列出的", job.State == UpdateStates.PendingRestart && recycled.SequenceEqual(new[] { "empty-one" })
                && !Directory.Exists(P("empty-one")) && Directory.Exists(P("empty-two")) && File.Exists(P("3玩法模组/A-A/A.dll")), job.State + " " + job.Error + " recycled=" + string.Join(",", recycled));
            Check("S38 已经不空的跳过并提示，不算失败", Directory.Exists(P("refill")) && job.Warnings.Any(w => w.Contains("refill")), string.Join(" | ", job.Warnings));
            Post(root, worker, new UpdateCommand { Action = "rollback", JobId = job.Id }); await Drive(worker);
            Check("S38 回退：模组回原位", job.State == UpdateStates.RolledBack && File.Exists(P("A-A/A.dll")), job.State + " " + job.Error);
            Check("S38 回退：用户原有的空分组文件夹保留", Directory.Exists(P("3玩法模组")));
        }
    }
    finally { MCE.UpdateWorker.RecycleBin.SendOverride = null; }
}

// S39 (2.4.4): a lower-version manifest.json of the same package elsewhere (MonoDetour: core\Team-Name from a manual install;
// Profile_Cards: the package root above the nested plugins\ copy) is backed up and replaced by the new one, and rolled back with it.
{
    var (root, core) = NewRoot("s39"); var store = new FakeStore(); ShopQueue.TestHandler = store;
    string package = Installed(root, "Team-Bar", "Bar", "1.0.0", Fixtures.Plugin(core, "Bar", "team.bar", "Bar", "1.0.0"), "Bar.dll");
    string staleCore = Path.Combine(root, "core", "Team-Bar"); Directory.CreateDirectory(staleCore);
    File.WriteAllText(Path.Combine(staleCore, "manifest.json"), Fixtures.Manifest("Bar", "0.9.0"));
    string otherPackage = Path.Combine(root, "core", "Team-Other"); Directory.CreateDirectory(otherPackage);
    File.WriteAllText(Path.Combine(otherPackage, "manifest.json"), Fixtures.Manifest("Other", "0.1.0"));
    string staleOriginal = Hash(Path.Combine(staleCore, "manifest.json")), otherOriginal = Hash(Path.Combine(otherPackage, "manifest.json"));
    store.Add("Team", "Bar", "1.1.0", PackageZip("Bar", "1.1.0", Fixtures.Plugin(core, "Bar", "team.bar", "Bar", "1.1.0")));
    using var worker = new QueueWorker(root);
    var job = Job("Team", "Bar", package, "1.0.0", "1.1.0", "team.bar");
    Post(root, worker, new UpdateCommand { Action = "add", Job = job });
    await Drive(worker);
    var result = Result(worker, job);
    string Version(string file) => ManifestCopies.Read(file, File.ReadAllText)?.Version ?? "?";
    Check("S39 更新成功", result.State == UpdateStates.PendingRestart || result.State == UpdateStates.Completed, result.State + " " + result.Error);
    Check("S39 包内说明文件为新版", Version(Path.Combine(package, "manifest.json")) == "1.1.0", Version(Path.Combine(package, "manifest.json")));
    Check("S39 core 下同包旧说明文件一并换成新版", Version(Path.Combine(staleCore, "manifest.json")) == "1.1.0", Version(Path.Combine(staleCore, "manifest.json")));
    Check("S39 别的包的说明文件不动", Hash(Path.Combine(otherPackage, "manifest.json")) == otherOriginal);
    Check("S39 有替换提示", result.Warnings.Any(w => w.Contains("同包旧说明文件")), string.Join(" | ", result.Warnings));
    Check("S39 旧说明文件进了备份", Directory.Exists(result.BackupDirectory) && Directory.GetFiles(result.BackupDirectory, "*manifest.json", SearchOption.AllDirectories).Any(f => Hash(f) == staleOriginal));
    Post(root, worker, new UpdateCommand { Action = "rollback", JobId = job.Id });
    await Drive(worker);
    result = Result(worker, job);
    Check("S39 回退：core 下旧说明文件恢复原样", result.State == UpdateStates.RolledBack && Hash(Path.Combine(staleCore, "manifest.json")) == staleOriginal, result.State + " " + result.Error);
}

// M1 (2.4.4): ManifestCopies pure rules — highest wins, lower copies are stale, unparsable copies are ignored; reconciliation guard.
{
    var copies = new List<ManifestCopies.Copy> {
        new() { Key = "MonoDetour-MonoDetour", Version = "0.7.16", ManifestPath = "core" },
        new() { Key = "MonoDetour-MonoDetour", Version = "0.7.1701", ManifestPath = "plugins" },
        new() { Key = "AtomicStudio-Profile_Cards", Version = "0.1.5", ManifestPath = "root" },
        new() { Key = "AtomicStudio-Profile_Cards", Version = "0.1.7", ManifestPath = "nested" },
        new() { Key = "Hamunii-AutoHookGenPatcher", Version = "1.1.1", ManifestPath = "a" },
        new() { Key = "Hamunii-AutoHookGenPatcher", Version = "1.1.1", ManifestPath = "b" },
        new() { Key = "Bad-Bad", Version = "x", ManifestPath = "bad" } };
    var verdicts = ManifestCopies.Resolve(copies).ToDictionary(v => v.Key);
    Check("M1 MonoDetour 取 0.7.1701（数字段 1701 > 16）", verdicts["MonoDetour-MonoDetour"].Version == "0.7.1701" && verdicts["MonoDetour-MonoDetour"].Stale.Single().ManifestPath == "core");
    Check("M1 Profile_Cards 取嵌套的 0.1.7、包根 0.1.5 是残留", verdicts["AtomicStudio-Profile_Cards"].Version == "0.1.7" && verdicts["AtomicStudio-Profile_Cards"].Stale.Single().ManifestPath == "root");
    Check("M1 版本一致的多份不算残留", !verdicts["Hamunii-AutoHookGenPatcher"].HasStale);
    Check("M1 版本解析不了的副本不参与", !verdicts.ContainsKey("Bad-Bad"));
    Check("M1 对账：已更新到 0.7.1701、扫描仍 0.7.16、最新 0.7.1701 → 不一致", ManifestCopies.Contradicts("0.7.16", "0.7.1701", "0.7.1701"));
    Check("M1 对账：商店又出了 0.7.1800 → 正常提示更新", !ManifestCopies.Contradicts("0.7.16", "0.7.1800", "0.7.1701"));
    Check("M1 对账：没有完成记录 → 正常提示更新", !ManifestCopies.Contradicts("0.7.16", "0.7.1701", ""));
    Check("M1 对账：扫描版本已等于完成版本 → 不算不一致", !ManifestCopies.Contradicts("0.7.1701", "0.7.1701", "0.7.1701"));
    string dir = Path.Combine(home, "m1", "Team-Name"); Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "manifest.json"), "{\"name\":\"Name\",\"version_number\":\"1.2.3\"}");
    var read = ManifestCopies.Read(Path.Combine(dir, "manifest.json"), File.ReadAllText);
    Check("M1 无 namespace 时由 Team-Name 文件夹补全包键", read != null && read.Key == "Team-Name" && read.Version == "1.2.3", read?.Key + " " + read?.Version);
}

Console.WriteLine($"=== {passed} passed, {failed} failed ===");
if (failed == 0) { try { Directory.Delete(home, true); } catch { } }
return failed == 0 ? 0 : 1;
