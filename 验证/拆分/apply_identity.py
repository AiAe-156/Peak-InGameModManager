# -*- coding: utf-8 -*-
"""拆分：逐处把写死的旧身份换成 ManagerIdentity（精确整句替换，每条必须命中指定次数，否则整体停下）。
写入一律新名；判断「是不是管理器 / 管理目录」的地方新旧都认。
用法：python -I apply_identity.py <工程根> [--apply]"""
import io
import os
import sys

root = sys.argv[1]
apply = "--apply" in sys.argv
I = "ModConfigEnhance.Updating.ManagerIdentity"   # 插件侧全名
U = "ManagerIdentity"                              # 已 using ModConfigEnhance.Updating 的文件

E = []  # (file, old, new, count)


def e(f, old, new, n=1):
    E.append((f, old, new, n))


# ── 插件身份 ──
e("src/Plugin.cs", 'public const string PluginGuid = "com.aiae.modconfigenhance";', 'public const string PluginGuid = Updating.ManagerIdentity.Guid;')
e("src/Plugin.cs", 'string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "MCE", "worker-instance.lock");', 'string path = Updating.ManagerIdentity.Runtime(BepInEx.Paths.BepInExRootPath, "worker-instance.lock");')
e("src/Updating/ComponentDependencyGraph.cs", 'public const string MceGuid = "com.aiae.modconfigenhance";', 'public const string MceGuid = ManagerIdentity.Guid;')
e("src/Updating/ComponentDependencyGraph.cs", '&& (Compare.Equals(guid, MceGuid) || MceRequiredIdentities.Contains(guid, Compare));', '&& (ManagerIdentity.IsManagerGuid(guid) || MceRequiredIdentities.Contains(guid, Compare));')
e("src/Updating/ComponentDependencyGraph.cs", 'foreach (var seed in Components.Where(c => c.PluginGuids.Contains(MceGuid, Compare)))', 'foreach (var seed in Components.Where(c => c.PluginGuids.Any(ManagerIdentity.IsManagerGuid)))')
e("src/Updating/GroupFolderPlanner.cs", 'public string ManagerGuid = "com.aiae.modconfigenhance";', 'public string ManagerGuid = ManagerIdentity.Guid;')
e("src/Updating/GroupFolderPlanner.cs", "parts[1].StartsWith(\"MCEPack_\", StringComparison.OrdinalIgnoreCase)", "ManagerIdentity.IsPackFolderPrefixed(parts[1])")
e("src/Updating/InstallationLayoutPlanner.cs", 'IgnoreCase.Equals(p.Id, "com.aiae.modconfigenhance")', 'ManagerIdentity.IsManagerGuid(p.Id)')
e("src/Diagnostics/RuntimeLogCapture.cs", 'MceLogApi.CreateChannel("com.aiae.modconfigenhance", "runtime-timestamped"', 'MceLogApi.CreateChannel(Plugin.PluginGuid, "runtime-timestamped"')
e("src/Diagnostics/RuntimeLogCapture.cs", 'MceLogApi.CreateChannel("com.aiae.modconfigenhance", "language-detail"', 'MceLogApi.CreateChannel(Plugin.PluginGuid, "language-detail"')

# ── 打包 / 发布过滤（管理器的配置与运行目录不进整合包；新旧都认）──
e("src/Overview/PublicationFilePolicy.cs", '&& !file.Equals("com.aiae.modconfigenhance.cfg", StringComparison.OrdinalIgnoreCase)', f'&& !{I}.IsManagerConfigFile(file)')
e("src/Overview/PublicationFilePolicy.cs", '''            && !path.StartsWith("ModConfigEnhance/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("MCE/", StringComparison.OrdinalIgnoreCase)''', f'''            && !{I}.IsConfigDirectory(path.Split('/')[0])
            && !{I}.UnderRuntime(path)''')
e("src/Overview/PublicationFilePolicy.cs", 'parts[1].Equals("MCE", StringComparison.OrdinalIgnoreCase)', f'{I}.IsRuntimeDirectory(parts[1])')
e("src/Overview/PublicationFilePolicy.cs", 'p.Equals("MCEPack", StringComparison.OrdinalIgnoreCase)', f'{I}.IsPackFolder(p)')
e("src/Overview/PublicationFilePolicy.cs", '''        string root = Path.GetFullPath(Path.Combine(bepinexDirectory, "MCE")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(file).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);''',
  f'''        string full = Path.GetFullPath(file).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (string name in new[] {{ {I}.RuntimeDirectory, {I}.LegacyRuntimeDirectory }})
        {{
            string root = Path.GetFullPath(Path.Combine(bepinexDirectory, name)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
        }}
        return false;''')
