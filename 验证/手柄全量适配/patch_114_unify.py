# -*- coding: utf-8 -*-
"""1.1.4 (user: list page and keybindings page behave differently on the controller): one scheme on both pages.
- LT + RB / LT + LB: expand / collapse all (already on both);
- RT layer: RT+↑↓ previous / next group, RT+←→ page, RT+L3 fold all (now also on the keybindings page);
- L3: fold the focused group (keybindings: the group of the focused row);
- prompt bar shows "RT Hold for more" and "L3 Fold group" on both pages, and the RT list while RT is held."""
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

edit('Overview/OverviewHotkeyView.cs', [
('''        if (!_comboRecording && GamepadSupport.IsGamepad && !OverviewUi.HasModal && _popup == null && !_capture.IsCapturing && !_search.isFocused) FoldAllShortcut();''',
 '''        if (!_comboRecording && GamepadSupport.IsGamepad && !OverviewUi.HasModal && _popup == null && !_capture.IsCapturing && !_search.isFocused) { FoldAllShortcut(); ListGestures(); }'''),
('''    /// <summary>LT + RB expands all, LT + LB collapses all (same on the mod list).</summary>''',
 '''    /// <summary>1.1.4: the same controller gestures as the mod list. RT layer: ↑↓ group, ←→ page, L3 fold all;
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
    /// <summary>LT + RB expands all, LT + LB collapses all (same on the mod list).</summary>'''),
])

edit('Overview/OverviewPage.Gamepad.cs', [
('''        string where = parts[0], kind = parts[1]; bool layer = parts[2] == "True" && where == PageTab.Mods.ToString();''',
 '''        string where = parts[0], kind = parts[1];
        bool listLike = where == PageTab.Mods.ToString() || where == PageTab.Hotkeys.ToString(); // 1.1.4: one scheme
        bool layer = parts[2] == "True" && listLike;'''),
('''            items.Add(rt + Glyph("buttonNorth") + " " + Loc.T("Group menu", "分组菜单", "分組選單"));
            items.Add(rt + Glyph("leftStickPress") + " " + Loc.T("Fold all groups", "折叠全部分组", "摺疊全部分組"));''',
 '''            if (where == PageTab.Mods.ToString()) items.Add(rt + Glyph("buttonNorth") + " " + Loc.T("Group menu", "分组菜单", "分組選單"));
            items.Add(rt + Glyph("leftStickPress") + " " + Loc.T("Fold all groups", "折叠全部分组", "摺疊全部分組"));'''),
('''        if (where == PageTab.Mods.ToString()) Add("rightTrigger", Loc.T("Hold for more", "按住看更多", "按住看更多"));
        if (where == PageTab.Mods.ToString()) Add("leftStickPress", Loc.T("Fold group", "折叠分组", "摺疊分組"));''',
 '''        if (listLike) Add("rightTrigger", Loc.T("Hold for more", "按住看更多", "按住看更多"));
        if (listLike) Add("leftStickPress", Loc.T("Fold group", "折叠分组", "摺疊分組"));'''),
])
print('ok')
