using System.Text.Json;
using System.Text.Json.Nodes;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal sealed class PackSwitchPlan
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Phase { get; set; } = "prepared";
    public string CreatedUtc { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string TargetName { get; set; } = "";
    public PackIndex Before { get; set; } = new();
    public PackIndex After { get; set; } = new();
    public List<PackFile> Originals { get; set; } = new();
    public List<PackFile> Desired { get; set; } = new();
    public List<string> Removed { get; set; } = new();
    public List<string> Placed { get; set; } = new();
    public string NewContent { get; set; } = "";
    public string Backup { get; set; } = "";
    public int SourceProcessId { get; set; }
    public bool Adopting { get; set; }
    public int EnvironmentVersion { get; set; } = 1;
    public List<PackLedgerChange> Ledgers { get; set; } = new();
}

internal static class PackTransactions
{
    internal static Action<string>? StepProbe = null; // isolated checks only
    static string Canonical(string path) => PluginFileNames.OriginalPath(path);
    static bool Host(PackIndex index, string path) => index.HostRoots.Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
    static bool Running(int pid) => Files.GameRunning(new UpdateJob { SourceProcessId = pid });
    static void Stopped(int pid) { if (Running(pid)) throw new IOException("检测到游戏重新启动，停止文件提交，退出后恢复旧环境。"); }
    internal static List<PackFile> Live(PackStorage storage, PackIndex index) => Live(storage, index, 1);
    internal static List<PackFile> Live(PackStorage storage, PackIndex index, int environmentVersion)
    {
        if (environmentVersion is not (1 or 2)) throw new IOException("未知环境版本。");
        var result = new List<PackFile>();
        foreach (string route in environmentVersion == 2 ? new[] { "core", "plugins", "patchers", "config" } : new[] { "plugins", "patchers", "config" })
        {
            string start = Path.Combine(storage.Root, route); if (!Directory.Exists(start)) continue;
            var pending = new Stack<string>(); pending.Push(start);
            while (pending.TryPop(out var directory))
            {
                Files.RejectLinks(storage.Root, directory);
                foreach (string file in Directory.GetFiles(directory))
                {
                    string path = Path.GetRelativePath(storage.Root, file).Replace('\\', '/');
                    if (!PackStorage.ManagedPath(path, environmentVersion) || environmentVersion == 1 && Host(index, path)) continue;
                    Files.RejectLinks(storage.Root, file); result.Add(new PackFile { Path = path, Hash = Files.Hash(file) });
                }
                foreach (string child in Directory.GetDirectories(directory))
                {
                    string path = Path.GetRelativePath(storage.Root, child).Replace('\\', '/');
                    if (environmentVersion == 1 && Host(index, path) || route == "config" && (environmentVersion == 1 ? PackStorage.GlobalConfig(path[7..] + "/") : PackEnvironmentPolicy.ManagementConfig(path[7..]))) continue;
                    pending.Push(child);
                }
            }
        }
        return result;
    }
    internal static PackTask Preview(PackStorage storage, PackRequest request)
    {
        var index = storage.Read();
        bool adopting = request.Operation is "preview-adopt" or "preview-adopt-environment";
        bool upgrading = request.Operation == "preview-upgrade-environment";
        var active = index.Packs.FirstOrDefault(p => p.Id == index.ActivePackId);
        int environmentVersion = adopting ? (request.Operation == "preview-adopt-environment" ? 2 : 1) : active?.EnvironmentVersion ?? 1;
        if (upgrading)
        {
            if (active == null || request.PackId != active.Id || PackEnvironmentPolicy.IsComplete(active)) throw new IOException("仅能明确升级当前启用的旧环境；非当前旧包不能从 live 补齐。");
            environmentVersion = 2;
        }
        // Saving another named snapshot is also allowed after the first save.
        if (!adopting && index.ActivePackId.Length == 0) throw new IOException("当前配置尚未保存为临时整合包。请先点击“保存当前配置为临时整合包”。");
        if (adopting)
        {
            index.HostPlugins = request.HostPlugins; index.HostRoots = request.HostRoots;
            if (environmentVersion == 1) ValidateHosts(storage, index);
        }
        PackRecord proposed = adopting ? new PackRecord { Id = Guid.NewGuid().ToString("N"), Name = SnapshotName(request.SnapshotName), ModifiedUtc = DateTime.UtcNow.ToString("O") }
            : index.Packs.SingleOrDefault(p => p.Id == request.PackId) ?? throw new IOException("库存中不存在所选整合包。");
        if (upgrading && (proposed.Content != active!.Content || proposed.PendingUpdate != null || proposed.UnsavedEnvironment != null)) throw new IOException("升级前请先处理当前旧包的待更新或未保存环境，不能覆盖它们。");
        if (adopting) { proposed.EnvironmentVersion = environmentVersion; proposed.MinimumWorkerProtocol = environmentVersion; }
        if (!adopting && !upgrading)
        {
            if (proposed.Id == index.ActivePackId && proposed.PendingUpdate == null) throw new IOException("此包已经启用。");
            proposed = proposed.UnsavedEnvironment ?? proposed.PendingUpdate ?? proposed;
            storage.Validate(proposed);
            if (environmentVersion == 2 || proposed.EnvironmentVersion == 2)
            {
                if (environmentVersion != 2) throw new IOException("当前旧环境必须先明确 preview-upgrade-environment / confirm-upgrade-environment，不能在切换时自动升级。");
                PackCompleteEnvironment.RequireComplete(storage, proposed);
            }
            else CheckRequirements(storage, proposed, request.HostPlugins);
        }
        var live = Live(storage, index, environmentVersion);
        // Snapshot language choice: offered from the live personal and author dictionaries; the player's current selection is preselected.
        var previewLanguages = adopting ? ScanLanguageDirectories(LiveLanguageRoots(storage, live)) : new List<PackLanguage>();
        if (adopting) proposed.Language = ChooseLanguage(previewLanguages, SelectedLanguage(storage), request.GameLanguage);
        var known = index.ActiveFiles.Select(f => Canonical(f.Path)).ToHashSet(Files.Compare);
        var unknown = live.Where(f => !known.Contains(Canonical(f.Path)) && !f.Path.StartsWith("config/", StringComparison.OrdinalIgnoreCase)).ToList();
        // Every live managed file belongs to the active environment, even before explicitly saving it.
        var task = new PackTask { Id = Guid.NewGuid().ToString("N"), Request = request, Proposed = proposed, State = "preview" };
        // First save explicitly includes the whole current setup. Ownership review is only meaningful for later switches.
        task.UnknownConfigurations = new();
        var previewFiles = adopting || upgrading ? live : proposed.Files;
        task.Report = new PackReport {
            Verdict = "ready", Name = proposed.Name, Version = proposed.Version, FileCount = previewFiles.Count,
            Kind = adopting ? "snapshot" : "switch", Languages = previewLanguages, GameLanguage = request.GameLanguage,
            KindText = adopting ? "类型：临时整合包。保存当前模组、启动补丁、配置、个人方案和语言文件；不会移动或替换当前文件，也不会自动删除。" : "类型：切换预览。退出游戏后先保存当前整合包的最终配置，再启用所选整合包。",
            Headline = adopting ? (environmentVersion == 2 ? "确认后等待游戏退出，保存完整 core、宿主、运行配置与组件状态。" : "确认后立即保存当前快照，无需退出游戏。切换整合包时才会等待退出并保存最终配置。") : "确认后等待游戏退出，再执行切换。当前整合包的文件和配置会保留，以便恢复。",
            Files = previewFiles.Select(f => new PackReportFile { Path = f.Path, Group = f.Path.StartsWith("config/", StringComparison.OrdinalIgnoreCase) ? "配置文件" : f.Path.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase) ? "启动补丁" : "模组文件" }).ToList()
        };
        task.Details.Add(adopting ? "将当前的插件、启动补丁和配置记录为一个整合包并备份，文件位置保持不变。" : "游戏退出后，保存旧包最后的插件版本和配置，再启用：" + proposed.Name);
        task.Details.Add("当前文件：" + live.Count + "；目标文件：" + proposed.Files.Count + "；固定宿主目录：" + index.HostRoots.Count);
        task.Details.AddRange(live.Where(f => !f.Path.StartsWith("config/", StringComparison.OrdinalIgnoreCase)).Select(f => "当前：" + f.Path));
        task.Details.Add("当前包新增文件：" + unknown.Count + "；均属于当前运行环境，可保存后切换或直接切换并保留未保存状态。");
        if (!adopting && index.Packs.First(p => p.Id == proposed.Id).UnsavedEnvironment != null) task.Details.Add("目标包有未保存运行环境，切回时自动恢复，仍保持未保存状态。");
        task.Details.AddRange(proposed.Files.Select(f => "启用：" + f.Path));
        if (upgrading)
        {
            task.Report.Kind = "upgrade-environment"; task.Report.FileCount = live.Count;
            task.Report.Headline = "明确升级当前旧环境：退出游戏后捕获完整 core、宿主、plugins、patchers、运行配置及当前 ledger；旧 revision 保留。";
            task.Details.Add(task.Report.Headline);
        }
        Files.Save(storage.TaskPath, task); return task;
    }
    // Snapshot-only operation: copies files into a new revision, never replaces installed files.
    // The live game can keep running. A changed source aborts the snapshot before the index is committed.
    internal static void SaveTemporary(PackStorage storage, PackTask task)
    {
        var index = storage.Read();
        bool first = index.ActivePackId.Length == 0;
        var context = JsonSerializer.Deserialize<PackIndex>(JsonSerializer.Serialize(index, Files.Json), Files.Json)!;
        if (first) { context.HostRoots = task.Request.HostRoots; context.HostPlugins = task.Request.HostPlugins; }
        int environmentVersion = task.Proposed.EnvironmentVersion;
        if (environmentVersion == 1) ValidateHosts(storage, context);
        string? ledgerBefore = environmentVersion == 2 ? PackCompleteEnvironment.ReadLedgerText(storage, index.ActivePackId) : null;
        var ledger = environmentVersion == 2 ? PackCompleteEnvironment.CaptureLedger(storage, index.ActivePackId, !first && index.Packs.Single(p => p.Id == index.ActivePackId).EnvironmentVersion == 2) : null;
        var files = Live(storage, context, environmentVersion);
        var saved = new PackRecord { Id = task.Proposed.Id, Name = SnapshotName(task.Request.SnapshotName), Content = "revisions/" + task.Id, ModifiedUtc = DateTime.UtcNow.ToString("O"), Files = files, Report = task.Report };
        saved.EnvironmentVersion = environmentVersion; saved.MinimumWorkerProtocol = environmentVersion;
        string destination = storage.Content(saved);
        if (Directory.Exists(destination)) throw new IOException("临时整合包保存位置已存在，请重新预览。");
        foreach (var file in files)
        {
            string source = Files.Child(storage.Root, file.Path), target = Files.Child(destination, file.Path);
            if (Files.Hash(source) != file.Hash) throw new IOException("保存期间文件发生变化，请重试：" + file.Path);
            Files.CopyAtomic(source, target);
            if (Files.Hash(target) != file.Hash) throw new IOException("配置正在写入，请稍后重试：" + file.Path);
        }
        var afterCopy = Live(storage, context, environmentVersion);
        if (!files.Select(f => f.Path + "|" + f.Hash).ToHashSet(Files.Compare).SetEquals(afterCopy.Select(f => f.Path + "|" + f.Hash))) throw new IOException("保存期间配置发生变化，请稍后重试。");
        if (first) MigrateWorkspace(storage, saved.Id);
        else PackStorage.CopyTree(storage.Workspace(index.ActivePackId), storage.Workspace(saved.Id));
        // Languages live in the snapshot's own workspace, never inside signed author data or the plugin inventory.
        CaptureLanguages(storage, saved.Id);
        var languages = ScanLanguageDirectories(LanguageRoots(storage, saved));
        saved.Language = languages.FirstOrDefault(l => Files.Compare.Equals(l.Key, task.Proposed.Language))?.Key
            ?? ChooseLanguage(languages, SelectedLanguage(storage), task.Request.GameLanguage);
        if (ledger != null)
        {
            if (ledgerBefore != PackCompleteEnvironment.ReadLedgerText(storage, index.ActivePackId)) throw new IOException("保存期间商店归属记录发生变化，请重试；未抹除 ledger。");
            WriteRuntimeState(storage, saved, ledger);
            PackCompleteEnvironment.RequireComplete(storage, saved);
            Files.Save(PackCompleteEnvironment.LedgerPath(storage, saved.Id), ledger);
        }
        context.Packs.Add(saved);
        if (first) { context.ActivePackId = saved.Id; context.ActiveFiles = files; }
        saved.Report = new PackReport { Verdict = "ready", Kind = "snapshot", Name = saved.Name, FileCount = files.Count, Headline = "已保存的临时整合包，仅查看内容不会改动游戏。", KindText = "类型：当前配置快照。", Languages = languages, GameLanguage = task.Request.GameLanguage, Files = files.Select(f => new PackReportFile { Path = f.Path, Group = f.Path.StartsWith("config/") ? "配置文件" : "模组文件" }).ToList() };
        storage.Save(context); task.Proposed = saved;
    }
    // ---- Snapshot languages ----
    // Layout: <pack>/workspace/language/*.txt holds the personal dictionaries captured at save time (top level only, like config/ModConfigEnhance/language).
    // Author dictionaries of an installed publication already travel as plugins/**/MCEPack/language inside the revision and are discovered from the file list.
    // Nothing is added to the plugin inventory or to signed author data, so signatures and plugin discovery are unaffected.
    static readonly string[] AuthorLanguages = { "/" + ManagerIdentity.PackFolder + "/language/", "/" + ManagerIdentity.LegacyPackFolder + "/language/" };
    internal static List<string> LanguageRoots(PackStorage storage, PackRecord pack)
    {
        var roots = new List<string>(); string content = storage.Content(pack);
        void Add(string path) { if (Directory.Exists(path) && !roots.Contains(path, Files.Compare)) roots.Add(path); }
        try { if (pack.AuthorRoot.Length > 0) Add(Files.Child(content, pack.AuthorRoot + "/language")); } catch (IOException) { }
        foreach (var file in pack.Files)
        {
            string path = file.Path.Replace((char)92, '/');
            foreach (string folder in AuthorLanguages)
            {
                int at = path.IndexOf(folder, StringComparison.OrdinalIgnoreCase);
                if (at < 0 || !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || path.IndexOf('/', at + folder.Length) >= 0) continue;
                try { Add(Files.Child(content, path[..(at + folder.Length - 1)])); } catch (IOException) { }
                break;
            }
        }
        Add(Path.Combine(storage.Workspace(pack.Id), "language"));
        return roots;
    }
    static List<string> LiveLanguageRoots(PackStorage storage, IEnumerable<PackFile> live)
    {
        var roots = new List<string>();
        foreach (var file in live)
        {
            string path = file.Path; string? folder = AuthorLanguages.FirstOrDefault(f => path.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
            int at = folder == null ? -1 : path.IndexOf(folder, StringComparison.OrdinalIgnoreCase);
            if (folder == null || at < 0 || !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || path.IndexOf('/', at + folder.Length) >= 0) continue;
            string directory = Files.Child(storage.Root, path[..(at + folder.Length - 1)]);
            if (!roots.Contains(directory, Files.Compare)) roots.Add(directory);
        }
        roots.Add(MceDirectories.Language(MceDirectories.RootFromBepInEx(storage.Root)));
        return roots;
    }
    /// <summary>Groups every directory with the shared PackLanguages rules, merging same-named groups. Also used by set-language.</summary>
    internal static List<PackLanguage> ScanLanguages(PackStorage storage, PackRecord pack) => ScanLanguageDirectories(LanguageRoots(storage, pack));
    internal static List<PackLanguage> ScanLanguageDirectories(IEnumerable<string> directories)
    {
        var merged = new Dictionary<string, PackLanguage>(Files.Compare);
        foreach (string directory in directories)
            foreach (var group in PackLanguages.Scan(directory))
            {
                if (!merged.TryGetValue(group.Key, out var target)) merged[group.Key] = target = new PackLanguage { Key = group.Key, Code = group.Code };
                foreach (string name in group.Files) if (!target.Files.Contains(name, Files.Compare)) target.Files.Add(name);
                if (target.Name.Length == 0) target.Name = group.Name;
            }
        return merged.Values.OrderBy(g => g.Key, Files.Compare).ToList();
    }
    static string ChooseLanguage(IReadOnlyList<PackLanguage> languages, string selection, string gameLanguage)
    {
        if (languages.Count == 0) return "";
        string wanted = selection.Trim().ToLowerInvariant(); wanted = wanted == "zh" ? "zh-cn" : wanted == "en" ? "en-us" : wanted;
        return languages.FirstOrDefault(l => wanted.Length > 0 && Files.Compare.Equals(l.Key, wanted))?.Key
            ?? PackLanguages.Choose(languages, gameLanguage.Length > 0 ? gameLanguage : "zh-cn")?.Key ?? "";
    }
    /// <summary>The "Language file" choice MCE keeps in its own settings; empty means "follow the game language".</summary>
    static string SelectedLanguage(PackStorage storage)
    {
        string file = Path.Combine(storage.Root, "config", ManagerIdentity.ConfigFileName);
        try
        {
            using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(input); bool state = false; string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.StartsWith('[')) { state = line.Equals("[3. State]", StringComparison.OrdinalIgnoreCase); continue; }
                int equals = line.IndexOf('=');
                if (state && equals > 0 && line[..equals].Trim().Equals("Language file", StringComparison.OrdinalIgnoreCase)) return line[(equals + 1)..].Trim();
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return "";
    }
    static void CaptureLanguages(PackStorage storage, string id)
    {
        string target = Path.Combine(storage.Workspace(id), "language");
        // A workspace copied from the previous snapshot must not keep dictionaries the player has since removed.
        if (Directory.Exists(target)) { Files.RejectLinks(storage.Home, target); Directory.Delete(target, true); }
        string source = MceDirectories.Language(MceDirectories.RootFromBepInEx(storage.Root));
        if (!Directory.Exists(source)) return;
        Files.RejectLinks(storage.Root, source); int copied = 0;
        foreach (string file in Directory.GetFiles(source, "*.txt").OrderBy(f => f, Files.Compare))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith('_') || new FileInfo(file).Length > 16L * 1024 * 1024 || copied >= 200) continue;
            Files.RejectLinks(storage.Root, file); Files.CopyAtomic(file, Path.Combine(target, name)); copied++;
        }
    }
    static string SnapshotName(string name)
    {
        string value = name.Trim();
        if (value.Length == 0) return "临时整合包";
        if (value.Length > 80 || value.Any(char.IsControl)) throw new IOException("临时整合包名称须为 1–80 个字符，且不能包含换行或控制字符。");
        return value;
    }
    internal static void ValidateHosts(PackStorage storage, PackIndex index)
    {
        if (index.HostRoots.Count == 0 || !index.HostPlugins.Any(p => ManagerIdentity.IsManagerGuid(p.Guid))) throw new IOException("缺少 MCE 宿主及依赖归属，无法接管。");
        foreach (string root in index.HostRoots)
        {
            if (!root.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) || root.TrimEnd('/') == "plugins") throw new IOException("宿主必须位于独立插件目录。");
            Files.RejectLinks(storage.Root, Files.Child(storage.Root, root));
        }
    }
    static void CheckRequirements(PackStorage storage, PackRecord pack, IReadOnlyList<PackHostPlugin> hosts)
    {
        foreach (var requirement in pack.Requirements)
        {
            // 1.0.0 拆分：旧整合包固定的宿主是旧 GUID（2.x 版本号）；新管理器接替它，版本号从 1.0.0 重新起算，按「已满足」处理
            if (ManagerIdentity.IsManagerGuid(requirement.Guid) && hosts.Any(p => ManagerIdentity.IsManagerGuid(p.Guid))) continue;
            var actual = hosts.FirstOrDefault(p => Files.Compare.Equals(p.Guid, requirement.Guid));
            if (actual == null || !Version.TryParse(actual.Version, out var version) || !Version.TryParse(requirement.Version, out var minimum) || version < minimum)
                throw new IOException("固定宿主版本不满足：" + requirement.Guid + " >= " + requirement.Version);
        }
        foreach (var requirement in pack.CoreRequirements)
        {
            string actual = Files.Child(Files.GameRootFor(storage.Root), requirement.Path);
            if (!File.Exists(actual) || Files.Hash(actual) != requirement.Hash) throw new IOException("核心兼容条件已改变：" + requirement.Path);
        }
    }
    internal static PackSwitchPlan Prepare(PackStorage storage, PackTask task)
    {
        Stopped(task.Request.SourceProcessId);
        var before = storage.Read();
        var after = JsonSerializer.Deserialize<PackIndex>(JsonSerializer.Serialize(before, Files.Json), Files.Json)!;
        bool upgrading = task.Request.Operation == "confirm-upgrade-environment";
        bool adopting = task.Request.Operation is "confirm-adopt" or "confirm-adopt-environment";
        var source = before.Packs.FirstOrDefault(p => p.Id == before.ActivePackId);
        int environmentVersion = adopting ? task.Proposed.EnvironmentVersion : upgrading ? 2 : source?.EnvironmentVersion ?? 1;
        if (upgrading && (source == null || source.EnvironmentVersion != 1 || task.Proposed.Id != source.Id || task.Proposed.Content != source.Content)) throw new IOException("旧环境升级预览已失效。");
        if (adopting) { after.HostRoots = task.Request.HostRoots; after.HostPlugins = task.Request.HostPlugins; if (environmentVersion == 1) ValidateHosts(storage, after); }
        else if (before.ActivePackId.Length == 0) throw new IOException("当前配置尚未保存为临时整合包，无法切换。");
        if (!adopting && !upgrading)
        {
            var target = after.Packs.Single(p => p.Id == task.Proposed.Id);
            target = target.UnsavedEnvironment ?? target.PendingUpdate ?? target;
            if (environmentVersion == 2 || target.EnvironmentVersion == 2)
            {
                if (environmentVersion != 2) throw new IOException("请先显式升级当前旧环境，再切换完整包。");
                PackCompleteEnvironment.RequireComplete(storage, target);
            }
        }
        var live = Live(storage, after, environmentVersion);
        string? ledgerBefore = environmentVersion == 2 ? PackCompleteEnvironment.ReadLedgerText(storage, before.ActivePackId) : null;
        var runtimeLedger = environmentVersion == 2 ? PackCompleteEnvironment.CaptureLedger(storage, before.ActivePackId, source?.EnvironmentVersion == 2) : null;
        // Final exit-time inventory is owned by the active pack, including files created since preview.
        var plan = new PackSwitchPlan { Id = task.Id, Before = before, After = after, Originals = live,
            CreatedUtc = DateTime.UtcNow.ToString("O"), SourceName = before.Packs.FirstOrDefault(p => p.Id == before.ActivePackId)?.Name ?? "当前配置", TargetName = task.Proposed.Name,
            SourceProcessId = task.Request.SourceProcessId, Adopting = adopting || upgrading, EnvironmentVersion = environmentVersion, SchemaVersion = environmentVersion == 2 ? 2 : 1,
            Backup = Path.Combine(storage.Home, ".transactions", task.Id, "backup") };
        PackRecord old;
        if (adopting) { old = task.Proposed; after.Packs.Add(old); after.ActivePackId = old.Id; }
        else old = after.Packs.Single(p => p.Id == before.ActivePackId);
        var runtime = JsonSerializer.Deserialize<PackRecord>(JsonSerializer.Serialize(old, Files.Json), Files.Json)!;
        runtime.PendingUpdate = null; runtime.UnsavedEnvironment = null; runtime.Report = null;
        runtime.EnvironmentVersion = environmentVersion; runtime.MinimumWorkerProtocol = environmentVersion; runtime.RevisionStateHash = "";
        runtime.Content = "revisions/" + task.Id;
        if (Directory.Exists(storage.Content(runtime))) throw new IOException("该准备尝试的 revision 已存在，请重新预览；保留中断时的快照。");
        runtime.ModifiedUtc = DateTime.UtcNow.ToString("O"); runtime.Files = live;
        if (adopting || upgrading || task.Request.SaveCurrentBeforeSwitch)
        {
            old.Content = runtime.Content; old.ModifiedUtc = runtime.ModifiedUtc; old.Files = live; old.Report = null; old.UnsavedEnvironment = null;
        }
        else old.UnsavedEnvironment = runtime;
        string snapshot = storage.Content(runtime);
        foreach (var file in live)
        {
            Stopped(plan.SourceProcessId);
            string actual = Files.Child(storage.Root, file.Path);
            if (!File.Exists(actual) || Files.Hash(actual) != file.Hash) throw new IOException("游戏退出后文件仍在改变，请稍后重试：" + file.Path);
            string saved = Files.Child(snapshot, file.Path); Files.CopyAtomic(actual, saved);
            string backup = Files.Child(plan.Backup, file.Path); Files.CopyAtomic(actual, backup);
        }
        if (runtimeLedger != null)
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
            if (adopting) MigrateWorkspace(storage, old.Id, environmentVersion == 1);
            CaptureLanguages(storage, old.Id);
            var languages = ScanLanguages(storage, old);
            if (!languages.Any(l => Files.Compare.Equals(l.Key, old.Language))) old.Language = ChooseLanguage(languages, SelectedLanguage(storage), task.Request.GameLanguage);
            old.Report ??= new PackReport { Verdict = "ready", Kind = "snapshot", Name = old.Name, FileCount = live.Count, KindText = "类型：当前配置快照。", Languages = languages, GameLanguage = task.Request.GameLanguage }; // marks a user snapshot, not an imported modpack
            plan.Desired = live; after.ActiveFiles = live;
        }
        else
        {
            var storedNext = after.Packs.Single(p => p.Id == task.Proposed.Id);
            var next = storedNext.UnsavedEnvironment ?? task.Proposed; storage.Validate(next);
            if (environmentVersion == 2)
            {
                PackCompleteEnvironment.RequireComplete(storage, next);
                var targetLedger = new PackRevisionState(storage).Read(next)!;
                plan.Ledgers.Add(new PackLedgerChange { PackId = next.Id, Before = PackCompleteEnvironment.ReadLedgerText(storage, next.Id), After = JsonSerializer.Serialize(targetLedger, Files.Json) });
            }
            else
            {
                CheckRequirements(storage, next, task.Request.HostPlugins);
                foreach (var file in next.Files) if (Host(after, file.Path)) throw new IOException("目标包试图覆盖固定宿主：" + file.Path);
            }
            plan.NewContent = storage.Content(next); plan.Desired = next.Files;
            if (storedNext.UnsavedEnvironment == null) { after.Packs.RemoveAll(p => p.Id == next.Id); after.Packs.Add(next); }
            after.ActivePackId = next.Id; after.ActiveFiles = next.Files; after.HostPlugins = task.Request.HostPlugins;
        }
        Stopped(plan.SourceProcessId); Files.Save(PlanPath(storage, task.Id), plan); StepProbe?.Invoke("prepared"); return plan;
    }
    static void WriteRuntimeState(PackStorage storage, PackRecord runtime, ShopLedger ledger)
    {
        try { new PackRevisionState(storage).Write(runtime, ledger); }
        catch (IOException error) { throw new IOException("无法捕获完整环境：商店归属证据与当前文件不一致。请明确修复或放弃对应组件归属后重试；不会自动抹除 ledger。" + error.Message, error); }
    }
    static void MigrateWorkspace(PackStorage storage, string id, bool copyLedger = true)
    {
        string old = Path.Combine(storage.Root, "config", ManagerIdentity.ConfigDirectory), workspace = storage.Workspace(id);
        PackStorage.CopyTree(Path.Combine(old, "profiles"), Path.Combine(workspace, "profiles"));
        string shopLedger=ManagerIdentity.Runtime(storage.Root,"shop","installed-unmanaged.json");
        if(copyLedger && File.Exists(shopLedger))Files.CopyAtomic(shopLedger,Path.Combine(workspace,"shop-installed.json"));
        foreach (string name in new[] { "applied-profile.json", "pending-profile.json" })
        {
            string source = Path.Combine(MceDirectories.Runtime(old), name);
            if (File.Exists(source)) Files.CopyAtomic(source, Path.Combine(workspace, "runtime", name));
        }
        foreach (string file in Directory.Exists(workspace) ? Directory.GetFiles(workspace, "*.json", SearchOption.AllDirectories) : Array.Empty<string>())
        {
            JsonNode? data = JsonNode.Parse(File.ReadAllText(file)); if (data == null) continue;
            void Rewrite(JsonNode node)
            {
                if (node is JsonObject obj) foreach (var pair in obj.ToArray())
                {
                    if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && text.StartsWith(Path.Combine(old, "profiles") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        obj[pair.Key] = Path.Combine(workspace, "profiles", Path.GetRelativePath(Path.Combine(old, "profiles"), text));
                    else if (pair.Value != null) Rewrite(pair.Value);
                }
                else if (node is JsonArray array) foreach (var value in array) if (value != null) Rewrite(value);
            }
            Rewrite(data); File.WriteAllText(file, data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    internal static string PlanPath(PackStorage storage, string id) => Path.Combine(storage.Home, ".transactions", id, "switch.json");
    static void Save(PackStorage storage, PackSwitchPlan plan) => Files.Save(PlanPath(storage, plan.Id), plan);
    internal static void Commit(PackStorage storage, PackSwitchPlan plan)
    {
        ValidatePlan(storage, plan, true); Stopped(plan.SourceProcessId);
        VerifyEnvironment(storage, plan.After, plan.Originals, plan.EnvironmentVersion);
        foreach (var ledger in plan.Ledgers) if (PackCompleteEnvironment.ReadLedgerText(storage, ledger.PackId) != ledger.Before) throw new IOException("准备后商店归属发生变化，停止切换且不覆盖 ledger。");
        if (plan.EnvironmentVersion == 2)
        {
            var target = plan.After.Packs.Single(p => p.Id == plan.After.ActivePackId);
            PackCompleteEnvironment.RequireComplete(storage, target.UnsavedEnvironment ?? target);
        }
        plan.Phase = "committing"; Save(storage, plan);
        if (!plan.Adopting)
        {
            foreach (var original in plan.Originals)
            {
                Stopped(plan.SourceProcessId); string file = Files.Child(storage.Root, original.Path);
                if (File.Exists(file))
                {
                    if (Files.Hash(file) != original.Hash) throw new IOException("提交时发现用户修改：" + original.Path);
                    Files.MoveAside(file, Files.Child(Path.Combine(storage.Home, ".transactions", plan.Id, "retired"), original.Path));
                }
                plan.Removed.Add(original.Path); Save(storage, plan); StepProbe?.Invoke("removed:" + original.Path); Stopped(plan.SourceProcessId);
            }
            foreach (var desired in plan.Desired)
            {
                Stopped(plan.SourceProcessId); string target = Files.Child(storage.Root, desired.Path);
                if (File.Exists(target)) throw new IOException("目标出现新文件，停止覆盖：" + desired.Path);
                plan.Placed.Add(desired.Path); Save(storage, plan); // journal intent before writing
                Files.CopyAtomic(Files.Child(plan.NewContent, desired.Path), target);
                StepProbe?.Invoke("placed:" + desired.Path); Stopped(plan.SourceProcessId);
            }
        }
        Stopped(plan.SourceProcessId); VerifyEnvironment(storage, plan.After, plan.Desired, plan.EnvironmentVersion); Stopped(plan.SourceProcessId);
        ApplyLedgers(storage, plan, false); StepProbe?.Invoke("before-index");
        storage.Save(plan.After); StepProbe?.Invoke("after-index"); plan.Phase = "installed"; Save(storage, plan);
    }
    internal static void Rollback(PackStorage storage, PackSwitchPlan plan)
    {
        ValidatePlan(storage, plan); Stopped(plan.SourceProcessId); plan.Phase = "rolling-back"; Save(storage, plan);
        foreach (string path in plan.Placed.AsEnumerable().Reverse())
        {
            Stopped(plan.SourceProcessId); string file = Files.Child(storage.Root, path);
            var desired = plan.Desired.Single(f => Files.Compare.Equals(f.Path, path));
            if (File.Exists(file))
            {
                var original = plan.Originals.FirstOrDefault(f => Files.Compare.Equals(f.Path, path));
                if (original != null && Files.Hash(file) == original.Hash) continue; // already restored before an interrupted rollback
                if (Files.Hash(file) != desired.Hash) throw new IOException("回滚目标已修改，请人工恢复：" + path);
                Files.MoveAside(file, Files.Child(Path.Combine(storage.Home, ".transactions", plan.Id, "withdrawn"), path));
            }
        }
        foreach (var original in plan.Originals)
        {
            Stopped(plan.SourceProcessId); string file = Files.Child(storage.Root, original.Path);
            if (File.Exists(file)) { if (Files.Hash(file) != original.Hash) throw new IOException("回滚原文件已修改：" + original.Path); continue; }
            Files.CopyAtomic(Files.Child(plan.Backup, original.Path), file);
        }
        ApplyLedgers(storage, plan, true); StepProbe?.Invoke("rollback-ledgers");
        storage.Save(plan.Before); plan.Phase = "rolled-back"; Save(storage, plan);
    }
    static void ValidatePlan(PackStorage storage, PackSwitchPlan plan, bool requireTargetState = false)
    {
        if (plan.SchemaVersion is not (1 or 2) || plan.EnvironmentVersion is not (1 or 2) || plan.SchemaVersion == 1 && plan.EnvironmentVersion != 1 || !PackStorage.Id(plan.Id) || plan.Before == null || plan.After == null || plan.Before.SchemaVersion is not (1 or 2) || plan.After.SchemaVersion is not (1 or 2)
            || plan.Originals == null || plan.Desired == null || plan.Placed == null || plan.Removed == null) throw new IOException("切包事务损坏，请先恢复备份。");
        string expected = Path.Combine(storage.Home, ".transactions", plan.Id, "backup");
        if (!Files.Compare.Equals(Path.GetFullPath(expected), Path.GetFullPath(plan.Backup))) throw new IOException("切包备份目录越界。");
        if (plan.EnvironmentVersion == 1) ValidateHosts(storage, plan.After);
        var ledgerIds = new HashSet<string>(Files.Compare);
        foreach (var ledger in plan.Ledgers)
        {
            if (plan.SchemaVersion != 2 || !PackStorage.Id(ledger.PackId) || !ledgerIds.Add(ledger.PackId) || !plan.After.Packs.Any(p => p.Id == ledger.PackId)) throw new IOException("ledger 事务身份无效。");
            if (JsonSerializer.Deserialize<ShopLedger>(ledger.After, Files.Json)?.SchemaVersion != 1) throw new IOException("ledger 事务损坏。");
            if (requireTargetState)
            {
                var record = plan.After.Packs.Single(p => p.Id == ledger.PackId);
                record = record.UnsavedEnvironment ?? record;
                var paired = new PackRevisionState(storage).Read(record);
                if (paired == null || JsonSerializer.Serialize(paired, Files.Json) != ledger.After) throw new IOException("事务 ledger 与版本状态不匹配。");
            }
        }
        if (plan.EnvironmentVersion == 2 && (plan.SchemaVersion != 2 || plan.Ledgers.Count == 0)) throw new IOException("完整切换缺少配套 ledger 事务。");
        if (!plan.Adopting)
        {
            if (plan.EnvironmentVersion == 1 && !plan.Before.HostRoots.ToHashSet(Files.Compare).SetEquals(plan.After.HostRoots)) throw new IOException("固定宿主归属发生变化。");
            var stored = plan.After.Packs.SingleOrDefault(p => p.Id == plan.After.ActivePackId) ?? throw new IOException("目标包身份缺失。");
            var next = stored.UnsavedEnvironment ?? stored;
            if (!Files.Compare.Equals(Path.GetFullPath(plan.NewContent), Path.GetFullPath(storage.Content(next)))) throw new IOException("目标库存来源越界。");
            if (!plan.Desired.Select(f => f.Path + "|" + f.Hash).ToHashSet(Files.Compare).SetEquals(next.Files.Select(f => f.Path + "|" + f.Hash))) throw new IOException("切包计划与库存清单不匹配。");
        }
        var paths = new HashSet<string>(Files.Compare);
        foreach (var file in plan.Originals.Concat(plan.Desired))
        {
            if (!PackStorage.ManagedPath(file.Path, plan.EnvironmentVersion) || plan.EnvironmentVersion == 1 && Host(plan.After, file.Path)) throw new IOException("切包目标超出受管范围：" + file.Path);
            Files.RejectLinks(storage.Root, Files.Child(storage.Root, file.Path));
        }
        foreach (string path in plan.Placed) if (!plan.Desired.Any(f => Files.Compare.Equals(f.Path, path)) || !paths.Add(path)) throw new IOException("切包日志不完整。");
        foreach (var original in plan.Originals)
        {
            string backup = Files.Child(plan.Backup, original.Path); Files.RejectLinks(storage.Home, backup);
            if (!File.Exists(backup) || Files.Hash(backup) != original.Hash) throw new IOException("切包备份缺失或损坏：" + original.Path);
        }
    }

    static void ApplyLedgers(PackStorage storage, PackSwitchPlan plan, bool rollback)
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
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(desired); output.Write(bytes, 0, bytes.Length); output.Flush(true);
                }
                try { FrameworkCompat.MoveOverwrite(temporary, path); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { throw new IOException("写入版本配套 ledger 失败：" + path + "；" + error.Message, error); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            StepProbe?.Invoke((rollback ? "restored-ledger:" : "ledger:") + ledger.PackId);
        }
    }
    static void VerifyEnvironment(PackStorage storage, PackIndex index, IReadOnlyList<PackFile> expected, int environmentVersion)
    {
        var actual = Live(storage, index, environmentVersion);
        if (!actual.Select(f => f.Path + "|" + f.Hash).ToHashSet(Files.Compare).SetEquals(expected.Select(f => f.Path + "|" + f.Hash)))
            throw new IOException("受管目录在准备后发生变化，保留新内容并停止切换。");
    }

    internal static void ValidateCompleted(PackStorage storage, PackSwitchPlan plan)
    {
        ValidatePlan(storage, plan);
        var index = storage.Read();
        var storedExpected = plan.After.Packs.Single(p => p.Id == plan.After.ActivePackId);
        var expected = storedExpected.UnsavedEnvironment ?? storedExpected;
        var storedActual = index.Packs.FirstOrDefault(p => p.Id == index.ActivePackId);
        var actual = storedActual?.UnsavedEnvironment ?? storedActual;
        if (index.ActivePackId != plan.After.ActivePackId || actual?.Content != expected.Content) throw new IOException("已提交切换与当前包身份不一致，请恢复事务记录。");
        if (plan.EnvironmentVersion == 2)
        {
            storage.Validate(expected);
            foreach (var ledger in plan.Ledgers)
                if (PackCompleteEnvironment.ReadLedgerText(storage, ledger.PackId) != ledger.After) throw new IOException("已提交切换的 ledger 与版本状态不一致，请恢复事务记录。");
        }
    }

    internal static void RecordUpdate(string root, Transaction transaction, bool rollback = false)
    {
        var storage = new PackStorage(root); var index = storage.Read(); if (index.ActivePackId.Length == 0) return;
        var owned = index.ActiveFiles.Select(f => Canonical(f.Path)).ToHashSet(Files.Compare);
        string Relative(string p) => Path.GetRelativePath(root, p).Replace('\\', '/');
        if (!transaction.Originals.Concat(transaction.Desired).Any(f => owned.Contains(Canonical(Relative(f.Target))))) return; // host updates remain global
        var removed = transaction.Originals.Concat(transaction.Desired).Select(f => Canonical(Relative(f.Target))).ToHashSet(Files.Compare);
        index.ActiveFiles.RemoveAll(f => removed.Contains(Canonical(f.Path)));
        foreach (var desired in rollback ? transaction.Originals : transaction.Desired)
        {
            string relative = Relative(desired.Target);
            int environmentVersion = index.Packs.Single(p => p.Id == index.ActivePackId).EnvironmentVersion;
            if (PackStorage.ManagedPath(relative, environmentVersion) && (environmentVersion == 2 || !Host(index, relative))) index.ActiveFiles.Add(new PackFile { Path = relative, Hash = desired.Hash });
        }
        storage.Save(index);
    }
}
