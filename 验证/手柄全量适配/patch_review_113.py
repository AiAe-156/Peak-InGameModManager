# -*- coding: utf-8 -*-
"""1.1.3 review fixes (Opus 5.5 second review)."""
import io, os, re
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

# ---- #1 no per-frame duplicate polling; refresh only when ModConfig recomputes duplicates ----
edit('SettingCellExtras.cs', [
('''        private void RefreshConflict()
        {
            if (_chip == null || _nativeWarning == null || _cell?.m_text == null) return;
            bool native = _nativeWarning.gameObject.activeSelf;
            string text = native ? _nativeWarning.text : "";
            if (native) _nativeWarning.gameObject.SetActive(false); // the long orange line; the chip replaces it
            if (native) _conflicts = text;
            bool show = _conflicts.Length > 0 && (native || _chip.gameObject.activeSelf) && _property != null && StillConflicting();
            if (_chip.gameObject.activeSelf != show) { _chip.gameObject.SetActive(show); _laidOutWidth = -1; }
            if (!show) return;''',
'''        /// <summary>1.1.3 review: called from ModConfig's own duplicate recompute (GetDuplicateControls postfix), never per
        /// frame. That call is expensive (it scans every setting and game binding and logs each comparison).</summary>
        internal void OnDuplicatesRecomputed(bool duplicate, string text)
        {
            _conflicts = duplicate ? text : "";
            if (_nativeWarning != null && _nativeWarning.gameObject.activeSelf) _nativeWarning.gameObject.SetActive(false); // the chip replaces the long orange line
            _conflictDirty = true;
        }
        private bool _conflictDirty = true;
        private void RefreshConflict()
        {
            if (_chip == null || _nativeWarning == null || _cell == null || _cell.m_text == null) return;
            if (_nativeWarning.gameObject.activeSelf) { _conflicts = _nativeWarning.text; _nativeWarning.gameObject.SetActive(false); _conflictDirty = true; }
            if (!_conflictDirty) return;
            _conflictDirty = false;
            bool show = _conflicts.Length > 0;
            if (_chip.gameObject.activeSelf != show) { _chip.gameObject.SetActive(show); _laidOutWidth = -1; }
            if (!show) return;'''),
('''        private bool StillConflicting()
        {
            try { return _property != null && ModSettingsMenu.GetMatchingValues(_property).Length > 0; }
            catch { return true; }
        }
''', ''),
# #13 Unity null trap
('''        private void Show() { if (Owner != null) ModConfigDescriptionPanel.Current?.ShowNotice(Owner.Explanation(), warning: true); }''',
 '''        private void Show() { var panel = ModConfigDescriptionPanel.Current; if (Owner != null && panel != null) panel.ShowNotice(Owner.Explanation(), warning: true); }'''),
# #5-ish: FitKeyBox should not give up leaving the 50-high box: shrink height anyway
('''            if (right - left < 60f) { _keyBoxDone = true; return; }''', '''            if (right - left < 60f) right = left + 60f; // still make it one line high; narrow pages accept a short box'''),
])
edit('ModConfigLocalizationPatch.cs', [
('''        TextMeshProUGUI? warning = ui.DuplicateWarningText;
        if (warning == null || !warning.gameObject.activeSelf)
        {
            return;
        }
''', '''        TextMeshProUGUI? warning = ui.DuplicateWarningText;
        // 1.1.3: tell the cell's corner badge (it no longer polls the expensive duplicate scan each frame).
        var extras = ui.GetComponentInParent<SettingCellExtras.CellExtras>();
        if (extras != null) extras.OnDuplicatesRecomputed(warning != null && warning.gameObject.activeSelf, warning != null ? warning.text : "");
        if (warning == null || !warning.gameObject.activeSelf)
        {
            return;
        }
'''),
])

