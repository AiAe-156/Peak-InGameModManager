#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using Mono.Cecil;
using ModConfigEnhance.Updating;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewCatalogue
{
    /// <summary>一个 DLL 里读出来的插件声明；只依赖文件本身，可按「大小＋修改时间」缓存。</summary>
    private sealed class PluginRecord
    {
        internal string Guid = "", Name = "", Version = "";
        internal readonly List<OverviewDependency> Dependencies = new();
    }
    private readonly struct Stamp
    {
        internal readonly long Size; internal readonly DateTime Written, Created;
        internal Stamp(FileInfo info) { Size = info.Length; Written = info.LastWriteTimeUtc; Created = info.CreationTimeUtc; }
        internal bool Matches(FileInfo info) => info.Length == Size && info.LastWriteTimeUtc == Written && info.CreationTimeUtc == Created;
    }
    // 读 DLL 元数据和解析 manifest 都只和文件内容有关：同一文件没动过就不必重读。
    // 读不出插件声明的（原生 DLL，如 54 MB 的 nvngx_dlss.dll）同样记下来，别每次扫描都重开一遍再抛异常。
    private static System.Threading.Tasks.Task? _prefetch;
    private static int _cacheGeneration;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Stamp Stamp, List<PluginRecord> Records)> AssemblyCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Stamp Stamp, JObject? Data)> ManifestCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>「刷新」按钮用：放弃全部文件缓存，重新读盘。</summary>
    internal static void InvalidateCaches()
    {
        System.Threading.Interlocked.Increment(ref _cacheGeneration);
        AssemblyCache.Clear(); ManifestCache.Clear(); InvalidateScanEvidence();
    }

    /// <summary>Read-only metadata warmup; no repository, Unity object, plugin config or source mutation.</summary>
    internal static void Prefetch()
    {
        if(_prefetch!=null && !_prefetch.IsCompleted)return;
        string plugins=Paths.PluginPath;int generation=System.Threading.Volatile.Read(ref _cacheGeneration);
        _prefetch=System.Threading.Tasks.Task.Run(()=> {
            try
            {
                foreach(string path in OverviewRepository.SafeFiles(plugins))
                {
                    if(generation!=System.Threading.Volatile.Read(ref _cacheGeneration))break;
                    if(!PluginFileNames.IsAssembly(path))continue;
                    AssemblyRecords(path);
                    string? manifest=FindManifest(path);
                    if(manifest!=null)ManifestData(manifest);
                }
            }
            catch(Exception error) { Plugin.Log.LogDebug("[概览预热] 元数据预读结束："+error.Message); }
        });
    }
    private static List<PluginRecord> AssemblyRecords(string path)
    {
        int generation=System.Threading.Volatile.Read(ref _cacheGeneration);
        FileInfo info;
        try { info = new FileInfo(path); if (!info.Exists) return new List<PluginRecord>(); }
        catch { return new List<PluginRecord>(); }
        if (AssemblyCache.TryGetValue(path, out var cached) && cached.Stamp.Matches(info)) return cached.Records;
        var records = new List<PluginRecord>();
        try
        {
            // Cecil reads metadata only. Never load or instantiate a third-party assembly.
            using var resolver = OverviewAssemblyReader.ResolverFor(path);
            using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadSymbols = false, ReadingMode = ReadingMode.Deferred, AssemblyResolver = resolver });
            foreach (var type in assembly.MainModule.Types)
            foreach (var attribute in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInPlugin" && a.ConstructorArguments.Count == 3))
            {
                string guid = attribute.ConstructorArguments[0].Value as string ?? "";
                if (string.IsNullOrWhiteSpace(guid)) continue;
                var record = new PluginRecord { Guid = guid, Name = attribute.ConstructorArguments[1].Value as string ?? type.Name,
                    Version = attribute.ConstructorArguments[2].Value as string ?? "" };
                foreach (var dependency in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInDependency" && a.ConstructorArguments.Count > 0))
                {
                    var item = new OverviewDependency { Id = dependency.ConstructorArguments[0].Value as string ?? "" };
                    if (dependency.ConstructorArguments.Count > 1)
                    {
                        object value = dependency.ConstructorArguments[1].Value;
                        if (value is string minimum) item.MinimumVersion = minimum;
                        else item.Optional = (Convert.ToInt32(value) & (int)BepInDependency.DependencyFlags.SoftDependency) != 0;
                    }
                    record.Dependencies.Add(item);
                }
                records.Add(record);
            }
        }
        catch (Exception ex) { Plugin.Log.LogDebug("[概览] 跳过无法读取的程序集 " + Path.GetFileName(path) + ": " + ex.Message); }
        if(generation==System.Threading.Volatile.Read(ref _cacheGeneration))AssemblyCache[path] = (new Stamp(info), records);
        return records;
    }

    public List<OverviewMod> Scan(OverviewRepository repository)
    {
        var rows = new List<OverviewMod>(); var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Chainloader.PluginInfos.Values)
        {
            string path = entry.Location;
            if (string.IsNullOrEmpty(path)) continue;
            path = Path.GetFullPath(path);
            if (!File.Exists(path))
            {
                var copies = PluginFileNames.ActionableCopies(path);
                if (copies.Count == 1) path = copies[0];
            }
            known.Add(path);
            var loaded = Create(repository, entry.Metadata.GUID, entry.Metadata.Name, entry.Metadata.Version.ToString(), path, true);
            loaded.DependencyMetadataKnown = true;
            foreach (var dependency in entry.Dependencies)
                loaded.RuntimeDependencies.Add(new OverviewDependency { Id = dependency.DependencyGUID, MinimumVersion = dependency.MinimumVersion?.ToString() ?? "", Optional = (dependency.Flags & BepInDependency.DependencyFlags.SoftDependency) != 0 });
            rows.Add(loaded);
        }
        // Only physical evidence is shared with repository initialization. Rules, sources,
        // translations, queue state and every mutable row are projected again below.
        foreach (var physical in DiscoverPhysicalEvidence())
        {
            if (known.Contains(physical.Path) || PluginFileNames.IsBackupCompanion(physical.Path)) continue;
            foreach (var record in physical.Records)
            {
                var row = Create(repository, record.Guid, record.Name, record.Version, physical.Path, false,
                    physical.ManifestPath, physical.ManifestJson, true);
                row.DependencyMetadataKnown = true;
                foreach (var dependency in record.Dependencies)
                    row.RuntimeDependencies.Add(new OverviewDependency { Id = dependency.Id, MinimumVersion = dependency.MinimumVersion, Optional = dependency.Optional });
                rows.Add(row);
            }
        }
        AddResourcePacks(repository, rows);
        return Consolidate(rows);
    }

    /// <summary>2.3.4 (user decision): a manifest package without any assembly is a resource pack — a normal row with a
    /// badge. Excluded: a manifest that is only the packaging copy inside another package (x/plugins/manifest.json), a
    /// folder some row already belongs to, a folder containing another package's assemblies, and the plugins half of a
    /// package whose assemblies live in core/patchers (MonoDetour: shown once, under components).</summary>
    private static void AddResourcePacks(OverviewRepository repository, List<OverviewMod> rows)
    {
        var inventory = Volatile.Read(ref _scanEvidence)?.Inventory;
        if (inventory == null) return;
        string plugins = Path.GetFullPath(Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar);
        var owned = new HashSet<string>(rows.Select(r => Path.GetFullPath(r.PackageRoot).TrimEnd(Path.DirectorySeparatorChar)), StringComparer.OrdinalIgnoreCase);
        var assemblyDirs = inventory.Assemblies.Select(a => Path.GetDirectoryName(Path.GetFullPath(a))!).ToList();
        var components = new HashSet<string>(InfrastructurePackages.Results.Where(p => p.IdentityConfirmed).Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
        foreach (string manifest in inventory.Manifests.OrderBy(m => m.Length))
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(manifest))!.TrimEnd(Path.DirectorySeparatorChar);
            if (folder.Equals(plugins, StringComparison.OrdinalIgnoreCase) || owned.Contains(folder)) continue;
            // Packaging copy: x/plugins/manifest.json beside or below a package that already has its own manifest/row.
            string? parent = Path.GetDirectoryName(folder);
            if (Path.GetFileName(folder).Equals("plugins", StringComparison.OrdinalIgnoreCase) && parent != null && owned.Contains(parent.TrimEnd(Path.DirectorySeparatorChar))) continue;
            // Some row's package lies inside this folder: it is a container or an outer package, not a resource pack.
            if (owned.Any(o => o.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
            if (assemblyDirs.Any(d => d.Equals(folder, StringComparison.OrdinalIgnoreCase) || d.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
            var data = ManifestData(manifest);
            if (data == null) continue;
            string name = data.Value<string>("name") ?? Path.GetFileName(folder);
            string ns = data.Value<string>("namespace") ?? data.Value<string>("author") ?? "";
            if (!ThunderstoreUpdateService.ValidPart(ns) && ThunderstoreUpdateService.TryParseSource(Path.GetFileName(folder), out var folderNs, out var folderName) && folderName == name) ns = folderNs;
            if (components.Contains(ns + "-" + name)) continue;
            string id = "resource:" + (ThunderstoreUpdateService.ValidPart(ns) && ThunderstoreUpdateService.ValidPart(name) ? ns + "-" + name
                : Path.GetFileName(folder));
            var row = Create(repository, id, name, data.Value<string>("version_number") ?? "", manifest, false, manifest, data.ToString(Newtonsoft.Json.Formatting.None), true);
            row.PackageRoot = folder; row.ResourcePack = true; row.DependencyMetadataKnown = true;
            row.AssemblyPaths = new List<string>(); row.PluginGuids = new List<string> { id };
            row.PluginCopies = new List<OverviewPluginCopy>();
            row.TargetEnabled = !manifest.EndsWith(".old", StringComparison.OrdinalIgnoreCase);
            rows.Add(row); owned.Add(folder);
        }

    private static List<OverviewMod> Consolidate(List<OverviewMod> rows)
    {
        // The loader keeps using its original assembly until the next launch. A prepared,
        // higher version must enrich that row rather than impersonate an already loaded mod.
        rows = rows.GroupBy(r => r.PackageRoot, StringComparer.OrdinalIgnoreCase)
            .SelectMany(package => package.GroupBy(r => r.Guid, StringComparer.OrdinalIgnoreCase).Select(copies =>
            {
                var row = copies.OrderByDescending(r => r.Loaded).ThenBy(r => IsUpdateCandidate(r.Location))
                    .ThenByDescending(r => r.TargetEnabled).ThenByDescending(r => ParsedVersion(r.PluginVersion)).First();
                row.PluginCopies = copies.SelectMany(OverviewPluginCopies.For).GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
                row.AssemblyPaths = copies.SelectMany(r => r.AssemblyPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                row.PreparedVersion = OverviewUpdateQueue.PreparedVersion(row.Guid, row.PackageRoot);
                if (row.PreparedVersion.Length == 0)
                    row.PreparedVersion = copies.Where(r => !r.Loaded && IsUpdateCandidate(r.Location))
                        .OrderByDescending(r => ParsedVersion(r.PluginVersion)).Select(r => r.PluginVersion).FirstOrDefault() ?? "";
                return row;
            })).ToList();
        foreach (var package in rows.GroupBy(r => r.PackageRoot, StringComparer.OrdinalIgnoreCase))
        {
            var paths = package.SelectMany(r => r.AssemblyPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var guids = package.Select(r => r.Guid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            // Candidate DLLs follow the current package's enablement; they never turn an
            // otherwise disabled package back on merely because their version is higher.
            var currentPaths = paths.Where(p => !IsUpdateCandidate(p)).ToList();
            if (currentPaths.Count == 0) currentPaths = paths;
            bool protectedPackage = package.Any(r => !r.CanToggle);
            foreach (var row in package)
            {
                row.AssemblyPaths = new List<string>(paths); row.PluginGuids = new List<string>(guids);
                row.TargetEnabled = currentPaths.Any(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                if (currentPaths.Any(p => PluginFileNames.ActionableCopies(PluginFileNames.OriginalPath(p)).Count > 1))
                {
                    row.IdentityConflict = OverviewFileConflicts.Copies(row.DisplayName, currentPaths.SelectMany(p => PluginFileNames.ExistingCopies(PluginFileNames.OriginalPath(p))));
                    row.CanToggle = false; row.ToggleReason = row.IdentityConflict;
                }
                if (protectedPackage && row.CanToggle) { row.CanToggle = false; row.ToggleReason = Loc.T("This package contains a protected library.", "同一资料包包含受保护的基础模组", "同一資料包包含受保護的基礎模組"); }
            }
        }
        // Distinct package roots retain exact rename ownership even when GUIDs repeat.
        // Same-GUID versions are reported as advice; per-path candidate conflicts above remain protected.
        string bepinex = Path.GetFullPath(Paths.BepInExRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var row in rows) row.CopyKey = CopyKey(bepinex, row.PackageRoot);
        foreach (var copies in rows.GroupBy(r => r.Guid, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var list = copies.ToList();
            foreach (var row in list) row.CopyRows = list;
        }
        LinkNested(rows);
        return rows.OrderBy(r => r.Rule.order).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Marks each row with the mod whose folder it is nested in (see <see cref="NestedMods"/>). Rows sharing a
    /// package root are one package, so only distinct roots are compared.</summary>
    private static void LinkNested(List<OverviewMod> rows)
    {
        var byRoot = rows.Where(r => !string.IsNullOrEmpty(r.PackageRoot) && Directory.Exists(r.PackageRoot))
            .GroupBy(r => NestedMods.Normalize(Path.GetFullPath(r.PackageRoot)), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var outers = NestedMods.OuterOf(byRoot.Keys, NestedMods.ManifestName);
        foreach (var row in rows) { row.Outer = null; row.Inner.Clear(); }
        foreach (var pair in outers)
        {
            if (pair.Value.Length == 0 || !byRoot.TryGetValue(pair.Value, out var outerRows) || !byRoot.TryGetValue(pair.Key, out var innerRows)) continue;
            var outer = outerRows.OrderByDescending(r => r.Loaded).First();
            foreach (var inner in innerRows) { inner.Outer = outer; foreach (var o in outerRows) if (!o.Inner.Contains(inner)) o.Inner.Add(inner); }
        }
    }

    private static string CopyKey(string bepinex, string packageRoot)
    {
        string full = Path.GetFullPath(packageRoot);
        if (full.StartsWith(bepinex, StringComparison.OrdinalIgnoreCase)) full = full.Substring(bepinex.Length);
        return full.Replace('\\', '/').TrimEnd('/');
    }

    internal List<OverviewMod> PublicationCache(OverviewRepository repository)
    {
        var rows = new List<OverviewMod>();
        foreach (string directory in Directory.GetDirectories(Paths.BepInExRootPath))
        {
            if (new[] { "plugins", "core", "config", "patchers", "monomod", "MCE", "cache", "logs" }.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase)) continue;
            foreach (string path in OverviewRepository.SafeFiles(directory).Where(PluginFileNames.IsAssembly))
            {
                if (path.Substring(directory.Length).Replace('\\','/').Split('/').Any(p => p == "Demo" || p == "备份" || p == "验证" || p.StartsWith("待安装",StringComparison.OrdinalIgnoreCase))) continue;
                if (PluginFileNames.IsBackupCompanion(path)) continue;
                foreach (var record in AssemblyRecords(path))
                {
                    var row = Create(repository, record.Guid, record.Name, record.Version, path, false);
                    row.RuntimeDependencies = record.Dependencies.Select(d => new OverviewDependency { Id=d.Id, MinimumVersion=d.MinimumVersion, Optional=d.Optional }).ToList();
                    row.DependencyMetadataKnown = true; rows.Add(row);
                }
            }
        }
        return rows;
    }

    private static System.Version ParsedVersion(string version) => System.Version.TryParse(version, out var parsed) ? parsed : new System.Version();
    internal static bool IsUpdateCandidate(string path) => PluginFileNames.IsUpdateCandidate(path);
    internal static string CanonicalPluginPath(string path) => PluginFileNames.CanonicalPath(path);
    /// <summary>沿插件目录向上找 manifest.json；物理扫描证据负责校验缺失路径，不长期缓存负结果。</summary>
    private static string? FindManifest(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory == null) return null;
        string? manifest = null;
        string pluginRoot = Path.GetFullPath(Paths.BepInExRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string? probe = directory;
        while (probe != null && (probe + Path.DirectorySeparatorChar).StartsWith(pluginRoot, StringComparison.OrdinalIgnoreCase))
        {
            string candidate = Path.Combine(probe, "manifest.json");
            if (File.Exists(candidate)) { manifest = candidate; break; }
            if (File.Exists(candidate + ".old")) { manifest = candidate + ".old"; break; }
            probe = Path.GetDirectoryName(probe);
        }
        return manifest;
    }

    private static JObject? ManifestData(string manifest)
    {
        int generation=System.Threading.Volatile.Read(ref _cacheGeneration);
        FileInfo info;
        try { info = new FileInfo(manifest); if (!info.Exists) return null; }
        catch { return null; }
        if (ManifestCache.TryGetValue(manifest, out var cached) && cached.Stamp.Matches(info)) return cached.Data;
        JObject? data = null;
        try { data = JObject.Parse(File.ReadAllText(manifest)); }
        catch (Exception ex) { Plugin.Log.LogDebug("[概览] manifest读取失败：" + ex.Message); }
        if(generation==System.Threading.Volatile.Read(ref _cacheGeneration))ManifestCache[manifest] = (new Stamp(info), data);
        return data;
    }

    private static OverviewMod Create(OverviewRepository repository, string guid, string name, string version, string path, bool loaded,
        string? evidenceManifest = null, string? evidenceJson = null, bool physicalEvidence = false)
    {
        string pluginRoot = Path.GetFullPath(Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string? manifest = physicalEvidence ? evidenceManifest : FindManifest(path);
        string root = manifest != null ? Path.GetDirectoryName(manifest)! : Path.GetDirectoryName(path)!;
        // Root-level loose DLLs are independent physical packages.
        if (string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), pluginRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) root = CanonicalPluginPath(path);
        bool loose = string.Equals(Path.GetDirectoryName(Path.GetFullPath(path))!.TrimEnd(Path.DirectorySeparatorChar), pluginRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && PluginFileNames.IsAssembly(path);
        var row = new OverviewMod { Id = guid, Guid = guid, Name = name, RawName = name, SettingDisplayName = Plugin.FixNaming(name), PluginVersion = version, Location = path, PackageRoot = root, Loaded = loaded,
            TargetEnabled = path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), Rule = repository.GetRule(guid), AssemblyPaths = new() { path }, PluginGuids = new() { guid },
            PluginCopies = new() { new OverviewPluginCopy { Guid = guid, Name = name, Version = version, Path = path } }, LooseFile = loose };
        JObject? parsed = physicalEvidence ? (evidenceJson == null ? null : JObject.Parse(evidenceJson))
            : manifest != null ? ManifestData(manifest) : null;
        if (parsed != null)
        try
        {
            JObject data = parsed;
            row.PackageDependencies = data["dependencies"]?.Values<string>().Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d!).ToList() ?? new();
            row.PackageName = data.Value<string>("name") ?? "";
            string packageVersion = data.Value<string>("version_number") ?? "";
            if (Regex.IsMatch(packageVersion, @"^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$")) row.PackageVersion = packageVersion;
            row.Description = data.Value<string>("description") ?? "";
            row.RawDescription = row.Description;
            row.Author = data.Value<string>("author") ?? data.Value<string>("namespace") ?? "";
            if (!string.IsNullOrWhiteSpace(row.Author)) row.AuthorSource = "manifest";
            string website = data.Value<string>("website_url") ?? "";
            if (Uri.TryCreate(website, UriKind.Absolute, out var homepage) &&
                (homepage.Scheme == Uri.UriSchemeHttps || homepage.Scheme == Uri.UriSchemeHttp))
                row.HomepageUrl = homepage.AbsoluteUri;
            string ns = data.Value<string>("namespace") ?? data.Value<string>("author") ?? "";
            if (ThunderstoreUpdateService.ValidPart(ns) && ThunderstoreUpdateService.ValidPart(row.PackageName))
            { row.PackageNamespace = ns; row.SourceNamespace = ns; row.SourceName = row.PackageName; row.SourceConfirmed = false; }
            else
            {
                // A standard Team-Package folder must agree with manifest.name exactly.
                string folder = Path.GetFileName(root);
                if (ThunderstoreUpdateService.TryParseSource(folder, out string folderNs, out string folderName) && folderName == row.PackageName)
                { row.PackageNamespace = folderNs; row.SourceNamespace = folderNs; row.SourceName = folderName; row.SourceConfirmed = false; }
            }
            if (row.SourceNamespace.Length == 0 && ThunderstoreUpdateService.TryParseSource(data.Value<string>("website_url") ?? "", out string linkNs, out string linkName) && linkName == row.PackageName)
            { row.PackageNamespace = linkNs; row.SourceNamespace = linkNs; row.SourceName = linkName; row.SourceConfirmed = false; }
            ModSources.ReadManifestIdentity(row, data, root);
            if (!string.IsNullOrWhiteSpace(row.Description) && XUnityBridge.TryGetExactTranslation(row.Description, out string translated)) row.Description = translated;
        }
        catch (Exception ex) { Plugin.Log.LogDebug("[概览] manifest读取失败：" + ex.Message); }
        if (ModSources.Get(guid)?.note != "auto" && ThunderstoreUpdateService.ValidPart(row.Rule.sourceNamespace) && ThunderstoreUpdateService.ValidPart(row.Rule.sourceName))
        { row.SourceNamespace = row.Rule.sourceNamespace; row.SourceName = row.Rule.sourceName; row.SourceConfirmed = false; }
        if (string.IsNullOrWhiteSpace(row.Author) && row.SourceConfirmed)
        { row.Author = row.SourceNamespace; row.AuthorSource = "store"; }
        if (string.IsNullOrWhiteSpace(row.Author) && Uri.TryCreate(row.HomepageUrl, UriKind.Absolute, out var home) &&
            (home.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || home.Host.Equals("codeberg.org", StringComparison.OrdinalIgnoreCase)))
        {
            var segments = home.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && Regex.IsMatch(segments[0], @"^[A-Za-z0-9_.-]+$"))
            { row.Author = segments[0]; row.AuthorSource = "homepage"; }
        }
        bool infrastructure = guid.Equals(Plugin.PluginGuid, StringComparison.OrdinalIgnoreCase) || guid.IndexOf("peaklib.core", StringComparison.OrdinalIgnoreCase) >= 0
            || guid.IndexOf("peaklib.ui", StringComparison.OrdinalIgnoreCase) >= 0 || guid.IndexOf("peaklib.modconfig", StringComparison.OrdinalIgnoreCase) >= 0;
        string core = Path.GetFullPath(Paths.BepInExRootPath + Path.DirectorySeparatorChar + "core").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (infrastructure || path.StartsWith(core, StringComparison.OrdinalIgnoreCase)) { row.CanToggle = false; row.ToggleReason = Loc.T("The overview and settings pages require this library. It cannot be disabled here.", "概览和设置页依赖此基础模组，不能在这里停用", "概覽和設置頁依賴此基礎模組，不能在這里停用"); }
        if (PluginFileNames.IsForeignDisabled(path))
        { row.CanToggle = false; row.ToggleReason = Loc.T("Disabled by another tool. Restore it using that tool.", "由其他工具停用，请使用原工具恢复", "由其他工具停用，請使用原工具恢復"); }
        ModSources.Apply(row);
        return row;
    }
}


