# -*- coding: utf-8 -*-
"""1.1.4 (user): with a controller, keyboard cells cannot be recorded; with keyboard and mouse, controller cells cannot
be recorded. Controller-column hint rewritten in the wording the user gave (formal, short, marked terms)."""
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
('''    private void Record(OverviewHotkeyEntry entry)
    {ClosePopup();''', '''    private void Record(OverviewHotkeyEntry entry)
    {if(GamepadSupport.IsGamepad){_status(Loc.T("Keyboard keys are set with keyboard and mouse.","键盘快捷键需使用键鼠设置。","鍵盤快捷鍵需使用鍵鼠設定。"));return;}ClosePopup();'''),
('''    private void RecordPack(OverviewHotkeyEntry entry)
    {ClosePopup();''', '''    private void RecordPack(OverviewHotkeyEntry entry)
    {if(GamepadSupport.IsGamepad){_status(Loc.T("Keyboard keys are set with keyboard and mouse.","键盘快捷键需使用键鼠设置。","鍵盤快捷鍵需使用鍵鼠設定。"));return;}ClosePopup();'''),
('''    private void RecordCombo(OverviewHotkeyEntry entry)
    {
        ClosePopup();''', '''    private void RecordCombo(OverviewHotkeyEntry entry)
    {
        if(!GamepadSupport.IsGamepad){_comboToPreset=false;_status(Loc.T("Controller keys are set with a controller.","手柄快捷键需使用手柄设置。","手把快捷鍵需使用手把設定。"));return;}
        ClosePopup();'''),
('''        var combo=GamepadCombos.For(entry.Id);
        string keyboard=Loc.T("Keyboard: ","键盘：","鍵盤：")+entry.CurrentLabel;
        return (combo!=null
            ? Loc.F("In game, {0} presses this mod's key.","局内按 {0}，等于按下这个模组的快捷键。","遊戲中按 {0}，等於按下這個模組的快捷鍵。",GamepadCombos.Label(combo))
            : Loc.T("This mod only reads the keyboard. Press A, then press a controller button (hold the modifier first for a combo) to use it from the controller.","这个模组只认键盘。按 A，再按一个手柄键（先按住修饰键就记成组合）即可用手柄触发。","這個模組只認鍵盤。按 A，再按一個手把鍵（先按住修飾鍵就記成組合）即可用手把觸發。"))
            +"\\n"+keyboard+"\\n"+Loc.T("The game still does what that button normally does, unless it is the second key of a modifier combo.","这个手柄键原本在游戏里的操作照常触发；只有修饰键组合里的第二个键不会。","這個手把鍵原本在遊戲裡的操作照常觸發；只有修飾鍵組合裡的第二個鍵不會。")
            +(entry.Adapter.IsPath?"\\n"+Loc.T("This mod reads an input path; some such mods do not see controller combos.","这个模组按输入路径读键，部分这类模组收不到手柄组合。","這個模組按輸入路徑讀鍵，部分這類模組收不到手把組合。"):"");''',
 '''        // 1.1.4 (user, exact wording): formal and short; the modifier shown is the one actually set.
        var combo=GamepadCombos.For(entry.Id);
        string modifier=GamepadCombos.Modifier.Length>0?GamepadCombos.ButtonName(GamepadCombos.Modifier):Loc.T("not set","未分配","未分配");
        string head=combo!=null
            ? Loc.F("Controller hotkey: {0}","已分配的手柄快捷键：{0}","已分配的手把快捷鍵：{0}",GamepadCombos.Label(combo))
            : Loc.T("This mod has no preset controller hotkey.","该模组未分配预设的手柄快捷键。","該模組未分配預設的手把快捷鍵。");
        return head+"\\n\\n"
            +Loc.T("Press the controller's A button to assign a hotkey.","按下手柄的 A 键分配快捷键。","按下手把的 A 鍵分配快捷鍵。")+"\\n"
            +Loc.F("* Use \\"{0}\\" + another button as a combo to avoid clashing with the game's default buttons.","*建议使用「{0}」+其他按键组合为组合键，以避免和游戏默认按键冲突。","*建議使用「{0}」+其他按鍵組合為組合鍵，以避免和遊戲預設按鍵衝突。",modifier);'''),
])
print('ok')