# ---- #2 storage: Newtonsoft, migration, Load inside try ----
edit('GamepadCombos.cs', [
('''using HarmonyLib;''', '''using HarmonyLib;
using Newtonsoft.Json;'''),
('''    [Serializable] private sealed class Store { public string modifier = ""; public List<Item> items = new(); }''',
 '''    private sealed class Store { public string? modifier; public List<Item>? items; }'''),
('''        public string id = "", guid = "", section = "", key = "", button = "";
        /// <summary>The modifier held when this combo was recorded ("" = single button).</summary>
        public string modifier = "";
        [NonSerialized] internal ConfigEntryBase? Entry; [NonSerialized] internal bool Resolved;''',
 '''        public string id = "", guid = "", section = "", key = "", button = "";
        /// <summary>The modifier held when this combo was recorded ("" = single button; null = written by 1.1.2, where
        /// the modifier was always LT).</summary>
        public string? modifier;
        [JsonIgnore] internal ConfigEntryBase? Entry; [JsonIgnore] internal bool Resolved;
        [JsonIgnore] internal string Mod => modifier ?? "";
        [JsonIgnore] internal List<KeyCode>? Keys; [JsonIgnore] internal object? KeysFor;'''),
('''        try
        {
            if (File.Exists(FilePath))
            {
                var store = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
                _items = store?.items ?? new(); _modifier = store != null && Modifiers.Contains(store.modifier) ? store.modifier : "";
            }
        }
        catch (Exception error) { Plugin.Log.LogWarning("[手柄组合] 读取失败：" + error.Message); _items = new(); }
        _items.RemoveAll(i => string.IsNullOrEmpty(i.id) || !Buttons.Contains(i.button) || i.modifier == i.button || (i.modifier.Length > 0 && !Modifiers.Contains(i.modifier)));''',
 '''        try
        {
            if (File.Exists(FilePath))
            {
                // 1.1.3 review: 1.1.2 wrote this with JsonUtility, which drops List<Item> (files are "{}"); Newtonsoft now.
                var store = JsonConvert.DeserializeObject<Store>(File.ReadAllText(FilePath));
                _items = store?.items ?? new();
                _modifier = store?.modifier != null && Modifiers.Contains(store.modifier) ? store.modifier : "";
                foreach (var item in _items) if (item.modifier == null) item.modifier = "leftTrigger"; // 1.1.2: always LT
            }
            _items.RemoveAll(i => i == null || string.IsNullOrEmpty(i.id) || !Buttons.Contains(i.button) || i.Mod == i.button || (i.Mod.Length > 0 && !Modifiers.Contains(i.Mod)));
        }
        catch (Exception error) { Plugin.Log.LogWarning("[手柄组合] 读取失败：" + error.Message); _items = new(); }'''),
('''            File.WriteAllText(temp, JsonUtility.ToJson(new Store { modifier = _modifier, items = _items }, true));''',
 '''            File.WriteAllText(temp, JsonConvert.SerializeObject(new Store { modifier = _modifier, items = _items }, Formatting.Indented));'''),
('''        string? moved = _items.FirstOrDefault(i => i.button == button && i.modifier == modifier && i.id != id)?.id;
        _items.RemoveAll(i => i.id == id || (i.button == button && i.modifier == modifier));''',
 '''        string? moved = _items.FirstOrDefault(i => i.button == button && i.Mod == modifier && i.id != id)?.id;
        _items.RemoveAll(i => i.id == id || (i.button == button && i.Mod == modifier));'''),
('''    internal static string Label(Item item) => (item.modifier.Length > 0 ? ButtonName(item.modifier) + " + " : "") + ButtonName(item.button);''',
 '''    internal static string Label(Item item) => (item.Mod.Length > 0 ? ButtonName(item.Mod) + " + " : "") + ButtonName(item.button);'''),
# #9 no per-frame allocation, cached keys, try/catch
('''    private static readonly HashSet<KeyCode> _held = new();''', '''    private static readonly HashSet<KeyCode> _held = new(), _nowHeld = new();
    private static readonly Dictionary<Item, int> _pressedAt = new();'''),
('''        var pad = Gamepad.current;
        var nowHeld = new HashSet<KeyCode>();
        if (pad != null && !TypingGuard.Active && _items.Count > 0 && InGame())
            foreach (var item in _items)
            {
                var control = Control(pad, item.button);
                if (control == null || !Pressed(control)) continue;
                // A single-button combo only fires while no recorded modifier is held, so "LT + A" never also fires "A".
                if (item.modifier.Length > 0) { var mod = Control(pad, item.modifier); if (mod == null || !Pressed(mod)) continue; }
                else if (AnyModifierHeld(pad)) continue;
                var entry = Resolve(item); if (entry == null) continue;
                foreach (var code in KeysOf(entry)) nowHeld.Add(code);
            }
        foreach (var code in nowHeld) if (!_held.Contains(code)) _downFrame[code] = Time.frameCount;
        foreach (var code in _held) if (!nowHeld.Contains(code)) _upFrame[code] = Time.frameCount;
        _held.Clear(); foreach (var code in nowHeld) _held.Add(code);''',
 '''        if (_items.Count == 0 && _held.Count == 0) return;
        try
        {
            var pad = Gamepad.current;
            _nowHeld.Clear();
            if (pad != null && !TypingGuard.Active && _items.Count > 0 && InGame())
                foreach (var item in _items)
                {
                    var control = Control(pad, item.button);
                    if (control == null || !Pressed(control)) { _pressedAt.Remove(item); continue; }
                    if (!_pressedAt.ContainsKey(item)) _pressedAt[item] = Time.frameCount;
                    // A single-button combo only fires while no recorded modifier is held, so "LT + A" never also fires "A".
                    if (item.Mod.Length > 0) { var mod = Control(pad, item.Mod); if (mod == null || !Pressed(mod)) continue; }
                    else if (AnyModifierHeld(pad)) continue;
                    var entry = Resolve(item); if (entry == null) continue;
                    foreach (var code in CachedKeys(item, entry)) _nowHeld.Add(code);
                }
            foreach (var code in _nowHeld) if (!_held.Contains(code)) _downFrame[code] = Time.frameCount;
            foreach (var code in _held) if (!_nowHeld.Contains(code)) _upFrame[code] = Time.frameCount;
            _held.Clear(); foreach (var code in _nowHeld) _held.Add(code);
        }
        catch (Exception error) { WarnOnce("[手柄组合] 更新失败：" + error.Message); _held.Clear(); }'''),
('''    private static bool Pressed(ButtonControl control) { _reentry++; try { return control.isPressed; } finally { _reentry--; } }''',
 '''    private static bool Pressed(ButtonControl control) { _reentry++; try { return control.isPressed; } finally { _reentry--; } }
    private static bool _warned;
    private static void WarnOnce(string message) { if (_warned) return; _warned = true; Plugin.Log.LogWarning(message); }
    /// <summary>Keys of the setting, re-parsed only when its value changes.</summary>
    private static List<KeyCode> CachedKeys(Item item, ConfigEntryBase entry)
    {
        object? value = entry.BoxedValue;
        if (item.Keys == null || !Equals(item.KeysFor, value)) { item.Keys = KeysOf(entry); item.KeysFor = value; }
        return item.Keys;
    }'''),
('''        foreach (var item in _items) if (item.modifier.Length > 0) { var mod = Control(pad, item.modifier); if (mod != null && Pressed(mod)) return true; }''',
 '''        foreach (var item in _items) if (item.Mod.Length > 0) { var mod = Control(pad, item.Mod); if (mod != null && Pressed(mod)) return true; }'''),
# #7 mask only presses that started after the modifier
('''        foreach (var item in _items)
        {
            if (item.modifier.Length == 0 || Control(pad, item.button) != control) continue;
            var mod = Control(pad, item.modifier); if (mod != null && Pressed(mod)) return true;
        }
        return false;''',
 '''        foreach (var item in _items)
        {
            if (item.Mod.Length == 0 || Control(pad, item.button) != control) continue;
            var mod = Control(pad, item.Mod); if (mod == null || !Pressed(mod)) continue;
            // Only a press that began while the modifier was down: holding RT to climb and then pressing LT must not
            // make the game drop RT (review #7).
            if (_pressedAt.TryGetValue(item, out int since) && _modifierSince.TryGetValue(item.Mod, out int modSince) && since < modSince) continue;
            return true;
        }
        return false;'''),
('''    private static int _reentry;''', '''    private static int _reentry;
    private static readonly Dictionary<string, int> _modifierSince = new();
    /// <summary>Frame each modifier went down (for review #7).</summary>
    private static void TrackModifiers(Gamepad pad)
    {
        foreach (var name in Modifiers)
        {
            if (name.Length == 0) continue;
            var control = Control(pad, name); bool down = control != null && Pressed(control);
            if (down && !_modifierSince.ContainsKey(name)) _modifierSince[name] = Time.frameCount;
            else if (!down) _modifierSince.Remove(name);
        }
    }'''),
('''            var pad = Gamepad.current;
            _nowHeld.Clear();''', '''            var pad = Gamepad.current;
            _nowHeld.Clear();
            if (pad != null) TrackModifiers(pad);'''),
# #3 legacy joystick buttons hide while an injected key is down (BepInEx KeyboardShortcut.ModifierKeyTest)
('''    private static void LegacyHeld(KeyCode key, ref bool __result) { if (!__result && _reentry == 0 && _items.Count > 0 && Held(key)) __result = true; }''',
 '''    private static void LegacyHeld(KeyCode key, ref bool __result)
    {
        if (_reentry != 0 || _items.Count == 0) return;
        try
        {
            if (!__result && Held(key)) __result = true;
            // BepInEx KeyboardShortcut.IsDown() also requires "no other key held", and the legacy input reports the
            // controller's face/shoulder buttons as JoystickButton*: hide those while an injected key is down (review #3).
            else if (__result && key >= KeyCode.JoystickButton0 && key <= KeyCode.Joystick8Button19 && _held.Count > 0) __result = false;
        }
        catch { }
    }'''),
('''    private static void LegacyDown(KeyCode key, ref bool __result) { if (!__result && _reentry == 0 && _items.Count > 0 && Down(key)) __result = true; }
    private static void LegacyUp(KeyCode key, ref bool __result) { if (!__result && _reentry == 0 && _items.Count > 0 && Up(key)) __result = true; }''',
 '''    private static void LegacyDown(KeyCode key, ref bool __result) { if (!__result && _reentry == 0 && _items.Count > 0) try { if (Down(key)) __result = true; } catch { } }
    private static void LegacyUp(KeyCode key, ref bool __result) { if (!__result && _reentry == 0 && _items.Count > 0) try { if (Up(key)) __result = true; } catch { } }'''),
('''        if (_reentry > 0 || _items.Count == 0) return;
        _reentry++;
        try
        {
            if (control is KeyControl keyControl && control.device is Keyboard)''', '''        if (_reentry > 0 || _items.Count == 0) return;
        _reentry++;
        try
        {
            if (control is KeyControl keyControl && control.device is Keyboard && _held.Count + _upFrame.Count > 0)'''),
('''        finally { _reentry--; }
    }
    /// <summary>While LT is held''', '''        catch { }
        finally { _reentry--; }
    }
    /// <summary>While LT is held'''),
])

