# -*- coding: utf-8 -*-
"""1.1.4 keybindings page: direct write (no draft step), controller combos for controller-native settings too,
controller column colour and header, "Not set", right columns follow the input device, chips without bold + "+"."""
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

# ---------- 1. key chips: regular weight, clear font, "+" between keys ----------
edit('KeyChips.cs', [
('''            text.text = label; text.fontSize = fontSize; text.fontStyle = FontStyles.Bold; text.richText = false; text.parseCtrlCharacters = false;''',
 '''            // 1.1.4 (user): bold made the game font blurry at this size; regular weight, crisp system font when available.
            var crisp = Overview.MceRegularFont.Get(font); if (crisp != null) text.font = crisp;
            text.text = label; text.fontSize = fontSize; text.fontStyle = FontStyles.Normal; text.richText = false; text.parseCtrlCharacters = false;'''),
('''        float total = 0; foreach (var c in chips) total += c.Width; total += gap * Math.Max(0, chips.Count - 1);
        float x = centre ? -total / 2f : -total;
        foreach (var (rect, width) in chips)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(centre ? .5f : 1f, .5f); rect.pivot = new Vector2(0, .5f);
            rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(x, 0);
            x += width + gap;
        }
        return total;''',
 '''        // 1.1.4 (user): a "+" between keys of a combo.
        var plus = new List<RectTransform>(); float plusWidth = 0;
        for (int i = 1; i < chips.Count; i++)
        {
            var sign = new GameObject("KeyChip", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            sign.transform.SetParent(host, false);
            var crisp = Overview.MceRegularFont.Get(font); if (crisp != null) sign.font = crisp; else if (font != null) sign.font = font;
            sign.text = "+"; sign.fontSize = fontSize; sign.color = Dim; sign.alignment = TextAlignmentOptions.Midline; sign.raycastTarget = false;
            plusWidth = Mathf.Ceil(sign.GetPreferredValues("+", Mathf.Infinity, height).x) + 2f;
            plus.Add(sign.rectTransform);
        }
        float total = 0; foreach (var c in chips) total += c.Width; total += (gap * 2 + plusWidth) * Math.Max(0, chips.Count - 1);
        float x = centre ? -total / 2f : -total;
        for (int i = 0; i < chips.Count; i++)
        {
            var (rect, width) = chips[i];
            rect.anchorMin = rect.anchorMax = new Vector2(centre ? .5f : 1f, .5f); rect.pivot = new Vector2(0, .5f);
            rect.sizeDelta = new Vector2(width, height); rect.anchoredPosition = new Vector2(x, 0);
            x += width + gap;
            if (i < plus.Count)
            {
                var p = plus[i]; p.anchorMin = p.anchorMax = rect.anchorMin; p.pivot = new Vector2(0, .5f);
                p.sizeDelta = new Vector2(plusWidth, height); p.anchoredPosition = new Vector2(x, 0); x += plusWidth + gap;
            }
        }
        return total;'''),
])

