using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

/// <summary>Written next to the quarantined pack directory so a deleted pack can be restored by hand or purged later.</summary>
internal sealed class MaintenanceTrashEntry
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string PackId { get; set; } = "";
    public PackRecord Record { get; set; } = new();
    public string DeletedUtc { get; set; } = "";
    public long IndexRevision { get; set; }
}

/// <summary>
/// Two-phase maintenance: Preview() computes and stores a plan (never changes pack data), Execute() recomputes the plan from disk,
/// refuses unless it matches the stored preview fingerprint, and only then touches files.
/// Execute never deletes a path taken from the request or from the stored preview file; targets always come from the fresh plan.
/// </summary>
internal static class PackMaintenance
{
    const int MaxFiles = 200000, MaxModFiles = 5000;
    static readonly TimeSpan PreviewLifetime = TimeSpan.FromMinutes(30);
    static readonly string[] DownloadExtensions = { ".zip", ".part", ".tmp" };
    static readonly string[] ViewCacheExtensions = { ".json", ".tmp" };

    internal sealed class Target
    {
        public MaintenanceItem Item = new();
        public string Absolute = "";
        public List<string>? Files; // absolute files to delete; null means the whole directory
    }
    internal sealed class Plan
    {
        public MaintenancePreview Preview = new();
        public PackIndex Index = new();
        public PackRecord? Pack;
        public List<PackFile> Remaining = new();
        public List<Target> Targets = new();
        public PackTask? Task;
        public bool TaskUnreadable;
    }
    internal sealed record Facts(string Path, string Name, List<string> Guids, List<(string Guid, bool Optional)> Dependencies, List<string> References);

    // ------------------------------------------------------------------ public entry points

    internal static MaintenancePreview Preview(PackStorage storage, MaintenanceRequest request)
    {
        var plan = Build(storage, request);
        var preview = plan.Preview;
        preview.Id = Guid.NewGuid().ToString("N");
        preview.CreatedUtc = DateTime.UtcNow.ToString("O"); preview.ExpiresUtc = (DateTime.UtcNow + PreviewLifetime).ToString("O");
        string directory = MaintenanceDirectory(storage);
        Files.Save(Path.Combine(directory, preview.Id + ".json"), preview);
        Prune(directory);
        return preview;
    }

    internal static MaintenanceResult Execute(PackStorage storage, MaintenanceRequest request)
    {
        if (!PackStorage.Id(request.PreviewId)) throw new IOException("维护预览编号无效。");
        string file = Path.Combine(MaintenanceDirectory(storage), request.PreviewId + ".json");
        if (!File.Exists(file)) throw new IOException("预览已失效或已使用，请重新预览。");
        var stored = Files.Read<MaintenancePreview>(file);
        if (stored.SchemaVersion != 1 || stored.Id != request.PreviewId) throw new IOException("维护预览记录损坏，请重新预览。");
        if (!DateTime.TryParse(stored.ExpiresUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expires) || expires < DateTime.UtcNow)
            throw new IOException("预览已过期，请重新预览。");
        if (stored.Blockers.Count > 0) throw new IOException("此预览存在阻止项，不能执行：" + stored.Blockers[0].Message);
        // The request must describe exactly what was previewed; it can only narrow nothing and add nothing.
        if (!Files.Compare.Equals(request.Operation, stored.Operation)) throw new IOException("执行操作与预览不一致。");
        if (!Files.Compare.Equals(request.PackId, stored.PackId)) throw new IOException("执行范围与预览不一致（整合包）。");
        if (!SameSet(NormalizeScopes(request.Operation, request.Scopes), stored.Scopes)) throw new IOException("执行范围与预览不一致（清理范围）。");
        if (!SameSet(request.Operation == MaintenanceOperations.DeleteMod ? NormalizeModFiles(request.ModFiles) : new List<string>(), stored.ModFiles))
            throw new IOException("执行范围与预览不一致（文件清单）。");
        if (request.WholeCategory != stored.WholeCategory) throw new IOException("清理范围与预览不一致。");
        if (!SameSet(request.SelectedPaths, stored.SelectedPaths)) throw new IOException("执行项目与预览不一致。");
        if (request.Operation == MaintenanceOperations.PurgePackTrash && !request.PermanentConfirmed) throw new IOException("永久删除需要单独确认。");

        var plan = Build(storage, new MaintenanceRequest { Operation = stored.Operation, PackId = stored.PackId, ModFiles = stored.ModFiles.ToList(), Scopes = stored.Scopes.ToList(), SelectedPaths = stored.SelectedPaths.ToList(), WholeCategory = stored.WholeCategory });
        if (plan.Preview.Blockers.Count > 0) throw new IOException("现在不能执行：" + plan.Preview.Blockers[0].Message);
        if (plan.Preview.Fingerprint != stored.Fingerprint) throw new IOException("预览之后内容已经变化，没有执行任何删除，请重新预览。");
        plan.Preview.Id = stored.Id;
        File.Delete(file); // single use

        var result = new MaintenanceResult { PreviewId = stored.Id, Operation = stored.Operation };
        switch (stored.Operation)
        {
            case MaintenanceOperations.DeletePack: ExecuteDeletePack(storage, plan, result); break;
            case MaintenanceOperations.DeleteMod: ExecuteDeleteMod(storage, plan, result); break;
            default: ExecuteDeleteTargets(storage, plan, result); break;
        }
        Files.Save(Path.Combine(MaintenanceDirectory(storage), "result-" + stored.Id + ".json"), result);
        return result;
    }

    // ------------------------------------------------------------------ planning (read-only)

