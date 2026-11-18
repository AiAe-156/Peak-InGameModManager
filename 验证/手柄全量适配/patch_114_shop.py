# -*- coding: utf-8 -*-
"""1.1.4 shop gamepad (review): focus survives every Draw() (logical key per control), explicit navigation between the
toolbar / filter column / card grid / pager / detail area, focus ring + scroll follow on everything, B backs out one
layer at a time (popup → zoomed image → details → store), popups take the focus and block the page behind them, and the
document area is a focusable reading surface (up/down scrolls, A or right opens the page's link menu)."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', 'src'))
def edit(name, pairs):
    p = os.path.join(SRC, name)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (name, a[:90], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('Overview/ShopWindow.cs', [
# B handler + gamepad wiring after Build
('''view._installationState.DependencyUninstallReason=SourceUi.UninstallReason;view.Build();''',
 '''view._installationState.DependencyUninstallReason=SourceUi.UninstallReason;view.Build();
        OverviewUi.SetModalCloseHandler(()=>{if(view==null||!view.BackOneLayer())OverviewUi.CloseModal();});'''),
# Draw: remember focus before clearing, rewire after building
('''    private void Draw()
    {
        ClearBody();if(_selected!=null){Details();return;}''', '''    private void Draw()
    {
        RememberFocus();
        try{DrawBody();}finally{WireGamepad();}
    }
    private void DrawBody()
    {
        ClearBody();if(_selected!=null){Details();return;}'''),
# key the main controls: filters, cards, pager, detail tabs, versions
('''        Button Filter(string label,Action action){var button=Button(filters,label,action,8,y,left-36,UiActionRole.Edit);y+=38;return button;}''',
 '''        int filterIndex=0;
        Button Filter(string label,Action action){var button=Button(filters,label,action,8,y,left-36,UiActionRole.Edit);y+=38;Key(button,"filter:"+filterIndex++);return button;}'''),
('''var row=FlatListRow.Create(cards,()=>{_selected=item;_version=item.Version;_tab="intro";_markdownKey="";Draw();},cardWidth,cardHeight,ModConfigLook.Surface(new Color(.12f,.16f,.17f,1)));OverviewUi.Place((RectTransform)row.transform,x,top,cardWidth,cardHeight);''',
 '''var row=FlatListRow.Create(cards,()=>{_cardFocus=item.Key;_selected=item;_version=item.Version;_tab="intro";_markdownKey="";Draw();},cardWidth,cardHeight,ModConfigLook.Surface(new Color(.12f,.16f,.17f,1)));OverviewUi.Place((RectTransform)row.transform,x,top,cardWidth,cardHeight);Key(row,"card:"+item.Key);'''),
('''        Button(_body,T("Previous","上一页"),()=>{_page--;Draw();},left+14,h-36,110);Button(_body,T("Next","下一页"),()=>{_page++;Draw();},left+134,h-36,110);''',
 '''        Key(Button(_body,T("Previous","上一页"),()=>{_page--;Draw();},left+14,h-36,110),"page:prev");Key(Button(_body,T("Next","下一页"),()=>{_page++;Draw();},left+134,h-36,110),"page:next");'''),
('''        foreach(var tab in tabs){var local=tab;Button(_body,tab.Item2,()=>{_tab=local.Item1;_markdownDrawn="";Draw();},i++*140,44,130,UiActionRole.Edit);}''',
 '''        foreach(var tab in tabs){var local=tab;var tabButton=Button(_body,tab.Item2,()=>{_tab=local.Item1;_markdownDrawn="";Draw();},i++*140,44,130,UiActionRole.Edit);Key(tabButton,"tab:"+local.Item1);UiActionStyle.Select(tabButton,local.Item1==_tab);}'''),
('''Button(rows,v.Version+" · "+v.Date.ToString("yyyy-MM-dd")+" · "+(v.Bytes/1048576d).ToString("0.00")+" MB",()=>{_version=version.Version;_markdownKey="";Draw();},8,y,w-40,UiActionRole.Edit);y+=40;''',
 '''Key(Button(rows,v.Version+" · "+v.Date.ToString("yyyy-MM-dd")+" · "+(v.Bytes/1048576d).ToString("0.00")+" MB",()=>{_version=version.Version;_markdownKey="";Draw();},8,y,w-40,UiActionRole.Edit),"version:"+v.Version);y+=40;'''),
# reader: a focusable reading surface over the document
('''            else if(_markdown?.IsCompletedSuccessfully==true){_reader=new IntroductionEditorView(content,_document=new ShopMarkdownSource(_markdown.Result),OverviewUi.Text);_reader.Reload();_reader.OnOpened();}''',
 '''            else if(_markdown?.IsCompletedSuccessfully==true){_reader=new IntroductionEditorView(content,_document=new ShopMarkdownSource(_markdown.Result),OverviewUi.Text);_reader.Reload();_reader.OnOpened();AddReadingSurface(content);}'''),
# install button keyed
('''        _uninstallButton=Button(_body,T("Uninstall","卸载"),()=>Preview(true),192,h-40,150,UiActionRole.Danger);''',
 '''        Key(_installButton,"action:install");
        _uninstallButton=Button(_body,T("Uninstall","卸载"),()=>Preview(true),192,h-40,150,UiActionRole.Danger);Key(_uninstallButton,"action:uninstall");'''),
# popups: track the top one (B closes it, focus moves into it)
('''        var popup=OverviewUi.Panel("InstallGroupList",_panel,ModConfigLook.Surface(new Color(.10f,.14f,.15f,1)));''',
 '''        var popup=OverviewUi.Panel("InstallGroupList",_panel,ModConfigLook.Surface(new Color(.10f,.14f,.15f,1)));Popup(popup);'''),
('''        var popup=OverviewUi.Panel("NewInstallGroup",_panel,ModConfigLook.Surface(new Color(.12f,.16f,.17f,1)));''',
 '''        var popup=OverviewUi.Panel("NewInstallGroup",_panel,ModConfigLook.Surface(new Color(.12f,.16f,.17f,1)));Popup(popup);'''),
('''        var popup=OverviewUi.Panel("UninstallFinalConfirm",_panel,ModConfigLook.Surface(new Color(.15f,.10f,.10f,1)));''',
 '''        var popup=OverviewUi.Panel("UninstallFinalConfirm",_panel,ModConfigLook.Surface(new Color(.15f,.10f,.10f,1)));Popup(popup,safeLast:true);'''),
('''            var popup=OverviewUi.Panel("InfrastructureVersions",_panel,ModConfigLook.Surface(new Color(.10f,.14f,.15f,1)));''',
 '''            var popup=OverviewUi.Panel("InfrastructureVersions",_panel,ModConfigLook.Surface(new Color(.10f,.14f,.15f,1)));Popup(popup);'''),
('''()=>{var popup=OverviewUi.Panel("UninstallDetails",_panel,ModConfigLook.Surface(new Color(.12f,.14f,.15f,1)));''',
 '''()=>{var popup=OverviewUi.Panel("UninstallDetails",_panel,ModConfigLook.Surface(new Color(.12f,.14f,.15f,1)));Popup(popup);'''),
# per-frame: popup lifecycle + focus recovery
('''    private void Update()
    {
        if(InventoryContext!=_inventoryContext)''', '''    private void Update()
    {
        GamepadFrame();
        if(InventoryContext!=_inventoryContext)'''),
# helpers
('''    private void OnDestroy(){_cancel.Cancel();_reader?.Dispose();_cancel.Dispose();}''',
 '''    private void OnDestroy(){_cancel.Cancel();_reader?.Dispose();_cancel.Dispose();}

    // ── 1.1.4 gamepad ─────────────────────────────────────────────────────────
    private readonly Dictionary<GameObject,string> _keys=new();
    private string _focusKey="",_cardFocus="";
    private RectTransform? _popup;private GameObject? _popupOpener;private CanvasGroup? _bodyGroup;
    private Selectable? _readingSurface;
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
        var body=_body.GetComponentsInChildren<Selectable>(false).Where(s=>s.IsActive()&&s.IsInteractable()&&s.navigation.mode!=Navigation.Mode.None).ToList();
        if(body.Count==0&&top.Count==0)yield break;
        // Grid-aware links: nearest control in the pressed direction by screen position (rows / columns / areas alike).
        var all=top.Concat(body).ToList();
        foreach(var control in all)
        {
            var nav=new Navigation{mode=Navigation.Mode.Explicit};
            nav.selectOnUp=Nearest(control,all,Vector2.up);nav.selectOnDown=Nearest(control,all,Vector2.down);
            nav.selectOnLeft=Nearest(control,all,Vector2.left);nav.selectOnRight=Nearest(control,all,Vector2.right);
            if(control==_readingSurface)continue; // the reading surface handles its own moves
            control.navigation=nav;
        }
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
            if(!VisibleInScroll(c))continue;
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
        _popup=popup;
        var group=popup.gameObject.AddComponent<CanvasGroup>();group.ignoreParentGroups=true;
        if(_bodyGroup==null)_bodyGroup=_panel.GetComponent<CanvasGroup>()??_panel.gameObject.AddComponent<CanvasGroup>();
        _bodyGroup.interactable=false;
        StartCoroutine(FocusPopup(popup,safeLast));
    }
    private System.Collections.IEnumerator FocusPopup(RectTransform popup,bool safeLast)
    {
        yield return null;if(popup==null)yield break;
        foreach(var input in popup.GetComponentsInChildren<TMP_InputField>(true)){if(input.readOnly)input.shouldActivateOnSelect=false;else GamepadSupport.MarkSelectable(input.gameObject,null,null);}
        PublicationNavigation.Attach(popup);
        yield return null;if(popup==null||!GamepadSupport.IsGamepad)yield break;
        var buttons=popup.GetComponentsInChildren<Button>(false).Where(b=>b.IsInteractable()).ToList();
        var pick=safeLast?buttons.LastOrDefault():buttons.FirstOrDefault();
        if(pick!=null)EventSystem.current?.SetSelectedGameObject(pick.gameObject);
    }
    private void GamepadFrame()
    {
        if(_popup!=null&&(_popup==null||!_popup.gameObject.activeInHierarchy))ClosePopupState();
        else if(_popup==null&&_bodyGroup!=null&&!_bodyGroup.interactable)ClosePopupState();
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
        _popup=null;if(_bodyGroup!=null)_bodyGroup.interactable=true;
        var opener=_popupOpener;_popupOpener=null;
        if(opener!=null&&opener.activeInHierarchy&&GamepadSupport.IsGamepad)EventSystem.current?.SetSelectedGameObject(opener);
    }
    /// <summary>B: popup → zoomed image → details back to the list → (false) close the store.</summary>
    internal bool BackOneLayer()
    {
        if(WindowPopover.TryCloseTop())return true;
        if(_popup!=null){Destroy(_popup.gameObject);ClosePopupState();return true;}
        if(_reader!=null&&_reader.TryCloseLayer())return true;
        if(_showingTask){_showingTask=false;_requestedTask=false;Draw();return true;}
        if(_selected!=null){_focusKey="card:"+_selected.Key;_cardFocus=_selected.Key;_selected=null;_tab="intro";Draw();return true;}
        return false;
    }
    /// <summary>The document is drawn on a canvas the controller cannot enter. A reading surface over it: up/down scrolls,
    /// A or right opens this page's links, left goes back to the tabs.</summary>
    private void AddReadingSurface(RectTransform content)
    {
        var scroll=content.GetComponentInChildren<ScrollRect>(true);if(scroll==null)return;
        var host=OverviewUi.Rect("MCE_ShopReading",content);OverviewUi.Fill(host);host.SetAsLastSibling();
        var image=host.gameObject.AddComponent<Image>();image.color=Color.clear;image.raycastTarget=false; // pointer goes to the document
        var surface=host.gameObject.AddComponent<HelpReadingSurface>();surface.Scroll=scroll;surface.targetGraphic=image;surface.transition=Selectable.Transition.None;
        surface.MovePage=delta=>{if(scroll.content==null)return;var p=scroll.content.anchoredPosition;float max=Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height);p.y=Mathf.Clamp(p.y+delta,0,max);scroll.content.anchoredPosition=p;};
        surface.Links=ShowDocumentLinks;
        surface.Leave=()=>{var tab=_keys.FirstOrDefault(p=>p.Value=="tab:"+_tab).Key;if(tab!=null)EventSystem.current?.SetSelectedGameObject(tab);};
        GamepadSupport.MarkSelectable(host.gameObject,null,null);
        _readingSurface=surface;Key(surface,"reader");
    }
    private void ShowDocumentLinks()
    {
        if(_document==null||_markdown==null||!_markdown.IsCompletedSuccessfully)return;
        var links=new List<(string Label,string Value)>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(_markdown.Result,@"\\[([^\\]]*)\\]\\((https?://[^)\\s]+)\\)"))
            if(seen.Add(m.Groups[2].Value))links.Add(((m.Groups[1].Value.Length>0?m.Groups[1].Value:m.Groups[2].Value)+Loc.T(" (external)","（外部）","（外部）"),m.Groups[2].Value));
        foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(_markdown.Result,@"(?<![\\(\\[""])https?://[^\\s)\\]>""]+"))
            if(seen.Add(m.Value))links.Add((m.Value+Loc.T(" (external)","（外部）","（外部）"),m.Value));
        if(links.Count==0){_status.text=T("No links on this page.","本页没有链接。");return;}
        OverviewUi.Choose(T("Links on this page","本页链接"),links,"",url=>Application.OpenURL(url));
    }'''),
])
# usings
p = os.path.join(SRC, 'Overview', 'ShopWindow.cs'); s = io.open(p, encoding='utf-8').read()
for u in ['using UnityEngine.EventSystems;', 'using System.Linq;', 'using System.Collections.Generic;']:
    if u not in s: s = s.replace('using UnityEngine;', 'using UnityEngine;\n' + u, 1)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