# ---------- 2. direct write: record = draft + immediate hotkey-scope commit ----------
edit('Overview/OverviewHotkeyView.cs', [
('''    private void Write(Func<string?> operation){string? error=operation();if(!string.IsNullOrEmpty(error))_status(Loc.LocalizeMessage(error));else {_status(T("Hotkey draft edited; save hotkeys to use it.","快捷键草稿已修改，保存后使用。"));Render();_headersChanged();}}''',
 '''    private void Write(Func<string?> operation){string? error=operation();if(!string.IsNullOrEmpty(error))_status(Loc.LocalizeMessage(error));else {_status(T("Hotkey draft edited; save hotkeys to use it.","快捷键草稿已修改，保存后使用。"));Render();_headersChanged();}}
    /// <summary>1.1.4 (user): the keybindings page has no draft step. Recording writes the key and commits the hotkey
    /// scope at once, so the mod uses it immediately.</summary>
    private void WriteNow(Func<string?> operation)
    {
        string? error=operation();
        if(!string.IsNullOrEmpty(error)){_status(Loc.LocalizeMessage(error));return;}
        try
        {
            ConfigurationSaveRequest.Run(_repo,_mods(),ConfigurationSaveScope.Hotkeys,"",result=>{
                _status(result.Success?Loc.T("Key saved and in use.","按键已保存并生效。","按鍵已儲存並生效。"):Loc.LocalizeMessage(result.Message));
                Render();_headersChanged();});
        }
        catch(Exception ex){_status(Loc.LocalizeMessage(ex.Message));Render();}
    }'''),
('''    {ClosePopup();_capture.Begin(entry,value=>Write(()=>Service.TrySet(entry,value,out var error)?null:error),()=>_status(T("Recording cancelled.","已取消按键录制。")));_status(T("Press a key or shortcut; Escape cancels.","请按下按键或组合键，ESC取消。"));}''',
 '''    {ClosePopup();_capture.Begin(entry,value=>WriteNow(()=>Service.TrySet(entry,value,out var error)?null:error),()=>_status(T("Recording cancelled.","已取消按键录制。")));_status(T("Press a key or shortcut; Escape cancels.","请按下按键或组合键，ESC取消。"));}'''),
])

