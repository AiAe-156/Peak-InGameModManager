#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ModConfigEnhance.Updating;
using ModConfigEnhance.Overview.Editor;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;

internal sealed class ShopWindow : MonoBehaviour
{
    private OverviewRepository _repo=null!;
    private IReadOnlyList<OverviewMod> _installed=Array.Empty<OverviewMod>();
    private RectTransform _panel=null!,_body=null!;
    private TextMeshProUGUI _status=null!;
    private TMP_InputField _search=null!;
    private RectTransform _networkAnchor=null!;
    private NetworkUiSubmission _networkSubmission=null!;
    private List<ShopListing> _catalog=new();
    private Task<List<ShopListing>>? _loading;
    private Task<string>? _markdown;
    private IntroductionEditorView? _reader;
    private ShopMarkdownSource? _document;
    private ShopListing? _selected;
    private string _tab="intro",_version="",_query="",_category="",_sort="updated",_filter="all",_markdownKey="",_group="",_newGroup="",_groupColor="#7CC7E6",_taskKey="";
    private readonly HashSet<string> _favorites=new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cancel=new();
    private int _page;
    private bool _deprecated,_nsfw;
    private string _markdownDrawn="";
    private float _poll, _searchAt;
    private bool _searchPending, _showingTask, _requestedTask, _submissionPending;
    private string _groupTaskId="", _uninstallConfirmedId="";
    private readonly HashSet<string> _dependencyExpanded=new(StringComparer.OrdinalIgnoreCase);
    private readonly ShopInstallationState _installationState=new();
    private ShopInstalled.Snapshot _installation=>_installationState.Snapshot;
    private TextMeshProUGUI? _localInstallationText;
    private Button? _installButton,_uninstallButton;
    private Button _inventoryRetry=null!;
    private Task<(ShopInstalled.Snapshot Snapshot, ComponentDependencyGraph Graph)>? _inventory;
    private int _pendingRevision;
    private string _pendingDependencyContext="";
    private bool _openUninstall;
    private string _uninstallPackage="";
    private string _inventoryContext="";
    private float _inventoryAt;
    private int _pendingGeneration;
    private string _pendingContext="";
    private void ReloadInstalledRows()
    {
        ModSources.InvalidateRecords();
        var catalogue=new OverviewCatalogue();
        _installed=catalogue.Scan(_repo).Concat(catalogue.PublicationCache(_repo)).ToArray();
    }
    private string InventoryContext=>Path.GetFullPath(PackLibrary.Managed?Path.Combine(PackLibrary.Workspace,"shop-installed.json"):Path.Combine(Home,"installed-unmanaged.json"));
    private void RefreshInstallation()
    {
        var packIndex=PackLibrary.Read();var active=packIndex.Packs.FirstOrDefault(p=>p.Id==packIndex.ActivePackId);
        string context=InventoryContext;
        if(context!=_inventoryContext){_inventoryContext=context;_installationState.Reset(context);}
        _installationState.CompleteCurrentPack=packIndex.SchemaVersion==2&&packIndex.MinimumWorkerProtocol==2&&active!=null&&PackEnvironmentPolicy.IsComplete(active)&&active.RevisionStateHash.Length>0;
        if(_inventory!=null)return;
        var confirmed=_installed.Where(m=>m.SourceConfirmed&&m.IdentityConflict.Length==0).SelectMany(m=>(m.AssemblyPaths.Count>0?m.AssemblyPaths:new List<string>{m.Location}).Select(path=>new ShopInstalled.Evidence{Key=m.SourceKey,Version=m.PackageVersion.Length>0?m.PackageVersion:m.PluginVersion,Path=path})).ToArray();
        string root=BepInEx.Paths.BepInExRootPath;
        _pendingGeneration=_installationState.Generation;_pendingContext=context;
        _pendingRevision=OverviewPageCache.Inventory;_pendingDependencyContext=SourceUi.DependencyContext;
        var graphconfirmed=confirmed.Select(c=>new ComponentDependencyCollector.ConfirmedFile{PackageKey=c.Key,Path=c.Path}).ToArray();
        _inventory=Task.Run(()=>(ShopInstalled.Build(root,context,context,confirmed), InfrastructureDependencyCollector.Collect(root,graphconfirmed)));
        _inventoryAt=Time.unscaledTime+5;
    }
    private string InstallationText(ShopListing p)
    {
        if(_installationState.Status!=ShopInstallationState.Phase.Ready)return InventoryMessage;
        if(!_installation.Packages.TryGetValue(p.Key,out var rows))return T("Not installed","未安装");
        return T("Installed · Local ","已安装 · 本地 ")+_installation.Version(p.Key)+T(" · Store "," · 商店 ")+p.Version+"\n"+string.Join("\n",rows.Select(r=>(r.Cached?T("Cached · ","缓存停用 · "):r.Disabled?T("Disabled · ","已禁用 · "):"")+r.Path).Distinct());
    }
    private string InventoryMessage=>_installationState.Status==ShopInstallationState.Phase.Failed
        ?T("Installation status read failed; retry: ","安装状态读取失败，请重试：")+_installationState.Error
        :T("Reading installation status...","正在读取安装状态……");
    private void RetryInstallation()
    {
        _installationState.Retry();_inventoryAt=0;InstallationChanged();
    }
    private void InstallationChanged()
    {
        _inventoryRetry.gameObject.SetActive(_installationState.Status==ShopInstallationState.Phase.Failed);
        if(_showingTask)return;
        if(_selected!=null){UpdateInstallationDetails();return;}
        var offsets=_body.GetComponentsInChildren<ScrollRect>().Select(v=>v.content.anchoredPosition).ToArray();
        Draw();var scrolls=_body.GetComponentsInChildren<ScrollRect>();
        Canvas.ForceUpdateCanvases();
        for(int i=0;i<Math.Min(offsets.Length,scrolls.Length);i++)scrolls[i].content.anchoredPosition=offsets[i];
        _status.text=_installationState.Status!=ShopInstallationState.Phase.Ready?InventoryMessage:_catalog.Count+T(" packages"," 个包");
    }
    private string SelectedActionText(ShopListing package)
    {
        if(!_installationState.CanInstall(package.Key))return T("Operation unavailable","操作不可用");
        var action=ComponentActions.Select(_installation.Version(package.Key),_version);
        return action==ComponentAction.Install?T("Install ","安装 ")+_version:
            action==ComponentAction.Upgrade?T("Upgrade to ","升级到该版本 ")+_version:
            action==ComponentAction.Downgrade?T("Downgrade to ","降级到该版本 ")+_version:T("Reinstall version: ","重装版本：")+_version;
    }
    private void UpdateInstallationDetails()
    {
        var p=_selected;if(p==null)return;
        _status.text=InstallationText(p).Split('\n')[0];
        bool core=_installation.Packages.TryGetValue(p.Key,out var rows)&&rows.Any(r=>r.Area=="core");
        if(_installButton!=null){
            _installButton.interactable=_installationState.CanInstall(p.Key);
            _installButton.GetComponentInChildren<TextMeshProUGUI>().text=SelectedActionText(p);
        }
        if(_uninstallButton!=null)_uninstallButton.interactable=_installationState.CanUninstall(p.Key,ReadLedger().Packages.Any(x=>x.Key==p.Key));
        if(_localInstallationText!=null){
            string text=InstallationText(p);string reason=_installationState.UninstallBlockReason(p.Key);if(reason.Length>0)text+="\n"+Loc.LocalizeMessage(reason);else if(Installed(p)&&!_installationState.CanUninstall(p.Key,ReadLedger().Packages.Any(x=>x.Key==p.Key)))text+="\n"+T("Uninstall unavailable: independent package ownership could not be verified.","无法卸载：尚未核实独立包文件归属。");else if(Installed(p))text+="\n"+T("Uninstall first previews exact ownership and hard dependencies; nothing is removed before confirmation.","卸载将先核对精确文件归属和硬依赖，确认前不删除文件。");_localInstallationText.text=text;
            _localInstallationText.rectTransform.sizeDelta=new Vector2(_localInstallationText.rectTransform.sizeDelta.x,Math.Max(120,text.Split('\n').Length*30));
            var parent=(RectTransform)_localInstallationText.transform.parent;
            parent.sizeDelta=new Vector2(parent.sizeDelta.x,Math.Max(_body.rect.height-156,_localInstallationText.rectTransform.sizeDelta.y+20));
        }
    }
    private ShopTask? _task;
    private static OverviewRepository? _lastRepo;
    private static IReadOnlyList<OverviewMod> _lastInstalled=Array.Empty<OverviewMod>();
    internal static void OpenTasks(){var repo=_lastRepo??new OverviewRepository();Open(repo,_lastInstalled,true);}
    internal static void OpenPackage(OverviewRepository repo,OverviewMod mod){Open(repo,new[]{mod},false,mod.SourceNamespace+"-"+mod.SourceName);}
    private string _openPackage="";
    private float _sentAt;
    private string _sentTaskKey="";
    private string Home=>ModConfigEnhance.Updating.ManagerIdentity.Runtime(BepInEx.Paths.BepInExRootPath, "shop");
    private string FavoritePath=>Path.Combine(Loc.RuntimeDirectory,"shop-favorites.json");
    internal static void Open(OverviewRepository repo,IReadOnlyList<OverviewMod> installed,bool showTask=false,string packageKey="",bool uninstall=false)
    {
        _lastRepo=repo;_lastInstalled=installed.ToArray();
        var panel=OverviewUi.CreateModalPanel("MCE_ModStore",1500,940);if(panel==null)return;
        var view=panel.gameObject.AddComponent<ShopWindow>();view._repo=repo;view._installed=installed.ToArray();view._panel=panel;view._requestedTask=showTask;view._openPackage=packageKey;view._openUninstall=uninstall;view._uninstallPackage=uninstall?packageKey:"";view._installationState.DependencyUninstallReason=SourceUi.UninstallReason;view.Build();
    }
    private string T(string en,string zh)=>Loc.T(en,zh,zh);
    private Button Button(Transform host,string text,Action action,float x,float y,float width=120,UiActionRole role=UiActionRole.Neutral)
    {var b=OverviewUi.Button(host,text,()=>{try{action();}catch(Exception e){_status.text=e.Message;}},width,32,role);OverviewUi.Place((RectTransform)b.transform,x,y,width,32);return b;}
    private void Build()
    {
        float w=_panel.rect.width,h=_panel.rect.height;
        OverviewUi.Place(OverviewUi.Text(_panel,T("Mod store","模组商店"),25).rectTransform,164,12,w-650,34);
        Button(_panel,T("Close","关闭"),OverviewUi.RequestCloseModal,w-130,12,108);
        _networkSubmission=_panel.gameObject.AddComponent<NetworkUiSubmission>();
        _networkAnchor=(RectTransform)Button(_panel,T("Network / proxy","网络／代理"),Network,w-450,12,150,UiActionRole.Edit).transform;
        Button(_panel,T("Tasks","下载任务"),ShowTask,w-286,12,140);
        _search=OverviewUi.Input(_panel,"");OverviewUi.Place((RectTransform)_search.transform,20,58,w-570,36);_search.textComponent.fontSize=20;_search.textComponent.margin=Vector4.zero;_search.textComponent.alignment=TextAlignmentOptions.MidlineLeft;
        _search.onValueChanged.AddListener(_=>{_searchAt=Time.unscaledTime+.18f;_searchPending=true;});
        _search.onSubmit.AddListener(_=>{_query=_search.text;_page=0;_selected=null;Draw();});
        Button(_panel,T("Search","搜索"),()=>{_query=_search.text;_page=0;_selected=null;Draw();},w-536,60,108,UiActionRole.Edit);
        Button? sortButton=null;
        sortButton=Button(_panel,T("Sort","排序"),()=>Options(T("Sort","排序"),new[]{(Loc.T("Last updated","最近更新","最近更新"),"updated"),(Loc.T("Downloads","下载量","下載量"),"downloads"),(Loc.T("Name","名称","名稱"),"name")},v=>{_sort=v;Draw();},(RectTransform)sortButton!.transform),w-418,60,118);
        Button(_panel,T("Refresh","刷新目录"),()=>{ReloadInstalledRows();RetryInstallation();_loading=ShopCatalog.Load(_cancel.Token,true);_status.text=T("Reading store...","正在读取商店……");},w-290,60,130);
        Button(_panel,T("Back","返回上一级"),()=>{_showingTask=false;if(_selected!=null)_selected=null;_tab="intro";_requestedTask=false;Draw();},20,12,128);
        _inventoryRetry=Button(_panel,T("Retry installation status","重试安装状态"),RetryInstallation,w-210,h-48,190);_inventoryRetry.gameObject.SetActive(false);
        _body=OverviewUi.Rect("ShopBody",_panel);OverviewUi.Place(_body,20,108,w-40,h-170);
        _status=OverviewUi.Text(_panel,T("Reading store...","正在读取商店……"),14);OverviewUi.Place(_status.rectTransform,20,h-48,w-250,38);_status.textWrappingMode=TextWrappingModes.Normal;
        try{if(File.Exists(FavoritePath))foreach(var key in JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(FavoritePath))??new())_favorites.Add(key);}catch(IOException){}
        _catalog=ShopCatalog.Cached();
        // OpenPackage may supply only one row. Inventory needs the complete confirmed overview,
        // including the user-designated disabled cache, not only this session's loaded plugins.
        ReloadInstalledRows();
        RefreshInstallation();
        _loading=ShopCatalog.Load(_cancel.Token);Draw();_status.text=InventoryMessage;
    }
    private void Options(string title,IReadOnlyList<(string,string)> options,Action<string> pick,RectTransform? anchor=null)
    {
        WindowPopover.Menu(_panel,anchor??_networkAnchor,title,options,"",pick);
    }
    private RectTransform Scroll(Transform host,float x,float y,float width,float height)
    {
        var viewport=OverviewUi.Rect("Viewport",host);OverviewUi.Place(viewport,x,y,width,height);viewport.gameObject.AddComponent<RectMask2D>();viewport.gameObject.AddComponent<Image>().color=ModConfigLook.Surface(new Color(.075f,.09f,.095f,.95f));
        var content=OverviewUi.Rect("Rows",viewport);OverviewUi.Place(content,0,0,width-18,1);
        var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.inertia=false;scroll.scrollSensitivity=90;scroll.movementType=ScrollRect.MovementType.Clamped;ManagementUi.Scrollbar(viewport,scroll);return content;
    }
    private void ClearBody(){_localInstallationText=null;_installButton=null;_uninstallButton=null;_reader?.Dispose();_reader=null;_document=null;foreach(Transform child in _body){child.gameObject.SetActive(false);Destroy(child.gameObject);}}
    private bool Installed(ShopListing p)=>_installation.Has(p.Key);
    private ShopLedger ReadLedger(){string file=PackLibrary.Managed?Path.Combine(PackLibrary.Workspace,"shop-installed.json"):Path.Combine(Home,"installed-unmanaged.json");try{return File.Exists(file)?JsonConvert.DeserializeObject<ShopLedger>(File.ReadAllText(file))??new():new();}catch(IOException){return new();}}
    private void Draw()
    {
        ClearBody();if(_selected!=null){Details();return;}
        float w=_body.rect.width,h=_body.rect.height,left=190;
        var filters=Scroll(_body,0,0,left,h);float y=8;
        Button Filter(string label,Action action){var button=Button(filters,label,action,8,y,left-36,UiActionRole.Edit);y+=38;return button;}
        Filter(Loc.T("Core / preloader","核心／启动补丁更新","核心／啟動修補更新"),()=>{
            var components=_installation.Packages.Values.SelectMany(r=>r).Where(r=>r.Area is "core" or "patchers").ToList();var listing=_catalog.Where(p=>components.Any(c=>c.Key==p.Key));
            string text=string.Join("\n\n",listing.Select(p=>{var local=components.First(c=>c.Key==p.Key);return p.Key+"\n"+local.Version+" → "+p.Version+"\n"+local.Path;}));
            var popup=OverviewUi.Panel("InfrastructureVersions",_panel,ModConfigLook.Surface(new Color(.10f,.14f,.15f,1)));OverviewUi.Place(popup,30,150,Math.Min(1000,_panel.rect.width-60),Math.Min(500,_panel.rect.height-200));var field=OverviewUi.Input(popup,_installationState.Status!=ShopInstallationState.Phase.Ready?InventoryMessage:text.Length>0?text:T("No recognized package manifests.","未识别到组件包清单。"),true);field.readOnly=true;field.textComponent.fontSize=17;OverviewUi.Place((RectTransform)field.transform,12,12,popup.rect.width-24,popup.rect.height-64);Button(popup,T("Close","关闭"),()=>Destroy(popup.gameObject),12,popup.rect.height-42,108);
        });
        Filter(T("All mods","全部模组"),()=>{_filter="all";_category="";_page=0;Draw();});
        Filter(T("Installed","已安装"),()=>{_filter="installed";_page=0;Draw();});Filter(T("Favorites","收藏"),()=>{_filter="favorites";_page=0;Draw();});
        Filter(T("Updates available","有更新"),()=>{_filter="updates";_page=0;Draw();});
        Button? categoryButton=null;
        categoryButton=Filter(T("Categories","分类标签"),()=>Options(T("Category","分类"),new[]{(T("All","全部"),"")}.Concat(_catalog.SelectMany(p=>p.Categories).Distinct().OrderBy(s=>s).Select(s=>(s,s))).ToList(),v=>{_category=v;_page=0;Draw();},(RectTransform)categoryButton!.transform));
        Filter(T("Deprecated: ","弃用：")+(_deprecated?"on":"off"),()=>{_deprecated=!_deprecated;Draw();});Filter("NSFW: "+(_nsfw?"on":"off"),()=>{_nsfw=!_nsfw;Draw();});filters.sizeDelta=new Vector2(left-18,y+8);
        var matches=_catalog.Where(p=>(_deprecated||!p.Deprecated)&&(_nsfw||!p.Nsfw)&&(_category.Length==0||p.Categories.Contains(_category))&&(_filter!="installed"||Installed(p))&&(_filter!="favorites"||_favorites.Contains(p.Key))&&(_filter!="updates"||_installation.Updates(p.Key,p.Version))&&ShopSearch.Matches(p.Key,p.Description,_query));
        matches=_sort=="downloads"?matches.OrderByDescending(p=>p.Downloads):_sort=="name"?matches.OrderBy(p=>p.Name):matches.OrderByDescending(p=>p.Updated);
        var all=matches.ToList();const int pageSize=36;_page=Mathf.Clamp(_page,0,Math.Max(0,(all.Count-1)/pageSize));
        float area=w-left-14;int columns=Math.Max(1,(int)(area/250));float cardWidth=(area-(columns-1)*10)/columns,cardHeight=216;
        var cards=Scroll(_body,left+14,0,area,h-44);int n=0;
        if(all.Count==0)Label(cards,_catalog.Count==0?T("Loading the catalog. Cached results will appear here.","正在读取目录，有缓存时会先显示缓存。"):T("No matches. Try fewer keywords or clear filters.","没有匹配项，请缩短关键词或清空筛选。"),12,14,area-40,50,18);
        foreach(var p in all.Skip(_page*pageSize).Take(pageSize))
        {
            var item=p;float x=(n%columns)*(cardWidth+10),top=(n/columns)*(cardHeight+10);var row=FlatListRow.Create(cards,()=>{_selected=item;_version=item.Version;_tab="intro";_markdownKey="";Draw();},cardWidth,cardHeight,ModConfigLook.Surface(new Color(.12f,.16f,.17f,1)));OverviewUi.Place((RectTransform)row.transform,x,top,cardWidth,cardHeight);
            var icon=OverviewUi.Rect("Icon",row.transform);OverviewUi.Place(icon,10,8,48,48);icon.gameObject.AddComponent<RawImage>().raycastTarget=false;icon.gameObject.AddComponent<ShopIcon>().Load(item.Icon);
            Label(row.transform,item.Name,66,8,cardWidth-76,28,19);Label(row.transform,item.Team+T(" · Store "," · 商店 ")+item.Version,66,38,cardWidth-76,24,14,OverviewUi.Blue);
            var desc=Label(row.transform,item.Description,10,66,cardWidth-20,52,15);desc.textWrappingMode=TextWrappingModes.Normal;desc.overflowMode=TextOverflowModes.Ellipsis;
            Label(row.transform,string.Join(" · ",item.Categories.Take(3)),10,124,cardWidth-20,22,12,OverviewUi.Muted);
            if(_installationState.Status!=ShopInstallationState.Phase.Ready)Label(row.transform,_installationState.Status==ShopInstallationState.Phase.Failed?T("Installation status failed — retry","安装状态读取失败，请重试"):T("Reading installation status...","正在读取安装状态……"),10,148,cardWidth-20,44,13,OverviewUi.Muted);
            else if(Installed(item)){
                Label(row.transform,T("Installed","已安装"),10,148,72,24,14,new Color(.40f,.88f,.52f,1));
                Label(row.transform,T("Local version: ","本地版本：")+_installation.Version(item.Key),86,148,cardWidth-96,24,13,OverviewUi.Muted);
                var states=_installation.Packages[item.Key];
                string state=states.All(r=>r.Cached)?T("Cached / disabled","缓存停用"):states.All(r=>r.Disabled)?T("Disabled","已禁用"):"";
                string update=_installation.Updates(item.Key,item.Version)?T("Update available","有更新"):"";
                Label(row.transform,string.Join(" · ",new[]{update,state}.Where(v=>v.Length>0)),10,174,cardWidth-20,20,13,OverviewUi.Blue);
            }
            else Label(row.transform,item.Downloads+" ↓ · "+UpdatedAgo(item.Updated),10,153,cardWidth-20,24,13);
            n++;
        }
        cards.sizeDelta=new Vector2(area-18,Math.Max(h-44,((n+columns-1)/columns)*(cardHeight+10)));
        Button(_body,T("Previous","上一页"),()=>{_page--;Draw();},left+14,h-36,110);Button(_body,T("Next","下一页"),()=>{_page++;Draw();},left+134,h-36,110);
        Label(_body,(_page+1)+" / "+Math.Max(1,(all.Count+pageSize-1)/pageSize)+" · "+all.Count+T(" results"," 项"),left+260,h-34,300,30,15);
    }
    private string UpdatedAgo(DateTime value)
    {var age=DateTime.Now-value.ToLocalTime();return age.TotalDays>=1?((int)age.TotalDays)+T(" days ago","天前更新"):age.TotalHours>=1?((int)age.TotalHours)+T(" hours ago","小时前更新"):Math.Max(0,(int)age.TotalMinutes)+T(" minutes ago","分钟前更新");}
    private TextMeshProUGUI Label(Transform host,string text,float x,float y,float width,float height,float size,Color? color=null)
    {var label=OverviewUi.Text(host,text,size);if(color.HasValue)label.color=color.Value;OverviewUi.PlaceClipped(label,x,y,width,height);return label;}
    private void Details()
    {
        var p=_selected!;float w=_body.rect.width,h=_body.rect.height;
        var title=Label(_body,p.Name+" <size=65%><color=#8C918E>("+p.Team+" · "+p.Key+" · "+_version+")</color></size>",4,0,w-180,36,24);title.richText=true;
        var existing=Installed(p)?_installed.FirstOrDefault(m=>m.SourceConfirmed&&m.SourceKey.Equals(p.Key,StringComparison.OrdinalIgnoreCase)&&File.Exists(m.Location)):null;
        bool core=_installation.Packages.TryGetValue(p.Key,out var evidence)&&evidence.Any(e=>e.Area=="core");
        _status.text=InstallationText(p).Split('\n')[0];
        Button(_body,_favorites.Contains(p.Key)?T("Unfavorite","取消收藏"):T("Favorite","收藏"),()=>{if(!_favorites.Add(p.Key))_favorites.Remove(p.Key);Directory.CreateDirectory(Path.GetDirectoryName(FavoritePath)!);File.WriteAllText(FavoritePath,JsonConvert.SerializeObject(_favorites));Draw();},w-160,4,150);
        var tabs=new[]{("intro",T("Introduction","介绍")),("deps",T("Dependencies","依赖项")),("changelog",T("Changelog","更新日志")),("versions",T("Versions","版本")),("installed",T("Local installation","本地安装"))};int i=0;
        foreach(var tab in tabs){var local=tab;Button(_body,tab.Item2,()=>{_tab=local.Item1;_markdownDrawn="";Draw();},i++*140,44,130,UiActionRole.Edit);}
        var content=OverviewUi.Rect("DetailContent",_body);OverviewUi.Place(content,0,86,w,h-156);
        if(_tab is "intro" or "changelog")
        {
            string key=p.Key+"|"+_version+"|"+_tab;
            if(_markdownKey!=key){_markdownKey=key;_markdownDrawn="";_markdown=ShopCatalog.Markdown(p,_version,_tab=="changelog",_cancel.Token);}
            if(_markdown?.IsCompletedSuccessfully==true&&string.IsNullOrWhiteSpace(_markdown.Result)){Label(content,_tab=="changelog"?T("No changelog provided by the author.","作者未提供更新日志。"):T("No introduction provided by the author.","作者未提供模组介绍。"),8,8,w-16,40,17);_markdownDrawn=key;}
            else if(_markdown?.IsCompletedSuccessfully==true){_reader=new IntroductionEditorView(content,_document=new ShopMarkdownSource(_markdown.Result),OverviewUi.Text);_reader.Reload();_reader.OnOpened();}
            else Label(content,_markdown?.IsFaulted==true?T("Author document unavailable: ","作者文档暂不可用：")+_markdown.Exception!.GetBaseException().Message:T("Loading...","正在加载……"),8,8,w-16,80,17);
            if(_markdown?.IsFaulted==true)Button(content,T("Retry document","重试读取文档"),()=>{_markdownKey="";_markdownDrawn="";Draw();},8,100,180,UiActionRole.Edit);
        }
        else if(_tab=="installed")
        {
            var rows=Scroll(content,0,0,w,content.rect.height);
            string text=InstallationText(p)+(existing!=null?"\n"+T("Loaded this session: ","本次加载版本：")+(existing.Loaded?existing.PluginVersion:T("Not loaded","未加载")):"");
            var local=Label(rows,text,8,8,w-50,Math.Max(120,text.Split('\n').Length*30),17);local.textWrappingMode=TextWrappingModes.Normal;_localInstallationText=local;
            rows.sizeDelta=new Vector2(w-18,Math.Max(content.rect.height,local.rectTransform.sizeDelta.y+20));
        }
        else if(_tab=="versions")
        {
            var rows=Scroll(content,0,0,w,content.rect.height);float y=6;foreach(var v in p.Versions){var version=v;Button(rows,v.Version+" · "+v.Date.ToString("yyyy-MM-dd")+" · "+(v.Bytes/1048576d).ToString("0.00")+" MB",()=>{_version=version.Version;_markdownKey="";Draw();},8,y,w-40,UiActionRole.Edit);y+=40;}rows.sizeDelta=new Vector2(w-18,y);
        }
        else
        {
            var rows=Scroll(content,0,0,w,content.rect.height);float y=8;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Tree(ShopListing package,string version,int depth)
            {
                if(depth>12)return;var v=package.Versions.FirstOrDefault(v=>v.Version==version);foreach(string dep in v?.Dependencies??new())
                {int at=dep.LastIndexOf('-');var target=at>0?_catalog.FirstOrDefault(p=>p.Key==dep.Substring(0,at)):null;var dependency=target;
                    var row=FlatListRow.Create(rows,()=>{if(dependency!=null){_selected=dependency;_version=dependency.Version;_tab="intro";_markdownKey="";Draw();}},w-40,30,ModConfigLook.Surface(new Color(.12f,.17f,.18f,1)));OverviewUi.Place((RectTransform)row.transform,8,y,w-40,30);Label(row.transform,new string(' ',depth*3)+"- "+dep,8,0,w-64,28,16,OverviewUi.Blue);y+=34;if(at<0||!seen.Add(dep))continue;var child=_catalog.FirstOrDefault(p=>p.Key==dep.Substring(0,at));if(child!=null)Tree(child,dep.Substring(at+1),depth+1);}
            }
            Tree(p,_version,0);if(y==8)Label(rows,T("No declared dependencies.","没有声明依赖项。"),8,8,w-30,34,17);rows.sizeDelta=new Vector2(w-18,Math.Max(50,y));
        }
        _installButton=Button(_body,SelectedActionText(p),()=>{if(!_installationState.CanInstall(p.Key))return;var currentExisting=Installed(p)?_installed.FirstOrDefault(m=>m.SourceConfirmed&&m.SourceKey.Equals(p.Key,StringComparison.OrdinalIgnoreCase)&&File.Exists(m.Location)):null;if(currentExisting!=null&&_installationState.Snapshot.Packages.TryGetValue(p.Key,out var locations)&&locations.Any(location=>location.Cached))OverviewUpdateQueue.EnqueueTarget(_repo,currentExisting,_version,message=>_status.text=message);else Preview(false);},0,h-40,180,UiActionRole.Commit);
        _uninstallButton=Button(_body,T("Uninstall","卸载"),()=>Preview(true),192,h-40,150,UiActionRole.Danger);
        Button(_body,T("Open store page","打开商店页面"),()=>Application.OpenURL(ThunderstoreUpdateService.HomeUrl(p.Team,p.Name)),354,h-40,180);
        UpdateInstallationDetails();
    }
    private void ChooseGroup()
    {
        var popup=OverviewUi.Panel("InstallGroupList",_panel,ModConfigLook.Surface(new Color(.10f,.14f,.15f,1)));OverviewUi.Place(popup,30,_panel.rect.height-450,Math.Min(720,_panel.rect.width-60),380);
        var rows=Scroll(popup,10,48,popup.rect.width-20,320);float y=0;
        void Item(string name,string id,Color tint){var row=FlatListRow.Create(rows,()=>{_group=id;_newGroup="";Destroy(popup.gameObject);if(id=="new")GroupName();else ShowTask();},rows.rect.width-16,38,ModConfigLook.Surface(new Color(.12f,.17f,.18f,1)));OverviewUi.Place((RectTransform)row.transform,0,y,rows.rect.width-16,38);var rail=OverviewUi.Rect("Color",row.transform);OverviewUi.Place(rail,0,0,4,38);rail.gameObject.AddComponent<Image>().color=tint;Label(row.transform,name,14,1,rows.rect.width-40,34,17,tint);y+=42;}
        Item(T("Automatic grouping","自动分组"),"",OverviewUi.Muted);Item(T("Create group...","新建分组……"),"new",OverviewUi.Blue);
        var tree=new GroupTree(_repo.State.groups);
        foreach(var group in tree.Flat){ColorUtility.TryParseHtmlString(group.color,out var tint);Item(new string(' ',(tree.Depth(group.id)-1)*3)+(tree.Depth(group.id)>1?"└ ":"")+group.name,group.id,tint);}rows.sizeDelta=new Vector2(rows.rect.width,y);
        Button(popup,T("Close","关闭"),()=>Destroy(popup.gameObject),popup.rect.width-120,8,108);
    }
    private string ShopText(string text)
    {
        if(Loc.T("en","zh","zh")=="en")return text;
        var translations=new Dictionary<string,string>{["Review dependencies before approving downloads."]="请核对依赖清单，确认后下载到缓存并检查。",["Loader already supplied by this installation: "]="当前安装已提供加载框架：",["Files checked. Confirm installation after exiting PEAK."]="文件检查完成，确认后将在退出游戏后安装。",["Store operation completed. Start PEAK to use the result."]="操作完成，启动游戏后生效。",["No installed files were changed."]="现装文件未被改动。",["Review exact uninstall files; configuration is retained unless selected."]="请核对卸载范围；默认保留配置。",["preview"]="下载预览",["uninstall-preview"]="卸载预览",["waiting-exit"]="等待游戏退出",["ready"]="检查完成",["requested"]="已请求",["downloading"]="正在下载",["completed"]="已完成",["failed"]="失败"};
        foreach(var pair in translations.OrderByDescending(p=>p.Key.Length))text=text.Replace(pair.Key,pair.Value);return text;
    }
    private void GroupName()
    {
        var popup=OverviewUi.Panel("NewInstallGroup",_panel,ModConfigLook.Surface(new Color(.12f,.16f,.17f,1)));OverviewUi.Place(popup,30,150,550,180);
        var name=OverviewUi.Input(popup,"");OverviewUi.Place((RectTransform)name.transform,12,12,526,34);
        var color=OverviewUi.Input(popup,"#7CC7E6");OverviewUi.Place((RectTransform)color.transform,12,54,526,34);
        Button(popup,T("Use group","使用分组"),()=>{if(name.text.Trim().Length==0||!ColorUtility.TryParseHtmlString(color.text,out _))throw new IOException(T("Enter name and hex color.","请填写分组名和颜色代码。"));_newGroup=name.text.Trim();_group="";_groupColor=color.text;Destroy(popup.gameObject);ShowTask();},12,110,180,UiActionRole.Commit);
        Button(popup,T("Cancel","取消"),()=>Destroy(popup.gameObject),206,110,120);
    }
    private void Preview(bool uninstall)
    {
        if(_selected==null||(uninstall ? !_installationState.CanUninstall(_selected.Key,ReadLedger().Packages.Any(x=>x.Key==_selected.Key)) : !_installationState.CanInstall(_selected.Key)))return;
        if(uninstall){var selected=_selected!;UninstallDialog.Open(selected.Name,new ShopRequest{Namespace=selected.Team,Package=selected.Name,Version=_installation.Version(selected.Key)},_panel);return;}
        var p=_selected!;
        var request=new ShopRequest{Operation="preview-install",Namespace=p.Team,Package=p.Name,Version=_version,GroupId=_group,NewGroupName=_newGroup,NewGroupColor=_groupColor};
        if(Installed(p))
        {
            OverviewUpdateQueue.SubmitUpdates(new[]{new UpdateCommand{Id=Guid.NewGuid().ToString("N"),Action="shop",Shop=request}});
            _status.text=Loc.LocalizeMessage("Updates run independently; configuration changes remain unsaved.");
        }
        else
        {
            OverviewUpdateQueue.SendShop(request);
            _status.text=Loc.LocalizeMessage("Mod operation queued independently. Configuration changes remain unsaved.");
        }
    }
    private void ShowTask()
    {
        _showingTask=true;
        string path=Path.Combine(Home,"task.json");if(File.Exists(path))_task=JsonConvert.DeserializeObject<ShopTask>(File.ReadAllText(path));if(_task==null){_status.text=T("No store tasks.","没有商店任务。" );return;}
        if(_task.State=="uninstall-preview"){ShowUninstallConfirmation(_task);return;}
        ClearBody();var t=_task;var rows=Scroll(_body,0,0,_body.rect.width,_body.rect.height-56);float y=8;
        void Text(string text){var l=Label(rows,text,10,y,rows.rect.width-24,32,16);l.textWrappingMode=TextWrappingModes.Normal;float height=Math.Max(32,l.GetPreferredValues(text,rows.rect.width-24,0).y+8);OverviewUi.Place(l.rectTransform,10,y,rows.rect.width-24,height);y+=height+6;}
        Text(ShopText(t.State)+" · "+ShopText(t.Message));if(t.Error.Length>0)Text(t.Error);foreach(var blocker in t.Blockers)Text("! "+blocker);
        if(_groupTaskId!=t.Id){_groupTaskId=t.Id;_group=t.Request.GroupId;_newGroup=t.Request.NewGroupName;_groupColor=t.Request.NewGroupColor;}
        var shown=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Card(ShopPackage package,int depth)
        {
            if(depth>12)return;bool first=shown.Add(package.Key);bool open=_dependencyExpanded.Contains(package.Key);float indent=Math.Min(80,depth*22),width=rows.rect.width-indent-24;
            var row=FlatListRow.Create(rows,()=>{if(!_dependencyExpanded.Add(package.Key))_dependencyExpanded.Remove(package.Key);ShowTask();},width,72,package.Reuse?ModConfigLook.Surface(new Color(.10f,.20f,.16f,1)):ModConfigLook.Surface(new Color(.12f,.18f,.23f,1)));OverviewUi.Place((RectTransform)row.transform,10+indent,y,width,72);
            string previous=t.PreviousPackages.FirstOrDefault(p=>p.Key==package.Key)?.Version??"";
            var action=ComponentActions.Select(previous,package.Version);
            string state=package.Reuse?T("Already installed · reuse","已安装 · 直接复用"):action==ComponentAction.Upgrade?T("Upgrade","升级"):action==ComponentAction.Downgrade?T("Downgrade","降级"):action==ComponentAction.Reinstall?T("Reinstall","同版重装"):T("Download and install","下载并安装");
            Label(row.transform,(open?"- ":"+ ")+package.Key,12,6,width-24,28,18);Label(row.transform,package.Version+" · "+state+(first?"":T(" · shared reference"," · 共享引用")),12,37,width-24,26,15,package.Reuse?OverviewUi.Green:OverviewUi.Blue);y+=80;
            var dependencies=package.Dependencies.Where(d=>!d.StartsWith("BepInEx-BepInExPack",StringComparison.OrdinalIgnoreCase)).ToList();
            if(!open){if(dependencies.Count>0)Text(T("Depends on: ","依赖：")+string.Join(" · ",dependencies.Select(d=>{int at=d.LastIndexOf('-');return at>0?d.Substring(0,at):d;})));return;}
            if(!first)return;foreach(string declaration in dependencies){int at=declaration.LastIndexOf('-');if(at<1)continue;var child=t.Packages.FirstOrDefault(p=>p.Key.Equals(declaration.Substring(0,at),StringComparison.OrdinalIgnoreCase));if(child!=null)Card(child,depth+1);}
        }
        var target=t.Packages.FirstOrDefault(p=>p.Key==t.Request.Namespace+"-"+t.Request.Package);if(target!=null)Card(target,0);
        foreach(var package in t.Packages.Where(p=>!shown.Contains(p.Key)).ToArray())Card(package,0);
        foreach(var verify in t.VerificationPackages)Text(T("Explicit download consent: original official ZIP for exact installed-core verification: ","下载同意范围：用于现装核心逐文件核对的原版本官方 ZIP：")+verify.Key+" "+verify.Version+"\n"+verify.Url);
        foreach(string line in t.Details.Where(s=>!s.StartsWith("Loader already supplied",StringComparison.Ordinal)).Take(12))Text(ShopText(line));
        if(t.State=="completed"&&t.OrphanCandidates.Count>0)
        {
            Text(T("Possibly unused dependencies — nothing is removed automatically. Preview each candidate before cleaning.","可能不再使用的依赖：不会自动删除，清理前逐项检查文件与依赖。"));
            foreach(var orphan in t.OrphanCandidates)
            {
                var candidate=orphan;Button(rows,T("Review cleanup: ","预览清理：")+candidate.Key,()=>{int at=candidate.Key.IndexOf('-');OverviewUpdateQueue.SendShop(new ShopRequest{Operation="preview-uninstall",Namespace=candidate.Key.Substring(0,at),Package=candidate.Key.Substring(at+1),Version=candidate.Version});},10,y,Math.Min(600,rows.rect.width-30),UiActionRole.Danger);y+=40;
            }
        }
        Text(T("Files to remove: ","移除文件：")+t.Originals.Count+T("; files to write: ","；写入文件：")+t.Desired.Count);rows.sizeDelta=new Vector2(rows.rect.width,y);
        if(t.State is "preview" or "ready"){
            Button(_body,T("Install to group","安装到分组"),ChooseGroup,320,_body.rect.height-42,180,UiActionRole.Edit).interactable=!_submissionPending;
            Label(_body,_newGroup.Length>0?_newGroup:_repo.State.groups.FirstOrDefault(g=>g.id==_group)?.name??T("Automatic grouping","自动分组"),510,_body.rect.height-40,_body.rect.width-530,30,16);
        }
        if(t.State is "waiting-exit" or "committing" or "completed")Text(T("Install to group (locked): ","安装到分组（已锁定）：")+(t.Request.NewGroupName.Length>0?t.Request.NewGroupName:_repo.State.groups.FirstOrDefault(g=>g.id==t.Request.GroupId)?.name??T("Automatic grouping","自动分组")));
        rows.sizeDelta=new Vector2(rows.rect.width,y);
        string op=t.State=="preview"?"approve-download":t.State=="ready"?"confirm-install":t.State=="uninstall-preview"?"confirm-uninstall":t.State=="recovery-required"?"recover":"";
        if(op.Length>0)Button(_body,t.State=="preview"?T("Confirm download","确认下载"):t.State=="recovery-required"?T("Retry recovery","重试恢复"):T("Confirm after-exit operation","确认退出后执行"),()=>{
            if(_submissionPending)return;
            void Submit(){if(op=="confirm-install"&&(_group.Length>0||_newGroup.Length>0))ShopGroupingPersistence.Bind(Home,t.Id,_repo);_submissionPending=true;_sentAt=Time.unscaledTime;_sentTaskKey=_taskKey;OverviewUpdateQueue.SendShop(new ShopRequest{Operation=op,PreviewId=t.Id,PlanToken=t.PlanToken,GroupId=_group,NewGroupName=_newGroup,NewGroupColor=_groupColor});ShowTask();_status.text=T("Submitted. Waiting for helper acknowledgement.","已提交，等待助手确认。" );}
            if(op is "confirm-install" or "confirm-uninstall"){
                var popup=OverviewUi.Panel("ShopFinalConfirm",_panel,ModConfigLook.Surface(new Color(.14f,.12f,.10f,1)));OverviewUi.Place(popup,30,_panel.rect.height-280,Math.Min(800,_panel.rect.width-60),180);
                Label(popup,op=="confirm-uninstall"?T("Uninstall the previewed files after PEAK exits? Configuration is retained. A backup will be kept.","确认在退出游戏后卸载以上文件？默认保留配置，并保留恢复备份。"):T("Install the checked files and dependencies after PEAK exits? Existing changed files are backed up.","确认在退出游戏后安装已检查的文件及依赖？被替换文件会先备份。"),14,12,popup.rect.width-28,90,18).textWrappingMode=TextWrappingModes.Normal;
                Button(popup,T("Confirm","确认执行"),()=>{Destroy(popup.gameObject);Submit();},14,126,160,op=="confirm-uninstall"?UiActionRole.Danger:UiActionRole.Commit);Button(popup,T("Cancel","取消"),()=>Destroy(popup.gameObject),188,126,108);
            }else Submit();
        },0,_body.rect.height-42,300,UiActionRole.Commit).interactable=t.Blockers.Count==0&&!_submissionPending;
    }
    private void ShowUninstallConfirmation(ShopTask task)
    {
        ClearBody();
        var card=OverviewUi.Panel("UninstallSummary",_body,ModConfigLook.Surface(new Color(.13f,.12f,.11f,1)));OverviewUi.Place(card,12,12,_body.rect.width-24,Math.Min(300,_body.rect.height-70));
        Label(card,T("Uninstall mod","卸载模组"),16,12,card.rect.width-32,34,24);
        Label(card,task.Request.Namespace+"-"+task.Request.Package+" · "+task.Request.Version,16,52,card.rect.width-32,34,21);
        Label(card,T("Files to remove: ","移除文件：")+task.Originals.Count+"\n"+T("Configuration is retained. Files are backed up before removal. The operation runs after PEAK exits.","保留配置。移除前备份文件，退出游戏后执行。"),16,98,card.rect.width-32,100,18).textWrappingMode=TextWrappingModes.Normal;
        if(task.Blockers.Count>0){
            string message=T("Cannot uninstall yet: ","暂不能卸载：")+task.Blockers[0]+(task.Blockers.Count>1?T("\nMore details are available below.","\n其他原因可展开详情查看。"):"");
            Label(card,message,16,194,card.rect.width-32,80,16,new Color(.95f,.65f,.37f)).textWrappingMode=TextWrappingModes.Normal;
            Button(_body,T("View details","查看详情"),()=>{var popup=OverviewUi.Panel("UninstallDetails",_panel,ModConfigLook.Surface(new Color(.12f,.14f,.15f,1)));OverviewUi.Place(popup,30,100,_panel.rect.width-60,_panel.rect.height-190);var text=OverviewUi.Input(popup,string.Join("\n\n",task.Blockers),true);text.readOnly=true;text.textComponent.fontSize=16;OverviewUi.Place((RectTransform)text.transform,12,12,popup.rect.width-24,popup.rect.height-64);Button(popup,T("Close","关闭"),()=>Destroy(popup.gameObject),12,popup.rect.height-42,108);},12,_body.rect.height-42,130);
            return;
        }
        Button(_body,T("Uninstall...","卸载……"),()=>OpenUninstallFinal(task),12,_body.rect.height-42,180,UiActionRole.Danger).interactable=!_submissionPending;
        if(_uninstallConfirmedId!=task.Id){_uninstallConfirmedId=task.Id;OpenUninstallFinal(task);}
    }
    private void OpenUninstallFinal(ShopTask task)
    {
        var popup=OverviewUi.Panel("UninstallFinalConfirm",_panel,ModConfigLook.Surface(new Color(.15f,.10f,.10f,1)));OverviewUi.Place(popup,40,150,Math.Min(800,_panel.rect.width-80),200);
        Label(popup,T("Confirm uninstall: ","确认卸载：")+task.Request.Namespace+"-"+task.Request.Package+"?\n"+T("Keep configuration and back up files. Uninstall after PEAK exits.","保留配置并备份文件，退出游戏后卸载。"),16,16,popup.rect.width-32,110,20).textWrappingMode=TextWrappingModes.Normal;
        Button(popup,T("Confirm uninstall","确认卸载"),()=>{if(_submissionPending)return;_submissionPending=true;_sentAt=Time.unscaledTime;OverviewUpdateQueue.SendShop(new ShopRequest{Operation="confirm-uninstall",PreviewId=task.Id,PlanToken=task.PlanToken});Destroy(popup.gameObject);_status.text=T("Uninstall queued; waiting for PEAK to exit.","卸载已加入队列，等待游戏退出。" );},16,144,180,UiActionRole.Danger);
        Button(popup,T("Cancel","取消"),()=>Destroy(popup.gameObject),208,144,108);
    }
    internal static void ResumeGrouping(OverviewRepository repo)
    {
        string home=ModConfigEnhance.Updating.ManagerIdentity.Runtime(BepInEx.Paths.BepInExRootPath, "shop"),file=Path.Combine(home,"task.json");
        if(!File.Exists(file))return;
        try
        {
            var task=JsonConvert.DeserializeObject<ShopTask>(File.ReadAllText(file));
            if(task!=null)ShopGroupingPersistence.Apply(home,task,repo);
        }
        catch(Exception ex){Plugin.Log.LogWarning("[Shop grouping] Pending grouping was not consumed: "+ex.Message);}
    }
    private void ApplyCompletedGroup(ShopTask task)
    {
        if(ShopGroupingPersistence.Apply(Home,task,_repo))
            _status.text=T("Installation grouping saved; other drafts remain unsaved.","安装分组已保存，其他草稿仍未保存。" );
    }
    private void Network()=>Options(T("Network","网络"),new[]{(T("System proxy","系统代理"),"system"),(T("Direct connection","直接连接"),"direct"),(T("Custom proxy...","自定义代理……"),"custom")},mode=>{
        if(mode!="custom"){_networkSubmission.Submit(mode,"",text=>_status.text=text);return;}
        var popup=WindowPopover.Open(_panel,_networkAnchor,"ShopProxy",380,124);
        var address=OverviewUi.Input(popup,OverviewUpdateQueue.Snapshot.Network.ProxyAddress,false,"http://127.0.0.1:7890");OverviewUi.Place((RectTransform)address.transform,12,12,popup.rect.width-24,39);
        Button(popup,T("Save proxy","保存代理"),()=>{string value=address.text.Trim();if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="http"&&uri.Scheme!="https"||uri.UserInfo.Length>0)throw new IOException("HTTP(S) proxy address required.");_networkSubmission.Submit("custom",value,text=>_status.text=text);_panel.GetComponent<WindowPopover>()?.Close();},12,70,180,UiActionRole.Commit);Button(popup,T("Cancel","取消"),()=>_panel.GetComponent<WindowPopover>()?.Close(),210,70,120);
    });
    private void Update()
    {
        if(InventoryContext!=_inventoryContext){_inventoryContext=InventoryContext;_installationState.Reset(_inventoryContext);ReloadInstalledRows();_inventoryAt=0;InstallationChanged();}
        if(_inventory?.IsCompleted==true){
            bool changed=_installationState.Complete(_pendingGeneration,_pendingContext,
                _inventory.IsCompletedSuccessfully?_inventory.Result.Snapshot:null,
                _inventory.IsCompletedSuccessfully?"":_inventory.Exception?.GetBaseException().Message??"Inventory scan cancelled.");
            if(_inventory.IsCompletedSuccessfully&&_installationState.Current(_pendingGeneration,_pendingContext)
                &&_pendingDependencyContext==SourceUi.DependencyContext&&_pendingRevision==OverviewPageCache.Inventory)
                SourceUi.PublishDependencyGraph(_inventory.Result.Graph,_pendingDependencyContext,_pendingRevision);
            _inventory=null;
            if(changed)InstallationChanged();else if(_selected!=null&&!_showingTask)UpdateInstallationDetails();
        }
        if(_inventory==null&&Time.unscaledTime>=_inventoryAt)RefreshInstallation();
        if(_openPackage.Length>0&&_catalog.Count>0){var package=_catalog.FirstOrDefault(p=>p.Key==_openPackage);_openPackage="";if(package!=null){_selected=package;_version=package.Version;_tab="intro";_markdownKey="";Draw();}else _status.text=T("Package not found in the cached catalog.","当前目录未找到对应商店包。" );}
        if(_openUninstall&&_openPackage.Length==0&&_selected!=null&&_installationState.Status==ShopInstallationState.Phase.Ready){
            _openUninstall=false;string reason=_installationState.UninstallBlockReason(_selected.Key);
            if(_selected.Key!=_uninstallPackage)_status.text=T("Selection changed; uninstall was not started.","选择已变化，未开始卸载。");
            else if(reason.Length>0)_status.text=Loc.LocalizeMessage(reason);else Preview(true);
        }
        if(_sentAt>0&&Time.unscaledTime-_sentAt>1){OverviewUpdateQueue.Poll();string error=OverviewUpdateQueue.LastError;if(error.Length>0){_status.text=T("Request failed: ","请求失败：")+Loc.LocalizeMessage(error);_submissionPending=false;_sentAt=0;}else if(Time.unscaledTime-_sentAt>15){_status.text=T("The helper has not acknowledged this request. No new operation was confirmed.","助手尚未确认此请求，请查看下载队列错误；没有确认新的文件操作。" );_submissionPending=false;_sentAt=0;}}
        if(_reader!=null&&_document?.MediaChanged()==true)_reader.Reload();
        if(_searchPending&&Time.unscaledTime>=_searchAt&&string.IsNullOrEmpty(Input.compositionString)){_searchPending=false;_query=_search.text;_page=0;_selected=null;Draw();}
        if(_loading?.IsCompleted==true){if(_loading.IsFaulted)_status.text=T("Catalog refresh failed; cached results retained: ","目录刷新失败，继续使用缓存：")+_loading.Exception!.GetBaseException().Message;else{_catalog=_loading.Result;_status.text=_catalog.Count+T(" packages"," 个包");}_loading=null;if(!_showingTask){Draw();if(_installationState.Status!=ShopInstallationState.Phase.Ready)_status.text=InventoryMessage;}}
        if(!_showingTask&&_markdown?.IsCompleted==true&&_reader==null&&_selected!=null&&_tab is "intro" or "changelog" && _markdownDrawn!=_markdownKey){_markdownDrawn=_markdownKey;Draw();}
        if(Time.unscaledTime<_poll)return;_poll=Time.unscaledTime+.7f;
        try{string file=Path.Combine(Home,"task.json");if(!File.Exists(file))return;var task=JsonConvert.DeserializeObject<ShopTask>(File.ReadAllText(file));if(task==null)return;string key=task.Id+task.State+task.Error;if(key!=_taskKey){if(task.State=="completed")ApplyCompletedGroup(task);_sentAt=0;_submissionPending=false;_taskKey=key;_task=task;_status.text=ShopText(task.Message)+(task.Error.Length>0?" · "+ShopText(task.Error):"");if(_showingTask&&task.Id==_task?.Id&&key==_taskKey){_task=task;}
            if((_requestedTask||_showingTask)&&(task.State is "preview" or "ready" or "uninstall-preview" or "recovery-required" or "failed" or "waiting-exit" or "downloading" or "committing" || task.State=="completed"&&task.OrphanCandidates.Count>0)){_requestedTask=false;ShowTask();}}}catch(Exception ex) when(ex is IOException or JsonException or InvalidOperationException){_status.text=T("Grouping/task read failed; pending grouping retained: ","分组或任务读取失败，保留待处理分组：")+ex.Message;}
    }
    private void OnDestroy(){_cancel.Cancel();_reader?.Dispose();_cancel.Dispose();}
}
