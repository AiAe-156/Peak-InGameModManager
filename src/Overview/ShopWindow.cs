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
using UnityEngine.EventSystems;
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
    {Relink();
        var p=_selected;if(p==null)return;
        _status.text=p.Name+"："+InstallationText(p).Split('\n')[0];
        bool core=_installation.Packages.TryGetValue(p.Key,out var rows)&&rows.Any(r=>r.Area=="core");
        if(_installButton!=null){
            bool enabled=_installationState.CanInstall(p.Key);
            if(_installButton.interactable!=enabled){_installButton.interactable=enabled;PublicationNavigation.Invalidate(_panel);}
            _installButton.GetComponentInChildren<TextMeshProUGUI>().text=SelectedActionText(p);
        }
        if(_uninstallButton!=null){bool enabled=_installationState.CanUninstall(p.Key,ReadLedger().Packages.Any(x=>x.Key==p.Key));if(_uninstallButton.interactable!=enabled){_uninstallButton.interactable=enabled;PublicationNavigation.Invalidate(_panel);}}
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
        var view=panel.gameObject.AddComponent<ShopWindow>();view._repo=repo;view._installed=installed.ToArray();view._panel=panel;view._requestedTask=showTask;view._openPackage=packageKey;view._openUninstall=uninstall;view._uninstallPackage=uninstall?packageKey:"";view._installationState.DependencyUninstallReason=SourceUi.UninstallReason;PublicationNavigation.AttachBoundary(panel);view.Build();
        OverviewUi.SetModalCloseHandler(()=>{if(view==null||!view.BackOneLayer()){if(BackGuard.Allow())OverviewUi.CloseModal();}});
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
        Button(_panel,T("Tasks","下载任务"),ShowQueue,w-286,12,140);
        _search=OverviewUi.Input(_panel,"");OverviewUi.Place((RectTransform)_search.transform,20,58,w-570,36);_search.textComponent.fontSize=20;_search.textComponent.margin=Vector4.zero;_search.textComponent.alignment=TextAlignmentOptions.MidlineLeft;
        _search.onValueChanged.AddListener(_=>{_searchAt=Time.unscaledTime+.18f;_searchPending=true;});
        _search.onSubmit.AddListener(_=>{_query=_search.text;_page=0;_selected=null;Draw();});
        Button(_panel,T("Search","搜索"),()=>{_query=_search.text;_page=0;_selected=null;Draw();},w-536,60,108,UiActionRole.Edit);
        Button? sortButton=null;
        sortButton=Button(_panel,T("Sort","排序"),()=>Options(T("Sort","排序"),new[]{(Loc.T("Last updated","最近更新","最近更新"),"updated"),(Loc.T("Downloads","下载量","下載量"),"downloads"),(Loc.T("Name","名称","名稱"),"name")},v=>{_sort=v;Draw();},(RectTransform)sortButton!.transform),w-418,60,118);
        Button(_panel,T("Refresh","刷新目录"),()=>{ReloadInstalledRows();RetryInstallation();_loading=ShopCatalog.Load(_cancel.Token,true);_status.text=T("Reading store...","正在读取商店……");},w-290,60,130);
        Button(_panel,T("Back","返回上一级"),()=>{_showingTask=false;_showingQueue=false;if(_selected!=null)_selected=null;_tab="intro";_requestedTask=false;Draw();},20,12,128);
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
    private void ClearBody(){if(_queue!=null){_queue.Dispose();_queue=null;}_localInstallationText=null;_installButton=null;_uninstallButton=null;_reader?.Dispose();_reader=null;_document=null;foreach(Transform child in _body){child.gameObject.SetActive(false);Destroy(child.gameObject);}}
    private bool Installed(ShopListing p)=>_installation.Has(p.Key);
    private ShopLedger ReadLedger(){string file=PackLibrary.Managed?Path.Combine(PackLibrary.Workspace,"shop-installed.json"):Path.Combine(Home,"installed-unmanaged.json");try{return File.Exists(file)?JsonConvert.DeserializeObject<ShopLedger>(File.ReadAllText(file))??new():new();}catch(IOException){return new();}}
    private void Draw()
    {
        RememberFocus();
        try{DrawBody();}finally{WireGamepad();}
    }
    private void DrawBody()
    {
        ClearBody();if(_selected!=null){Details();return;}
        float w=_body.rect.width,h=_body.rect.height,left=190;
        var filters=Scroll(_body,0,0,left,h);float y=8;
        int filterIndex=0;
        Button Filter(string label,Action action){var button=Button(filters,label,action,8,y,left-36,UiActionRole.Edit);y+=38;Key(button,"filter:"+filterIndex++);return button;}
        Filter(Loc.T("Core / preloader","核心／启动补丁更新","核心／啟動修補更新"),()=>{
            var components=_installation.Packages.Values.SelectMany(r=>r).Where(r=>r.Area is "core" or "patchers").ToList();var listing=_catalog.Where(p=>components.Any(c=>c.Key==p.Key));
            string text=string.Join("\n\n",listing.Select(p=>{var local=components.First(c=>c.Key==p.Key);return p.Key+"\n"+local.Version+" → "+p.Version+"\n"+local.Path;}));
            var popup=OverviewUi.Panel("InfrastructureVersions",_panel,ModConfigLook.Surface(new Color(.10f,.14f,.15f,1)));Popup(popup);OverviewUi.Place(popup,30,150,Math.Min(1000,_panel.rect.width-60),Math.Min(500,_panel.rect.height-200));var field=OverviewUi.Input(popup,_installationState.Status!=ShopInstallationState.Phase.Ready?InventoryMessage:text.Length>0?text:T("No recognized package manifests.","未识别到组件包清单。"),true);field.readOnly=true;field.textComponent.fontSize=17;OverviewUi.Place((RectTransform)field.transform,12,12,popup.rect.width-24,popup.rect.height-64);Button(popup,T("Close","关闭"),()=>Destroy(popup.gameObject),12,popup.rect.height-42,108);
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
            var item=p;float x=(n%columns)*(cardWidth+10),top=(n/columns)*(cardHeight+10);var row=FlatListRow.Create(cards,()=>{_cardFocus=item.Key;_selected=item;_version=item.Version;_tab="intro";_markdownKey="";Draw();},cardWidth,cardHeight,ModConfigLook.Surface(new Color(.12f,.16f,.17f,1)));OverviewUi.Place((RectTransform)row.transform,x,top,cardWidth,cardHeight);Key(row,"card:"+item.Key);
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
        Key(Button(_body,T("Previous","上一页"),()=>{_page--;Draw();},left+14,h-36,110),"page:prev");Key(Button(_body,T("Next","下一页"),()=>{_page++;Draw();},left+134,h-36,110),"page:next");
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
        _status.text=p.Name+"："+InstallationText(p).Split('\n')[0];
        Button(_body,_favorites.Contains(p.Key)?T("Unfavorite","取消收藏"):T("Favorite","收藏"),()=>{if(!_favorites.Add(p.Key))_favorites.Remove(p.Key);Directory.CreateDirectory(Path.GetDirectoryName(FavoritePath)!);File.WriteAllText(FavoritePath,JsonConvert.SerializeObject(_favorites));Draw();},w-160,4,150);
        var tabs=new[]{("intro",T("Introduction","介绍")),("deps",T("Dependencies","依赖项")),("changelog",T("Changelog","更新日志")),("versions",T("Versions","版本")),("installed",T("Local installation","本地安装"))};int i=0;
        foreach(var tab in tabs){var local=tab;var tabButton=Button(_body,tab.Item2,()=>{_tab=local.Item1;_markdownDrawn="";Draw();},i++*140,44,130,UiActionRole.Edit);Key(tabButton,"tab:"+local.Item1);UiActionStyle.Select(tabButton,local.Item1==_tab);}
        var content=OverviewUi.Rect("DetailContent",_body);OverviewUi.Place(content,0,86,w,h-156);
        if(_tab is "intro" or "changelog")
        {
            string key=p.Key+"|"+_version+"|"+_tab;
            if(_markdownKey!=key){_markdownKey=key;_markdownDrawn="";_markdown=ShopCatalog.Markdown(p,_version,_tab=="changelog",_cancel.Token);}
            if(_markdown?.IsCompletedSuccessfully==true&&string.IsNullOrWhiteSpace(_markdown.Result)){Label(content,_tab=="changelog"?T("No changelog provided by the author.","作者未提供更新日志。"):T("No introduction provided by the author.","作者未提供模组介绍。"),8,8,w-16,40,17);_markdownDrawn=key;}
            else if(_markdown?.IsCompletedSuccessfully==true){_reader=new IntroductionEditorView(content,_document=new ShopMarkdownSource(_markdown.Result),OverviewUi.Text);_reader.SetControllerReading(false);_reader.Reload();_reader.OnOpened();AddReadingSurface(content);}
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
            var rows=Scroll(content,0,0,w,content.rect.height);float y=6;foreach(var v in p.Versions){var version=v;Key(Button(rows,v.Version+" · "+v.Date.ToString("yyyy-MM-dd")+" · "+(v.Bytes/1048576d).ToString("0.00")+" MB",()=>{_version=version.Version;_markdownKey="";Draw();},8,y,w-40,UiActionRole.Edit),"version:"+v.Version);y+=40;}rows.sizeDelta=new Vector2(w-18,y);
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
        Key(_installButton,"action:install");
        _uninstallButton=Button(_body,T("Uninstall","卸载"),()=>Preview(true),192,h-40,150,UiActionRole.Danger);Key(_uninstallButton,"action:uninstall");
        Button(_body,T("Open store page","打开商店页面"),()=>Application.OpenURL(ThunderstoreUpdateService.HomeUrl(p.Team,p.Name)),354,h-40,180);
        UpdateInstallationDetails();
    }
    private void ChooseGroup()
    {
        var popup=OverviewUi.Panel("InstallGroupList",_panel,ModConfigLook.Surface(new Color(.10f,.14f,.15f,1)));Popup(popup);OverviewUi.Place(popup,30,_panel.rect.height-450,Math.Min(720,_panel.rect.width-60),380);
        var rows=Scroll(popup,10,48,popup.rect.width-20,320);float y=0;
        void Item(string name,string id,Color tint){var row=FlatListRow.Create(rows,()=>{_group=id;_newGroup="";Destroy(popup.gameObject);if(id=="new")GroupName();else ShowTask();},rows.rect.width-16,38,ModConfigLook.Surface(new Color(.12f,.17f,.18f,1)));OverviewUi.Place((RectTransform)row.transform,0,y,rows.rect.width-16,38);var rail=OverviewUi.Rect("Color",row.transform);OverviewUi.Place(rail,0,0,4,38);rail.gameObject.AddComponent<Image>().color=tint;Label(row.transform,name,14,1,rows.rect.width-40,34,17,tint);y+=42;}
        Item(T("Automatic grouping","自动分组"),"",OverviewUi.Muted);Item(T("Create group...","新建分组……"),"new",OverviewUi.Blue);
        var tree=new GroupTree(_repo.State.groups);
        foreach(var group in tree.Flat){ColorUtility.TryParseHtmlString(group.color,out var tint);Item(new string(' ',(tree.Depth(group.id)-1)*3)+(tree.Depth(group.id)>1?"└ ":"")+group.name,group.id,tint);}rows.sizeDelta=new Vector2(rows.rect.width,y);
        Button(popup,T("Close","关闭"),()=>Destroy(popup.gameObject),popup.rect.width-120,8,108);
    }
    private static string ShopText(string text)
    {
        if(Loc.T("en","zh","zh")=="en")return text;
        var translations=new Dictionary<string,string>{["Review dependencies before approving downloads."]="请核对依赖清单，确认后下载到缓存并检查。",["Loader already supplied by this installation: "]="当前安装已提供加载框架：",["Files checked. Confirm installation after exiting PEAK."]="文件检查完成，确认后将在退出游戏后安装。",["Store operation completed. Start PEAK to use the result."]="操作完成，启动游戏后生效。",["No installed files were changed."]="现装文件未被改动。",["Review exact uninstall files; configuration is retained unless selected."]="请核对卸载范围；默认保留配置。",["preview"]="下载预览",["uninstall-preview"]="卸载预览",["waiting-exit"]="等待游戏退出",["ready"]="检查完成",["requested"]="已请求",["downloading"]="正在下载",["completed"]="已完成",["failed"]="失败"};
        foreach(var pair in translations.OrderByDescending(p=>p.Key.Length))text=text.Replace(pair.Key,pair.Value);return text;
    }
    private void GroupName()
    {
        var popup=OverviewUi.Panel("NewInstallGroup",_panel,ModConfigLook.Surface(new Color(.12f,.16f,.17f,1)));Popup(popup);OverviewUi.Place(popup,30,150,550,180);
        var name=OverviewUi.Input(popup,"");OverviewUi.Place((RectTransform)name.transform,12,12,526,34);
        var color=OverviewUi.Input(popup,"#7CC7E6");OverviewUi.Place((RectTransform)color.transform,12,54,526,34);
        Button(popup,T("Use group","使用分组"),()=>{if(name.text.Trim().Length==0||!ColorUtility.TryParseHtmlString(color.text,out _))throw new IOException(T("Enter name and hex color.","请填写分组名和颜色代码。"));_newGroup=name.text.Trim();_group="";_groupColor=color.text;Destroy(popup.gameObject);ShowTask();},12,110,180,UiActionRole.Commit);
        Button(popup,T("Cancel","取消"),()=>Destroy(popup.gameObject),206,110,120);
    }
    private void Preview(bool uninstall)
    {
        if(_selected==null||(uninstall ? !_installationState.CanUninstall(_selected.Key,ReadLedger().Packages.Any(x=>x.Key==_selected.Key)) : !_installationState.CanInstall(_selected.Key)))return;
        if(uninstall){var selected=_selected!;UninstallDialog.Open(selected.Name,new ShopRequest{Namespace=selected.Team,Package=selected.Name,Version=_installation.Version(selected.Key)},_panel);
            var overlay=_panel.Find("UninstallOverlay") as RectTransform;if(overlay!=null)Popup(overlay,safeLast:true);return;}
        var p=_selected!;
        var request=new ShopRequest{Operation="preview-install",Namespace=p.Team,Package=p.Name,Version=_version,GroupId=_group,NewGroupName=_newGroup,NewGroupColor=_groupColor};
        if(Installed(p))
        {
            OverviewUpdateQueue.SubmitUpdates(new[]{new UpdateCommand{Id=Guid.NewGuid().ToString("N"),Action="shop",Shop=request}});
            _status.text=p.Name+"："+Loc.LocalizeMessage("Updates run independently; configuration changes remain unsaved.");
        }
        else
        {
            OverviewUpdateQueue.SendShop(request);
            _status.text=p.Name+"："+Loc.LocalizeMessage("Mod operation queued independently. Configuration changes remain unsaved.");
        }
    }
    /// <summary>1.1.5 (user: "store tasks and update tasks must be one data source, same tasks, same history, in sync"):
    /// the store's task view is the update queue itself (embedded), fed by the same snapshot as the overview's queue.</summary>
    private UpdateQueueView? _queue;
    private void ShowQueue()
    {
        _showingTask=true;_showingQueue=true;ClearBody();_queue?.Dispose();
        var host=OverviewUi.Rect("ShopQueue",_body);OverviewUi.Fill(host);
        _queue=new UpdateQueueView(host,()=>{},()=>{},text=>_status.text=text,Network,id=>{_cameFromQueue=true;ShowTask();});
        _queue.HideUpdateButtons(); _queue.Open(OverviewUpdateQueue.Snapshot);
        _status.text=Loc.T("Same list as the update queue in the mod overview.","与模组概览中的更新队列是同一份列表。","與模組概覽中的更新佇列是同一份列表。");
    }
    private bool _showingQueue,_cameFromQueue;
    private void ShowTask()
    {
        _showingQueue=false;
        _showingTask=true;StartCoroutine(WireAfterBuild());
        string path=Path.Combine(Home,"task.json");if(File.Exists(path))_task=JsonConvert.DeserializeObject<ShopTask>(File.ReadAllText(path));
        // 1.1.5 (user: "the download page is ugly and unclear; redo it"): status card + package list + folded details.
        // Folder organisation / local import previews belong to the update queue, not to the store's task page.
        if(_task!=null&&_task.Request.Operation is "layout-preview" or "local-preview")_task=null;
        if(_task==null||_task.Dismissed){ClearBody();Label(_body,Loc.T("No store task. Install or update a mod from the store and its progress shows here.","当前没有商店任务。在商店中安装或更新模组后，进度会显示在这里。","目前沒有商店任務。在商店中安裝或更新模組後，進度會顯示在這裡。"),8,8,_body.rect.width-16,40,18,OverviewUi.Muted);_status.text=Loc.T("No store task.","没有商店任务。","沒有商店任務。");return;}
        if(_task.State=="uninstall-preview"){ShowUninstallConfirmation(_task);return;}
        ClearBody();var t=_task;float W=_body.rect.width,H=_body.rect.height;
        if(_groupTaskId!=t.Id){_groupTaskId=t.Id;_group=t.Request.GroupId;_newGroup=t.Request.NewGroupName;_groupColor=t.Request.NewGroupColor;}
        var target=t.Packages.FirstOrDefault(p=>p.Key==t.Request.Namespace+"-"+t.Request.Package);
        string targetName=t.Request.Package.Length>0?t.Request.Package:target?.Key??"";
        string targetVersion=target?.Version??t.Request.Version;

        // status card
        var card=OverviewUi.Panel("TaskCard",_body,ModConfigLook.Surface(new Color(.10f,.14f,.16f,1)));OverviewUi.Place(card,0,0,W,150);
        bool failed=t.State is "failed" or "recovery-required" or "rolled-back";
        Color red=new Color(.92f,.42f,.38f);
        Color accent=failed?red:t.State=="completed"?OverviewUi.Green:t.State=="cancelled"?OverviewUi.Muted:OverviewUi.Blue;
        var rail=OverviewUi.Rect("Accent",card);OverviewUi.Place(rail,0,0,5,150);rail.gameObject.AddComponent<Image>().color=accent;
        Label(card,TaskKind(t)+"　<b>"+targetName+"</b>　<color=#8C918E>"+targetVersion+"</color>",20,12,W-260,34,22).richText=true;
        Label(card,TaskStateName(t.State),W-330,14,310,30,18,accent).alignment=TextAlignmentOptions.MidlineRight;
        string[] stepNames={Loc.T("Check","检查","檢查"),Loc.T("Download","下载","下載"),Loc.T("Wait for exit","等待退出","等待退出"),Loc.T("Install","安装","安裝"),Loc.T("Done","完成","完成")};
        int at=TaskStep(t.State);float sx=20,sw=(W-40)/stepNames.Length;
        for(int i=0;i<stepNames.Length;i++)
        {
            bool reached=i<at||(i==at&&!failed),current=i==at;
            var dot=OverviewUi.Rect("Step",card);OverviewUi.Place(dot,sx+i*sw,56,14,14);var di=dot.gameObject.AddComponent<Image>();di.sprite=ModConfigSidebarWidgets.RoundedSprite;di.type=Image.Type.Sliced;
            di.color=current&&failed?red:reached?accent:new Color(1,1,1,.18f);
            if(i<stepNames.Length-1){var line=OverviewUi.Rect("StepLine",card);OverviewUi.Place(line,sx+i*sw+20,62,sw-26,2);line.gameObject.AddComponent<Image>().color=i<at?accent:new Color(1,1,1,.14f);}
            Label(card,stepNames[i],sx+i*sw-4,74,sw,24,15,current?OverviewUi.Ivory:reached?accent:OverviewUi.Muted);
        }
        float progress=t.TotalBytes>0?Mathf.Clamp01((float)t.DownloadedBytes/t.TotalBytes):t.State=="completed"?1f:0f;
        var track=OverviewUi.Rect("Track",card);OverviewUi.Place(track,20,108,W-360,10);track.gameObject.AddComponent<Image>().color=new Color(1,1,1,.10f);
        var fill=OverviewUi.Rect("Fill",track);fill.anchorMin=Vector2.zero;fill.anchorMax=new Vector2(progress,1);fill.offsetMin=fill.offsetMax=Vector2.zero;fill.gameObject.AddComponent<Image>().color=accent;
        string amount=t.TotalBytes>0?(progress*100).ToString("0")+"%　"+Bytes(t.DownloadedBytes)+" / "+Bytes(t.TotalBytes):"";
        Label(card,amount,W-330,100,310,26,15,OverviewUi.Muted).alignment=TextAlignmentOptions.MidlineRight;
        Label(card,NextStep(t),20,122,W-40,26,15,failed?new Color(1f,.72f,.55f):OverviewUi.Ivory);

        // package list
        float listTop=162,footer=52,detailsH=Math.Min(220,H*.28f);
        var listHost=OverviewUi.Rect("Packages",_body);OverviewUi.Place(listHost,0,listTop,W,H-listTop-footer-detailsH-10);
        var rows=Scroll(listHost,0,0,W,listHost.rect.height);float y=6;
        var ordered=new List<ShopPackage>();if(target!=null)ordered.Add(target);ordered.AddRange(t.Packages.Where(p=>p!=target));
        Label(rows,Loc.F("{0} package(s) in this task","本次涉及 {0} 个包","本次涉及 {0} 個包",ordered.Count),10,y,W-40,26,15,OverviewUi.Muted);y+=30;
        foreach(var package in ordered)
        {
            string previous=t.PreviousPackages.FirstOrDefault(p=>p.Key==package.Key)?.Version??"";
            var action=ComponentActions.Select(previous,package.Version);
            string tag;Color tint;
            if(package.Reuse){tag=Loc.T("Reuse","复用","複用");tint=OverviewUi.Green;}
            else if(action==ComponentAction.Upgrade){tag=Loc.T("Upgrade","升级","升級");tint=OverviewUi.Blue;}
            else if(action==ComponentAction.Downgrade){tag=Loc.T("Downgrade","降级","降級");tint=new Color(1f,.72f,.4f);}
            else if(action==ComponentAction.Reinstall){tag=Loc.T("Reinstall","重装","重裝");tint=OverviewUi.Muted;}
            else{tag=Loc.T("New","新装","新裝");tint=new Color(.80f,.62f,1f);}
            var row=OverviewUi.Rect("Package",rows);OverviewUi.Place(row,6,y,W-36,40);row.gameObject.AddComponent<Image>().color=ModConfigLook.Surface(package==target?new Color(.13f,.19f,.22f,1):new Color(.10f,.13f,.15f,1));
            var chip=OverviewUi.Rect("Tag",row);OverviewUi.Place(chip,10,8,68,24);var ci=chip.gameObject.AddComponent<Image>();ci.sprite=ModConfigSidebarWidgets.RoundedSprite;ci.type=Image.Type.Sliced;ci.color=new Color(tint.r,tint.g,tint.b,.22f);
            Label(chip,tag,0,0,68,24,14,tint).alignment=TextAlignmentOptions.Center;
            Label(row,package.Key+(package==target?"":"　<color=#8C918E>"+Loc.T("dependency","依赖","依賴")+"</color>"),92,6,W-400,28,17).richText=true;
            string versions=previous.Length>0&&previous!=package.Version?previous+"  →  "+package.Version:package.Version;
            Label(row,versions,W-300,6,250,28,16,OverviewUi.Muted).alignment=TextAlignmentOptions.MidlineRight;
            y+=46;
        }
        foreach(var blocker in t.Blockers){Label(rows,"! "+ShopText(blocker),10,y,W-40,28,15,new Color(1f,.72f,.55f));y+=30;}
        if(t.State=="completed"&&t.OrphanCandidates.Count>0)
        {
            Label(rows,Loc.T("Possibly unused dependencies (nothing is removed automatically):","可能不再使用的依赖（不会自动删除）：","可能不再使用的依賴（不會自動刪除）："),10,y,W-40,28,15,OverviewUi.Muted);y+=32;
            foreach(var orphan in t.OrphanCandidates){var candidate=orphan;Button(rows,Loc.T("Review cleanup: ","预览清理：","預覽清理：")+candidate.Key,()=>{int dash=candidate.Key.IndexOf('-');OverviewUpdateQueue.SendShop(new ShopRequest{Operation="preview-uninstall",Namespace=candidate.Key.Substring(0,dash),Package=candidate.Key.Substring(dash+1),Version=candidate.Version});},10,y,Math.Min(600,W-60),UiActionRole.Danger);y+=40;}
        }
        rows.sizeDelta=new Vector2(rows.rect.width,y+6);

        // folded details
        var detailsHost=OverviewUi.Rect("Details",_body);OverviewUi.Place(detailsHost,0,H-footer-detailsH,W,detailsH);
        bool editable=t.State is "preview" or "ready";
        string group=editable?(_newGroup.Length>0?_newGroup:_repo.State.groups.FirstOrDefault(g=>g.id==_group)?.name??Loc.T("Automatic grouping","自动分组","自動分組"))
            :(t.Request.NewGroupName.Length>0?t.Request.NewGroupName:_repo.State.groups.FirstOrDefault(g=>g.id==t.Request.GroupId)?.name??Loc.T("Automatic grouping","自动分组","自動分組"));
        var lines=new List<string>{
            Loc.F("Install location: {0}","安装位置：{0}","安裝位置：{0}",group)+(editable?"":Loc.T(" (locked)","（已锁定）","（已鎖定）")),
            Loc.F("Files: write {0}, replace or remove {1}","文件：写入 {0} 个，替换或移除 {1} 个","檔案：寫入 {0} 個，替換或移除 {1} 個",t.Desired.Count,t.Originals.Count)};
        if(t.Error.Length>0)lines.Add(Loc.T("Error: ","错误：","錯誤：")+ShopText(t.Error));
        foreach(var verify in t.VerificationPackages)lines.Add(Loc.T("Also downloads for verification: ","另下载用于核对：","另下載用於核對：")+verify.Key+" "+verify.Version);
        var detailLines=t.Details.Where(s=>!s.StartsWith("Loader already supplied",StringComparison.Ordinal)).Select(ShopText).ToList();
        var drows=Scroll(detailsHost,0,0,W,detailsH);float dy=6;
        foreach(var line in lines){Label(drows,line,10,dy,W-40,26,15,OverviewUi.Ivory);dy+=28;}
        if(detailLines.Count>0){Button(drows,(_taskDetailsOpen?"▾ ":"▸ ")+Loc.F("Detailed record ({0} lines)","详细记录（{0} 行）","詳細記錄（{0} 行）",detailLines.Count),()=>{_taskDetailsOpen=!_taskDetailsOpen;ShowTask();},10,dy,300);dy+=38;}
        if(_taskDetailsOpen)foreach(var line in detailLines){var l=Label(drows,line,22,dy,W-60,24,14,OverviewUi.Muted);l.textWrappingMode=TextWrappingModes.Normal;float lh=Math.Max(24,l.GetPreferredValues(line,W-60,0).y+4);OverviewUi.Place(l.rectTransform,22,dy,W-60,lh);dy+=lh+2;}
        drows.sizeDelta=new Vector2(drows.rect.width,dy+6);

        // actions
        string op=t.State=="preview"?"approve-download":t.State=="ready"?"confirm-install":t.State=="recovery-required"?"recover":"";
        if(op.Length>0)Button(_body,t.State=="preview"?Loc.T("Confirm download","确认下载","確認下載"):t.State=="recovery-required"?Loc.T("Retry recovery","重试恢复","重試恢復"):Loc.T("Confirm (installs after exit)","确认（退出游戏后安装）","確認（退出遊戲後安裝）"),()=>{
            if(_submissionPending)return;
            void Submit(){if(op=="confirm-install"&&(_group.Length>0||_newGroup.Length>0))ShopGroupingPersistence.Bind(Home,t.Id,_repo);_submissionPending=true;_sentAt=Time.unscaledTime;_sentTaskKey=_taskKey;OverviewUpdateQueue.SendShop(new ShopRequest{Operation=op,PreviewId=t.Id,PlanToken=t.PlanToken,GroupId=_group,NewGroupName=_newGroup,NewGroupColor=_groupColor});ShowTask();_status.text=Loc.T("Submitted. Waiting for the helper to confirm.","已提交，等待助手确认。","已提交，等待助手確認。");}
            if(op=="confirm-install"){
                var popup=OverviewUi.Panel("ShopFinalConfirm",_panel,ModConfigLook.Surface(new Color(.14f,.12f,.10f,1)));Popup(popup,safeLast:true);OverviewUi.Place(popup,30,_panel.rect.height-280,Math.Min(800,_panel.rect.width-60),180);
                Label(popup,Loc.T("Install the checked files and dependencies after PEAK exits? Replaced files are backed up first.","确认在退出游戏后安装已检查的文件及依赖？被替换的文件会先备份。","確認在退出遊戲後安裝已檢查的檔案及依賴？被替換的檔案會先備份。"),14,12,popup.rect.width-28,90,18).textWrappingMode=TextWrappingModes.Normal;
                Button(popup,Loc.T("Confirm","确认执行","確認執行"),()=>{Destroy(popup.gameObject);Submit();},14,126,160,UiActionRole.Commit);Button(popup,Loc.T("Cancel","取消","取消"),()=>Destroy(popup.gameObject),188,126,108);
            }else Submit();
        },0,H-42,320,UiActionRole.Commit).interactable=t.Blockers.Count==0&&!_submissionPending;
        if(editable)Button(_body,Loc.T("Install to group…","安装到分组…","安裝到分組…"),ChooseGroup,336,H-42,200,UiActionRole.Edit).interactable=!_submissionPending;
        _status.text=targetName.Length>0?targetName+"："+TaskStateName(t.State):TaskStateName(t.State);
    }
    private bool _taskDetailsOpen;
    private static string Bytes(long value)=>value>=1024*1024?(value/(1024.0*1024)).ToString("0.0")+" MB":Math.Max(1,value/1024)+" KB";
    private static string TaskKind(ShopTask t)=>t.Uninstall?Loc.T("Uninstall","卸载","解除安裝"):t.PreviousPackages.Count>0?Loc.T("Update","更新","更新"):Loc.T("Install","安装","安裝");
    /// <summary>0 check, 1 download, 2 wait for exit, 3 install, 4 done.</summary>
    private static int TaskStep(string state)=>state switch{"requested" or "resolving" or "preview"=>0,"downloading"=>1,"ready" or "waiting-exit"=>2,"committing" or "rolling-back" or "recovery-required"=>3,"completed"=>4,"failed" or "cancelled" or "rolled-back"=>1,_=>0};
    private static string TaskStateName(string state)=>state switch{
        "requested"=>Loc.T("Requested","已请求","已請求"),"resolving"=>Loc.T("Checking dependencies","正在检查依赖","正在檢查依賴"),"preview"=>Loc.T("Waiting for download confirmation","等待确认下载","等待確認下載"),
        "downloading"=>Loc.T("Downloading","正在下载","正在下載"),"ready"=>Loc.T("Downloaded, waiting for install confirmation","已下载，等待确认安装","已下載，等待確認安裝"),"waiting-exit"=>Loc.T("Waiting for PEAK to exit","等待游戏退出","等待遊戲退出"),
        "committing"=>Loc.T("Installing","正在安装","正在安裝"),"completed"=>Loc.T("Completed","已完成","已完成"),"failed"=>Loc.T("Failed","失败","失敗"),"cancelled"=>Loc.T("Cancelled","已取消","已取消"),
        "rolled-back"=>Loc.T("Rolled back","已回滚","已回滾"),"rolling-back"=>Loc.T("Rolling back","正在回滚","正在回滾"),"recovery-required"=>Loc.T("Needs recovery","需要恢复","需要恢復"),_=>state};
    private static string NextStep(ShopTask t)=>t.State switch{
        "requested" or "resolving"=>Loc.T("Next: the helper reads the dependencies; nothing is downloaded yet.","下一步：助手读取依赖清单，尚未下载。","下一步：助手讀取依賴清單，尚未下載。"),
        "preview"=>Loc.T("Next: check the packages below, then press \"Confirm download\".","下一步：核对下方的包，然后按「确认下载」。","下一步：核對下方的包，然後按「確認下載」。"),
        "downloading"=>Loc.T("Next: files are downloaded into the cache and checked; installed files are not touched yet.","下一步：文件下载到缓存并检查，现装文件暂不改动。","下一步：檔案下載到快取並檢查，現裝檔案暫不改動。"),
        "ready"=>Loc.T("Next: press \"Confirm\"; the files are installed after PEAK exits.","下一步：按「确认」，退出游戏后安装。","下一步：按「確認」，退出遊戲後安裝。"),
        "waiting-exit"=>Loc.T("Next: exit PEAK; the files are installed automatically.","下一步：退出游戏，文件会自动安装。","下一步：退出遊戲，檔案會自動安裝。"),
        "committing"=>Loc.T("Installing; replaced files are backed up first.","正在安装，被替换的文件会先备份。","正在安裝，被替換的檔案會先備份。"),
        "completed"=>Loc.T("Done. Start PEAK to use the result.","已完成，启动游戏后生效。","已完成，啟動遊戲後生效。"),
        "failed" or "rolled-back"=>Loc.T("Installed files were not changed. See the detailed record below.","现装文件未被改动，原因见下方详细记录。","現裝檔案未被改動，原因見下方詳細記錄。"),
        "recovery-required"=>Loc.T("The last install was interrupted. Press \"Retry recovery\".","上次安装中断，请按「重试恢复」。","上次安裝中斷，請按「重試恢復」。"),
        "cancelled"=>Loc.T("This task was cancelled.","此任务已取消。","此任務已取消。"),_=>ShopText(t.Message)};
    private void ShowUninstallConfirmation(ShopTask task)
    {
        ClearBody();StartCoroutine(WireAfterBuild());
        var card=OverviewUi.Panel("UninstallSummary",_body,ModConfigLook.Surface(new Color(.13f,.12f,.11f,1)));OverviewUi.Place(card,12,12,_body.rect.width-24,Math.Min(300,_body.rect.height-70));
        Label(card,T("Uninstall mod","卸载模组"),16,12,card.rect.width-32,34,24);
        Label(card,task.Request.Namespace+"-"+task.Request.Package+" · "+task.Request.Version,16,52,card.rect.width-32,34,21);
        Label(card,T("Files to remove: ","移除文件：")+task.Originals.Count+"\n"+T("Configuration is retained. Files are backed up before removal. The operation runs after PEAK exits.","保留配置。移除前备份文件，退出游戏后执行。"),16,98,card.rect.width-32,100,18).textWrappingMode=TextWrappingModes.Normal;
        if(task.Blockers.Count>0){
            string message=T("Cannot uninstall yet: ","暂不能卸载：")+task.Blockers[0]+(task.Blockers.Count>1?T("\nMore details are available below.","\n其他原因可展开详情查看。"):"");
            Label(card,message,16,194,card.rect.width-32,80,16,new Color(.95f,.65f,.37f)).textWrappingMode=TextWrappingModes.Normal;
            Button(_body,T("View details","查看详情"),()=>{var popup=OverviewUi.Panel("UninstallDetails",_panel,ModConfigLook.Surface(new Color(.12f,.14f,.15f,1)));Popup(popup);OverviewUi.Place(popup,30,100,_panel.rect.width-60,_panel.rect.height-190);var text=OverviewUi.Input(popup,string.Join("\n\n",task.Blockers),true);text.readOnly=true;text.textComponent.fontSize=16;OverviewUi.Place((RectTransform)text.transform,12,12,popup.rect.width-24,popup.rect.height-64);Button(popup,T("Close","关闭"),()=>Destroy(popup.gameObject),12,popup.rect.height-42,108);},12,_body.rect.height-42,130);
            return;
        }
        Button(_body,T("Uninstall...","卸载……"),()=>OpenUninstallFinal(task),12,_body.rect.height-42,180,UiActionRole.Danger).interactable=!_submissionPending;
        if(_uninstallConfirmedId!=task.Id){_uninstallConfirmedId=task.Id;OpenUninstallFinal(task);}
    }
    private void OpenUninstallFinal(ShopTask task)
    {
        var popup=OverviewUi.Panel("UninstallFinalConfirm",_panel,ModConfigLook.Surface(new Color(.15f,.10f,.10f,1)));Popup(popup,safeLast:true);OverviewUi.Place(popup,40,150,Math.Min(800,_panel.rect.width-80),200);
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
        GamepadFrame();
        if(_showingQueue&&_queue!=null){OverviewUpdateQueue.Poll();_queue.Refresh(OverviewUpdateQueue.Snapshot,OverviewUpdateQueue.LastError);}
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
        try{string file=Path.Combine(Home,"task.json");if(!File.Exists(file))return;var task=JsonConvert.DeserializeObject<ShopTask>(File.ReadAllText(file));if(task==null)return;string key=task.Id+task.State+task.Error;if(key!=_taskKey){if(task.State=="completed")ApplyCompletedGroup(task);_sentAt=0;_submissionPending=false;_taskKey=key;_task=task;_status.text=(task.Request.Package.Length>0?task.Request.Package+"：":"")+ShopText(task.Message)+(task.Error.Length>0?" · "+ShopText(task.Error):"");if(_showingTask&&task.Id==_task?.Id&&key==_taskKey){_task=task;}
            if((_requestedTask||_showingTask&&!_showingQueue)&&(task.State is "preview" or "ready" or "uninstall-preview" or "recovery-required" or "failed" or "waiting-exit" or "downloading" or "committing" || task.State=="completed"&&task.OrphanCandidates.Count>0)){_requestedTask=false;ShowTask();}}}catch(Exception ex) when(ex is IOException or JsonException or InvalidOperationException){_status.text=T("Grouping/task read failed; pending grouping retained: ","分组或任务读取失败，保留待处理分组：")+ex.Message;}
    }
    private void OnDestroy(){_cancel.Cancel();_reader?.Dispose();_cancel.Dispose();}

    // ── 1.1.4 gamepad ─────────────────────────────────────────────────────────
    private readonly Dictionary<GameObject,string> _keys=new();
    private string _focusKey="",_cardFocus="";
    private RectTransform? _popup;private bool _popupOpen;private GameObject? _popupOpener;private CanvasGroup? _bodyGroup;
    private ModConfigEnhance.Overview.Help.HelpReadingSurface? _readingSurface;
    /// <summary>Pages built outside Draw (task, uninstall confirmation): wire them once they exist.</summary>
    private System.Collections.IEnumerator WireAfterBuild(){yield return null;if(_body!=null){RememberFocus();WireGamepad();}}
    /// <summary>Install / uninstall / retry change state after the inventory scan; relink so they are reachable.</summary>
    internal void Relink(){if(isActiveAndEnabled)StartCoroutine(LinkNextFrame());}
    private T0 Key<T0>(T0 control,string key) where T0:Selectable{if(control!=null)_keys[control.gameObject]=key;return control!;}
    /// <summary>Before a rebuild: remember what had the focus by its logical key (filter, card, tab, version, button).</summary>
    private void RememberFocus()
    {
        var selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
        if(selected!=null&&selected.transform.IsChildOf(_body)&&_keys.TryGetValue(selected,out var key))_focusKey=key;
        else if(selected!=null&&selected.transform.IsChildOf(_body)&&_readingSurface!=null&&selected==_readingSurface.gameObject)_focusKey="reader";
        _keys.Clear();
    }
    /// <summary>After a rebuild: explicit navigation, focus ring + scroll follow, and the focus back on the same logical
    /// control (or the card that was opened, or the first card / first tab).</summary>
    private void WireGamepad()
    {
        foreach(var scroll in _body.GetComponentsInChildren<ScrollRect>(true))
            foreach(var control in scroll.content.GetComponentsInChildren<Selectable>(true))GamepadSupport.MarkSelectable(control.gameObject,scroll,(RectTransform)control.transform);
        foreach(var control in _body.GetComponentsInChildren<Selectable>(true))if(control.GetComponent<GamepadSupport.FocusScrollIntoView>()==null)GamepadSupport.MarkSelectable(control.gameObject,null,null);
        StartCoroutine(LinkNextFrame());
    }
    private System.Collections.IEnumerator LinkNextFrame()
    {
        yield return null; // layout settles first
        if(_body==null)yield break;
        var top=_panel.GetComponentsInChildren<Selectable>(false).Where(s=>!s.transform.IsChildOf(_body)&&(_popup==null||!s.transform.IsChildOf(_popup))&&s.IsInteractable()&&s.transform.parent==_panel).OrderBy(s=>Pos(s).y).ThenBy(s=>Pos(s).x).ToList();
        // Disabled buttons stay in the links (install / uninstall turn on once the inventory scan finishes); Unity skips a
        // non-interactable target, so nothing lands on them while they are off.
        var body=_body.GetComponentsInChildren<Selectable>(false).Where(s=>s.IsActive()&&s.navigation.mode!=Navigation.Mode.None).ToList();
        if(body.Count==0&&top.Count==0)yield break;
        PublicationNavigation.Invalidate(_panel);
        RestoreFocus(body,top);
    }
    private static Vector2 Pos(Selectable s){var r=(RectTransform)s.transform;return r.TransformPoint(r.rect.center);}
    /// <summary>Nearest visible control in a direction: must lie in that half-plane; distance weights the cross axis 3×.</summary>
    private Selectable? Nearest(Selectable from,List<Selectable> all,Vector2 dir)
    {
        Vector2 o=Pos(from);Selectable? best=null;float bestScore=float.MaxValue;
        foreach(var c in all)
        {
            if(c==from||!c.gameObject.activeInHierarchy)continue;
            Vector2 d=Pos(c)-o;float along=Vector2.Dot(d,dir);if(along<=4f)continue;
            float cross=Mathf.Abs(dir.x!=0?d.y:d.x);float score=along+cross*3f;
            if(score<bestScore){bestScore=score;best=c;}
        }
        return best;
    }
    /// <summary>Controls inside a scroll view count only when on screen, except the one closest to the edge (so a move
    /// past the bottom row reaches the next row; the focus follow scrolls it in).</summary>
    private static bool VisibleInScroll(Selectable c)
    {
        var scroll=c.GetComponentInParent<ScrollRect>();if(scroll==null||scroll.viewport==null)return true;
        var corners=new Vector3[4];scroll.viewport.GetWorldCorners(corners);
        var r=(RectTransform)c.transform;var mine=new Vector3[4];r.GetWorldCorners(mine);
        float margin=r.rect.height*1.2f*r.lossyScale.y;
        return mine[0].y<corners[1].y+margin&&mine[1].y>corners[0].y-margin;
    }
    private void RestoreFocus(List<Selectable> body,List<Selectable> top)
    {
        if(!GamepadSupport.IsGamepad||_popup!=null)return;
        var system=EventSystem.current;if(system==null)return;
        var current=system.currentSelectedGameObject;
        if(current!=null&&current.activeInHierarchy&&current.transform.IsChildOf(_panel))return;
        GameObject? target=null;
        string want=_selected==null&&_cardFocus.Length>0&&_focusKey.StartsWith("action:")?"card:"+_cardFocus:_focusKey;
        if(_selected==null&&_cardFocus.Length>0&&(want.StartsWith("tab:")||want=="reader"||want.StartsWith("version:")||want.StartsWith("action:")))want="card:"+_cardFocus;
        if(_selected!=null&&want.StartsWith("card:"))want="tab:"+_tab;
        if(want=="reader"&&_readingSurface!=null)target=_readingSurface.gameObject;
        if(target==null)foreach(var pair in _keys)if(pair.Value==want&&pair.Key!=null&&pair.Key.activeInHierarchy){target=pair.Key;break;}
        if(target==null)target=_keys.FirstOrDefault(p=>p.Key!=null&&p.Key.activeInHierarchy&&p.Value.StartsWith(_selected!=null?"tab:":"card:")).Key;
        if(target==null&&body.Count>0)target=body[0].gameObject;
        if(target==null&&top.Count>0)target=top[0].gameObject;
        if(target!=null)system.SetSelectedGameObject(target);
    }
    /// <summary>A popup panel built on the store window: takes the focus, blocks the page behind it, B closes it and the
    /// focus returns to the opener.</summary>
    private void Popup(RectTransform popup,bool safeLast=false)
    {
        if(_popup==null)_popupOpener=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
        _popup=popup;_popupOpen=true;
        var group=popup.gameObject.AddComponent<CanvasGroup>();group.ignoreParentGroups=true;
        if(_bodyGroup==null)_bodyGroup=_panel.GetComponent<CanvasGroup>()??_panel.gameObject.AddComponent<CanvasGroup>();
        _bodyGroup.interactable=false;
        StartCoroutine(FocusPopup(popup,safeLast));
    }
    private System.Collections.IEnumerator FocusPopup(RectTransform popup,bool safeLast)
    {
        yield return null;if(popup==null)yield break;
        foreach(var input in popup.GetComponentsInChildren<TMP_InputField>(true)){if(input.readOnly)input.shouldActivateOnSelect=false;else GamepadSupport.MarkSelectable(input.gameObject,null,null);}
        PublicationNavigation.AttachBoundary(popup);
        yield return null;if(popup==null||!GamepadSupport.IsGamepad)yield break;
        var buttons=popup.GetComponentsInChildren<Button>(false).Where(b=>b.IsInteractable()).ToList();
        var pick=safeLast?buttons.LastOrDefault():buttons.FirstOrDefault();
        if(pick!=null)EventSystem.current?.SetSelectedGameObject(pick.gameObject);
    }
    private void GamepadFrame()
    {
        // A popup closed by its own buttons (Destroy) leaves a destroyed reference: Unity's == null is true for it.
        if(_popupOpen&&(_popup==null||!_popup.gameObject.activeInHierarchy))ClosePopupState();
        if(!GamepadSupport.IsGamepad||_popup!=null)return;
        var system=EventSystem.current;if(system==null)return;
        var current=system.currentSelectedGameObject;
        if(current==null||!current.activeInHierarchy)
        {
            var pad=UnityEngine.InputSystem.Gamepad.current;
            if(pad!=null&&(pad.dpad.ReadValue().sqrMagnitude>.25f||pad.leftStick.ReadValue().sqrMagnitude>.25f||pad.buttonSouth.wasPressedThisFrame))
                RestoreFocus(_body.GetComponentsInChildren<Selectable>(false).Where(s=>s.IsInteractable()).ToList(),new List<Selectable>());
        }
    }
    private void ClosePopupState()
    {
        _popup=null;_popupOpen=false;if(_bodyGroup!=null)_bodyGroup.interactable=true;
        var opener=_popupOpener;_popupOpener=null;
        if(opener!=null&&opener.activeInHierarchy&&GamepadSupport.IsGamepad)EventSystem.current?.SetSelectedGameObject(opener);
    }
    /// <summary>B: popup → zoomed image → details back to the list → (false) close the store.</summary>
    internal bool BackOneLayer()
    {
        if(WindowPopover.TryCloseTop())return true;
        if(_popup!=null){Destroy(_popup.gameObject);ClosePopupState();return true;}
        if(_reader!=null&&_reader.TryCloseLayer())return true;
        if(_showingTask&&!_showingQueue&&_queue==null&&_cameFromQueue){_cameFromQueue=false;ShowQueue();return true;}
        if(_showingTask){_showingTask=false;_showingQueue=false;_requestedTask=false;Draw();return true;}
        if(_selected!=null){_focusKey="card:"+_selected.Key;_cardFocus=_selected.Key;_selected=null;_tab="intro";Draw();return true;}
        return false;
    }
    /// <summary>One controller reading surface: directions navigate, A locks browsing, B unlocks; a second A opens links.</summary>
    private void AddReadingSurface(RectTransform content)
    {
        var scroll=content.GetComponentInChildren<ScrollRect>(true);if(scroll==null)return;
        var host=OverviewUi.Rect("MCE_ShopReading",content);OverviewUi.Fill(host);host.SetAsLastSibling();
        var image=host.gameObject.AddComponent<Image>();image.color=Color.clear;image.raycastTarget=false; // pointer goes to the document
        var surface=host.gameObject.AddComponent<ModConfigEnhance.Overview.Help.HelpReadingSurface>();surface.Scroll=scroll;surface.targetGraphic=image;surface.transition=Selectable.Transition.None;
        surface.TryMovePage=delta=>{if(scroll.content==null)return false;var p=scroll.content.anchoredPosition;float max=Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height);float y=Mathf.Clamp(p.y+delta,0,max);if(Mathf.Abs(y-p.y)<.5f)return false;p.y=y;scroll.content.anchoredPosition=p;return true;};
        surface.Links=ShowDocumentLinks;
        GamepadSupport.MarkSelectable(host.gameObject,null,null);
        _readingSurface=surface;Key(surface,"reader");
    }
    private void ShowDocumentLinks()
    {
        if(_document==null||_markdown==null||!_markdown.IsCompletedSuccessfully)return;
        var links=new List<(string Label,string Value)>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(_markdown.Result,@"\[([^\]]*)\]\((https?://[^)\s]+)\)"))
            if(seen.Add(m.Groups[2].Value))links.Add(((m.Groups[1].Value.Length>0?m.Groups[1].Value:m.Groups[2].Value)+Loc.T(" (external)","（外部）","（外部）"),m.Groups[2].Value));
        foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(_markdown.Result,@"(?<![\(\[""])https?://[^\s)\]>""]+"))
            if(seen.Add(m.Value))links.Add((m.Value+Loc.T(" (external)","（外部）","（外部）"),m.Value));
        if(links.Count==0){_status.text=Loc.T("No links on this page.","本页没有链接。","本頁沒有連結。");return;}
        // OverviewUi.Choose has no owner and would CloseModal() — that is the store itself. Use the store's popover host.
        var anchor=_readingSurface!=null?(RectTransform)_readingSurface.transform:_networkAnchor;
        WindowPopover.Menu(_panel,anchor,T("Links on this page","本页链接"),links,"",url=>Application.OpenURL(url));
    }
}
