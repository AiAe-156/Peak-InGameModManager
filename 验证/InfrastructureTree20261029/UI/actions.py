from pathlib import Path
root=Path.cwd()
def edit(f,a,b):
 p=root/f;s=p.read_text(encoding='utf-8-sig');assert s.count(a)==1,(f,a[:60],s.count(a));p.write_text(s.replace(a,b),encoding='utf-8')
edit('src/Overview/SourceUi.cs','internal static class SourceUi\n{','''internal static class SourceUi
{
    private static ModConfigEnhance.Updating.ComponentDependencyGraph? _dependencyGraph;
    private static string _dependencyContext = "";
    private static int _dependencyRevision = -1;
    internal static string DependencyContext => System.IO.Path.GetFullPath(PackLibrary.Managed ? PackLibrary.Workspace : BepInEx.Paths.BepInExRootPath);
    internal static void PublishDependencyGraph(ModConfigEnhance.Updating.ComponentDependencyGraph graph, string context, int revision)
    { _dependencyGraph = graph; _dependencyContext = context; _dependencyRevision = revision; }
    internal static string UninstallReason(string key)
        => _dependencyGraph == null || _dependencyContext != DependencyContext || _dependencyRevision != OverviewPageCache.Inventory
            ? Loc.T("Dependency protection is being read or is unavailable; uninstall is disabled.", "依赖保护正在读取或不可用，暂不能卸载。", "依賴保護正在讀取或不可用，暫不能解除安裝。")
            : _dependencyGraph.GetCapability(key).Reason;
    internal static void ComponentMenu(OverviewRepository repo, IReadOnlyList<OverviewMod> mods, InfrastructureOverviewModel package)
    {
        string reason = UninstallReason(package.Key);
        var options = new List<(string, string)> {
            (Loc.T("View in mod store / version operations", "内置商店查看／版本操作", "內建商店查看／版本操作"), "store"),
            (Loc.T("Open store in browser", "浏览器打开商店页", "瀏覽器開啟商店頁"), "browser"),
            (Loc.T("Open folder", "打开文件夹", "開啟資料夾"), "folder"),
            (reason.Length == 0 ? Loc.T("Uninstall… (safe preview)", "卸载……（安全预览）", "解除安裝……（安全預覽）")
                : Loc.T("Uninstall unavailable: ", "无法卸载：", "無法解除安裝：") + reason, "uninstall") };
        OverviewUi.Choose(package.Name, options, "", action => {
            if (action == "folder") { OverviewListView.OpenFolder(package.Path, _ => { }); return; }
            if (action == "browser") { int split = package.Key.IndexOf('-'); Application.OpenURL(ThunderstoreUpdateService.HomeUrl(package.Key.Substring(0, split), package.Key.Substring(split + 1))); return; }
            if (action == "uninstall") {
                string current = UninstallReason(package.Key);
                if (current.Length > 0) { OverviewUi.ShowText(Loc.T("Uninstall unavailable", "无法卸载", "無法解除安裝"), current); return; }
                // Core must use the store's staged official-old-ZIP consent flow, not the legacy auto-confirm dialog.
                ShopWindow.Open(repo, mods, false, package.Key, true); return;
            }
            ShopWindow.Open(repo, mods, false, package.Key);
        }, action => action == "uninstall" ? (reason.Length == 0 ? UiActionRole.Danger : UiActionRole.Neutral) : UiActionRole.Neutral);
    }
''')
edit('src/Overview/SourceUi.cs','options.Add((Loc.T("Uninstall mod...","卸载模组……","解除安裝模組……"),"uninstall"));','''string reason = UninstallReason(mod.SourceKey);
        options.Add((reason.Length == 0 ? Loc.T("Uninstall mod...","卸载模组……","解除安裝模組……")
            : Loc.T("Uninstall unavailable: ", "无法卸载：", "無法解除安裝：") + reason,"uninstall"));''')
edit('src/Overview/SourceUi.cs','            if(action=="uninstall"){','''            if(action=="uninstall"){
                string reason=UninstallReason(mod.SourceKey);
                if(reason.Length>0){OverviewUi.ShowText(Loc.T("Uninstall unavailable","无法卸载","無法解除安裝"),reason);return;}''')
edit('src/Overview/ShopInstallationState.cs','    internal string UninstallBlockReason(string key)=>','    internal Func<string,string>? DependencyUninstallReason { get; set; }\n    internal string UninstallBlockReason(string key)=>DependencyUninstallReason?.Invoke(key) is string protection && protection.Length>0 ? protection : ')
edit('src/Overview/ShopWindow.cs','private Task<ShopInstalled.Snapshot>? _inventory;','private Task<(ShopInstalled.Snapshot Snapshot, ComponentDependencyGraph Graph)>? _inventory;\n    private int _pendingRevision;\n    private string _pendingDependencyContext="";\n    private bool _openUninstall;')
edit('src/Overview/ShopWindow.cs','        _inventory=Task.Run(()=>ShopInstalled.Build(root,context,context,confirmed));','''        _pendingRevision=OverviewPageCache.Inventory;_pendingDependencyContext=SourceUi.DependencyContext;
        var graphconfirmed=confirmed.Select(c=>new ComponentDependencyCollector.ConfirmedFile{PackageKey=c.Key,Path=c.Path}).ToArray();
        _inventory=Task.Run(()=>(ShopInstalled.Build(root,context,context,confirmed), InfrastructureDependencyCollector.Collect(root,graphconfirmed)));''')
edit('src/Overview/ShopWindow.cs','string reason=_installationState.BlockReason(p.Key);','string reason=_installationState.UninstallBlockReason(p.Key);')
edit('src/Overview/ShopWindow.cs','bool showTask=false,string packageKey="")','bool showTask=false,string packageKey="",bool uninstall=false)')
edit('src/Overview/ShopWindow.cs','view._openPackage=packageKey;view.Build();','view._openPackage=packageKey;view._openUninstall=uninstall;view._installationState.DependencyUninstallReason=SourceUi.UninstallReason;view.Build();')
edit('src/Overview/ShopWindow.cs','_inventory.IsCompletedSuccessfully?_inventory.Result:null,','_inventory.IsCompletedSuccessfully?_inventory.Result.Snapshot:null,')
edit('src/Overview/ShopWindow.cs','            _inventory=null;\n            if(changed)InstallationChanged();','''            if(_inventory.IsCompletedSuccessfully&&_installationState.Current(_pendingGeneration,_pendingContext)
                &&_pendingDependencyContext==SourceUi.DependencyContext&&_pendingRevision==OverviewPageCache.Inventory)
                SourceUi.PublishDependencyGraph(_inventory.Result.Graph,_pendingDependencyContext,_pendingRevision);
            _inventory=null;
            if(changed)InstallationChanged();''')
edit('src/Overview/ShopWindow.cs','        if(_sentAt>0&&Time.unscaledTime-_sentAt>1)','''        if(_openUninstall&&_openPackage.Length==0&&_selected!=null&&_installationState.Status==ShopInstallationState.Phase.Ready){
            _openUninstall=false;string reason=_installationState.UninstallBlockReason(_selected.Key);
            if(reason.Length>0)_status.text=Loc.LocalizeMessage(reason);else Preview(true);
        }
        if(_sentAt>0&&Time.unscaledTime-_sentAt>1)''')
edit('src/Overview/ShopWindow.cs','if((_requestedTask||_showingTask)&&!task.Request.Operation.Contains("uninstall")&&','if((_requestedTask||_showingTask)&&')