# ---------- 3. controller column: native settings also accept combos ----------
edit('Overview/OverviewHotkeyView.cs', [
('''                        var pad=KeyButton(item,"",()=>{if(native)Record(entry);else RecordCombo(entry);},xPad-rowIndent,3,keyWidth-4,OverviewUi.Ivory);
                        if(native)KeyChipsOn(pad,RawKey(entry));
                        else if(combo!=null)KeyChipsOn(pad,combo);
                        else{var empty=pad.GetComponentInChildren<TMP_Text>(true);if(empty!=null){empty.text=Loc.T("Keyboard only","仅键盘","僅鍵盤");empty.color=OverviewUi.Muted;}}
                        Track(pad,entry.Id+":pad");
                        Hint(pad.gameObject,native?entry.CurrentLabel+"\\n"+Loc.T("The mod reads this controller key itself. A records a new one.","模组自己读这个手柄键。按 A 重新录制。","模組自己讀這個手把鍵。按 A 重新錄製。"):ComboHint(entry));''',
 '''                        // 1.1.4 (user): A on the controller cell always records a controller key or combo. A single button on a
                        // controller-native setting is written into the mod's own setting; a combo (or any key on a keyboard-only
                        // setting) is kept by the manager, which presses the mod's key for it in game.
                        var pad=KeyButton(item,"",()=>RecordCombo(entry),xPad-rowIndent,3,keyWidth-4,OverviewUi.Ivory);
                        StylePad(pad);
                        if(combo!=null)KeyChipsOn(pad,combo);
                        else if(native&&RawKey(entry).Length>0)KeyChipsOn(pad,RawKey(entry));
                        else{var empty=pad.GetComponentInChildren<TMP_Text>(true);if(empty!=null){empty.text=Loc.T("Not set","未设置","未設定");empty.color=PadMuted;}}
                        Track(pad,entry.Id+":pad");
                        Hint(pad.gameObject,ComboHint(entry));'''),
('''        string modifier=GamepadCombos.Modifier; var modControl=modifier.Length>0?GamepadCombos.Control(pad,modifier):null;
        bool modHeld=modControl!=null&&modControl.isPressed;''',
 '''        string modifier=GamepadCombos.Modifier; var modControl=modifier.Length>0?GamepadCombos.Control(pad,modifier):null;
        bool modHeld=modControl!=null&&modControl.isPressed;
        bool native=GamepadCombos.NativeGamepad(entry.Config);'''),
('''            string used=modHeld?modifier:"";
            string? moved=GamepadCombos.Set(entry.Id,entry.Guid,entry.Section,entry.Key,used,button);
            EndComboRecording(); Service.RefreshConflicts();''',
 '''            string used=modHeld?modifier:"";
            if(native&&used.Length==0)
            {
                // A single button for a controller-native setting goes into the mod's own setting (written at once).
                GamepadCombos.Clear(entry.Id); EndComboRecording();
                object? value=GamepadCombos.NativeValue(entry.Config,button);
                if(value==null){_status(Loc.T("This mod does not accept that button.","这个模组不接受这个键。","這個模組不接受這個鍵。"));Render();return;}
                WriteNow(()=>Service.TrySet(entry,value,out var error)?null:error); return;
            }
            string? moved=GamepadCombos.Set(entry.Id,entry.Guid,entry.Section,entry.Key,used,button);
            EndComboRecording(); Service.RefreshConflicts();'''),
])
edit('GamepadCombos.cs', [
('''    private static KeyCode FromCanonical(string canonical)''', '''    /// <summary>The value to store in a controller-native setting for <paramref name="button"/> (GamepadButton enum or a
    /// &lt;Gamepad&gt; path, matching the setting's type); null when the setting cannot hold it.</summary>
    internal static object? NativeValue(ConfigEntryBase entry, string button)
    {
        try
        {
            if (entry.SettingType == typeof(GamepadButton))
            {
                string name = button switch { "buttonSouth" => "South", "buttonEast" => "East", "buttonWest" => "West", "buttonNorth" => "North",
                    "leftStickPress" => "LeftStick", "rightStickPress" => "RightStick", "dpad/up" => "DpadUp", "dpad/down" => "DpadDown",
                    "dpad/left" => "DpadLeft", "dpad/right" => "DpadRight", _ => char.ToUpperInvariant(button[0]) + button.Substring(1) };
                return Enum.TryParse(name, true, out GamepadButton parsed) ? parsed : null;
            }
            if (entry.SettingType == typeof(string)) return "<Gamepad>/" + button;
        }
        catch { }
        return null;
    }
    /// <summary>The controller button a controller-native setting currently holds (for injection: a combo for such a
    /// setting presses that button for the mod).</summary>
    internal static ButtonControl? NativeControl(ConfigEntryBase entry, Gamepad pad)
    {
        string raw = Convert.ToString(entry.BoxedValue) ?? "";
        if (raw.Length == 0 || raw.Equals("None", StringComparison.OrdinalIgnoreCase)) return null;
        string token = raw.StartsWith("<", StringComparison.Ordinal) ? raw.Substring(raw.IndexOf('/') + 1) : raw;
        token = token switch { "South" or "A" or "Cross" => "buttonSouth", "East" or "B" or "Circle" => "buttonEast", "West" or "X" or "Square" => "buttonWest",
            "North" or "Y" or "Triangle" => "buttonNorth", "LeftStick" => "leftStickPress", "RightStick" => "rightStickPress", "DpadUp" => "dpad/up",
            "DpadDown" => "dpad/down", "DpadLeft" => "dpad/left", "DpadRight" => "dpad/right", "Start" => "start", "Select" => "select",
            var other => char.ToLowerInvariant(other[0]) + other.Substring(1) };
        return Control(pad, token);
    }
    private static KeyCode FromCanonical(string canonical)'''),
])

