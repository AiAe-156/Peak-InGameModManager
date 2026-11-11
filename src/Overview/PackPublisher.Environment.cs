#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BepInEx;

namespace ModConfigEnhance.Overview;

internal static partial class PackPublisher
{
    internal const string EnvironmentFile = "MCEEnvironment.json";
    // Local now means a self-contained BepInEx environment. Data/store formats stay legacy.
    private static Dictionary<string, string> EnvironmentPlan(PublishRequest request, IReadOnlyList<OverviewMod> mods, OverviewProfile defaults)
    {
        var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var goals = defaults.mods.GroupBy(m => m.guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().enabled, StringComparer.OrdinalIgnoreCase);
        void Add(string source, string relative, bool project)
        {
            relative = relative.Replace('\\', '/');
            if (!PackEnvironmentPolicy.ManagedPath(relative, 2)) throw new IOException("发布路径不属于独立环境：" + relative);
            if (new[] { ".ps1", ".bat", ".cmd", ".sh" }.Any(e => relative.EndsWith(e, StringComparison.OrdinalIgnoreCase))) throw new IOException("独立环境不附带脚本：" + relative);
            bool tool = relative.IndexOf("/Tools/Updater/", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!tool && !KeepCopiedFile(source, relative)) return;
            if (project) relative = ProjectPluginRelative(source, relative, mods, goals);
            if (plan.TryGetValue(relative, out var previous) && !previous.Equals(source, StringComparison.OrdinalIgnoreCase)) throw new IOException("发布文件目标冲突：" + relative);
            plan[relative] = source;
        }
        void Tree(string source, string prefix, bool project)
        {
            if (File.Exists(source)) { Add(source, prefix, project); return; }
            foreach (string file in OverviewRepository.SafeFiles(source))
            {
                RejectPublicationLinks(source, file);
                if (file.EndsWith(".dll.old", StringComparison.OrdinalIgnoreCase) && File.Exists(file.Substring(0, file.Length - 4))) continue;
                Add(file, prefix.TrimEnd('/') + "/" + Relative(source, file), project);
            }
        }
        foreach (var package in mods.GroupBy(m => m.PackageRoot, StringComparer.OrdinalIgnoreCase))
        {
            string source = PackageSource(package.First());
            Tree(source, PublicationRelative(source), true);
        }
        // Loader and third-party core libraries are captured independently of the old IncludeBootstrap checkbox.
        Tree(Path.Combine(Paths.BepInExRootPath, "core"), "core", false);
        Tree(Path.Combine(Paths.BepInExRootPath, "patchers"), "patchers", false);
        foreach (string file in OverviewRepository.SafeFiles(Paths.ConfigPath))
        {
            string relative = "config/" + Relative(Paths.ConfigPath, file).Replace('\\', '/');
            if (PackEnvironmentPolicy.ManagedPath(relative, 2)) Add(file, relative, false);
        }
        foreach (string extra in request.ExtraFiles)
        {
            string relative = extra.Replace('\\', '/');
            if (!relative.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) throw new IOException("Doorstop/游戏根文件为平台前置条件，不属于独立环境：" + extra);
            Tree(OverviewProfileAssets.CheckedPath(GameRoot, extra), relative.Substring(8), false);
        }
        foreach (string name in new[] { "BepInEx.dll", "BepInEx.Preloader.dll", "0Harmony.dll" })
            if (!plan.ContainsKey("core/" + name)) throw new IOException("完整发布缺少 core/" + name);
        if (!mods.Any(m => m.Guid == Plugin.PluginGuid && m.AssemblyPaths.Any(File.Exists))) throw new IOException("完整发布缺少 MCE 宿主。");
        foreach (var entry in plan) RejectPublicationLinks(Paths.BepInExRootPath, entry.Value);
        ValidateEnvironmentAssemblies(plan);
        return plan;
    }
    private static void ValidateEnvironmentAssemblies(Dictionary<string, string> plan)
    {
        var assemblies = new List<(string Name, string Token, Version Version, List<Mono.Cecil.AssemblyNameReference> References)>();
        foreach (var item in plan.Where(p => p.Key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(item.Value);
                assemblies.Add((assembly.Name.Name, PublicationToken(assembly.Name), assembly.Name.Version, assembly.MainModule.AssemblyReferences.ToList()));
            }
            catch (BadImageFormatException) { }
        }
        foreach (var group in assemblies.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) throw new IOException("目标程序集同名冲突：" + group.Key);
        foreach (var owner in assemblies)
        foreach (var reference in owner.References)
        {
            if (assemblies.Any(a => a.Name.Equals(reference.Name, StringComparison.OrdinalIgnoreCase) && a.Token == PublicationToken(reference) && a.Version >= reference.Version)) continue;
            if (reference.Name == "mscorlib" || reference.Name == "netstandard") continue;
            string platform = OverviewProfileAssets.CheckedPath(Paths.ManagedPath, reference.Name + ".dll");
            if (!reference.Name.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase) && File.Exists(platform))
            {
                RejectPublicationLinks(Paths.ManagedPath, platform);
                using var provided = Mono.Cecil.AssemblyDefinition.ReadAssembly(platform);
                if (provided.Name.Name == reference.Name && PublicationToken(provided.Name) == PublicationToken(reference) && provided.Name.Version >= reference.Version) continue;
            }
            throw new IOException("完整发布缺少目标程序集：" + owner.Name + " -> " + reference.Name);
        }
    }
    private static string PublicationToken(Mono.Cecil.AssemblyNameReference name) => BitConverter.ToString(name.PublicKeyToken ?? Array.Empty<byte>());
    // Publication always captures LIVE payload + LIVE workspace ledger, never a saved revision's ledger.
    // A retained UnsavedEnvironment is not necessarily the running version either.
    internal static string PublicationSourceLabel() => Loc.F(
        "Live environment · Active modpack: {0} · Saved revision: {1}. Apply or save configuration to the running files first. Publication uses the live environment.",
        "当前运行环境 · 活动包：{0} · 已保存版本：{1}。请先将配置应用或保存到运行文件；发布时使用当前运行环境。",
        "目前執行環境 · 活動包：{0} · 已儲存版本：{1}。請先將設定套用或儲存至執行檔案；發布時使用目前執行環境。",
        PackLibrary.Active?.Name ?? Loc.T("Unmanaged", "未接管", "未接管"),
        PackLibrary.Active?.Content ?? Loc.T("None", "无", "無"));

    private static void ValidatePublicationProtocol(string content)
    {
        var hosts = OverviewRepository.SafeFiles(content).Where(p => Path.GetFileName(p).Equals("ModConfigEnhance.dll", StringComparison.OrdinalIgnoreCase)).ToList();
        if (hosts.Count != 1) throw new IOException("完整发布必须包含唯一 MCE protocol2 宿主。");
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(hosts[0]);
        var metadata = assembly.CustomAttributes.SingleOrDefault(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute"
            && a.ConstructorArguments.Count == 2 && a.ConstructorArguments[0].Value?.ToString() == "MCE.WorkerProtocol");
        if (metadata == null || metadata.ConstructorArguments[1].Value?.ToString() != "2") throw new IOException("目标 MCE 未声明 worker protocol2；产品版本不能代替管理协议。");
        WorkerProtocol.ValidateTools(Path.Combine(Path.GetDirectoryName(hosts[0])!, "Tools", "Updater"));
    }
    private static ShopLedger PublicationLedger(List<PackFile> files, PublishRequest request, OverviewProfile defaults, IReadOnlyList<OverviewMod> mods)
    {
        string path = PackLibrary.Managed ? Path.Combine(PackLibrary.Workspace, "shop-installed.json")
            : Path.Combine(Paths.BepInExRootPath, "MCE", "shop", "installed-unmanaged.json");
        if (!File.Exists(path))
        {
            if (PackLibrary.Active != null && PackEnvironmentPolicy.IsComplete(PackLibrary.Active)) throw new IOException("当前完整环境的 workspace 组件记录缺失；不能借 saved 状态发布。");
            return new ShopLedger(); // no managed ownership has ever been established
        }
        RejectPublicationLinks(Paths.BepInExRootPath, path);
        var source = JsonConvert.DeserializeObject<ShopLedger>(File.ReadAllText(path)) ?? throw new IOException("运行组件记录为空。");
        if (source.SchemaVersion != 1 || source.Revision < 0 || source.Packages == null) throw new IOException("运行组件记录无效。");
        var result = new ShopLedger { Revision = source.Revision };
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var goals = defaults.mods.GroupBy(m => m.guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().enabled, StringComparer.OrdinalIgnoreCase);
        foreach (var package in source.Packages)
        {
            if (package == null || string.IsNullOrWhiteSpace(package.Key) || !keys.Add(package.Key) || package.Files == null || package.Dependencies == null || package.PluginGuids == null) throw new IOException("运行组件记录无效。");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var projected = new List<PackFile>(); int omitted = 0;
            foreach (var owned in package.Files)
            {
                if (owned == null || !PackEnvironmentPolicy.TryNormalize(owned.Path, out string relative) || !PackEnvironmentPolicy.ManagedPath(relative, 2) || !seen.Add(relative)) throw new IOException("组件归属路径无效或重复。");
                string live = OverviewProfileAssets.CheckedPath(Paths.BepInExRootPath, relative);
                RejectPublicationLinks(Paths.BepInExRootPath, live);
                if (!File.Exists(live) || !PublicationHash(live).Equals(owned.Hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("运行组件归属与 live 不一致，请先保存/核对组件状态：" + owned.Path);
                // Enablement projection is an intentional rename; configuration projection is an intentional byte change.
                string outputPath = ProjectPluginRelative(live, relative, mods, goals);
                var output = files.SingleOrDefault(f => f.Path.Equals(outputPath, StringComparison.OrdinalIgnoreCase));
                if (output == null) { omitted++; continue; }
                bool configProjection = relative.StartsWith("config/", StringComparison.OrdinalIgnoreCase) && ProfileConfigFiles(defaults.config).Contains(relative.Substring(7), StringComparer.OrdinalIgnoreCase);
                if (!configProjection && !output.Hash.Equals(owned.Hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("发布草稿改变组件归属文件：" + owned.Path);
                projected.Add(new PackFile { Path = output.Path, Hash = output.Hash });
            }
            if (omitted == package.Files.Count && omitted > 0) continue;
            if (omitted > 0) throw new IOException("发布仅包含组件的部分文件：" + package.Key);
            var copy = JsonConvert.DeserializeObject<ShopPackage>(JsonConvert.SerializeObject(package))!;
            copy.Files = projected; result.Packages.Add(copy);
        }
        return result;
    }
    private static void RejectPublicationLinks(string root, string file)
    {
        string boundary = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(file).StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) throw new IOException("发布文件越界：" + file);
        for (var item = new FileInfo(file) as FileSystemInfo; item != null; item = Directory.GetParent(item.FullName))
        {
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("发布文件不能经过链接：" + file);
            if (item.FullName.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
    }
    private static string PublicationHash(string path)
    {
        using var hash = SHA256.Create(); using var input = File.OpenRead(path);
        return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "");
    }
    private static List<string> ValidateEnvironmentPreview(PublishRequest request, IReadOnlyList<OverviewMod> mods, OverviewProfile profile)
    {
        var defaults = JsonConvert.DeserializeObject<OverviewProfile>(JsonConvert.SerializeObject(profile))!;
        PublicationDrafts.ApplyConfigEdits(request, defaults.config);
        string work = Path.Combine(Loc.RuntimeDirectory, "publication-previews", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            foreach (var entry in EnvironmentPlan(request, mods, defaults))
                CopyFile(entry.Value, OverviewProfileAssets.CheckedPath(Path.Combine(work, "BepInEx"), entry.Key), true);
            foreach (string relative in request.ExtraConfigFiles)
            {
                if (!ExtraConfigAllowed(relative) || ProfileConfigFiles(defaults.config).Contains(relative.Replace((char)92, (char)47), StringComparer.OrdinalIgnoreCase)) throw new IOException("Additional config overlaps profile/private data.");
                CopyFile(OverviewProfileAssets.CheckedPath(Paths.ConfigPath, relative), OverviewProfileAssets.CheckedPath(Path.Combine(work, "BepInEx", "config"), relative), true);
            }
            ProjectConfig(Path.Combine(work, "BepInEx", "config"), defaults.config);
            PublicationDrafts.Apply(request, work, "", skipPrefix: "BepInEx/plugins/" + request.Name + "/MCEPack/");
            WriteEnvironmentDescription(work, request, defaults, mods);
            return OverviewRepository.SafeFiles(Path.Combine(work, "BepInEx")).Select(f => "BepInEx/" + Relative(Path.Combine(work, "BepInEx"), f).Replace((char)92, (char)47)).ToList();
        }
        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
    }
    private static void WriteEnvironmentDescription(string output, PublishRequest request, OverviewProfile defaults, IReadOnlyList<OverviewMod> mods)
    {
        string content = Path.Combine(output, "BepInEx");
        var files = OverviewRepository.SafeFiles(content).Select(file => new PackFile { Path = Relative(content, file).Replace('\\', '/'), Hash = PublicationHash(file) }).OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Any(f => !PackEnvironmentPolicy.ManagedPath(f.Path, 2) || new[] { ".ps1", ".bat", ".cmd", ".sh" }.Any(e => f.Path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))) throw new IOException("完整发布含非环境文件。");
        ValidatePublicationProtocol(content);
        var ledger = PublicationLedger(files, request, defaults, mods);
        var roots = new JArray();
        if (request.Mode == PublishMode.ThunderstoreComplete)
        {
            var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var externalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in mods.GroupBy(m => m.PackageRoot, StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                // The management host/tools and platform loader remain verified inline infrastructure.
                if (group.Any(m => m.Guid == Plugin.PluginGuid) || IsInline(request, first)) continue;
                string dependency = request.Dependencies.TryGetValue(group.Key, out var chosen) ? chosen : Dependency(first);
                if (!ValidDependency(dependency)) throw new IOException("组件无法唯一映射为固定商店依赖，请明确内置：" + first.Name);
                int cut = dependency.LastIndexOf('-'); string key = dependency.Substring(0, cut), version = dependency.Substring(cut + 1);
                if (!externalKeys.Add(key)) throw new IOException("多个组件目录映射到同一商店身份，请明确内置：" + key);
                var component = ledger.Packages.SingleOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                List<PackFile> owned;
                if (component != null)
                {
                    if (component.Version != version) throw new IOException("组件记录与发布固定版本不一致：" + key);
                    owned = component.Files;
                }
                else
                {
                    string source = PackageSource(first);
                    string prefix = PublicationRelative(source).Replace((char)92, (char)47);
                    if (!File.Exists(source) && !Directory.Exists(source)) throw new IOException("组件来源缺失，请明确内置：" + key);
                    owned = files.Where(f => File.Exists(source) ? f.Path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                        : f.Path.StartsWith(prefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (owned.Count == 0 || group.Any(m => m.SourceKey != key)) throw new IOException("组件无法按 package route 唯一映射，请明确内置：" + key);
                }
                if (owned.Count == 0 || owned.Any(f => f.Path.IndexOf("/Tools/Updater/", StringComparison.OrdinalIgnoreCase) >= 0
                    || new[] { "core/BepInEx.dll", "core/BepInEx.Preloader.dll", "core/0Harmony.dll" }.Contains(f.Path, StringComparer.OrdinalIgnoreCase))) throw new IOException("组件含必须 inline 的管理/加载器文件，请内置：" + key);
                if (owned.Any(f => PluginFileNames.IsDisabled(f.Path)
                    || f.Path.StartsWith("config/", StringComparison.OrdinalIgnoreCase))) throw new IOException("组件包含作者启停/配置投影，固定商店 ZIP 无法保留该投影，请明确内置：" + key);
                foreach (var file in owned)
                {
                    removed.Add(file.Path);
                }
                roots.Add(new JObject { ["Key"] = key, ["Version"] = version });
            }
            foreach (var inline in ledger.Packages.Where(p => !externalKeys.Contains(p.Key)))
                if (inline.Files.Any(f => removed.Contains(f.Path))) throw new IOException("外部组件与 inline 组件共享文件，请明确内置：" + inline.Key);
            foreach (string relative in removed) File.Delete(OverviewProfileAssets.CheckedPath(content, relative));
            files.RemoveAll(f => removed.Contains(f.Path));
            ledger.Packages.RemoveAll(p => externalKeys.Contains(p.Key));
        }
        OverviewRepository.AtomicWrite(Path.Combine(output, EnvironmentFile), new JObject {
            ["SchemaVersion"] = 2, ["EnvironmentVersion"] = 2, ["MinimumWorkerProtocol"] = 2,
            ["AssemblyMode"] = roots.Count > 0 ? "resolved" : "embedded", ["StorePackages"] = roots,
            ["Files"] = JArray.FromObject(files), ["Ledger"] = JObject.FromObject(ledger)
        }.ToString(Formatting.Indented));
    }
}
