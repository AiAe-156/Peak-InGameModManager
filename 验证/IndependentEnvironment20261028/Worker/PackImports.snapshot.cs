using System.IO.Compression;
using System.Text.Json.Nodes;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal static class PackImports
{
    /// <summary>Without <paramref name="approved"/> the preview only resolves store metadata and lists what would be downloaded.
    /// With it, exactly those key|version|url entries may be downloaded; anything else aborts before the network is touched.</summary>
    internal static async Task<PackTask> Preview(PackStorage storage, PackRequest request, UpdateNetworkOptions network, HttpMessageHandler? testHandler = null, IReadOnlyCollection<PackDownloadItem>? approved = null)
    {
        var approvedSet = approved?.Select(a => a.Key + "|" + a.Version + "|" + a.Url).ToHashSet(StringComparer.Ordinal);
        var pending = new List<PackDownloadItem>();
        var task = new PackTask { Id = Guid.NewGuid().ToString("N"), State = "preparing", Request = request };
        task.StagingDirectory = Path.Combine(storage.Home, ".imports", task.Id);
        Files.Save(storage.TaskPath, task);
        string raw = Path.Combine(task.StagingDirectory, "raw"), content = Path.Combine(task.StagingDirectory, "content");
        string source = Path.GetFullPath(request.Source);
        if (Directory.Exists(source)) PackStorage.CopyTree(source, raw);
        else Extract(source, raw);
        var rawFiles = Files.Inventory(raw).Keys.ToList();
        static bool IsDemo(string path) { var seg = path.Replace((char)92, (char)47).Split((char)47); return seg.Any(s => s.Equals("Demo", StringComparison.OrdinalIgnoreCase)) && seg.Any(s => s.Equals("profile-format", StringComparison.OrdinalIgnoreCase)); }
        foreach (string demo in rawFiles.Where(IsDemo).ToList()) { File.Delete(Files.Child(raw, demo)); rawFiles.Remove(demo); }
        var single = rawFiles.Where(p => Path.GetFileName(p).Equals("profile.json", StringComparison.OrdinalIgnoreCase)).ToList();
        var releases = rawFiles.Where(p => Path.GetFileName(p).Equals("release.json", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(Path.GetDirectoryName(p))!.Equals("MCEPack", StringComparison.OrdinalIgnoreCase)).ToList();
        if (releases.Count > 1) throw new IOException("导入内容含多个整合包身份，请拆分后分别导入。");
        JsonObject? release = releases.Count == 0 ? null : JsonNode.Parse(File.ReadAllText(Files.Child(raw, releases[0])))?.AsObject();
        if (release?["schemaVersion"] is JsonNode schema && schema.GetValue<int>() != 1) throw new IOException("整合包资料版本不受支持。");
        string publication = release?["packId"]?.GetValue<string>() ?? "";
        task.SingleProfile = publication.Length == 0 && single.Count == 1 && rawFiles.All(p => !Packages.IsAssembly(p));
        if (task.SingleProfile)
        {
            var index = storage.Read();
            if (index.ActivePackId.Length == 0) throw new IOException("请先在整合包页确认接管当前环境，再追加单方案。");
            string dir = Path.GetDirectoryName(Files.Child(raw, single[0]))!;
            if (!File.Exists(Path.Combine(dir, "config-values.json"))) throw new IOException("单方案缺少 config-values.json。");
            task.Proposed = index.Packs.Single(p => p.Id == index.ActivePackId);
            task.StagingDirectory = dir;
            task.Details.Add("无独立整合包身份：确认后作为个人方案追加到当前整合包；不会切换环境。");
            task.State = "preview"; Files.Save(storage.TaskPath, task); return task;
        }
        task.Proposed = new PackRecord { Id = publication.Length == 0 ? Guid.NewGuid().ToString("N") : PackStorage.StableId(publication),
            PublicationId = publication, Name = release?["name"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(source),
            Version = release?["version"]?.GetValue<string>() ?? "", Content = "revisions/" + task.Id, ModifiedUtc = DateTime.UtcNow.ToString("O") };
        string root = raw;
        while (Directory.GetFiles(root).Length == 0 && Directory.GetDirectories(root).Length == 1)
        {
            string child = Directory.GetDirectories(root)[0];
            if (new[] { "BepInEx", "plugins", "config", "patchers", "core", "monomod", "MCEPack" }.Contains(Path.GetFileName(child), Files.Compare)) break;
            root = child;
        }
        var descriptions = Files.Inventory(root).Keys.Where(p => Path.GetFileName(p).Equals(PackImportEnvironment.FileName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (descriptions.Count > 1) throw new IOException("存在多个独立环境描述。");
        PackImportEnvironment? environment = descriptions.Count == 0 ? null : PackImportEnvironment.Read(Files.Child(root, descriptions[0]));
        if (environment != null)
        {
            if (!Files.Compare.Equals(descriptions[0], PackImportEnvironment.FileName)) throw new IOException("独立环境描述必须位于包根目录。");
            task.Proposed.EnvironmentVersion = 2; task.Proposed.MinimumWorkerProtocol = 2;
            Files.Save(Path.Combine(task.StagingDirectory, PackImportEnvironment.FileName), environment);
        }
        Route(storage, root, content, task.Proposed, "local");
        bool complete = PackEnvironmentPolicy.IsComplete(task.Proposed);
        // Each identity has one explicitly requested version. Resolve recursively before making any live writes.
        var versions = new Dictionary<string, string>(Files.Compare);
        var visiting = new HashSet<string>(Files.Compare); var done = new HashSet<string>(Files.Compare);
        var bundled = BundledPackages(content, complete ? Array.Empty<PackHostPlugin>() : request.HostPlugins);
        var corePackages = complete ? new Dictionary<string, (string Version, bool Host)>(Files.Compare) : BundledPackages(Path.Combine(storage.Root,"core"), Array.Empty<PackHostPlugin>());
        var locks = new Dictionary<string,string>(Files.Compare);
        string rootManifest=Path.Combine(root,"manifest.json");
        if(File.Exists(rootManifest)) foreach(var dep in JsonNode.Parse(File.ReadAllText(rootManifest))?["dependencies"]?.AsArray() ?? new JsonArray())
        { string value=dep?.GetValue<string>()??""; int dash=value.LastIndexOf('-'); if(dash<=0) continue; string key=value[..dash]; if(locks.TryGetValue(key,out var old)&&old!=value[(dash+1)..]) task.Proposed.Problems.Add("作者根清单版本锁冲突："+key); else locks[key]=value[(dash+1)..]; }
        using var store = new StoreClient(network, testHandler);
        void AddOnce(string section,string state,string group,string title,string detail,string key="")
        { if(!task.Report.Lines.Any(l=>l.Section==section&&l.Title==title)) task.Report.Lines.Add(new PackReportLine{Section=section,State=state,Group=group,Title=title,Detail=detail,Key=key}); }
        // Every requester is remembered so a conflict can name its sources, and the highest satisfying version wins (as in the official mod manager).
        var requirements = new Dictionary<string, List<(string By, Version Need)>>(Files.Compare);
        var chosen = new Dictionary<string, Version>(Files.Compare);
        var resolvedCache = new Dictionary<string, StoreClient.StoreVersion>(Files.Compare);
        var planned = new HashSet<string>(Files.Compare);
        static void Split(string value, out string key, out Version version)
        {
            int cut = value.LastIndexOf('-'); key = cut > 0 ? value[..cut] : "";
            version = cut > 0 && Version.TryParse(value[(cut + 1)..], out var parsed) ? parsed : new Version(0, 0);
        }
        string Who(string key, Version version) => PackReportText.Pretty(key) + " " + version;
        string Requesters(string key)
        { var top = requirements[key].Max(r => r.Need); return string.Join("；", requirements[key].Where(r => r.Need == top).Select(r => r.By + " 要求至少 " + r.Need).Distinct()); }
        async Task Plan(string value, string by)
        {
            Split(value, out string key, out Version version); int sep = key.IndexOf('-');
            if (sep <= 0 || version.Major == 0 && version.Minor == 0) return; // malformed values are reported by Dependency
            if (!requirements.TryGetValue(key, out var list)) requirements[key] = list = new();
            list.Add((by, version)); planned.Add(key);
            if (PackageRules.IsLoader(key) || bundled.ContainsKey(key) || corePackages.ContainsKey(key)) return;
            Version best = list.Max(r => r.Need)!;
            if (locks.TryGetValue(key, out var pin) && Version.TryParse(pin, out var pinned))
            { if (pinned < best) throw new IOException("作者固定版本低于传递依赖要求：" + key + " " + pinned + " < " + best + "（" + Requesters(key) + "）"); best = pinned; }
            if (chosen.TryGetValue(key, out var existing) && existing >= best) return;
            chosen[key] = best;
            string id = key + "-" + best;
            if (!resolvedCache.TryGetValue(id, out var info))
            { info = await store.Resolve(new UpdateJob { Namespace = key[..sep], Package = key[(sep + 1)..] }, best.ToString(), CancellationToken.None); resolvedCache[id] = info; }
            foreach (string child in info.Dependencies) await Plan(child, Who(key, best!));
        }
        async Task Dependency(string value)
        {
            int dash = value.LastIndexOf('-'); string key = dash > 0 ? value[..dash] : "", version = dash > 0 ? value[(dash + 1)..] : "";
            int separator = key.IndexOf('-');
            bool Part(string text) => text.Length > 0 && text.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_');
            if (separator <= 0 || !Part(key[..separator]) || !Part(key[(separator + 1)..]) || !Version.TryParse(version, out _)) throw new IOException("商店依赖格式无效：" + value);
            await Plan(value, "整合包");
            string minimum = requirements[key].Max(r => r.Need)!.ToString();
            if (chosen.TryGetValue(key, out var picked)) version = picked.ToString();
            else if (locks.TryGetValue(key, out var pinnedText)) version = pinnedText;
            if (PackageRules.IsLoader(key))
            {
                task.Details.Add("加载器要求（仅检查，不写入）：" + value);
                int before=task.Proposed.Problems.Count;
                string loaderRoot = complete ? content : storage.Root;
                string loaderPath=Path.Combine(loaderRoot,"core","BepInEx.dll");
                if(!File.Exists(loaderPath)) {task.Proposed.Problems.Add("缺少预装加载器："+value);return;}
                using var loader = Mono.Cecil.AssemblyDefinition.ReadAssembly(loaderPath);
                if(loader.Name.Name!="BepInEx"||loader.Name.Version.Major!=5||new Version(minimum).Major!=5||!File.Exists(Path.Combine(loaderRoot,"core","BepInEx.Preloader.dll"))) task.Proposed.Problems.Add("BepInEx 5 加载器不完整或主版本不兼容："+value);
                else if (!complete) task.Proposed.CoreRequirements.Add(new PackFile {Path="BepInEx/core/BepInEx.dll",Hash=Files.Hash(loaderPath)});
                return;
            }
            versions[key] = version;
            if (versions.Count > 256) throw new IOException("商店依赖超过 256 个，停止递归解析。");
            if (!visiting.Add(key)) throw new IOException("商店依赖存在循环：" + key);
            if (done.Contains(key)) { visiting.Remove(key); return; }
            if (bundled.TryGetValue(key, out var inline))
            {
                if (!Version.TryParse(inline.Version,out var inlineVersion)||inlineVersion<new Version(minimum)||locks.ContainsKey(key)&&inline.Version!=version) throw new IOException("随包本地版本与依赖要求冲突：" + key + " 本地 " + inline.Version + "，依赖 " + version);
                versions[key]=inline.Version;
                AddOnce("download","ok","随包提供",PackReportText.Pretty(key)+" "+inline.Version,"已包含在整合包内，无需下载。",key);
                visiting.Remove(key); done.Add(key); return;
            }
            if(corePackages.TryGetValue(key,out var corePackage))
            {
                if(!Version.TryParse(corePackage.Version,out var installedVersion)||installedVersion<new Version(minimum)||locks.ContainsKey(key)&&corePackage.Version!=version) throw new IOException("预装核心辅助包版本不满足："+key+" "+corePackage.Version+"，要求 "+version);
                versions[key]=corePackage.Version;
                foreach(var manifest in Files.Inventory(Path.Combine(storage.Root,"core")).Keys.Where(p=>Path.GetFileName(p).Equals("manifest.json",StringComparison.OrdinalIgnoreCase)))
                { string file=Files.Child(Path.Combine(storage.Root,"core"),manifest); var data=JsonNode.Parse(File.ReadAllText(file)); if((data?["name"]?.GetValue<string>()??"")!=key[(separator+1)..]) continue; string directory=Path.GetDirectoryName(file)!; foreach(var member in Files.Inventory(directory)) task.Proposed.CoreRequirements.Add(new PackFile {Path="BepInEx/core/"+Path.GetRelativePath(Path.Combine(storage.Root,"core"),Files.Child(directory,member.Key)).Replace('\\','/'),Hash=member.Value}); }
                task.Details.Add("需预装的核心辅助包（仅核对本机，不部署）："+key+"-"+corePackage.Version);
                AddOnce("prereq","ok","",PackReportText.Pretty(key)+" "+corePackage.Version,"本机已存在符合要求的版本，不会重复安装。");
                visiting.Remove(key); done.Add(key); return;
            }
            var job = new UpdateJob { Namespace = key[..separator], Package = key[(separator + 1)..] };
            value = key + "-" + version;
            var resolved = resolvedCache.TryGetValue(value, out var known) ? known : await store.Resolve(job, version, CancellationToken.None);
            foreach (string child in resolved.Dependencies) await Dependency(child);
            if (approvedSet == null)
            {
                pending.Add(new PackDownloadItem { Key = key, Version = version, Url = resolved.DownloadUrl });
                AddOnce("download", "warn", "需下载（待同意）", PackReportText.Pretty(key) + " " + version, "预览只读取了商店信息，同意后才会下载。", key);
                visiting.Remove(key); done.Add(key); return;
            }
            if (!approvedSet.Contains(key + "|" + version + "|" + resolved.DownloadUrl)) throw new IOException("依赖清单与已同意的预览不一致，未下载：" + key + " " + version + "。请重新预览。");
            string cache = Files.Child(Path.Combine(storage.Root, "MCE", "downloads"), value + ".zip"); Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            task.Message = "正在下载依赖：" + PackReportText.Pretty(key) + " " + version + "（已完成 " + done.Count + " 个）"; Files.Save(storage.TaskPath, task);
            await store.Download(resolved.DownloadUrl, cache, (_, _, _) => { }, CancellationToken.None);
            string extracted = Path.Combine(task.StagingDirectory, "dependencies", value);
            Extract(cache, extracted); Route(storage, extracted, content, task.Proposed, key);
            AddOnce("download","ok","需下载",PackReportText.Pretty(key)+" "+version,"已下载，存入时一并保存。",key);
            visiting.Remove(key); done.Add(key);
        }
        var manifests = Files.Inventory(raw).Keys.Where(p => Path.GetFileName(p).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (string manifest in manifests)
        {
            var data = JsonNode.Parse(File.ReadAllText(Files.Child(raw, manifest)));
            foreach (var dep in data?["dependencies"]?.AsArray() ?? new JsonArray()) if (dep != null)
            {
                try { await Dependency(dep.GetValue<string>()); }
                catch (Exception error) { visiting.Clear(); task.Proposed.Problems.Add("商店依赖未补齐：" + error.Message); }
            }
        }
        // Data-only publications can still identify pinned store dependencies, or reuse exact local package versions.
        var sources = new Dictionary<string, JsonNode>(Files.Compare);
        foreach (string path in Files.Inventory(content).Keys.Where(p => Path.GetFileName(p).Equals("sources.json", StringComparison.OrdinalIgnoreCase)))
            if (JsonNode.Parse(File.ReadAllText(Files.Child(content, path))) is JsonObject map)
                foreach (var pair in map) if (pair.Value != null) sources[pair.Key] = pair.Value;
        var available = Files.Inventory(content).Keys.Where(Packages.IsAssembly).SelectMany(p => Packages.ReadPlugins(Files.Child(content, p))).ToList();
        var installed = (complete ? new Dictionary<string, string>() : Files.Inventory(Path.Combine(storage.Root, "plugins"))).Keys.Where(Packages.IsAssembly)
            .SelectMany(p => Packages.ReadPlugins(Files.Child(Path.Combine(storage.Root, "plugins"), p))).ToList();
        foreach (string profile in Files.Inventory(content).Keys.Where(p => Path.GetFileName(p).Equals("profile.json", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var data = JsonNode.Parse(File.ReadAllText(Files.Child(content, profile)));
            foreach (var item in data?["mods"]?.AsArray() ?? new JsonArray())
            {
                string guid = item?["guid"]?.GetValue<string>() ?? "";
                if (guid.Length == 0 || available.Any(p => Files.Compare.Equals(p.Guid, guid)) || !complete && request.HostPlugins.Any(p => Files.Compare.Equals(p.Guid, guid))) continue;
                string version = item?["pluginVersion"]?.GetValue<string>() ?? item?["version"]?.GetValue<string>() ?? "";
                sources.TryGetValue(guid, out var record);
                string ns = item?["packageNamespace"]?.GetValue<string>() ?? "", name = item?["packageName"]?.GetValue<string>() ?? "", packageVersion = item?["packageVersion"]?.GetValue<string>() ?? "";
                bool local = record?["kind"]?.ToString() is "2" or "Local";
                if (ns.Length == 0) ns = record?["ns"]?.GetValue<string>() ?? "";
                if (name.Length == 0) name = record?["name"]?.GetValue<string>() ?? "";
                if (packageVersion.Length == 0) packageVersion = record?["packageVersion"]?.GetValue<string>() ?? "";
                if (!local && ns.Length > 0 && name.Length > 0 && packageVersion.Length > 0)
                {
                    try { await Dependency(ns + "-" + name + "-" + packageVersion); }
                    catch (Exception error) { visiting.Clear(); task.Proposed.Problems.Add("商店依赖未补齐：" + error.Message); }
                }
                else
                {
                    var candidates = installed.Where(p => Files.Compare.Equals(p.Guid, guid) && p.Version == version).ToList();
                    if (candidates.Count == 1)
                    {
                        string directory = Path.GetDirectoryName(candidates[0].File)!;
                        // Prefer a declared package boundary. Flat DLLs copy only that file.
                        for (var ancestor = new DirectoryInfo(directory); ancestor != null && Files.Within(Path.Combine(storage.Root, "plugins"), ancestor.FullName); ancestor = ancestor.Parent)
                            if (File.Exists(Path.Combine(ancestor.FullName, "manifest.json"))) { directory = ancestor.FullName; break; }
                        bool flat = Files.Compare.Equals(directory, Path.Combine(storage.Root, "plugins"));
                        foreach (var file in flat ? new Dictionary<string, string> { [Path.GetFileName(candidates[0].File)] = Files.Hash(candidates[0].File) } : Files.Inventory(directory))
                        {
                            string actual = Files.Child(directory, file.Key), relative = Path.GetRelativePath(storage.Root, actual).Replace('\\', '/');
                            string target = Files.Child(content, relative);
                            if (!File.Exists(target)) Files.CopyAtomic(actual, target);
                            else if (Files.Hash(target) != file.Value) task.Proposed.Problems.Add("复用本地文件存在冲突：" + relative);
                        }
                        task.Details.Add("复用本机同版本：" + guid + " " + version);
                    }
                }
                available = Files.Inventory(content).Keys.Where(Packages.IsAssembly).SelectMany(p => Packages.ReadPlugins(Files.Child(content, p))).ToList();
            }
        }
        if (pending.Count > 0)
        { PendingPreview(task, pending, requirements, done.Count); Files.Save(storage.TaskPath, task); return task; }
        if (!complete) RemoveHostCopies(storage, content, task.Proposed, request);
        var plugins = Files.Inventory(content).Keys.Where(Packages.IsAssembly).SelectMany(p => Packages.ReadPlugins(Files.Child(content, p))).ToList();
        foreach (var duplicate in plugins.GroupBy(p => p.Guid, Files.Compare).Where(g => g.Count() > 1)) task.Proposed.Problems.Add("重复插件身份：" + duplicate.Key);
        ValidateDependencies(storage, plugins, task.Proposed, complete ? Array.Empty<PackHostPlugin>() : request.HostPlugins, content);
        foreach (string profile in Files.Inventory(content).Keys.Where(p => Path.GetFileName(p).Equals("profile.json", StringComparison.OrdinalIgnoreCase)))
        {
            var data = JsonNode.Parse(File.ReadAllText(Files.Child(content, profile)));
            foreach (var item in data?["mods"]?.AsArray() ?? new JsonArray())
            {
                string guid = item?["guid"]?.GetValue<string>() ?? "";
                if (guid.Length > 0 && !plugins.Any(p => Files.Compare.Equals(p.Guid, guid)) && (complete || !request.HostPlugins.Any(p => Files.Compare.Equals(p.Guid, guid))))
                {
                    bool enabled = item?["enabled"]?.GetValue<bool>() ?? true;
                    if (enabled) task.Proposed.Problems.Add("缺少模组：" + guid);
                    else task.Details.Add("停用模组未附带：" + guid);
                }
            }
        }
        var authorRoots = Files.Inventory(content).Keys.Where(p => Path.GetFileName(p).Equals("profiles.json", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(Path.GetDirectoryName(p))!.Equals("MCEPack", StringComparison.OrdinalIgnoreCase)).Select(p => Path.GetDirectoryName(p)!.Replace('\\', '/')).ToList();
        if (authorRoots.Count > 1) task.Proposed.Problems.Add("存在多份作者资料根目录。");
        task.Proposed.AuthorRoot = authorRoots.FirstOrDefault() ?? "";
        if (task.Proposed.AuthorRoot.Length > 0) PackSignature.Verify(Files.Child(content, task.Proposed.AuthorRoot));
        // Complete publications already contain projected defaults. Never mutate signed/owned payload on import.
        if (!complete) ProjectDefaults(content, task.Proposed, release);
        else { environment!.Validate(content); task.Proposed.Problems.AddRange(PackImportTarget.Check(storage, content)); }
        task.Proposed.Problems = task.Proposed.Problems.Distinct().GroupBy(x => x.Split("（")[0]).Select(g => g.OrderBy(x => x.Length).First()).ToList();
        task.Proposed.Files = PackStorage.Inventory(content);
        task.Details.Add("整合包：" + task.Proposed.Name + " " + task.Proposed.Version);
        task.Details.Add("本地文件：" + task.Proposed.Files.Count + "；商店依赖：" + done.Count);
        task.Details.AddRange(task.Proposed.Problems);
        if (task.Proposed.AuthorRoot.Length > 0)
        {
            task.Report.Languages = PackLanguages.Scan(Path.Combine(Files.Child(content, task.Proposed.AuthorRoot), "language"));
            task.Report.GameLanguage = request.GameLanguage.Length > 0 ? request.GameLanguage : "zh-cn";
            task.Proposed.Language = PackLanguages.Choose(task.Report.Languages, task.Report.GameLanguage)?.Key ?? "";
        }
        BuildReport(task, content, plugins, request, Files.Inventory(raw).Keys.Any(Packages.IsAssembly), requirements, chosen);
        task.State = "preview"; Files.Save(storage.TaskPath, task); return task;
    }

    internal static string PlanHash(IEnumerable<PackDownloadItem> items) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
        string.Join((char)10, items.Select(p => p.Key + "|" + p.Version + "|" + p.Url).OrderBy(x => x, StringComparer.Ordinal)))));

    /// <summary>Preview that stops before any download: dependencies are only listed, and nothing can be stored until the player approves this exact list.</summary>
    static void PendingPreview(PackTask task, List<PackDownloadItem> pending, Dictionary<string, List<(string By, Version Need)>> requirements, int resolved)
    {
        foreach (var item in pending) item.RequiredBy = requirements.TryGetValue(item.Key, out var asks) ? string.Join("、", asks.Select(a => a.By).Distinct()) : "整合包";
        task.PendingDownloads = pending;
        task.DownloadPlan = PlanHash(pending);
        task.Proposed.Problems = task.Proposed.Problems.Distinct().ToList();
        var report = task.Report;
        report.Name = task.Proposed.Name; report.Version = task.Proposed.Version; report.Kind = "store";
        report.DownloadCount = pending.Count;
        report.BundledCount = report.Lines.Count(l => l.Section == "download" && l.Group.StartsWith("随包提供"));
        report.Lines.InsertRange(0, task.Proposed.Problems.Select(PackReportText.Problem));
        report.KindText = "类型：商店格式包。有 " + pending.Count + " 个依赖需要从 Thunderstore 下载；同意之前不会下载，也不会存入。";
        report.Verdict = "attention";
        report.Headline = "待同意下载 " + pending.Count + " 个依赖。点击“同意下载并补齐”后才会联网下载，下载完成会重新分析并再次预览。";
        task.Details.Add("待下载依赖（尚未下载）：" + string.Join("、", pending.Select(p => p.Key + "-" + p.Version)));
        task.Message = "";
        task.State = "preview";
    }

    static void BuildReport(PackTask task, string content, List<Packages.Plugin> plugins, PackRequest request, bool hasModFiles, Dictionary<string, List<(string By, Version Need)>> requirements, Dictionary<string, Version> chosen)
    {
        var report = task.Report; var pack = task.Proposed;
        report.Name = pack.Name; report.Version = pack.Version; report.FileCount = pack.Files.Count;
        report.DownloadCount = report.Lines.Count(l => l.Section == "download" && l.Group.StartsWith("需下载"));
        report.BundledCount = report.Lines.Count(l => l.Section == "download" && l.Group.StartsWith("随包提供"));
        report.Lines.InsertRange(0, pack.Problems.Select(PackReportText.Problem));
        foreach (var line in report.Lines.Where(l => l.Section == "download" && l.Key.Length > 0 && requirements.ContainsKey(l.Key)))
        {
            var asks = requirements[line.Key].GroupBy(a => a.By).Select(g => (By: g.Key, Need: g.Max(x => x.Need))).ToList();
            if (asks.Count < 2)
            { if (asks.Count == 1 && asks[0].By != "整合包") line.Detail += " 由 " + asks[0].By + " 使用。"; continue; }
            bool differ = asks.Select(a => a.Need).Distinct().Count() > 1;
            line.Detail += " 被 " + string.Join("、", asks.Select(a => a.By.Replace(" " + a.Need, "") + (differ ? "（要求 " + a.Need + "）" : ""))) + " 共用"
                + (differ && chosen.TryGetValue(line.Key, out var pick) ? "；版本要求不同，已采用同时满足各方的最高版本 " + pick + "。" : "。");
        }
        int fetched = report.Lines.Count(l => l.Section == "download" && l.Group.StartsWith("需下载"));
        report.Kind = fetched > 0 ? "store" : !hasModFiles ? "data" : "full";
        if (PackEnvironmentPolicy.IsComplete(pack)) report.Kind = "full";
        report.KindText = PackEnvironmentPolicy.IsComplete(pack) ? "类型：独立完整环境（environment2/protocol2），含目标 core、宿主与组件记录；游戏根 Doorstop/PEAK_Data 为平台前置条件。" : report.Kind switch {
            "data" => "类型：作者资料包（不含模组文件）。包含方案、分组、介绍和配置；所需模组使用本机已有的相同版本，缺少时按固定版本下载。",
            "store" => "类型：商店格式包。另有 " + fetched + " 个依赖需要从 Thunderstore 下载，预览和存入时需要联网。",
            _ => "类型：旧格式本地包（legacy）。不代表独立完整环境，core 与宿主仍为旧格式前置条件。" };
        var disabled = task.Details.Where(d => d.StartsWith("停用模组未附带：", StringComparison.Ordinal)).Select(d => d["停用模组未附带：".Length..]).Distinct(Files.Compare).ToList();
        var reused = task.Details.Where(d => d.StartsWith("复用本机同版本：", StringComparison.Ordinal)).Select(d => d["复用本机同版本：".Length..]).Distinct(Files.Compare).ToList();
        foreach (var plugin in plugins.OrderBy(p => p.File, Files.Compare))
        {
            string relative = Path.GetRelativePath(content, plugin.File).Replace((char)92, '/');
            var parts = relative.Split('/'); string group = parts.Length >= 3 && parts[0].Equals("plugins", StringComparison.OrdinalIgnoreCase) ? parts[1] : "";
            string title = plugin.Name.Length > 0 ? plugin.Name : Path.GetFileNameWithoutExtension(plugin.File.Replace(".dll.old", ".dll").Replace(".dll.disabled-by-mce", ".dll"));
            bool enabled = plugin.File.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            string shown = title.Replace('_', ' ') + " " + plugin.Version;
            var same = report.Lines.FirstOrDefault(l => l.Section == "mod" && l.Group == group && l.Title == shown);
            if (same != null) { if (enabled) { same.State = "ok"; same.Detail = "启用"; } continue; }
            report.Lines.Add(new PackReportLine { Section = "mod", State = enabled ? "ok" : "info", Group = group, Title = shown, Detail = enabled ? "启用" : "停用（需要时可在方案里启用）" });
        }
        foreach (string text in reused) report.Lines.Add(new PackReportLine { Section = "mod", State = "ok", Group = "使用本机已装的同版本", Title = text });
        if (disabled.Count > 0) report.Lines.Add(new PackReportLine { Section = "note", State = "info", Ignorable = true, Title = "有 " + disabled.Count + " 个停用状态的模组未随包提供", Detail = "它们在原方案中本就处于停用状态，不影响使用。", Items = disabled });
        report.ModCount = report.Lines.Count(l => l.Section == "mod"); report.EnabledCount = report.Lines.Count(l => l.Section == "mod" && l.State == "ok");
        foreach (var file in pack.Files)
        {
            string path = file.Path.Replace((char)92, '/'), lower = path.ToLowerInvariant();
            string group = lower.Contains("/mcepack/") || lower.StartsWith("plugins/mcepack/") ? "作者资料" : lower.StartsWith("config/") ? "配置文件" : lower.StartsWith("patchers/") ? "启动补丁" : "模组文件";
            report.Files.Add(new PackReportFile { Group = group, Path = path });
        }
        foreach (string manifest in pack.Files.Select(f => f.Path.Replace((char)92, '/')).Where(p => p.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                string version = JsonNode.Parse(File.ReadAllText(Files.Child(content, manifest)))?["version_number"]?.GetValue<string>() ?? "";
                if (version.Length > 0) report.FolderVersions[manifest[..^"/manifest.json".Length]] = version;
            }
            catch (Exception) { }
        }
        foreach (var plugin in plugins)
        {
            string folder = Path.GetDirectoryName(Path.GetRelativePath(content, plugin.File).Replace((char)92, '/'))?.Replace((char)92, '/') ?? "";
            if (folder.Length > 0 && !report.FolderVersions.ContainsKey(folder) && plugin.Version.Length > 0 && !plugin.File.Replace((char)92, '/').Contains("/Demo/")) report.FolderVersions[folder] = plugin.Version;
        }
        bool bad = pack.Problems.Count > 0;
        report.Verdict = bad ? "attention" : "ready";
        report.Headline = bad ? "存在需要处理的问题。仍可存入整合包库，但问题解决之前无法启用。"
            : "未发现问题。存入整合包库不会改动当前的游戏文件，启用时再选择切换。";
    }

    static Dictionary<string, (string Version, bool Host)> BundledPackages(string content, IReadOnlyList<PackHostPlugin> hosts)
    {
        var result = new Dictionary<string, (string Version, bool Host)>(Files.Compare);
        var inventory = Files.Inventory(content);
        var plugins = inventory.Keys.Where(Packages.IsAssembly).SelectMany(p => Packages.ReadPlugins(Files.Child(content, p))).ToList();
        foreach (string path in inventory.Keys.Where(p => Path.GetFileName(p).Equals("profile.json", StringComparison.OrdinalIgnoreCase)))
        {
            var data = JsonNode.Parse(File.ReadAllText(Files.Child(content, path)));
            foreach (var mod in data?["mods"]?.AsArray() ?? new JsonArray())
            {
                string guid = mod?["guid"]?.GetValue<string>() ?? "", ns = mod?["packageNamespace"]?.GetValue<string>() ?? "", name = mod?["packageName"]?.GetValue<string>() ?? "", version = mod?["packageVersion"]?.GetValue<string>() ?? "";
                if (guid.Length > 0 && ns.Length > 0 && name.Length > 0 && version.Length > 0 && plugins.Any(p => Files.Compare.Equals(p.Guid, guid)))
                    result[ns + "-" + name] = (version, hosts.Any(p => Files.Compare.Equals(p.Guid, guid)));
            }
        }
        foreach (string path in inventory.Keys.Where(p => Path.GetFileName(p).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            var data = JsonNode.Parse(File.ReadAllText(Files.Child(content, path)));
            string ns = data?["namespace"]?.GetValue<string>() ?? data?["author"]?.GetValue<string>() ?? "", name = data?["name"]?.GetValue<string>() ?? "", version = data?["version_number"]?.GetValue<string>() ?? "";
            string directory = Path.GetDirectoryName(Files.Child(content, path))!;
            // The dependency download is routed to its verified Team-Package folder. Some upstream manifests carry a stale/wrong namespace.
            string folder = Path.GetFileName(directory);
            if (name.Length > 0 && folder.EndsWith("-" + name, StringComparison.OrdinalIgnoreCase)) ns = folder[..^(name.Length + 1)];
            if (ns.Length == 0 || name.Length == 0 || version.Length == 0) continue;
            var members = plugins.Where(p => Files.Within(directory, p.File)).ToList();
            result[ns + "-" + name] = (version, members.Count>0&&members.All(p => hosts.Any(h => Files.Compare.Equals(h.Guid, p.Guid))));
        }
        foreach (string path in inventory.Keys.Where(p => Path.GetFileName(p).Equals("sources.json", StringComparison.OrdinalIgnoreCase)))
        {
            if (JsonNode.Parse(File.ReadAllText(Files.Child(content, path))) is not JsonObject records) continue;
            foreach (var pair in records)
            {
                var plugin = plugins.FirstOrDefault(p => Files.Compare.Equals(p.Guid, pair.Key)); if (plugin == null) continue;
                string ns = pair.Value?["ns"]?.GetValue<string>() ?? "", name = pair.Value?["name"]?.GetValue<string>() ?? "", version = pair.Value?["packageVersion"]?.GetValue<string>() ?? "";
                if (ns.Length == 0 || name.Length == 0) continue;
                if (version.Length == 0)
                {
                    for (var directory = new DirectoryInfo(Path.GetDirectoryName(plugin.File)!); directory != null && Files.Within(content, directory.FullName); directory = directory.Parent)
                    {
                        string manifest = Path.Combine(directory.FullName, "manifest.json"); if (!File.Exists(manifest)) continue;
                        version = JsonNode.Parse(File.ReadAllText(manifest))?["version_number"]?.GetValue<string>() ?? ""; break;
                    }
                }
                if (version.Length > 0) result[ns + "-" + name] = (version, hosts.Any(h => Files.Compare.Equals(h.Guid, pair.Key)));
            }
        }
        return result;
    }

    static void ValidateDependencies(PackStorage storage, IReadOnlyList<Packages.Plugin> plugins, PackRecord pack, IReadOnlyList<PackHostPlugin> hosts, string content)
    {
        foreach (string file in plugins.Select(p => p.File).Distinct(Files.Compare))
        {
            using var resolver = new Mono.Cecil.DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.Combine(PackEnvironmentPolicy.IsComplete(pack) ? content : storage.Root, "core")); resolver.AddSearchDirectory(Path.GetDirectoryName(file)!);
            resolver.AddSearchDirectory(Path.Combine(Directory.GetParent(storage.Root)!.FullName, "PEAK_Data", "Managed"));
            using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(file, new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });
            foreach (var type in assembly.MainModule.Types)
            foreach (var attribute in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInDependency" && a.ConstructorArguments.Count > 0))
            {
                string guid = attribute.ConstructorArguments[0].Value?.ToString() ?? "", minimum = ""; bool optional = false;
                if (attribute.ConstructorArguments.Count > 1)
                {
                    object value = attribute.ConstructorArguments[1].Value;
                    if (value is string version) minimum = version; else optional = (Convert.ToInt32(value) & 2) != 0;
                }
                if (optional) continue;
                var host = hosts.FirstOrDefault(h => Files.Compare.Equals(h.Guid, guid));
                string? actual = host?.Version ?? plugins.FirstOrDefault(p => Files.Compare.Equals(p.Guid, guid))?.Version;
                if (actual == null) pack.Problems.Add("缺少必需插件依赖：" + guid);
                else if (minimum.Length > 0 && (!Version.TryParse(actual, out var found) || !Version.TryParse(minimum, out var wanted) || found < wanted)) pack.Problems.Add("插件依赖版本不足：" + guid + " 要求 " + minimum);
                if (host != null && minimum.Length > 0) pack.Requirements.Add(new PackHostPlugin { Guid = guid, Version = minimum });
            }
        }
    }

    static void ProjectDefaults(string content, PackRecord pack, JsonObject? release)
    {
        if (pack.AuthorRoot.Length == 0) return;
        string root = Files.Child(content, pack.AuthorRoot), manifest = Path.Combine(root, "profiles.json");
        var data = JsonNode.Parse(File.ReadAllText(manifest));
        var profiles = data is JsonArray array ? array : data?["profiles"]?.AsArray();
        string selected = release?["defaultProfile"]?.GetValue<string>() ?? "";
        var profile = profiles?.FirstOrDefault(p => p?["id"]?.GetValue<string>() == selected) ?? (selected.Length == 0 ? profiles?.FirstOrDefault() : null);
        if (profile == null) { if (selected.Length > 0) pack.Problems.Add("找不到整合包指定的默认方案：" + selected); return; }
        string relative = profile["path"]?.GetValue<string>() ?? profile["directory"]?.GetValue<string>() ?? "profiles/" + profile["id"]!.GetValue<string>();
        string profileRoot = Files.Child(root, relative);
        var state = JsonNode.Parse(File.ReadAllText(Path.Combine(profileRoot, "profile.json")));
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(profileRoot, "config-values.json")));
        if (state?["schemaVersion"]?.GetValue<int>() != 1 || config?["schemaVersion"]?.GetValue<int>() != 1) throw new IOException("默认方案或配置快照版本无效。");
        var targets = new Dictionary<string, bool>(Files.Compare);
        foreach (var mod in state?["mods"]?.AsArray() ?? new JsonArray())
        {
            string guid = mod?["guid"]?.GetValue<string>() ?? "";
            if (guid.Length > 0) targets[guid] = mod?["enabled"]?.GetValue<bool>() ?? true;
        }
        foreach (string file in Files.Inventory(Path.Combine(content, "plugins")).Keys.Where(Packages.IsAssembly).ToArray())
        {
            string full = Files.Child(Path.Combine(content, "plugins"), file);
            var goals = Packages.ReadPlugins(full).Where(p => targets.ContainsKey(p.Guid)).Select(p => targets[p.Guid]).Distinct().ToList();
            if (goals.Count > 1) { pack.Problems.Add("同一 DLL 中的模组启停目标冲突：" + file); continue; }
            if (goals.Count == 0) continue;
            string canonical = full.EndsWith(".dll.old", StringComparison.OrdinalIgnoreCase) ? full[..^4] : full.EndsWith(".dll.disabled-by-mce", StringComparison.OrdinalIgnoreCase) ? full[..^16] : full;
            string target = canonical + (goals[0] ? "" : ".old");
            if (!Files.Compare.Equals(full, target))
            { if (File.Exists(target)) pack.Problems.Add("默认启停目标重名：" + file); else File.Move(full, target); }
        }
        var grouped = (config?["values"]?.AsArray() ?? new JsonArray()).Where(p => p != null).GroupBy(p => p!["configFile"]?.GetValue<string>() ?? "", Files.Compare);
        foreach (var group in grouped)
        {
            if (PackStorage.GlobalConfig(group.Key) || !group.Key.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)) continue;
            string target = Files.Child(content, "config/" + group.Key);
            var updates = new Dictionary<(string Section, string Key), string>();
            foreach (var section in group.GroupBy(p => p!["section"]?.GetValue<string>() ?? ""))
            {
                if (section.Key.IndexOfAny(new[] { '\r', '\n', ']' }) >= 0) throw new IOException("配置分区含无效字符。");
                foreach (var item in section)
                {
                    string key = item!["key"]?.GetValue<string>() ?? "", value = item["serializedValue"]?.GetValue<string>() ?? "";
                    if (key.Length == 0 || key.IndexOfAny(new[] { '\r', '\n', '=' }) >= 0 || value.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new IOException("配置项含无效换行或名称。");
                    if (!updates.TryAdd((section.Key, key), value)) throw new IOException("配置快照包含重复配置项。");
                }
            }
            var lines = new List<string>(); string currentSection = "";
            foreach (string line in File.Exists(target) ? File.ReadAllLines(target) : Array.Empty<string>())
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) currentSection = trimmed[1..^1];
                int equals = trimmed.IndexOf('=');
                var identity = (currentSection, equals < 0 ? "" : trimmed[..equals].Trim());
                if (!trimmed.StartsWith('#') && equals > 0 && updates.Remove(identity, out var wanted)) lines.Add(identity.Item2 + " = " + wanted);
                else lines.Add(line);
            }
            foreach (var section in updates.GroupBy(p => p.Key.Section))
            { lines.Add(""); lines.Add("[" + section.Key + "]"); foreach (var value in section) lines.Add(value.Key.Key + " = " + value.Value); }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllLines(target, lines);
        }
    }

    internal static void Extract(string zip, string target)
    {
        using var archive = ZipFile.OpenRead(zip); long total = 0; var seen = new HashSet<string>(Files.Compare);
        if (archive.Entries.Count > 50000) throw new IOException("整合包文件数超过 50000。");
        foreach (var entry in archive.Entries)
        {
            string path = entry.FullName.Replace('\\', '/'); if (path.EndsWith('/')) continue;
            string file = Files.Child(target, path);
            if (!seen.Add(path) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0) throw new IOException("整合包含重复路径或链接：" + path);
            total += entry.Length;
            if (total > 8L * 1024 * 1024 * 1024 || entry.Length > 2L * 1024 * 1024 * 1024) throw new IOException("整合包解压大小超过限额。");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!); using var input = entry.Open(); using var output = new FileStream(file, FileMode.CreateNew); input.CopyTo(output);
        }
    }

    static void Route(PackStorage storage, string source, string target, PackRecord pack, string package)
    {
        foreach (var file in Files.Inventory(source))
        {
            string relative = file.Key.Replace('\\', '/'); string lower = relative.ToLowerInvariant();
            if (PackEnvironmentPolicy.IsComplete(pack))
            {
                if (relative.Equals(PackImportEnvironment.FileName, StringComparison.OrdinalIgnoreCase) || Packages.Documentation(relative) && !relative.Contains('/')) continue;
                if (lower.StartsWith("bepinex/")) relative = relative[8..];
                if (!PackStorage.ManagedPath(relative, pack) || relative.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || relative.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("完整环境含越界或脚本内容：" + relative);
                string targetFile = Files.Child(target, relative);
                Files.RejectLinks(target, targetFile);
                if (File.Exists(targetFile)) { if (Files.Hash(targetFile) != file.Value) throw new IOException("完整环境目标冲突：" + relative); }
                else Files.CopyAtomic(Files.Child(source, file.Key), targetFile);
                continue;
            }
            bool explicitRoot = lower.StartsWith("bepinex/");
            if (explicitRoot) relative = relative[8..];
            lower = relative.ToLowerInvariant();
            if (lower.StartsWith("translation/")) continue;
            string leaf = Path.GetFileName(relative);
            if (leaf.Equals("BepInEx.dll", StringComparison.OrdinalIgnoreCase) || leaf.Equals("0Harmony.dll", StringComparison.OrdinalIgnoreCase)
                || !lower.StartsWith("patchers/") && leaf.StartsWith("BepInEx.", StringComparison.OrdinalIgnoreCase) && leaf.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            { relative = "core/" + leaf; lower = relative.ToLowerInvariant(); }
            else if (leaf.Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
            { relative = "PEAK_Data/Managed/" + leaf; lower = relative.ToLowerInvariant(); }
            if (lower.StartsWith("core/") || lower == "winhttp.dll" || lower == "doorstop_config.ini" || lower == ".doorstop_version" || lower.StartsWith("peak_data/"))
            {
                string actual = lower.StartsWith("core/") ? Files.Child(storage.Root, relative) : Files.Child(Directory.GetParent(storage.Root)!.FullName, relative);
                pack.CoreRequirements.Add(new PackFile { Path = lower.StartsWith("core/") ? "BepInEx/" + relative : relative, Hash = file.Value });
                if (!File.Exists(actual) || Files.Hash(actual) != file.Value) pack.Problems.Add("核心兼容检查未通过（不会写入）：" + relative);
                continue;
            }
            if (lower.StartsWith("monomod/") || lower.EndsWith(".mm.dll") || lower.EndsWith(".bat") || lower.EndsWith(".ps1") || lower.EndsWith(".cmd"))
            { pack.Problems.Add("需人工安装的不支持内容：" + relative); continue; }
            if (lower.StartsWith("mce/")) { pack.Problems.Add("含本机运行数据：" + relative); continue; }
            if (explicitRoot && !new[] { "plugins/", "config/", "patchers/" }.Any(p => lower.StartsWith(p, StringComparison.Ordinal)))
            { pack.Problems.Add("BepInEx 下的额外内容需要人工归类：" + relative); continue; }
            if (lower.StartsWith("config/") && PackStorage.GlobalConfig(relative[7..])) continue;
            if (!lower.StartsWith("plugins/") && !lower.StartsWith("config/") && !lower.StartsWith("patchers/"))
                relative = lower.StartsWith("mcepack/") ? "plugins/" + relative : "plugins/" + package + "/" + relative;
            string destination = Files.Child(target, relative);
            if (File.Exists(destination))
            { if (Files.Hash(destination) != file.Value) pack.Problems.Add("文件目标冲突：" + relative); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); Files.CopyAtomic(Files.Child(source, file.Key), destination);
        }
    }

    static void RemoveHostCopies(PackStorage storage, string content, PackRecord pack, PackRequest request)
    {
        var inventory = Files.Inventory(content);
        var plugins = inventory.Keys.Where(Packages.IsAssembly).SelectMany(p => Packages.ReadPlugins(Files.Child(content, p))).ToList();
        var removed = new HashSet<string>(Files.Compare);
        foreach (var plugin in plugins)
        {
            var host = request.HostPlugins.FirstOrDefault(h => Files.Compare.Equals(h.Guid, plugin.Guid)); if (host == null) continue;
            pack.Requirements.Add(new PackHostPlugin { Guid = plugin.Guid, Version = plugin.Version });
            if (!Version.TryParse(host.Version, out var actual) || !Version.TryParse(plugin.Version, out var wanted) || actual < wanted)
                pack.Problems.Add("宿主版本不足：" + plugin.Guid + " 要求 " + plugin.Version + "，本机 " + host.Version);
            string directory = Path.GetDirectoryName(plugin.File)!;
            for (var ancestor = new DirectoryInfo(directory); ancestor != null && Files.Within(Path.Combine(content, "plugins"), ancestor.FullName); ancestor = ancestor.Parent)
                if (File.Exists(Path.Combine(ancestor.FullName, "manifest.json"))) { directory = ancestor.FullName; break; }
            if (plugins.Any(p => Files.Within(directory, p.File) && request.HostPlugins.All(h => !Files.Compare.Equals(h.Guid, p.Guid))))
            { pack.Problems.Add("宿主与其他模组混放，文件归属需人工确认：" + directory); continue; }
            foreach (string path in inventory.Keys.Where(p => Files.Within(directory, Files.Child(content, p)))) removed.Add(path);
        }
        foreach (string path in removed) File.Delete(Files.Child(content, path)); // only disposable import staging, never installed files
    }

    internal static void Commit(PackStorage storage, PackTask task)
    {
        if (task.PendingDownloads.Count > 0) throw new IOException("还有依赖等待同意下载，请先点击“同意下载并补齐”。");
        task.Proposed.Report = task.Report;
        if (task.SingleProfile) throw new IOException("单方案由游戏内方案服务导入。");
        var index = storage.Read();
        if (!Files.Compare.Equals(Path.GetFullPath(task.StagingDirectory), Path.Combine(storage.Home, ".imports", task.Id)) || task.Proposed.Content != "revisions/" + task.Id) throw new IOException("整合包准备目录与预览身份不匹配。");
        Files.RejectLinks(storage.Home, task.StagingDirectory);
        string prepared = Path.Combine(task.StagingDirectory, "content");
        var actual = PackStorage.Inventory(prepared);
        if (!actual.Select(f => f.Path + "|" + f.Hash).ToHashSet(Files.Compare).SetEquals(task.Proposed.Files.Select(f => f.Path + "|" + f.Hash))) throw new IOException("预览后导入文件发生变化，请重新预览。");
        PackImportEnvironment? environment = null;
        if (PackEnvironmentPolicy.IsComplete(task.Proposed))
        {
            if (task.Proposed.Problems.Count > 0) throw new IOException("完整环境尚有未满足条件，不能伪造完整库存版本。");
            environment = PackImportEnvironment.Read(Path.Combine(task.StagingDirectory, PackImportEnvironment.FileName));
            environment.Validate(prepared);
            var issues = PackImportTarget.Check(storage, prepared);
            if (issues.Count > 0) throw new IOException(string.Join("\n", issues));
        }
        string destination = storage.Content(task.Proposed);
        if (Directory.Exists(destination)) throw new IOException("库存版本已存在。");
        PackStorage.CopyTree(Path.Combine(task.StagingDirectory, "content"), destination);
        // State only follows the payload at its FINAL revision path; never read the live ledger here.
        if (environment != null)
        {
            new PackRevisionState(storage).Write(task.Proposed, environment.Ledger);
            storage.Validate(task.Proposed);
        }
        if (index.ActivePackId == task.Proposed.Id) index.Packs.Single(p => p.Id == task.Proposed.Id).PendingUpdate = task.Proposed;
        else { index.Packs.RemoveAll(p => p.Id == task.Proposed.Id); index.Packs.Add(task.Proposed); }
        storage.Save(index);
        task.State = "completed"; task.Message = index.ActivePackId == task.Proposed.Id ? "当前包的更新已存入库存，可预览并确认退出后应用。" : "整合包已存入库存。"; Files.Save(storage.TaskPath, task);
    }
}
