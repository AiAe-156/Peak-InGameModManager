using System.Text.Json.Nodes;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

// Internal staging protocol. No client supplied seal, URL, route or live dependency fallback.
internal static class PackResolvedImport
{
    const string PlanName = "resolved-plan.json", SealName = "sealed-environment.json";
    internal sealed class Plan
    {
        public string Source { get; set; } = "";
        public string InputHash { get; set; } = "";
        public List<Entry> Entries { get; set; } = new();
        public string SealHash { get; set; } = "";
        public string DescriptionHash { get; set; } = "";
    }
    internal sealed class Entry
    {
        public string Key { get; set; } = "";
        public string Version { get; set; } = "";
        public string Url { get; set; } = "";
        public List<string> Dependencies { get; set; } = new();
        public string Archive { get; set; } = "";
        public string ArchiveHash { get; set; } = "";
    }
    internal static string Fingerprint(string source) => Directory.Exists(source)
        ? PackImports.PlanHash(Files.Inventory(source).Select(f => new PackDownloadItem { Key = f.Key, Version = f.Value })) : Files.Hash(source);
    static (string Key, string Version) Identity(string value)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+$")) throw new IOException("商店依赖身份无效：" + value);
        int cut = value.LastIndexOf('-'); return (value[..cut], value[(cut + 1)..]);
    }
    internal static async Task<PackImportEnvironment?> Prepare(PackStorage storage, PackTask task, PackImportEnvironment input, string content, string source, UpdateNetworkOptions network, HttpMessageHandler? handler, IReadOnlyCollection<PackDownloadItem>? approved, PackTask? previous)
    {
        input.Validate(content); // Files is exactly the inline ZIP payload, not the assembled environment.
        var plan = new Plan { Source = source, InputHash = Fingerprint(source), DescriptionHash = Files.Hash(Path.Combine(task.StagingDirectory, PackImportEnvironment.FileName)) };
        using var store = new StoreClient(network, handler);
        if (approved == null)
        {
            var pins = input.StorePackages.ToDictionary(p => p.Key, p => p.Version, Files.Compare);
            var selected = new Dictionary<string, Entry>(Files.Compare);
            var queue = new Queue<string>(input.StorePackages.Select(p => p.Key + "-" + p.Version));
            var expandedInline = new HashSet<string>(Files.Compare);
            int iterations = 0;
            while (queue.Count > 0)
            {
                if (++iterations > 4096 || selected.Count > 256) throw new IOException("依赖闭包过大或无法收敛。");
                var (key, minimum) = Identity(queue.Dequeue());
                string version = pins.TryGetValue(key, out var pin) ? pin : minimum;
                if (new Version(version) < new Version(minimum)) throw new IOException("作者固定版本低于传递依赖要求：" + key);
                // Inline infrastructure is only satisfied by actual target bytes, never the live core.
                var inline = input.Ledger.Packages.SingleOrDefault(p => Files.Compare.Equals(p.Key, key));
                if (inline != null && !PackageRules.IsLoader(key))
                {
                    if (inline.Files.Count == 0) throw new IOException("inline 组件没有文件证据：" + key);
                    if (!Version.TryParse(inline.Version, out var found) || found < new Version(minimum) || pins.ContainsKey(key) && inline.Version != version) throw new IOException("inline 与固定依赖冲突：" + key);
                    if (expandedInline.Add(key)) foreach (string dependency in inline.Dependencies) queue.Enqueue(dependency);
                    continue;
                }
                if (PackageRules.IsLoader(key))
                {
                    string dll = Path.Combine(content, "core", "BepInEx.dll");
                    if (!File.Exists(dll) || !File.Exists(Path.Combine(content, "core", "BepInEx.Preloader.dll"))) throw new IOException("随包加载器不完整：" + key);
                    using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(dll);
                    if (assembly.Name.Name != "BepInEx" || assembly.Name.Version < new Version(minimum)) throw new IOException("随包加载器版本不足：" + key);
                    continue;
                }
                if (selected.TryGetValue(key, out var old) && new Version(old.Version) >= new Version(version)) continue;
                int split = key.IndexOf('-');
                var metadata = await store.Resolve(new UpdateJob { Namespace = key[..split], Package = key[(split + 1)..] }, version, CancellationToken.None);
                var entry = new Entry { Key = key, Version = version, Url = metadata.DownloadUrl, Dependencies = metadata.Dependencies.ToList() };
                selected[key] = entry;
                foreach (var dependency in entry.Dependencies) queue.Enqueue(dependency);
            }
            plan.Entries = selected.Values.OrderBy(p => p.Key, Files.Compare).ToList();
        }
        else
        {
            if (previous == null) throw new IOException("缺少原下载预览。");
            if (!PackStorage.Id(previous.Id) || !Files.Compare.Equals(Path.GetFullPath(previous.StagingDirectory), Path.Combine(storage.Home, ".imports", previous.Id))) throw new IOException("原下载计划 staging 身份无效。");
            Files.RejectLinks(storage.Home, previous.StagingDirectory);
            var original = Files.Read<Plan>(Path.Combine(previous.StagingDirectory, PlanName));
            if (original.Source != source || original.InputHash != plan.InputHash || original.DescriptionHash != plan.DescriptionHash) throw new IOException("原 source 已变化，请重新预览并同意。");
            plan = original;
            var expected = plan.Entries.Select(p => new PackDownloadItem { Key = p.Key, Version = p.Version, Url = p.Url }).ToList();
            if (PackImports.PlanHash(expected) != previous.DownloadPlan || PackImports.PlanHash(approved) != previous.DownloadPlan) throw new IOException("已同意计划与磁盘固定计划不一致。");
        }
        Files.Save(Path.Combine(task.StagingDirectory, PlanName), plan);
        if (approved == null && plan.Entries.Count > 0)
        {
            task.PendingDownloads = plan.Entries.Select(p => new PackDownloadItem { Key = p.Key, Version = p.Version, Url = p.Url, RequiredBy = "独立环境固定闭包" }).ToList();
            task.DownloadPlan = PackImports.PlanHash(task.PendingDownloads);
            task.Report.DownloadCount = task.PendingDownloads.Count;
            task.Report.Kind = "full"; task.Report.Verdict = "attention";
            task.Report.Headline = "待同意下载 " + task.PendingDownloads.Count + " 个固定依赖；尚未下载，不能存入。";
            task.State = "preview"; return null;
        }
        foreach (var entry in plan.Entries)
        {
            entry.Archive = "dependencies/" + entry.Key + "-" + entry.Version + ".zip";
            string zip = Files.Child(task.StagingDirectory, entry.Archive);
            Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
            await store.Download(entry.Url, zip, (_, _, _) => { }, CancellationToken.None);
            entry.ArchiveHash = Files.Hash(zip);
            task.Report.Lines.Add(new PackReportLine { Section = "download", State = "ok", Group = "需下载", Key = entry.Key, Title = entry.Key + " " + entry.Version, Detail = "已按批准固定计划下载并核对实际 ZIP。" });
            string extracted = Files.Child(task.StagingDirectory, "extracted/" + entry.Key);
            PackImports.Extract(zip, extracted);
            string manifestPath = Path.Combine(extracted, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            int split = entry.Key.IndexOf('-');
            if (manifest["name"]?.GetValue<string>() != entry.Key[(split + 1)..] || manifest["version_number"]?.GetValue<string>() != entry.Version
                || manifest["namespace"] != null && manifest["namespace"]!.GetValue<string>() != entry.Key[..split]) throw new IOException("实际 ZIP 身份不匹配：" + entry.Key);
            var deps = (manifest["dependencies"]?.AsArray() ?? new JsonArray()).Select(d => d!.GetValue<string>()).ToList();
            if (!deps.OrderBy(d => d, StringComparer.Ordinal).SequenceEqual(entry.Dependencies.OrderBy(d => d, StringComparer.Ordinal))) throw new IOException("实际 ZIP 依赖与已同意元数据不符，请重新预览：" + entry.Key);
            var owned = new List<PackFile>();
            foreach (var file in Files.Inventory(extracted))
            {
                string relative = file.Key.Replace('\\', '/');
                if (!relative.Contains('/') && (relative.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || relative.Equals("icon.png", StringComparison.OrdinalIgnoreCase) || Packages.Documentation(relative))) continue;
                if (relative.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) relative = relative[8..];
                if (relative.StartsWith("PEAK_Data/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("MCE/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("monomod/", StringComparison.OrdinalIgnoreCase) || new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" }.Contains(relative, Files.Compare)) throw new IOException("依赖 ZIP 包含非环境内容：" + relative);
                relative = ShopQueue.Route(relative, entry.Key);
                if (!PackStorage.ManagedPath(relative, 2) || new[] { ".ps1", ".bat", ".cmd", ".sh" }.Any(e => relative.EndsWith(e, StringComparison.OrdinalIgnoreCase))) throw new IOException("依赖 ZIP 路径越界：" + relative);
                string target = Files.Child(content, relative); Files.RejectLinks(content, target);
                if (File.Exists(target) && Files.Hash(target) != file.Value) throw new IOException("inline/依赖文件冲突：" + relative);
                if (!File.Exists(target)) Files.CopyAtomic(Files.Child(extracted, file.Key), target);
                if (owned.Any(f => Files.Compare.Equals(f.Path, relative))) throw new IOException("依赖 ZIP 重复目标：" + relative);
                owned.Add(new PackFile { Path = relative, Hash = file.Value });
            }
            if (owned.Count == 0) throw new IOException("依赖 ZIP 无可映射环境文件：" + entry.Key);
            input.Ledger.Packages.Add(new ShopPackage { Key = entry.Key, Version = entry.Version, Url = entry.Url, Dependencies = deps, Files = owned, ArchiveHash = entry.ArchiveHash,
                PluginGuids = owned.Where(f => Packages.IsAssembly(f.Path)).SelectMany(f => Packages.ReadPlugins(Files.Child(content, f.Path))).Select(p => p.Guid).Distinct(Files.Compare).ToList() });
        }
        var sealedEnvironment = new PackImportEnvironment { SchemaVersion = 2, EnvironmentVersion = 2, MinimumWorkerProtocol = 2, Files = PackStorage.Inventory(content), Ledger = input.Ledger };
        sealedEnvironment.Validate(content);
        string seal = Path.Combine(task.StagingDirectory, SealName); Files.Save(seal, sealedEnvironment);
        plan.SealHash = Files.Hash(seal); Files.Save(Path.Combine(task.StagingDirectory, PlanName), plan);
        return sealedEnvironment;
    }
    internal static PackImportEnvironment? ReadConfirmed(PackTask task)
    {
        string path = Path.Combine(task.StagingDirectory, PlanName);
        if (!File.Exists(path)) return null;
        var plan = Files.Read<Plan>(path);
        if (Path.GetFullPath(task.Request.Source) != plan.Source || Fingerprint(plan.Source) != plan.InputHash) throw new IOException("确认前原 source 已改变，请重新预览。");
        if (Files.Hash(Path.Combine(task.StagingDirectory, PackImportEnvironment.FileName)) != plan.DescriptionHash) throw new IOException("原始环境描述已改变。");
        string seal = Path.Combine(task.StagingDirectory, SealName);
        if (plan.SealHash.Length == 0 || Files.Hash(seal) != plan.SealHash) throw new IOException("sealed 环境已改变。");
        foreach (var entry in plan.Entries)
            if (Files.Hash(Files.Child(task.StagingDirectory, entry.Archive)) != entry.ArchiveHash) throw new IOException("sealed ZIP 摘要已改变。");
        return PackImportEnvironment.Read(seal);
    }
}
