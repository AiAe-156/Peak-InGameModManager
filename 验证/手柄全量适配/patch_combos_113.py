# -*- coding: utf-8 -*-
"""1.1.3: selectable modifier (none/LT/RT/LB/RB/Back/Start/RS/LS), per-combo modifier."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
p = os.path.abspath(os.path.join(HERE, '..', '..', 'src', 'GamepadCombos.cs'))
s = io.open(p, encoding='utf-8').read()

def rep(a, b):
    global s
    assert s.count(a) == 1, (a[:70], s.count(a))
    s = s.replace(a, b)

rep('''/// 1.1.2 手柄组合键映射（用户 2026-11-12 拍板：修饰键固定 LT，只能有一个修饰键）。''',
'''/// 1.1.2 手柄组合键映射。1.1.3（用户 2026-11-12）：修饰键改为快捷键页顶部下拉选择（无 / LT / RT / LB / RB /
/// Back / Start / RS / LS，默认「无」），全局只有一个；不强制使用：录制时按住修饰键就记成「修饰键 + 键」，
/// 不按就记成单键。每条组合记下录制时的修饰键，之后改下拉不影响已录好的。''')
rep('''    /// <summary>Fixed modifier (user decision 2026-11-12: one modifier only, LT).</summary>
    internal const string Modifier = "leftTrigger";
    internal static readonly string[] Buttons = { "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder",
        "rightTrigger", "dpad/up", "dpad/down", "dpad/left", "dpad/right", "leftStickPress", "rightStickPress", "start", "select" };''',
'''    internal static readonly string[] Buttons = { "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder",
        "leftTrigger", "rightTrigger", "dpad/up", "dpad/down", "dpad/left", "dpad/right", "leftStickPress", "rightStickPress", "start", "select" };
    /// <summary>Modifier choices for the dropdown ("" = none).</summary>
    internal static readonly string[] Modifiers = { "", "leftTrigger", "rightTrigger", "leftShoulder", "rightShoulder", "select", "start", "rightStickPress", "leftStickPress" };
    /// <summary>The modifier used when recording (dropdown at the top of the keybindings tab). Stored with the combos.</summary>
    internal static string Modifier { get { Load(); return _modifier; } set { Load(); _modifier = Modifiers.Contains(value) ? value : ""; Save(); } }
    private static string _modifier = "";''')
rep('''    [Serializable] private sealed class Store { public List<Item> items = new(); }''',
    '''    [Serializable] private sealed class Store { public string modifier = ""; public List<Item> items = new(); }''')
rep('''        public string id = "", guid = "", section = "", key = "", button = "";''',
'''        public string id = "", guid = "", section = "", key = "", button = "";
        /// <summary>The modifier held when this combo was recorded ("" = single button).</summary>
        public string modifier = "";''')
rep('''        try { if (File.Exists(FilePath)) _items = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath))?.items ?? new(); }
        catch (Exception error) { Plugin.Log.LogWarning("[手柄组合] 读取失败：" + error.Message); _items = new(); }
        _items.RemoveAll(i => string.IsNullOrEmpty(i.id) || !Buttons.Contains(i.button));''',
'''        try
        {
            if (File.Exists(FilePath))
            {
                var store = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
                _items = store?.items ?? new(); _modifier = store != null && Modifiers.Contains(store.modifier) ? store.modifier : "";
            }
        }
        catch (Exception error) { Plugin.Log.LogWarning("[手柄组合] 读取失败：" + error.Message); _items = new(); }
        _items.RemoveAll(i => string.IsNullOrEmpty(i.id) || !Buttons.Contains(i.button) || i.modifier == i.button || (i.modifier.Length > 0 && !Modifiers.Contains(i.modifier)));''')
rep('''            File.WriteAllText(temp, JsonUtility.ToJson(new Store { items = _items }, true));''',
    '''            File.WriteAllText(temp, JsonUtility.ToJson(new Store { modifier = _modifier, items = _items }, true));''')
rep('''    internal static string? Set(string id, string guid, string section, string key, string button)
    {
        Load();
        string? moved = _items.FirstOrDefault(i => i.button == button && i.id != id)?.id;
        _items.RemoveAll(i => i.id == id || i.button == button);
        _items.Add(new Item { id = id, guid = guid, section = section, key = key, button = button });
        Save(); return moved;
    }''', '''    internal static string? Set(string id, string guid, string section, string key, string modifier, string button)
    {
        Load();
        string? moved = _items.FirstOrDefault(i => i.button == button && i.modifier == modifier && i.id != id)?.id;
        _items.RemoveAll(i => i.id == id || (i.button == button && i.modifier == modifier));
        _items.Add(new Item { id = id, guid = guid, section = section, key = key, modifier = modifier, button = button });
        Save(); return moved;
    }''')
rep('''    internal static string Label(string button)
    {
        string glyph = GamepadIntent.Glyph(Modifier, out _) + " + " + ButtonName(button);
        return glyph;
    }
    internal static string ButtonName(string button) => button switch
    {
        "buttonSouth" => "A", "buttonEast" => "B", "buttonWest" => "X", "buttonNorth" => "Y", "leftShoulder" => "LB", "rightShoulder" => "RB",
        "rightTrigger" => "RT", "dpad/up" => "↑", "dpad/down" => "↓", "dpad/left" => "←", "dpad/right" => "→",
        "leftStickPress" => "L3", "rightStickPress" => "R3", "start" => "Menu", "select" => "View", _ => button
    };''', '''    internal static string ButtonName(string button) => button.Length == 0 ? Loc.T("None", "无", "無") : KeyChips.GamepadName(button);
    internal static string Label(Item item) => (item.modifier.Length > 0 ? ButtonName(item.modifier) + " + " : "") + ButtonName(item.button);''')
rep('''        "leftShoulder" => pad.leftShoulder, "rightShoulder" => pad.rightShoulder, "rightTrigger" => pad.rightTrigger,''',
    '''        "leftShoulder" => pad.leftShoulder, "rightShoulder" => pad.rightShoulder, "rightTrigger" => pad.rightTrigger, "leftTrigger" => pad.leftTrigger,''')
rep('''        var pad = Gamepad.current;
        bool modifier = pad != null && pad.leftTrigger.ReadValue() > .5f && !TypingGuard.Active;
        var nowHeld = new HashSet<KeyCode>();
        if (modifier && _items.Count > 0)
            foreach (var item in _items)
            {
                var control = Control(pad!, item.button);
                if (control == null || !control.isPressed) continue;
                var entry = Resolve(item); if (entry == null) continue;
                foreach (var code in KeysOf(entry)) nowHeld.Add(code);
            }''', '''        var pad = Gamepad.current;
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
            }''')
rep('''    internal static bool Held(KeyCode code) { Tick(); return _held.Contains(code); }''',
'''    private static bool Pressed(ButtonControl control) { _reentry++; try { return control.isPressed; } finally { _reentry--; } }
    private static bool AnyModifierHeld(Gamepad pad)
    {
        foreach (var item in _items) if (item.modifier.Length > 0) { var mod = Control(pad, item.modifier); if (mod != null && Pressed(mod)) return true; }
        return false;
    }
    /// <summary>Combos act only while playing, not in the pause menu (where the manager itself is used).</summary>
    private static bool InGame()
    {
        try { return Character.localCharacter != null && !GUIManager.InPauseMenu; } catch { return false; }
    }
    internal static bool Held(KeyCode code) { Tick(); return _held.Contains(code); }''')
rep('''    /// <summary>The combo's second button is held together with LT (the game should not also act on it).</summary>
    internal static bool ComboButtonHeld(ButtonControl control)
    {
        var pad = Gamepad.current; if (pad == null || control.device != pad || _items.Count == 0) return false;
        if (pad.leftTrigger.ReadValue() <= .5f) return false;
        foreach (var item in _items) if (Control(pad, item.button) == control) return true;
        return false;
    }''', '''    /// <summary>The control is the second button of a modifier combo whose modifier is held: the game should not
    /// also act on it (LT + A must not jump). Single-button combos are not masked: the game keeps that button too.</summary>
    internal static bool ComboButtonHeld(ButtonControl control)
    {
        var pad = Gamepad.current; if (pad == null || control.device != pad || _items.Count == 0 || !InGame()) return false;
        foreach (var item in _items)
        {
            if (item.modifier.Length == 0 || Control(pad, item.button) != control) continue;
            var mod = Control(pad, item.modifier); if (mod != null && Pressed(mod)) return true;
        }
        return false;
    }''')
rep('''            if (active != null && active.name != Modifier && ComboButtonHeld(active)) __result = false;''',
    '''            if (active != null && ComboButtonHeld(active)) __result = false;''')
rep('''/// 「LT + 手柄键」，局内按住 LT 再按那个键时，管理器替模组「按下」它的键盘键：''', '''/// 手柄键（可带修饰键），局内按下时，管理器替模组「按下」它的键盘键：''')
io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('ok')