e("src/Updating/PackContracts.cs", '''        return Equal(p, "com.aiae.modconfigenhance.cfg") || Equal(p, "BepInEx.cfg") || Equal(p, "AutoTranslatorConfig.ini")
            || Under(p, "ModConfigEnhance") || Under(p, "MCE") || Under(p, "Translation")''', '''        return ManagerIdentity.IsManagerConfigFile(p) || Equal(p, "BepInEx.cfg") || Equal(p, "AutoTranslatorConfig.ini")
            || Under(p, ManagerIdentity.ConfigDirectory) || Under(p, ManagerIdentity.LegacyConfigDirectory)
            || Under(p, ManagerIdentity.RuntimeDirectory) || Under(p, ManagerIdentity.LegacyRuntimeDirectory) || Under(p, "Translation")''')
e("src/Updating/PackContracts.cs", '''    public static bool ManagementConfig(string relative) => Equal(relative, "com.aiae.modconfigenhance.cfg")
        || Equal(relative, "ModConfigEnhance") || Under(relative, "ModConfigEnhance")
        || Equal(relative, "MCE") || Under(relative, "MCE");''', '''    public static bool ManagementConfig(string relative) => ManagerIdentity.IsManagerConfigFile(relative)
        || ManagerIdentity.IsConfigDirectory(relative) || Under(relative, ManagerIdentity.ConfigDirectory) || Under(relative, ManagerIdentity.LegacyConfigDirectory)
        || ManagerIdentity.IsRuntimeDirectory(relative) || Under(relative, ManagerIdentity.RuntimeDirectory) || Under(relative, ManagerIdentity.LegacyRuntimeDirectory);''')
e("src/Updating/UpdateContracts.cs", 'private static readonly string[] Reserved = { "core", "config", "patchers", "plugins", "MCE", "cache", "interop", "unhollowed", "monomod" };',
  'private static readonly string[] Reserved = { "core", "config", "patchers", "plugins", ManagerIdentity.RuntimeDirectory, ManagerIdentity.LegacyRuntimeDirectory, "cache", "interop", "unhollowed", "monomod" };')
e("src/Updating/UpdateContracts.cs", 'string home = Path.Combine(installationRoot, "MCE");', 'string home = ManagerIdentity.Runtime(installationRoot);')
e("src/Updating/UpdateWriteMarker.cs", 'public static string PathFor(string root) => Path.Combine(root, "MCE", "updating.lock");', 'public static string PathFor(string root) => ManagerIdentity.Runtime(root, "updating.lock");')
e("src/Updating/WorkerProtocol.cs", 'public const string Executable = "MCE.UpdateWorker.exe";', 'public const string Executable = ManagerIdentity.WorkerExecutable;')

# ── 插件侧运行目录 BepInEx\MCE → BepInEx\IGMM ──
for f, old in [
    ("src/MceDirectoryMigration.cs", 'Path.Combine(BepInEx.Paths.BepInExRootPath, "MCE", "layout-backups")'),
    ("src/Overview/ConfigurationPackageInventory.cs", 'Path.Combine(BepInEx.Paths.BepInExRootPath, "MCE", "shop", "installed-unmanaged.json")'),
    ("src/Overview/ModSources.Installed.cs", 'Path.Combine(BepInEx.Paths.BepInExRootPath, "MCE", "shop", "installed-unmanaged.json")'),
    ("src/Overview/PackPublisher.Environment.cs", 'Path.Combine(Paths.BepInExRootPath, "MCE", "shop", "installed-unmanaged.json")'),
    ("src/Overview/ManagerProfileSync.cs", 'Path.Combine(Paths.BepInExRootPath, "MCE", "manager-sync.json")'),
    ("src/Overview/ManagerProfileSync.cs", 'Path.Combine(Paths.BepInExRootPath, "MCE", "manager-sync-backups")'),
    ("src/Overview/OverviewPageCache.cs", 'Path.Combine(Paths.BepInExRootPath, "MCE", "packs", "index.json")'),
    ("src/Overview/OverviewUpdateQueue.cs", 'Path.Combine(Paths.BepInExRootPath, "MCE")'),
    ("src/Overview/PackLibrary.cs", 'Path.Combine(Paths.BepInExRootPath, "MCE", "packs")'),
    ("src/Overview/PackLibrary.cs", 'Path.Combine(Paths.BepInExRootPath, "MCE", "pack-task.json")'),
    ("src/Overview/ShopWindow.cs", 'Path.Combine(BepInEx.Paths.BepInExRootPath,"MCE","shop")'),  # 两处（Home 与 task.json）
    ("src/Overview/ModToggleService.cs", 'Path.Combine(bepinexRoot, "MCE", "shop", "installed-unmanaged.json")'),
]:
    import re
    args = re.match(r'Path\.Combine\((.*)\)$', old).group(1)
    parts = [a.strip() for a in args.split(",")]
    base, rest = parts[0], [p for p in parts[1:] if p != '"MCE"']
    new = f'{I}.Runtime({base}' + (", " + ", ".join(rest) if rest else "") + ")"
    E.append((f, old, new, None))
