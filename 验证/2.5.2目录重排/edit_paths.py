# -*- coding: utf-8 -*-
"""2.5.2 目录重排：把写死的 config\\ModConfigEnhance 子目录全部改走 MceDirectories。用法 python -I edit_paths.py <工程根>"""
import io
import os
import sys

ROOT = sys.argv[1]
count = 0


def edit(rel, pairs):
    global count
    p = os.path.join(ROOT, rel)
    raw = io.open(p, "rb").read()
    bom = raw[:3] == b"\xef\xbb\xbf"
    s = raw.decode("utf-8-sig")
    crlf = "\r\n" in s
    s = s.replace("\r\n", "\n")
    for a, b in pairs:
        n = s.count(a)
        if n == 0 and b in s:
            continue  # 上次已改
        assert n == 1, (rel, a[:100], n)
        s = s.replace(a, b)
        count += 1
    if crlf:
        s = s.replace("\n", "\r\n")
    io.open(p, "w", encoding="utf-8-sig" if bom else "utf-8", newline="").write(s)
    print("ok", rel, len(pairs))


D = "Updating.MceDirectories"

edit("src/Loc.cs", [
    ('    internal static string LanguageDirectory => Path.Combine(RootDirectory, "language");',
     '    internal static string LanguageDirectory => ' + D + '.Language(RootDirectory);'),
    ('    internal static string ExportDirectory => Path.Combine(RootDirectory, "translation-export");\n'
     '    internal static string LegacyExportDirectory => Path.Combine(RootDirectory, "export");',
     '    internal static string ExportDirectory => ' + D + '.TranslationOutput(RootDirectory);\n'
     '    /// <summary>2.5.2：运行数据（原 runtime\\）、备份、说明覆盖、旧版数据都在 data\\ 下；整合包导出在 output\\packs。</summary>\n'
     '    internal static string RuntimeDirectory => ' + D + '.Runtime(RootDirectory);\n'
     '    internal static string BackupDirectory => ' + D + '.Backups(RootDirectory);\n'
     '    internal static string PackOutputDirectory => ' + D + '.PackOutput(RootDirectory);'),
])
edit("src/Overview/ChangeLedger.cs", [
    ('Path.Combine(Loc.RootDirectory, "runtime", "changes.json")', 'Path.Combine(Loc.RuntimeDirectory, "changes.json")'),
    ('Path.Combine(Loc.RootDirectory, "runtime", "changes-read.json")', 'Path.Combine(Loc.RuntimeDirectory, "changes-read.json")'),
])
edit("src/Overview/FolderShortcuts.cs", [
    ('() => System.IO.Path.Combine(Loc.RootDirectory, "backups"));', '() => Loc.BackupDirectory);'),
])
edit("src/Overview/Help/HelpDocumentSource.cs", [
    ('internal static string BaseDirectory => Path.Combine(Loc.RootDirectory, "help");', 'internal static string BaseDirectory => ' + D + '.Help(Loc.RootDirectory);'),
    ('public string ExportDirectory => Path.Combine(Loc.RootDirectory, "exports");', 'public string ExportDirectory => Loc.PackOutputDirectory;'),
    ('Path.Combine(Loc.RootDirectory, "backups", "help",', 'Path.Combine(Loc.BackupDirectory, "help",'),
])
edit("src/Overview/LogWindow.cs", [
    ('private string FavoritesPath => Path.Combine(Loc.RootDirectory,"log-favorites.json");', 'private string FavoritesPath => Path.Combine(Loc.RuntimeDirectory,"log-favorites.json");'),
])
edit("src/Overview/LogSavePicker.cs", [
    ('internal static string FallbackPath(string configRoot, string name) => Path.Combine(configRoot, "export", name);',
     'internal static string FallbackPath(string configRoot, string name) => Path.Combine(' + D + '.Output(configRoot), "logs", name);'),
])
edit("src/Overview/ModSources.cs", [
    ('Path.Combine(Loc.RootDirectory, "runtime", "sources.json")', 'Path.Combine(Loc.RuntimeDirectory, "sources.json")'),
])
edit("src/Overview/ModSources.Discovery.cs", [
    ('File.ReadAllText(Path.Combine(Loc.RootDirectory, "runtime", "source-identities.json"))', 'File.ReadAllText(Path.Combine(Loc.RuntimeDirectory, "source-identities.json"))'),
    ('OverviewRepository.AtomicWrite(Path.Combine(Loc.RootDirectory, "runtime", "source-identities.json"),', 'OverviewRepository.AtomicWrite(Path.Combine(Loc.RuntimeDirectory, "source-identities.json"),'),
])
edit("src/Overview/OverviewPage.Management.cs", [
    ('ManagementUi.OpenFolder(Path.Combine(_repo.ConfigRootDirectory, "exports"))', 'ManagementUi.OpenFolder(' + D + '.PackOutput(_repo.ConfigRootDirectory))'),
])
edit("src/Overview/OverviewPageCache.cs", [
    ('_appliedPath = Path.Combine(_data, "runtime", "applied-profile.json");', '_appliedPath = Path.Combine(' + D + '.Runtime(_data), "applied-profile.json");'),
    ('_legacyPath = Path.Combine(_data, "overview", "state.json");', '_legacyPath = Path.Combine(' + D + '.Legacy(_data), "state.json");'),
    ('|| relative.Equals("runtime/ui.json", StringComparison.OrdinalIgnoreCase)\n'
     '                || relative.Equals("runtime/applied-profile.json", StringComparison.OrdinalIgnoreCase)\n'
     '                || relative.Equals("runtime/sources.json", StringComparison.OrdinalIgnoreCase)\n'
     '                || relative.StartsWith("overview/", StringComparison.OrdinalIgnoreCase)',
     '|| relative.Equals("data/runtime/ui.json", StringComparison.OrdinalIgnoreCase)\n'
     '                || relative.Equals("data/runtime/applied-profile.json", StringComparison.OrdinalIgnoreCase)\n'
     '                || relative.Equals("data/runtime/sources.json", StringComparison.OrdinalIgnoreCase)\n'
     '                || relative.StartsWith("data/legacy/", StringComparison.OrdinalIgnoreCase)'),
])
edit("src/Overview/OverviewProfileStore.cs", [
    ('public string RuntimeDirectory => Path.Combine(Path.GetDirectoryName(RootDirectory)!, "runtime");',
     '/// <summary>同级的运行目录：整合包工作区是 workspace\\runtime；未托管时 profiles 在 config\\ModConfigEnhance 下，运行目录是 data\\runtime（2.5.2）。</summary>\n'
     '    public string RuntimeDirectory => Path.GetFileName(Path.GetDirectoryName(RootDirectory)!).Equals("ModConfigEnhance", StringComparison.OrdinalIgnoreCase)\n'
     '        ? ' + D + '.Runtime(Path.GetDirectoryName(RootDirectory)!) : Path.Combine(Path.GetDirectoryName(RootDirectory)!, "runtime");'),
])
edit("src/Overview/OverviewRepository.cs", [
    ('_legacyRootDirectory = rootDirectory ?? Path.Combine(Paths.ConfigPath, "ModConfigEnhance", "overview");\n'
     '        ConfigRootDirectory = Path.GetDirectoryName(_legacyRootDirectory)!;\n'
     '        RootDirectory = Path.Combine(ConfigRootDirectory, "runtime");',
     '// 2.5.2：旧版 overview\\ 搬到 data\\legacy\\；ConfigRootDirectory 仍是 config\\ModConfigEnhance\n'
     '        ConfigRootDirectory = rootDirectory != null ? Path.GetDirectoryName(rootDirectory)! : Loc.RootDirectory;\n'
     '        _legacyRootDirectory = rootDirectory ?? ' + D + '.Legacy(ConfigRootDirectory);\n'
     '        RootDirectory = rootDirectory != null ? Path.Combine(ConfigRootDirectory, "runtime") : ' + D + '.Runtime(ConfigRootDirectory);'),
])
edit("src/Overview/OverviewRepository.Profiles.cs", [
    ('new OverviewConfigService(Path.GetDirectoryName(ConfigRootDirectory), Path.Combine(ConfigRootDirectory, "backups", "', 'new OverviewConfigService(Path.GetDirectoryName(ConfigRootDirectory), Path.Combine(' + D + '.Backups(ConfigRootDirectory), "'),
    ('string backup = Path.Combine(ConfigRootDirectory, "backups", "before-profiles-"', 'string backup = Path.Combine(' + D + '.Backups(ConfigRootDirectory), "before-profiles-"'),
    ('Path.Combine(ConfigRootDirectory, "exports"));', D + '.PackOutput(ConfigRootDirectory));'),
])
edit("src/Overview/OverviewProfileExport.cs", [
    ('OverviewProfileAssets.CheckedPath(configRoot, "exports")', 'OverviewProfileAssets.CheckedPath(configRoot, "output/packs")'),
])
edit("src/Overview/PackConflictStore.cs", [
    ('Path.Combine(configRoot ?? Loc.RootDirectory, "runtime", "pack-conflicts",', 'Path.Combine(' + D + '.Runtime(configRoot ?? Loc.RootDirectory), "pack-conflicts",'),
])
edit("src/Overview/PackPublisher.cs", [
    ('Path.Combine(Loc.RootDirectory, "runtime", "publisher.json")', 'Path.Combine(Loc.RuntimeDirectory, "publisher.json")'),
    ('OverviewProfileAssets.CheckedPath(configRoot, "runtime/publication-builds")', 'OverviewProfileAssets.CheckedPath(configRoot, "data/runtime/publication-builds")'),
    ('Path.GetFullPath(Path.Combine(repo.ConfigRootDirectory, "exports"))', 'Path.GetFullPath(' + D + '.PackOutput(repo.ConfigRootDirectory))'),
])
edit("src/Overview/PackPublisher.Environment.cs", [
    ('Path.Combine(Loc.RootDirectory, "runtime", "publication-previews",', 'Path.Combine(Loc.RuntimeDirectory, "publication-previews",'),
])
edit("src/Overview/PublicationDrafts.cs", [
    ('OverviewProfileAssets.CheckedPath(repo.ConfigRootDirectory, "runtime/publication-drafts/"', 'OverviewProfileAssets.CheckedPath(repo.ConfigRootDirectory, "data/runtime/publication-drafts/"'),
])
edit("src/Overview/PublishedPack.cs", [
    ('internal static string BootstrapPath => Path.Combine(RuntimeRootProvider?.Invoke() ?? Path.Combine(Loc.RootDirectory, "runtime"), "bootstrap.json");',
     'internal static string BootstrapPath => Path.Combine(RuntimeRootProvider?.Invoke() ?? Loc.RuntimeDirectory, "bootstrap.json");'),
])
edit("src/Overview/ResourceOperationDraft.cs", [
    ('PackLibrary.Managed ? PackLibrary.Workspace : Path.Combine(BepInEx.Paths.ConfigPath, "ModConfigEnhance", "runtime");', 'PackLibrary.Managed ? PackLibrary.Workspace : Loc.RuntimeDirectory;'),
])
edit("src/Overview/ShopCatalog.cs", [
    ('Path.Combine(Loc.RootDirectory,"runtime","shop-catalog.json")', 'Path.Combine(Loc.RuntimeDirectory,"shop-catalog.json")'),
])
edit("src/Overview/ShopMarkdownSource.cs", [
    ('Path.Combine(Loc.RootDirectory,"runtime","shop-media",name)', 'Path.Combine(Loc.RuntimeDirectory,"shop-media",name)'),
])
edit("src/Overview/ShopWindow.cs", [
    ('Path.Combine(Loc.RootDirectory,"shop-favorites.json")', 'Path.Combine(Loc.RuntimeDirectory,"shop-favorites.json")'),
])
edit("src/Overview/OverviewConfigService.cs", [
    ('_backupRoot = runtimeDirectory ?? Path.Combine(_configRoot, "ModConfigEnhance", "runtime", "config-backups");',
     '_backupRoot = runtimeDirectory ?? Path.Combine(' + D + '.Runtime(Path.Combine(_configRoot, "ModConfigEnhance")), "config-backups");'),
])
edit("src/Overview/PackStorageBrowser.cs", [
    ('("商店图标与文档缓存", "config/ModConfigEnhance/runtime/shop-resources", MaintenanceScopes.ShopResources),',
     '("商店图标与文档缓存", "config/ModConfigEnhance/data/runtime/shop-resources", MaintenanceScopes.ShopResources),'),
])
edit("src/Overview/Authoring/OverviewDocumentView.cs", [
    ('string exportRoot=Path.Combine(_repository.RootDirectory,"exports",', 'string exportRoot=Path.Combine(Loc.PackOutputDirectory,"introduction",'),
])
edit("src/Overview/Editor/IntroductionEditorView.cs", [
    ('?? Path.Combine(Repository.RootDirectory, "exports");', '?? Loc.PackOutputDirectory;'),
])
edit("src/Overview/Authoring/IntroductionFontRegistry.cs", [
    ('Path.Combine(BepInEx.Paths.ConfigPath, "ModConfigEnhance", "fonts")', 'Path.Combine(Loc.RuntimeDirectory, "fonts")'),
])
# 更新助手
edit("Updater/PackagePlan.cs", [
    ('Path.Combine(root, "config", "ModConfigEnhance", "runtime", "store-catalog.json")', 'Path.Combine(MceDirectories.Runtime(MceDirectories.RootFromBepInEx(root)), "store-catalog.json")'),
])
edit("Updater/PackMaintenance.cs", [
    ('Path.Combine(storage.Root,"config","ModConfigEnhance","runtime","shop-resources")', 'Path.Combine(MceDirectories.Runtime(MceDirectories.RootFromBepInEx(storage.Root)),"shop-resources")'),
])
edit("Updater/PackTransactions.cs", [
    ('roots.Add(Path.Combine(storage.Root, "config", "ModConfigEnhance", "language"));', 'roots.Add(MceDirectories.Language(MceDirectories.RootFromBepInEx(storage.Root)));'),
    ('string source = Path.Combine(storage.Root, "config", "ModConfigEnhance", "language");', 'string source = MceDirectories.Language(MceDirectories.RootFromBepInEx(storage.Root));'),
    ('string source = Path.Combine(old, "runtime", name);', 'string source = Path.Combine(MceDirectories.Runtime(old), name);'),
])
print("edits:", count)
