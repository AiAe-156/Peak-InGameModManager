#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;

namespace ModConfigEnhance.Overview;

/// <summary>Read-only directory navigation. Missing paths stay disabled; opening never creates a directory.</summary>
internal static class FolderShortcuts
{
    private sealed class Folder
    {
        internal string Group = "", Label = "", Path = "", Reason = "";
        internal bool Exists;
    }

    internal static void Open(OverviewMod mod, RectTransform anchor, Action<string> status, OverviewRepository? repo = null)
    {
        var folders = new List<Folder>();
        string modGroup = Loc.T("Mod", "模组", "模組");
        Add(folders, modGroup, Loc.T("Mod folder", "模组目录", "模組資料夾"), () => mod.PackageRoot.Length > 0
            ? mod.PackageRoot : System.IO.Path.GetDirectoryName(mod.Location) ?? "");
        if (!string.Equals(mod.Guid, Plugin.PluginGuid, StringComparison.OrdinalIgnoreCase)) { Show(folders, anchor, status); return; }

        Add(folders, modGroup, Loc.T("Configuration folder", "配置目录", "設定資料夾"), () => Paths.ConfigPath);
        string translation = Loc.T("Translation", "翻译", "翻譯");
        Add(folders, translation, Loc.T("Language folder", "翻译目录", "翻譯資料夾"), () => Loc.LanguageDirectory);
        Add(folders, translation, Loc.T("Translation exports", "翻译导出", "翻譯匯出"), () => Loc.ExportDirectory);
        Add(folders, modGroup, Loc.T("Exported modpacks", "导出的整合包", "匯出的整合包"), () => Loc.PackOutputDirectory);
        string modpack = Loc.T("Modpack", "整合包", "整合包");
        Add(folders, modpack, Loc.T("Current workspace", "当前工作区", "目前工作區"), () => PackLibrary.Workspace);
        Add(folders, modpack, Loc.T("Viewed profile resources", "当前查看方案资源", "目前檢視方案資源"),
            () => repo?.ViewedProfile.directory ?? "", Loc.T("Profile unavailable", "方案暂不可用", "方案暫時無法使用"));
        string maintenance = Loc.T("Maintenance", "维护", "維護");
        Add(folders, maintenance, Loc.T("BepInEx log folder", "BepInEx 日志目录", "BepInEx 日誌資料夾"), () => Paths.BepInExRootPath);
        Add(folders, maintenance, Loc.T("Game log folder", "游戏日志目录", "遊戲日誌資料夾"), GameLogDirectory);
        Add(folders, maintenance, Loc.T("Download cache", "下载缓存", "下載快取"), () => System.IO.Path.Combine(OverviewUpdateQueue.Root, "downloads"));
        Add(folders, maintenance, Loc.T("Configuration backups", "配置备份", "設定備份"), () => Loc.BackupDirectory);
        Add(folders, maintenance, Loc.T("Pack switch backups", "整合包切换备份", "整合包切換備份"), () => System.IO.Path.Combine(PackLibrary.Home, ".transactions"));
        Show(folders, anchor, status);
    }

    internal static void Open(InfrastructureOverviewModel component, RectTransform anchor, Action<string> status)
    {
        var folders = new List<Folder>();
        foreach (var location in component.Locations)
        {
            string area = location.Area == "core" ? Loc.T("Core", "核心组件(Core)", "核心組件(Core)")
                : Loc.T("Patcher", "启动补丁(Patcher)", "啟動補丁(Patcher)");
            Add(folders, Loc.T("Mod", "模组", "模組"), area + " · " + System.IO.Path.GetFileName(location.Path), () => location.Path);
        }
        Show(folders, anchor, status);
    }

    private static void Add(List<Folder> folders, string group, string label, Func<string> path, string? unavailable = null)
    {
        var folder = new Folder { Group = group, Label = label };
        try
        {
            string value = path();
            if (string.IsNullOrWhiteSpace(value)) folder.Reason = unavailable ?? Loc.T("Folder unavailable", "目录暂不可用", "資料夾暫時無法使用");
            else
            {
                folder.Path = System.IO.Path.GetFullPath(value);
                folder.Exists = Directory.Exists(folder.Path);
                if (!folder.Exists) folder.Reason = Loc.T("Folder not found", "目录不存在", "資料夾不存在");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
            or Newtonsoft.Json.JsonException)
        {
            folder.Reason = unavailable ?? Loc.T("Folder unavailable", "目录暂不可用", "資料夾暫時無法使用");
        }
        // Keep different intentions at one path, but do not repeat an identical label/path entry.
        if (!folders.Any(f => f.Group == group && f.Label == label && f.Path.Equals(folder.Path, StringComparison.OrdinalIgnoreCase))) folders.Add(folder);
    }

    private static string GameLogDirectory()
    {
        string file = Application.consoleLogPath;
        if (string.IsNullOrWhiteSpace(file)) return Paths.GameRootPath;
        if (!System.IO.Path.IsPathRooted(file)) file = System.IO.Path.Combine(Paths.GameRootPath, file);
        return System.IO.Path.GetDirectoryName(file) ?? Paths.GameRootPath;
    }

    private static void Show(IReadOnlyList<Folder> folders, RectTransform anchor, Action<string> status)
    {
        if (folders.Count == 1 && folders[0].Exists) { OverviewListView.OpenFolder(folders[0].Path, status); return; }
        var entries = new List<UiMenuEntry>();
        foreach (var group in folders.GroupBy(f => f.Group))
        {
            entries.Add(new UiMenuEntry { Label = group.Key, Header = true });
            foreach (var folder in group)
                entries.Add(new UiMenuEntry { Label = folder.Label, Enabled = folder.Exists, DisabledReason = folder.Reason,
                    CanInvoke = () => Directory.Exists(folder.Path), Invoke = () => OverviewListView.OpenFolder(folder.Path, status) });
        }
        OverviewUi.Dropdown(anchor, Loc.T("Folder shortcuts", "目录快捷方式", "資料夾捷徑"), entries, 360);
    }
}