e("src/Overview/SourceUi.cs", 'System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"MCE"', f'{I}.Runtime(BepInEx.Paths.BepInExRootPath')
e("src/Overview/OverviewNetworkSettings.cs", 'OverviewProfileAssets.CheckedPath(BepInEx.Paths.BepInExRootPath, "MCE/probes")', f'OverviewProfileAssets.CheckedPath(BepInEx.Paths.BepInExRootPath, {I}.RuntimeDirectory + "/probes")')
e("src/Overview/OverviewCatalogue.cs", 'new[] { "plugins", "core", "config", "patchers", "monomod", "MCE", "cache", "logs" }', f'new[] {{ "plugins", "core", "config", "patchers", "monomod", {I}.RuntimeDirectory, {I}.LegacyRuntimeDirectory, "cache", "logs" }}')
e("src/Overview/PackPublisher.cs", 'new[] { "plugins", "config", "core", "patchers", "monomod", "MCE"', f'new[] {{ "plugins", "config", "core", "patchers", "monomod", {I}.RuntimeDirectory, {I}.LegacyRuntimeDirectory')
e("src/Overview/PackPublisher.cs", '!new[]{"config","MCE",', f'!new[]{{"config",{I}.RuntimeDirectory,{I}.LegacyRuntimeDirectory,')
e("src/Overview/ShopInstalled.cs", 'new[]{"plugins","core","patchers","config","MCE",', f'new[]{{"plugins","core","patchers","config",{I}.RuntimeDirectory,{I}.LegacyRuntimeDirectory,')
for label in ('"MCE/downloads"', '"MCE/packs/.view-cache"', '"MCE/shop/tasks"', '"MCE/packs/.transactions"', '"MCE/packs/.trash"'):
    e("src/Overview/PackStorageBrowser.cs", label, f'{I}.RuntimeDirectory + "{label[4:]}')

# ── 包键 / DLL / 配置目录 ──
e("src/Overview/ShopInstallationState.cs", 'key.Equals("AiAeT-ModConfigEnha', 'ModConfigEnhance.Updating.ManagerIdentity.IsManagerPackage(key) || key.Equals("AiAeT-ModConfigEnha')
e("src/Loc.cs", 'internal static string RootDirectory => Path.Combine(Paths.ConfigPath, "ModConfigEnhance");', 'internal static string RootDirectory => Path.Combine(Paths.ConfigPath, Updating.ManagerIdentity.ConfigDirectory);')
e("src/Overview/Authoring/IntroductionFontRegistry.cs", 'Path.Combine(BepInEx.Paths.ConfigPath, "ModConfigEnhance", "fonts")', f'Path.Combine(BepInEx.Paths.ConfigPath, {I}.ConfigDirectory, "fonts")')
e("src/Overview/Authoring/OverviewDocumentView.cs", 'Path.Combine(exportRoot,"config","ModConfigEnhance","language")', f'Path.Combine(exportRoot,"config",{I}.ConfigDirectory,"language")')
e("src/Overview/Authoring/OverviewDocumentView.cs", 'Path.Combine(exportRoot,"config","ModConfigEnhance","fonts")', f'Path.Combine(exportRoot,"config",{I}.ConfigDirectory,"fonts")')
e("src/Overview/Authoring/OverviewDocumentView.cs", 'Path.Combine(exportRoot,"plugins","MCEPack")', f'Path.Combine(exportRoot,"plugins",{I}.PackFolder)')
e("src/Overview/OverviewConfigService.cs", 'Path.Combine(_configRoot, "ModConfigEnhance")', f'Path.Combine(_configRoot, {I}.ConfigDirectory)')
e("src/Overview/OverviewPageCache.cs", 'FullRoot(Path.Combine(Paths.ConfigPath, "ModConfigEnhance"))', f'FullRoot(Path.Combine(Paths.ConfigPath, {I}.ConfigDirectory))')
e("src/Overview/OverviewProfileStore.cs", 'Path.Combine(Paths.ConfigPath, "ModConfigEnhance", "profiles")', f'Path.Combine(Paths.ConfigPath, {I}.ConfigDirectory, "profiles")')
e("src/Overview/PackPublisher.cs", '!relative.Replace(\'\\\\\', \'/\').StartsWith("ModConfigEnhance/", StringComparison.OrdinalIgnoreCase)', f'!{I}.IsConfigDirectory(relative.Replace(\'\\\\\', \'/\').Split(\'/\')[0])')
e("src/Overview/PublishedPack.cs", 'Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ModConfigEnhance", "signing", "author-key.json")', f'Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), {I}.ConfigDirectory, "signing", "author-key.json")')
e("src/Updating/MceDirectories.cs", 'internal static string RootFromBepInEx(string bepInExRoot) => Path.Combine(bepInExRoot, "config", "ModConfigEnhance");', 'internal static string RootFromBepInEx(string bepInExRoot) => Path.Combine(bepInExRoot, "config", ManagerIdentity.ConfigDirectory);')
e("src/Overview/ShopNetwork.cs", 'ParseAdd("ModConfigEnhance/2.0.0")', 'ParseAdd(ModConfigEnhance.Updating.ManagerIdentity.AssemblyName + "/" + Plugin.PluginVersion)')