    internal static Plan Build(PackStorage storage, MaintenanceRequest request)
    {
        if (!MaintenanceOperations.Known(request.Operation)) throw new IOException("未知维护操作。");
        var index = storage.Read();
        var plan = new Plan { Index = index };
        (plan.Task, plan.TaskUnreadable) = ReadTask(storage);
        var p = plan.Preview;
        p.Operation = request.Operation; p.PackId = request.PackId ?? ""; p.IndexRevision = index.Revision;
        p.WholeCategory = request.WholeCategory;
        if (request.WholeCategory && (request.SelectedPaths.Count > 0 || !string.IsNullOrEmpty(request.PackId) || request.Operation is not (MaintenanceOperations.CleanCache or MaintenanceOperations.PurgePackTrash))) throw new IOException("整类清理不能携带单项目标。");
        p.Scopes = NormalizeScopes(request.Operation, request.Scopes);
        if (request.Operation == MaintenanceOperations.DeleteMod) p.ModFiles = NormalizeModFiles(request.ModFiles);
        switch (request.Operation)
        {
            case MaintenanceOperations.DeletePack: PlanDeletePack(storage, plan); break;
            case MaintenanceOperations.DeleteMod: PlanDeleteMod(storage, plan); break;
            case MaintenanceOperations.CleanCache: PlanCleanCache(storage, plan); break;
            default: PlanPurgeTrash(storage, plan); break;
        }
        p.SelectedPaths = request.SelectedPaths.Select(p => p.Replace((char)92, '/')).Distinct(Files.Compare).OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (p.SelectedPaths.Count > 0)
        {
            foreach (string path in p.SelectedPaths)
                if (!p.Items.Any(i => Files.Compare.Equals(i.Path, path))) Block(p, "not-in-preview", "项目不在可清理范围内：" + path);
            p.Items = p.Items.Where(i => p.SelectedPaths.Contains(i.Path, Files.Compare)).ToList();
            plan.Targets = plan.Targets.Where(t => p.SelectedPaths.Contains(t.Item.Path, Files.Compare)).ToList();
        }
        p.Items = p.Items.OrderBy(i => i.Scope, StringComparer.Ordinal).ThenBy(i => i.Path, StringComparer.Ordinal).ToList();
        p.TotalBytes = p.Items.Sum(i => i.Bytes); p.TotalFiles = p.Items.Sum(i => i.FileCount);
        p.Fingerprint = Fingerprint(p, request.Operation is MaintenanceOperations.DeletePack or MaintenanceOperations.DeleteMod);
        return plan;
    }

    static void Block(MaintenancePreview p, string code, string message) => p.Blockers.Add(new MaintenanceIssue { Code = code, Message = message });

    static (PackTask?, bool) ReadTask(PackStorage storage)
    {
        try { return (File.Exists(storage.TaskPath) ? Files.Read<PackTask>(storage.TaskPath) : null, false); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return (null, true); }
    }
    /// <summary>A task that may still read or write pack data.</summary>
    internal static bool Busy(PackTask task) => task.BlocksUpdates || task.State is "requested" or "preparing";
    internal static bool Mentions(PackTask task, string packId) => task.Proposed.Id == packId || task.Request.PackId == packId;

    static void PlanDeletePack(PackStorage storage, Plan plan)
    {
        var p = plan.Preview;
        if (!PackStorage.Id(p.PackId)) { Block(p, "missing", "整合包编号无效。"); return; }
        var pack = plan.Index.Packs.FirstOrDefault(x => x.Id == p.PackId);
        if (pack == null) { Block(p, "missing", "整合包不在库存中。"); return; }
        plan.Pack = pack; p.PackName = pack.Name;
        if (plan.Index.ActivePackId == pack.Id) Block(p, "active-pack", "这是当前使用的整合包，不能删除。请先切换到其他整合包（退出游戏后生效）。");
        RequireNoPendingTask(plan, true);
        if (pack.PendingUpdate != null) p.Warnings.Add("此包有尚未应用的更新版本，会一并移入回收区。");
        p.Warnings.Add("整合包的个人方案、语言文件等工作区数据也随包一起移入回收区。");
        string directory = storage.DirectoryFor(pack.Id);
        try
        {
            var walk = Walk(Directory.GetParent(storage.Root)!.FullName, directory);
            string stamp = Digest(walk, JsonSerializer.Serialize(pack, Files.Json));
            var item = new MaintenanceItem { Kind = "pack", Scope = "pack", Path = Relative(storage, directory), Bytes = walk.Sum(f => f.Length), FileCount = walk.Count, Stamp = stamp };
            p.Items.Add(item); plan.Targets.Add(new Target { Item = item, Absolute = directory });
            p.Details.Add("名称：" + pack.Name + " " + pack.Version + "；文件 " + walk.Count + " 个。");
            p.Details.Add("删除方式：移入 MCE/packs/.trash/<编号>，可在回收区恢复；永久删除需要另行确认。");
        }
        catch (IOException e) { Block(p, "links", e.Message); }
    }

    static void RequireNoPendingTask(Plan plan, bool packOperation)
    {
        var p = plan.Preview;
        if (plan.TaskUnreadable) { Block(p, "pending-task", "整合包任务记录无法读取，为安全起见暂停维护。"); return; }
        var task = plan.Task; if (task == null) return;
        if (Busy(task)) Block(p, "pending-task", "有整合包任务正在进行或等待退出（" + task.State + "），请先完成或取消。");
        else if (packOperation && task.State == "preview" && Mentions(task, plan.Preview.PackId)) Block(p, "pending-task", "该整合包正有一个等待确认的预览，请先取消或确认。");
    }