# ---------- 4. controller column look + header, modifier & fold-all above it ----------
edit('Overview/OverviewHotkeyView.cs', [
('''            Button modifierButton=null!;
            modifierButton=OverviewUi.Button(_tools,Loc.T("Controller modifier: ","手柄修饰键：","手把修飾鍵：")+GamepadCombos.ButtonName(GamepadCombos.Modifier),()=>ModifierMenu((RectTransform)modifierButton.transform),190,30,role:UiActionRole.Edit);
            OverviewUi.Place((RectTransform)modifierButton.transform,toolX,1,190,30);toolX+=196;''',
 '''            Button modifierButton=null!;
            modifierButton=OverviewUi.Button(_tools,Loc.T("Modifier: ","修饰键：","修飾鍵：")+GamepadCombos.ButtonName(GamepadCombos.Modifier),()=>ModifierMenu((RectTransform)modifierButton.transform),150,30,role:UiActionRole.Edit);
            StylePad(modifierButton);'''),
('''            var foldAll=OverviewUi.IconButton(_tools,anyFolded?"triangle-right":"triangle-down",()=>ToggleAllFolds(),30,30);
            OverviewUi.Place((RectTransform)foldAll.transform,toolX,1,30,30);''',
 '''            var foldAll=OverviewUi.IconButton(_tools,anyFolded?"triangle-right":"triangle-down",()=>ToggleAllFolds(),30,30);
            StylePad(foldAll);'''),
('''            var padTitle=Label(_header,Loc.T("Controller","手柄按键","手把按鍵"),xPad,1,keyWidth-4,30);padTitle.alignment=TextAlignmentOptions.Midline;''',
 '''            var padTitle=Label(_header,Loc.T("Controller","手柄按键","手把按鍵"),xPad,1,keyWidth-4,30,PadAccent);padTitle.alignment=TextAlignmentOptions.Midline;
            // 1.1.4 (user): the modifier picker and expand/collapse-all sit right above the controller column, in its colour.
            OverviewUi.Place((RectTransform)modifierButton.transform,Mathf.Max(toolX,xPad-rowIndentTop()),1,Mathf.Max(110,keyWidth-38),30);
            OverviewUi.Place((RectTransform)foldAll.transform,Mathf.Max(toolX,xPad-rowIndentTop())+Mathf.Max(110,keyWidth-38)+4,1,30,30);'''),
('''    private void ModifierMenu(RectTransform anchor)''', '''    private static float rowIndentTop()=>0f;
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
    private void ModifierMenu(RectTransform anchor)'''),
])