# ── 作者资料夹 MCEPack → IGMMPack（写新名；识别新旧）──
e("src/Overview/OverviewProfileExport.cs", 'OverviewProfileAssets.CheckedPath(exportRoot, "plugins/MCEPack")', f'OverviewProfileAssets.CheckedPath(exportRoot, "plugins/" + {I}.PackFolder)')
e("src/Overview/OverviewProfileStore.cs", '!Path.GetFileName(packDir).Equals("MCEPack", StringComparison.OrdinalIgnoreCase)', f'!{I}.IsPackFolder(Path.GetFileName(packDir))', 2)
e("src/Overview/OverviewRepository.cs", 'string.Equals(Path.GetFileName(Path.GetDirectoryName(p)), "MCEPack", StringComparison.OrdinalIgnoreCase)', f'{I}.IsPackFolder(Path.GetFileName(Path.GetDirectoryName(p)))')
e("src/Overview/PackLibrary.cs", 'string.Equals(Path.GetFileName(Path.GetDirectoryName(f.Path)), "MCEPack", StringComparison.OrdinalIgnoreCase)', f'{I}.IsPackFolder(Path.GetFileName(Path.GetDirectoryName(f.Path)))')
e("src/Overview/PublishedPack.cs", 'Path.GetFileName(Path.GetDirectoryName(p)) == "MCEPack"', f'{I}.IsPackFolder(Path.GetFileName(Path.GetDirectoryName(p)))')
e("src/Overview/OverviewPageCache.cs", 'full.Replace(\'\\\\\', \'/\').IndexOf("/MCEPack/", StringComparison.OrdinalIgnoreCase) >= 0', f'{I}.ContainsPackFolder(full)')
e("src/Overview/PackPreviewLanguage.cs", '''            int at = p.IndexOf("/MCEPack/language/", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) directories.Add(OverviewProfileAssets.CheckedPath(root, p[..(at + "/MCEPack/language".Length)]));''',
  f'''            int at = {I}.IndexOfPackSubfolder(p, "language", out int marker);
            if (at >= 0) directories.Add(OverviewProfileAssets.CheckedPath(root, p[..(at + marker - 1)]));''')