    static void PlanDeleteMod(PackStorage storage, Plan plan)
    {
        var p = plan.Preview; var index = plan.Index;
        if (!PackStorage.Id(p.PackId)) { Block(p, "missing", "整合包编号无效。"); return; }
        var pack = index.Packs.FirstOrDefault(x => x.Id == p.PackId);
        if (pack == null) { Block(p, "missing", "整合包不在库存中。"); return; }
        plan.Pack = pack; p.PackName = pack.Name;
        if (index.ActivePackId == pack.Id) { Block(p, "active-pack", "当前启用的整合包不能直接修改。请先退出游戏并切换到其他整合包，再删除其中的模组。"); return; }
        RequireNoPendingTask(plan, true);
        if (pack.UnsavedEnvironment != null) { Block(p, "unsaved-environment", "此包有未保存运行环境，请先明确保存或放弃后再修改。"); return; }
        if (pack.PendingUpdate != null) { Block(p, "pending-update", "此包有尚未应用的更新版本，请先处理更新后再删除模组。"); return; }
        if (pack.Content.Length == 0) { Block(p, "missing", "此包没有可修改的库存内容。"); return; }
        if (p.ModFiles.Count == 0) { Block(p, "missing", "没有选择要删除的文件。"); return; }
        if (p.ModFiles.Count > MaxModFiles) { Block(p, "too-many", "一次最多删除 " + MaxModFiles + " 个文件。"); return; }
        var owned = pack.Files.ToDictionary(f => f.Path.Replace('\\', '/'), f => f, Files.Compare);
        foreach (string path in p.ModFiles)
        {
            if (!path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) || !PackStorage.ManagedPath(path)) Block(p, "scope", "只能删除 plugins 下的模组文件：" + path);
            else if (!owned.ContainsKey(path)) Block(p, "not-in-pack", "文件不在该整合包清单中：" + path);
            else if (!PackEnvironmentPolicy.IsComplete(pack) && HostOwned(index, path)) Block(p, "host-file", "这是固定宿主（MCE）的文件，不能删除：" + path);
            else if (ManagerIdentity.ContainsPackFolder(path) || pack.AuthorRoot.Length > 0 && path.StartsWith(pack.AuthorRoot.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase))
                Block(p, "author-data", "作者资料受签名保护，不能单独删除：" + path);
        }
        if (p.Blockers.Count > 0) return;
        try { storage.Validate(pack); }
        catch (IOException e) { Block(p, "revision-changed", "整合包库存校验失败：" + e.Message); return; }
        string content = storage.Content(pack);
        var removing = p.ModFiles.ToHashSet(Files.Compare);
        plan.Remaining = pack.Files.Where(f => !removing.Contains(f.Path.Replace('\\', '/'))).Select(f => new PackFile { Path = f.Path, Hash = f.Hash }).ToList();
        if (plan.Remaining.Count == 0) { Block(p, "all-files", "这会清空整个整合包，请改为删除整合包。"); return; }
        foreach (string path in p.ModFiles)
            p.Items.Add(new MaintenanceItem { Kind = "mod-file", Scope = "mod", Path = path, Bytes = new FileInfo(Files.Child(content, path)).Length, FileCount = 1, Stamp = owned[path].Hash });
        foreach (var issue in CheckDependencies(content, pack, p.ModFiles, index)) p.Blockers.Add(issue);
        p.Warnings.Add("无法检查非代码资源（贴图、音频、配置）是否被其他模组使用。");
        p.Details.Add("将生成新的库存版本并只移除上述 " + p.ModFiles.Count + " 个文件；旧版本保留在库存目录中，当前游戏文件不会被修改。");
    }

    static bool HostOwned(PackIndex index, string path) => index.HostRoots.Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));

    internal static Facts? Inspect(string file, string relative)
    {
        try
        {
            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(file)!);
            for(var parent=new DirectoryInfo(Path.GetDirectoryName(file)!);parent!=null;parent=parent.Parent)
            {
                string core=Path.Combine(parent.FullName,"core");if(File.Exists(Path.Combine(core,"BepInEx.dll")))resolver.AddSearchDirectory(core);
                string installedCore=Path.Combine(parent.FullName,"BepInEx","core");if(File.Exists(Path.Combine(installedCore,"BepInEx.dll")))resolver.AddSearchDirectory(installedCore);
                string managed=Path.Combine(parent.FullName,"PEAK_Data","Managed");if(Directory.Exists(managed))resolver.AddSearchDirectory(managed);
            }
            using var assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { AssemblyResolver=resolver, ReadSymbols = false, ReadingMode = ReadingMode.Deferred });
            var facts = new Facts(relative, assembly.Name.Name, new(), new(), assembly.MainModule.AssemblyReferences.Select(r => r.Name).ToList());
            foreach (var type in assembly.MainModule.Types)
            {
                foreach (var attribute in type.CustomAttributes)
                {
                    string kind = attribute.AttributeType.FullName;
                    if (kind == "BepInEx.BepInPlugin" && attribute.ConstructorArguments.Count == 3 && attribute.ConstructorArguments[0].Value is string guid) facts.Guids.Add(guid);
                    else if (kind == "BepInEx.BepInDependency" && attribute.ConstructorArguments.Count > 0 && attribute.ConstructorArguments[0].Value is string dependency)
                    {
                        bool optional = attribute.ConstructorArguments.Count > 1 && attribute.ConstructorArguments[1].Value is not string && (Convert.ToInt32(attribute.ConstructorArguments[1].Value) & 2) != 0;
                        facts.Dependencies.Add((dependency, optional));
                    }
                }
            }
            return facts;
        }
        catch (BadImageFormatException) { return null; } // native or non-.NET file
        catch (ArgumentException) { return null; }
    }

    /// <summary>Blocks removal of an assembly that another remaining DLL hard-depends on (BepInDependency or assembly reference) or a remaining manifest depends on.</summary>
    internal static List<MaintenanceIssue> CheckDependencies(string content, PackRecord pack, IReadOnlyList<string> removed, PackIndex index)
    {
        var issues = new List<MaintenanceIssue>();
        var removedSet = removed.ToHashSet(Files.Compare);
        var gone = new List<Facts>(); var kept = new List<Facts>();
        foreach (var file in pack.Files.Where(f => Packages.IsAssembly(f.Path)))
        {
            string path = file.Path.Replace('\\', '/');
            Facts? facts;
            try { facts = Inspect(Files.Child(content, path), path); }
            catch (IOException e) { issues.Add(new MaintenanceIssue { Code = "dependency", Message = "无法分析程序集，不能确认依赖安全：" + path + "（" + e.Message + "）" }); continue; }
            if (facts != null) (removedSet.Contains(path) ? gone : kept).Add(facts);
        }
        var goneGuids = gone.SelectMany(f => f.Guids).ToHashSet(Files.Compare);
        var goneNames = gone.Select(f => f.Name).ToHashSet(Files.Compare);
        var provided = kept.SelectMany(f => f.Guids).Concat(PackEnvironmentPolicy.IsComplete(pack) ? Array.Empty<string>() : index.HostPlugins.Select(h => h.Guid)).ToHashSet(Files.Compare);
        var providedNames = kept.Select(f => f.Name).ToHashSet(Files.Compare);
        foreach (var guid in gone.SelectMany(f => f.Guids)) if (PackEnvironmentPolicy.IsComplete(pack) ? ManagerIdentity.IsManagerGuid(guid) : index.HostPlugins.Any(h => Files.Compare.Equals(h.Guid, guid)))
            issues.Add(new MaintenanceIssue { Code = "host-file", Message = "要删除的插件是固定宿主：" + guid });
        foreach (var other in kept)
        {
            foreach (var (guid, optional) in other.Dependencies)
                if (goneGuids.Contains(guid) && !provided.Contains(guid) && !optional)
                    issues.Add(new MaintenanceIssue { Code = "dependency", Message = other.Path + " 硬依赖插件 " + guid + "，不能删除提供它的文件。" });
            foreach (string reference in other.References)
                if (goneNames.Contains(reference) && !providedNames.Contains(reference))
                    issues.Add(new MaintenanceIssue { Code = "dependency", Message = other.Path + " 引用程序集 " + reference + "，不能删除该程序集。" });
        }
        // Thunderstore manifests: a remaining package may list a removed package as dependency.
        var removedNames = new HashSet<string>(Files.Compare);
        foreach (string path in removed.Where(r => Path.GetFileName(r).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
            if (ManifestName(Files.Child(content, path)) is { Length: > 0 } name) removedNames.Add(name);
        if (removedNames.Count > 0)
            foreach (var file in pack.Files.Where(f => Path.GetFileName(f.Path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase) && !removedSet.Contains(f.Path.Replace('\\', '/'))))
                foreach (string dependency in ManifestDependencies(Files.Child(content, file.Path.Replace('\\', '/'))))
                {
                    var parts = dependency.Split('-');
                    if (parts.Length >= 3 && removedNames.Contains(parts[^2]))
                        issues.Add(new MaintenanceIssue { Code = "dependency", Message = file.Path + " 的依赖列表包含 " + dependency + "，不能删除该模组。" });
                }
        return issues;
    }
    static string ManifestName(string file)
    { try { return JsonNode.Parse(File.ReadAllText(file))?["name"]?.GetValue<string>() ?? ""; } catch (Exception e) when (e is JsonException or InvalidOperationException or IOException) { return ""; } }
    static List<string> ManifestDependencies(string file)
    {
        try { return (JsonNode.Parse(File.ReadAllText(file))?["dependencies"] as JsonArray)?.Select(v => v?.GetValue<string>() ?? "").Where(v => v.Length > 0).ToList() ?? new(); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException) { return new(); }
    }

    static void PlanCleanCache(PackStorage storage, Plan plan)
    {
        var p = plan.Preview;
        string game = Directory.GetParent(storage.Root)!.FullName;
        RequireNoPendingTask(plan, false);
        foreach (string scope in p.Scopes)
        {
            try
            {
                if (scope == MaintenanceScopes.Downloads) PlanDownloads(storage, plan, game);
                else if (scope == MaintenanceScopes.ViewCache) PlanViewCache(storage, plan, game);
                else if(scope==MaintenanceScopes.ShopTasks)PlanShopTasks(storage,plan,game);
                else if(scope==MaintenanceScopes.ShopResources)PlanShopResources(storage,plan,game);
                else PlanTransactions(storage, plan, game);
            }
            catch (IOException e) { Block(p, "links", scope + "：" + e.Message); }
        }
    }

    internal static string DownloadsDirectory(PackStorage storage) => ManagerIdentity.Runtime(storage.Root, "downloads");
    internal static string ViewCacheDirectory(PackStorage storage) => Path.Combine(storage.Home, ".view-cache");
    internal static string TransactionsDirectory(PackStorage storage) => Path.Combine(storage.Home, ".transactions");
    internal static string TrashDirectory(PackStorage storage) => Path.Combine(storage.Home, ".trash");

    internal static string ShopTasksDirectory(PackStorage storage)=>ManagerIdentity.Runtime(storage.Root,"shop","tasks");
    internal static string ShopResourcesDirectory(PackStorage storage)=>Path.Combine(MceDirectories.Runtime(MceDirectories.RootFromBepInEx(storage.Root)),"shop-resources");
    internal static bool CompletedShopTask(string directory,string name)
    {
        try {
            var file=Path.Combine(directory,"task.json");if(!File.Exists(file))return false;
            var task=Files.Read<ShopTask>(file);if(task.Id!=name||task.State is not ("completed" or "failed"))return false;
            string journal=Path.Combine(directory,"transaction.json");
            return File.Exists(journal)?Files.Read<ShopQueue.ShopTransaction>(journal).Phase is "installed" or "rolled-back":task.State=="failed";
        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){return false;}
    }
    static void PlanShopTasks(PackStorage storage,Plan plan,string game)
    {
        string root=ShopTasksDirectory(storage);if(!Directory.Exists(root))return;Files.RejectLinks(game,root);
        foreach(string directory in Directory.GetDirectories(root).OrderBy(s=>s,StringComparer.Ordinal)){
            string name=Path.GetFileName(directory);if(!PackStorage.Id(name)||!CompletedShopTask(directory,name)){plan.Preview.Warnings.Add("商店任务未完成或记录无效，已保留："+name);continue;}
            string current=ManagerIdentity.Runtime(storage.Root,"shop","task.json");if(File.Exists(current)){var task=Files.Read<ShopTask>(current);if(task.Id==name&&task.BlocksWrites){plan.Preview.Warnings.Add("商店任务仍被使用，已保留："+name);continue;}}
            var walk=Walk(game,directory);var item=new MaintenanceItem{Kind="directory",Scope=MaintenanceScopes.ShopTasks,Path=Relative(storage,directory),Bytes=walk.Sum(f=>f.Length),FileCount=walk.Count,Stamp=Digest(walk,"")};
            plan.Preview.Items.Add(item);plan.Targets.Add(new Target{Item=item,Absolute=directory});
        }
    }
    static void PlanShopResources(PackStorage storage,Plan plan,string game)
    {
        string root=ShopResourcesDirectory(storage);if(!Directory.Exists(root))return;
        var walk=Walk(game,root).Where(f=>Path.GetExtension(f.Rel).Equals(".cache",StringComparison.OrdinalIgnoreCase)).ToList();
        if(walk.Count==0)return;var item=new MaintenanceItem{Kind="directory",Scope=MaintenanceScopes.ShopResources,Path=Relative(storage,root),Bytes=walk.Sum(f=>f.Length),FileCount=walk.Count,Stamp=Digest(walk,"")};
        plan.Preview.Items.Add(item);plan.Targets.Add(new Target{Item=item,Absolute=root,Files=walk.Select(f=>Files.Child(root,f.Rel)).ToList()});
    }
    static void PlanDownloads(PackStorage storage, Plan plan, string game)
    {
        var p = plan.Preview; string directory = DownloadsDirectory(storage);
        if (!Directory.Exists(directory)) return;
        if (plan.TaskUnreadable || plan.Task != null && Busy(plan.Task)) { Block(p, "pending-task", "有整合包导入或切换任务正在使用下载缓存，暂不能清理。"); return; }
        Files.RejectLinks(game, directory); int skipped = 0;
        foreach (string file in Directory.GetFiles(directory).OrderBy(f => f, StringComparer.Ordinal))
        {
            Files.RejectLinks(game, file);
            if (!DownloadExtensions.Contains(Path.GetExtension(file), Files.Compare)) { skipped++; continue; }
            var info = new FileInfo(file);
            var item = new MaintenanceItem { Kind = "file", Scope = MaintenanceScopes.Downloads, Path = Relative(storage, file), Bytes = info.Length, FileCount = 1, Stamp = info.Length + "-" + info.LastWriteTimeUtc.Ticks };
            p.Items.Add(item); plan.Targets.Add(new Target { Item = item, Absolute = file, Files = new List<string> { file } });
        }
        skipped += Directory.GetDirectories(directory).Length;
        if (skipped > 0) p.Warnings.Add("下载缓存中有 " + skipped + " 个非缓存文件或子目录，已保留。");
    }

    static void PlanViewCache(PackStorage storage, Plan plan, string game)
    {
        var p = plan.Preview; string directory = ViewCacheDirectory(storage);
        if (!Directory.Exists(directory)) return;
        var walk = Walk(game, directory); int skipped = walk.Count(f => !ViewCacheExtensions.Contains(Path.GetExtension(f.Rel), Files.Compare));
        walk = walk.Where(f => ViewCacheExtensions.Contains(Path.GetExtension(f.Rel), Files.Compare)).ToList();
        if (skipped > 0) p.Warnings.Add("预览缓存目录中有 " + skipped + " 个非缓存文件，已保留。");
        if (walk.Count == 0) return;
        var item = new MaintenanceItem { Kind = "directory", Scope = MaintenanceScopes.ViewCache, Path = Relative(storage, directory), Bytes = walk.Sum(f => f.Length), FileCount = walk.Count, Stamp = Digest(walk, "") };
        p.Items.Add(item);
        plan.Targets.Add(new Target { Item = item, Absolute = directory, Files = walk.Select(f => Path.Combine(directory, f.Rel)).ToList() });
    }

    static void PlanTransactions(PackStorage storage, Plan plan, string game)
    {
        var p = plan.Preview; string root = TransactionsDirectory(storage);
        if (!Directory.Exists(root)) return;
        if (plan.TaskUnreadable) { Block(p, "pending-task", "整合包任务记录无法读取，暂不能清理事务。"); return; }
        Files.RejectLinks(game, root); int kept = 0;
        var referenced = ReferencedRevisions(storage, plan.Index);
        foreach (string directory in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(directory);
            if (!PackStorage.Id(name) || Protects(referenced, directory)) { kept++; continue; }
            if (plan.Task != null && plan.Task.Id == name && plan.Task.State is not ("completed" or "failed" or "cancelled")) { p.Warnings.Add("已保护当前未完成的整合包事务：" + name); continue; }
            if (!CompletedTransaction(storage, directory, name)) { kept++; continue; }
            var walk = Walk(game, directory);
            var item = new MaintenanceItem { Kind = "directory", Scope = MaintenanceScopes.Transactions, Path = Relative(storage, directory), Bytes = walk.Sum(f => f.Length), FileCount = walk.Count, Stamp = Digest(walk, "") };
            p.Items.Add(item); plan.Targets.Add(new Target { Item = item, Absolute = directory });
        }
        if (kept > 0) p.Warnings.Add("有 " + kept + " 个事务目录未完成、不可识别或被库存引用，已保留。");
    }

    /// <summary>Only an installed or rolled-back switch plan with the matching id is considered finished.</summary>
    internal static bool CompletedTransaction(PackStorage storage, string directory, string name)
    {
        string file = Path.Combine(directory, "switch.json");
        try
        {
            if (!File.Exists(file)) return false;
            var plan = Files.Read<PackSwitchPlan>(file);
            return plan.SchemaVersion is 1 or 2 && plan.Id == name && plan.Phase is "installed" or "rolled-back";
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Absolute content directories of every revision any index record (including pending updates) points at.</summary>
    internal static List<string> ReferencedRevisions(PackStorage storage, PackIndex index)
    {
        var result = new List<string>();
        foreach (var pack in index.Packs)
            foreach (var record in new[] { pack, pack.PendingUpdate, pack.UnsavedEnvironment })
                if (record != null && record.Content.Length > 0)
                {
                    try
                    {
                        result.Add(Path.GetFullPath(storage.Content(record)));
                        if (PackEnvironmentPolicy.IsComplete(record)) result.Add(Path.GetDirectoryName(new PackRevisionState(storage).PathFor(record))!);
                    }
                    catch (IOException) { }
                }
        return result;
    }
    /// <summary>True when the candidate is, contains or lies inside a referenced revision.</summary>
    internal static bool Protects(IEnumerable<string> referenced, string candidate)
    {
        string full = Path.GetFullPath(candidate);
        return referenced.Any(r => Files.Compare.Equals(r, full) || Files.Within(r, full) || Files.Within(full, r));
    }

    static void PlanPurgeTrash(PackStorage storage, Plan plan)
    {
        var p = plan.Preview; string root = TrashDirectory(storage), game = Directory.GetParent(storage.Root)!.FullName;
        if (p.PackId.Length > 0 && !PackStorage.Id(p.PackId)) { Block(p, "missing", "回收区条目编号无效。"); return; }
        if (!Directory.Exists(root)) { Block(p, "missing", "回收区为空。"); return; }
        RequireNoPendingTask(plan, false);
        try
        {
            Files.RejectLinks(game, root);
            foreach (string directory in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
            {
                string name = Path.GetFileName(directory);
                if (!PackStorage.Id(name) || p.PackId.Length > 0 && name != p.PackId) continue;
                if (!ReadTrash(directory, name, out var entry)) { p.Warnings.Add("回收区条目缺少有效记录，已保留：" + name); continue; }
                var walk = Walk(game, directory);
                var item = new MaintenanceItem { Kind = "directory", Scope = MaintenanceScopes.PackTrash, Path = Relative(storage, directory), Bytes = walk.Sum(f => f.Length), FileCount = walk.Count, Stamp = Digest(walk, "") };
                p.Items.Add(item); plan.Targets.Add(new Target { Item = item, Absolute = directory });
                p.Details.Add("永久删除：" + entry!.Record.Name + "（" + name + "），无法再恢复。");
            }
        }
        catch (IOException e) { Block(p, "links", e.Message); }
        if (p.Items.Count == 0 && p.Blockers.Count == 0) Block(p, "missing", "回收区中没有可永久删除的整合包。");
    }
    static bool ReadTrash(string directory, string name, out MaintenanceTrashEntry? entry)
    {
        entry = null;
        try
        {
            string file = Path.Combine(directory, "trash.json"); if (!File.Exists(file)) return false;
            entry = Files.Read<MaintenanceTrashEntry>(file);
            return entry.SchemaVersion == 1 && entry.Id == name && PackStorage.Id(entry.PackId);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }

    // ------------------------------------------------------------------ execution

    static void ExecuteDeletePack(PackStorage storage, Plan plan, MaintenanceResult result)
    {
        var pack = plan.Pack!; var target = plan.Targets.Single(); string id = plan.Preview.Id;
        if (!IsAllowedTarget(storage, "pack", target.Absolute)) throw new IOException("删除目标不在允许范围内。");
        string trash = Path.Combine(TrashDirectory(storage), id), moved = Path.Combine(trash, "pack");
        bool exists = Directory.Exists(target.Absolute);
        Directory.CreateDirectory(trash);
        Files.Save(Path.Combine(trash, "trash.json"), new MaintenanceTrashEntry { Id = id, PackId = pack.Id, Record = pack, DeletedUtc = DateTime.UtcNow.ToString("O"), IndexRevision = plan.Index.Revision });
        try
        {
            if (exists) Directory.Move(target.Absolute, moved);
            var index = plan.Index;
            if (storage.Read().Revision != index.Revision) throw new IOException("整合包索引在预览后发生变化，请重新预览。");
            index.Packs.RemoveAll(x => x.Id == pack.Id);
            try { storage.Save(index); }
            catch { if (exists && Directory.Exists(moved) && !Directory.Exists(target.Absolute)) Directory.Move(moved, target.Absolute); throw; }
        }
        catch
        {
            try { if (!Directory.Exists(moved) && File.Exists(Path.Combine(trash, "trash.json"))) { File.Delete(Path.Combine(trash, "trash.json")); Directory.Delete(trash); } } catch (IOException) { }
            throw;
        }
        result.Success = true; result.RemovedItems = 1; result.FreedBytes = 0; result.TrashPath = Relative(storage, trash);
        result.Message = "整合包「" + pack.Name + "」已移入回收区，可以恢复；尚未永久删除。";
    }

    static void ExecuteDeleteMod(PackStorage storage, Plan plan, MaintenanceResult result)
    {
        var pack = plan.Pack!; var index = plan.Index;
        string oldContent = storage.Content(pack), revisionId = Guid.NewGuid().ToString("N");
        string newRelative = "revisions/" + revisionId;
        var oldRecord = (Content: pack.Content, Files: pack.Files, Modified: pack.ModifiedUtc, Report: pack.Report, StateHash: pack.RevisionStateHash);
        var ledger = new PackRevisionState(storage).Read(pack); // state paired to source revision, never workspace
        pack.Content = newRelative; // Content() validates the shape and gives the new location below this pack's own directory
        string newContent = storage.Content(pack);
        pack.Content = oldRecord.Content;
        if (Directory.Exists(newContent)) throw new IOException("新库存版本位置已存在，请重试。");
        try
        {
            foreach (var file in plan.Remaining)
            {
                string source = Files.Child(oldContent, file.Path), target = Files.Child(newContent, file.Path);
                if (Files.Hash(source) != file.Hash) throw new IOException("库存文件在删除期间改变：" + file.Path);
                Files.CopyAtomic(source, target);
                if (Files.Hash(target) != file.Hash) throw new IOException("新库存文件校验失败：" + file.Path);
            }
            pack.Content = newRelative; pack.Files = plan.Remaining; pack.ModifiedUtc = DateTime.UtcNow.ToString("O"); pack.Report = null;
            if (ledger != null)
            {
                pack.RevisionStateHash = "";
                PackCompleteEnvironment.ReconcileEditedLedger(ledger, pack);
                new PackRevisionState(storage).Write(pack, ledger);
            }
            storage.Validate(pack);
            if (storage.Read().Revision != index.Revision) throw new IOException("整合包索引在预览后发生变化，请重新预览。");
            storage.Save(index);
        }
        catch
        {
            pack.Content = oldRecord.Content; pack.Files = oldRecord.Files; pack.ModifiedUtc = oldRecord.Modified; pack.Report = oldRecord.Report; pack.RevisionStateHash = oldRecord.StateHash;
            // Failed new payload/state remain unreferenced for diagnosis; never damage the original pair.
            throw;
        }
        result.Success = true; result.RemovedItems = plan.Preview.ModFiles.Count; result.FreedBytes = 0; result.NewRevision = newRelative;
        result.Message = "已从整合包「" + pack.Name + "」移除 " + plan.Preview.ModFiles.Count + " 个文件，生成了新的库存版本。旧版本仍保留在库存目录。";
    }

    static void ExecuteDeleteTargets(PackStorage storage, Plan plan, MaintenanceResult result)
    {
        var referenced = ReferencedRevisions(storage, plan.Index);
        foreach (var target in plan.Targets)
        {
            string scope = target.Item.Scope;
            try
            {
                if (!IsAllowedTarget(storage, scope, target.Absolute)) throw new IOException("目标不在允许范围内：" + target.Item.Path);
                if (scope != MaintenanceScopes.PackTrash && Protects(referenced, target.Absolute)) throw new IOException("目标属于库存引用的版本：" + target.Item.Path);
                if (target.Files != null)
                {
                    foreach (string file in target.Files)
                    {
                        if (!Files.Within(target.Absolute, file) && !Files.Compare.Equals(Path.GetDirectoryName(file), target.Absolute) && !Files.Compare.Equals(file, target.Absolute)) throw new IOException("文件越界：" + file);
                        if (File.Exists(file)) File.Delete(file);
                    }
                    if (scope is MaintenanceScopes.ViewCache or MaintenanceScopes.ShopResources) RemoveEmptyDirectories(target.Absolute);
                }
                else if (Directory.Exists(target.Absolute)) Directory.Delete(target.Absolute, true);
                result.RemovedItems++; result.FreedBytes += target.Item.Bytes;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { result.Failures.Add(target.Item.Path + "：" + e.Message); }
        }
        result.Success = result.Failures.Count == 0;
        result.Message = (plan.Preview.Operation == MaintenanceOperations.PurgePackTrash ? "已永久删除 " : "已清理 ") + result.RemovedItems + " 项，释放约 " + result.FreedBytes / 1024 + " KB。" + (result.Failures.Count > 0 ? "有 " + result.Failures.Count + " 项失败，已保留。" : "");
    }
    static void RemoveEmptyDirectories(string root)
    {
        foreach (string directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
    }

    /// <summary>The only places maintenance may delete from or move away. Everything else (user project backups, game core, live plugins) is rejected.</summary>
    internal static bool IsAllowedTarget(PackStorage storage, string scope, string absolute)
    {
        string full = Path.GetFullPath(absolute), parent = Path.GetDirectoryName(full) ?? "", name = Path.GetFileName(full);
        bool Same(string a, string b) => Files.Compare.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'));
        return scope switch
        {
            "pack" => Same(parent, storage.Home) && PackStorage.Id(name),
            MaintenanceScopes.Downloads => Same(parent, DownloadsDirectory(storage)) && DownloadExtensions.Contains(Path.GetExtension(name), Files.Compare),
            MaintenanceScopes.ViewCache => Same(full, ViewCacheDirectory(storage)),
            MaintenanceScopes.ShopResources => Same(full,ShopResourcesDirectory(storage)),
            MaintenanceScopes.ShopTasks => Same(parent,ShopTasksDirectory(storage))&&PackStorage.Id(name),
            MaintenanceScopes.Transactions => Same(parent, TransactionsDirectory(storage)) && PackStorage.Id(name),
            MaintenanceScopes.PackTrash => Same(parent, TrashDirectory(storage)) && PackStorage.Id(name),
            _ => false
        };
    }

    // ------------------------------------------------------------------ helpers

    internal static List<string> NormalizeScopes(string operation, IEnumerable<string>? scopes)
    {
        if (operation == MaintenanceOperations.PurgePackTrash) return new() { MaintenanceScopes.PackTrash };
        if (operation != MaintenanceOperations.CleanCache) return new();
        var result = new List<string>();
        foreach (string raw in scopes ?? Array.Empty<string>())
        {
            string scope = (raw ?? "").Trim().ToLowerInvariant();
            if (!MaintenanceScopes.Cache.Contains(scope)) throw new IOException("未知清理范围：" + raw);
            if (!result.Contains(scope)) result.Add(scope);
        }
        if (result.Count == 0) throw new IOException("请至少选择一个清理范围。");
        return result.OrderBy(s => s, StringComparer.Ordinal).ToList();
    }
    internal static List<string> NormalizeModFiles(IEnumerable<string>? files)
    {
        var result = new HashSet<string>(Files.Compare);
        foreach (string raw in files ?? Array.Empty<string>())
        {
            string path = (raw ?? "").Trim().Replace('\\', '/');
            if (path.Length == 0 || path.Length > 400 || path.StartsWith('/') || path.Contains(':') || path.IndexOfAny(new[] { '*', '?', '<', '>', '|', '"' }) >= 0
                || path.Split('/').Any(s => s is "" or "." or ".." || s.EndsWith(' ') || s.EndsWith('.')))
                throw new IOException("文件路径不安全：" + raw);
            if (!result.Add(path)) throw new IOException("文件清单包含重复项：" + path);
        }
        return result.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }
    static bool SameSet(IEnumerable<string> a, IEnumerable<string> b) => a.ToHashSet(Files.Compare).SetEquals(b);

    internal static string Fingerprint(MaintenancePreview p, bool withIndex)
    {
        var lines = new List<string> { "mce-maintenance-v1", p.Operation, p.PackId, withIndex ? p.IndexRevision.ToString() : "", string.Join(",", p.Scopes), string.Join(",", p.ModFiles), p.WholeCategory.ToString() };
        lines.AddRange(p.Items.OrderBy(i => i.Scope, StringComparer.Ordinal).ThenBy(i => i.Path, StringComparer.Ordinal)
            .Select(i => i.Kind + "|" + i.Scope + "|" + i.Path + "|" + i.Bytes + "|" + i.FileCount + "|" + i.Stamp));
        return Hex(string.Join("\n", lines));
    }
    static string Hex(string text) => FrameworkCompat.Hex(FrameworkCompat.HashData(Encoding.UTF8.GetBytes(text)));
    static string Digest(List<(string Rel, long Length, long Ticks)> walk, string extra) => Hex(extra + "\n" + string.Join("\n", walk.Select(f => f.Rel + "|" + f.Length + "|" + f.Ticks)));
    static string Relative(PackStorage storage, string path) => Path.GetRelativePath(storage.Root, path).Replace('\\', '/');

    /// <summary>Lists regular files below root (relative path, size, mtime). Throws if any link is found or the tree is implausibly large.</summary>
    internal static List<(string Rel, long Length, long Ticks)> Walk(string boundary, string root)
    {
        var result = new List<(string, long, long)>();
        if (!Directory.Exists(root)) return result;
        Files.RejectLinks(boundary, root);
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                Files.RejectLinks(boundary, file); var info = new FileInfo(file);
                result.Add((Path.GetRelativePath(root, file).Replace('\\', '/'), info.Length, info.LastWriteTimeUtc.Ticks));
                if (result.Count > MaxFiles) throw new IOException("目录文件数量过多，已停止：" + root);
            }
            foreach (string child in Directory.GetDirectories(directory)) { Files.RejectLinks(boundary, child); pending.Push(child); }
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return result;
    }

    static string MaintenanceDirectory(PackStorage storage)
    {
        string directory = ManagerIdentity.Runtime(storage.Root, "maintenance");
        Files.RejectLinks(Directory.GetParent(storage.Root)!.FullName, directory);
        return directory;
    }
    /// <summary>Removes only this feature's own stale records (hex-named json) from MCE/maintenance.</summary>
    static void Prune(string directory)
    {
        try
        {
            foreach (string file in Directory.GetFiles(directory, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file); bool result = name.StartsWith("result-", StringComparison.Ordinal);
                if (!PackStorage.Id(result ? name[7..] : name)) continue;
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow - (result ? TimeSpan.FromDays(7) : TimeSpan.FromDays(1))) File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
