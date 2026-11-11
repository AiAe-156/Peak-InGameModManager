#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModConfigEnhance.Updating;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed class StorageTreeNode
{
    internal string Name = "", Path = "", Scope = "", TrashId = "", Note = "";
    internal long Bytes;
    internal bool Directory, CanDelete, Category;
    internal List<StorageTreeNode> Children = new();
}

/// <summary>Background read-only directory inventory. Deletion always goes through the worker's fresh preview.</summary>
internal static class PackStorageBrowser
{
    internal static Task<List<StorageTreeNode>> Load(string bepRoot) => Task.Run(() => {
        var result = new List<StorageTreeNode>();
        foreach (var spec in new[] {
            ("下载缓存", ModConfigEnhance.Updating.ManagerIdentity.RuntimeDirectory + "/downloads", MaintenanceScopes.Downloads),
            ("详情缓存", ModConfigEnhance.Updating.ManagerIdentity.RuntimeDirectory + "/packs/.view-cache", MaintenanceScopes.ViewCache),
            ("商店下载与操作备份", ModConfigEnhance.Updating.ManagerIdentity.RuntimeDirectory + "/shop/tasks", MaintenanceScopes.ShopTasks),
            ("商店图标与文档缓存", "config/InGameModManager/data/runtime/shop-resources", MaintenanceScopes.ShopResources),
            ("切换恢复备份", ModConfigEnhance.Updating.ManagerIdentity.RuntimeDirectory + "/packs/.transactions", MaintenanceScopes.Transactions),
            ("整合包回收区", ModConfigEnhance.Updating.ManagerIdentity.RuntimeDirectory + "/packs/.trash", MaintenanceScopes.PackTrash) })
        {
            var root = new StorageTreeNode { Name = spec.Item1, Path = Path.Combine(bepRoot, spec.Item2), Scope = spec.Item3, Directory = true, Category = true };
            if (System.IO.Directory.Exists(root.Path))
            {
                int budget = 20000;
                foreach (string path in System.IO.Directory.GetFileSystemEntries(root.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    var item = Scan(path, ref budget, 0); item.Scope = root.Scope;
                    if (root.Scope == MaintenanceScopes.Downloads)
                        item.CanDelete = !item.Directory && new[] { ".zip", ".part", ".tmp" }.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());
                    else if(root.Scope==MaintenanceScopes.ShopTasks)
                    {
                        try {
                            string recordPath=System.IO.Path.Combine(path,"task.json");var task=JObject.Parse(File.ReadAllText(recordPath));
                            string id=System.IO.Path.GetFileName(path);string state=task.Value<string>("State")??"";
                            string journal=System.IO.Path.Combine(path,"transaction.json");string phase=File.Exists(journal)?JObject.Parse(File.ReadAllText(journal)).Value<string>("Phase")??"":"";
                            item.CanDelete=PackLibrary.ValidId(id)&&task.Value<string>("Id")==id&&(state is "completed" or "failed")&&(phase is "installed" or "rolled-back"||phase.Length==0&&state=="failed");
                            var request=task["Request"];item.Name=(request?.Value<string>("Namespace")??"")+"-"+(request?.Value<string>("Package")??"")+" · "+RecordDate(task,"CreatedUtc",recordPath,out _);
                            item.Note=item.CanDelete?"已结束；清理下载包、解压缓存和操作备份，不影响已安装模组":"待确认、下载、安装或恢复中的任务，受保护";
                        }catch(Exception e) when(e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException){item.Note="缺少有效商店任务记录，受保护";}
                    }
                    else if(root.Scope==MaintenanceScopes.ShopResources){item.Note="图标／文档缓存，可重新下载";}
                    else if (root.Scope == MaintenanceScopes.ViewCache)
                    {
                        try {
                            var cache = JObject.Parse(File.ReadAllText(path));
                            string name = cache.Value<string>("DisplayName") ?? cache["Report"]?.Value<string>("Name") ?? "详情缓存";
                            string version = cache.Value<string>("Version") ?? cache["Report"]?.Value<string>("Version") ?? "";
                            item.Name = name + (version.Length > 0 ? " · v" + version : "") + " · " + RecordDate(cache, "CreatedUtc", path, out bool fallback);
                            if (fallback) item.Note = "日期来自文件时间";
                        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException) { item.Name = "详情缓存 · " + File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"); item.Note = "日期来自文件时间；缺少名称记录"; }
                    }
                    else if (root.Scope == MaintenanceScopes.Transactions)
                    {
                        try {
                            var plan = JObject.Parse(File.ReadAllText(System.IO.Path.Combine(path, "switch.json")));
                            item.CanDelete = plan.Value<string>("Phase") is "installed" or "rolled-back";
                            string from = plan.Value<string>("SourceName") ?? ActiveName(plan["Before"]), to = plan.Value<string>("TargetName") ?? ActiveName(plan["After"]);
                            string date = RecordDate(plan, "CreatedUtc", System.IO.Path.Combine(path, "switch.json"), out bool fallback);
                            item.Name = (from.Length > 0 ? from : "切换备份") + (to.Length > 0 ? " → " + to : "") + " · " + date;
                            item.Note = (item.CanDelete ? "已完成，确认后可清理整个项目" : "未完成，受保护") + (fallback ? "；日期来自文件时间" : "");
                        } catch (Exception e) when (e is IOException || e is Newtonsoft.Json.JsonException) { item.Note = "缺少有效记录，受保护"; }
                    }
                    else if (root.Scope == MaintenanceScopes.PackTrash)
                    {
                        try {
                            var record = JObject.Parse(File.ReadAllText(System.IO.Path.Combine(path, "trash.json")));
                            item.TrashId = System.IO.Path.GetFileName(path);
                            item.CanDelete = PackLibrary.ValidId(item.TrashId) && record.Value<string>("Id") == item.TrashId;
                            string name = record["Record"]?.Value<string>("Name") ?? "";
                            if (name.Length > 0) item.Name = name;
                            item.Note = "回收项目，永久删除需确认";
                        } catch (Exception e) when (e is IOException || e is Newtonsoft.Json.JsonException) { item.Note = "缺少有效回收记录，受保护"; }
                    }
                    root.Bytes += item.Bytes; root.Children.Add(item);
                }
            }
            root.CanDelete = root.Children.Count > 0;
            result.Add(root);
        }
        return result;
    });
    private static string ActiveName(JToken? index)
    {
        string id = index?.Value<string>("ActivePackId") ?? "";
        return index?["Packs"]?.FirstOrDefault(p => p.Value<string>("Id") == id)?.Value<string>("Name") ?? "";
    }
    private static string RecordDate(JObject record, string key, string path, out bool fallback)
    {
        fallback = !DateTime.TryParse(record.Value<string>(key), null, System.Globalization.DateTimeStyles.RoundtripKind, out var date);
        return (fallback ? File.GetLastWriteTime(path) : date.ToLocalTime()).ToString("yyyy-MM-dd HH:mm");
    }
    private static StorageTreeNode Scan(string path, ref int budget, int depth)
    {
        var node = new StorageTreeNode { Name = System.IO.Path.GetFileName(path), Path = path };
        if (--budget < 0 || depth > 20) { node.Note = "目录过大，停止展开；请在资源管理器查看"; return node; }
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) { node.Note = "链接，受保护"; return node; }
        node.Directory = (attributes & FileAttributes.Directory) != 0;
        if (!node.Directory) { node.Bytes = new FileInfo(path).Length; return node; }
        foreach (string child in System.IO.Directory.GetFileSystemEntries(path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (budget <= 0) { node.Note = "仅展示部分内容，请在资源管理器查看完整目录"; break; }
            var item = Scan(child, ref budget, depth + 1); node.Bytes += item.Bytes; node.Children.Add(item);
        }
        return node;
    }
}

internal sealed class PackStorageTreeView
{
    private readonly HashSet<string> _expanded;
    private readonly Action _refresh;
    private readonly Action<StorageTreeNode> _delete;
    private readonly bool _allowDelete;
    internal PackStorageTreeView(HashSet<string> expanded, string bepRoot, Action refresh, Action<StorageTreeNode> delete, bool allowDelete = true)
    { _expanded = expanded; _refresh = refresh; _delete = delete; _allowDelete = allowDelete; }
    internal float Draw(RectTransform host, IEnumerable<StorageTreeNode> roots, float y, float width)
    { foreach (var node in roots) Row(host, node, 0, ref y, width); return y; }
    private void Row(RectTransform host, StorageTreeNode node, int depth, ref float y, float width)
    {
        float indent = Mathf.Min(depth * 16, Mathf.Max(0, width - 240)), rowHeight = node.Note.Length > 0 ? 65 : 45;
        var row = OverviewUi.Rect("StorageRow", host); OverviewUi.Place(row, 0, y, width, rowHeight);
        row.gameObject.AddComponent<Image>().color = depth == 0 ? ModConfigLook.Surface(new Color(.16f, .22f, .23f, 1)) : ModConfigLook.Surface(new Color(.11f, .11f, .10f, .6f));
        bool open = _expanded.Contains(node.Path);
        bool removeVisible = node.CanDelete || node.Category;
        float removeWidth = node.Category ? 116 : 62;
        float actions = 28 + (!node.Directory ? 60 : 0) + (removeVisible ? removeWidth + 8 : 0);
        float actionX = width - actions - 8;
        if (node.Directory)
        {
            var toggle = OverviewUi.IconButton(row, open ? "triangle-down" : "triangle-right", () => { if (!_expanded.Add(node.Path)) _expanded.Remove(node.Path); _refresh(); }, 24, 26);
            OverviewUi.Place((RectTransform)toggle.transform, 5 + indent, 5, 24, 26);
        }
        var title = OverviewUi.Text(row, StorageText.Display(node.Name), depth == 0 ? 18 : 15);
        OverviewUi.PlaceClipped(title, indent + 35, 4, Mathf.Max(35, width - indent - actions - 53), 24);
        var meta = OverviewUi.Text(row, (node.Bytes / 1048576d).ToString("0.00") + " MB" + (node.Note.Length > 0 ? " · " + StorageText.Display(node.Note) : ""), 12); meta.color = OverviewUi.Muted;
        meta.textWrappingMode = TextWrappingModes.Normal; OverviewUi.Place(meta.rectTransform, indent + 35, 29, Mathf.Max(50, width - indent - actions - 53), rowHeight - 30);
        var folder = OverviewUi.IconButton(row, "folder", () => PackFileShortcuts.Open(node.Path, true), 28, 28); OverviewUi.Place((RectTransform)folder.transform, actionX, 5, 28, 28);
        if (!node.Directory)
        {
            var file = OverviewUi.Button(row, Loc.T("Open", "打开", "開啟"), () => PackFileShortcuts.Open(node.Path, false), 52, 28, UiActionRole.Neutral); OverviewUi.Place((RectTransform)file.transform, actionX + 36, 5, 52, 28);
        }
        if (removeVisible)
        {
            string caption = node.Category ? node.Scope == MaintenanceScopes.PackTrash
                ? Loc.T("Empty trash", "清空回收区", "清空回收區") : Loc.T("Clean category", "清理本类", "清理本類")
                : Loc.T("Delete", "删除", "刪除");
            var remove = OverviewUi.Button(row, caption, () => _delete(node), removeWidth, 28, UiActionRole.Danger);
            remove.interactable = _allowDelete && node.CanDelete; OverviewUi.Place((RectTransform)remove.transform, width - removeWidth - 8, 5, removeWidth, 28);
        }
        y += rowHeight + 4;
        if (node.Directory && open) foreach (var child in node.Children) Row(host, child, depth + 1, ref y, width);
    }
}

/// <summary>2.3.8: the storage tree is built on a worker thread in Simplified Chinese; this translates its fixed
/// phrases on the main thread when a row is drawn (Loc is not thread-safe).</summary>
internal static class StorageText
{
    private static readonly (string Cn, string En, string Tw)[] Phrases =
    {
        ("下载缓存", "Download cache", "下載快取"),
        ("详情缓存", "Details cache", "詳情快取"),
        ("商店下载与操作备份", "Store downloads and operation backups", "商店下載與操作備份"),
        ("商店图标与文档缓存", "Store icon and document cache", "商店圖示與文件快取"),
        ("切换恢复备份", "Switch recovery backups", "切換還原備份"),
        ("整合包回收区", "Modpack recycle area", "整合包回收區"),
        ("切换备份", "Switch backup", "切換備份"),
        ("已结束；清理下载包、解压缓存和操作备份，不影响已安装模组", "Finished; removes download packages, extraction cache and operation backups, not installed mods", "已結束；清理下載套件、解壓快取和操作備份，不影響已安裝模組"),
        ("待确认、下载、安装或恢复中的任务，受保护", "Task awaiting confirmation, downloading, installing or recovering; protected", "待確認、下載、安裝或還原中的任務，受保護"),
        ("缺少有效商店任务记录，受保护", "No valid store task record; protected", "缺少有效商店任務記錄，受保護"),
        ("图标／文档缓存，可重新下载", "Icon / document cache; can be downloaded again", "圖示／文件快取，可重新下載"),
        ("日期来自文件时间", "date from file time", "日期取自檔案時間"),
        ("；日期来自文件时间", "; date from file time", "；日期取自檔案時間"),
        ("已完成，确认后可清理整个项目", "Completed; the whole item can be cleaned after confirmation", "已完成，確認後可清理整個項目"),
        ("未完成，受保护", "Unfinished; protected", "未完成，受保護"),
        ("缺少有效记录，受保护", "No valid record; protected", "缺少有效記錄，受保護"),
        ("回收项目，永久删除需确认", "Recycled item; permanent deletion needs confirmation", "回收項目，永久刪除需確認"),
        ("缺少有效回收记录，受保护", "No valid recycle record; protected", "缺少有效回收記錄，受保護"),
        ("目录过大，停止展开；请在资源管理器查看", "Folder too large to expand; view it in File Explorer", "目錄過大，停止展開；請在檔案總管檢視"),
        ("链接，受保护", "Link; protected", "連結，受保護"),
        ("仅展示部分内容，请在资源管理器查看完整目录", "Only part of the folder is shown; view it in File Explorer", "僅顯示部分內容，請在檔案總管檢視完整目錄")
    };
    internal static string Display(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string probe = Loc.T("en", "zh-cn", "zh-tw");
        if (probe == "zh-cn") return text;
        foreach (var p in Phrases.OrderByDescending(p => p.Cn.Length)) text = text.Replace(p.Cn, probe == "en" ? p.En : p.Tw);
        return text;
    }
}