e("src/Overview/PackPublisher.Environment.cs", 'skipPrefix: "BepInEx/plugins/" + request.Name + "/MCEPack/"', f'skipPrefix: "BepInEx/plugins/" + request.Name + "/" + {I}.PackFolder + "/"')
e("src/Overview/PackPublisher.Environment.cs", 'internal const string EnvironmentFile = "MCEEnvironment.json";', f'internal const string EnvironmentFile = {I}.EnvironmentFile;')
e("src/Overview/PackPublisher.Environment.cs", 'a.ConstructorArguments[0].Value?.ToString() == "MCE.WorkerProtocol"', 'a.ConstructorArguments[0].Value?.ToString() is "IGMM.WorkerProtocol" or "MCE.WorkerProtocol"')
e("src/Overview/PackPublisher.cs", 'string payload = request.Mode == PublishMode.Data ? "plugins/MCEPack/" : "BepInEx/plugins/" + request.Name + "/MCEPack/";', f'string payload = request.Mode == PublishMode.Data ? "plugins/" + {I}.PackFolder + "/" : "BepInEx/plugins/" + request.Name + "/" + {I}.PackFolder + "/";')
e("src/Overview/PackPublisher.cs", 'string payload = Path.Combine(export, "plugins", "MCEPack");', f'string payload = Path.Combine(export, "plugins", {I}.PackFolder);')
e("src/Overview/PackPublisher.cs", 'request.Mode == PublishMode.Data ? "plugins/MCEPack/" : "BepInEx/plugins/" + request.Name + "/MCEPack/");', f'request.Mode == PublishMode.Data ? "plugins/" + {I}.PackFolder + "/" : "BepInEx/plugins/" + request.Name + "/" + {I}.PackFolder + "/");')
e("src/Overview/PackPublisher.cs", 'CopyTree(payload, Path.Combine(output, "BepInEx", "plugins", request.Name, "MCEPack"), preservePublishedFiles: true);', f'CopyTree(payload, Path.Combine(output, "BepInEx", "plugins", request.Name, {I}.PackFolder), preservePublishedFiles: true);')
e("src/Overview/PackPublisher.cs", 'skipPrefix: "BepInEx/plugins/" + request.Name + "/MCEPack/");', f'skipPrefix: "BepInEx/plugins/" + request.Name + "/" + {I}.PackFolder + "/");')
e("src/Overview/PublicationDrafts.cs", 'internal string PayloadPrefix => _request.Mode == PublishMode.Data ? "plugins/MCEPack/" : "BepInEx/plugins/" + _request.Name + "/MCEPack/";', f'internal string PayloadPrefix => _request.Mode == PublishMode.Data ? "plugins/" + {I}.PackFolder + "/" : "BepInEx/plugins/" + _request.Name + "/" + {I}.PackFolder + "/";')
e("src/Overview/PublishFilePreview.cs", 'normalized.IndexOf("/MCEPack/profiles/", StringComparison.OrdinalIgnoreCase) >= 0 || normalized.StartsWith("plugins/MCEPack/profiles/", StringComparis', f'{I}.IndexOfPackSubfolder("/" + normalized, "profiles", out _) >= 0 || normalized.StartsWith("plugins/MCEPack/profiles/", StringComparis')
e("src/Overview/PublishFilePreview.cs", 'normalized.Contains("/MCEPack/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("plugins/MCEPack/", StringComparison.OrdinalIgnoreCase)', f'{I}.ContainsPackFolder("/" + normalized)')
e("src/Overview/StoredPackDetails.cs", 'private const string AuthorLanguageFolder = "/MCEPack/language/";', 'private static readonly string[] AuthorLanguageFolders = { "/" + ModConfigEnhance.Updating.ManagerIdentity.PackFolder + "/language/", "/" + ModConfigEnhance.Updating.ManagerIdentity.LegacyPackFolder + "/language/" };')
e("src/Overview/PublishWizard.cs", '"MCEPack_" + PackPublisher.PackId()', f'{I}.PackFolder + "_" + PackPublisher.PackId()')

# ── 恢复入口 / 进度窗 / 助手程序名 ──
e("src/Overview/RecoveryLauncher.cs", 'internal const string ScriptName = "MCE-Recovery.ps1";', f'internal const string ScriptName = {I}.RecoveryScript;')
e("src/Overview/RecoveryLauncher.cs", 'internal const string EntryName = "MCE-Recovery.cmd";', f'internal const string EntryName = {I}.RecoveryEntry;')
e("src/Overview/RecoveryLauncher.cs", "@('MCE.UpdateWorker.exe','Mono.Cecil.LICENSE.txt'", "@('IGMM.UpdateWorker.exe','Mono.Cecil.LICENSE.txt'")
e("src/Overview/RecoveryLauncher.cs", "$info.FileName=Join-Path $tools 'MCE.UpdateWorker.exe'", "$info.FileName=Join-Path $tools 'IGMM.UpdateWorker.exe'")
e("src/Overview/OverviewUpdateQueue.cs", 'new ProcessStartInfo(Path.Combine(tools, "MCE.UpdateWorker.exe")', 'new ProcessStartInfo(Path.Combine(tools, ModConfigEnhance.Updating.ManagerIdentity.WorkerExecutable)', 2)
e("src/Overview/PackSwitchProgressWindow.cs", 'string viewer = Path.Combine(tools, "MCE.Desktop.exe");', 'string viewer = Path.Combine(tools, ModConfigEnhance.Updating.ManagerIdentity.DesktopExecutable);')

