# -*- coding: utf-8 -*-
"""1.1.2: GamepadCombos resolves keys live from the mod setting; hook install in Plugin.cs."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))

def edit(rel, pairs, keep_nl=True):
    p = os.path.join(ROOT, rel)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (rel, a[:80], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('src/GamepadCombos.cs', [
('''using HarmonyLib;''', '''using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;'''),
('''    [Serializable] internal sealed class Item { public string id = "", button = "", key = ""; }''',
'''    /// <summary>One combo: LT + <see cref="button"/> presses whatever keyboard key the mod's setting currently holds
    /// (resolved live, so re-recording the keyboard key keeps the combo working).</summary>
    [Serializable] internal sealed class Item
    {
        public string id = "", guid = "", section = "", key = "", button = "";
        [NonSerialized] internal ConfigEntryBase? Entry; [NonSerialized] internal bool Resolved;
    }'''),
('''    internal static string? Set(string id, string button, string key)
    {
        Load();
        string? moved = _items.FirstOrDefault(i => i.button == button && i.id != id)?.id;
        _items.RemoveAll(i => i.id == id || i.button == button);
        _items.Add(new Item { id = id, button = button, key = key });
        Save(); return moved;
    }
    internal static void Clear(string id) { Load(); if (_items.RemoveAll(i => i.id == id) > 0) Save(); }
    /// <summary>The key a hotkey presses changed (the player re-recorded it): keep the combo, follow the new key.</summary>
    internal static void UpdateKey(string id, string key) { Load(); var item = _items.FirstOrDefault(i => i.id == id); if (item != null && item.key != key) { item.key = key; Save(); } }''',
'''    internal static string? Set(string id, string guid, string section, string key, string button)
    {
        Load();
        string? moved = _items.FirstOrDefault(i => i.button == button && i.id != id)?.id;
        _items.RemoveAll(i => i.id == id || i.button == button);
        _items.Add(new Item { id = id, guid = guid, section = section, key = key, button = button });
        Save(); return moved;
    }
    internal static void Clear(string id) { Load(); if (_items.RemoveAll(i => i.id == id) > 0) Save(); }

    /// <summary>Keyboard / mouse keys the setting currently holds (modifiers first, main key last); empty when
    /// unbound or not a keyboard binding.</summary>
    internal static List<KeyCode> KeysOf(ConfigEntryBase entry)
    {
        var keys = new List<KeyCode>();
        try
        {
            var adapter = Overview.OverviewHotkeyAdapter.Create(entry);
            string signature = adapter != null ? adapter.Signature(entry.BoxedValue) : "";
            var parts = signature.Split(':');
            if (parts.Length != 3 || (parts[0] != "keyboard" && parts[0] != "mouse")) return keys;
            foreach (var modifier in parts[2].Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries)) { var m = FromCanonical(modifier); if (m != KeyCode.None) keys.Add(m); }
            var main = FromCanonical(parts[1]); if (main != KeyCode.None) keys.Add(main);
        }
        catch { }
        return keys;
    }
    /// <summary>The setting holds a controller binding of its own (GamepadButton or a gamepad path): no combo needed.</summary>
    internal static bool NativeGamepad(ConfigEntryBase entry)
    {
        if (entry.SettingType == typeof(GamepadButton)) return true;
        var adapter = Overview.OverviewHotkeyAdapter.Create(entry);
        if (adapter == null) return false;
        string signature = adapter.Signature(entry.BoxedValue);
        if (signature.Length == 0) signature = adapter.Signature(entry.DefaultValue);
        return signature.StartsWith("gamepad", StringComparison.Ordinal);
    }
    private static KeyCode FromCanonical(string canonical)
    {
        if (canonical.StartsWith("mouse", StringComparison.Ordinal) && int.TryParse(canonical.Substring(5), out int button)) return KeyCode.Mouse0 + button;
        if (Enum.TryParse(canonical, true, out Key key) && key != Key.None) return ToKeyCode(key);
        return Enum.TryParse(canonical, true, out KeyCode code) ? code : KeyCode.None;
    }
    private static ConfigEntryBase? Resolve(Item item)
    {
        if (item.Resolved) return item.Entry;
        item.Resolved = true;
        if (Chainloader.PluginInfos.TryGetValue(item.guid, out var plugin) && plugin.Instance != null)
            foreach (var pair in plugin.Instance.Config)
                if (pair.Key.Section == item.section && pair.Key.Key == item.key) { item.Entry = pair.Value; break; }
        return item.Entry;
    }'''),
('''        if (modifier && _items.Count > 0)
            foreach (var item in _items)
            {
                var control = Control(pad!, item.button);
                if (control == null || !control.isPressed) continue;
                if (!Enum.TryParse(item.key, true, out KeyCode code) || code == KeyCode.None) continue;
                nowHeld.Add(code);
            }''', '''        if (modifier && _items.Count > 0)
            foreach (var item in _items)
            {
                var control = Control(pad!, item.button);
                if (control == null || !control.isPressed) continue;
                var entry = Resolve(item); if (entry == null) continue;
                foreach (var code in KeysOf(entry)) nowHeld.Add(code);
            }'''),
('''        if (_installed) return; _installed = true;
        int count = 0;''', '''        if (_installed) return; _installed = true;
        Load();
        int count = 0;'''),
])
edit('src/Plugin.cs', [('        Overview.OverviewController.Register(harmony);',
    '        Overview.OverviewController.Register(harmony);\n        GamepadCombos.Install(harmony); // 1.1.2: LT + controller button presses a keyboard-only mod hotkey')])
print('ok')
