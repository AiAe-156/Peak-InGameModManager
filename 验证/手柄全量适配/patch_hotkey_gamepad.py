# -*- coding: utf-8 -*-
"""1.1.2: keybindings page shows controller keys while the controller is active and records LT combos."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))

def edit(rel, pairs):
    p = os.path.join(ROOT, rel)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (rel, a[:90], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

V = 'src/Overview/OverviewHotkeyView.cs'
edit(V, [
# re-render when input device flips (like the game's own rebinding page)
('''        if (!_rendering && (_dirty || Math.Abs(_root.rect.width - _width) > 2 || _revision != OverviewLanguageBridge.Revision)) { _dirty = false; Render(); }''',
 '''        // 1.1.2 (user): like the game's own key page, controller input shows controller keys and keyboard/mouse input
        // switches straight back.
        if (_gamepadView != GamepadSupport.IsGamepad && !_capture.IsCapturing && !_comboRecording) { _gamepadView = GamepadSupport.IsGamepad; _dirty = true; }
        if (_comboRecording) TickComboRecording();
        if (!_rendering && (_dirty || Math.Abs(_root.rect.width - _width) > 2 || _revision != OverviewLanguageBridge.Revision)) { _dirty = false; Render(); }'''),
# header title
('''            var currentTitle=Label(_header,T("Current key","当前按键"),xCurrent,1,keyWidth-4,30);currentTitle.alignment=TextAlignmentOptions.Midline;''',
 '''            var currentTitle=Label(_header,_gamepadView?Loc.T("Controller","手柄按键","手把按鍵"):T("Current key","当前按键"),xCurrent,1,keyWidth-4,30);currentTitle.alignment=TextAlignmentOptions.Midline;'''),
# current cell
('''                        var current=KeyButton(item,entry.CurrentLabel,()=>Record(entry),xCurrent-rowIndent,3,xConflict-xCurrent-4,OverviewUi.Ivory);Track(current,entry.Id+":current");Hint(current.gameObject,entry.CurrentLabel+"\\n"+T("Actual current binding. Click to record a draft binding; save hotkeys to commit.","这是实际当前按键；点击直接录制草稿，保存快捷键后提交。"));''',
 '''                        var current=_gamepadView&&!GamepadCombos.NativeGamepad(entry.Config)
                            ? KeyButton(item,ComboLabel(entry),()=>RecordCombo(entry),xCurrent-rowIndent,3,xConflict-xCurrent-4,GamepadCombos.For(entry.Id)!=null?OverviewUi.Ivory:OverviewUi.Muted)
                            : KeyButton(item,entry.CurrentLabel,()=>Record(entry),xCurrent-rowIndent,3,xConflict-xCurrent-4,OverviewUi.Ivory);
                        Track(current,entry.Id+":current");
                        Hint(current.gameObject,_gamepadView&&!GamepadCombos.NativeGamepad(entry.Config)?ComboHint(entry):entry.CurrentLabel+"\\n"+T("Actual current binding. Click to record a draft binding; save hotkeys to commit.","这是实际当前按键；点击直接录制草稿，保存快捷键后提交。"));'''),
# key menu: clear combo
('''        if(entry.IgnoredCount>0)items.Add((T("Restore ignored warnings","恢复已忽略冲突提醒"),()=>Write(()=>Service.RestoreIgnoredFor(entry,out var error)?null:error),true));
        Popup(anchor,items);''',
 '''        if(entry.IgnoredCount>0)items.Add((T("Restore ignored warnings","恢复已忽略冲突提醒"),()=>Write(()=>Service.RestoreIgnoredFor(entry,out var error)?null:error),true));
        if(GamepadCombos.For(entry.Id)!=null)items.Add((Loc.T("Remove controller combo","移除手柄组合键","移除手把組合鍵"),()=>{GamepadCombos.Clear(entry.Id);_status(Loc.T("Controller combo removed.","已移除手柄组合键。","已移除手把組合鍵。"));Render();},true));
        Popup(anchor,items);'''),
# fields + methods
('''    private void Record(OverviewHotkeyEntry entry)''',
 '''    private bool _gamepadView, _comboRecording;
    private OverviewHotkeyEntry? _comboEntry;
    private float _comboStarted;
    private string ComboLabel(OverviewHotkeyEntry entry)
    {
        var combo=GamepadCombos.For(entry.Id);
        return combo!=null?GamepadCombos.Label(combo.button):Loc.T("Keyboard only","仅键盘","僅鍵盤");
    }
    private string ComboHint(OverviewHotkeyEntry entry)
    {
        var combo=GamepadCombos.For(entry.Id);
        string keyboard=Loc.T("Keyboard: ","键盘：","鍵盤：")+entry.CurrentLabel;
        return (combo!=null
            ? Loc.F("Hold LT and press {0} in game to press this mod's key.","局内按住 LT 再按 {0}，等于按下这个模组的快捷键。","遊戲中按住 LT 再按 {0}，等於按下這個模組的快捷鍵。",GamepadCombos.ButtonName(combo.button))
            : Loc.T("This mod only reads the keyboard. Press A, then hold LT and press a button to give it a controller combo.","这个模组只认键盘。按 A，再按住 LT 并按一个键，给它配一个手柄组合键。","這個模組只認鍵盤。按 A，再按住 LT 並按一個鍵，給它配一個手把組合鍵。"))
            +"\\n"+keyboard+"\\n"+Loc.T("LT is also the game's secondary use; holding it still triggers that.","LT 也是游戏的副使用键，按住时游戏照常触发。","LT 也是遊戲的副使用鍵，按住時遊戲照常觸發。");
    }
    /// <summary>A on the controller cell: wait for LT + one button (B / Menu cancels).</summary>
    private void RecordCombo(OverviewHotkeyEntry entry)
    {
        ClosePopup(); _comboRecording=true; _comboEntry=entry; _comboStarted=Time.unscaledTime;
        GamepadIntent.ReleaseAll(); var system=EventSystem.current; if(system!=null)system.sendNavigationEvents=false;
        _status(Loc.T("Hold LT and press a button for this hotkey. B cancels.","按住 LT，再按一个键作为这个快捷键的手柄组合。按 B 取消。","按住 LT，再按一個鍵作為這個快捷鍵的手把組合。按 B 取消。"));
    }
    private void TickComboRecording()
    {
        var pad=Gamepad.current; var entry=_comboEntry;
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
        }
    }
    private void EndComboRecording(){_comboRecording=false;_comboEntry=null;var system=EventSystem.current;if(system!=null)system.sendNavigationEvents=true;}
    private void Record(OverviewHotkeyEntry entry)'''),
# B while recording combo should not leave the page
('''        if (_capture.CancelCapture()) { _status(T("Recording cancelled.", "已取消按键录制。")); return true; }''',
 '''        if (_capture.CancelCapture()) { _status(T("Recording cancelled.", "已取消按键录制。")); return true; }
        if (_comboRecording) { EndComboRecording(); _status(Loc.T("Recording cancelled.", "已取消录制。", "已取消錄製。")); return true; }'''),
('''    public void OnClosed() { GamepadIntent.Release(_root.gameObject);''',
 '''    public void OnClosed() { if (_comboRecording) EndComboRecording(); GamepadIntent.Release(_root.gameObject);'''),
])
print('ok')
