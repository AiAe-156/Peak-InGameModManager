#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>Hotkeys share the catalogue's groups but own their search, mod folds and pixel scroll anchor.</summary>
internal sealed class OverviewHotkeyView : IDisposable
{
    private readonly OverviewRepository _repo;
    private readonly Func<IReadOnlyList<OverviewMod>> _mods;
    private readonly Action<string> _status;
    private readonly Action _headersChanged, _exportPack;
    private readonly Action<OverviewMod?> _openSettings;
    private readonly Action<GameObject, OverviewMod> _dependencyHint;
    private readonly Func<OverviewMod, int?> _displayNumber;
    private readonly RectTransform _root, _tools, _header, _content, _viewport, _hint;
    private readonly TextMeshProUGUI _hintText;
    private readonly TMP_InputField _search;
    private readonly TextMeshProUGUI _placeholder;
    private readonly ScrollRect _scroll;
    private readonly OverviewCascade _cascade;
    private readonly OverviewListPump _pump;
    private readonly OverviewHotkeyCapture _capture;
    private readonly Dictionary<string, GameObject> _targets = new(StringComparer.Ordinal);
    private GameObject? _popup, _popupReturn;
    private string _selectedId = "", _query = "";
    private int _filter, _revision = -1;
    private float _width;
    private bool _authorMode, _opened, _disposed, _rendering, _dirty;
    private bool _hintFollowsPointer;
    private RectTransform? _hintAnchor;
    public GameObject Root => _root.gameObject;
    public OverviewHotkeyService Service { get; }
    public Action? ManageGroups { get; set; }
    public Action<OverviewGroup?>? EditGroup { get; set; }
    public bool IsRecording => _capture.IsCapturing;
    internal Func<OverviewMod, string, bool>? StatusMatch;
    private OverviewTagSelection Tags => _repo.State.hotkeyTagFilter ??= new();
    internal void RefreshTagFilter() => Render();
    private static string T(string en, string zh) => Loc.T(en, zh, zh);