# ---- #4 B / Start while recording must not leave the page ----
edit('Overview/OverviewHotkeyView.cs', [
('''        if(!modHeld&&pad.buttonEast.wasPressedThisFrame){EndComboRecording();_status(Loc.T("Recording cancelled.","已取消录制。","已取消錄製。"));return;}''',
 '''        // Any frame where recording handles B or Start is marked consumed, so the page's back handling does not also leave.
        if(pad.buttonEast.wasPressedThisFrame||pad.startButton.wasPressedThisFrame)OverviewHotkeyCapture.MarkConsumed();
        if(!modHeld&&pad.buttonEast.wasPressedThisFrame){EndComboRecording();_status(Loc.T("Recording cancelled.","已取消录制。","已取消錄製。"));return;}'''),
# #5 icon vs action use the same rule (anything folded → expand)
('''            bool anyOpen=_repo.State.groups.Any(g=>!_repo.State.collapsedGroups.Contains(g.id))||Service.Entries.Select(e=>e.Mod.Id).Distinct().Any(m=>!_repo.State.collapsedHotkeyMods.Contains(m));
            var foldAll=OverviewUi.IconButton(_tools,anyOpen?"triangle-down":"triangle-right",()=>ToggleAllFolds(),30,30);''',
 '''            bool anyFolded=_repo.State.groups.Any(g=>_repo.State.collapsedGroups.Contains(g.id))||Service.Entries.Select(e=>e.Mod.Id).Distinct().Any(m=>_repo.State.collapsedHotkeyMods.Contains(m));
            bool anyOpen=!anyFolded; // the click expands when anything is folded, otherwise collapses
            var foldAll=OverviewUi.IconButton(_tools,anyFolded?"triangle-right":"triangle-down",()=>ToggleAllFolds(),30,30);'''),
# #6 path settings: pass the raw value to the chips
('''                        if(!native)KeyChipsOn(current,entry.CurrentLabel);''', '''                        if(!native)KeyChipsOn(current,RawKey(entry));'''),
('''                        if(native)KeyChipsOn(pad,entry.CurrentLabel);''', '''                        if(native)KeyChipsOn(pad,RawKey(entry));'''),
('''    private void ModifierMenu(RectTransform anchor)''', '''    /// <summary>The stored value as text ("&lt;Gamepad&gt;/buttonSouth", "LeftControl + F5", "South"), for key chips.</summary>
    private static string RawKey(OverviewHotkeyEntry entry)
    {
        object value=entry.Config.BoxedValue;
        if(value is BepInEx.Configuration.KeyboardShortcut shortcut)return shortcut.MainKey==KeyCode.None?"":string.Join(" + ",shortcut.Modifiers.Select(m=>m.ToString()).Append(shortcut.MainKey.ToString()));
        string text=Convert.ToString(value)??"";
        return OverviewHotkeyAdapter.IsNone(text)?"":text;
    }
    private void ModifierMenu(RectTransform anchor)'''),
('''        if(combo.modifier.Length>0)keys.Add((GamepadCombos.ButtonName(combo.modifier),true));''', '''        if(combo.Mod.Length>0)keys.Add((GamepadCombos.ButtonName(combo.Mod),true));'''),
('''        KeyChips.Build(chipHost,keys,text.font,20,dimFirst:combo.modifier.Length>0,centre:true,fontSize:13);''', '''        KeyChips.Build(chipHost,keys,text.font,20,dimFirst:combo.Mod.Length>0,centre:true,fontSize:13);'''),
])
edit('Overview/OverviewHotkeyCapture.cs', [
('''    public static int ConsumedCancelFrame { get; private set; } = -1;''', '''    public static int ConsumedCancelFrame { get; private set; } = -1;
    /// <summary>1.1.3: the controller combo recorder also consumes B / Start this frame.</summary>
    internal static void MarkConsumed() => ConsumedCancelFrame = UnityEngine.Time.frameCount;'''),
])
edit('Overview/OverviewHotkeyService.cs', [
('''            if (combo.modifier.Length == 0)''', '''            if (combo.Mod.Length == 0)'''),
])

