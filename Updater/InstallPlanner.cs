using System.Text.Json;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal sealed class InstallPlanInput
{
    public string Root = "";
    public UpdateJob Job = new();
    public string Stage = "";
    public Dictionary<string, string> Entries = new(Files.Compare);
    public IReadOnlyCollection<string> LoadedAssemblies = Array.Empty<string>();
}

internal sealed record PackageManifest(string Name, string Version, List<string> Dependencies);

/// <summary>Turns an extracted package into a file plan. Local state is the baseline: nothing is compared against an old archive,
/// extra local files stay, plugin identity changes are reported but never block.</summary>
internal static class InstallPlanner
{
    internal const string Version3 = "v3";

    internal static Transaction Plan(InstallPlanInput input)
    {
        var job = input.Job; string root = input.Root; var warnings = job.Warnings;
        Files.ValidateJob(root, job);
        string game = Directory.GetParent(root)!.FullName;
        var plan = new Transaction
        {
            Root = root, PackageRoot = job.PackageRoot, JobId = job.Id, OldVersion = job.InstalledVersion, NewVersion = job.TargetVersion,
            NewTree = input.Stage, SourceProcessId = job.SourceProcessId, Installer = Version3, CanPlaceLive = false, StoreVerified = true, Phase = "prepared"
        };
        var manifest = ReadManifest(input);
        if (manifest != null)
        {
            plan.Dependencies = manifest.Dependencies.ToList();
            if (!manifest.Name.Equals(job.Package, StringComparison.OrdinalIgnoreCase)) warnings.Add("压缩包清单名称（" + manifest.Name + "）与任务包名（" + job.Package + "）不同，按任务继续。");
            if (job.TargetVersion.Length > 0 && manifest.Version != job.TargetVersion) warnings.Add("压缩包清单版本（" + manifest.Version + "）与目标版本（" + job.TargetVersion + "）不同，按压缩包内容继续。");
        }
        else warnings.Add("压缩包没有 manifest.json，无法读取依赖声明。");

        var newPlugins = new Dictionary<string, PluginInfo>(Files.Compare);
        foreach (var entry in input.Entries)
        {
            if (!PluginFileNames.IsAssembly(entry.Key)) continue;
            foreach (var plugin in InstallMetadata.Read(root, Files.Child(input.Stage, entry.Key), warnings))
                if (!newPlugins.ContainsKey(entry.Key)) newPlugins[entry.Key] = plugin;
        }
        var localPlugins = Directory.Exists(job.PackageRoot) ? InstallMetadata.ReadTree(root, job.PackageRoot, warnings) : new List<PluginInfo>();
        var localByGuid = localPlugins.GroupBy(p => p.Guid, Files.Compare).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.File.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList(), Files.Compare);