    public OverviewHotkeyView(RectTransform parent, OverviewRepository repo, Func<IReadOnlyList<OverviewMod>> mods,
        Action<string> status, Action headersChanged, Action<OverviewMod?> openSettings, Action exportPack,
        Action<GameObject, OverviewMod> bindDependencyHint, Func<OverviewMod, int?> displayNumber)
    {
        _displayNumber = displayNumber;
        _repo = repo; _mods = mods; _status = status; _headersChanged = headersChanged;
        _openSettings = openSettings; _exportPack = exportPack; _dependencyHint = bindDependencyHint;
        _root = OverviewUi.Rect("OverviewHotkeys", parent); OverviewUi.Fill(_root);
        _tools = OverviewUi.Rect("Tools", _root); _header = OverviewUi.Panel("Header", _root, ModConfigLook.Surface(new Color(.12f,.13f,.15f,.92f)));
        _search = OverviewUi.Input(_tools, ""); _search.characterLimit = 256;
        _search.textComponent.fontSize = 16;
        _placeholder = OverviewUi.Text(_search.transform, "", 15); OverviewUi.Fill(_placeholder.rectTransform, 4); _placeholder.color = OverviewUi.Muted; _search.placeholder = _placeholder;
        _search.onValueChanged.AddListener(v => { _query = v; Render(); });
        var host = OverviewUi.Rect("Scroll", _root); OverviewUi.Fill(host); host.offsetMax = new Vector2(0,-73);
        _viewport = OverviewUi.Panel("Viewport", host, Color.clear); OverviewUi.Fill(_viewport); _viewport.offsetMax = new Vector2(-15,0); _viewport.gameObject.AddComponent<RectMask2D>();
        _content = OverviewUi.Rect("Rows", _viewport); _content.anchorMin = new Vector2(0,1); _content.anchorMax = Vector2.one; _content.pivot = new Vector2(0,1);
        _scroll = host.gameObject.AddComponent<ScrollRect>(); _scroll.viewport = _viewport; _scroll.content = _content; _scroll.horizontal = false; _scroll.scrollSensitivity = 40; _scroll.movementType = ScrollRect.MovementType.Clamped;
        var bar = OverviewUi.Panel("Scrollbar", host, ModConfigLook.Surface(new Color(.1f,.1f,.1f,.5f))); bar.anchorMin = new Vector2(1,0); bar.anchorMax = Vector2.one; bar.offsetMin = new Vector2(-12,0); bar.offsetMax = Vector2.zero;
        var handle = OverviewUi.Panel("Handle", bar, OverviewUi.Muted); OverviewUi.Fill(handle);
        var scrollbar = bar.gameObject.AddComponent<Scrollbar>(); scrollbar.handleRect = handle; scrollbar.targetGraphic = handle.GetComponent<Image>(); scrollbar.direction = Scrollbar.Direction.BottomToTop; _scroll.verticalScrollbar = scrollbar;
        _cascade = _root.gameObject.AddComponent<OverviewCascade>();
        _hint = OverviewUi.Panel("HotkeyHint", _root, ModConfigLook.Surface(new Color(.08f,.08f,.1f,.98f))); _hint.GetComponent<Image>().raycastTarget = false;
        _hintText = OverviewUi.Text(_hint,"",17); OverviewUi.Fill(_hintText.rectTransform,10); _hintText.textWrappingMode = TextWrappingModes.Normal; _hint.gameObject.SetActive(false);
        Service = new OverviewHotkeyService(repo); Service.Changed += OnChanged;
        _capture = _root.gameObject.AddComponent<OverviewHotkeyCapture>();
        _pump = _root.gameObject.AddComponent<OverviewListPump>(); _pump.Tick = Tick;
        // 构造期不读数据，交给 OnPageEnter / OnOpened 统一加载。
    }
    private void OnChanged() => _dirty = true;
    private bool _loaded;
    public void SetAuthorMode(bool value) { if (_authorMode == value) return; _authorMode = value; Service.AuthorMode = value; if (_loaded) Render(); }
    public void Reload() { Service.Reload(_mods()); _loaded = true; _dirty = false; Render(); }
    internal Func<ProfilePendingState>? PendingState;
    public void OnOpened() { _opened = true; if (!_loaded) Reload(); else if (_dirty) { _dirty = false; Render(); } if (_targets.TryGetValue(_selectedId, out var target) && target.activeInHierarchy) EventSystem.current?.SetSelectedGameObject(target); _cascade.Play(_scroll, AppearanceRows, _header); }
    public void OnClosed() { _cascade.PauseForExit(); var selected = EventSystem.current?.currentSelectedGameObject; if (selected != null) { var pair = _targets.FirstOrDefault(p => p.Value == selected); if (pair.Key != null) _selectedId = pair.Key; } _opened = false; _capture.CancelCapture(); ClosePopup(); _hint.gameObject.SetActive(false); }
    private IEnumerable<OverviewCascade.Row> AppearanceRows()
    {
        foreach (Transform child in _content)
            if (child is RectTransform rect) yield return new OverviewCascade.Row(rect);
    }
    public bool TryCloseLayer()
    {
        if (_capture.CancelCapture()) { _status(T("Recording cancelled.", "已取消按键录制。")); return true; }
        if (_popup != null) { ClosePopup(); return true; }
        return false;
    }
    private void Tick()
    {
        if (!_opened || _disposed) return;
        if (_hint.gameObject.activeSelf && _hintFollowsPointer) PositionHint();
        if (!_rendering && (_dirty || Math.Abs(_root.rect.width - _width) > 2 || _revision != OverviewLanguageBridge.Revision)) { _dirty = false; Render(); }
        if (!OverviewUi.HasModal && _popup == null && !_capture.IsCapturing && !_search.isFocused && Gamepad.current != null)
        {
            float stick = Gamepad.current.rightStick.ReadValue().y;
            if (Math.Abs(stick) > .15f) { _scroll.StopMovement(); SetPixel(_content.anchoredPosition.y - stick * 600 * Time.unscaledDeltaTime); }
        }
    }
    private void Save()
    {
        try
        {
            ConfigurationSaveRequest.Run(_repo, _mods(), ConfigurationSaveScope.Hotkeys, "", result => { _status(result.Message); _headersChanged(); });
        }
        catch (Exception ex) { _status(Loc.LocalizeMessage(ex.Message)); }
    }
    private void SetPixel(float value) => _content.anchoredPosition = new Vector2(0, Mathf.Clamp(value,0,Mathf.Max(0,_content.rect.height-_viewport.rect.height)));
    private static void Clear(Transform parent) { for(int i=parent.childCount-1;i>=0;i--) { var go=parent.GetChild(i).gameObject; go.SetActive(false); UnityEngine.Object.Destroy(go); } }
    private void Render()
    {
        if (!_loaded || _disposed || _rendering || !_cascade.BeforeRebuild()) return; _rendering = true;
        try
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            bool ownFocus = selected != null && selected.transform.IsChildOf(_content);
            if (ownFocus) { var old = _targets.FirstOrDefault(p=>p.Value==selected); if(old.Key!=null) _selectedId=old.Key; EventSystem.current?.SetSelectedGameObject(null); }
            float pixel = Math.Max(0,_content.anchoredPosition.y); _scroll.StopMovement();
            _hint.gameObject.SetActive(false); ClosePopup(); Clear(_content); Clear(_header);
            // Keep the search field alive while typing; tool buttons are rebuilt without replacing that input.
            for(int i=_tools.childCount-1;i>=0;i--) if(_tools.GetChild(i)!=_search.transform) { var go=_tools.GetChild(i).gameObject;go.SetActive(false);UnityEngine.Object.Destroy(go); }
            _targets.Clear(); _width = _root.rect.width; float width = Mathf.Max(1,_width-15); _revision = OverviewLanguageBridge.Revision;
            OverviewUi.Place(_tools,0,0,width,34); OverviewUi.Place(_header,0,37,width,32);
            float searchWidth = Math.Max(100,width-(_authorMode?340:232)); OverviewUi.Place((RectTransform)_search.transform,0,0,searchWidth,32);
            _placeholder.text=T("Search name, mod, description or key", "搜索名称、模组、说明或按键");
            Button filter=OverviewUi.Button(_tools,FilterLabel(),()=>Popup((RectTransform)_tools.transform,new[]{(T("All","全部"),(Action)(()=>SetFilter(0)),true),(T("Conflicts","重复"),(Action)(()=>SetFilter(1)),true),(T("Unbound","未绑定"),(Action)(()=>SetFilter(2)),true) }),96,30, role: UiActionRole.Edit);OverviewUi.Place((RectTransform)filter.transform,searchWidth+6,1,96,30);
            float toolX=searchWidth+108;
            if(_authorMode){Button adjust=null!;adjust=OverviewUi.Button(_tools,T("Preset options","预设调整"),()=>PresetMenu((RectTransform)adjust.transform),102,30, role: UiActionRole.Edit);OverviewUi.Place((RectTransform)adjust.transform,toolX,1,102,30);toolX+=108;}
            var save=OverviewUi.Button(_tools,T("Save hotkeys","保存快捷键"),Save,114,30, role: UiActionRole.Commit);OverviewUi.Place((RectTransform)save.transform,toolX,1,114,30);
            float xName=48, nameColumn=Mathf.Clamp(width*.18f,70,190), conflictWidth=Mathf.Clamp(width*.07f,32,64);
            // Budget all three binding columns together, leaving a real description column on narrow pages.
            float keyLimit=Mathf.Max(52,Mathf.Min(136,(width-xName-nameColumn-conflictWidth-Mathf.Min(120,width*.18f))/3));
            float KeyWidth(IEnumerable<string> labels) => labels.Select(s => Measure(s,15)+16).DefaultIfEmpty(74).Max();
            float keyWidth=Mathf.Min(keyLimit,Mathf.Max(74,Mathf.Max(
                KeyWidth(Service.Entries.Select(e=>e.CurrentLabel).Concat(Service.Entries.Select(e=>e.DefaultLabel)).Concat(Service.Entries.Select(e=>e.PackPreset?.Label??""))),
                new[]{T("Current key","当前按键"),T("Mod default","模组默认"),T("Profile preset","方案预设")}.Max(s=>Measure(s,16))+12)));
            float currentWidth=keyWidth,modDefaultWidth=keyWidth,packWidth=keyWidth;
            float xDesc=xName+nameColumn,xCurrent=width-packWidth-modDefaultWidth-conflictWidth-currentWidth,xConflict=xCurrent+currentWidth,xModDefault=xConflict+conflictWidth,xPack=xModDefault+modDefaultWidth;
            Label(_header,T("Setting","配置项名"),xName,1,xDesc-xName-6,30);
            Label(_header,T("Description","短说明"),xDesc,1,xCurrent-xDesc-6,30);
            var currentTitle=Label(_header,T("Current key","当前按键"),xCurrent,1,keyWidth-4,30);currentTitle.alignment=TextAlignmentOptions.Midline;
            Label(_header,"!",xConflict,1,xModDefault-xConflict-2,30);
            var defaultTitle=Label(_header,T("Mod default","模组默认"),xModDefault,1,keyWidth-4,30,OverviewUi.Green);defaultTitle.alignment=TextAlignmentOptions.Midline;
            var packTitle=Label(_header,T("Profile preset","方案预设"),xPack,1,keyWidth-4,30,OverviewUi.Blue);packTitle.alignment=TextAlignmentOptions.Midline;
            foreach (var columnTitle in new[]{currentTitle,defaultTitle,packTitle})
            {
                columnTitle.enableAutoSizing=true; columnTitle.fontSizeMin=12; columnTitle.fontSizeMax=16;
                Hint(columnTitle.gameObject,columnTitle.text);
            }
            var tree=new GroupTree(_repo.State.groups);
            var visible=Service.Entries.Where(Matches).OrderBy(e=>tree.Position(e.Mod.Rule.groupId)).ThenBy(e=>e.Mod.Rule.order).ThenBy(e=>e.Mod.Name).ThenBy(e=>e.Section).ThenBy(e=>e.Key).ToList();
            bool foldApplies=Tags.Count==0&&_filter==0&&string.IsNullOrWhiteSpace(_query);
            var emitted=new HashSet<string>(StringComparer.Ordinal);
            var settingsOwners = OverviewSettingsNavigator.SettingsOwners();
            float y=0;
            void Header(string id)
            {
                if(!emitted.Add(id))return;
                var group=tree.Get(id);Color color=ColorUtility.TryParseHtmlString(group?.color??"",out var c)?c:OverviewUi.Muted;
                int depth=Math.Max(1,tree.Depth(id));float indent=(depth-1)*OverviewListView.GroupIndent;
                bool folded=foldApplies&&_repo.State.collapsedGroups.Contains(id);
                var row=OverviewUi.Panel("Group_"+id,_content,Color.Lerp(ModConfigLook.Surface(new Color(.1f,.11f,.12f,.95f)),new Color(color.r,color.g,color.b,.95f),depth<=1?.14f:.09f));OverviewUi.Place(row,0,y,width,depth<=1?34:30);
                float railX=0;int railLevel=0;foreach(var level in group==null?new List<OverviewGroup>():tree.Chain(id)){var rail=OverviewUi.Rail("GroupRail",row,ColorUtility.TryParseHtmlString(level.color,out var lc)?lc:OverviewUi.Muted);float rw=GroupRailShape.Width(OverviewListView.RailWidth,railLevel);var (ry,rh)=GroupRailShape.Vertical(0,depth<=1?34:30,railLevel);OverviewUi.Place(rail,railX,ry,rw,rh);railX+=rw+OverviewListView.RailGap;railLevel++;}
                var arrow=OverviewUi.IconButton(row,folded?"triangle-right":"triangle-down",()=> {if(!_repo.State.collapsedGroups.Remove(id))_repo.State.collapsedGroups.Add(id);_repo.SaveUiState();Render();},22,26);OverviewUi.Place((RectTransform)arrow.transform,6+indent,depth<=1?3:2,22,26);Track(arrow,"group:"+id);
                string title=group?.name??T("Ungrouped","未分组");float size=depth<=1?20:depth==2?18:17;float nameWidth=Mathf.Min(width*.42f,Measure(title,size)+10);
                var titleText=Label(row,title,32+indent,0,nameWidth,depth<=1?32:28,color,size);OverviewUi.ReadableName(titleText,nameWidth);titleText.richText=false;Hint(titleText.gameObject,group==null?title:tree.Path(id)); titleText.gameObject.AddComponent<OverviewHotkeyTextAction>().DoubleClicked=()=>EditGroup?.Invoke(group);
                Label(row,group?.description??"",36+indent+nameWidth,0,Math.Max(0,width-nameWidth-indent-42),depth<=1?32:28,OverviewUi.Muted,14);y+=depth<=1?36:32;
            }
            foreach(var grouping in visible.GroupBy(e=>tree.Contains(e.Mod.Rule.groupId)?e.Mod.Rule.groupId:""))
            {
                string id=grouping.Key;
                // Every ancestor header first, so a subgroup is never shown without its parent; a header under a collapsed ancestor folds away.
                bool hiddenByAncestor=foldApplies&&id.Length>0&&tree.HeaderHidden(id,_repo.State.collapsedGroups);
                if(hiddenByAncestor)continue;
                foreach(var ancestor in tree.Ancestors(id))Header(ancestor.id);
                Header(id);
                if(foldApplies&&_repo.State.collapsedGroups.Contains(id))continue;
                Color color=ColorUtility.TryParseHtmlString(tree.Get(id)?.color??"",out var groupColor)?groupColor:OverviewUi.Muted;
                float memberIndent=(Math.Max(1,tree.Depth(id))-1)*OverviewListView.GroupIndent;
                foreach(var modEntries in grouping.GroupBy(e=>e.Mod.Id))
                {
                    var mod=modEntries.First().Mod;bool collapsed=Tags.Count==0&&_filter==0&&string.IsNullOrWhiteSpace(_query)&&_repo.State.collapsedHotkeyMods.Contains(mod.Id);
                    var modRow=OverviewUi.Panel("Mod_"+mod.Id,_content,ModConfigLook.Surface(new Color(.11f,.13f,.15f,.75f)));OverviewUi.Place(modRow,14+memberIndent,y,width-14-memberIndent,32);
                    var modArrow=OverviewUi.IconButton(modRow,collapsed?"triangle-right":"triangle-down",()=>{if(!_repo.State.collapsedHotkeyMods.Remove(mod.Id))_repo.State.collapsedHotkeyMods.Add(mod.Id);_repo.SaveUiState();Render();},20,24);OverviewUi.Place((RectTransform)modArrow.transform,4,4,20,24);Track(modArrow,"mod:"+mod.Id);
                    var modRail=OverviewUi.Rail("GroupRail",modRow,color);OverviewUi.Place(modRail,0,0,2,32);
                    int? number=_displayNumber(mod);float nameX=28;if(number.HasValue){var index=Label(modRow,number.Value.ToString(),28,0,40,30,OverviewUi.Muted,16);index.name="RowIndex";index.alignment=TextAlignmentOptions.MidlineLeft;nameX=68;}
                    var name=Label(modRow,OverviewLanguageBridge.DisplayNameWithOriginal(mod),nameX,0,width-54-nameX,30,Color.Lerp(color,OverviewUi.Ivory,.35f),18);OverviewUi.ReadableName(name,width-54-nameX);_dependencyHint(name.gameObject,mod);
                    bool canOpenSettings=mod.Loaded&&(settingsOwners.Contains(mod.Guid)||mod.PluginGuids.Any(settingsOwners.Contains));
                    var gear=OverviewUi.IconButton(modRow,"gear",()=>_openSettings(mod),28,28);OverviewUi.Place((RectTransform)gear.transform,width-48,2,28,28);
                    gear.interactable=canOpenSettings;
                    if(canOpenSettings)Track(gear,"settings:"+mod.Id);
                    else foreach(var icon in gear.GetComponentsInChildren<OverviewIconGraphic>())icon.color=OverviewUi.Muted;
                    Hint(gear.gameObject,canOpenSettings?T("Open mod settings","打开模组设置"):Loc.T("No ModConfig settings.","没有 ModConfig 设置项。","沒有 ModConfig 設定項。"));y+=34;
                    if(collapsed)continue;
                    foreach(var entry in modEntries)
                    {
                        const float rowIndent=28;
                        var item=OverviewUi.Panel("Key_"+entry.Id,_content,ModConfigLook.Surface(new Color(.1f,.11f,.13f,.45f)));OverviewUi.Place(item,rowIndent,y,width-rowIndent,34);
                        var label=Label(item,entry.Name,xName-rowIndent,2,xDesc-xName-6,30,OverviewUi.Ivory,16);Hint(label.gameObject,entry.Section+" → "+entry.Key);
                        var desc=TextButton(item,entry.ShortDescription,()=> {if(_authorMode||GamepadSupport.IsGamepad)EditDescription(entry);},xDesc-rowIndent,2,xCurrent-xDesc-6,30,OverviewUi.Muted);Track(desc,entry.Id+":description");Hint(desc.gameObject,entry.ShortDescription);
                        desc.gameObject.AddComponent<OverviewHotkeyTextAction>().DoubleClicked=()=>EditDescription(entry);OverviewEditableFrame.Set((RectTransform)desc.transform,_authorMode);
                        var current=KeyButton(item,entry.CurrentLabel,()=>Record(entry),xCurrent-rowIndent,3,xConflict-xCurrent-4,OverviewUi.Ivory);Track(current,entry.Id+":current");Hint(current.gameObject,entry.CurrentLabel+"\n"+T("Actual current binding. Click to record a draft binding; save hotkeys to commit.","这是实际当前按键；点击直接录制草稿，保存快捷键后提交。"));
                        current.gameObject.AddComponent<OverviewHotkeyTextAction>().ContextRequested=()=>KeyMenu(entry,(RectTransform)current.transform);

                        if(entry.Conflicts.Count>0){Button warning=null!;warning=TextButton(item,T("! Duplicate","！重复键"),()=>OpenConflicts(entry,(RectTransform)warning.transform),xConflict-rowIndent,2,xModDefault-xConflict-2,30,new Color(.95f,.65f,.35f));Track(warning,entry.Id+":conflict");warning.gameObject.AddComponent<OverviewHotkeyTextAction>().ContextRequested=()=>KeyMenu(entry,(RectTransform)warning.transform);Hint(warning.gameObject,string.Join("\n",entry.Conflicts.Select(k=>OverviewLanguageBridge.GetDisplayName(k.Mod)+" → "+k.Section+" / "+k.Key+" → "+k.Label)));}
                        var original=KeyButton(item,entry.DefaultLabel,()=>Write(()=>Service.RestoreDefault(entry,out var error)?null:error),xModDefault-rowIndent,3,keyWidth-4,OverviewUi.Green);Track(original,entry.Id+":default");
                        Hint(original.gameObject,entry.DefaultLabel+"\n"+T("Record the mod's original default in this profile draft; actual bindings stay unchanged.","将模组自带默认键记入此方案草稿，不改变实际按键。"));
                        string packLabel=entry.PackPreset?.Label??T("Not recorded","未记录");
                        var pack=KeyButton(item,packLabel,()=>RecordPack(entry),xPack-rowIndent,3,keyWidth-4,OverviewUi.Blue);
                        Track(pack,entry.Id+":pack");
                        ChangeLedger.Dot(pack.transform, ChangeLedger.Key(entry.Guid, entry.Config), _repo.ViewedProfile, () => entry.CurrentLabel);
                        ChangeLedger.Dot(current.transform, ChangeLedger.Key(entry.Guid, entry.Config), _repo.ViewedProfile, () => entry.CurrentLabel);
                        Hint(pack.gameObject,packLabel+"\n"+(entry.PackPreset==null?T("This profile has not recorded this parameter; current value is kept until explicitly included. Click to record the draft.","此方案未记录此参数，保持当前值；点击录制并纳入方案草稿。"):T("Edit the target binding, then save hotkeys to use it.","修改目标按键，点击保存快捷键后使用。")));
                        ProfilePendingMarkers.Attach(pack.transform, () => PendingState?.Invoke().Parameter(entry.Guid, entry.Config), text => ShowHint(text, (RectTransform)pack.transform, !GamepadSupport.IsGamepad), () => _hint.gameObject.SetActive(false), reserveText: false);
                        pack.gameObject.AddComponent<OverviewHotkeyTextAction>().ContextRequested=()=>PackKeyMenu(entry,(RectTransform)pack.transform);
                        y+=36;
                    }
                }
            }
            if(visible.Count==0){Label(_content,T("No matching hotkeys","没有符合条件的快捷键"),20,12,width-40,35,OverviewUi.Muted);y=60;}
            _content.sizeDelta=new Vector2(0,y);SetPixel(pixel);
            if(ownFocus){if(_targets.TryGetValue(_selectedId,out var target))EventSystem.current?.SetSelectedGameObject(target);else EventSystem.current?.SetSelectedGameObject(_targets.Values.FirstOrDefault());}
        }
        finally{_rendering=false;}
    }
    private bool Matches(OverviewHotkeyEntry e)
    {
        if (!Tags.Matches(e.Mod, StatusMatch)) return false;
        if(_filter==1&&e.Conflicts.Count==0||_filter==2&&!e.IsUnbound)return false;
        if(string.IsNullOrWhiteSpace(_query))return true;
        string text=e.Name+"\n"+e.Key+"\n"+e.Section+"\n"+OverviewLanguageBridge.GetDisplayName(e.Mod)+"\n"+e.Mod.RawName+"\n"+e.ShortDescription+"\n"+e.CurrentLabel+"\n"+e.DefaultLabel+"\n"+(e.PackPreset?.Label??"");
        return text.IndexOf(_query,StringComparison.OrdinalIgnoreCase)>=0;
    }
    private string FilterLabel()=>_filter==1?T("Conflicts","重复"):_filter==2?T("Unbound","未绑定"):T("All","全部");
    private void SetFilter(int filter){_filter=filter;Render();}
    private float Measure(string s,float size)=>_hintText.GetPreferredValues(s).x*size/Math.Max(1,_hintText.fontSize);
    private TextMeshProUGUI Label(Transform parent,string text,float x,float y,float width,float height,Color? color=null,float size=16)
    {var label=OverviewUi.Text(parent,text,size);label.alignment=TextAlignmentOptions.MidlineLeft;label.color=color??OverviewUi.Ivory;label.enableAutoSizing=true;label.fontSizeMin=Mathf.Min(size,Mathf.Clamp(height*.4f,8,12));label.fontSizeMax=size;label.overflowMode=TextOverflowModes.Ellipsis;OverviewUi.Place(label.rectTransform,x,y,Math.Max(1,width),height);return label;}
    private Button TextButton(Transform parent,string text,Action action,float x,float y,float width,float height,Color color)
    {var rect=OverviewUi.Rect("TextAction",parent);OverviewUi.Place(rect,x,y,width,height);var image=rect.gameObject.AddComponent<Image>();image.color=Color.clear;var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>action());var label=Label(rect,text,0,0,width,height,color);return button;}
    private Button KeyButton(Transform parent,string label,Action action,float x,float y,float width,Color color)
    {var button=OverviewUi.Button(parent,label,action,Math.Max(20,width),28, role: UiActionRole.Neutral);OverviewUi.Place((RectTransform)button.transform,x,y,Math.Max(20,width),28);foreach(var text in button.GetComponentsInChildren<TMP_Text>()){text.color=color;text.textWrappingMode=TextWrappingModes.NoWrap;text.overflowMode=TextOverflowModes.Ellipsis;text.enableAutoSizing=true;text.fontSizeMin=10;text.fontSizeMax=15;text.fontSize=15;text.margin=new Vector4(KeyCaptionInset,0,KeyCaptionInset,0);}return button;}
    /// <summary>2.4.2：缎带两端的缺口约占按钮宽 7%，文字左右各让出这么多，长键名（gamepad / rightTrigger）不再伸出缎带。</summary>
    private const float KeyCaptionInset = 14f;
    private void Track(Button button,string id)
    { _targets[id]=button.gameObject;button.gameObject.AddComponent<OverviewHotkeyFocus>().Selected=()=>{_selectedId=id;EnsureVisible((RectTransform)button.transform);}; }
    private void EnsureVisible(RectTransform rect)
    {if(!_opened||_rendering)return;var corners=new Vector3[4];rect.GetWorldCorners(corners);float top=-_content.InverseTransformPoint(corners[1]).y;float bottom=-_content.InverseTransformPoint(corners[0]).y;float pixel=_content.anchoredPosition.y;if(top<pixel)SetPixel(top);else if(bottom>pixel+_viewport.rect.height)SetPixel(bottom-_viewport.rect.height);}
    private void Hint(GameObject target,string text)
    {
        if(string.IsNullOrWhiteSpace(text))return;
        var anchor=(RectTransform)target.transform;
        var hover=target.AddComponent<OverviewHover>(); hover.Enter=()=>ShowHint(text,anchor,true);hover.Exit=()=>_hint.gameObject.SetActive(false);
        var focus=target.AddComponent<OverviewHotkeyFocus>();focus.Selected=()=>ShowHint(text,anchor,false);focus.Deselected=()=>_hint.gameObject.SetActive(false);
        if(target.GetComponent<Graphic>()!=null)target.GetComponent<Graphic>().raycastTarget=true;
    }
    private void ShowHint(string text,RectTransform anchor,bool followsPointer)
    {
        if(!_opened)return;_hintText.richText=text.Contains("<color=#");_hintText.text=text;_hintAnchor=anchor;_hintFollowsPointer=followsPointer;
        PositionHint();_hint.gameObject.SetActive(true);_hint.SetAsLastSibling();
    }
    private void PositionHint()
    {
        float width=Math.Min(650,Math.Max(180,_root.rect.width-30));
        float height=Math.Min(_root.rect.height*.45f,Math.Max(50,_hintText.GetPreferredValues(_hintText.text,width-20,0).y+20));
        Vector2 point;
        if(_hintFollowsPointer)
        {
            var canvas=_root.GetComponentInParent<Canvas>()?.rootCanvas;var camera=canvas!=null&&canvas.renderMode!=RenderMode.ScreenSpaceOverlay?canvas.worldCamera:null;
            Vector2 mouse=Mouse.current!=null?Mouse.current.position.ReadValue():(Vector2)Input.mousePosition;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(_root,mouse,camera,out point))return;
        }
        else {if(_hintAnchor==null)return;var corners=new Vector3[4];_hintAnchor.GetWorldCorners(corners);point=_root.InverseTransformPoint(corners[0]);}
        float x=point.x-_root.rect.xMin+16,y=_root.rect.yMax-point.y+18;
        if(y+height>_root.rect.height-5)y=_root.rect.yMax-point.y-height-12;
        OverviewUi.Place(_hint,Mathf.Clamp(x,5,Math.Max(5,_root.rect.width-width-5)),Mathf.Clamp(y,5,Math.Max(5,_root.rect.height-height-5)),width,height);
    }
    private void Write(Func<string?> operation){string? error=operation();if(!string.IsNullOrEmpty(error))_status(Loc.LocalizeMessage(error));else {_status(T("Hotkey draft edited; save hotkeys to use it.","快捷键草稿已修改，保存后使用。"));Render();_headersChanged();}}
    private void Record(OverviewHotkeyEntry entry)
    {ClosePopup();_capture.Begin(entry,value=>Write(()=>Service.TrySet(entry,value,out var error)?null:error),()=>_status(T("Recording cancelled.","已取消按键录制。")));_status(T("Press a key or shortcut; Escape cancels.","请按下按键或组合键，ESC取消。"));}
    private void EditDescription(OverviewHotkeyEntry entry)
    {OverviewUi.AskValidated(T("Hotkey description","快捷键短说明"),new[]{(T("Description (blank uses original setting description)","短说明（留空显示配置项说明）"),entry.Rule.description)},null,values=>{if(!Service.SetDescription(entry,values[0],out var error))throw new InvalidOperationException(error);Render();});}
    private void OpenConflicts(OverviewHotkeyEntry entry, RectTransform anchor)
    {
        var conflicts = entry.Conflicts.ToList();
        if (conflicts.Count == 1) { JumpTo(conflicts[0].EntryId); return; }
        Popup(anchor, conflicts.Select(c => (OverviewLanguageBridge.GetDisplayName(c.Mod) + " / " + c.Section + " / " + OverviewLanguageBridge.Translate(c.Key) + " · " + c.Label, (Action)(() => JumpTo(c.EntryId)), true)).ToList());
    }
    private void JumpTo(string entryId)
    {
        var entry = Service.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) { _status(T("That binding is no longer available. Refresh the page.", "目标快捷键已不可用，请刷新页面。")); return; }
        _query = ""; _search.SetTextWithoutNotify(""); _filter = 0;
        foreach (var level in new GroupTree(_repo.State.groups).Chain(entry.Mod.Rule.groupId)) _repo.State.collapsedGroups.Remove(level.id);
        _repo.State.collapsedGroups.Remove(entry.Mod.Rule.groupId); _repo.State.collapsedHotkeyMods.Remove(entry.Mod.Id);
        _selectedId = entryId + ":current"; _repo.SaveUiState(); Render(); Canvas.ForceUpdateCanvases();
        if (_targets.TryGetValue(_selectedId, out var target))
        {
            EnsureVisible((RectTransform)target.transform); EventSystem.current?.SetSelectedGameObject(target);
            var row = target.transform.parent; var image = row != null ? row.GetComponent<Image>() : null;
            if (image != null) { var flash = image.gameObject.GetComponent<OverviewHotkeyJumpFlash>() ?? image.gameObject.AddComponent<OverviewHotkeyJumpFlash>(); flash.Begin(image); }
        }
        _status(T("Jumped to: ", "已跳转到：") + OverviewLanguageBridge.GetDisplayName(entry.Mod) + " → " + entry.Name);
    }
    private void RecordPack(OverviewHotkeyEntry entry)
    {ClosePopup();_capture.Begin(entry,value=>Write(()=>Service.SetPackPreset(entry,value,out var error)?null:error),()=>_status(T("Recording cancelled.","已取消预设录制。")));_status(T("Record profile preset; current keys are unchanged.","请录制方案预设；当前按键保持不变。"));}
    private void PresetMenu(RectTransform anchor)
    {
        Popup(anchor,new[]{
            (T("Record all current keys in this profile","从当前按键生成方案目标"),(Action)(()=>OverviewUi.ConfirmDiscard(T("Replace this profile's key targets with current bindings?","用当前按键替换此方案的目标按键？"),()=>Write(()=>Service.CopyCurrentToPackPresets(out var count,out var error)?null:error))),true),
            (T("Forget profile key targets","清除此方案的按键记录"),(Action)(()=>OverviewUi.ConfirmDiscard(T("Forget recorded key targets? Actual keys stay unchanged.","清除已记录的按键目标？实际按键保持不变。"),()=>Write(()=>Service.ClearAllPackPresets(out var error)?null:error))),true),
            (T("Restore all duplicate warnings","恢复全部冲突提醒"),(Action)(()=>Write(()=>Service.ClearIgnored(out var error)?null:error)),true)}, new[] { UiActionRole.Commit, UiActionRole.Danger, UiActionRole.Neutral });
    }
    private void PackKeyMenu(OverviewHotkeyEntry entry,RectTransform anchor)
    {Popup(anchor,new[]{(T("Edit profile preset","调整方案预设"),(Action)(()=>RecordPack(entry)),true),(T("Include current binding","纳入当前按键"),(Action)(()=>Write(()=>Service.SetPackPreset(entry,entry.Config.BoxedValue,out var error)?null:error)),true),(T("Forget profile target","移除方案按键记录"),(Action)(()=>Write(()=>Service.ClearPack(entry,out var error)?null:error)),entry.PackPreset!=null)}, new[] { UiActionRole.Edit, UiActionRole.Commit, UiActionRole.Danger });}
    private void KeyMenu(OverviewHotkeyEntry entry,RectTransform anchor)
    {
        var items=new List<(string Label,Action Action,bool Enabled)>{
            (T("Set profile target to unbound","方案目标设为未绑定"),()=>Write(()=>Service.Clear(entry,out var error)?null:error),entry.CanClear),
            (T("Record original mod default","记录原模组默认"),()=>Write(()=>Service.RestoreDefault(entry,out var error)?null:error),true)};
        foreach(var conflict in entry.Conflicts.ToList()){var c=conflict;items.Add((T("Ignore: ","忽略与：")+OverviewLanguageBridge.GetDisplayName(c.Mod)+" / "+c.Key,()=>Write(()=>Service.IgnoreConflict(entry,c.EntryId,out var error)?null:error),true));}
        if(entry.IgnoredCount>0)items.Add((T("Restore ignored warnings","恢复已忽略冲突提醒"),()=>Write(()=>Service.RestoreIgnoredFor(entry,out var error)?null:error),true));
        Popup(anchor,items);
    }
    private void Popup(RectTransform anchor,IReadOnlyList<(string Label,Action Action,bool Enabled)> items, UiActionRole[]? roles = null)
    {
        if(_capture.IsCapturing)return;ClosePopup();_popupReturn=EventSystem.current?.currentSelectedGameObject;
        if (items.Count <= 16)
        {
            var entries = items.Select((item, index) => new UiMenuEntry { Label = item.Label, Enabled = item.Enabled,
                Role = roles != null && index < roles.Length ? roles[index] : UiActionRole.Edit, Invoke = item.Action }).ToArray();
            OverviewUi.CompactDropdown(anchor, "", entries, _root); return;
        }
        var shade=OverviewUi.Rect("HotkeyDropdown",_root);OverviewUi.Fill(shade);_popup=shade.gameObject;shade.gameObject.AddComponent<Image>().color=Color.clear;var outside=shade.gameObject.AddComponent<Button>();outside.navigation=new Navigation{mode=Navigation.Mode.None};outside.onClick.AddListener(ClosePopup);
        float menuWidth=Mathf.Min(Mathf.Max(245,items.Select(i=>Measure(i.Label,14)+32).DefaultIfEmpty(245).Max()),Mathf.Max(100,_root.rect.width-16));
        float contentHeight=Math.Max(33,items.Count*39-6),menuHeight=Mathf.Min(contentHeight+14,Mathf.Max(60,_root.rect.height-12));
        var panel=OverviewUi.Panel("Menu",shade,OverviewUi.Brown);var corners=new Vector3[4];anchor.GetWorldCorners(corners);var point=_root.InverseTransformPoint(corners[0]);float x=Mathf.Clamp(point.x-_root.rect.xMin,0,Math.Max(0,_root.rect.width-menuWidth));float y=Mathf.Clamp(_root.rect.yMax-point.y+4,0,Math.Max(0,_root.rect.height-menuHeight));OverviewUi.Place(panel,x,y,menuWidth,menuHeight);
        var viewport=OverviewUi.Rect("Viewport",panel);OverviewUi.Fill(viewport,7);viewport.gameObject.AddComponent<RectMask2D>();
        var content=OverviewUi.Rect("Choices",viewport);OverviewUi.Place(content,0,0,menuWidth-14,contentHeight);
        var scroll=panel.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.scrollSensitivity=40;scroll.movementType=ScrollRect.MovementType.Clamped;
        var enabled=new List<Button>();
        for(int i=0;i<items.Count;i++)
        {
            var item=items[i];float top=i*39;var button=OverviewUi.Button(content,item.Label,()=>{ClosePopup();item.Action();},menuWidth-16,33, role: roles != null && i < roles.Length ? roles[i] : UiActionRole.Edit);OverviewUi.Place((RectTransform)button.transform,1,top,menuWidth-16,33);button.interactable=item.Enabled;
            foreach(var text in button.GetComponentsInChildren<TMP_Text>()){text.fontSize=14;text.overflowMode=TextOverflowModes.Ellipsis;}
            button.gameObject.AddComponent<OverviewHotkeyFocus>().Selected=()=>{float pixel=content.anchoredPosition.y,visible=menuHeight-14;if(top<pixel)pixel=top;else if(top+33>pixel+visible)pixel=top+33-visible;scroll.StopMovement();content.anchoredPosition=new Vector2(0,Mathf.Clamp(pixel,0,Math.Max(0,contentHeight-visible)));};
            if(item.Enabled)enabled.Add(button);
        }
        for(int i=0;i<enabled.Count;i++)enabled[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=i>0?enabled[i-1]:null,selectOnDown=i+1<enabled.Count?enabled[i+1]:null};
        if(enabled.Count>0)EventSystem.current?.SetSelectedGameObject(enabled[0].gameObject);
    }
    private void ClosePopup(){_root.GetComponent<WindowPopover>()?.Close();if(_popup==null)return;_popup.SetActive(false);UnityEngine.Object.Destroy(_popup);_popup=null;if(_popupReturn!=null&&_popupReturn.activeInHierarchy)EventSystem.current?.SetSelectedGameObject(_popupReturn);_popupReturn=null;}
    public void Dispose(){if(_disposed)return;OnClosed();_disposed=true;Service.Changed-=OnChanged;Service.Dispose();_pump.Tick=null;UnityEngine.Object.Destroy(_root.gameObject);}
}
internal sealed class OverviewHotkeyFocus : MonoBehaviour,ISelectHandler,IDeselectHandler
{internal Action? Selected,Deselected;public void OnSelect(BaseEventData data)=>Selected?.Invoke();public void OnDeselect(BaseEventData data)=>Deselected?.Invoke();}
internal sealed class OverviewHotkeyJumpFlash : MonoBehaviour
{
    private Image? _image; private Color _original; private float _until;
    internal void Begin(Image image) { if (_image == null) _original = image.color; _image = image; _until = Time.unscaledTime + 1.2f; image.color = ModConfigLook.Surface(new Color(.15f,.31f,.38f,.85f)); }
    private void Update() { if (_image == null || Time.unscaledTime < _until) return; _image.color = _original; UnityEngine.Object.Destroy(this); }
}
internal sealed class OverviewHotkeyTextAction : MonoBehaviour,IPointerClickHandler
{
    internal Action? DoubleClicked,ContextRequested;
    public void OnPointerClick(PointerEventData data){if(data.button==PointerEventData.InputButton.Right)ContextRequested?.Invoke();else if(data.clickCount>=2)DoubleClicked?.Invoke();}
    private void Update(){if(EventSystem.current?.currentSelectedGameObject==gameObject&&Gamepad.current?.buttonNorth.wasPressedThisFrame==true)ContextRequested?.Invoke();}
}
