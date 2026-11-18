# -*- coding: utf-8 -*-
"""1.1.3 keybindings page: permanent controller column, key chips, modifier dropdown, split duplicate markers,
expand/collapse all (button + LT+RB / LT+LB)."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
p = os.path.join(ROOT, 'src', 'Overview', 'OverviewHotkeyView.cs')
s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'

def rep(a, b):
    global s
    a = a.replace('\n', nl); b = b.replace('\n', nl)
    assert s.count(a) == 1, (a[:90], s.count(a))
    s = s.replace(a, b)

# 1. no more device flip
rep('''        // 1.1.2 (user): like the game's own key page, controller input shows controller keys and keyboard/mouse input
        // switches straight back.
        if (_gamepadView != GamepadSupport.IsGamepad && !_capture.IsCapturing && !_comboRecording) { _gamepadView = GamepadSupport.IsGamepad; _dirty = true; }
        if (_comboRecording) TickComboRecording();''',
'''        // 1.1.3 (user): the controller column is permanent, so nothing flips with the input device any more.
        if (_comboRecording) TickComboRecording();
        if (!_comboRecording && GamepadSupport.IsGamepad && !OverviewUi.HasModal && _popup == null && !_capture.IsCapturing && !_search.isFocused) FoldAllShortcut();''')

# 2. toolbar: modifier dropdown + expand/collapse all
rep('''            var save=OverviewUi.Button(_tools,T("Save hotkeys","保存快捷键"),Save,114,30, role: UiActionRole.Commit);OverviewUi.Place((RectTransform)save.transform,toolX,1,114,30);''',
'''            var save=OverviewUi.Button(_tools,T("Save hotkeys","保存快捷键"),Save,114,30, role: UiActionRole.Commit);OverviewUi.Place((RectTransform)save.transform,toolX,1,114,30);toolX+=120;
            Button modifierButton=null!;
            modifierButton=OverviewUi.Button(_tools,Loc.T("Controller modifier: ","手柄修饰键：","手把修飾鍵：")+GamepadCombos.ButtonName(GamepadCombos.Modifier),()=>ModifierMenu((RectTransform)modifierButton.transform),190,30,role:UiActionRole.Edit);
            OverviewUi.Place((RectTransform)modifierButton.transform,toolX,1,190,30);toolX+=196;
            Hint(modifierButton.gameObject,Loc.T("Optional. When recording a controller key, hold this button to record \\"modifier + key\\"; without it, a single button is recorded. Only one modifier at a time; combos already recorded keep theirs.","可选。录制手柄键时按住它，就记成「修饰键 + 键」；不按就记成单键。同一时间只有一个修饰键，已录好的组合不受影响。","可選。錄製手把鍵時按住它，就記成「修飾鍵 + 鍵」；不按就記成單鍵。同一時間只有一個修飾鍵，已錄好的組合不受影響。"));
            bool anyOpen=_repo.State.groups.Any(g=>!_repo.State.collapsedGroups.Contains(g.id))||Service.Entries.Select(e=>e.Mod.Id).Distinct().Any(m=>!_repo.State.collapsedHotkeyMods.Contains(m));
            var foldAll=OverviewUi.IconButton(_tools,anyOpen?"triangle-down":"triangle-right",()=>ToggleAllFolds(),30,30);
            OverviewUi.Place((RectTransform)foldAll.transform,toolX,1,30,30);
            Hint(foldAll.gameObject,(anyOpen?Loc.T("Collapse all groups","收起全部分组","收起全部分組"):Loc.T("Expand all groups","展开全部分组","展開全部分組"))+"\\n"+Loc.T("Controller: LT + RB expands all, LT + LB collapses all.","手柄：LT + RB 全部展开，LT + LB 全部收起。","手把：LT + RB 全部展開，LT + LB 全部收起。"));'''),

# 3. header title: permanent controller column between current and conflict
rep('''            float xDesc=xName+nameColumn,xCurrent=width-packWidth-modDefaultWidth-conflictWidth-currentWidth,xConflict=xCurrent+currentWidth,xModDefault=xConflict+conflictWidth,xPack=xModDefault+modDefaultWidth;''',
'''            float padWidth=keyWidth;
            float xDesc=xName+nameColumn,xCurrent=width-packWidth-modDefaultWidth-conflictWidth-currentWidth-padWidth,xPad=xCurrent+currentWidth,xConflict=xPad+padWidth,xModDefault=xConflict+conflictWidth,xPack=xModDefault+modDefaultWidth;'''),
rep('''            var currentTitle=Label(_header,_gamepadView?Loc.T("Controller","手柄按键","手把按鍵"):T("Current key","当前按键"),xCurrent,1,keyWidth-4,30);currentTitle.alignment=TextAlignmentOptions.Midline;''',
'''            var currentTitle=Label(_header,Loc.T("Keyboard","键盘按键","鍵盤按鍵"),xCurrent,1,keyWidth-4,30);currentTitle.alignment=TextAlignmentOptions.Midline;
            var padTitle=Label(_header,Loc.T("Controller","手柄按键","手把按鍵"),xPad,1,keyWidth-4,30);padTitle.alignment=TextAlignmentOptions.Midline;'''),
rep('''            foreach (var columnTitle in new[]{currentTitle,defaultTitle,packTitle})''', '''            foreach (var columnTitle in new[]{currentTitle,padTitle,defaultTitle,packTitle})'''),

# 4. cells: keyboard column always keyboard; controller column always present
rep('''                        var current=_gamepadView&&!GamepadCombos.NativeGamepad(entry.Config)
                            ? KeyButton(item,ComboLabel(entry),()=>RecordCombo(entry),xCurrent-rowIndent,3,xConflict-xCurrent-4,GamepadCombos.For(entry.Id)!=null?OverviewUi.Ivory:OverviewUi.Muted)
                            : KeyButton(item,entry.CurrentLabel,()=>Record(entry),xCurrent-rowIndent,3,xConflict-xCurrent-4,OverviewUi.Ivory);
                        Track(current,entry.Id+":current");
                        Hint(current.gameObject,_gamepadView&&!GamepadCombos.NativeGamepad(entry.Config)?ComboHint(entry):entry.CurrentLabel+"\\n"+T("Actual current binding. Click to record a draft binding; save hotkeys to commit.","这是实际当前按键；点击直接录制草稿，保存快捷键后提交。"));''',
'''                        bool native=GamepadCombos.NativeGamepad(entry.Config);
                        // Keyboard column: the keyboard key (empty for controller-only settings).
                        var current=KeyButton(item,native?"":entry.CurrentLabel,()=>{if(native)Record(entry);else Record(entry);},xCurrent-rowIndent,3,xPad-xCurrent-4,OverviewUi.Ivory);
                        if(!native)KeyChipsOn(current,entry.CurrentLabel);
                        Track(current,entry.Id+":current");
                        Hint(current.gameObject,(native?Loc.T("This setting is a controller binding; see the controller column.","这是手柄按键设置，见手柄列。","這是手把按鍵設定，見手把欄。"):entry.CurrentLabel)+"\\n"+T("Actual current binding. Click to record a draft binding; save hotkeys to commit.","这是实际当前按键；点击直接录制草稿，保存快捷键后提交。"));
                        // Controller column: the mod's own controller key, or the combo standing in for its keyboard key.
                        var combo=GamepadCombos.For(entry.Id);
                        var pad=KeyButton(item,"",()=>{if(native)Record(entry);else RecordCombo(entry);},xPad-rowIndent,3,keyWidth-4,OverviewUi.Ivory);
                        if(native)KeyChipsOn(pad,entry.CurrentLabel);
                        else if(combo!=null)KeyChipsOn(pad,combo);
                        else{var empty=pad.GetComponentInChildren<TMP_Text>(true);if(empty!=null){empty.text=Loc.T("Keyboard only","仅键盘","僅鍵盤");empty.color=OverviewUi.Muted;}}
                        Track(pad,entry.Id+":pad");
                        Hint(pad.gameObject,native?entry.CurrentLabel+"\\n"+Loc.T("The mod reads this controller key itself. A records a new one.","模组自己读这个手柄键。按 A 重新录制。","模組自己讀這個手把鍵。按 A 重新錄製。"):ComboHint(entry));
                        pad.gameObject.AddComponent<OverviewHotkeyTextAction>().ContextRequested=()=>KeyMenu(entry,(RectTransform)pad.transform);'''),

# 5. split duplicate marker: controller ← / keyboard →
rep('''                        if(entry.Conflicts.Count>0){Button warning=null!;warning=TextButton(item,T("! Duplicate","！重复键"),()=>OpenConflicts(entry,(RectTransform)warning.transform),xConflict-rowIndent,2,xModDefault-xConflict-2,30,new Color(.95f,.65f,.35f));''',
'''                        bool padConflict=entry.Conflicts.Any(IsPadConflict), keyConflict=entry.Conflicts.Any(c=>!IsPadConflict(c));
                        string conflictText=padConflict&&keyConflict?Loc.T("← Duplicate →","← 重复键 →","← 重複鍵 →"):padConflict?Loc.T("← Duplicate","← 重复键","← 重複鍵"):Loc.T("Duplicate →","重复键 →","重複鍵 →");
                        if(entry.Conflicts.Count>0){Button warning=null!;warning=TextButton(item,conflictText,()=>OpenConflicts(entry,(RectTransform)warning.transform),xConflict-rowIndent,2,xModDefault-xConflict-2,30,new Color(.95f,.65f,.35f));'''),

# 6. combo label/hint helpers + recording with selected modifier + fold-all + modifier menu
rep('''    private string ComboLabel(OverviewHotkeyEntry entry)
    {
        var combo=GamepadCombos.For(entry.Id);
        return combo!=null?GamepadCombos.Label(combo.button):Loc.T("Keyboard only","仅键盘","僅鍵盤");
    }''',
'''    /// <summary>The conflict is on the controller side: a controller-native setting, or another combo / game control on a gamepad button.</summary>
    private bool IsPadConflict(OverviewHotkeyConflict conflict) => conflict.Label.StartsWith("gamepad", StringComparison.OrdinalIgnoreCase)
        || conflict.Label.IndexOf("<Gamepad>", StringComparison.OrdinalIgnoreCase) >= 0 || conflict.Key.StartsWith("combo:", StringComparison.Ordinal);
    /// <summary>Draws key chips on a key button (keyboard label or controller path).</summary>
    private void KeyChipsOn(Button button, string label)
    {
        var text=button.GetComponentInChildren<TMP_Text>(true); if(text==null)return;
        var host=(RectTransform)text.transform.parent; text.text="";
        var keys=new List<(string,bool)>();
        foreach(var part in label.Split('+').Select(x=>x.Trim()).Where(x=>x.Length>0)) keys.Add(KeyChips.Describe(part));
        if(keys.Count==0)return;
        var chipHost=OverviewUi.Rect("KeyChips",host);OverviewUi.Fill(chipHost);
        KeyChips.Build(chipHost,keys,text.font,20,dimFirst:false,centre:true,fontSize:13);
    }
    private void KeyChipsOn(Button button, GamepadCombos.Item combo)
    {
        var text=button.GetComponentInChildren<TMP_Text>(true); if(text==null)return;
        var host=(RectTransform)text.transform.parent; text.text="";
        var keys=new List<(string,bool)>();
        if(combo.modifier.Length>0)keys.Add((GamepadCombos.ButtonName(combo.modifier),true));
        keys.Add((GamepadCombos.ButtonName(combo.button),true));
        var chipHost=OverviewUi.Rect("KeyChips",host);OverviewUi.Fill(chipHost);
        KeyChips.Build(chipHost,keys,text.font,20,dimFirst:combo.modifier.Length>0,centre:true,fontSize:13);
    }
    private void ModifierMenu(RectTransform anchor)
    {
        Popup(anchor,GamepadCombos.Modifiers.Select(m=>((m.Length==0?Loc.T("None (single buttons)","无（只用单键）","無（只用單鍵）"):GamepadCombos.ButtonName(m))+(m==GamepadCombos.Modifier?"  ✓":""),
            (Action)(()=>{GamepadCombos.Modifier=m;_status(Loc.F("Controller modifier: {0}","手柄修饰键：{0}","手把修飾鍵：{0}",GamepadCombos.ButtonName(m)));Render();}),true)).ToList());
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
    /// <summary>LT + RB expands all, LT + LB collapses all (same on the mod list).</summary>
    private void FoldAllShortcut()
    {
        var pad=Gamepad.current; if(pad==null||pad.leftTrigger.ReadValue()<=.5f)return;
        if(pad.rightShoulder.wasPressedThisFrame)ToggleAllFolds(true);
        else if(pad.leftShoulder.wasPressedThisFrame)ToggleAllFolds(false);
    }'''),
rep('''        var combo=GamepadCombos.For(entry.Id);
        string keyboard=Loc.T("Keyboard: ","键盘：","鍵盤：")+entry.CurrentLabel;
        return (combo!=null
            ? Loc.F("Hold LT and press {0} in game to press this mod's key.","局内按住 LT 再按 {0}，等于按下这个模组的快捷键。","遊戲中按住 LT 再按 {0}，等於按下這個模組的快捷鍵。",GamepadCombos.ButtonName(combo.button))
            : Loc.T("This mod only reads the keyboard. Press A, then hold LT and press a button to give it a controller combo.","这个模组只认键盘。按 A，再按住 LT 并按一个键，给它配一个手柄组合键。","這個模組只認鍵盤。按 A，再按住 LT 並按一個鍵，給它配一個手把組合鍵。"))
            +"\\n"+keyboard+"\\n"+Loc.T("LT is also the game's secondary use; holding it still triggers that.","LT 也是游戏的副使用键，按住时游戏照常触发。","LT 也是遊戲的副使用鍵，按住時遊戲照常觸發。");''',
'''        var combo=GamepadCombos.For(entry.Id);
        string keyboard=Loc.T("Keyboard: ","键盘：","鍵盤：")+entry.CurrentLabel;
        return (combo!=null
            ? Loc.F("In game, {0} presses this mod's key.","局内按 {0}，等于按下这个模组的快捷键。","遊戲中按 {0}，等於按下這個模組的快捷鍵。",GamepadCombos.Label(combo))
            : Loc.T("This mod only reads the keyboard. Press A, then press a controller button (hold the modifier first for a combo) to use it from the controller.","这个模组只认键盘。按 A，再按一个手柄键（先按住修饰键就记成组合）即可用手柄触发。","這個模組只認鍵盤。按 A，再按一個手把鍵（先按住修飾鍵就記成組合）即可用手把觸發。"))
            +"\\n"+keyboard+"\\n"+Loc.T("The game still does what that button normally does, unless it is the second key of a modifier combo.","这个手柄键原本在游戏里的操作照常触发；只有修饰键组合里的第二个键不会。","這個手把鍵原本在遊戲裡的操作照常觸發；只有修飾鍵組合裡的第二個鍵不會。");'''),
rep('''        _status(Loc.T("Hold LT and press a button for this hotkey. B cancels.","按住 LT，再按一个键作为这个快捷键的手柄组合。按 B 取消。","按住 LT，再按一個鍵作為這個快捷鍵的手把組合。按 B 取消。"));''',
'''        _status(GamepadCombos.Modifier.Length>0
            ? Loc.F("Press a controller button for this hotkey (hold {0} first for a combo). B cancels.","按一个手柄键作为这个快捷键（先按住 {0} 就记成组合）。按 B 取消。","按一個手把鍵作為這個快捷鍵（先按住 {0} 就記成組合）。按 B 取消。",GamepadCombos.ButtonName(GamepadCombos.Modifier))
            : Loc.T("Press a controller button for this hotkey. B cancels.","按一个手柄键作为这个快捷键。按 B 取消。","按一個手把鍵作為這個快捷鍵。按 B 取消。"));'''),
rep('''        var pad=Gamepad.current; var entry=_comboEntry;
        if(pad==null||entry==null||Time.unscaledTime-_comboStarted<.25f)return;
        bool lt=pad.leftTrigger.ReadValue()>.5f;
        if(!lt&&pad.buttonEast.wasPressedThisFrame){EndComboRecording();_status(Loc.T("Recording cancelled.","已取消录制。","已取消錄製。"));return;}
        if(!lt)return;
        foreach(var button in GamepadCombos.Buttons)
        {
            var control=GamepadCombos.Control(pad,button);
            if(control==null||!control.wasPressedThisFrame)continue;
            string? moved=GamepadCombos.Set(entry.Id,entry.Guid,entry.Section,entry.Key,button);
            EndComboRecording();
            var other=moved==null?null:Service.Entries.FirstOrDefault(e=>e.Id==moved);
            _status(Loc.F("LT + {0} now presses this hotkey.","LT + {0} 现在会按下这个快捷键。","LT + {0} 現在會按下這個快捷鍵。",GamepadCombos.ButtonName(button))
                +(other!=null?" "+Loc.F("(moved from {0})","（原来分给了 {0}）","（原來分給了 {0}）",OverviewLanguageBridge.GetDisplayName(other.Mod)+" / "+other.Name):""));
            Render();return;
        }''',
'''        var pad=Gamepad.current; var entry=_comboEntry;
        if(pad==null||entry==null||Time.unscaledTime-_comboStarted<.25f)return;
        string modifier=GamepadCombos.Modifier; var modControl=modifier.Length>0?GamepadCombos.Control(pad,modifier):null;
        bool modHeld=modControl!=null&&modControl.isPressed;
        if(!modHeld&&pad.buttonEast.wasPressedThisFrame){EndComboRecording();_status(Loc.T("Recording cancelled.","已取消录制。","已取消錄製。"));return;}
        foreach(var button in GamepadCombos.Buttons)
        {
            if(button==modifier)continue; // the modifier alone is not a key
            var control=GamepadCombos.Control(pad,button);
            if(control==null||!control.wasPressedThisFrame)continue;
            string used=modHeld?modifier:"";
            string? moved=GamepadCombos.Set(entry.Id,entry.Guid,entry.Section,entry.Key,used,button);
            EndComboRecording();
            var other=moved==null?null:Service.Entries.FirstOrDefault(e=>e.Id==moved);
            var item=GamepadCombos.For(entry.Id);
            _status(Loc.F("{0} now presses this hotkey.","{0} 现在会按下这个快捷键。","{0} 現在會按下這個快捷鍵。",item!=null?GamepadCombos.Label(item):GamepadCombos.ButtonName(button))
                +(other!=null?" "+Loc.F("(moved from {0})","（原来分给了 {0}）","（原來分給了 {0}）",OverviewLanguageBridge.GetDisplayName(other.Mod)+" / "+other.Name):""));
            Render();return;
        }'''),
rep('''    private bool _gamepadView, _comboRecording;''', '''    private bool _comboRecording;'''),
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