# ── 更新助手 ──
W = "Updater/"
e(W + "LayoutPlan.cs", 'discovered.Any(p => Files.Compare.Equals(p.Guid, "com.aiae.modconfigenhance"))', 'discovered.Any(p => ManagerIdentity.IsManagerGuid(p.Guid))')
e(W + "PackCompleteEnvironment.cs", '''if (!assemblies.Any(a => a.Name == "ModConfigEnhance" && a.Path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase))''', '''if (!assemblies.Any(a => ManagerIdentity.IsManagerAssembly(a.Name) && a.Path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase))''')
e(W + "PackCompleteEnvironment.cs", '|| !plugins.Any(p => p.Guid == "com.aiae.modconfigenhance")) issues.Add(', '|| !plugins.Any(p => ManagerIdentity.IsManagerGuid(p.Guid))) issues.Add(')
e(W + "PackCompleteEnvironment.cs", 'reference.Name is "0Harmony" or "ModConfigEnhance"', 'reference.Name == "0Harmony" || ManagerIdentity.IsManagerAssembly(reference.Name)')
e(W + "PackCompleteEnvironment.cs", 'Path.Combine(storage.Root, "MCE", "shop", "installed-unmanaged.json")', 'ManagerIdentity.Runtime(storage.Root, "shop", "installed-unmanaged.json")')
e(W + "PackImportTarget.cs", 'if (!plugins.Any(p => p.Guid == "com.aiae.modconfigenhance")) issues.Add(', 'if (!plugins.Any(p => ManagerIdentity.IsManagerGuid(p.Guid))) issues.Add(')
e(W + "PackMaintenance.cs", 'Files.Compare.Equals(guid, "com.aiae.modconfigenhance")', 'ManagerIdentity.IsManagerGuid(guid)')
e(W + "PackMaintenance.cs", 'Path.Combine(storage.Root, "MCE", "downloads")', 'ManagerIdentity.Runtime(storage.Root, "downloads")')
e(W + "PackMaintenance.cs", 'Path.Combine(storage.Root,"MCE","shop","tasks")', 'ManagerIdentity.Runtime(storage.Root,"shop","tasks")')
e(W + "PackMaintenance.cs", 'Path.Combine(storage.Root,"MCE","shop","task.json")', 'ManagerIdentity.Runtime(storage.Root,"shop","task.json")')
e(W + "PackMaintenance.cs", 'Path.Combine(storage.Root, "MCE", "maintenance")', 'ManagerIdentity.Runtime(storage.Root, "maintenance")')
e(W + "PackMaintenance.cs", 'path.Contains("/MCEPack/", StringComparison.OrdinalIgnoreCase)', 'ManagerIdentity.ContainsPackFolder(path)')
e(W + "PackTransactions.cs", 'string file = Path.Combine(storage.Root, "config", "com.aiae.modconfigenhance.cfg");', 'string file = Path.Combine(storage.Root, "config", ManagerIdentity.ConfigFileName);')
e(W + "PackTransactions.cs", '!index.HostPlugins.Any(p => p.Guid == "com.aiae.modconfigenhance")) throw', '!index.HostPlugins.Any(p => ManagerIdentity.IsManagerGuid(p.Guid))) throw')
e(W + "PackTransactions.cs", 'string old = Path.Combine(storage.Root, "config", "ModConfigEnhance"), workspace', 'string old = Path.Combine(storage.Root, "config", ManagerIdentity.ConfigDirectory), workspace')
e(W + "PackTransactions.cs", 'string shopLedger=Path.Combine(storage.Root,"MCE","shop","installed-unmanaged.json");', 'string shopLedger=ManagerIdentity.Runtime(storage.Root,"shop","installed-unmanaged.json");')
e(W + "PackTransactions.cs", 'const string AuthorLanguage = "/MCEPack/language/";', 'static readonly string[] AuthorLanguages = { "/" + ManagerIdentity.PackFolder + "/language/", "/" + ManagerIdentity.LegacyPackFolder + "/language/" };')
e(W + "ShopCoreBatch.cs", 'Files.Compare.Equals(p.Key,"AiAeT-ModConfigEnhance")', 'ManagerIdentity.IsManagerPackage(p.Key)')
e(W + "ShopDependencyValidation.cs", 'var required = new HashSet<string>(Files.Compare) { "com.aiae.modconfigenhance" };', 'var required = new HashSet<string>(Files.Compare) { ManagerIdentity.Guid, ManagerIdentity.LegacyGuid };')
e(W + "ShopLocalImport.cs", '''if (Path.GetFileName(entry.Path).Equals("ModConfigEnhance.dll", StringComparison.OrdinalIgnoreCase)
                    || package.PluginGuids.Contains("com.aiae.modconfigenhance", Files.Compare))''', '''if (ManagerIdentity.IsManagerDll(Path.GetFileName(entry.Path))
                    || package.PluginGuids.Any(ManagerIdentity.IsManagerGuid))''')
