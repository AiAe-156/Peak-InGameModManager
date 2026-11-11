#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModConfigEnhance.Updating;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewPage
{
    private string _packImportSource = "", _packViewKey = "", _packPreviewId = "", _packProfileCommitting = "";
    private float _packPollAt, _packSentAt; private string _packSentKey = "";
    private static float _packScrollY; private static string _packScrollTask = "";
    private string _packCompletionShown = "";
    private string _packSelectedId = "", _packPane = "library", _packLoadingKey = "";
    private Task<PackTask>? _packLoading;
    private bool _packLoadRendered;
    private Task<List<StorageTreeNode>>? _storageLoading;
    private bool _storageRendered, _packStorageTab;
    private readonly HashSet<string> _storageExpanded = new(StringComparer.OrdinalIgnoreCase);
    internal static readonly HashSet<string> _ignoredPackLines = new(StringComparer.OrdinalIgnoreCase);

    private void BuildManagementPacks(RectTransform host)
    {
        var index = PackLibrary.Read(); var task = PackLibrary.Task();
        bool busy = task?.State is "requested" or "preparing" or "saving" or "waiting-exit" or "switching" or "recovery-required";
        var active = index.Packs.FirstOrDefault(p => p.Id == index.ActivePackId);
        float w = host.rect.width, h = host.rect.height, left = Mathf.Clamp(w * .25f, 210, 300);
        float bodyY = 140, bodyH = Mathf.Max(100, h - bodyY);
        var library = host.Find("PackLibrary") as RectTransform;
        if (library == null)
        {
        var top = OverviewUi.Panel("PackToolbar", host, ModConfigLook.Surface(new Color(.10f, .12f, .13f, 1))); OverviewUi.Place(top, 0, 0, w, 88);
        var heading = OverviewUi.Text(top, T("Modpacks", "整合包"), 25); OverviewUi.PlaceClipped(heading, 12, 6, left - 24, 32);
        var current = OverviewUi.Text(top, T("Active: ", "当前启用：") + (active?.Name ?? T("Unsaved current setup", "尚未保存的当前配置")), 16);
        current.color = OverviewUi.Muted; OverviewUi.PlaceClipped(current, 12, 48, w - 24, 30);
        float saveW = Mathf.Min(320, (w - left - 36) * .67f), importW = Mathf.Min(140, w - left - saveW - 36);
        var save = OverviewUi.Button(top, T("Save current setup as a temporary modpack", "保存当前配置为临时整合包"), () => ManagementAction(NameTemporaryPack), saveW, 32, UiActionRole.Neutral);
        OverviewUi.Place((RectTransform)save.transform, w - saveW - 12, 7, saveW, 32); save.interactable = !busy;
        var recovery = OverviewUi.Button(top, T("Recover existing unfinished transactions only", "仅恢复既有未完成事务"), () => ManagementAction(PackSwitchProgressWindow.ShowRecovery), 300, 28, UiActionRole.Edit);
        OverviewUi.Place((RectTransform)recovery.transform, w - 312, 48, 300, 28);
        var add = OverviewUi.Button(top, T("Import modpack", "导入整合包"), () => { _packStorageTab = false; _packPane = "import"; _packScrollY = 0; _management?.Rebuild(ManagementTab.Packs); }, importW, 32, UiActionRole.Edit);
        OverviewUi.Place((RectTransform)add.transform, w - saveW - importW - 24, 7, importW, 32); add.interactable = !busy;

        var libraryTab = OverviewUi.Button(host, Loc.T("Modpack library", "整合包库", "整合包庫"), () => { _packStorageTab = false; _packPane = "library"; _management?.Rebuild(ManagementTab.Packs); }, 140, 32, UiActionRole.Edit); OverviewUi.Place((RectTransform)libraryTab.transform, 0, 98, 140, 32); UiActionStyle.Select(libraryTab, !_packStorageTab);
        var storageTab = OverviewUi.Button(host, Loc.T("Storage management", "空间管理", "空間管理"), OpenPackStorage, 140, 32, UiActionRole.Overview); OverviewUi.Place((RectTransform)storageTab.transform, 150, 98, 140, 32); UiActionStyle.Select(storageTab, _packStorageTab);
        library = OverviewUi.Panel("PackLibrary", host, ModConfigLook.Surface(new Color(.09f, .10f, .11f, .95f))); OverviewUi.Place(library, 0, bodyY, left, bodyH);
        var listHost = OverviewUi.Rect("LibraryInset", library); OverviewUi.Place(listHost, 12, 8, left - 24, bodyH - 16);
        var list = new ManagementForm(listHost, ManagementAction);
        list.Block((content, y) => {
            var title = OverviewUi.Text(content, T("Library", "整合包库") + " · " + index.Packs.Count, 20);
            title.enableAutoSizing = true; title.fontSizeMin = 14; title.fontSizeMax = 20; title.overflowMode = TextOverflowModes.Ellipsis;
            OverviewUi.Place(title.rectTransform, 10, y + 4, Mathf.Max(60, list.Width - 20), 34); return y + 44;
        });
        if (index.Packs.Count == 0) list.Text(T("No stored modpacks. Use Import or Save above.", "暂无整合包，请使用上方的导入或保存。"));
        if (!index.Packs.Any(p => p.Id == _packSelectedId)) _packSelectedId = active?.Id ?? index.Packs.FirstOrDefault()?.Id ?? "";
        foreach (var pack in index.Packs.OrderByDescending(p => p.Id == index.ActivePackId))
        {
            list.Block((content, y) => PackLibraryItem.Draw(content, pack, y, list.Width,
                _packSelectedId == pack.Id, pack.Id == index.ActivePackId,
                () => ManagementAction(() => { _packStorageTab = false; _packSelectedId = pack.Id; _packPane = "library"; _packScrollY = 0; PackPreviewView.PackLanguageChoice = ""; _management?.Rebuild(ManagementTab.Packs); })));
        }

        list.Action(T("Refresh details", "刷新详情"), () => {
            var selected = index.Packs.FirstOrDefault(p => p.Id == _packSelectedId);
            if (selected != null) { _packLoading = StoredPackDetails.LoadAsync(selected.PendingUpdate ?? selected, true); _packLoadingKey = StoredPackDetails.CacheKey(selected.PendingUpdate ?? selected); _packLoadRendered = false; }
            _management?.Rebuild(ManagementTab.Packs);
        }, UiActionRole.Neutral); list.Finish();

        }
        library.gameObject.SetActive(!_packStorageTab);
        var detail = OverviewUi.Panel("PackDetail", host, ModConfigLook.Surface(new Color(.13f, .12f, .10f, .95f))); OverviewUi.Place(detail, _packStorageTab ? 0 : left + 12, bodyY, _packStorageTab ? w : w - left - 12, bodyH);
        var detailHost = OverviewUi.Rect("DetailInset", detail); OverviewUi.Place(detailHost, 18, 10, detail.rect.width - 36, bodyH - 20);
        var form = new ManagementForm(detailHost, ManagementAction);
        PackTask? shown = null;
        if (_packPane == "storage")
        {
            form.Text(Loc.T("Storage and modpack recycle area", "空间管理与整合包回收区", "空間管理與整合包回收區"), true);
            form.Text(Loc.T("Expand an item to see its files. Deleting removes the whole item and rechecks the scope first. Protected items cannot be deleted.", "展开项目查看文件；删除按项目整体处理，执行前重新核对范围。受保护项目不提供删除操作。", "展開項目檢視檔案；刪除以項目整體處理，執行前重新核對範圍。受保護項目不提供刪除操作。"));
            if (busy) form.Text(Loc.T("A modpack task is running; deleting is unavailable for now.", "整合包任务进行中，删除操作暂不可用。", "整合包任務進行中，刪除操作暫不可用。"));
            if (_storageLoading == null || !_storageLoading.IsCompleted) form.Text(Loc.T("Reading the file tree in the background…", "正在后台读取文件树……", "正在背景讀取檔案樹……"));
            else if (_storageLoading.IsFaulted) { form.Text("读取失败：" + _storageLoading.Exception?.GetBaseException().Message); _storageRendered = true; }
            else
            {
                _storageRendered = true;
                var tree = new PackStorageTreeView(_storageExpanded, BepInEx.Paths.BepInExRootPath, () => _management?.Rebuild(ManagementTab.Packs), DeleteStorageProject, !busy);
                form.Block((content, y) => tree.Draw(content, _storageLoading.Result, y, form.Width));
            }
            form.Action(Loc.T("Refresh", "刷新", "重新整理"), () => { _storageLoading = null; _storageRendered = false; _management?.Rebuild(ManagementTab.Packs); }, UiActionRole.Neutral);
            form.Action(Loc.T("Open manager data folder", "打开管理目录", "開啟管理目錄"), () => ManagementUi.OpenFolder(OverviewUpdateQueue.Root), UiActionRole.Neutral);

        }
        else if (_packPane == "import")
        {
            form.Text(T("Import · Select source", "导入整合包 · 选择来源"), true);
            form.Field(T("Modpack file (.zip) or matching folder", "整合包文件（.zip）或符合结构的文件夹"), _packImportSource, false, value => _packImportSource = value);
            form.Button(T("Browse…", "浏览…"), () => { if (IntroductionFilePicker.TrySelect("pack", out string? selected) && !string.IsNullOrEmpty(selected)) { _packImportSource = selected; _management?.Rebuild(ManagementTab.Packs); } }, UiActionRole.Neutral).interactable = !busy;
            form.Action(T("Analyze import", "分析导入内容"), () => SendPackPreview("preview-import", _packImportSource.Trim().Trim('"'))).interactable = !busy;
            form.Action(T("Back to library", "返回整合包库"), () => { _packPane = "library"; _management?.Rebuild(ManagementTab.Packs); }, UiActionRole.Neutral);
            form.Text(T("Select a source only when importing a new modpack. Stored modpacks are viewed directly from the library.", "仅导入新整合包需要选择来源；已保存的整合包直接从左侧查看。"));
        }
        else if (_packPane == "task" && task != null)
        {
            form.Text(task.Request.Operation.StartsWith("maintenance-", StringComparison.Ordinal) ? task.Maintenance?.Operation == MaintenanceOperations.DeletePack ? Loc.T("Delete modpack · check scope", "删除整合包 · 核对范围", "刪除整合包 · 核對範圍") : Loc.T("Storage · check scope", "空间管理 · 核对范围", "空間管理 · 核對範圍") : task.Request.Operation.Contains("import") ? T("Import · Review", "导入整合包 · 核对内容") : task.Request.Operation.Contains("adopt") ? T("Temporary modpack · Save", "临时整合包 · 保存当前快照") : T("Switch · Review changes", "切换整合包 · 核对影响"), true);
            BuildPackOperation(form, task); shown = task;
            if (!busy) form.Action(_packStorageTab ? Loc.T("Back to storage", "返回空间管理", "返回空間管理") : T("Back to library", "返回整合包库"), () => { _packPane = _packStorageTab ? "storage" : "library"; _storageLoading = null; _management?.Rebuild(ManagementTab.Packs); }, UiActionRole.Neutral);
        }
        else
        {
            var pack = index.Packs.FirstOrDefault(p => p.Id == _packSelectedId);
            if (pack == null) form.Text(T("Select a modpack on the left to view its contents.", "从左侧选择整合包以查看内容。"), true);
            else
            {
                var revision = pack.UnsavedEnvironment ?? pack.PendingUpdate ?? pack;
                if (pack.UnsavedEnvironment != null) form.Text(Loc.T("Has an unsaved running environment; switching back restores it.", "含未保存运行环境；切回自动恢复。", "含未儲存執行環境；切回時自動還原。"), true);
                form.Text(pack.Name, true);
                form.Text((pack.Id == index.ActivePackId ? T("Active", "当前启用") : T("Stored", "已保存"))
                    + (revision.Version.Length > 0 ? " · v" + revision.Version : "") + " · " + revision.Files.Count + T(" files", " 个文件"));
                if (!PackEnvironmentPolicy.IsComplete(revision))
                {
                    form.Text(pack.Id == index.ActivePackId ? Loc.T("Legacy environment: a full upgrade captures the current core, host and component records after the game exits; the original version is kept.", "旧格式环境（legacy）：完整升级须退出游戏后捕获当前 core、宿主与组件记录；原版本保留。", "舊格式環境（legacy）：完整升級須結束遊戲後擷取目前 core、宿主與組件記錄；原版本保留。") : Loc.T("Legacy stored pack: core and host must be completed from an explicit original source; the current core is not used to fake a historical environment.", "旧格式库存：须从明确原始来源补齐 core 与宿主，不能用当前 core 伪造历史环境。", "舊格式庫存：須從明確原始來源補齊 core 與宿主，不能用目前 core 偽造歷史環境。"));
                    if (pack.Id == index.ActivePackId)
                        form.Button(Loc.T("Preview upgrade to an independent environment", "预览升级为独立环境", "預覽升級為獨立環境"), () => SendPackPreview("preview-upgrade-environment", "", pack.Id), UiActionRole.Edit).interactable = !busy;
                }
                string cache = StoredPackDetails.CacheKey(revision);
                if (_packLoadingKey != cache) { _packLoadingKey = cache; _packLoading = null; _packLoadRendered = false; }
                // Start only on the next poll: the first frame renders the list and basic metadata without scanning files.
                if (_packLoading == null || !_packLoading.IsCompleted) form.Text(T("Loading details in the background…", "正在后台加载详情……"));
                else if (_packLoading.IsFaulted)
                {
                    form.Text(T("Could not load details: ", "详情加载失败：") + _packLoading.Exception?.GetBaseException().Message);
                    _packLoadRendered = true;
                }
                else
                {
                    shown = _packLoading.Result; _packLoadRendered = true;
                    var view = new PackPreviewView(form.Content, shown, form.Width, () => _management?.Rebuild(ManagementTab.Packs), path => OpenPackFile(shown, path));
                    form.Block((_, y) => view.LanguageRow(y));
                    form.Block((_, y) => view.Body(y));
                    if (shown.Report.Languages.Count > 0) form.Button(T("Apply language file", "应用所选语言文件"), () => {
                        string language = PackPreviewView.PackLanguageChoice.Length > 0 ? PackPreviewView.PackLanguageChoice : shown.Proposed.Language;
                        OverviewUpdateQueue.SendPack(new PackRequest { Operation = "set-language", PackId = pack.Id, PackLanguage = language });
                        if (pack.Id == index.ActivePackId && !OverviewLanguageBridge.Select(language, out string error)) throw new IOException(error);
                        ManagementStatus(T("Language selection saved.", "语言文件选择已保存。"));
                    });
                }
                if (pack.Id != index.ActivePackId || pack.PendingUpdate != null)
                    form.Action(T("Preview switch", "预览切换影响"), () => SendPackPreview("preview-switch", "", pack.Id)).interactable = !busy && revision.Problems.Count == 0;
                form.Action(T("Open pack folder", "打开整合包目录"), () => ManagementUi.OpenFolder(StoredPackDetails.Content(revision)), UiActionRole.Neutral);
                form.Button(T("Open library folder", "打开整合包库目录"), () => ManagementUi.OpenFolder(PackLibrary.Home), UiActionRole.Neutral);
                if (pack.Id != index.ActivePackId)
                {
                    form.Button(T("Delete stored modpack…", "删除库内整合包……"), () => PreviewMaintenance(new MaintenanceRequest { Operation = MaintenanceOperations.DeletePack, PackId = pack.Id }), UiActionRole.Danger).interactable = !busy;
                    form.Button(T("Remove a mod…", "移除包内模组……"), () => PickPackModRemoval(revision), UiActionRole.Neutral).interactable = !busy;
                }
            }
        }
        form.Finish();
        string scrollKey = _packPane + ":" + (shown?.Id ?? "");
        if (_packScrollTask != scrollKey) { _packScrollTask = scrollKey; _packScrollY = 0; }
        float max = Mathf.Max(0, form.Content.sizeDelta.y - Mathf.Max(70, detailHost.rect.height - 62));
        form.Content.anchoredPosition = new Vector2(0, Mathf.Min(_packScrollY, max));
        form.Scroll.onValueChanged.AddListener(_ => _packScrollY = form.Content.anchoredPosition.y);
    }

    private void BuildPackOperation(ManagementForm form, PackTask task)
    {
        string op = task.Request.Operation;
        string what = op == "preview-import" ? T("Previewing", "正在预览") : op == "preview-adopt-environment" ? T("Preparing temporary modpack preview", "正在生成临时整合包预览") : T("Preparing to switch", "正在准备切换");
        switch (task.State)
        {
            case "requested": case "preparing":
                form.Text(task.Message.Length > 0 ? task.Message : what + "……", true); return;
            case "maintenance-preview": BuildMaintenancePreview(form, task); return;
            case "preview": BuildPackPreview(form, task); return;
            case "waiting-exit":
                form.Text(PackStatusText(task), true);
                form.Text(T("Wait for completion before launching again. Launching early stops the operation and restores the previous setup.", "请等待完成后再启动游戏。提前启动会中止操作并恢复原有配置。"));
                form.Button(T("Cancel", "取消"), () => OverviewUpdateQueue.SendPack(new PackRequest { Operation = "cancel", PreviewId = task.Id }), UiActionRole.Neutral); return;
            case "saving": case "switching": form.Text(PackStatusText(task), true); return;
            case "recovery-required":
                form.Text(T("The last switch did not finish. Exit PEAK and wait; the manager will restore the previous setup.", "上次切换未完成。请退出 PEAK 并等待，管理器会恢复原有配置。"), true);
                if (task.Error.Length > 0) form.Text(task.Error);
                form.Button(Loc.T("Retry restoring the original environment", "重试恢复原环境", "重試還原原環境"), () => { OverviewUpdateQueue.SendPack(new PackRequest { Operation = "retry-recovery", PreviewId = task.Id }); PackSwitchProgressWindow.Show(task.Id); });
                return;
            case "completed": form.Text(task.Message.Length > 0 ? task.Message : T("Done.", "已完成。"), true); return;
            case "cancelled": form.Text(T("Cancelled. Nothing was changed.", "已取消，未做任何改动。")); return;
            default:
                form.Text(T("Not completed", "操作未完成"), true);
                if (task.Error.Length > 0)
                {
                    var line = PackReportText.Problem(task.Error);
                    form.Text(line.Title + (line.Detail.Length > 0 ? "\n" + line.Detail : "") + (line.Hint.Length > 0 ? "\n" + T("Suggestion: ", "建议：") + line.Hint : ""));
                }
                else if (task.Message.Length > 0) form.Text(task.Message);
                form.Text(task.Message.Length > 0 ? task.Message : Loc.T("The task did not finish. Check the transaction details to confirm the recovery state.", "任务未完成；请查看事务详情确认恢复状态。", "任務未完成；請查看交易詳情確認還原狀態。"));
                if (task.Request.Operation == "confirm-switch") form.Button(Loc.T("Recheck and retry switch", "重新检查并重试切换", "重新檢查並重試切換"), () => { OverviewUpdateQueue.SendPack(new PackRequest { Operation = "retry-switch", PreviewId = task.Id }); PackSwitchProgressWindow.Show(task.Id); }, UiActionRole.Commit);
                return;
        }
    }

    private void BuildPackPreview(ManagementForm form, PackTask task)
    {
        if (_packPreviewId != task.Id) { _packPreviewId = task.Id; PackPreviewView.PackLanguageChoice = ""; }
        var view = new PackPreviewView(form.Content, task, form.Width, () => _management?.Rebuild(ManagementTab.Packs), path => OpenPackFile(task, path));
        if (PackPreviewView.PackLanguageChoice.Length > 0) task.Proposed.Language = PackPreviewView.PackLanguageChoice;
        form.Block((content, top) => view.Banner(top));
        form.Block((content, top) => view.LanguageRow(top + 4));
        if (task.Details.Any(d => d.StartsWith("无独立整合包身份", StringComparison.Ordinal)))
            form.Text(T("This file has no modpack identity. It will be added to the active modpack as a personal profile; nothing is switched.", "此文件没有整合包标识，将作为个人方案追加到当前启用的整合包，不会切换。"));
        bool upgrading = task.Request.Operation == "preview-upgrade-environment";
        bool savingTemporary = task.Request.Operation == "preview-adopt-environment";
        if (task.Request.Operation == "preview-switch")
            form.Text(Loc.T("New and changed files in the current folder belong to the current modpack. Save and switch updates its saved version; switching without saving keeps the unsaved environment and restores it when you switch back. A complete environment replaces core and host on its own; legacy packs are not completed automatically.", "当前目录里的新增和修改均属于当前整合包。保存后切换会更新当前已保存版本；直接切换保留未保存环境，切回时自动恢复。完整环境独立替换 core 与宿主；旧格式不会自动补全。", "目前目錄裡的新增和修改均屬於目前整合包。儲存後切換會更新目前已儲存版本；直接切換保留未儲存環境，切回時自動還原。完整環境獨立取代 core 與宿主；舊格式不會自動補全。"));
        bool importing = task.Request.Operation == "preview-import";
        if (importing && task.PendingDownloads.Count > 0)
        {
            // Nothing has been downloaded yet. The list below is exactly what the helper will fetch, and nothing else can be sent.
            form.Text(T("Waiting for your approval to download", "待你同意下载") + " · " + task.PendingDownloads.Count, true);
            form.Text(string.Join("\n", task.PendingDownloads.Take(30).Select(d => "- " + PackReportText.Pretty(d.Key) + " " + d.Version + (d.RequiredBy.Length > 0 ? "  (" + d.RequiredBy + ")" : "")))
                + (task.PendingDownloads.Count > 30 ? "\n" + T("… and ", "……另有 ") + (task.PendingDownloads.Count - 30) + T(" more", " 个") : ""));
            form.Text(T("Only these Thunderstore packages are downloaded. The pack is not stored until the dependencies are complete and you confirm again.", "只会下载以上 Thunderstore 依赖。补齐后会重新分析，你再次确认才会存入整合包库。"));
            form.Action(T("Approve download and complete", "同意下载并补齐"), () => ConfirmPackDownload(task), UiActionRole.Commit).interactable = _packProfileCommitting != task.Id;
            form.EndRow();
            form.Block((content, top) => view.Body(top + 4));
            return;
        }
        bool blocked = task.Report.Verdict == "attention" && (!importing || PackEnvironmentPolicy.IsComplete(task.Proposed));
        string label = task.SingleProfile ? T("Add as personal profile", "添加为个人方案")
            : importing ? T("Store in library (no game changes)", "存入整合包库（不改动游戏）")
            : upgrading ? Loc.T("Upgrade environment after exit", "确认退出后升级环境", "確認結束後升級環境") : savingTemporary ? T("Save complete snapshot after exit", "退出后保存完整环境快照") : T("Switch after exit", "退出游戏后切换");
        if (task.Request.Operation == "preview-switch")
        {
            form.Action(Loc.T("Save current and switch", "保存当前配置并切换", "儲存目前設定並切換"), () => ConfirmPackPreview(task, true), UiActionRole.Commit).interactable = !blocked;
            form.Action(Loc.T("Switch without saving", "直接切换", "直接切換"), () => ConfirmPackPreview(task, false), UiActionRole.Edit).interactable = !blocked;
        }
        else form.Action(label, () => ConfirmPackPreview(task), UiActionRole.Commit).interactable = !blocked && _packProfileCommitting != task.Id;
        form.EndRow();
        form.Block((content, top) => view.Body(top + 4));
    }

    private void PreviewMaintenance(MaintenanceRequest request)
    {
        if (PackLibrary.Task()?.BlocksUpdates == true) throw new IOException("整合包切换正在等待退出或恢复，请先处理该任务。");
        _packSentKey = PackTaskKey(PackLibrary.Task()); _packSentAt = Time.unscaledTime;
        _packStorageTab = request.Operation is MaintenanceOperations.CleanCache or MaintenanceOperations.PurgePackTrash;
        if (!_packStorageTab && request.PackId.Length > 0) _packSelectedId = request.PackId;
        _packPane = "task";
        OverviewUpdateQueue.SendPack(new PackRequest { Operation = "maintenance-preview", Maintenance = request });
        ManagementStatus(Loc.T("Analysing file scope and reclaimable space…", "正在分析文件范围与可清理空间……", "正在分析檔案範圍與可清理空間……"));
    }
    private void OpenPackStorage()
    {
        _packStorageTab = true; _packPane = "storage"; _storageLoading = null; _storageRendered = false; _packScrollY = 0;
        _management?.Rebuild(ManagementTab.Packs);
    }
    private void DeleteStorageProject(StorageTreeNode node)
    {
        string relative = Path.GetRelativePath(BepInEx.Paths.BepInExRootPath, node.Path).Replace((char)92, '/');
        PreviewMaintenance(new MaintenanceRequest {
            Operation = node.Scope == MaintenanceScopes.PackTrash ? MaintenanceOperations.PurgePackTrash : MaintenanceOperations.CleanCache,
            PackId = node.Scope == MaintenanceScopes.PackTrash ? node.TrashId : "",
            Scopes = node.Scope == MaintenanceScopes.PackTrash ? new() : new List<string> { node.Scope },
            WholeCategory = node.Category,
            SelectedPaths = node.Category ? new List<string>() : new List<string> { relative }
        });
    }
    private void PickPackModRemoval(PackRecord pack)
    {
        var folders = pack.Files.Select(f => f.Path.Replace((char)92, '/')).Where(p => p.StartsWith("plugins/") && PluginFileNames.IsAssembly(p))
            .Select(p => p.Substring(0, p.LastIndexOf('/'))).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
        OverviewUi.Choose(Loc.T("Choose the mod folder to remove", "选择要移除的模组目录", "選擇要移除的模組目錄"), folders.Select(p => (p, p)).ToList(), "", folder => {
            var files = pack.Files.Where(f => f.Path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)).Select(f => f.Path).ToList();
            PreviewMaintenance(new MaintenanceRequest { Operation = MaintenanceOperations.DeleteMod, PackId = pack.Id, ModFiles = files });
        });
    }
    private void BuildMaintenancePreview(ManagementForm form, PackTask task)
    {
        var preview = task.Maintenance; if (preview == null) { form.Text(Loc.T("The maintenance preview is missing; please try again.", "维护预览缺失，请重新操作。", "維護預覽遺失，請重新操作。")); return; }
        form.Text(Loc.T("Delete / clean-up scope", "删除／清理范围", "刪除／清理範圍"), true);
        form.Text(Loc.F("Modpack: {0}\nItems: {1}\nFiles: {2}\nSpace: {3} MB", "整合包：{0}\n项目：{1}\n文件：{2}\n空间：{3} MB", "整合包：{0}\n項目：{1}\n檔案：{2}\n空間：{3} MB", preview.PackName, preview.Items.Count, preview.TotalFiles, (preview.TotalBytes / 1048576d).ToString("0.00")));
        foreach (var blocker in preview.Blockers) form.Text(Loc.T("Cannot run: ", "无法执行：", "無法執行：") + blocker.Message);
        foreach (var warning in preview.Warnings) form.Text(Loc.T("Note: ", "注意：", "注意：") + warning);
        foreach (var item in preview.Items.Take(40)) form.Text(item.Path + " · " + Loc.F("{0} files", "{0} 个文件", "{0} 個檔案", item.FileCount));
        foreach (var detail in preview.Details) form.Text(detail);
        form.Action(preview.Operation == MaintenanceOperations.DeletePack ? Loc.T("Move to recycle area", "确认移入回收区", "確認移入回收區") : Loc.T("Run clean-up / removal", "确认执行清理／移除", "確認執行清理／移除"), () => {
            OverviewUi.ConfirmText(Loc.T("Confirm file operation", "确认文件操作", "確認檔案操作"), Loc.T("The previewed scope will be processed. Deleted modpacks go to the recycle area first; cache clean-up and emptying the recycle area cannot be undone. Continue?", "将执行以上预览范围。整合包删除先移入回收区；缓存清理与回收区永久清除不可恢复。继续？", "將執行以上預覽範圍。整合包刪除先移入回收區；快取清理與回收區永久清除無法復原。繼續？"), () => {
                var request = new MaintenanceRequest { Operation = preview.Operation, PackId = preview.PackId, PreviewId = preview.Id, ModFiles = preview.ModFiles, Scopes = preview.Scopes, SelectedPaths = preview.SelectedPaths, WholeCategory = preview.WholeCategory, PermanentConfirmed = preview.Operation == MaintenanceOperations.PurgePackTrash };
                OverviewUpdateQueue.SendPack(new PackRequest { Operation = "maintenance-execute", Maintenance = request });
            }, UiActionRole.Danger);
        }, UiActionRole.Danger).interactable = preview.CanExecute && PackLibrary.Task()?.BlocksUpdates != true;
    }
    private void OpenPackFile(PackTask task, string relative)
    {
        var panel = OverviewUi.CreateModalPanel("PackFileBrowser", 1080, 760); if (panel == null) return;
        var body = OverviewUi.Rect("FileContent", panel); OverviewUi.Place(body, 16, 46, panel.rect.width - 32, panel.rect.height - 106);
        var files = new PackContentFiles(task);
        var content = new PublicationContentView(body, _repo, files, ManagementStatus);
        panel.gameObject.AddComponent<PublicationPreviewLifetime>().Release = content.Dispose;
        void CommitCopy()
        {
            files.SavePersonalCopy(task.Proposed.Name + Loc.T(" (personal copy)", "（个人副本）", "（個人副本）")); OverviewUi.CloseModal();
            _packPane = "task"; _management?.Rebuild(ManagementTab.Packs);
        }
        void Leave()
        {
            if (content.TryCloseLayer()) return;
            PackFileCloseGuard.Request(content.HasDirty, files.HasEdits, content.SaveCurrent, CommitCopy, OverviewUi.CloseModal,
                (saveDraft, discard, stay) => OverviewUi.ConfirmUnsaved(Loc.T("Changes are not saved as a personal copy yet", "修改尚未保存为个人副本", "修改尚未儲存為個人副本"), saveDraft, discard, stay));
        }
        OverviewUi.SetModalCloseHandler(Leave);
        OverviewUi.SetModalDirtyCheck(() => content.HasDirty || files.HasEdits);
        var back = OverviewUi.Button(panel, T("Close", "关闭"), Leave, 100, 32, UiActionRole.Neutral);
        OverviewUi.Place((RectTransform)back.transform, 16, 8, 100, 32);
        var save = OverviewUi.Button(panel, T("Save as personal copy", "保存为个人副本"), () => { if (content.SaveCurrent()) CommitCopy(); }, 200, 34, UiActionRole.Commit);
        OverviewUi.Place((RectTransform)save.transform, 16, panel.rect.height - 48, 200, 34);
        var shortcuts = new[] { Loc.T("Open file", "打开文件", "開啟檔案"), Loc.T("Containing folder", "所在文件夹", "所在資料夾"), Loc.T("Locate file", "定位文件", "定位檔案") };
        for (int i = 0; i < shortcuts.Length; i++)
        {
            int action = i;
            var shortcut = OverviewUi.Button(panel, shortcuts[i], () => {
                string? path = files.Resolve(relative); if (path == null) throw new FileNotFoundException("文件不存在。");
                if (action == 2) PackFileShortcuts.Reveal(path); else PackFileShortcuts.Open(path, action == 1);
            }, 100, 30, UiActionRole.Neutral);
            OverviewUi.Place((RectTransform)shortcut.transform, 128 + i * 112, 8, 100, 30);
        }
        content.Open(relative);
    }
    private void NameTemporaryPack()
    {
        OverviewUi.AskValidated(T("Save current setup as a temporary modpack", "保存当前配置为临时整合包"),
            new[] { (T("Modpack name", "整合包名称"), T("Temporary modpack", "临时整合包") + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")) },
            values => string.IsNullOrWhiteSpace(values[0]) || values[0].Trim().Length > 80 || values[0].Any(char.IsControl)
                ? T("Enter a name of 1–80 characters without line breaks.", "请输入 1–80 个字符的名称，不含换行。") : null,
            values => ManagementAction(() => SendPackPreview("preview-adopt-environment", snapshotName: values[0].Trim())));
    }
    private void SendPackPreview(string operation, string source = "", string packId = "", string snapshotName = "")
    {
        if (operation == "preview-import" && source.Length == 0) throw new IOException(T("Select a modpack file or folder first.", "请先选择整合包文件或文件夹。"));
        if (operation == "preview-switch" && PackLibrary.Read().ActivePackId.Length == 0)
        {
            OverviewUi.ConfirmText(T("Save the current setup first", "请先保存当前配置为临时整合包"),
                T("Switching replaces the active mods and settings. Save the current setup as a named temporary modpack first so it can be restored later. Continue to name it?",
                  "切换会替换当前模组和配置。请先将当前配置保存为一个有名称的临时整合包，以便之后切换回来。继续输入名称？"),
                () => ManagementAction(NameTemporaryPack), UiActionRole.Commit, T("Continue", "继续"));
            return;
        }
        if (operation == "preview-switch" && !OverviewRepository.IsMainMenuScene) throw new InvalidOperationException(T("Return to the main menu before switching modpacks.", "请返回主菜单后切换整合包。"));
        if (operation is "preview-switch" or "preview-adopt-environment" or "preview-upgrade-environment") _repo.Save();
        _packPane = "task";
        _packSentKey = PackTaskKey(PackLibrary.Task()); _packSentAt = Time.unscaledTime;
        var preview = PackLibrary.Request(operation, _repo, source, packId); preview.SnapshotName = snapshotName;
        OverviewUpdateQueue.SendPack(preview);
        ManagementStatus(T("Request sent. Waiting for the helper…", "请求已发送，等待助手响应……"));
        _management?.Rebuild(ManagementTab.Packs);
    }

    private void ConfirmPackDownload(PackTask task)
    {
        if (task.Request.Operation != "preview-import" || task.PendingDownloads.Count == 0 || task.DownloadPlan.Length == 0) throw new IOException(T("Preview again before downloading.", "请重新预览后再下载。"));
        // The command carries only the preview id and its plan hash: no source, URL or package list.
        var request = new PackRequest { Operation = "confirm-download", PreviewId = task.Id, Approved = true, ApprovedPlan = task.DownloadPlan };
        string list = string.Join("\n", task.PendingDownloads.Take(12).Select(d => "- " + PackReportText.Pretty(d.Key) + " " + d.Version))
            + (task.PendingDownloads.Count > 12 ? "\n" + T("… and ", "……另有 ") + (task.PendingDownloads.Count - 12) + T(" more", " 个") : "");
        OverviewUi.ConfirmText(T("Approve download", "同意下载并补齐"),
            T("Download these Thunderstore packages and analyze the import again? Nothing is stored in the library yet.\n", "从 Thunderstore 下载以下依赖并重新分析？此时仍不会存入整合包库。\n") + list,
            () => ManagementAction(() => {
                _packSentKey = PackTaskKey(PackLibrary.Task()); _packSentAt = Time.unscaledTime;
                OverviewUpdateQueue.SendPack(request); ManagementStatus(T("Approved. Downloading dependencies…", "已同意，正在下载依赖……")); _management?.Rebuild(ManagementTab.Packs);
            }), UiActionRole.Commit, T("Approve", "同意下载"));
    }

    private void ConfirmPackPreview(PackTask task, bool saveCurrent = true)
    {
        if (task.SingleProfile)
        {
            string prefix = Path.GetFullPath(Path.Combine(PackLibrary.Home, ".imports")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(task.StagingDirectory).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("方案导入目录越界。");
            var imported = _repo.Profiles.ImportDirectory(task.StagingDirectory);
            _packProfileCommitting = task.Id;
            OverviewUpdateQueue.SendPack(new PackRequest { Operation = "complete-profile", PreviewId = task.Id });
            ManagementStatus(T("Personal profile added: ", "已添加个人方案：") + imported.name); _management?.Rebuild(ManagementTab.Packs); return;
        }
        string operation = task.Request.Operation.Replace("preview-", "confirm-");
        var request = PackLibrary.Request(operation, _repo); request.PreviewId = task.Id;
        request.SaveCurrentBeforeSwitch = saveCurrent; request.PackLanguage = task.Proposed.Language;
        string body = operation == "confirm-upgrade-environment" ? Loc.T("After PEAK exits, the real core, host, runtime settings and component states are captured as an independent version. The old version is kept and other legacy packs are not completed. Wait for the helper to finish.", "退出 PEAK 后捕获当前真实 core、宿主、运行配置与组件状态为独立版本；保留旧版本，不补全其他旧库存。请等待助手完成。", "結束 PEAK 後擷取目前真實 core、宿主、執行設定與組件狀態為獨立版本；保留舊版本，不補全其他舊庫存。請等待助手完成。")
            : operation == "confirm-adopt-environment"
            ? T("Save the current mods, patches, settings and personal profiles as ", "将当前模组、启动补丁、配置和个人方案保存为临时整合包：") + task.Proposed.Name
                + T(". Wait for PEAK to exit, then capture core, host, runtime settings and component ownership. Game files are not replaced.", "。等待 PEAK 退出后收取 core、宿主、运行配置与组件归属，不替换游戏文件。")
            : operation == "confirm-import" ? T("Store this modpack in the library? Game files are not changed. A modpack with issues can be stored but cannot be enabled until they are resolved.", "将此整合包存入整合包库？不会改动游戏文件。存在问题的整合包可以先存入，问题解决之前无法启用。")
            : saveCurrent ? Loc.F("After PEAK exits, the current environment's final files and settings are saved as the current pack version, then it switches to \"{0}\". Wait for it to finish before launching.", "PEAK 退出后，将当前环境的最终文件与配置保存为当前包版本，再切换到「{0}」。请等待完成后再启动。", "PEAK 結束後，將目前環境的最終檔案與設定儲存為目前套件版本，再切換到「{0}」。請等待完成後再啟動。", task.Proposed.Name)
            : Loc.F("After PEAK exits, it switches straight to \"{0}\". The saved version stays unchanged and the running environment is kept separately; switching back restores it and keeps it marked unsaved.", "PEAK 退出后直接切换到「{0}」。当前已保存版本保持不变，运行环境单独保留；切回时自动恢复，并继续标记为未保存。", "PEAK 結束後直接切換到「{0}」。目前已儲存版本保持不變，執行環境單獨保留；切回時自動還原，並繼續標記為未儲存。", task.Proposed.Name);
        OverviewUi.ConfirmText(T("Confirm", "确认"), body, () => ManagementAction(() => {
            OverviewUpdateQueue.SendPack(request);
            if (operation is "confirm-switch" or "confirm-adopt-environment" or "confirm-upgrade-environment") PackSwitchProgressWindow.Show(task.Id);
            ManagementStatus(T("Confirmed. See the status above.", "已确认，进度见上方。"));
        }));
    }

    /// <summary>The bottom status line must always describe the task as it is now, not as it was when a button was clicked.</summary>
    private static string PackStatusText(PackTask? task)
    {
        if (task == null) return "";
        int problems = task.Report.Lines.Count(l => l.Section == "problem" && !_ignoredPackLines.Contains(l.Title));
        return task.State switch
        {
            "requested" or "preparing" => task.Message.Length > 0 ? task.Message : task.Request.Operation switch {
                "confirm-import" => T("Adding modpack to library…", "正在存入整合包库……"),
                "preview-adopt-environment" => T("Preparing temporary modpack preview…", "正在生成临时整合包预览……"),
                "confirm-adopt-environment" => T("Preparing temporary modpack save…", "正在准备保存临时整合包……"),
                _ => T("Previewing…", "正在预览……") }, 
            "preview" => task.PendingDownloads.Count > 0 ? T("Preview complete: ", "预览完成：") + task.PendingDownloads.Count + T(" dependencies wait for your approval to download.", " 个依赖等待你同意下载。")
                : task.Report.Verdict == "ready" ? T("Preview complete: no issues.", "预览完成：未发现问题。")
                : T("Preview complete: ", "预览完成：") + problems + T(" issue(s). See Issues below.", " 项问题，详见下方“待处理”。"),
            "waiting-exit" => task.Request.Operation == "confirm-adopt-environment"
                ? T("Waiting for game exit to save temporary modpack: ", "等待游戏退出，随后保存临时整合包：") + task.Proposed.Name
                : T("Waiting for game exit to switch modpacks.", "等待游戏退出，随后切换整合包。"),
            "saving" or "switching" => task.Message.Length > 0 ? task.Message : T("Processing. Do not launch the game.", "正在处理，请勿启动游戏。"),
            "recovery-required" => T("The last switch did not finish. Exit PEAK and wait.", "上次切换未完成，请退出 PEAK 并等待。"),
            "completed" => task.Message.Length > 0 ? task.Message : T("Done.", "已完成。"),
            "cancelled" => T("Cancelled. Nothing was changed.", "已取消，未做任何改动。"),
            _ => task.Error.Length > 0 ? PackReportText.Problem(task.Error).Title : task.Message.Length > 0 ? task.Message : T("Not completed. See the page for details.", "操作未完成，详见页面说明。"),
        };
    }

    private void ShowTemporaryPackResult(PackTask? task)
    {
        if (task == null || task.State != "completed" || task.Request.Operation != "confirm-adopt-environment" || _packCompletionShown == task.Id) return;
        _packCompletionShown = task.Id;
        PackBootstrap.NotifyTemporarySave();
    }
    private static string PackTaskKey(PackTask? task) => (task?.Id ?? "") + ":" + (task?.State ?? "") + ":" + (task?.Error ?? "") + ":" + (task?.Message ?? "");
    private void PollPacks()
    {
        if (Time.unscaledTime < _packPollAt) return; _packPollAt = Time.unscaledTime + .6f;
        if (_management?.IsOpen != true || _management.Current != ManagementTab.Packs) return;
        try
        {
            var task = PackLibrary.Task(); string key = PackTaskKey(task) + ":" + PackLibrary.Read().Revision;
            if (task?.State == "completed" && task.Request.Operation is "confirm-import" or "confirm-adopt-environment" or "personal-copy" or "set-language" && key != _packViewKey)
            { _packSelectedId = task.Proposed.Id; _packPane = "library"; _packLoadingKey = ""; }
            if (_packSentAt > 0)
            {
                if (PackTaskKey(task) != _packSentKey) { _packSentKey = ""; _packSentAt = 0; }
                else
                {
                    // The helper has not touched the task yet. Say so instead of leaving an old message on screen.
                    string reason = OverviewUpdateQueue.LastError;
                    if (reason.Length > 0) { ManagementStatus(T("Not started: ", "未开始：") + Loc.LocalizeMessage(reason)); _packSentAt = 0; _packSentKey = ""; }
                    else if (Time.unscaledTime - _packSentAt > 10) { ManagementStatus(T("The helper did not respond. Check the Updates page and try again.", "助手没有响应。请查看“更新”页后重试。")); _packSentAt = 0; _packSentKey = ""; }
                    return;
                }
            }
            if (_packPane == "storage")
            {
                if (_storageLoading == null) _storageLoading = PackStorageBrowser.Load(BepInEx.Paths.BepInExRootPath);
                if (_storageLoading.IsCompleted && !_storageRendered) _management.Rebuild(ManagementTab.Packs, true);
            }
            if (_packPane == "library")
            {
                var selected = PackLibrary.Read().Packs.FirstOrDefault(p => p.Id == _packSelectedId);
                if (selected != null)
                {
                    var revision = selected.UnsavedEnvironment ?? selected.PendingUpdate ?? selected; string selectedKey = StoredPackDetails.CacheKey(revision);
                    if (_packLoadingKey != selectedKey) { _packLoadingKey = selectedKey; _packLoading = null; _packLoadRendered = false; }
                    if (_packLoading == null) _packLoading = StoredPackDetails.LoadAsync(revision);
                    if (_packLoading.IsCompleted && !_packLoadRendered) _management.Rebuild(ManagementTab.Packs, true);
                }
            }
            if (key == _packViewKey) return;
            bool sameLibrary = _packViewKey.EndsWith(":" + PackLibrary.Read().Revision, StringComparison.Ordinal);
            _packViewKey = key; _management.Rebuild(ManagementTab.Packs, sameLibrary); ManagementStatus(PackStatusText(task));
            ShowTemporaryPackResult(task);
        }
        catch (Exception error) { ManagementStatus(error.Message); }
    }
}