# ---- #8 path-type keyboard settings: say combos may not reach them ----
edit('Overview/OverviewHotkeyView.cs', [
('''            +"\\n"+keyboard+"\\n"+Loc.T("The game still does what that button normally does, unless it is the second key of a modifier combo.","这个手柄键原本在游戏里的操作照常触发；只有修饰键组合里的第二个键不会。","這個手把鍵原本在遊戲裡的操作照常觸發；只有修飾鍵組合裡的第二個鍵不會。");''',
 '''            +"\\n"+keyboard+"\\n"+Loc.T("The game still does what that button normally does, unless it is the second key of a modifier combo.","这个手柄键原本在游戏里的操作照常触发；只有修饰键组合里的第二个键不会。","這個手把鍵原本在遊戲裡的操作照常觸發；只有修飾鍵組合裡的第二個鍵不會。")
            +(entry.Adapter.IsPath?"\\n"+Loc.T("This mod reads an input path; some such mods do not see controller combos.","这个模组按输入路径读键，部分这类模组收不到手柄组合。","這個模組按輸入路徑讀鍵，部分這類模組收不到手把組合。"):"");'''),
])

# ---- #10 RecoverFocus: only when the selection changes ----
edit('Overview/OverviewListView.Gamepad.cs', [
('''        if (selected != null && selected.activeInHierarchy)
        {
            if (selected.transform.IsChildOf(_content)) { var row = RowOf(selected); if (row?.View != null) _lastGamepadFocus = (row.Key, Math.Max(0, Array.FindIndex(RowControls(row.View), c => c.gameObject == selected))); }
            return;
        }''', '''        if (selected != null && selected.activeInHierarchy)
        {
            if (selected != _lastRecoverSelection && selected.transform.IsChildOf(_content))
            { var row = RowOf(selected); if (row != null && row.View != null) _lastGamepadFocus = (row.Key, Math.Max(0, Array.FindIndex(RowControls(row.View), c => c.gameObject == selected))); }
            _lastRecoverSelection = selected;
            return;
        }
        _lastRecoverSelection = null;'''),
('''    private (string Key, int Column)? _lastGamepadFocus;''', '''    private (string Key, int Column)? _lastGamepadFocus;
    private GameObject? _lastRecoverSelection;'''),
])

# ---- #11 FitControl for every stretched-or-not text box except the small slider number box ----
edit('ModConfigTreeLayoutPatch.cs', [
('''                if (control is TMP_InputField or Slider && rect.anchorMax.x - rect.anchorMin.x >= .5f) SettingCellExtras.FitControl(rect);''',
 '''                // Text boxes (any anchoring, so the manager-profile-directory box no longer runs under its Default button)
                // and sliders; the small number box beside a slider keeps its size.
                bool sliderNumber = control is TMP_InputField && controls.GetComponentInChildren<Slider>(true) != null;
                if ((control is TMP_InputField && !sliderNumber) || control is Slider) SettingCellExtras.FitControl(rect);'''),
])
print('ok')