e(W + "InstallDependencies.cs", 'key.Equals("AiAeT-ModConfigEnhance", StringComparison.OrdinalIgnoreCase)', 'ManagerIdentity.IsManagerPackage(key)')
e(W + "ShopQueue.cs", 'if(entry.Key=="AiAeT-ModConfigEnhance"&&!reuse)', 'if(ManagerIdentity.IsManagerPackage(entry.Key)&&!reuse)')
e(W + "WorkerTargetProtocol.cs", 'if (module.Assembly.Name.Name != "ModConfigEnhance") throw', 'if (!ManagerIdentity.IsManagerAssembly(module.Assembly.Name.Name)) throw')
e(W + "WorkerTargetProtocol.cs", 'string tools = Path.Combine(root, "MCE", "tools");', 'string tools = ManagerIdentity.Runtime(root, "tools");')
e(W + "PackImportEnvironment.cs", 'internal const string FileName = "MCEEnvironment.json";', 'internal const string FileName = ManagerIdentity.EnvironmentFile;')
e(W + "Program.cs", '@"Local\\MCE.UpdateWorker.FatalDialog"', '@"Local\\IGMM.UpdateWorker.FatalDialog"')
e(W + "Program.cs", 'string home = Path.Combine(root, "MCE"); Files.RejectLinks', 'string home = ManagerIdentity.Runtime(root); Files.RejectLinks')
e(W + "StoreClient.cs", 'ParseAdd("MCE.UpdateWorker/"', 'ParseAdd("IGMM.UpdateWorker/"')
e(W + "QueueWorker.cs", 'home = Path.Combine(root, "MCE");', 'home = ManagerIdentity.Runtime(root);')
e(W + "NetworkProbe.cs", 'string home = Path.Combine(root, "MCE");', 'string home = ManagerIdentity.Runtime(root);')
e(W + "ManagerProfileSync.cs", 'internal static string PathFor(string root) => Path.Combine(root, "MCE", FileName);', 'internal static string PathFor(string root) => ManagerIdentity.Runtime(root, FileName);')
e(W + "InstallPlanner.cs", 'Path.Combine(root, "MCE", "shop", "installed-unmanaged', 'ManagerIdentity.Runtime(root, "shop", "installed-unmanaged')
e(W + "Installer.cs", 'Path.Combine(plan.Root, "MCE", "shop", "installed-unmanaged.json"', 'ManagerIdentity.Runtime(plan.Root, "shop", "installed-unmanaged.json"', 2)
e(W + "PackImports.cs", 'Path.Combine(storage.Root, "MCE", "downloads")', 'ManagerIdentity.Runtime(storage.Root, "downloads")')
e(W + "PackImports.cs", 'new[] { "BepInEx", "plugins", "config", "patchers", "core", "monomod", "MCEPack" }.Contains(Path.GetFileName(child), Files.Compare)', 'new[] { "BepInEx", "plugins", "config", "patchers", "core", "monomod", ManagerIdentity.PackFolder, ManagerIdentity.LegacyPackFolder }.Contains(Path.GetFileName(child), Files.Compare)')
e(W + "PackImports.cs", 'Path.GetFileName(Path.GetDirectoryName(p))!.Equals("MCEPack", StringComparison.OrdinalIgnoreCase)', 'ManagerIdentity.IsPackFolder(Path.GetFileName(Path.GetDirectoryName(p)))', 2)
e(W + "PackPersonalCopy.cs", 'Path.GetFileName(p).Equals("MCEPack", StringComparison.OrdinalIgnoreCase)', 'ManagerIdentity.IsPackFolder(Path.GetFileName(p))')
e(W + "PackQueue.cs", 'Path.Combine(storage.Root, "MCE", "jobs", job.Id, "transaction.json")', 'ManagerIdentity.Runtime(storage.Root, "jobs", job.Id, "transaction.json")')
e(W + "PackResolvedImport.cs", 'relative.StartsWith("MCE/", StringComparison.OrdinalIgnoreCase)', 'ManagerIdentity.UnderRuntime(relative)')
e(W + "PackStorage.cs", 'Home = Path.Combine(root, "MCE", "packs");', 'Home = ManagerIdentity.Runtime(root, "packs");')
e(W + "PackStorage.cs", 'TaskPath = Path.Combine(root, "MCE", "pack-task.json");', 'TaskPath = ManagerIdentity.Runtime(root, "pack-task.json");')
e(W + "ShopQueue.cs", 'analysisHome ?? Path.Combine(storage.Root,"MCE","shop")', 'analysisHome ?? ManagerIdentity.Runtime(storage.Root,"shop")')
e(W + "WorkerRecovery.cs", 'string path = Path.Combine(root, "MCE", "packs", "index.json");', 'string path = ManagerIdentity.Runtime(root, "packs", "index.json");')
e(W + "WorkerRecovery.cs", 'if (!Files.Within(Path.Combine(root, "MCE"), path))', 'if (!Files.Within(ManagerIdentity.Runtime(root), path))')
e(W + "WorkerRecovery.cs", 'string home = Path.Combine(root, "MCE"); Checked(', 'string home = ManagerIdentity.Runtime(root); Checked(')
e(W + "WorkerRecovery.cs", 'string path = Path.Combine(root, "MCE", "pack-task.json");', 'string path = ManagerIdentity.Runtime(root, "pack-task.json");')
e(W + "WorkerRecovery.cs", 'string path = Path.Combine(root, "MCE", "queue.json");', 'string path = ManagerIdentity.Runtime(root, "queue.json");')
e(W + "WorkerRecovery.cs", 'job.WorkDirectory = Path.Combine(root, "MCE", "jobs", job.Id); job.BackupDirectory = Path.Combine(root, "MCE", "backups", job.Id);', 'job.WorkDirectory = ManagerIdentity.Runtime(root, "jobs", job.Id); job.BackupDirectory = ManagerIdentity.Runtime(root, "backups", job.Id);')
e(W + "WorkerRecovery.cs", 'new ShopQueue(root,Path.Combine(root,"MCE","operations",id,"shop")', 'new ShopQueue(root,ManagerIdentity.Runtime(root,"operations",id,"shop")')
e(W + "WorkerWindow.cs", 'OpenFolder(Path.Combine(root, "MCE"))', 'OpenFolder(ManagerIdentity.Runtime(root))')
e("Desktop/ProgressProtocol.cs", 'string home = Path.Combine(root, "MCE");', 'string home = ModConfigEnhance.Updating.ManagerIdentity.Runtime(root);')
e("Desktop/ProgressProtocol.cs", 'files.ContainsKey("MCE.UpdateWorker.exe")', 'files.ContainsKey(ModConfigEnhance.Updating.ManagerIdentity.WorkerExecutable)')
e("Desktop/ProgressProtocol.cs", 'Path.Combine(tools, "MCE.UpdateWorker.exe")', 'Path.Combine(tools, ModConfigEnhance.Updating.ManagerIdentity.WorkerExecutable)')
e("Desktop/ProgressWindow.cs", 'home = Path.Combine(root, "MCE");', 'home = ModConfigEnhance.Updating.ManagerIdentity.Runtime(root);')
e("Desktop/ProgressWindow.cs", 'string script = Path.Combine(home, "MCE-Recovery.ps1");', 'string script = Path.Combine(home, ModConfigEnhance.Updating.ManagerIdentity.RecoveryScript);')

# ── 执行 ──
problems, changed = [], {}
for f, old, new, n in E:
    p = os.path.join(root, *f.split("/"))
    if p not in changed:
        raw = io.open(p, "rb").read()
        text = raw.decode("utf-8-sig")
        changed[p] = [raw.startswith(b"\xef\xbb\xbf"), text.replace("\r\n", "\n"), "\r\n" in text]
    t = changed[p][1]
    c = t.count(old)
    if (n is None and c < 1) or (n is not None and c != n):
        problems.append(f"{f}: 期望 {n if n is not None else '≥1'} 命中 {c}: {old[:90]}")
        continue
    changed[p][1] = t.replace(old, new)
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "apply_identity_report.txt")
io.open(out, "w", encoding="utf-8").write("\n".join(problems) + "\n" + f"{len(E)} edits, {len(changed)} files\n")
print(len(problems), "problems", len(E), "edits")
if apply and not problems:
    for p, (bom, t, crlf) in changed.items():
        if crlf:
            t = t.replace("\n", "\r\n")
        io.open(p, "wb").write((b"\xef\xbb\xbf" if bom else b"") + t.encode("utf-8"))
    print("written", len(changed))
