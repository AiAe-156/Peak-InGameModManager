#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;
internal enum PublishMode { Local, Thunderstore, Data, ThunderstoreComplete }
internal sealed class PublishRequest
{
    internal PublishMode Mode;
    internal string Name = "", DisplayName = "", Version = "1.0.0", Description = "", Website = "", DefaultProfile = "", Icon = "", OutputDirectory = "";
    internal bool Sign = true, IncludeBootstrap = true;
    /// <summary>1.1.4: complete local packs also write a "newcomer" ZIP with the BepInEx loader files and an install note.</summary>
    internal bool NewcomerZip = true;
    internal string? SigningKeyPath = null;
    internal string LoaderDependency = "BepInEx-BepInExPack_PEAK-5.4.75301";
    internal List<string> Profiles = new(), Languages = new();
    internal HashSet<string> ExtraFiles = new(StringComparer.OrdinalIgnoreCase), InlineRoots = new(StringComparer.OrdinalIgnoreCase);
    internal HashSet<string> ExtraConfigFiles = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, string> Dependencies = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, string> DraftFiles = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string,string> ExcludedExtras = new(StringComparer.OrdinalIgnoreCase);
    internal PackConflictFile Conflicts = new();
    internal PublishRequest Snapshot() => new() { Mode = Mode, Name = Name, DisplayName = DisplayName, Version = Version, Description = Description, Website = Website, DefaultProfile = DefaultProfile, Icon = Icon,
        OutputDirectory = OutputDirectory, SigningKeyPath = SigningKeyPath, Sign = Sign, IncludeBootstrap = IncludeBootstrap, NewcomerZip = NewcomerZip, LoaderDependency = LoaderDependency, Profiles = new(Profiles), Languages = new(Languages), Conflicts = PackConflictStore.Clone(Conflicts),
        ExtraFiles = new(ExtraFiles, StringComparer.OrdinalIgnoreCase), InlineRoots = new(InlineRoots, StringComparer.OrdinalIgnoreCase), ExtraConfigFiles = new(ExtraConfigFiles, StringComparer.OrdinalIgnoreCase), Dependencies = new(Dependencies, StringComparer.OrdinalIgnoreCase), DraftFiles = new(DraftFiles, StringComparer.OrdinalIgnoreCase), ExcludedExtras=new(ExcludedExtras,StringComparer.OrdinalIgnoreCase) };
}
internal static partial class PackPublisher
{
    internal static bool CompleteMode(PublishMode mode) => mode == PublishMode.Local || mode == PublishMode.ThunderstoreComplete;
    internal static bool StoreMode(PublishMode mode) => mode == PublishMode.Thunderstore || mode == PublishMode.ThunderstoreComplete;
    internal static string GameRoot => Path.GetFullPath(Paths.GameRootPath);
    internal static bool Keep(string relative) => PublicationFilePolicy.Keep(relative);
    private static string SelfDirectory => Path.GetDirectoryName(Chainloader.PluginInfos.TryGetValue(Plugin.PluginGuid, out var own) ? own.Location : typeof(Plugin).Assembly.Location)!;
    private static bool KeepCopiedFile(string file, string relative) => Keep(relative) && !PublicationFilePolicy.IsSelfDemo(file, SelfDirectory)
        && !PublicationFilePolicy.IsUpdaterRuntime(file, Paths.BepInExRootPath);
    private static bool KeepPluginFile(string file, string relative, IReadOnlyList<OverviewMod> mods)
    {
        if (!KeepCopiedFile(file, relative)) return false;
        return PublicationFilePolicy.KeepSelfCopy(file, Plugin.PluginGuid, mods.Select(m => (m.Guid, m.PackageRoot, m.Loaded)));
    }
    internal static string PackId()
    {
        if (PackLibrary.Active is ModConfigEnhance.Updating.PackRecord current) return current.PublicationId.Length > 0 ? current.PublicationId : current.Id;
        string path = Path.Combine(Loc.RuntimeDirectory, "publisher.json");
        if (File.Exists(path)) { var value = JObject.Parse(File.ReadAllText(path)).Value<string>("packId"); if (!string.IsNullOrWhiteSpace(value)) return value!; }
        string id = PublishedPack.Active?.Id ?? Guid.NewGuid().ToString("N");
        OverviewRepository.AtomicWrite(path, new JObject { ["packId"] = id }.ToString()); return id;
    }
    internal static List<(string Label, string Relative)> Extras()
    {
        var result = new List<(string,string)>();
        foreach (var item in new DirectoryInfo(GameRoot).EnumerateFileSystemInfos())
        {
            string name = item.Name;
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0 || new[] { "BepInEx", "PEAK_Data", "MonoBleedingEdge", "MODs", "PEAK.exe", "UnityPlayer.dll", "UnityCrashHandler64.exe", "steam_api64.dll", "GameAssembly.dll" }.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            if (Keep(name)) result.Add((name, name));
        }
        foreach (var item in new DirectoryInfo(Paths.BepInExRootPath).EnumerateFileSystemInfos())
        {
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0 || new[] { "plugins", "config", "core", "patchers", "monomod", ModConfigEnhance.Updating.ManagerIdentity.RuntimeDirectory, ModConfigEnhance.Updating.ManagerIdentity.LegacyRuntimeDirectory }.Contains(item.Name, StringComparer.OrdinalIgnoreCase)) continue;
            string relative = "BepInEx/" + item.Name; if (Keep(relative)) result.Add((relative, relative));
        }
        foreach (string folder in new[] { "core", "patchers", "monomod" })
        {
            string path = Path.Combine(Paths.BepInExRootPath, folder); if (!Directory.Exists(path)) continue;
            foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                if (folder == "core" && (item.Name.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase) || item.Name.StartsWith("Mono.", StringComparison.OrdinalIgnoreCase) || item.Name.StartsWith("0Harmony", StringComparison.OrdinalIgnoreCase) || item.Name.StartsWith("HarmonyX", StringComparison.OrdinalIgnoreCase))) continue;
                string relative = "BepInEx/" + folder + "/" + item.Name; if (Keep(relative)) result.Add((relative, relative));
            }
        }
        return result;
    }
    internal static string Dependency(OverviewMod mod) => mod.SourceKind == ModSourceKind.Store && mod.SourceConfirmed && Regex.IsMatch(mod.PackageVersion, @"^\d+\.\d+\.\d+$") && ModSources.KnownVersion(mod.SourceNamespace, mod.SourceName, mod.PackageVersion) ? mod.SourceKey + "-" + mod.PackageVersion : "";
    internal static List<OverviewMod> Inventory(OverviewRepository repo)
    {
        var catalogue = new OverviewCatalogue();
        var mods = catalogue.Scan(repo);
        mods.AddRange(catalogue.PublicationCache(repo));
        var containers = ContainerRoots();
        foreach (string route in Directory.GetDirectories(Paths.BepInExRootPath).Select(Path.GetFileName).Where(p=>p!=null&&!new[]{"config",ModConfigEnhance.Updating.ManagerIdentity.RuntimeDirectory,ModConfigEnhance.Updating.ManagerIdentity.LegacyRuntimeDirectory,"cache","logs","log"}.Contains(p,StringComparer.OrdinalIgnoreCase)).Select(p => p!))
        foreach (string file in OverviewRepository.SafeFiles(Path.Combine(Paths.BepInExRootPath, route)).Where(p => Path.GetFileName(p) == "manifest.json"))
        {
            string root = Path.GetDirectoryName(file)!;
            if (containers.Contains(Path.GetFullPath(root))) continue;
            if (!Keep(Relative(Paths.BepInExRootPath, file)) || file.Replace('\\', '/').Split('/').Contains("Demo") || mods.Any(m => m.PackageRoot.Equals(root, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var data = JObject.Parse(File.ReadAllText(file)); string name = data.Value<string>("name") ?? "";
                if (!ThunderstoreUpdateService.ValidPart(name)) continue;
                string ns = data.Value<string>("namespace") ?? data.Value<string>("author") ?? "";
                if (!ThunderstoreUpdateService.ValidPart(ns) && ThunderstoreUpdateService.TryParseSource(Path.GetFileName(root), out var folderNs, out var folderName) && folderName == name) ns = folderNs;
                var mod = new OverviewMod { Guid = "package:" + Relative(Paths.BepInExRootPath, root).Replace('\\', '/'), Name = name, RawName = name, PackageName = name, PackageRoot = root,
                    PackageVersion = data.Value<string>("version_number") ?? "", SourceNamespace = ns, SourceName = name, SourceConfirmed = ThunderstoreUpdateService.ValidPart(ns), HomepageUrl = data.Value<string>("website_url") ?? "", Author = ns };
                mod.PackageDependencies = data["dependencies"]?.Values<string>().Where(v => v != null).Select(v => v!).ToList() ?? new();
                ModSources.Apply(mod); mods.Add(mod);
            }
            catch (Exception e) { Plugin.Log.LogDebug("[发布清单] " + e.Message); }
        }
        foreach(var mod in mods.Where(m=>m.SourceName.Length==0&&m.PackageName.Length>0))
        {
            var identities=mods.SelectMany(m=>m.PackageDependencies).Where(ValidDependency).Select(d=>d.Substring(0,d.LastIndexOf('-'))).Where(k=>k.EndsWith("-"+mod.PackageName,StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(identities.Count==1) {mod.SourceNamespace=identities[0].Substring(0,identities[0].Length-mod.PackageName.Length-1);mod.SourceName=mod.PackageName;}
        }
        return mods;
    }
    private static HashSet<string> ContainerRoots()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string plugins = Path.GetFullPath(Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var pack in PublishedPack.Packs.Where(p => p.Valid))
        {
            for (var parent = Directory.GetParent(pack.Root); parent != null && (parent.FullName + Path.DirectorySeparatorChar).StartsWith(plugins, StringComparison.OrdinalIgnoreCase); parent = parent.Parent)
                result.Add(parent.FullName);
            foreach (string? relative in pack.Release["containers"]?.Values<string>() ?? Array.Empty<string>())
            { string full = OverviewRepository.SafePath(Paths.BepInExRootPath, relative ?? ""); if (full.Length > 0) result.Add(full); }
        }
        return result;
    }
    internal static bool IsInline(PublishRequest request, OverviewMod mod) => request.InlineRoots.Contains(mod.PackageRoot) || mod.SourceKind == ModSourceKind.Local && !request.Dependencies.ContainsKey(mod.PackageRoot);
    internal static IEnumerable<string> ProfileConfigFiles(OverviewConfigSnapshot snapshot) => snapshot.values
        .Where(v => PublicationFilePolicy.ProfileConfigPath(v.configFile) && v.configFile.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
        .Select(v => v.configFile.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase);
    internal static List<string> AdditionalConfigFiles(OverviewConfigSnapshot snapshot)
    {
        var recorded = new HashSet<string>(ProfileConfigFiles(snapshot), StringComparer.OrdinalIgnoreCase);
        return OverviewRepository.SafeFiles(Paths.ConfigPath).Select(p => Relative(Paths.ConfigPath, p).Replace('\\', '/'))
            .Where(p => ExtraConfigAllowed(p) && !recorded.Contains(p)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }
    private static bool ExtraConfigAllowed(string relative) => Keep(relative) && !Path.IsPathRooted(relative)
        && !relative.Replace('\\', '/').Split('/').Any(p => p == "..")
        && !ModConfigEnhance.Updating.ManagerIdentity.IsConfigDirectory(relative.Replace('\\', '/').Split('/')[0])
        && !relative.Equals(Plugin.PluginGuid + ".cfg", StringComparison.OrdinalIgnoreCase);
    private static string PackageSource(OverviewMod mod) => Directory.Exists(mod.PackageRoot) || File.Exists(mod.PackageRoot) ? mod.PackageRoot
        : mod.AssemblyPaths.FirstOrDefault(path => File.Exists(path) && !OverviewCatalogue.IsUpdateCandidate(path)
            && OverviewCatalogue.CanonicalPluginPath(path).Equals(OverviewCatalogue.CanonicalPluginPath(mod.PackageRoot), StringComparison.OrdinalIgnoreCase)) ?? mod.PackageRoot;
    internal static List<string> PreviewFiles(OverviewRepository repo, PublishRequest request, IReadOnlyList<OverviewMod> mods)
    {
        mods = SelectInventory(repo, request, mods);
        OverviewUpdateQueue.EnsurePublicationSafe(mods.Select(m => m.PackageRoot));
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profile = repo.Profiles.ListProfiles().FirstOrDefault(p => p.id == request.DefaultProfile);
        var goals = profile?.mods.GroupBy(m => m.guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().enabled, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string,bool>();
        void AddTree(string source, string prefix, Func<string,bool>? include = null)
        { foreach (string file in OverviewRepository.SafeFiles(source)) { string relative = Relative(source, file).Replace('\\','/'); if (file.EndsWith(".dll.old", StringComparison.OrdinalIgnoreCase) && File.Exists(file.Substring(0, file.Length - 4))) continue; if (source == Paths.PluginPath && CompleteMode(request.Mode)) { if (!KeepPluginFile(file, relative, mods)) continue; relative = ProjectPluginRelative(file, relative, mods, goals); } if (KeepCopiedFile(file, relative) && (include == null || include(relative))) files.Add(prefix + relative); } }
        string payload = request.Mode == PublishMode.Data ? "plugins/" + ModConfigEnhance.Updating.ManagerIdentity.PackFolder + "/" : "BepInEx/plugins/" + request.Name + "/" + ModConfigEnhance.Updating.ManagerIdentity.PackFolder + "/";
        foreach (string name in new[] { "profiles.json", "release.json", "sources.json", "conflicts.json" }) files.Add(payload + name);
        if (request.Sign) files.Add(payload + "signature.json");
        foreach (string language in request.Languages) files.Add(payload + "language/" + language + ".txt");
        foreach (var item in repo.PrepareExport(request.Profiles))
        {
            string prefix = payload + "profiles/" + OverviewProfileExport.PortableId(item.Id) + "/";
            foreach (string name in new[] { "profile.json", "config-values.json", "introduction.mcedoc.json", "introduction.md" }.Concat(OverviewProfileExport.ResourceFiles(item))) files.Add(prefix + name);
        }
        if (CompleteMode(request.Mode))
        {
            var allocated = ValidateEnvironmentPreview(request, mods, profile ?? new OverviewProfile());
            files.Add("README.md"); files.Add("CHANGELOG.md");
            foreach (string relative in allocated) files.Add(relative);
            files.Add(EnvironmentFile);
        }
        if (request.Mode != PublishMode.Data)
        {
            foreach (string relative in ProfileConfigFiles(profile?.config ?? new OverviewConfigSnapshot())) files.Add("BepInEx/config/" + relative);
            foreach (string relative in request.ExtraConfigFiles)
            {
                if (!ExtraConfigAllowed(relative) || ProfileConfigFiles(profile?.config ?? new OverviewConfigSnapshot()).Contains(relative.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Loc.T("Additional configuration overlaps profile data or private runtime data.", "附加配置与方案配置或私人运行资料重叠。", "附加設定與方案設定或私人執行資料重疊。"));
                OverviewProfileAssets.CheckedPath(Paths.ConfigPath, relative); files.Add("BepInEx/config/" + relative.Replace('\\', '/'));
            }
        }
        if (request.Mode == PublishMode.Thunderstore)
        {
            foreach (var package in mods.GroupBy(m => m.PackageRoot).Where(g => IsInline(request, g.First())))
            { string source = PackageSource(package.First()); string prefix = "BepInEx/" + PublicationRelative(source); if (File.Exists(source)) files.Add(prefix); else AddTree(source, prefix + "/"); }
            foreach (string name in new[] { "manifest.json", "README.md", "icon.png" }) files.Add(name);
        }
        if (request.Mode == PublishMode.Thunderstore) foreach (string extra in request.ExtraFiles)
        { string path = OverviewProfileAssets.CheckedPath(GameRoot, extra); if (!KeepCopiedFile(path, extra)) continue; if (Directory.Exists(path)) AddTree(path, extra.TrimEnd('/') + "/"); else files.Add(extra); }
        if (request.Mode == PublishMode.ThunderstoreComplete) { files.Add("manifest.json"); files.Add("icon.png"); }
        foreach (string draft in request.DraftFiles.Keys) files.Add(draft);
        return files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }
    internal static bool ValidDependency(string value) => Regex.IsMatch(value, @"^[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+$");
    internal static void ValidateDefaults(OverviewProfile profile, IReadOnlyList<OverviewMod> mods)
    {
        var targets = profile.mods.GroupBy(m => m.guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last().enabled, StringComparer.OrdinalIgnoreCase);
        bool Enabled(OverviewMod mod) => targets.TryGetValue(mod.Guid, out bool value) ? value : mod.TargetEnabled;
        foreach (var group in mods.GroupBy(m => m.PackageRoot, StringComparer.OrdinalIgnoreCase))
            if (group.Select(Enabled).Distinct().Count() > 1) throw new InvalidOperationException(Loc.F("Conflicting enablement targets in the same package: {0}", "同一资料包的启停目标不一致：{0}", "同一資料包的啟停目標不一致：{0}", group.First().Name));
        foreach (var mod in mods)
        {
            if (!mod.CanToggle && !Enabled(mod)) throw new InvalidOperationException(Loc.F("The default profile cannot disable this required library: {0}", "默认方案不能停用此基础模组：{0}", "默認方案不能停用此基礎模組：{0}", mod.Name));
            if (!Enabled(mod)) continue;
            foreach (var dependency in mod.RuntimeDependencies.Where(d => !d.Optional && !d.Id.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)))
            {
                var provider = mods.FirstOrDefault(m => m.Guid.Equals(dependency.Id, StringComparison.OrdinalIgnoreCase));
                if (provider == null || !Enabled(provider)) throw new InvalidOperationException(Loc.F("{0}'s default profile is missing an enabled dependency: {1}", "{0} 的默认方案缺少启用的依赖：{1}", "{0} 的默認方案缺少啟用的依賴：{1}", mod.Name, dependency.Id));
                if (System.Version.TryParse(dependency.MinimumVersion, out var minimum) && System.Version.TryParse(provider.PluginVersion, out var actual) && actual < minimum) throw new InvalidOperationException(Loc.F("{0} has an outdated dependency: {1}", "{0} 的依赖版本不足：{1}", "{0} 的依賴版本不足：{1}", mod.Name, provider.Name));
            }
        }
    }
    internal static Task<string> PublishAsync(OverviewRepository repo, PublishRequest request)
    {
        request.OutputDirectory = ValidateOutputDirectory(repo, request.OutputDirectory);
        var mods = SelectInventory(repo, request, Inventory(repo));
        var conflictErrors = PackConflictStore.Validate(request.Conflicts, mods);
        if (conflictErrors.Count > 0) throw new InvalidDataException(string.Join("\n", conflictErrors));
        OverviewUpdateQueue.EnsurePublicationSafe(mods.Select(m => m.PackageRoot));
        var selected = repo.Profiles.ListProfiles().First(p => p.id == request.DefaultProfile);
        var defaults = JsonConvert.DeserializeObject<OverviewProfile>(JsonConvert.SerializeObject(selected))!;
        PublicationDrafts.ApplyConfigEdits(request, defaults.config);
        ValidateDefaults(defaults, mods);
        repo.Configs.PrepareApply(defaults.config).Validate();
        JObject enhance = Chainloader.PluginInfos.TryGetValue(Plugin.PluginGuid, out var own) && own.Instance != null ? PublishedPack.CaptureDefaults(own.Instance.Config) : new();
        PublicationScrub.ScrubSnapshot(defaults.config); // the default profile's values are written into the package too
        var items = repo.PrepareExport(request.Profiles);
        foreach (var item in items.Where(i => i.Id == (selected.publicationId.Length > 0 ? selected.publicationId : request.DefaultProfile))) item.ConfigValuesJson = JsonConvert.SerializeObject(defaults.config);
        var languages = Loc.SnapshotLanguages(request.Languages);
        string configRoot = repo.ConfigRootDirectory;
        return Task.Run(() => {
            if (!CompleteMode(request.Mode))
            {
                string export = OverviewProfileExport.Export(items, configRoot, "", request.Languages, languages, request.OutputDirectory, request.DisplayName);
                return Publish(repo, request, mods, defaults, enhance, export);
            }
            string workParent = OverviewProfileAssets.CheckedPath(configRoot, "data/runtime/publication-builds");
            string work = Path.Combine(workParent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                string export = OverviewProfileExport.Export(items, configRoot, "", request.Languages, languages, work, request.DisplayName);
                string zip = Publish(repo, request, mods, defaults, enhance, export);
                string name = ExportName(request) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip";
                string destination = OverviewProfileAssets.CheckedPath(request.OutputDirectory, name);
                File.Move(zip, destination);
                string newcomer = NewcomerPath(zip);
                if (File.Exists(newcomer)) File.Move(newcomer, NewcomerPath(destination));
                return destination;
            }
            finally
            {
                string verified = Path.GetFullPath(work);
                if (verified.StartsWith(Path.GetFullPath(workParent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(verified))
                    Directory.Delete(verified, true);
            }
        });
    }
    internal static string SafeLabel(string value)
    {
        string clean = new string(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return clean.Length == 0 ? "Modpack" : clean.Substring(0, Math.Min(64, clean.Length));
    }
    internal static string ExportName(PublishRequest request) => SafeLabel(request.Name + (request.DisplayName.Length > 0 && request.DisplayName != request.Name ? "_" + request.DisplayName : "")) + "-" + request.Version;
    internal static string DefaultOutputDirectory(OverviewRepository repo) => Path.GetFullPath(Updating.MceDirectories.PackOutput(repo.ConfigRootDirectory));
    internal static string ValidateOutputDirectory(OverviewRepository repo, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException(Loc.T("Choose an output folder.", "请选择输出目录。", "請選擇輸出資料夾。"));
        string full;
        try { full = Path.GetFullPath(directory.Trim().Trim('"')); }
        catch (Exception e) { throw new InvalidOperationException(Loc.T("The output folder path is invalid.", "输出目录路径无效。", "輸出資料夾路徑無效。"), e); }
        string game = Path.GetFullPath(GameRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        bool legacyDefault = string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), DefaultOutputDirectory(repo).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        if (!legacyDefault && (candidate.StartsWith(game, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate, game, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(Loc.T("Choose an output folder outside the PEAK installation.", "输出目录不能位于 PEAK 游戏安装目录内。", "輸出資料夾不能位於 PEAK 遊戲安裝目錄內。"));
        string source = Path.GetFullPath(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!);
        if (candidate.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Loc.T("Choose an output folder outside the mod source folder.", "输出目录不能位于模组文件目录内。", "輸出資料夾不能位於模組檔案目錄內。"));
        for (var ancestor = new DirectoryInfo(full); ancestor != null; ancestor = ancestor.Parent)
        {
            if (legacyDefault && InstallationPaths.AtGameRoot(ancestor.FullName)) break;
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(Loc.T("Output folders cannot use directory links or junctions.", "输出目录不能使用目录链接或联接点。", "輸出資料夾不能使用目錄鏈接或聯接點。"));
        }
        Directory.CreateDirectory(full);
        string probe = Path.Combine(full, ".mce-write-check-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        return full;
    }
    internal static string Publish(OverviewRepository repo, PublishRequest request, List<OverviewMod> mods, OverviewProfile defaults, JObject enhance, string export)
    {
        mods = SelectInventory(repo, request, mods);
        OverviewUpdateQueue.EnsurePublicationSafe(mods.Select(m => m.PackageRoot));
        if (request.Profiles.Count == 0 || !request.Profiles.Contains(request.DefaultProfile)) throw new InvalidOperationException(Loc.T("Select the profiles to publish and choose one as the default.", "请选择要发布的方案及其中一套默认方案。", "請選擇要發布的方案及其中一套默認方案。"));
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || request.Name == "." || request.Name == ".." || !Regex.IsMatch(request.Version, @"^\d+\.\d+\.\d+$")) throw new InvalidOperationException(Loc.T("Enter a valid publication name and a three-part numeric version.", "请填写有效的发布名称和三段数字版本。", "請填寫有效的發布名稱和三段數字版本。"));
        if (StoreMode(request.Mode) && !ThunderstoreUpdateService.ValidPart(request.Name)) throw new InvalidOperationException(Loc.T("Store publication names may contain only ASCII letters, numbers, and underscores.", "商店发布名称只能包含英文、数字和下划线。", "商店發布名稱只能包含英文、數字和下劃線。"));
        if (request.Website.Length > 0 && (!Uri.TryCreate(request.Website, UriKind.Absolute, out var website) || !(website.Scheme == "https" || website.Scheme == "http"))) throw new InvalidOperationException(Loc.T("Enter a complete HTTP(S) project URL, or leave it empty.", "项目主页请填写完整的 HTTP(S) 地址，或留空。", "項目主頁請填寫完整的 HTTP(S) 地址，或留空。"));
        var packages = mods.GroupBy(m => m.PackageRoot, StringComparer.OrdinalIgnoreCase).ToList();
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (request.Mode == PublishMode.Thunderstore)
        {
            foreach (var package in packages)
            {
                var first = package.First(); if (IsInline(request, first)) continue;
                string dependency = request.Dependencies.TryGetValue(package.Key, out var chosen) ? chosen : Dependency(first);
                if (!ValidDependency(dependency)) throw new InvalidOperationException(Loc.F("Resolve pending mods first: {0}", "请先处理待定模组：{0}", "請先處理待定模組：{0}", first.Name));
                dependencies.Add(dependency);
            }
            if(!ValidDependency(request.LoaderDependency)||!request.LoaderDependency.StartsWith("BepInEx-BepInExPack",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("请确认商店根清单的加载器版本。");
            dependencies.Add(request.LoaderDependency);
            var localKeys = new HashSet<string>(mods.Where(m => IsInline(request, m)).Select(m => m.SourceKey), StringComparer.OrdinalIgnoreCase);
            foreach (string dependency in mods.Where(m => IsInline(request, m)).SelectMany(m => m.PackageDependencies))
            {
                int dash = dependency.LastIndexOf('-');
                if (ValidDependency(dependency) && dash > 0 && !localKeys.Contains(dependency.Substring(0, dash)) && !dependency.StartsWith("BepInEx-BepInExPack",StringComparison.OrdinalIgnoreCase))
                { string key=dependency.Substring(0,dash); var selectedPackage=mods.FirstOrDefault(m=>m.SourceKey.Equals(key,StringComparison.OrdinalIgnoreCase)); dependencies.Add(selectedPackage!=null&&ValidDependency(key+"-"+selectedPackage.PackageVersion)?key+"-"+selectedPackage.PackageVersion:dependency); }
            }
            foreach (var local in mods.Where(m => IsInline(request, m)))
                if (local.SourceNamespace.Length > 0 && dependencies.Any(d => d.StartsWith(local.SourceKey + "-", StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException(Loc.F("The same mod is both a store dependency and a bundled local mod: {0}", "同一模组同时被选为商店依赖与本地附带：{0}", "同一模組同時被選為商店依賴與本地附帶：{0}", local.Name));
            var queue = new Queue<string>(dependencies.Concat(mods.Where(m => !IsInline(request, m)).SelectMany(m => m.PackageDependencies)));
            var checkedDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (queue.Count > 0)
            {
                string dependency = queue.Dequeue(); if (!ValidDependency(dependency) || !checkedDependencies.Add(dependency)) continue;
                string key = dependency.Substring(0, dependency.LastIndexOf('-'));
                if (localKeys.Contains(key)) continue; // A third-party requirement is satisfied by the explicitly selected bundled package.
                foreach (string child in ModSources.Dependencies(dependency)) queue.Enqueue(child);
            }
            var conflicts = dependencies.Where(ValidDependency).GroupBy(d => d.Substring(0, d.LastIndexOf('-')), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
            if (conflicts.Count > 0) throw new InvalidOperationException("商店依赖版本冲突：" + string.Join("；", conflicts.Select(g => string.Join(", ",g))));
            var resolved = dependencies.Where(ValidDependency).ToArray();
            dependencies.Clear(); dependencies.UnionWith(resolved);
        }
        // All output is new. Live configs, plugins and author originals are never rewritten.
        string payload = Path.Combine(export, "plugins", ModConfigEnhance.Updating.ManagerIdentity.PackFolder);
        string id = PackId();
        PackConflictStore.WritePublished(payload, request.Conflicts, mods);
        foreach (string file in OverviewRepository.SafeFiles(payload).Where(p => Path.GetFileName(p) == "profile.json"))
        {
            var profile = JObject.Parse(File.ReadAllText(file));
            if (profile["state"]?["pack"] is JObject pack) { pack["id"] = id; pack["version"] = request.Version; if (string.IsNullOrWhiteSpace(pack.Value<string>("name"))) pack["name"] = request.Name; }
            File.WriteAllText(file, profile.ToString(Formatting.Indented), new UTF8Encoding(false));
        }
        ModSources.Export(payload, mods);
        string sourcesPath = Path.Combine(payload, "sources.json"); var sourceRecords = JObject.Parse(File.ReadAllText(sourcesPath));
        foreach (var mod in mods)
        {
            if (IsInline(request, mod)) { if (sourceRecords[mod.Guid] is JObject inlineSource) inlineSource["distribution"] = "embedded"; }
            else if (request.Dependencies.TryGetValue(mod.PackageRoot, out string? declared))
            {
                var parts = declared.Split('-');
                sourceRecords[mod.Guid] = JObject.FromObject(new ModSourceRecord { kind = ModSourceKind.Store, ns = parts[0], name = parts[1], author = parts[0], homepage = mod.HomepageUrl, packageVersion = parts[2], distribution = "dependency" });
            }
        }
        File.WriteAllText(sourcesPath, sourceRecords.ToString(Formatting.Indented), new UTF8Encoding(false));
        OverviewRepository.AtomicWrite(Path.Combine(payload, "release.json"), new JObject {
            ["schemaVersion"] = 1, ["packId"] = id, ["name"] = request.Name, ["version"] = request.Version,
            ["defaultProfile"] = OverviewProfileExport.PortableId(defaults.publicationId.Length > 0 ? defaults.publicationId : request.DefaultProfile), ["signed"] = request.Sign,
            ["enhanceDefaults"] = enhance, ["containers"] = new JArray(ContainerRoots().Select(p => Relative(Paths.BepInExRootPath, p).Replace('\\', '/')))
            , ["warnings"] = new JArray(MissingTargets(repo,request,mods).Concat(request.ExcludedExtras.Select(p=>"Excluded attachment / 已确认排除附带内容："+p.Key+" — "+p.Value)))
        }.ToString(Formatting.Indented));
        PublicationDrafts.Apply(request, payload, request.Mode == PublishMode.Data ? "plugins/" + ModConfigEnhance.Updating.ManagerIdentity.PackFolder + "/" : "BepInEx/plugins/" + request.Name + "/" + ModConfigEnhance.Updating.ManagerIdentity.PackFolder + "/");
        if (request.Sign) { PublishedPack.Sign(payload, request.SigningKeyPath); PublishedPack.Verify(payload, true); }
        if (request.Mode == PublishMode.Data) return export;
        string output = Path.Combine(export, CompleteMode(request.Mode) ? "Local" : "Thunderstore"); Directory.CreateDirectory(output);
        CopyTree(payload, Path.Combine(output, "BepInEx", "plugins", request.Name, ModConfigEnhance.Updating.ManagerIdentity.PackFolder), preservePublishedFiles: true);
        if (CompleteMode(request.Mode))
        {
            foreach (var entry in EnvironmentPlan(request, mods, defaults))
                CopyFile(entry.Value, OverviewProfileAssets.CheckedPath(Path.Combine(output, "BepInEx"), entry.Key), true);

        }
        else
        {
            foreach (var package in packages.Where(p => IsInline(request, p.First()) && !PublicationRelative(PackageSource(p.First())).StartsWith("core/",StringComparison.OrdinalIgnoreCase)))
            {
                string source = PackageSource(package.First());
                string relative = PublicationRelative(source);
                if (File.Exists(source)) CopyFile(source, Path.Combine(output, "BepInEx", relative));
                else CopyTree(source, Path.Combine(output, "BepInEx", relative));
            }
            if (!IsIcon(request.Icon)) throw new InvalidOperationException(Loc.T("Choose a 256×256 PNG publication icon.", "请选择 256×256 PNG 发布图标。", "請選擇 256×256 PNG 發布圖標。"));
            CopyFile(request.Icon, Path.Combine(output, "icon.png"), true);
            OverviewRepository.AtomicWrite(Path.Combine(output, "manifest.json"), new JObject { ["name"] = request.Name, ["version_number"] = request.Version, ["description"] = request.Description, ["website_url"] = request.Website, ["dependencies"] = new JArray(dependencies.OrderBy(d => d, StringComparer.Ordinal)) }.ToString(Formatting.Indented));
            OverviewRepository.AtomicWrite(Path.Combine(output, "README.md"), "# " + request.Name + "\n\n" + request.Description + Loc.T("\n\nDefault configuration files are installed before the first launch. The default profile is registered/applied on the first main-menu visit. Back up personal configuration before upgrading.\n", "\n\n默认配置文件随包安装，供首次启动读取；首次进入主菜单继续登记并应用作者默认方案。升级前请备份个人配置。\n", "\n\n預設設定檔隨套件安裝，供首次啟動讀取；首次進入主選單繼續登記並套用作者預設方案。升級前請備份個人設定。\n"));
        }
        if (request.Mode == PublishMode.ThunderstoreComplete)
        {
            if (!IsIcon(request.Icon)) throw new InvalidOperationException("完整商店包需要 256×256 PNG 图标。");
            CopyFile(request.Icon, Path.Combine(output, "icon.png"), true);
            // Everything is bundled and described exactly. No speculative hashes or downloadable dependencies.
            OverviewRepository.AtomicWrite(Path.Combine(output, "manifest.json"), new JObject {
                ["name"] = request.Name, ["version_number"] = request.Version, ["description"] = request.Description,
                ["website_url"] = request.Website, ["dependencies"] = new JArray()
            }.ToString(Formatting.Indented));
        }
        // Both installable formats must supply cfg files before plugins load on first launch.
        string configOutput = Path.Combine(output, "BepInEx", "config");
        var recordedFiles = new HashSet<string>(ProfileConfigFiles(defaults.config), StringComparer.OrdinalIgnoreCase);
        foreach (string relative in request.ExtraConfigFiles)
        {
            if (!ExtraConfigAllowed(relative) || recordedFiles.Contains(relative.Replace('\\', '/')))
                throw new InvalidOperationException("附加配置与方案配置或私人运行资料重叠。");
            CopyFile(OverviewProfileAssets.CheckedPath(Paths.ConfigPath, relative), OverviewProfileAssets.CheckedPath(configOutput, relative), true);
        }
        ProjectConfig(configOutput, defaults.config);
        foreach (string relative in CompleteMode(request.Mode) ? Array.Empty<string>() : request.ExtraFiles.AsEnumerable())
        {
            if (request.Mode == PublishMode.Thunderstore && !new[] { "BepInEx/plugins/", "BepInEx/core/", "BepInEx/patchers/", "BepInEx/monomod/" }.Any(p => relative.StartsWith(p, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException(Loc.F("This additional path is only supported in local installation packages: {0}", "此附加路径仅适用于本地安装包：{0}", "此附加路徑僅適用于本地安裝包：{0}", relative));
            string source = OverviewProfileAssets.CheckedPath(GameRoot, relative), destination = OverviewProfileAssets.CheckedPath(output, relative);
            if (!KeepCopiedFile(source, relative)) continue;
            if (Directory.Exists(source)) CopyTree(source, destination); else CopyFile(source, destination, true);
        }
        var scrubbed = PublicationScrub.Run(Path.Combine(output, "BepInEx", "config"));
        OverviewRepository.AtomicWrite(Path.Combine(output, "CHANGELOG.md"), Changelog(request));
        if (CompleteMode(request.Mode)) OverviewRepository.AtomicWrite(Path.Combine(output, "README.md"), Readme(request));
        var missing=MissingTargets(repo,request,mods).Concat(request.ExcludedExtras.Select(p=>"Excluded attachment / 已确认排除附带内容："+p.Key+" — "+p.Value)).ToList();
        if(missing.Count>0) File.AppendAllText(Path.Combine(output,"README.md"),"\n\n"+string.Join("\n",missing.Select(m=>"- "+m))+"\n",new UTF8Encoding(false));
        if(scrubbed.Count>0) File.AppendAllText(Path.Combine(output,"README.md"),"\n\n"+Loc.T("Values that looked private were cleared from the packed configuration (set them again yourself):","打包时清空了疑似私人信息的配置值（需要的话请自己重新填写）：","打包時清空了疑似私人資訊的設定值（需要的話請自行重新填寫）：")+"\n"+string.Join("\n",scrubbed.Select(m=>"- "+m))+"\n",new UTF8Encoding(false));
        LastScrubbed = scrubbed;
        PublicationDrafts.Apply(request, output, "", skipPrefix: "BepInEx/plugins/" + request.Name + "/" + ModConfigEnhance.Updating.ManagerIdentity.PackFolder + "/");
        if (CompleteMode(request.Mode)) WriteEnvironmentDescription(output, request, defaults, mods);
        string zip = Path.Combine(export, ExportName(request) + ".zip");
        string partial = zip + ".partial"; ZipFile.CreateFromDirectory(output, partial, CompressionLevel.Optimal, false); File.Move(partial, zip);
        LastNewcomerNote = "";
        if (request.Mode == PublishMode.Local && request.NewcomerZip) WriteNewcomerZip(output, zip, request);
        return zip;
    }
    internal static List<string> LastScrubbed = new();
    internal static string LastNewcomerNote = "";
    internal static string NewcomerPath(string zip) => Path.Combine(Path.GetDirectoryName(zip)!, Path.GetFileNameWithoutExtension(zip) + Loc.T("_newcomer", "_新手版", "_新手版") + ".zip");
    /// <summary>1.1.4: the same files plus the game-root loader (winhttp.dll, doorstop_config.ini, .doorstop_version) and an
    /// install note, so a player without BepInEx unzips it into the PEAK folder. Built after the import ZIP, from the same
    /// folder, so the import ZIP never carries game-root files (its importer rejects them).</summary>
    private static void WriteNewcomerZip(string output, string zip, PublishRequest request)
    {
        try
        {
            string winhttp = Path.Combine(GameRoot, "winhttp.dll");
            if (!File.Exists(winhttp)) { LastNewcomerNote = Loc.T("No newcomer ZIP: winhttp.dll is not in the game folder.", "未生成新手版：游戏目录里没有 winhttp.dll。", "未產生新手版：遊戲目錄裡沒有 winhttp.dll。"); return; }
            CopyFile(winhttp, Path.Combine(output, "winhttp.dll"), true);
            string version = Path.Combine(GameRoot, ".doorstop_version");
            if (File.Exists(version)) CopyFile(version, Path.Combine(output, ".doorstop_version"), true);
            // The author's ini only when it is the standard BepInEx one; an r2modman ini points into a profile folder.
            string ini = Path.Combine(GameRoot, "doorstop_config.ini");
            string text = File.Exists(ini) ? File.ReadAllText(ini) : "";
            bool standard = System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)^\s*target_assembly\s*=\s*BepInEx[\\/]core[\\/]BepInEx\.Preloader\.dll\s*$")
                && System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)^\s*enabled\s*=\s*true\s*$")
                && !System.Text.RegularExpressions.Regex.IsMatch(text, @"(?im)^\s*(dll_search_path_override|boot_config_override|doorstop_dll_search_override|mono_dll_search_path_override)\s*=\s*\S");
            File.WriteAllText(Path.Combine(output, "doorstop_config.ini"), standard ? text : StandardDoorstopIni, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, Loc.T("INSTALL.txt", "安装说明.txt", "安裝說明.txt")), NewcomerNote(request), new UTF8Encoding(true));
            string target = NewcomerPath(zip), partial = target + ".partial";
            if (File.Exists(partial)) File.Delete(partial);
            ZipFile.CreateFromDirectory(output, partial, CompressionLevel.Optimal, false); File.Move(partial, target);
        }
        catch (Exception error) { LastNewcomerNote = Loc.T("Newcomer ZIP failed: ", "新手版生成失败：", "新手版產生失敗：") + error.Message; Plugin.Log.LogWarning("[发布包] " + LastNewcomerNote); }
    }
    private const string StandardDoorstopIni = "[General]\r\nenabled = true\r\ntarget_assembly = BepInEx\\core\\BepInEx.Preloader.dll\r\nredirect_output_log = false\r\nboot_config_override =\r\nignore_disable_switch = false\r\n\r\n[UnityMono]\r\ndll_search_path_override =\r\ndebug_enabled = false\r\ndebug_address = 127.0.0.1:10000\r\ndebug_suspend = false\r\n";
    private static string NewcomerNote(PublishRequest request)
    {
        string bepinex = "";
        try { var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(Paths.BepInExRootPath, "core", "BepInEx.dll")); bepinex = info.ProductVersion ?? info.FileVersion ?? ""; } catch { }
        string game = UnityEngine.Application.version;
        // NEWCOMER-NOTE-ZH: the install note is written bilingually on purpose (Chinese block + English block in one file).
        string zh = "【" + request.Name + " " + request.Version + " · 新手版】\r\n\r\n"
            + "这个压缩包已经包含模组加载器 BepInEx" + (bepinex.Length > 0 ? "（" + bepinex + "）" : "") + "、全部模组和作者的配置，不需要另外安装 r2modman 或雷霆商店。\r\n"
            + "制作时的游戏版本：" + game + "\r\n\r\n"
            + "安装：\r\n"
            + "1. 在 Steam 里右键 PEAK → 管理 → 浏览本地文件，打开游戏文件夹（里面有 PEAK.exe）。\r\n"
            + "2. 如果文件夹里已经有 BepInEx，先把它整个复制一份备份。\r\n"
            + "3. 把这个压缩包里的全部内容解压到这个文件夹，遇到同名文件选「替换」。\r\n"
            + "4. 启动游戏。第一次进入主菜单时会应用作者的默认方案，按提示重启一次游戏。\r\n\r\n"
            + "怎么确认装好了：主菜单左上角出现「模组概览」按钮；或者游戏文件夹里出现 BepInEx\\LogOutput.log。\r\n\r\n"
            + "常见问题：\r\n"
            + "· 杀毒软件可能把 winhttp.dll 报成可疑文件，这是 BepInEx 的加载器，请把游戏文件夹加入信任。\r\n"
            + "· 游戏更新后模组失效：等作者发布新版本，或在「模组概览」里检查更新。\r\n"
            + "· 想恢复原版：删掉游戏文件夹里的 winhttp.dll（或整个 BepInEx 文件夹）即可。\r\n"
            + "· 这个压缩包只用于直接解压安装；已经装了 IGMM 的人请用不带「新手版」的那个压缩包导入。\r\n";
        string en = "\r\n----------------------------------------\r\n\r\n"
            + "[" + request.Name + " " + request.Version + " · Newcomer edition]\r\n\r\n"
            + "This archive already contains the BepInEx mod loader" + (bepinex.Length > 0 ? " (" + bepinex + ")" : "") + ", every mod and the author's configuration. No r2modman or Thunderstore needed.\r\n"
            + "Game version when built: " + game + "\r\n\r\n"
            + "Install:\r\n"
            + "1. In Steam, right-click PEAK → Manage → Browse local files (the folder with PEAK.exe).\r\n"
            + "2. If that folder already has a BepInEx folder, back it up first.\r\n"
            + "3. Extract everything in this archive into that folder and replace existing files.\r\n"
            + "4. Start the game. The author's default profile is applied on the first main-menu visit; restart once when asked.\r\n\r\n"
            + "It worked if the main menu shows a \"Mod overview\" button, or BepInEx\\LogOutput.log appears in the game folder.\r\n"
            + "Antivirus may flag winhttp.dll (the BepInEx loader); allow the game folder. To go back to vanilla, delete winhttp.dll.\r\n"
            + "This archive is for extracting only; IGMM users should import the archive without \"newcomer\" in its name.\r\n";
        return zh + en;
    }
    internal static string Readme(PublishRequest request) => "# " + request.Name + " " + request.Version + Loc.T("\n\nNew installation: copy this package into the PEAK game directory. The author's default profile is applied on the first main-menu visit; follow any restart prompts.\n\nExisting installation: back up first and preserve personal configuration in BepInEx/config, then update the mods and IGMMPack author data. Check the current enablement targets in profile management after starting the game.\n\nAuthor translations and presets are stored in IGMMPack; personal changes are in config/InGameModManager.\n", "\n\n新安装：将包内文件放到 PEAK 游戏目录。首次进入主菜单将应用作者默认方案，按提示完成所需重启。\n\n更新已有安装：先备份，保留现有 BepInEx/config 个人配置，再更新模组和 IGMMPack 作者资料；启动后可在方案管理里核对当前启停目标。\n\n作者译文与预设位于 IGMMPack，个人修改位于 config/InGameModManager。\n", "\n\n新安裝：將包內檔案放到 PEAK 遊戲目錄。首次進入主選單將套用作者預設方案，按提示完成所需重新啟動。\n\n更新既有安裝：先備份，保留現有 BepInEx/config 個人設定，再更新模組和 IGMMPack 作者資料；啟動後可在方案管理裡核對目前啟停目標。\n\n作者譯文與預設位於 IGMMPack，個人修改位於 config/InGameModManager。\n");
    internal static string Changelog(PublishRequest request) => "# " + request.Version + "\n\n" + request.Description + "\n";
    internal static bool IsIcon(string path)
    {
        try { using var reader = new BinaryReader(File.OpenRead(path)); byte[] icon = reader.ReadBytes(24); return icon.Length == 24 && icon[0] == 137 && icon[1] == 80 && icon[2] == 78 && icon[3] == 71 && BigEndian(icon, 16) == 256 && BigEndian(icon, 20) == 256; }
        catch { return false; }
    }
    private static int BigEndian(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    private static string Relative(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path); if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException(Loc.F("File is outside the expected directory: {0}", "文件不在预期目录：{0}", "文件不在預期目錄：{0}", path));
        return full.Substring(fullRoot.Length);
    }
    private static string ProjectPluginRelative(string file, string relative, IReadOnlyList<OverviewMod> mods, Dictionary<string,bool> targets)
    {
            var mod = mods.FirstOrDefault(m => m.AssemblyPaths.Any(p => p.Equals(file, StringComparison.OrdinalIgnoreCase)));
            if (mod != null && targets.TryGetValue(mod.Guid, out bool enabled) && mod.CanToggle)
            {
                relative = PluginFileNames.OriginalPath(relative);
                if (!enabled && relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) relative += PluginFileNames.DisabledSuffix;
            }
        return relative;
    }
    private static void CopyPlugins(string source, string destination, IReadOnlyList<OverviewMod> mods, Dictionary<string, bool> targets)
    {
        if (!Directory.Exists(source)) return;
        foreach (string file in OverviewRepository.SafeFiles(source))
        {
            string relative = Relative(source, file); if (!KeepPluginFile(file, relative, mods)) continue;
            if (PluginFileNames.IsDisabled(file) && File.Exists(PluginFileNames.OriginalPath(file))) continue;
            relative = ProjectPluginRelative(file, relative, mods, targets);
            CopyFile(file, OverviewProfileAssets.CheckedPath(destination, relative));
        }
    }
    internal static void CopyTree(string source, string destination, Func<string, bool>? include = null, bool preservePublishedFiles = false)
    {
        if (!Directory.Exists(source)) return;
        foreach (string file in OverviewRepository.SafeFiles(source))
        {
            string relative = Relative(source, file); if (!PublicationFilePolicy.IsUpdaterRuntime(file, Paths.BepInExRootPath)
                && (preservePublishedFiles || KeepCopiedFile(file, relative)) && (include == null || include(relative))) CopyFile(file, OverviewProfileAssets.CheckedPath(destination, relative));
        }
    }
    private static void CopyFile(string source, string destination, bool required = false)
    {
        if (!File.Exists(source)) { if (required) throw new FileNotFoundException(Loc.T("A required publication file does not exist.", "发布需要的文件不存在。", "發布需要的文件不存在。"), source); return; }
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException(Loc.F("Publication files cannot be links: {0}", "发布文件不能是链接：{0}", "發布文件不能是鏈接：{0}", source));
        var info = new FileInfo(source); long size = info.Length; DateTime stamp = info.LastWriteTimeUtc;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            using var first=File.OpenRead(source); using var second=File.OpenRead(destination);
            if(first.Length==second.Length)
            { var a=new byte[81920];var b=new byte[81920]; int count; bool equal=true; while((count=first.Read(a,0,a.Length))>0) { if(second.Read(b,0,count)!=count||!a.Take(count).SequenceEqual(b.Take(count))) {equal=false;break;} } if(equal) return; }
            throw new IOException(Loc.F("Conflicting export destination: {0}", "导出目标内容冲突：{0}", "導出目標內容衝突：{0}", destination));
        }
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
        info.Refresh(); if (info.Length != size || info.LastWriteTimeUtc != stamp) throw new IOException(Loc.F("A source file changed during publication. Try again: {0}", "发布期间源文件发生变化，请重试：{0}", "發布期間源文件發生變化，請重試：{0}", source));
    }
    internal static void ProjectConfig(string configRoot, OverviewConfigSnapshot snapshot)
    {
        foreach (var group in snapshot.values.Where(v => PublicationFilePolicy.ProfileConfigPath(v.configFile))
            .GroupBy(v => v.configFile, StringComparer.OrdinalIgnoreCase))
        {
            string path = OverviewProfileAssets.CheckedPath(configRoot, group.Key);
            if (!path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase)) continue;
            var output = new List<string>();
            foreach (var section in group.GroupBy(v => v.section, StringComparer.Ordinal))
            {
                if (section.Key.IndexOfAny(new[] { '\r', '\n', '[', ']' }) >= 0) throw new InvalidDataException(Loc.T("Invalid configuration section.", "配置分区名称无效。", "設定區段名稱無效。"));
                output.Add("[" + section.Key + "]");
                foreach (var value in section)
                {
                    if (value.key.IndexOfAny(new[] { '\r', '\n', '=' }) >= 0 || value.serializedValue.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                        throw new InvalidDataException(Loc.T("Invalid serialized configuration value.", "配置项名称或序列化值无效。", "設定項目名稱或序列化值無效。"));
                    output.Add(value.key + " = " + value.serializedValue);
                }
                output.Add("");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, string.Join("\n", output), new UTF8Encoding(false));
        }
    }
}
