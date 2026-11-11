#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal static partial class SourceUi
{
    private static ModConfigEnhance.Updating.ComponentDependencyGraph? _dependencyGraph;
    private static string _dependencyContext = "";
    private static int _dependencyRevision = -1;
    private static IReadOnlyList<InfrastructureOverviewModel> _components = Array.Empty<InfrastructureOverviewModel>();
    internal static string DependencyContext {
        get {
            var active=PackLibrary.Active;
            return System.IO.Path.GetFullPath(active!=null ? PackLibrary.Workspace : BepInEx.Paths.BepInExRootPath)
                + "|" + (active?.RevisionStateHash ?? "");
        }
    }
    // Display-only identity. Mutation/ownership gates continue to use DependencyContext
    // and DependencyGraph above/below, with their live file/path validation.
    private static readonly DisplayRevisionCache<string> DisplayContext = new();
    internal static string DisplayDependencyContext => DisplayContext.Value ?? "";
    internal static void PollDisplayContext(bool force = false)
    {
        OverviewPageCache.Initialize();
        DisplayContext.Refresh(OverviewPageCache.Environment, Time.unscaledTime,
            OverviewPageCache.EnvironmentReliable, () => DependencyContext, force);
    }
    internal static ModConfigEnhance.Updating.ComponentDependencyGraph? DisplayDependencyGraph
        => _dependencyGraph == null || _dependencyContext != DisplayDependencyContext
            || _dependencyRevision != OverviewPageCache.Inventory ? null : _dependencyGraph;
    internal static void InvalidateDependencyGraph() { _dependencyGraph = null; _components = Array.Empty<InfrastructureOverviewModel>(); }
    internal static void PublishDependencyGraph(ModConfigEnhance.Updating.ComponentDependencyGraph graph, string context, int revision,
        IReadOnlyList<InfrastructureOverviewModel>? components = null)
    { _dependencyGraph = graph; _dependencyContext = context; _dependencyRevision = revision; _components = components ?? Array.Empty<InfrastructureOverviewModel>(); }
    internal static ModConfigEnhance.Updating.ComponentDependencyGraph? DependencyGraph
        => _dependencyGraph == null || _dependencyContext != DependencyContext
            || _dependencyRevision != OverviewPageCache.Inventory ? null : _dependencyGraph;
    internal static DiagnosticSummary DependencyDiagnostic(string key)
        => DiagnosticSummary.ForPackage(DependencyGraph, key);
    internal static string UninstallReason(string key) => DependencyDiagnostic(key).BlockReason;
    private static void ShowRelationships(OverviewRepository repo, IReadOnlyList<OverviewMod> mods, OverviewMod mod)
    {
        var graph = SharedRelationshipSnapshot.Get(mods, DependencyGraph == null ? Array.Empty<InfrastructureOverviewModel>() : _components, DependencyGraph);
        ComponentDetailsView.Open(repo, mods, null, SharedRelationshipSnapshot.RootId(mod, graph), graph, SharedRelationshipSnapshot.Components(graph));
    }
    internal static void ComponentMenu(OverviewRepository repo, IReadOnlyList<OverviewMod> mods,
        InfrastructureOverviewModel package, IReadOnlyList<InfrastructureOverviewModel> components, Action? changed = null)
        => ComponentDetailsView.Open(repo, mods, package, package.Key, DependencyGraph, components, changed);

    private static SourceMenuSection Section(string action) => action is "project" or "github" or "store" or "internal-store" ? SourceMenuSection.Pages
        : action is "uninstall" or "inspection-details" ? SourceMenuSection.Maintenance : SourceMenuSection.Source;

    internal static void ComponentFolders(InfrastructureOverviewModel package, RectTransform anchor, Action<string> status)
        => FolderShortcuts.Open(package, anchor, status);

    internal static void ComponentSourceMenu(OverviewRepository repo, IReadOnlyList<OverviewMod> mods,
        InfrastructureOverviewModel package, IReadOnlyList<InfrastructureOverviewModel> components)
    {
        bool identified = package.IdentityConfirmed && ShopInstalled.Key(package.Key);
        string reason = identified ? UninstallReason(package.Key)
            : Loc.T("Source identity is unconfirmed. Store actions are unavailable.", "来源身份尚未确认，商店操作暂不可用。", "來源身分尚未確認，商店操作暫不可用。");
        var entries = new List<(SourceMenuSection, UiMenuEntry)>();
        void Add(SourceMenuSection section, string label, Action action, bool enabled = true,
            string disabledReason = "", UiActionRole role = UiActionRole.Neutral, Func<bool>? canInvoke = null)
            => entries.Add((section, new UiMenuEntry { Label = label, Invoke = action, Enabled = enabled,
                DisabledReason = disabledReason, Role = role, CanInvoke = canInvoke }));
        var listing = identified ? ShopCatalog.Cached().FirstOrDefault(p => p.Key.Equals(package.Key, StringComparison.OrdinalIgnoreCase)) : null;
        if (listing != null && Uri.TryCreate(listing.Homepage, UriKind.Absolute, out var home) && (home.Scheme == "http" || home.Scheme == "https"))
            Add(SourceMenuSection.Pages, Loc.T("Project homepage", "项目主页", "專案首頁"), () => Application.OpenURL(listing.Homepage));
        Add(SourceMenuSection.Pages, Loc.T("View in mod store", "内置商店查看", "內建商店查看"),
            () => ShopWindow.Open(repo, mods, false, package.Key), identified);
        Add(SourceMenuSection.Pages, Loc.T("Open store in browser", "浏览器打开商店页", "瀏覽器開啟商店頁"), () => {
            int at = package.Key.IndexOf('-'); Application.OpenURL(ThunderstoreUpdateService.HomeUrl(package.Key.Substring(0, at), package.Key.Substring(at + 1)));
        }, identified);
        Add(SourceMenuSection.Source, Loc.T("Installed package source", "已安装资源包来源", "已安裝套件來源"), () => { }, false,
            identified ? package.Key : reason);
        Add(SourceMenuSection.Maintenance, Loc.T("Dependencies and dependants", "依赖与被依赖模组", "依賴與被依賴模組"),
            () => ComponentMenu(repo, mods, package, components));
        Add(SourceMenuSection.Maintenance, Loc.T("Uninstall (safe preview)", "卸载（安全预览）", "解除安裝（安全預覽）"),
            () => ShopWindow.Open(repo, mods, false, package.Key, true), identified && reason.Length == 0, reason, UiActionRole.Danger,
            () => identified && UninstallReason(package.Key).Length == 0);
        SourceMenuView.Show(package.Name, entries);
    }

    internal static void Menu(OverviewRepository repo, OverviewMod mod, Action refresh, Action<string> status, IReadOnlyList<OverviewMod>? mods = null)
    {
        var entries = Options(mod).Select(option => (Section(option.Item2), new UiMenuEntry {
            Label = option.Item1,
            Enabled = (option.Item2 != "uninstall" || UninstallReason(mod.SourceKey).Length == 0) && (option.Item2 != "local" || !ModSources.HasInstalledStore(mod)),
            DisabledReason = option.Item2 == "uninstall" ? UninstallReason(mod.SourceKey) : "",
            CanInvoke = () => (option.Item2 != "uninstall" || UninstallReason(mod.SourceKey).Length == 0) && (option.Item2 != "local" || !ModSources.HasInstalledStore(mod)),
            Role = option.Item2 == "uninstall" ? UiActionRole.Danger
                : option.Item2 is "project" or "github" or "store" or "internal-store" or "find" or "inspection-details" ? UiActionRole.Neutral : UiActionRole.Edit,
            Invoke = () => Execute(repo, mod, refresh, status, option.Item2, mods)
        })).ToArray();
        SourceMenuView.Show(OverviewLanguageBridge.GetDisplayName(mod), entries);
    }

    internal static void Actions(ManagementForm form, OverviewRepository repo, OverviewMod mod, Action refresh, Action<string> status, IReadOnlyList<OverviewMod>? mods = null)
    {
        form.Text(Loc.T("Pages and source", "页面与来源", "頁面與來源"), true);
        foreach (var option in Options(mod).Where(o => o.Item2 is "project" or "github" or "store" or "internal-store" or "find" or "address"))
            form.Button(option.Item1, () => Execute(repo, mod, refresh, status, option.Item2, mods));
        form.Text(Loc.T("Source classification", "来源识别", "來源識別"), true);
        foreach (var option in Options(mod).Where(o => o.Item2 is "local" or "auto"))
            form.Button(option.Item1, () => Execute(repo, mod, refresh, status, option.Item2, mods));
    }
    private static List<(string, string)> Options(OverviewMod mod)
    {
        var options = new List<(string, string)>();
        string github = GitHubUrl(mod);
        if (github.Length > 0) options.Add((Loc.T("Open GitHub", "打开 GitHub 链接", "開啟 GitHub 連結"), "github"));
        if (github.Length == 0 && Uri.TryCreate(mod.HomepageUrl, UriKind.Absolute, out var home) && (home.Scheme == "http" || home.Scheme == "https")) options.Add((Loc.T("Project homepage", "项目主页", "專案首頁"), "project"));
        if (mod.SourceConfirmed) {options.Add((Loc.T("View in mod store", "内置商店查看", "內建商店查看"), "internal-store"));options.Add((Loc.T("Open store in browser", "浏览器打开商店页", "瀏覽器開啟商店頁"), "store"));}
        options.Add((Loc.T("Find store source", "查找商店来源", "尋找商店來源"), "find"));
        options.Add((Loc.T("Enter store address", "手动填写商店地址", "手動填寫商店位址"), "address"));
        options.Add((Loc.T("Mark as local", "标记为本地", "標記為本機"), "local"));
        options.Add((Loc.T("Reset source and identify now", "重置来源并立即自动识别", "重設來源並立即自動識別"), "auto"));
        options.Add((Loc.T("Uninstall (safe preview)", "卸载（安全预览）", "解除安裝（安全預覽）"), "uninstall"));
        options.Add((Loc.T("Dependencies and dependants", "依赖与被依赖模组", "依賴與被依賴模組"), "inspection-details"));
        return options;
    }
    private static void Execute(OverviewRepository repo, OverviewMod mod, Action refresh, Action<string> status, string action, IReadOnlyList<OverviewMod>? mods = null)
    {
            if (action == "inspection-details") { ShowRelationships(repo, mods ?? new[] { mod }, mod); return; }
            if(action=="uninstall"){
                string reason=UninstallReason(mod.SourceKey);
                if(reason.Length>0){ShowRelationships(repo, mods ?? new[] { mod }, mod);return;}
                string ledgerPath=PackLibrary.Managed?System.IO.Path.Combine(PackLibrary.Workspace,"shop-installed.json"):System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"MCE","shop","installed-unmanaged.json");
                var ledger=System.IO.File.Exists(ledgerPath)?Newtonsoft.Json.JsonConvert.DeserializeObject<ModConfigEnhance.Updating.ShopLedger>(System.IO.File.ReadAllText(ledgerPath)):null;
                var owned=ledger?.Packages.FirstOrDefault(p=>p.PluginGuids.Contains(mod.Guid,StringComparer.OrdinalIgnoreCase));
                if(owned==null&&mod.SourceConfirmed&&System.IO.Directory.Exists(mod.PackageRoot)&&!System.IO.Path.GetFullPath(mod.PackageRoot).TrimEnd('\\','/').Equals(System.IO.Path.GetFullPath(BepInEx.Paths.PluginPath).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)&&System.IO.File.Exists(System.IO.Path.Combine(mod.PackageRoot,"manifest.json"))&&Version.TryParse(mod.PackageVersion,out _))owned=new ModConfigEnhance.Updating.ShopPackage{Key=mod.SourceNamespace+"-"+mod.SourceName,Version=mod.PackageVersion};
                if(owned==null){OverviewUi.ShowText(Loc.T("Uninstall not started","未开始卸载","未開始解除安裝"),mod.Name+"\n\n"+Loc.T("No verified independent package identity is available. Confirm the store source and inspect its installation status before requesting an exact-file uninstall preview. No files were deleted.","尚无可验证的独立包身份。请先核对商店来源和安装状态，再申请精确文件卸载预览。没有删除任何文件。","尚無可驗證的獨立套件身分，請先核對來源與安裝狀態；沒有刪除檔案。"));return;}
                int split=owned.Key.IndexOf('-');UninstallDialog.Open(mod.Name,new ModConfigEnhance.Updating.ShopRequest{Namespace=owned.Key.Substring(0,split),Package=owned.Key.Substring(split+1),Version=owned.Version});return;
            }
            if(action=="internal-store"){ShopWindow.OpenPackage(repo,mod);return;}
            if (action == "github") Application.OpenURL(GitHubUrl(mod));
            if (action == "project") Application.OpenURL(mod.HomepageUrl);
            if (action == "store") Application.OpenURL(ThunderstoreUpdateService.HomeUrl(mod.SourceNamespace, mod.SourceName));
            if (action == "find") Search(repo, mod, refresh, status);
            if (action == "local" && ModSources.HasInstalledStore(mod)) { status(Loc.T("Installed from the store; source already confirmed.", "此包已由商店安装，来源已确认。", "此包已由商店安裝，來源已確認。")); return; }
            if (action == "local") { ModSources.Set(new[] { mod }, new ModSourceRecord { kind = ModSourceKind.Local, ns = mod.SourceNamespace, name = mod.SourceName, author = mod.Author, homepage = mod.HomepageUrl }); refresh(); }
            if (action == "auto") { ResetAndIdentify(repo, mod, refresh, status); return; }
            if (action == "address") OverviewUi.Ask(Loc.T("Store source", "商店来源", "商店來源"), new[] { ("Thunderstore URL / Team-Package", "") }, values => {
                if (!ThunderstoreUpdateService.TryParseSource(values[0], out var ns, out var name)) { status(Loc.T("Invalid store address.", "商店地址无效。", "商店位址無效。")); return; }
                ValidateAddress(mod, ns, name, refresh, status); });
    }
    private static async void ValidateAddress(OverviewMod mod, string ns, string name, Action refresh, Action<string> status)
    {
        long revision = ModSources.ChoiceRevision; string context = ModSources.Context; string instance = ModSources.InstallationToken(mod);
        try {
            var catalog = await ModSources.Catalog(CancellationToken.None);
            if (revision != ModSources.ChoiceRevision || context != ModSources.Context || instance != ModSources.InstallationToken(mod)) return;
            var verified = new StoreIdentityIndex(catalog).Identity(ns, name);
            if (verified == null) { status(Loc.T("No active PEAK store identity was found.", "未找到有效的 PEAK 商店身份。", "未找到有效的 PEAK 商店身分。")); return; }
            ModSources.Confirm(mod, verified); refresh();
        } catch (Exception e) { status(e.Message); }
    }
    internal static async void Search(OverviewRepository repo, OverviewMod mod, Action refresh, Action<string> status, string? query = null, bool forceCatalog = false, Action? returnTo = null)
    {
        ModSources.Apply(mod);
        long revision = ModSources.ChoiceRevision; string instance = ModSources.InstallationToken(mod);
        bool Current() => revision == ModSources.ChoiceRevision && instance == ModSources.InstallationToken(mod);
        var panel = OverviewUi.CreateModalPanel("MCE_SourceCandidates", 820, 600); if (panel == null) return;
        PublicationNavigation.Attach(panel);
        var cancellation = new CancellationTokenSource(); panel.gameObject.AddComponent<SourceSearchLifetime>().Cancellation = cancellation;
        float width = panel.rect.width, height = panel.rect.height;
        var heading = OverviewUi.Text(panel, Loc.T("Choose store source", "选择商店来源", "選擇商店來源") + " · " + mod.Name, 22); OverviewUi.PlaceClipped(heading, 20, 12, width - 40, 36);
        bool compact = height < 480;
        float cardHeight = compact ? 72 : 96, lineHeight = compact ? 20 : 24, lineStep = compact ? 21 : 26;
        var localInfo = OverviewUi.Panel("InstalledMod", panel, ModConfigLook.Surface(new Color(.10f, .12f, .12f, .5f))); OverviewUi.Place(localInfo, 20, 52, width - 40, cardHeight);
        string original = string.IsNullOrWhiteSpace(mod.RawName) ? mod.Name : mod.RawName;
        string originalLabel = Loc.T("Original name: ", "本地原名：", "本機原名：") + original;
        if (!string.IsNullOrWhiteSpace(mod.PackageName) && !mod.PackageName.Equals(original, StringComparison.OrdinalIgnoreCase)) originalLabel += Loc.T(" · Package: ", " · 包名：", " · 套件名稱：") + mod.PackageName;
        var originalName = OverviewUi.Text(localInfo, originalLabel, compact ? 16 : 18); OverviewUi.PlaceClipped(originalName, 12, 5, width - 110, lineHeight);
        var localVersions = OverviewUi.Text(localInfo, VersionText(mod), 15); OverviewUi.PlaceClipped(localVersions, 12, 5 + lineStep, width - 66, lineHeight);
        var localAuthor = OverviewUi.Text(localInfo, AuthorText(mod), 15); OverviewUi.PlaceClipped(localAuthor, 12, 5 + lineStep * 2, width - 66, lineHeight);
        FolderButton(localInfo, mod, width - 86, 7, status, repo);
        float searchTop = 62 + cardHeight, messageTop = searchTop + 40, columnsTop = messageTop + 30, rowsTop = columnsTop + 30;
        var input = OverviewUi.Input(panel, query ?? ModSources.SearchName(mod)); input.textComponent.fontSize = 20; input.textComponent.margin=Vector4.zero; input.textComponent.alignment=TextAlignmentOptions.MidlineLeft; input.caretWidth=2; OverviewUi.Place((RectTransform)input.transform, 20, searchTop, width - 160, 38);
        var search = OverviewUi.Button(panel, Loc.T("Search", "搜索", "搜尋"), () => Search(repo, mod, refresh, status, input.text, false, returnTo), 105, 34, role: UiActionRole.Neutral); OverviewUi.Place((RectTransform)search.transform, width - 125, searchTop, 105, 34);
        var message = OverviewUi.Text(panel, Loc.T("Reading store catalog…", "正在读取商店目录……", "正在讀取商店目錄……"), 16); OverviewUi.Place(message.rectTransform, 20, messageTop, width - 40, 26);
        StorePackage? selected = null;
        var confirm = OverviewUi.Button(panel, Loc.T("Use this source", "使用此来源", "使用此來源"), () => { if (selected == null || !Current()) return; ModSources.Confirm(mod, selected); OverviewUi.CloseModal(); refresh(); returnTo?.Invoke(); }, 150, 34, role: UiActionRole.Commit); OverviewUi.Bottom((RectTransform)confirm.transform, 20, 16); confirm.interactable = false;
        void Back() { OverviewUi.CloseModal(); returnTo?.Invoke(); }
        var cancel = OverviewUi.Button(panel, Loc.T("Cancel", "取消", "取消"), Back, 95, 34, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)cancel.transform, 185, 16);
        OverviewUi.SetModalCloseHandler(Back);
        var reload = OverviewUi.Button(panel, Loc.T("Refresh catalog", "刷新目录", "重新整理目錄"), () => Search(repo, mod, refresh, status, input.text, true, returnTo), 110, 34, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)reload.transform, 295, 16);
        try
        {
            var catalog = await ModSources.Catalog(cancellation.Token, forceCatalog);
            if (panel == null || cancellation.IsCancellationRequested || !Current()) return;
            if (query == null && !ModSources.ExplicitLocal(mod) && ModSources.Exact(catalog, mod) is StorePackage)
            { await ModSources.Discover(new[] { mod }, cancellation.Token); OverviewUi.CloseModal(); refresh(); returnTo?.Invoke(); return; }
            string term = input.text.Trim();
            var matches = ModSources.Candidates(catalog, term, mod);
            message.text = matches.Count == 0 ? Loc.T("No candidates found. Try a shorter original name or an author/team name.", "没有找到候选。请尝试更短的原名，或作者／团队名称。", "沒有找到候選。請嘗試更短的原名，或作者／團隊名稱。")
                : Loc.T("Related candidates — check the package before confirming.", "相关候选，请核对模组后再确认。", "相關候選，請核對模組後再確認。") + " (" + matches.Count + ")";
            OverviewUi.PlaceClipped(message, 20, messageTop, width - 40, 26);
            var columns = OverviewUi.Text(panel, Loc.T("Name · Author/team · Version · Updated", "模组名 · 作者／团队 · 版本 · 更新日期", "模組名 · 作者／團隊 · 版本 · 更新日期"), 15); OverviewUi.PlaceClipped(columns, 20, columnsTop, width - 40, 24);
            var content = Rows(panel, rowsTop, Mathf.Max(0, height - rowsTop - 64), out _);
            var evidenceIndex = new StoreIdentityIndex(catalog);
            for (int i = 0; i < matches.Count; i++)
            {
                var item = matches[i]; int index = i;
                var choose = FlatListRow.Create(content, () => { selected = item; confirm.interactable = true; message.text = item.Author + "-" + item.Name; PublicationNavigation.Attach(panel); }, width - 126, 68, ModConfigLook.Surface(new Color(.13f,.19f,.22f,.98f))); OverviewUi.Place((RectTransform)choose.transform, 0, i * 73, width - 126, 68);
                var title = OverviewUi.Text(choose.transform, item.Name, 17); OverviewUi.PlaceClipped(title, 8, 2, (width - 130) * .39f, 36);
                var team = OverviewUi.Text(choose.transform, item.Author, 15); OverviewUi.PlaceClipped(team, (width - 130) * .40f, 2, (width - 130) * .21f, 36);
                var version = OverviewUi.Text(choose.transform, item.Version, 15); OverviewUi.PlaceClipped(version, (width - 130) * .62f, 2, (width - 130) * .14f, 36);
                var date = OverviewUi.Text(choose.transform, item.Updated.ToLocalTime().ToString("yyyy-MM-dd"), 14); OverviewUi.PlaceClipped(date, (width - 130) * .77f, 2, (width - 130) * .23f - 8, 36);
                var reason = OverviewUi.Text(choose.transform, (item.Deprecated ? Loc.T("Deprecated package · ", "已弃用包 · ", "已棄用包 · ") : "") + evidenceIndex.Reason(item, mod), 13);
                reason.richText = false; reason.color = OverviewUi.Muted;
                OverviewUi.PlaceClipped(reason, 8, 38, width - 146, 26);
                var open = OverviewUi.Button(content, Loc.T("Store", "商店页", "商店頁"), () => Application.OpenURL(item.Url), 80, 34, role: UiActionRole.Neutral); OverviewUi.Place((RectTransform)open.transform, width - 122, i * 73 + 3, 78, 34);
            }
            content.sizeDelta = new Vector2(0, matches.Count * 73);
            PublicationNavigation.Attach(panel);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (panel != null) message.text = e.Message; }
    }
    internal static void BatchResults(OverviewRepository repo, IReadOnlyList<OverviewMod> mods, IReadOnlyList<StorePackage> catalog, Action refresh, Action<string> status)
    {
        foreach (var mod in mods) ModSources.Apply(mod, catalog);
        BatchResults(repo, new SourceIdentificationBatch(mods, SourceIdentificationBatch.CaptureBefore(mods), catalog, ModSources.Context), refresh, status);
    }
    internal static void BatchResults(OverviewRepository repo, SourceIdentificationBatch batch, Action refresh, Action<string> status)
    {
        if (batch.Environment != ModSources.Context) return;
        var catalog = batch.Catalog;
        var panel = OverviewUi.CreateModalPanel("MCE_SourceBatchResults", 980, 680); if (panel == null) return;
        var title = OverviewUi.Text(panel, Loc.T("Source lookup results", "批量来源查找结果", "批次來源查找結果"), 24); OverviewUi.PlaceClipped(title, 20, 12, panel.rect.width - 40, 36);
        string summary = batch.Summary;
        var result = OverviewUi.Text(panel, summary, 16); OverviewUi.PlaceClipped(result, 20, 54, panel.rect.width - 40, 30); status(summary);
        var content = Rows(panel, 94, panel.rect.height - 156, out _);
        var rows = batch.Rows.Where(r => r.Kind != ModSourceKind.Local).OrderBy(r => r.Kind != ModSourceKind.Pending).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < rows.Count; i++)
        {
            var entry = rows[i]; var mod = entry.Mod; float y = i * 90, width = panel.rect.width - 40;
            var name = OverviewUi.Text(content, entry.Name, 18); OverviewUi.PlaceClipped(name, 8, y, width - 286, 28);
            bool resolved = entry.Kind == ModSourceKind.Store;
            string detail = resolved ? entry.Namespace + " / " + entry.Package + (entry.Deprecated ? Loc.T(" · Deprecated; source identified", " · 已弃用，来源已确认", " · 已棄用，來源已確認") : "") : entry.Reason;
            var metadata = OverviewUi.Text(content, entry.Original + " · " + entry.Version + " · " + entry.Author, 14); OverviewUi.PlaceClipped(metadata, 8, y + 28, width - 286, 24);
            var description = OverviewUi.Text(content, detail, 14); description.color = OverviewUi.Muted; OverviewUi.PlaceClipped(description, 8, y + 53, width - 286, 26);
            FolderButton(content, mod, width - 264, y + 10, status, repo);
            void Return() => BatchResults(repo, batch.AfterManualAction(), refresh, status);
            var inspect = OverviewUi.Button(content, resolved ? Loc.T("Store", "商店页", "商店頁") : Loc.T("Identify manually", "手动识别", "手動識別"), () => { if (resolved) Application.OpenURL(ThunderstoreUpdateService.HomeUrl(entry.Namespace, entry.Package)); else Search(repo, mod, refresh, status, query: ModSources.SearchName(mod), returnTo: Return); }, 100, 34, role: resolved ? UiActionRole.Neutral : UiActionRole.Edit);
            // 2.3.9: both buttons fit their text (English "Identify manually" overflowed into "Mark as local").
            float inspectWidth = Mathf.Max(100, inspect.GetComponentInChildren<TMP_Text>(true).GetPreferredValues(inspect.GetComponentInChildren<TMP_Text>(true).text).x + 22);
            var measureCaption = inspect.GetComponentInChildren<TMP_Text>(true);
            float localWidth = resolved ? 0 : Mathf.Max(108, measureCaption.GetPreferredValues(Loc.T("Mark as local", "标记为本地", "標記為本機")).x + 22);
            OverviewUi.Place((RectTransform)inspect.transform, width - 4 - localWidth - (resolved ? 0 : 8) - inspectWidth, y + 8, inspectWidth, 34);
            if (!resolved)
            {
                var local = OverviewUi.Button(content, Loc.T("Mark as local", "标记为本地", "標記為本機"), () => { ModSources.Set(new[] { mod }, new ModSourceRecord { kind = ModSourceKind.Local, ns = mod.SourceNamespace, name = mod.SourceName, author = mod.Author, homepage = mod.HomepageUrl }); refresh(); Return(); }, 108, 34, role: UiActionRole.Edit); OverviewUi.Place((RectTransform)local.transform, width - 4 - localWidth, y + 8, localWidth, 34);
            }
        }
        content.sizeDelta = new Vector2(0, rows.Count * 90);
        var close = OverviewUi.Button(panel, Loc.T("Close", "关闭", "關閉"), OverviewUi.CloseModal, 100, 34, role: UiActionRole.Neutral); OverviewUi.Bottom((RectTransform)close.transform, 20, 16);
        PublicationNavigation.Attach(panel);
    }
    private static string VersionText(OverviewMod mod)
    {
        string version = Loc.T("DLL version: ", "DLL 版本：", "DLL 版本：") + (string.IsNullOrWhiteSpace(mod.PluginVersion) ? Loc.T("Unknown", "未知", "未知") : mod.PluginVersion);
        if (!string.IsNullOrWhiteSpace(mod.PackageVersion)) version += Loc.T(" · Package version: ", " · 包版本：", " · 套件版本：") + mod.PackageVersion;
        return version;
    }
    private static string AuthorText(OverviewMod mod) => Loc.T("Author/team: ", "作者／团队：", "作者／團隊：") + (string.IsNullOrWhiteSpace(mod.Author) ? Loc.T("Not declared", "未声明", "未宣告") : mod.Author);
    private static void FolderButton(Transform parent, OverviewMod mod, float x, float y, Action<string> status, OverviewRepository repo)
    {
        string? directory = OverviewListView.ModFolder(mod);
        Button? folder = null;
        folder = OverviewRowActions.Folder(parent, x, y, () => {
            if (folder != null) FolderShortcuts.Open(mod, (RectTransform)folder.transform, status, repo);
        }, directory != null, directory ?? "", (_, _) => { });
    }
    internal static RectTransform Rows(RectTransform panel, float top, float height, out ScrollRect scroll)
    {
        PublicationNavigation.Attach(panel);
        var host = OverviewUi.Rect("Scroll", panel); OverviewUi.Place(host, 20, top, panel.rect.width - 40, height);
        host.gameObject.AddComponent<RectMask2D>(); var background = host.gameObject.AddComponent<Image>(); background.color = ModConfigLook.Surface(new Color(0, 0, 0, .08f));
        scroll = host.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        var content = OverviewUi.Rect("Rows", host); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1); content.sizeDelta = Vector2.zero;
        scroll.viewport = host; scroll.content = content; return content;
    }
}
internal sealed class SourceSearchLifetime : MonoBehaviour
{
    internal CancellationTokenSource? Cancellation;
    private void OnDestroy() { var cancellation = Cancellation; Cancellation = null; cancellation?.Cancel(); cancellation?.Dispose(); }
}
internal sealed class RoundedChip : MaskableGraphic
{
    internal Color Ink;
    public override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); Rect r = rectTransform.rect; float radius = Mathf.Min(2, Mathf.Min(r.width, r.height) * .5f);
        mesh.AddVert(r.center, color, Vector2.zero); int count = 0;
        for (int corner = 0; corner < 4; corner++)
        for (int step = 0; step <= 8; step++)
        {
            float angle = (corner * 90 + step * 90f / 8) * Mathf.Deg2Rad;
            Vector2 center = new(corner == 0 || corner == 3 ? r.xMax - radius : r.xMin + radius, corner < 2 ? r.yMax - radius : r.yMin + radius);
            mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero); count++;
        }
        for (int i = 1; i <= count; i++) mesh.AddTriangle(0, i, i == count ? 1 : i + 1);
    }
    internal static RectTransform Add(Transform parent, ModSourceKind kind, float x, float y)
    {
        var root = OverviewUi.Rect("SourceChip", parent); OverviewUi.Place(root, x, y, 48, 23);
        var shape = root.gameObject.AddComponent<RoundedChip>(); shape.raycastTarget = false;
        shape.color = kind == ModSourceKind.Store ? new Color(.31f, .40f, .50f) : kind == ModSourceKind.Local ? new Color(.31f, .45f, .43f) : new Color(.43f, .41f, .37f);
        shape.Ink = new Color(.90f, .88f, .81f);
        var text = OverviewUi.Text(root, kind == ModSourceKind.Store ? Loc.T("Store", "商店", "商店") : kind == ModSourceKind.Local ? Loc.T("Local", "本地", "本機") : Loc.T("Pending", "待定", "待定"), 14);
        OverviewUi.ReadableName(text, 200); text.richText = false; text.color = shape.Ink; text.alignment = TextAlignmentOptions.Midline;
        text.enableAutoSizing = false; text.margin = Vector4.zero; OverviewUi.Fill(text.rectTransform); text.ForceMeshUpdate(true, true);
        Vector3 bounds = text.textBounds.size;
        float width = Mathf.Ceil(Mathf.Max(1, bounds.x)) + 6, height = Mathf.Ceil(Mathf.Max(10, bounds.y)) + 4;
        OverviewUi.Place(root, x, y + (23 - height) * .5f, width, height);
        return root;
    }
}