        // Users move DLLs into their own folder layout; follow the layout the matching plugin already uses.
        var relocation = new Dictionary<string, string>(Files.Compare);
        foreach (var pair in newPlugins)
        {
            if (!pair.Key.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) || !localByGuid.TryGetValue(pair.Value.Guid, out var locals)) continue;
            string zipDirectory = Slash(Path.GetDirectoryName(pair.Key.Substring(8)) ?? "");
            string localDirectory = Slash(Path.GetRelativePath(job.PackageRoot, Path.GetDirectoryName(locals[0].File)!));
            if (localDirectory == ".") localDirectory = "";
            if (relocation.TryGetValue(zipDirectory, out var prior) && !Files.Compare.Equals(prior, localDirectory)) { warnings.Add("本地目录布局与包内不一致，按包内布局放置：" + pair.Key); continue; }
            relocation[zipDirectory] = localDirectory;
        }

        var targets = new HashSet<string>(Files.Compare);
        var plannedTargets = new Dictionary<string, string>(Files.Compare);   // every archive file name → its routed target
        string ownFolder = Path.GetFileName(job.PackageRoot.TrimEnd('\\', '/')) + "/";
        foreach (var entry in input.Entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            string relative = entry.Key; string area = InstallArchive.Area(relative); string rest = relative.Substring(area.Length + 1);
            // Archives (and local imports staged as BepInEx trees) may already carry the package folder; the folder is the target root, not a child of it.
            if (area == "plugins" && rest.StartsWith(ownFolder, StringComparison.OrdinalIgnoreCase)) rest = rest.Substring(ownFolder.Length);
            string target; bool plugin = false;
            if (area == "plugins")
            {
                newPlugins.TryGetValue(relative, out var meta);
                plugin = meta != null;
                if (meta != null && localByGuid.TryGetValue(meta.Guid, out var locals))
                {
                    var local = locals[0];
                    string suffix = local.File.Substring(PluginFileNames.OriginalPath(local.File).Length);
                    target = Path.Combine(Path.GetDirectoryName(local.File)!, Path.GetFileName(rest) + suffix);
                }
                else
                {
                    target = Files.Child(job.PackageRoot, Relocate(rest, relocation));
                    if (meta != null && !job.Enabled && !localPlugins.Any()) target += PluginFileNames.DisabledSuffix;
                }
                if (Path.GetFileName(rest).Equals("manifest.json", StringComparison.OrdinalIgnoreCase) && !File.Exists(target) && File.Exists(target + ".old")) target += ".old";
            }
            else if (area == "config")
            {
                target = Files.Child(root, relative);
                if (File.Exists(target)) { plan.Extras.Add("保留现有配置：" + relative); continue; }
            }
            else target = Files.Child(root, relative);
            string targetRelative = Slash(Path.GetRelativePath(root, target));
            if (InstallArchive.Protected(targetRelative)) throw new IOException("目标受保护，不能自动写入：" + targetRelative);
            Files.RejectLinks(game, target);
            if (!targets.Add(target)) throw new IOException("新版文件映射发生冲突：" + target);
            // Only assemblies can be loaded twice; doc/manifest/icon names are generic and shared between packages.
            if (PluginFileNames.IsAssembly(target)) plannedTargets[Path.GetFileName(target)] = target;
            if (File.Exists(target))
            {
                string existing = Files.Hash(target);
                if (existing == entry.Value) continue;
                plan.Originals.Add(new FileChange { Target = target, Hash = existing });
            }
            plan.Desired.Add(new FileChange { Source = Files.Child(input.Stage, relative), Target = target, Hash = entry.Value, Plugin = plugin });
        }

        var newGuids = new HashSet<string>(newPlugins.Values.Select(p => p.Guid), Files.Compare);        var written = new HashSet<string>(plan.Desired.Select(f => f.Target), Files.Compare);
        foreach (var local in localPlugins)
        {
            if (!newGuids.Contains(local.Guid) || written.Contains(local.File)) continue;
            if (plan.Retired.Any(r => Files.Compare.Equals(r.Target, local.File))) continue;
            plan.Retired.Add(new FileChange { Target = local.File, Hash = Files.Hash(local.File), Plugin = true });
        }
        // A fork and its upstream carry different GUIDs, so the GUID rule above leaves the superseded DLL in place and
        // BepInEx loads both copies of one plugin (double patches, AssetBundle clash). Same plugin type *and* same display
        // name as a new plugin is that case; a localization companion differs in both and stays.
        foreach (var local in localPlugins)
        {
            if (newGuids.Contains(local.Guid) || written.Contains(local.File) || local.TypeName.Length == 0) continue;
            var replacement = newPlugins.Values.FirstOrDefault(p => Files.Compare.Equals(p.TypeName, local.TypeName) && Files.Compare.Equals(p.Name, local.Name));
            if (replacement == null || plan.Retired.Any(r => Files.Compare.Equals(r.Target, local.File))) continue;
            plan.Retired.Add(new FileChange { Target = local.File, Hash = Files.Hash(local.File), Plugin = true });
            warnings.Add("旧插件与新版是同一个插件（" + local.Guid + " " + local.Version + " → " + replacement.Guid + " " + replacement.Version + "），已归档避免重复加载：" + Slash(Path.GetRelativePath(root, local.File)));
        }

        // Startup components are loaded by file, not by folder: a same-named *assembly* left elsewhere under
        // core/ or patchers/ makes BepInEx load the payload twice ("An item with the same key has already been added").
        // Only assemblies are considered — README/CHANGELOG/manifest.json/icon.png names recur across packages and
        // must never be touched. This keys off the package's own file list, so it also runs when the payload is
        // already identical and nothing needs writing (the usual state after a manager-installed package was updated).
        if (plannedTargets.Count > 0)
            foreach (string area in new[] { "core", "patchers" })
                foreach (string file in InstallArchive.Walk(Path.Combine(root, area)))
                {
                    if (!PluginFileNames.IsAssembly(file)) continue;
                    if (!plannedTargets.TryGetValue(Path.GetFileName(file), out var planned) || Files.Compare.Equals(planned, file)) continue;
                    if (plan.Originals.Any(o => Files.Compare.Equals(o.Target, file)) || plan.Retired.Any(r => Files.Compare.Equals(r.Target, file))) continue;
                    plan.Retired.Add(new FileChange { Target = file, Hash = Files.Hash(file) });
                    warnings.Add("归档同名旧副本（否则会重复加载）：" + Slash(Path.GetRelativePath(root, file)));
                }

        var localGuids = new HashSet<string>(localPlugins.Select(p => p.Guid), Files.Compare);        var added = newGuids.Where(g => !localGuids.Contains(g)).ToList();
        var retiredFiles = new HashSet<string>(plan.Retired.Select(r => r.Target), Files.Compare);
        var removed = localPlugins.Where(p => !newGuids.Contains(p.Guid) && !retiredFiles.Contains(p.File)).Select(p => p.Guid).Distinct(Files.Compare).ToList();
        if (localPlugins.Count > 0 && (added.Count > 0 || removed.Count > 0))
            warnings.Add("插件集合有变化：新增 " + (added.Count == 0 ? "无" : string.Join(", ", added)) + "；本地另有 " + (removed.Count == 0 ? "无" : string.Join(", ", removed)) + "（原样保留）。");

        ReplaceStaleManifests(root, game, job, input, manifest, plan, targets, warnings);

        if (Directory.Exists(job.PackageRoot))
        {
            var known = new HashSet<string>(plan.Desired.Select(f => f.Target).Concat(plan.Originals.Select(f => f.Target)).Concat(plan.Retired.Select(f => f.Target)), Files.Compare);
            var extras = InstallArchive.Walk(job.PackageRoot).Where(f => !known.Contains(f)).Select(f => Slash(Path.GetRelativePath(job.PackageRoot, f))).ToList();
            if (extras.Count > 0)
            {
                plan.Extras.AddRange(extras.Select(e => "保留本地额外文件：" + e));
                warnings.Add("保留 " + extras.Count + " 个不在新版包内的本地文件：" + string.Join(", ", extras.Take(5)) + (extras.Count > 5 ? " …" : ""));
            }
        }

        string configHome = Path.Combine(root, "config");
        foreach (string guid in newGuids.Concat(localGuids).Distinct(Files.Compare))
        {
            string config = Path.Combine(configHome, guid + ".cfg");
            if (File.Exists(config) && !plan.Parameters.Any(p => Files.Compare.Equals(p.Target, config))) plan.Parameters.Add(new FileChange { Target = config, Hash = Files.Hash(config) });
        }
        foreach (string config in job.ConfigurationFiles)
            if (Files.Within(configHome, config) && File.Exists(config) && !plan.Parameters.Any(p => Files.Compare.Equals(p.Target, config))) plan.Parameters.Add(new FileChange { Target = config, Hash = Files.Hash(config) });

        var loaded = new HashSet<string>(input.LoadedAssemblies.Select(Physical), Files.Compare);
        bool touchesLoaded = plan.Originals.Concat(plan.Retired).Any(f => loaded.Contains(Physical(f.Target)));
        bool touchesBoot = plan.Originals.Any(f => { string r = Slash(Path.GetRelativePath(root, f.Target)); return r.StartsWith("core/", StringComparison.OrdinalIgnoreCase) || r.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase); });
        plan.RequiresExit = touchesLoaded || touchesBoot;
        return plan;
    }

    /// <summary>Uninstall = move everything the package owns into the job backup. Dependents only produce notes; the user already confirmed.</summary>
    internal static Transaction PlanUninstall(string root, UpdateJob job, IReadOnlyCollection<string> loadedAssemblies)
    {
        var warnings = job.Warnings;
        Files.ValidateJob(root, job);
        if (!Directory.Exists(job.PackageRoot)) throw new IOException("未找到已安装目录：" + job.PackageRoot);
        var plan = new Transaction { Root = root, PackageRoot = job.PackageRoot, JobId = job.Id, OldVersion = job.InstalledVersion, NewVersion = "", Installer = Version3, Phase = "prepared", StoreVerified = false };
        string game = Directory.GetParent(root)!.FullName;
        foreach (string file in InstallArchive.Walk(job.PackageRoot))
        {
            if (InstallArchive.Protected(Slash(Path.GetRelativePath(root, file)))) { warnings.Add("受保护文件未移除：" + Slash(Path.GetRelativePath(root, file))); continue; }
            Files.RejectLinks(game, file);
            plan.Originals.Add(new FileChange { Target = file, Hash = Files.Hash(file), Plugin = PluginFileNames.IsAssembly(file) });
        }
        string key = job.Namespace + "-" + job.Package;
        try
        {
            var storage = new PackStorage(root); string active = storage.Read().ActivePackId;
            string ledgerPath = active.Length > 0 ? Path.Combine(storage.Workspace(active), "shop-installed.json") : Path.Combine(root, "MCE", "shop", "installed-unmanaged.json");
            var owned = File.Exists(ledgerPath) ? Files.Read<ShopLedger>(ledgerPath).Packages.FirstOrDefault(p => Files.Compare.Equals(p.Key, key)) : null;
            foreach (var file in owned?.Files ?? new List<PackFile>())
            {
                string target = Files.Child(root, file.Path);
                if (!File.Exists(target) || Files.Within(job.PackageRoot, target) || InstallArchive.Protected(file.Path) || file.Path.StartsWith("config/", StringComparison.OrdinalIgnoreCase)) continue;
                if (plan.Originals.Any(o => Files.Compare.Equals(o.Target, target))) continue;
                plan.Originals.Add(new FileChange { Target = target, Hash = Files.Hash(target), Plugin = PluginFileNames.IsAssembly(target) });
            }
        }
        catch (Exception e) { warnings.Add("商店账本读取失败（只移除目录内文件）：" + e.Message); }
        var inventory = InstallInventory.Scan(root);
        foreach (var other in inventory.Where(p => !Files.Compare.Equals(p.Root, job.PackageRoot)))
            if (other.Dependencies.Any(d => d.StartsWith(key + "-", StringComparison.OrdinalIgnoreCase))) warnings.Add("仍被依赖：" + other.Name + "（" + Path.GetFileName(other.Root) + "）");
        var loaded = new HashSet<string>(loadedAssemblies.Select(Physical), Files.Compare);
        bool touchesLoaded = plan.Originals.Any(f => loaded.Contains(Physical(f.Target)));
        bool touchesBoot = plan.Originals.Any(f => { string r = Slash(Path.GetRelativePath(root, f.Target)); return r.StartsWith("core/", StringComparison.OrdinalIgnoreCase) || r.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase); });
        plan.RequiresExit = touchesLoaded || touchesBoot;
        return plan;
    }

    /// <summary>
    /// 2.4.4: the same package can leave manifest.json in other places than the one this update writes (MonoDetour:
    /// core\MonoDetour-MonoDetour from a manual install vs plugins\MonoDetour-MonoDetour from the store; Profile_Cards: the
    /// package root vs its nested plugins\). Leaving a lower-version copy behind made the overview offer the same update forever.
    /// Every other copy of this package key in core/patchers/plugins whose version is lower than the new one is backed up
    /// (Originals) and replaced by the archive's own manifest (Desired), so commit and rollback cover it like any other file.
    /// </summary>
    static void ReplaceStaleManifests(string root, string game, UpdateJob job, InstallPlanInput input, PackageManifest? manifest, Transaction plan, HashSet<string> targets, List<string> warnings)
    {
        if (manifest == null || manifest.Version.Length == 0) return;
        string key = job.Namespace + "-" + job.Package;
        var source = input.Entries.Keys.FirstOrDefault(k => k.Equals("plugins/manifest.json", StringComparison.OrdinalIgnoreCase))
            ?? input.Entries.Keys.FirstOrDefault(k => Path.GetFileName(k).Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (source == null) return;
        string sourceFile = Files.Child(input.Stage, source), sourceHash = input.Entries[source];
        foreach (string area in new[] { "core", "patchers", "plugins" })
            foreach (string file in InstallArchive.Walk(Path.Combine(root, area)))
            {
                if (!Path.GetFileName(file).Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || targets.Contains(file)) continue;
                var copy = ManifestCopies.Read(file, File.ReadAllText);
                if (copy == null || !Files.Compare.Equals(copy.Key, key) || ManifestCopies.Compare(copy.Version, manifest.Version) >= 0) continue;
                string relative = Slash(Path.GetRelativePath(root, file));
                if (InstallArchive.Protected(relative)) continue;
                Files.RejectLinks(game, file);
                targets.Add(file);
                plan.Originals.Add(new FileChange { Target = file, Hash = Files.Hash(file) });
                plan.Desired.Add(new FileChange { Source = sourceFile, Target = file, Hash = sourceHash });
                warnings.Add("同包旧说明文件（" + copy.Version + "）一并换成 " + manifest.Version + "：" + relative);
            }
    }

    internal static PackageManifest? ReadManifest(InstallPlanInput input)
    {
        var key = input.Entries.Keys.FirstOrDefault(k => k.Equals("plugins/manifest.json", StringComparison.OrdinalIgnoreCase))
            ?? input.Entries.Keys.FirstOrDefault(k => Path.GetFileName(k).Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
        if (key == null) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Files.Child(input.Stage, key)));
            var value = document.RootElement;
            string name = value.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string version = value.TryGetProperty("version_number", out var v) ? v.GetString() ?? "" : "";
            var dependencies = value.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Array ? d.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : new List<string>();
            return new PackageManifest(name, version, dependencies);
        }
        catch (JsonException) { return null; }
    }

    static string Relocate(string rest, Dictionary<string, string> relocation)
    {
        string directory = Slash(Path.GetDirectoryName(rest) ?? ""); string leaf = Path.GetFileName(rest);
        string? best = relocation.Keys.Where(k => k.Length > 0 && (directory.Equals(k, StringComparison.OrdinalIgnoreCase) || directory.StartsWith(k + "/", StringComparison.OrdinalIgnoreCase))).OrderByDescending(k => k.Length).FirstOrDefault();
        string mapped;
        if (best != null) mapped = Join(relocation[best], directory.Substring(best.Length).TrimStart('/'));
        else if (directory.Length == 0 && relocation.TryGetValue("", out var top)) mapped = top;
        else mapped = directory;
        return Join(mapped, leaf);
    }
    static string Join(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : a.TrimEnd('/') + "/" + b;
    static string Slash(string path) => path.Replace('\\', '/');
    static string Physical(string path) { try { return Files.PhysicalPath(path); } catch { return Path.GetFullPath(path); } }
}