# ---------- 5. right two columns follow the input device ----------
edit('Overview/OverviewHotkeyView.cs', [
('''        // 1.1.3 (user): the controller column is permanent, so nothing flips with the input device any more.
        if (_comboRecording) TickComboRecording();''',
 '''        // 1.1.4 (user): the two right columns (mod default / profile preset) follow the input device; the keyboard and
        // controller columns themselves stay put.
        if (_padDefaults != GamepadSupport.IsGamepad && !_capture.IsCapturing && !_comboRecording) { _padDefaults = GamepadSupport.IsGamepad; _dirty = true; }
        if (_comboRecording) TickComboRecording();'''),
('''    private bool _comboRecording;''', '''    private bool _comboRecording, _padDefaults;'''),
('''                        var original=KeyButton(item,entry.DefaultLabel,()=>Write(()=>Service.RestoreDefault(entry,out var error)?null:error),xModDefault-rowIndent,3,keyWidth-4,OverviewUi.Green);Track(original,entry.Id+":default");''',
 '''                        bool showPad=_padDefaults;
                        string defaultRaw=native?Convert.ToString(entry.Config.DefaultValue)??"":"";
                        var original=KeyButton(item,showPad?"":entry.DefaultLabel,()=>{if(showPad){if(native)WriteNow(()=>Service.RestoreDefault(entry,out var error)?null:error);else{GamepadCombos.Clear(entry.Id);Service.RefreshConflicts();Render();}}else WriteNow(()=>Service.RestoreDefault(entry,out var error)?null:error);},xModDefault-rowIndent,3,keyWidth-4,OverviewUi.Green);Track(original,entry.Id+":default");
                        if(showPad){StylePad(original);if(native&&!OverviewHotkeyAdapter.IsNone(defaultRaw))KeyChipsOn(original,defaultRaw);else{var t=original.GetComponentInChildren<TMP_Text>(true);if(t!=null){t.text=Loc.T("Not set","未设置","未設定");t.color=PadMuted;}}}'''),
('''                        string packLabel=entry.PackPreset?.Label??T("Not recorded","未记录");
                        var pack=KeyButton(item,packLabel,()=>RecordPack(entry),xPack-rowIndent,3,keyWidth-4,OverviewUi.Blue);''',
 '''                        string packLabel=entry.PackPreset?.Label??T("Not recorded","未记录");
                        var packCombo=GamepadCombos.PresetFor(_repo.State,entry.Id);
                        var pack=KeyButton(item,showPad?"":packLabel,()=>{if(showPad)RecordComboPreset(entry);else RecordPack(entry);},xPack-rowIndent,3,keyWidth-4,OverviewUi.Blue);
                        if(showPad){StylePad(pack);if(packCombo!=null)KeyChipsOn(pack,packCombo);else{var t=pack.GetComponentInChildren<TMP_Text>(true);if(t!=null){t.text=Loc.T("Not set","未设置","未設定");t.color=PadMuted;}}}'''),
('''    private void EndComboRecording(){''', '''    /// <summary>Records a controller combo into the profile (exported with packs), not into this PC's live combos.</summary>
    private void RecordComboPreset(OverviewHotkeyEntry entry){_comboToPreset=true;RecordCombo(entry);}
    private bool _comboToPreset;
    private void EndComboRecording(){'''),
('''            string? moved=GamepadCombos.Set(entry.Id,entry.Guid,entry.Section,entry.Key,used,button);
            EndComboRecording(); Service.RefreshConflicts();''',
 '''            if(_comboToPreset)
            {
                _comboToPreset=false; EndComboRecording();
                GamepadCombos.SetPreset(_repo.State,entry.Id,entry.Guid,entry.Section,entry.Key,used,button); _repo.Save();
                _status(Loc.T("Controller key recorded in this profile.","手柄键已记入此方案。","手把鍵已記入此方案。")); Render(); return;
            }
            string? moved=GamepadCombos.Set(entry.Id,entry.Guid,entry.Section,entry.Key,used,button);
            EndComboRecording(); Service.RefreshConflicts();'''),
])
edit('Overview/OverviewModels.cs', [
('''    public List<OverviewHotkeyPreset> packHotkeyPresets = new();''', '''    public List<OverviewHotkeyPreset> packHotkeyPresets = new();
    /// <summary>1.1.4: controller keys / combos recorded in the profile (exported with packs; applied to this PC's
    /// combos when the profile is applied).</summary>
    public List<GamepadCombos.Item> gamepadComboPresets = new();'''),
])
edit('GamepadCombos.cs', [
('''    internal static void Clear(string id) { Load(); if (_items.RemoveAll(i => i.id == id) > 0) Save(); }''',
 '''    internal static void Clear(string id) { Load(); if (_items.RemoveAll(i => i.id == id) > 0) Save(); }
    internal static Item? PresetFor(Overview.OverviewState state, string id) => state.gamepadComboPresets?.FirstOrDefault(i => i.id == id);
    internal static void SetPreset(Overview.OverviewState state, string id, string guid, string section, string key, string modifier, string button)
    {
        state.gamepadComboPresets ??= new();
        state.gamepadComboPresets.RemoveAll(i => i.id == id || (i.button == button && i.Mod == modifier));
        state.gamepadComboPresets.Add(new Item { id = id, guid = guid, section = section, key = key, modifier = modifier, button = button });
    }
    /// <summary>Profile applied: its controller presets replace this PC's combos for the same hotkeys.</summary>
    internal static void ApplyPresets(Overview.OverviewState state)
    {
        if (state.gamepadComboPresets == null || state.gamepadComboPresets.Count == 0) return;
        Load();
        foreach (var preset in state.gamepadComboPresets)
        {
            _items.RemoveAll(i => i.id == preset.id || (i.button == preset.button && i.Mod == preset.Mod));
            _items.Add(new Item { id = preset.id, guid = preset.guid, section = preset.section, key = preset.key, modifier = preset.Mod, button = preset.button });
        }
        Save();
    }'''),
])
print('ok')
