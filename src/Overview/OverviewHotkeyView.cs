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
    internal Func<Selectable, Selectable?>? ToolbarAbove;
    public OverviewHotkeyService Service { get; }
    public Action? ManageGroups { get; set; }
    public Action<OverviewGroup?>? EditGroup { get; set; }
    public bool IsRecording => _capture.IsCapturing || _comboRecording;
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
    public void OnClosed() { if (_comboRecording) EndComboRecording(); GamepadIntent.Release(_root.gameObject); _cascade.PauseForExit(); var selected = EventSystem.current?.currentSelectedGameObject; if (selected != null) { var pair = _targets.FirstOrDefault(p => p.Value == selected); if (pair.Key != null) _selectedId = pair.Key; } _opened = false; _capture.CancelCapture(); ClosePopup(); _hint.gameObject.SetActive(false); }
    private IEnumerable<OverviewCascade.Row> AppearanceRows()
    {
        foreach (Transform child in _content)
            if (child is RectTransform rect) yield return new OverviewCascade.Row(rect);
    }
    public bool TryCloseLayer()
    {
        if (_capture.CancelCapture()) { _status(T("Recording cancelled.", "已取消按键录制。")); return true; }
        if (_comboRecording) return true; // 1.1.4: B is a recordable button; hold B to cancel (TickComboRecording)
        if (_popup != null) { ClosePopup(); return true; }
        return false;
    }
    private void Tick()
    {
        if (!_opened || _disposed) return;
        if (_hint.gameObject.activeSelf && _hintFollowsPointer) PositionHint();
        // 1.1.4 (user): the two right columns (mod default / profile preset) follow the input device; the keyboard and
        // controller columns themselves stay put.
        if (_padDefaults != GamepadSupport.IsGamepad && !_capture.IsCapturing && !_comboRecording) { _padDefaults = GamepadSupport.IsGamepad; _dirty = true; }
        if (_comboRecording) TickComboRecording();
        if (!_comboRecording && GamepadSupport.IsGamepad && !OverviewUi.HasModal && _popup == null && !_capture.IsCapturing && !_search.isFocused) { FoldAllShortcut(); ListGestures(); }
        if (!_rendering && (_dirty || Math.Abs(_root.rect.width - _width) > 2 || _revision != OverviewLanguageBridge.Revision)) { _dirty = false; Render(); }
        // 1.1.0: the right stick only scrolls; the game also binds it to UI navigation, so navigation pauses while it moves.
        bool free = !OverviewUi.HasModal && _popup == null && !_capture.IsCapturing && !_comboRecording && !_search.isFocused;
        GamepadIntent.HoldNavigation(_root.gameObject, free && GamepadIntent.WantsHold);
        if (free && GamepadSupport.IsGamepad && Gamepad.current is Gamepad pad && EventSystem.current is EventSystem system
            && (system.currentSelectedGameObject == null || !system.currentSelectedGameObject.activeInHierarchy)
            && (pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame || GamepadIntent.PressedDirection() != MoveDirection.None))
            FocusForGamepad(); // focus lost (mouse, back from settings): the next press puts it back
        if (free && GamepadSupport.IsGamepad && Gamepad.current?.rightStickButton.wasPressedThisFrame == true && _search.gameObject.activeInHierarchy)
            EventSystem.current?.SetSelectedGameObject(_search.gameObject);
        if (free && GamepadIntent.ScrollY != 0)
        { _scroll.StopMovement(); SetPixel(_content.anchoredPosition.y - GamepadIntent.ScrollY * GamepadIntent.ScrollSpeed(600) * Time.unscaledDeltaTime); }
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
            // 1.1.1 (user): the search box used to fill the whole row; same width as the mod list's search box.
            float searchWidth = Mathf.Clamp(width*.32f,220,420); OverviewUi.Place((RectTransform)_search.transform,0,0,searchWidth,32);
            GamepadSupport.MarkSelectable(_search.gameObject,null,null);
            _placeholder.text=T("Search name, mod, description or key", "搜索名称、模组、说明或按键");
            Button filter=OverviewUi.Button(_tools,FilterLabel(),()=>Popup((RectTransform)_tools.transform,new[]{(T("All","全部"),(Action)(()=>SetFilter(0)),true),(T("Conflicts","重复"),(Action)(()=>SetFilter(1)),true),(T("Unbound","未绑定"),(Action)(()=>SetFilter(2)),true) }),96,30, role: UiActionRole.Edit);OverviewUi.Place((RectTransform)filter.transform,searchWidth+6,1,96,30);
            float toolX=searchWidth+108;
            if(_authorMode){Button adjust=null!;adjust=OverviewUi.Button(_tools,T("Preset options","预设调整"),()=>PresetMenu((RectTransform)adjust.transform),102,30, role: UiActionRole.Edit);OverviewUi.Place((RectTransform)adjust.transform,toolX,1,102,30);toolX+=108;}
            var save=OverviewUi.Button(_tools,T("Save hotkeys","保存快捷键"),Save,114,30, role: UiActionRole.Commit);OverviewUi.Place((RectTransform)save.transform,toolX,1,114,30);toolX+=120;
            Button modifierButton=null!;
            modifierButton=OverviewUi.Button(_tools,Loc.T("Modifier: ","修饰键：","修飾鍵：")+GamepadCombos.ButtonName(GamepadCombos.Modifier),()=>ModifierMenu((RectTransform)modifierButton.transform),150,30,role:UiActionRole.Edit);
            StylePad(modifierButton);
            Hint(modifierButton.gameObject,Loc.T("When recording a controller key, hold this button to record \"modifier + key\"; without it, a single button is recorded. There is one modifier for all combos: changing it here changes every recorded combo. Default: Back.","录制手柄键时按住它，就记成「修饰键 + 键」；不按就记成单键。所有组合共用一个修饰键：在这里改，已录好的组合会一起改。默认 Back。","錄製手把鍵時按住它，就記成「修飾鍵 + 鍵」；不按就記成單鍵。所有組合共用一個修飾鍵：在這裡改，已錄好的組合會一起改。預設 Back。"));
            bool anyFolded=_repo.State.groups.Any(g=>_repo.State.collapsedGroups.Contains(g.id))||Service.Entries.Select(e=>e.Mod.Id).Distinct().Any(m=>_repo.State.collapsedHotkeyMods.Contains(m));
            bool anyOpen=!anyFolded; // the click expands when anything is folded, otherwise collapses
            var foldAll=OverviewUi.IconButton(_tools,anyFolded?"triangle-right":"triangle-down",()=>ToggleAllFolds(),30,30);
            StylePad(foldAll);
            Hint(foldAll.gameObject,(anyOpen?Loc.T("Collapse all groups","收起全部分组","收起全部分組"):Loc.T("Expand all groups","展开全部分组","展開全部分組"))+"\n"+Loc.T("Controller: LT + RB expands all, LT + LB collapses all.","手柄：LT + RB 全部展开，LT + LB 全部收起。","手把：LT + RB 全部展開，LT + LB 全部收起。"));
            float xName=48, nameColumn=Mathf.Clamp(width*.18f,70,190), conflictWidth=Mathf.Clamp(width*.07f,32,64);
            // Budget all three binding columns together, leaving a real description column on narrow pages.
            float keyLimit=Mathf.Max(52,Mathf.Min(136,(width-xName-nameColumn-conflictWidth-Mathf.Min(120,width*.18f))/3));
            float KeyWidth(IEnumerable<string> labels) => labels.Select(s => Measure(s,15)+16).DefaultIfEmpty(74).Max();
            float keyWidth=Mathf.Min(keyLimit,Mathf.Max(74,Mathf.Max(
                KeyWidth(Service.Entries.Select(e=>e.CurrentLabel).Concat(Service.Entries.Select(e=>e.DefaultLabel)).Concat(Service.Entries.Select(e=>e.PackPreset?.Label??""))),
                new[]{T("Current key","当前按键"),T("Mod default","模组默认"),T("Profile preset","方案预设")}.Max(s=>Measure(s,16))+12)));
            float currentWidth=keyWidth,modDefaultWidth=keyWidth,packWidth=keyWidth;
            float padWidth=keyWidth;
            float xDesc=xName+nameColumn,xCurrent=width-packWidth-modDefaultWidth-conflictWidth-currentWidth-padWidth,xPad=xCurrent+currentWidth,xConflict=xPad+padWidth,xModDefault=xConflict+conflictWidth,xPack=xModDefault+modDefaultWidth;
            Label(_header,T("Setting","配置项名"),xName,1,xDesc-xName-6,30);
            Label(_header,T("Description","短说明"),xDesc,1,xCurrent-xDesc-6,30);
            var currentTitle=Label(_header,Loc.T("Keyboard","键盘按键","鍵盤按鍵"),xCurrent,1,keyWidth-4,30);currentTitle.alignment=TextAlignmentOptions.Midline;
            var padTitle=Label(_header,Loc.T("Controller","手柄按键","手把按鍵"),xPad,1,keyWidth-4,30,PadAccent);padTitle.alignment=TextAlignmentOptions.Midline;
            // 1.1.4 (user): the modifier picker and expand/collapse-all sit right above the controller column, in its colour.
            OverviewUi.Place((RectTransform)modifierButton.transform,Mathf.Max(toolX,xPad-rowIndentTop()),1,Mathf.Max(110,keyWidth-38),30);
            OverviewUi.Place((RectTransform)foldAll.transform,Mathf.Max(toolX,xPad-rowIndentTop())+Mathf.Max(110,keyWidth-38)+4,1,30,30);
            Label(_header,"!",xConflict,1,xModDefault-xConflict-2,30);
            var defaultTitle=Label(_header,T("Mod default","模组默认"),xModDefault,1,keyWidth-4,30,OverviewUi.Green);defaultTitle.alignment=TextAlignmentOptions.Midline;
            var packTitle=Label(_header,T("Profile preset","方案预设"),xPack,1,keyWidth-4,30,OverviewUi.Blue);packTitle.alignment=TextAlignmentOptions.Midline;
            foreach (var columnTitle in new[]{currentTitle,padTitle,defaultTitle,packTitle})
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
                        bool native=GamepadCombos.NativeGamepad(entry.Config);
                        // Keyboard column: the keyboard key (empty for controller-only settings).
                        var current=KeyButton(item,native?"":entry.CurrentLabel,()=>Record(entry),xCurrent-rowIndent,3,xPad-xCurrent-4,OverviewUi.Ivory);
                        KeyChipsOn(current,native?"":RawKey(entry));
                        Track(current,entry.Id+":current");
                        Hint(current.gameObject,(native?Loc.T("This setting is a controller binding; see the controller column.","这是手柄按键设置，见手柄列。","這是手把按鍵設定，見手把欄。"):entry.CurrentLabel)+"\n"+T("Actual current binding. Click to record a draft binding; save hotkeys to commit.","这是实际当前按键；点击直接录制草稿，保存快捷键后提交。"));
                        // Controller column: the mod's own controller key, or the combo standing in for its keyboard key.
                        var combo=GamepadCombos.For(entry.Id);
                        // 1.1.4 (user): A on the controller cell always records a controller key or combo. A single button on a
                        // controller-native setting is written into the mod's own setting; a combo (or any key on a keyboard-only
                        // setting) is kept by the manager, which presses the mod's key for it in game.
                        var pad=KeyButton(item,"",()=>RecordCombo(entry),xPad-rowIndent,3,keyWidth-4,OverviewUi.Ivory);
                        StylePad(pad);
                        if(combo!=null)KeyChipsOn(pad,combo);
                        else KeyChipsOn(pad,native?RawKey(entry):"");
                        Track(pad,entry.Id+":pad");
                        Hint(pad.gameObject,ComboHint(entry));
                        pad.gameObject.AddComponent<OverviewHotkeyTextAction>().ContextRequested=()=>KeyMenu(entry,(RectTransform)pad.transform);
                        current.gameObject.AddComponent<OverviewHotkeyTextAction>().ContextRequested=()=>KeyMenu(entry,(RectTransform)current.transform);

                        bool padConflict=native?entry.Conflicts.Count>0:entry.Conflicts.Any(c=>c.Combo), keyConflict=!native&&entry.Conflicts.Any(c=>!c.Combo);
                        string conflictText=padConflict&&keyConflict?Loc.T("← Duplicate →","← 重复键 →","← 重複鍵 →"):padConflict?Loc.T("← Duplicate","← 重复键","← 重複鍵"):Loc.T("Duplicate →","重复键 →","重複鍵 →");
                        if(entry.Conflicts.Count>0){Button warning=null!;warning=TextButton(item,conflictText,()=>OpenConflicts(entry,(RectTransform)warning.transform),xConflict-rowIndent,2,xModDefault-xConflict-2,30,new Color(.95f,.65f,.35f));Track(warning,entry.Id+":conflict");warning.gameObject.AddComponent<OverviewHotkeyTextAction>().ContextRequested=()=>KeyMenu(entry,(RectTransform)warning.transform);Hint(warning.gameObject,string.Join("\n",entry.Conflicts.Select(k=>OverviewLanguageBridge.GetDisplayName(k.Mod)+" → "+k.Section+" / "+k.Key+" → "+k.Label)));}
                        bool showPad=_padDefaults;
                        string defaultRaw=native?Convert.ToString(entry.Config.DefaultValue)??"":"";
                        var original=KeyButton(item,showPad?"":entry.DefaultLabel,()=>{if(showPad){if(native)WriteNow(entry,()=>Service.RestoreDefault(entry,out var error)?null:error);else{GamepadCombos.Clear(entry.Id);Service.RefreshConflicts();Render();}}else WriteNow(entry,()=>Service.RestoreDefault(entry,out var error)?null:error);},xModDefault-rowIndent,3,keyWidth-4,OverviewUi.Green);Track(original,entry.Id+":default");
                        if(showPad){StylePad(original);KeyChipsOn(original,native&&!OverviewHotkeyAdapter.IsNone(defaultRaw)?defaultRaw:"");}
                        else KeyChipsOn(original,RawOf(entry.Config.DefaultValue));
                        Hint(original.gameObject,entry.DefaultLabel+"\n"+T("Record the mod's original default in this profile draft; actual bindings stay unchanged.","将模组自带默认键记入此方案草稿，不改变实际按键。"));
                        string packLabel=entry.PackPreset?.Label??T("Not recorded","未记录");
                        var packCombo=GamepadCombos.PresetFor(_repo.State,entry.Id);
                        var pack=KeyButton(item,showPad?"":packLabel,()=>{if(showPad)RecordComboPreset(entry);else RecordPack(entry);},xPack-rowIndent,3,keyWidth-4,OverviewUi.Blue);
                        if(showPad){StylePad(pack);if(packCombo!=null)KeyChipsOn(pack,packCombo);else KeyChipsOn(pack,"");}
                        else{string raw="";if(entry.PackPreset!=null&&!entry.PackPreset.cleared)try{raw=RawOf(entry.Adapter.Deserialize(entry.PackPreset.value));}catch{raw="";}KeyChipsOn(pack,raw);}
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
            LinkToolbarNavigation(save);
            // 1.1.4 (user): moving down on the name column did not scroll; every focusable cell keeps itself in view.
            foreach(var selectable in _content.GetComponentsInChildren<Selectable>(false))
                if(selectable.GetComponent<ScrollFollow>()==null){var follow=selectable.gameObject.AddComponent<ScrollFollow>();var rect=(RectTransform)selectable.transform;follow.Selected=()=>EnsureVisible(rect);}
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
    /// <summary>1.1.1: the tool row (search, filter, preset, save) is one explicit left/right line and ↓ always goes into
    /// the list. Automatic navigation used to loop the search box onto itself, trapping the focus there.</summary>
    private void LinkToolbarNavigation(Button save)
    {
        var tools=_tools.GetComponentsInChildren<Selectable>(false).Where(s=>s.transform.parent==_tools&&s.IsActive()).OrderBy(s=>((RectTransform)s.transform).anchoredPosition.x).ToList();
        Selectable? first=_targets.TryGetValue(_selectedId,out var remembered)&&remembered!=null&&remembered.activeInHierarchy?remembered.GetComponent<Selectable>():null;
        first??=_targets.Values.Select(go=>go!=null?go.GetComponent<Selectable>():null).FirstOrDefault(s=>s!=null&&s.IsActive());
        for(int i=0;i<tools.Count;i++)
            tools[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=i>0?tools[i-1]:null,selectOnRight=i+1<tools.Count?tools[i+1]:null,selectOnDown=first,selectOnUp=ToolbarAbove?.Invoke(tools[i])};
    }
    /// <summary>Gamepad entry into this tab: the remembered binding, else the first one.</summary>
    internal void FocusForGamepad()
    {
        if(!GamepadSupport.IsGamepad)return;
        var target=_targets.TryGetValue(_selectedId,out var remembered)&&remembered!=null&&remembered.activeInHierarchy?remembered:_targets.Values.FirstOrDefault(go=>go!=null&&go.activeInHierarchy);
        if(target!=null)EventSystem.current?.SetSelectedGameObject(target);
    }
    private void Track(Button button,string id)
    { _targets[id]=button.gameObject;button.gameObject.AddComponent<OverviewHotkeyFocus>().Selected=()=>{_selectedId=id;EnsureVisible((RectTransform)button.transform);};
      GamepadSupport.MarkSelectable(button.gameObject,null,null); } // 1.1.2: this page's buttons never had the gamepad focus ring
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
    /// <summary>1.1.4 (user): the keybindings page has no draft step. Recording writes the key and commits the hotkey
    /// scope at once, so the mod uses it immediately.</summary>
    private void WriteNow(OverviewHotkeyEntry entry,Func<string?> operation,string suffix="")
    {
        string before=entry.Config.GetSerializedValue();
        string? error=operation();
        // 1.1.5 diagnosis (user: resetting one key changed another): which entry this write targeted.
        Plugin.Log.LogInfo("[快捷键] 写入 "+entry.Guid+" / ["+entry.Section+"] "+entry.Key+"："+before+" → 草稿 "+(_repo.DraftConfig.values.FirstOrDefault(v=>v.Identity==_repo.ConfigIdentity(entry.Guid,entry.Config))?.serializedValue??"（无）")+(string.IsNullOrEmpty(error)?"":" · 失败："+error));
        if(!string.IsNullOrEmpty(error)){_status(Loc.LocalizeMessage(error)+suffix);return;}
        try
        {
            // 1.1.5 (user: "resetting the fly modifier changed the fly toggle key"): commit only this hotkey. The hotkey
            // scope committed every hotkey value held in the profile draft, so a stale value of another key was written too.
            ConfigurationSaveRequest.Run(_repo,_mods(),ConfigurationSaveScope.HotkeyEntry,_repo.ConfigIdentity(entry.Guid,entry.Config),result=>{
                _status((result.Success?Loc.F("Saved: {0} / {1} = {2}.","已保存：{0} / {1} = {2}。","已儲存：{0} / {1} = {2}。",OverviewLanguageBridge.GetDisplayName(entry.Mod),entry.Name,entry.CurrentLabel):Loc.LocalizeMessage(result.Message))+suffix);
                Render();_headersChanged();});
        }
        catch(Exception ex){_status(Loc.LocalizeMessage(ex.Message)+suffix);Render();}
    }
    private bool _comboRecording, _padDefaults;
    private OverviewHotkeyEntry? _comboEntry;
    private float _comboStarted;
    /// <summary>Draws key chips on a key button (keyboard label or controller path).</summary>
    private void KeyChipsOn(Button button, string label)
    {
        var text=button.GetComponentInChildren<TMP_Text>(true); if(text==null)return;
        var host=(RectTransform)text.transform.parent; text.text="";
        var keys=new List<(string,bool)>();
        foreach(var part in label.Split('+').Select(x=>x.Trim()).Where(x=>x.Length>0)) keys.Add(KeyChips.Describe(part));
        var chipHost=OverviewUi.Rect("KeyChips",host);OverviewUi.Fill(chipHost);
        // 1.1.4 (user): no ribbon on key cells; an empty cell is a muted "Not set" chip. The cell itself (fixed rect, the
        // old ribbon's size) keeps the click target and the focus ring.
        if(keys.Count==0)KeyChips.Build(chipHost,new List<(string,bool)>{(Loc.T("Not set","未设置","未設定"),false)},text.font,24,centre:true,fontSize:14,muted:true);
        else KeyChips.Build(chipHost,keys,text.font,24,dimFirst:false,centre:true,fontSize:15);
        Frameless(button);
    }
    /// <summary>1.1.4 (user): a cell showing key chips drops the ribbon frame behind them; the cell stays clickable and
    /// focusable (transparent, not disabled, so it still takes the pointer and shows the focus ring).</summary>
    private static void Frameless(Button button)
    {
        var style=button.GetComponent<UiActionStyle>();if(style!=null){style.enabled=false;UnityEngine.Object.Destroy(style);}
        // 1.1.4 (user: "still a faint ribbon - just delete it"): every image of the ribbon (shadow, panel, borders, glow) is
        // switched off; a clear image on the cell rect keeps the click target and the focus ring the same size as before.
        foreach(var image in button.GetComponentsInChildren<Image>(true))if(image.gameObject.name!="MCE_KeyHit")image.enabled=false;
        if(button.transform.Find("MCE_KeyHit")==null){var hit=OverviewUi.Rect("MCE_KeyHit",button.transform);OverviewUi.Fill(hit);hit.SetAsFirstSibling();var img=hit.gameObject.AddComponent<Image>();img.color=Color.clear;img.raycastTarget=true;button.targetGraphic=img;}
        button.transition=Selectable.Transition.None;
    }
    private void KeyChipsOn(Button button, GamepadCombos.Item combo)
    {
        var text=button.GetComponentInChildren<TMP_Text>(true); if(text==null)return;
        var host=(RectTransform)text.transform.parent; text.text="";
        var keys=new List<(string,bool)>();
        string mod=combo.Mod.Length>0&&GamepadCombos.Modifier.Length>0?GamepadCombos.Modifier:combo.Mod; // one shared modifier
        if(mod.Length>0)keys.Add((GamepadCombos.ButtonName(mod),true));
        keys.Add((GamepadCombos.ButtonName(combo.button),true));
        var chipHost=OverviewUi.Rect("KeyChips",host);OverviewUi.Fill(chipHost);
        KeyChips.Build(chipHost,keys,text.font,24,dimFirst:mod.Length>0,centre:true,fontSize:15);
        Frameless(button);
    }
    /// <summary>The stored value as text ("&lt;Gamepad&gt;/buttonSouth", "LeftControl + F5", "South"), for key chips.</summary>
    private static string RawKey(OverviewHotkeyEntry entry)=>RawOf(entry.Config.BoxedValue);
    private static string RawOf(object? value)
    {
        if(value is BepInEx.Configuration.KeyboardShortcut shortcut)return shortcut.MainKey==KeyCode.None?"":string.Join(" + ",shortcut.Modifiers.Select(m=>m.ToString()).Append(shortcut.MainKey.ToString()));
        string text=Convert.ToString(value)??"";
        return OverviewHotkeyAdapter.IsNone(text)?"":text;
    }
    private static float rowIndentTop()=>0f;
    /// <summary>Controller-only colour: a teal-violet tint, distinct from the neutral key buttons and the action roles.</summary>
    internal static readonly Color PadAccent=new(.55f,.82f,.86f,1f), PadMuted=new(.55f,.82f,.86f,.55f);
    private static readonly Color PadFace=new(.13f,.27f,.33f,1f);
    private static void StylePad(Button button)
    {
        var peak=button.GetComponent<PEAKLib.UI.Elements.PeakMenuButton>();
        if(peak!=null&&peak.Panel!=null)peak.Panel.color=PadFace;
        foreach(var text in button.GetComponentsInChildren<TMP_Text>(true))text.color=PadAccent;
        foreach(var icon in button.GetComponentsInChildren<OverviewIconGraphic>(true))icon.color=PadAccent;
        var style=button.GetComponent<UiActionStyle>(); if(style!=null)UnityEngine.Object.Destroy(style); // keep our colour, not the role colour
    }
    private void ModifierMenu(RectTransform anchor)
    {
        Popup(anchor,GamepadCombos.Modifiers.Select(m=>((m.Length==0?Loc.T("None (single buttons)","无（只用单键）","無（只用單鍵）"):GamepadCombos.ButtonName(m))+(m==GamepadCombos.Modifier?"  ✓":""),
            (Action)(()=>{GamepadCombos.Modifier=m;Service.RefreshConflicts();_status(Loc.F("Controller modifier: {0}","手柄修饰键：{0}","手把修飾鍵：{0}",GamepadCombos.ButtonName(m)));Render();}),true)).ToList());
    }
    /// <summary>Expand everything if anything is folded, otherwise fold everything (groups and mod rows).</summary>
    private void ToggleAllFolds(bool? expand=null)
    {
        var groups=_repo.State.groups.Select(g=>g.id).ToList(); var mods=Service.Entries.Select(e=>e.Mod.Id).Distinct().ToList();
        bool open=expand??(groups.Any(_repo.State.collapsedGroups.Contains)||mods.Any(_repo.State.collapsedHotkeyMods.Contains));
        _repo.State.collapsedGroups.Clear(); _repo.State.collapsedHotkeyMods.Clear();
        if(!open){_repo.State.collapsedGroups.AddRange(groups);_repo.State.collapsedHotkeyMods.AddRange(mods);}
        _repo.SaveUiState(); Render();
        _status(open?Loc.T("All groups expanded.","已展开全部分组。","已展開全部分組。"):Loc.T("All groups collapsed.","已收起全部分组。","已收起全部分組。"));
    }
    /// <summary>1.1.4: the same controller gestures as the mod list. RT layer: ↑↓ group, ←→ page, L3 fold all;
    /// without RT: L3 folds the focused row's group.</summary>
    private void ListGestures()
    {
        var pad=Gamepad.current; if(pad==null)return;
        var selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
        if(GamepadIntent.Layer)
        {
            switch(GamepadIntent.PressedDirection())
            {
                case MoveDirection.Up: JumpGroup(selected,-1); break;
                case MoveDirection.Down: JumpGroup(selected,1); break;
                case MoveDirection.Left: PageBy(-1); break;
                case MoveDirection.Right: PageBy(1); break;
            }
            if(pad.leftStickButton.wasPressedThisFrame)ToggleAllFolds();
            return;
        }
        if(pad.leftStickButton.wasPressedThisFrame&&selected!=null)FoldGroupOf(selected);
    }
    /// <summary>The group id of a focused control (its group header, or the group its mod belongs to).</summary>
    private string GroupOf(GameObject selected)
    {
        var pair=_targets.FirstOrDefault(p=>p.Value==selected); string key=pair.Key??"";
        if(key.StartsWith("group:",StringComparison.Ordinal))return key.Substring(6);
        var tree=new GroupTree(_repo.State.groups);
        foreach(var entry in Service.Entries)
            if(key==entry.Id+":current"||key==entry.Id+":pad"||key==entry.Id+":default"||key==entry.Id+":pack"||key==entry.Id+":conflict"||key==entry.Id+":description"||key=="settings:"+entry.Mod.Id)
                return tree.Contains(entry.Mod.Rule.groupId)?entry.Mod.Rule.groupId:"";
        return "";
    }
    private void FoldGroupOf(GameObject selected)
    {
        string id=GroupOf(selected); if(id.Length==0)return;
        if(!_repo.State.collapsedGroups.Remove(id))_repo.State.collapsedGroups.Add(id);
        _repo.SaveUiState(); _selectedId="group:"+id; Render();
        if(_targets.TryGetValue("group:"+id,out var header))EventSystem.current?.SetSelectedGameObject(header);
    }
    private void JumpGroup(GameObject? from,int step)
    {
        var headers=_targets.Where(p=>p.Key.StartsWith("group:",StringComparison.Ordinal)&&p.Value!=null&&p.Value.activeInHierarchy)
            .Select(p=>p.Value).OrderByDescending(g=>g.transform.position.y).ToList();
        if(headers.Count==0)return;
        float y=from!=null?from.transform.position.y:float.MaxValue;
        var target=step>0?headers.FirstOrDefault(h=>h.transform.position.y<y-1f):headers.LastOrDefault(h=>h.transform.position.y>y+1f);
        if(target!=null){EventSystem.current?.SetSelectedGameObject(target);EnsureVisible((RectTransform)target.transform);}
    }
    private void PageBy(int step)
    {
        float view=_viewport.rect.height; SetPixel(_content.anchoredPosition.y+step*view*.9f);
        // focus follows to the first focusable cell now on screen
        var corners=new Vector3[4];_viewport.GetWorldCorners(corners);float top=corners[1].y,bottom=corners[0].y;
        var first=_targets.Values.Where(g=>g!=null&&g.activeInHierarchy&&g.transform.position.y<top&&g.transform.position.y>bottom).OrderByDescending(g=>g.transform.position.y).FirstOrDefault();
        if(first!=null)EventSystem.current?.SetSelectedGameObject(first);
    }
    /// <summary>LT + RB expands all, LT + LB collapses all (same on the mod list).</summary>
    private void FoldAllShortcut()
    {
        var pad=Gamepad.current; if(pad==null||pad.leftTrigger.ReadValue()<=.5f)return;
        if(pad.rightShoulder.wasPressedThisFrame)ToggleAllFolds(true);
        else if(pad.leftShoulder.wasPressedThisFrame)ToggleAllFolds(false);
    }
    private string ComboHint(OverviewHotkeyEntry entry)
    {
        // 1.1.4 (user, exact wording): formal and short; the modifier shown is the one actually set.
        var combo=GamepadCombos.For(entry.Id);
        string modifier=GamepadCombos.Modifier.Length>0?GamepadCombos.ButtonName(GamepadCombos.Modifier):Loc.T("not set","未分配","未分配");
        string head=combo!=null
            ? Loc.F("Controller hotkey: {0}","已分配的手柄快捷键：{0}","已分配的手把快捷鍵：{0}",GamepadCombos.Label(combo))
            : Loc.T("This mod has no preset controller hotkey.","该模组未分配预设的手柄快捷键。","該模組未分配預設的手把快捷鍵。");
        return head+"\n\n"
            +Loc.T("Press the controller's A button to assign a hotkey.","按下手柄的 A 键分配快捷键。","按下手把的 A 鍵分配快捷鍵。")+"\n"
            +Loc.F("* Use \"{0}\" + another button as a combo to avoid clashing with the game's default buttons.","*建议使用「{0}」+其他按键组合为组合键，以避免和游戏默认按键冲突。","*建議使用「{0}」+其他按鍵組合為組合鍵，以避免和遊戲預設按鍵衝突。",modifier);
    }
    /// <summary>A on the controller cell: wait for LT + one button (B / Menu cancels).</summary>
    private void RecordCombo(OverviewHotkeyEntry entry)
    {
        if(!GamepadSupport.IsGamepad){_comboToPreset=false;_status(Loc.T("Controller keys are set with a controller.","手柄快捷键需使用手柄设置。","手把快捷鍵需使用手把設定。"));return;}
        ClosePopup(); _comboRecording=true; _comboEntry=entry; _comboStarted=Time.unscaledTime; _bDownAt=-1; _take.Clear();
        GamepadIntent.ReleaseAll(); var system=EventSystem.current; _navigationBefore=system==null||system.sendNavigationEvents; if(system!=null)system.sendNavigationEvents=false;
        GamepadLock.Begin(_root.gameObject); ComboRecordingActive=true; // the A that started recording is already down: it never joins the take
        _status(GamepadCombos.Modifier.Length>0
            ? Loc.F("Recording (controller locked): press a button, or hold \"{0}\" first for a combo. Hold B for 1 second to exit.","录制中（手柄已锁定）：按下按键，或先按住「{0}」录制组合键。长按 B 1 秒退出。","錄製中（手把已鎖定）：按下按鍵，或先按住「{0}」錄製組合鍵。長按 B 1 秒退出。",GamepadCombos.ButtonName(GamepadCombos.Modifier))
            : Loc.T("Recording (controller locked): press a button. Hold B for 1 second to exit.","录制中（手柄已锁定）：按下按键。长按 B 1 秒退出。","錄製中（手把已鎖定）：按下按鍵。長按 B 1 秒退出。"));
        HoldRing.Show(_root,0f);
    }
    private void TickComboRecording()
    {
        var pad=Gamepad.current; var entry=_comboEntry;
        if(entry==null)return;
        // Keyboard Esc still cancels (the controller is locked and cannot reach the page).
        if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame){OverviewHotkeyCapture.MarkConsumed();EndComboRecording();_status(Loc.T("Recording cancelled.","已取消录制。","已取消錄製。"));return;}
        if(pad==null||!GamepadSupport.IsGamepad){EndComboRecording();_status(Loc.T("Recording stopped: the controller is no longer the input device.","录制已停止：当前输入设备已不是手柄。","錄製已停止：目前輸入裝置已不是手把。"));return;}
        if(OverviewUi.HasModal){EndComboRecording();_status(Loc.T("Recording stopped: a dialog opened.","录制已停止：有对话框弹出。","錄製已停止：有對話框彈出。"));return;}
        string modifier=GamepadCombos.Modifier;
        bool native=GamepadCombos.NativeGamepad(entry.Config);
        // 1.1.5 (user: "the lock is unreliable"): while recording, GamepadLock rewrites every controller event to
        // "nothing pressed" before the input system sees it, so the game, other mods and this mod's own pages read no
        // controller at all. Only the raw buttons below are read. Every button that goes down joins this take; when all
        // of them are up again the take is judged as a whole.
        _pressBuffer.Clear(); GamepadLock.DrainPresses(_pressBuffer);
        foreach(var name in _pressBuffer)
            if(!_take.Contains(name)){_take.Add(name);if(name=="buttonEast"&&_take.Count==1)_bDownAt=Time.unscaledTime;}
        bool anyHeld=false;
        foreach(var name in _take)if(GamepadLock.Pressed(name))anyHeld=true;
        // Hold B alone for 1 s = leave the lock (the ring shows it). B together with any other button never cancels.
        bool bAlone=_take.Count==1&&_take[0]=="buttonEast"&&GamepadLock.Pressed("buttonEast")&&_bDownAt>=0;
        HoldRing.Show(_root,bAlone?Mathf.Clamp01((Time.unscaledTime-_bDownAt)/1f):-1f);
        if(bAlone&&Time.unscaledTime-_bDownAt>=1f){_take.Clear();_bDownAt=-1;EndComboRecording();_status(Loc.T("Recording finished.","录制已结束。","錄製已結束。"));return;}
        if(_take.Count==0||anyHeld)return;
        var take=_take.ToList(); _take.Clear(); _bDownAt=-1;
        string used, button;
        if(take.Count==1&&take[0]!=modifier){used="";button=take[0];}
        else if(take.Count==2&&modifier.Length>0&&take.Contains(modifier)){used=modifier;button=take.First(b=>b!=modifier);}
        else
        {
            // Not a key this page can hold: say why and stay locked.
            string name=modifier.Length>0?GamepadCombos.ButtonName(modifier):"";
            _status(take.Count==1
                ? Loc.F("\"{0}\" is the modifier; press it together with another button. Hold B for 1 second to exit.","「{0}」是修饰键，需与其他按键组合。长按 B 1 秒退出。","「{0}」是修飾鍵，需與其他按鍵組合。長按 B 1 秒退出。",name)
                : modifier.Length>0
                    ? Loc.F("A combo is \"{0}\" + one button. Not recorded; recording continues. Hold B for 1 second to exit.","组合键为「{0}」+ 一个按键，本次未录入，录制继续。长按 B 1 秒退出。","組合鍵為「{0}」+ 一個按鍵，本次未錄入，錄製繼續。長按 B 1 秒退出。",name)
                    : Loc.T("No modifier is set; press one button. Not recorded; recording continues. Hold B for 1 second to exit.","未设置修饰键，请只按一个按键。本次未录入，录制继续。长按 B 1 秒退出。","未設定修飾鍵，請只按一個按鍵。本次未錄入，錄製繼續。長按 B 1 秒退出。"));
            return;
        }
        {
            if(native&&used.Length==0&&!_comboToPreset)
            {
                // A single button for a controller-native setting goes into the mod's own setting (written at once).
                object? value=GamepadCombos.NativeValue(entry.Config,button);
                if(value==null){_status(Loc.T("This mod does not accept that button. Recording continues; hold B for 1 second to finish.","该模组不接受这个键。录制继续，长按 B 1 秒结束。","該模組不接受這個鍵。錄製繼續，長按 B 1 秒結束。"));return;}
                GamepadCombos.Clear(entry.Id);
                WriteNow(entry,()=>Service.TrySet(entry,value,out var error)?null:error,StillRecording); return;
            }
            if(_comboToPreset)
            {
                GamepadCombos.SetPreset(_repo.State,entry.Id,entry.Guid,entry.Section,entry.Key,used,button); _repo.Save();
                _status(Loc.F("Recorded in this profile: {0}.","已记入此方案：{0}。","已記入此方案：{0}。",(used.Length>0?GamepadCombos.ButtonName(used)+" + ":"")+GamepadCombos.ButtonName(button))+StillRecording); Render(); return;
            }
            string? moved=GamepadCombos.Set(entry.Id,entry.Guid,entry.Section,entry.Key,used,button);
            Service.RefreshConflicts();
            var other=moved==null?null:Service.Entries.FirstOrDefault(e=>e.Id==moved);
            var item=GamepadCombos.For(entry.Id);
            _status(Loc.F("{0} now presses this hotkey.","{0} 现在会按下这个快捷键。","{0} 現在會按下這個快捷鍵。",item!=null?GamepadCombos.Label(item):GamepadCombos.ButtonName(button))
                +(other!=null?" "+Loc.F("(moved from {0})","（原来分给了 {0}）","（原來分給了 {0}）",OverviewLanguageBridge.GetDisplayName(other.Mod)+" / "+other.Name):"")+StillRecording);
            Render();return;
        }
    }
    /// <summary>Records a controller combo into the profile (exported with packs), not into this PC's live combos.</summary>
    private void RecordComboPreset(OverviewHotkeyEntry entry){_comboToPreset=true;RecordCombo(entry);}
    private float _bDownAt=-1;
    /// <summary>1.1.5 (user: "locked until a 1 s B hold, nothing else"): appended to every message while still recording.</summary>
    private static string StillRecording=>" "+Loc.T("Still recording: press another button to replace it, hold B for 1 second to finish.","仍在录制：再按其他键可覆盖，长按 B 1 秒结束。","仍在錄製：再按其他鍵可覆蓋，長按 B 1 秒結束。");
    /// <summary>Buttons pressed in the current take (in press order); judged when all are released.</summary>
    private readonly List<string> _take=new(), _pressBuffer=new();
    private bool _comboToPreset;
    /// <summary>Any controller recording in progress (read by row actions such as Y = key menu).</summary>
    internal static bool ComboRecordingActive;
    private void EndComboRecording(){_comboRecording=false;_comboEntry=null;_comboToPreset=false;_take.Clear();GamepadLock.End();ComboRecordingActive=false;HoldRing.Hide(_root);var system=EventSystem.current;if(system!=null)system.sendNavigationEvents=_navigationBefore;}
    private bool _navigationBefore=true;
    private void Record(OverviewHotkeyEntry entry)
    {if(GamepadSupport.IsGamepad){_status(Loc.T("Keyboard keys are set with keyboard and mouse.","键盘快捷键需使用键鼠设置。","鍵盤快捷鍵需使用鍵鼠設定。"));return;}ClosePopup();_capture.Begin(entry,value=>WriteNow(entry,()=>Service.TrySet(entry,value,out var error)?null:error),()=>_status(T("Recording cancelled.","已取消按键录制。")));_status(T("Press a key or shortcut; Escape cancels.","请按下按键或组合键，ESC取消。"));}
    private void EditDescription(OverviewHotkeyEntry entry)
    {OverviewUi.AskValidated(T("Hotkey description","快捷键短说明"),new[]{(T("Description (blank uses original setting description)","短说明（留空显示配置项说明）"),entry.Rule.description)},null,values=>{if(!Service.SetDescription(entry,values[0],out var error))throw new InvalidOperationException(error);Render();});}
    private void OpenConflicts(OverviewHotkeyEntry entry, RectTransform anchor)
    {
        var conflicts = entry.Conflicts.ToList();
        if (conflicts.Count == 1 && !conflicts[0].IsGame) { JumpTo(conflicts[0].EntryId); return; }
        Popup(anchor, conflicts.Select(c => c.IsGame
            ? (Loc.T("Game: ", "游戏：", "遊戲：") + c.Key + " · " + c.Label, (Action)(() => _status(Loc.T("This key is also a game control; pressing it triggers both.", "这个键也是游戏自己的操作，按下时两边都会触发。", "這個鍵也是遊戲自己的操作，按下時兩邊都會觸發。"))), true)
            : (OverviewLanguageBridge.GetDisplayName(c.Mod) + " / " + c.Section + " / " + OverviewLanguageBridge.Translate(c.Key) + " · " + c.Label, (Action)(() => JumpTo(c.EntryId)), true)).ToList());
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
    {if(GamepadSupport.IsGamepad){_status(Loc.T("Keyboard keys are set with keyboard and mouse.","键盘快捷键需使用键鼠设置。","鍵盤快捷鍵需使用鍵鼠設定。"));return;}ClosePopup();_capture.Begin(entry,value=>Write(()=>Service.SetPackPreset(entry,value,out var error)?null:error),()=>_status(T("Recording cancelled.","已取消预设录制。")));_status(T("Record profile preset; current keys are unchanged.","请录制方案预设；当前按键保持不变。"));}
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
        // 1.1.4 (user screenshot): reset to mod default / reset to profile preset / ignore ↔ … / set current as preset.
        // Every item writes at once like recording does (the keybindings page has no draft step).
        var preset=entry.PackPreset;object? presetValue=null;
        if(preset!=null&&!preset.cleared)try{presetValue=entry.Adapter.Deserialize(preset.value);}catch{presetValue=null;}
        var items=new List<(string Label,Action Action,bool Enabled)>{
            (Loc.T("Reset to the \"Mod default\" key","重设为「模组默认」预设的快捷键","重設為「模組預設」的快捷鍵"),()=>WriteNow(entry,()=>Service.RestoreDefault(entry,out var error)?null:error),true),
            (Loc.T("Reset to the key set in the current \"Profile preset\"","重设为当前「方案预设」中设定的快捷键","重設為目前「方案預設」中設定的快捷鍵"),()=>{if(presetValue!=null)WriteNow(entry,()=>Service.TrySet(entry,presetValue,out var error)?null:error);},presetValue!=null)};
        foreach(var conflict in entry.Conflicts.Where(k=>!k.IsGame&&!k.Combo).ToList()){var c=conflict;items.Add((Loc.T("Ignore conflict ←→ ","忽略冲突 ←→ ","忽略衝突 ←→ ")+OverviewLanguageBridge.GetDisplayName(c.Mod)+" / "+c.Key,()=>{string error;if(Service.IgnoreConflict(entry,c.EntryId,out error)){_status(Loc.T("Conflict ignored.","已忽略这条冲突。","已忽略這條衝突。"));Render();_headersChanged();}else _status(Loc.LocalizeMessage(error));},true));}
        if(entry.IgnoredCount>0)items.Add((T("Restore ignored warnings","恢复已忽略冲突提醒"),()=>{string error;if(Service.RestoreIgnoredFor(entry,out error)){Render();_headersChanged();}else _status(Loc.LocalizeMessage(error));},true));
        if(GamepadCombos.For(entry.Id)!=null)items.Add((Loc.T("Remove controller combo","移除手柄组合键","移除手把組合鍵"),()=>{GamepadCombos.Clear(entry.Id);Service.RefreshConflicts();_status(Loc.T("Controller combo removed.","已移除手柄组合键。","已移除手把組合鍵。"));Render();},true));
        items.Add((Loc.T("Set the current key as the current \"Profile preset\"","将「当前快捷键」设为当前的「方案预设」","將「目前快捷鍵」設為目前的「方案預設」"),()=>WriteNow(entry,()=>Service.SetPackPreset(entry,entry.Config.BoxedValue,out var error)?null:error),true));
        Popup(anchor,items);
    }
    private void Popup(RectTransform anchor,IReadOnlyList<(string Label,Action Action,bool Enabled)> items, UiActionRole[]? roles = null)
    {
        if(_capture.IsCapturing||_comboRecording)return; // 1.1.5 review: no menu while recording
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
/// <summary>1.1.4: keeps a focused cell scrolled into view (separate from OverviewHotkeyFocus, whose handlers get replaced).</summary>
internal sealed class ScrollFollow : MonoBehaviour,ISelectHandler{internal Action? Selected;public void OnSelect(BaseEventData data)=>Selected?.Invoke();}
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
    private void Update(){if(OverviewHotkeyView.ComboRecordingActive||GamepadLock.Active)return;if(EventSystem.current?.currentSelectedGameObject==gameObject&&Gamepad.current?.buttonNorth.wasPressedThisFrame==true)ContextRequested?.Invoke();}
}
