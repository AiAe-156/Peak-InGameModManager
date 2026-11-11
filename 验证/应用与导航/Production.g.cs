using Newtonsoft.Json.Linq; using System.Text;
partial class Checks {
    private static void CheckDependencies(Plan plan)
    {
        var records = new List<PluginRecord>();
        foreach (string file in SafeFiles(plan.PluginRoot, plan.PluginRoot))
        {
            if (OriginalDllIfPlugin(file) == null || PluginFileNames.IsBackupCompanion(file)) continue;
            string original = OriginalDll(file);
            ExistingFile(original);
            Observe(plan, file);
            var info = ReadPlugins(file);
            bool afterEnabled = plan.Files.TryGetValue(original, out bool target) ? target : file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            if (afterEnabled) records.AddRange(info);
        }
        var byGuid = records.GroupBy(r => r.Guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        // Multiple installed versions alone are advisory; dependency satisfaction can
        // use any enabled provider. File-operation ambiguity is validated separately.
        var disabledGuids = plan.Files.Where(p => !p.Value).SelectMany(p => ReadPlugins(ExistingFile(p.Key)))
            .Select(p => p.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
            foreach (var dependency in record.Dependencies)
            {
                // Existing pack defects do not veto unrelated edits. Newly enabled owners and
                // explicitly removed providers still undergo the complete dependency check.
                if (!(plan.Files.TryGetValue(record.Path, out bool enabled) && enabled) && !disabledGuids.Contains(dependency.Guid)) continue;
                if (!byGuid.TryGetValue(dependency.Guid, out var providers)) throw new InvalidOperationException(Loc.F("{0} requires {1}. Select the related mods too, or keep the dependency enabled.", "{0} 必须依赖 {1}；请同时选择有关模组，或保留该依赖启用。", "{0} 必須依賴 {1}；請同時選擇有關模組，或保留該依賴啟用。", record.Guid, dependency.Guid));
                if (!string.IsNullOrEmpty(dependency.Minimum))
                {
                    if (!System.Version.TryParse(dependency.Minimum, out var minimum)) throw new InvalidOperationException(Loc.F("{0} has an unknown plugin dependency version: {1}", "{0} 的插件依赖版本无法确认：{1}", "{0} 的插件依賴版本無法確認：{1}", record.Guid, dependency.Minimum));
                    if (!providers.Any(p => System.Version.TryParse(p.Version, out var actual) && actual >= minimum))
                        throw new InvalidOperationException(Loc.F("{0} requires {1} version at least {2}。", "{0} 需要 {1} 至少 {2}。", "{0} 需要 {1} 至少 {2}。", record.Guid, dependency.Guid, dependency.Minimum));
                }
            }
    }
    private static void CheckPackageDependencies(Plan plan)
    {
        var manifests = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string manifestPath in SafeFiles(plan.PluginRoot, plan.PluginRoot).Where(f => Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(f).Equals("manifest.json.old", StringComparison.OrdinalIgnoreCase)))
        {
            string directory = Path.GetDirectoryName(manifestPath)!;
            string key = Path.GetFileName(directory);
            Observe(plan, manifestPath);
            JObject manifest = JObject.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
            string ns = manifest.Value<string>("namespace") ?? manifest.Value<string>("author") ?? "";
            string name = manifest.Value<string>("name") ?? "";
            if (ThunderstoreUpdateService.ValidPart(ns) && ThunderstoreUpdateService.ValidPart(name)) key = ns + "-" + name;
            else if (ThunderstoreUpdateService.TryParseSource(key, out var folderNs, out var folderName) && folderName.Equals(name, StringComparison.OrdinalIgnoreCase)) key = folderNs + "-" + folderName;
            else if (ThunderstoreUpdateService.TryParseSource(manifest.Value<string>("website_url") ?? "", out var linkNs, out var linkName) && linkName.Equals(name, StringComparison.OrdinalIgnoreCase)) key = linkNs + "-" + linkName;
            if (plan.PackageIdentities.TryGetValue(directory, out var confirmed)) key = confirmed;
            if (manifests.ContainsKey(key)) throw new InvalidOperationException(Loc.F("Multiple copies have the same package identity. Dependencies are ambiguous: {0}", "安装包身份有多个副本，不能确认依赖：{0}", "安裝包身份有多個副本，不能確認依賴：{0}", key));
            manifests.Add(key, manifest);
            if (plan.PackageRoots.ContainsKey(directory)) changed.Add(key);
            var files = SafeFiles(plan.PluginRoot, directory).ToList();
            bool afterEnabled = plan.PackageRoots.TryGetValue(directory, out bool target) ? target
                : files.Any(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    || (!files.Any(f => OriginalDllIfPlugin(f) != null) && !manifestPath.EndsWith(".old", StringComparison.OrdinalIgnoreCase));
            if (afterEnabled) active.Add(key);
        }
        foreach (string key in active)
        {
            if (!manifests.TryGetValue(key, out var manifest)) continue;
            if (!(manifest["dependencies"] is JArray dependencies)) continue;
            foreach (var item in dependencies)
            {
                string identity = item.Value<string>() ?? "";
                int split = identity.LastIndexOf('-');
                if (split <= 0)
                {
                    if (!changed.Contains(key)) continue;
                    throw new InvalidOperationException(Loc.F("{0} has an unrecognized package dependency format.", "{0} 的包依赖格式无法确认。", "{0} 的包依賴格式無法確認。", key));
                }
                string dependency = identity.Substring(0, split);
                if (!changed.Contains(key) && !changed.Contains(dependency)) continue;
                // BepInEx is the loader, never an ordinary controllable package.
                if (PackageRules.IsLoader(dependency)) continue;
                if (!active.Contains(dependency)) throw new InvalidOperationException(Loc.F("{0} depends on package {1}. Select the related packages too, or keep the dependency enabled.", "{0} 的安装包依赖 {1}；请同时选择相关包或保留依赖启用。", "{0} 的安裝包依賴 {1}；請同時選擇相關包或保留依賴啟用。", key, dependency));
                string required = identity.Substring(split + 1);
                if (!TryPackageVersion(required, out var minimum)) throw new InvalidOperationException(Loc.F("{0} has an unknown package dependency version: {1}", "{0} 的包依赖版本无法确认：{1}", "{0} 的包依賴版本無法確認：{1}", key, required));
                if (!manifests.TryGetValue(dependency, out var provider) || !TryPackageVersion(provider.Value<string>("version_number") ?? "", out var actual) || actual < minimum)
                    throw new InvalidOperationException(Loc.F("{0} requires package {1} version at least {2}。", "{0} 的包依赖需要 {1} 至少 {2}。", "{0} 的包依賴需要 {1} 至少 {2}。", key, dependency, required));
            }
        }
    }
}
