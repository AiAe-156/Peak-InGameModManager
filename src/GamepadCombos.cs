#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace ModConfigEnhance;

/// <summary>
/// 1.1.2 手柄组合键映射。1.1.3（用户 2026-11-12）：修饰键改为快捷键页顶部下拉选择（无 / LT / RT / LB / RB /
/// Back / Start / RS / LS，默认「无」），全局只有一个；不强制使用：录制时按住修饰键就记成「修饰键 + 键」，
/// 不按就记成单键。每条组合记下录制时的修饰键，之后改下拉不影响已录好的。
/// 大多数模组的快捷键只认键盘（KeyCode / Key 类型），手柄按不出来。玩家在快捷键页给某个快捷键录一个
/// 手柄键（可带修饰键），局内按下时，管理器替模组「按下」它的键盘键：
///   · 注入点就是打字保护已经挂上的读键钩子（Input.GetKey/GetKeyDown/GetKeyUp 与 ButtonControl 的三个属性），
///     被映射的键在这一帧读起来就是「按下」，模组完全不用改。
///   · 按住 LT 期间的组合按下那一帧，游戏自己的 LT（副使用/投掷）照常；但组合里的第二个键（如 A=跳跃）
///     在这一帧对游戏不生效——游戏读的是 InputAction，我们在 InputSystem 更新前把它从动作里屏蔽一帧。
///     做不到完全零干扰：按住 LT 本身仍会触发游戏的副使用，这是选 LT 的代价（已告知用户）。
///   · 保存在 config/InGameModManager/data/runtime/gamepad-combos.json（个人设置，不随整合包发出）。
/// </summary>
internal static class GamepadCombos
{
    internal static readonly string[] Buttons = { "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder",
        "leftTrigger", "rightTrigger", "dpad/up", "dpad/down", "dpad/left", "dpad/right", "leftStickPress", "rightStickPress", "start", "select" };
    /// <summary>Modifier choices for the dropdown ("" = none).</summary>
    internal static readonly string[] Modifiers = { "", "leftTrigger", "rightTrigger", "leftShoulder", "rightShoulder", "select", "start", "rightStickPress", "leftStickPress" };
    /// <summary>The modifier used when recording (dropdown at the top of the keybindings tab). Stored with the combos.</summary>
    /// <summary>1.1.4 (user): changing the modifier changes every combo that uses one (they all share it); a combo whose
    /// button becomes the new modifier is dropped. Default is Back (user: "back works well as the default").</summary>
    internal static string Modifier
    {
        get { Load(); return _modifier; }
        set
        {
            Load(); string next = Modifiers.Contains(value) ? value : "";
            if (next == _modifier) return;
            if (next.Length > 0)
            {
                _items.RemoveAll(i => i.Mod.Length > 0 && i.button == next);
                foreach (var item in _items) if (item.Mod.Length > 0) item.modifier = next;
            }
            else foreach (var item in _items) if (item.Mod.Length > 0) item.modifier = ""; // "none": the combos become single buttons
            Dedupe();
            _modifier = next; Save();
        }
    }
    private static string _modifier = "select";
    private static bool _storedNone;

    private sealed class Store { public string? modifier; public List<Item>? items; }
    /// <summary>One combo: LT + <see cref="button"/> presses whatever keyboard key the mod's setting currently holds
    /// (resolved live, so re-recording the keyboard key keeps the combo working).</summary>
    [Serializable] internal sealed class Item
    {
        public string id = "", guid = "", section = "", key = "", button = "";
        /// <summary>The modifier held when this combo was recorded ("" = single button; null = written by 1.1.2, where
        /// the modifier was always LT).</summary>
        public string? modifier;
        [JsonIgnore] internal ConfigEntryBase? Entry; [JsonIgnore] internal bool Resolved;
        [JsonIgnore] internal string Mod => modifier ?? "";
        [JsonIgnore] internal List<KeyCode>? Keys; [JsonIgnore] internal object? KeysFor;
    }

    private static List<Item> _items = new();
    private static bool _loaded;
    private static string FilePath => Path.Combine(Loc.RuntimeDirectory, "gamepad-combos.json");

    internal static IReadOnlyList<Item> Items { get { Load(); return _items; } }
    internal static Item? For(string id) { Load(); return _items.FirstOrDefault(i => i.id == id); }

    private static void Load()
    {
        if (_loaded) return; _loaded = true;
        try
        {
            if (File.Exists(FilePath))
            {
                // 1.1.3 review: 1.1.2 wrote this with JsonUtility, which drops List<Item> (files are "{}"); Newtonsoft now.
                var store = JsonConvert.DeserializeObject<Store>(File.ReadAllText(FilePath));
                _items = store?.items ?? new();
                _modifier = store?.modifier != null && Modifiers.Contains(store.modifier) ? store.modifier : "select";
                _storedNone = store?.modifier == ""; // the user chose "none"; Load must not re-derive a modifier
                foreach (var item in _items) if (item.modifier == null) item.modifier = "leftTrigger"; // 1.1.2: always LT
            }
            _items.RemoveAll(i => i == null || string.IsNullOrEmpty(i.id) || !Buttons.Contains(i.button) || i.Mod == i.button || (i.Mod.Length > 0 && !Modifiers.Contains(i.Mod)));
            // 1.1.4 (user: "modifier is Back, the list still shows LT + X"): all combos share the one modifier. Files from
            // 1.1.2/1.1.3 hold combos recorded with LT while the stored modifier is something else; bring them in line.
            if (_modifier.Length == 0 && !_storedNone) { var first = _items.FirstOrDefault(i => i.Mod.Length > 0); if (first != null) _modifier = first.Mod; }
            if (_items.Any(i => i.Mod.Length > 0 && i.Mod != _modifier))
            {
                if (_modifier.Length > 0) _items.RemoveAll(i => i.Mod.Length > 0 && i.button == _modifier);
                foreach (var item in _items) if (item.Mod.Length > 0) item.modifier = _modifier;
                Dedupe(); Save();
            }
        }
        catch (Exception error) { Plugin.Log.LogWarning("[手柄组合] 读取失败：" + error.Message); _items = new(); }
    }
    /// <summary>After unifying the modifier two combos can end up on the same buttons; keep the first.</summary>
    private static void Dedupe()
    {
        var seen = new HashSet<string>();
        _items.RemoveAll(i => !seen.Add(i.Mod + "+" + i.button));
    }
    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(new Store { modifier = _modifier, items = _items }, Formatting.Indented));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
        }
        catch (Exception error) { Plugin.Log.LogWarning("[手柄组合] 保存失败：" + error.Message); }
    }

    /// <summary>Assigns LT + <paramref name="button"/> to the hotkey <paramref name="id"/>, which presses keyboard key
    /// <paramref name="key"/> (a KeyCode name). One combo per button: assigning it elsewhere moves it. Returns the id
    /// that lost the combo, if any.</summary>
    internal static string? Set(string id, string guid, string section, string key, string modifier, string button)
    {
        Load();
        string? moved = _items.FirstOrDefault(i => i.button == button && i.Mod == modifier && i.id != id)?.id;
        _items.RemoveAll(i => i.id == id || (i.button == button && i.Mod == modifier));
        _items.Add(new Item { id = id, guid = guid, section = section, key = key, modifier = modifier, button = button });
        Save(); return moved;
    }
    internal static void Clear(string id) { Load(); if (_items.RemoveAll(i => i.id == id) > 0) Save(); }
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
            string mod = preset.Mod.Length > 0 && _modifier.Length > 0 ? _modifier : preset.Mod; // shared modifier
            if (mod.Length > 0 && preset.button == mod) continue;
            _items.RemoveAll(i => i.id == preset.id || (i.button == preset.button && i.Mod == mod));
            _items.Add(new Item { id = preset.id, guid = preset.guid, section = preset.section, key = preset.key, modifier = mod, button = preset.button });
        }
        Save();
    }

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
    /// <summary>The value to store in a controller-native setting for <paramref name="button"/> (GamepadButton enum or a
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
    }

    internal static string ButtonName(string button) => button.Length == 0 ? Loc.T("None", "无", "無") : KeyChips.GamepadName(button);
    internal static string Label(Item item) => (item.Mod.Length > 0 ? ButtonName(item.Mod) + " + " : "") + ButtonName(item.button);
    internal static ButtonControl? Control(Gamepad pad, string button) => button switch
    {
        "buttonSouth" => pad.buttonSouth, "buttonEast" => pad.buttonEast, "buttonWest" => pad.buttonWest, "buttonNorth" => pad.buttonNorth,
        "leftShoulder" => pad.leftShoulder, "rightShoulder" => pad.rightShoulder, "rightTrigger" => pad.rightTrigger, "leftTrigger" => pad.leftTrigger,
        "dpad/up" => pad.dpad.up, "dpad/down" => pad.dpad.down, "dpad/left" => pad.dpad.left, "dpad/right" => pad.dpad.right,
        "leftStickPress" => pad.leftStickButton, "rightStickPress" => pad.rightStickButton, "start" => pad.startButton, "select" => pad.selectButton,
        _ => null
    };

    // ── 局内注入 ───────────────────────────────────────────────

    private static readonly Dictionary<KeyCode, int> _downFrame = new(), _upFrame = new();
    private static readonly HashSet<KeyCode> _held = new(), _nowHeld = new();
    /// <summary>1.1.4: controller buttons pressed virtually for controller-native settings (a combo for such a setting
    /// makes the mod see its own button go down). Edge frames tracked like the keyboard keys.</summary>
    private static readonly HashSet<ButtonControl> _pads = new(), _nowPads = new();
    private static readonly Dictionary<ButtonControl, int> _padDown = new(), _padUp = new();
    private static readonly Dictionary<Item, int> _pressedAt = new();
    private static readonly Dictionary<Key, KeyCode> _keyMap = new();
    private static int _frame = -1;

    /// <summary>Updates the injected key states once per frame (called from the key-read hooks and a driver).</summary>
    internal static void Tick()
    {
        if (_frame == Time.frameCount) return; _frame = Time.frameCount;
        Load();
        if (_items.Count == 0 && _held.Count == 0 && _pads.Count == 0) return;
        try
        {
            var pad = Gamepad.current;
            _nowHeld.Clear(); _nowPads.Clear();
            if (pad != null) TrackModifiers(pad);
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
                    if (NativeGamepad(entry)) { var native = NativeControl(entry, pad); if (native != null) _nowPads.Add(native); }
                    else foreach (var code in CachedKeys(item, entry)) _nowHeld.Add(code);
                }
            foreach (var code in _nowHeld) if (!_held.Contains(code)) _downFrame[code] = Time.frameCount;
            foreach (var code in _held) if (!_nowHeld.Contains(code)) _upFrame[code] = Time.frameCount;
            _held.Clear(); foreach (var code in _nowHeld) _held.Add(code);
            foreach (var control in _nowPads) if (!_pads.Contains(control)) _padDown[control] = Time.frameCount;
            foreach (var control in _pads) if (!_nowPads.Contains(control)) _padUp[control] = Time.frameCount;
            _pads.Clear(); foreach (var control in _nowPads) _pads.Add(control);
        }
        catch (Exception error) { WarnOnce("[手柄组合] 更新失败：" + error.Message); _held.Clear(); }
    }
    private static bool Pressed(ButtonControl control) { _reentry++; try { return control.isPressed; } finally { _reentry--; } }
    private static bool _warned;
    private static void WarnOnce(string message) { if (_warned) return; _warned = true; Plugin.Log.LogWarning(message); }
    /// <summary>Keys of the setting, re-parsed only when its value changes.</summary>
    private static List<KeyCode> CachedKeys(Item item, ConfigEntryBase entry)
    {
        object? value = entry.BoxedValue;
        if (item.Keys == null || !Equals(item.KeysFor, value)) { item.Keys = KeysOf(entry); item.KeysFor = value; }
        return item.Keys;
    }
    private static bool AnyModifierHeld(Gamepad pad)
    {
        foreach (var item in _items) if (item.Mod.Length > 0) { var mod = Control(pad, item.Mod); if (mod != null && Pressed(mod)) return true; }
        return false;
    }
    /// <summary>Combos act only while playing, not in the pause menu (where the manager itself is used).</summary>
    private static bool InGame()
    {
        try { return Character.localCharacter != null && !GUIManager.InPauseMenu; } catch { return false; }
    }
    internal static bool Held(KeyCode code) { Tick(); return _held.Contains(code); }
    internal static bool Down(KeyCode code) { Tick(); return _downFrame.TryGetValue(code, out int f) && f == Time.frameCount; }
    internal static bool Up(KeyCode code) { Tick(); return _upFrame.TryGetValue(code, out int f) && f == Time.frameCount; }
    /// <summary>The control is the second button of a modifier combo whose modifier is held: the game should not
    /// also act on it (LT + A must not jump). Single-button combos are not masked: the game keeps that button too.</summary>
    internal static bool ComboButtonHeld(ButtonControl control)
    {
        var pad = Gamepad.current; if (pad == null || control.device != pad || _items.Count == 0 || !InGame()) return false;
        foreach (var item in _items)
        {
            if (item.Mod.Length == 0 || Control(pad, item.button) != control) continue;
            var mod = Control(pad, item.Mod); if (mod == null || !Pressed(mod)) continue;
            // Only a press that began while the modifier was down: holding RT to climb and then pressing LT must not
            // make the game drop RT (review #7).
            if (_pressedAt.TryGetValue(item, out int since) && _modifierSince.TryGetValue(item.Mod, out int modSince) && since < modSince) continue;
            return true;
        }
        return false;
    }

    internal static KeyCode ToKeyCode(Key key)
    {
        if (_keyMap.TryGetValue(key, out var code)) return code;
        string name = key.ToString();
        if (name.StartsWith("Digit", StringComparison.Ordinal)) name = "Alpha" + name.Substring(5);
        else if (name.StartsWith("Numpad", StringComparison.Ordinal)) name = name == "NumpadEnter" ? "KeypadEnter" : "Keypad" + name.Substring(6);
        name = name switch { "Enter" => "Return", "LeftCtrl" => "LeftControl", "RightCtrl" => "RightControl", "LeftMeta" => "LeftCommand", "RightMeta" => "RightCommand",
            "Backquote" => "BackQuote", _ => name };
        code = Enum.TryParse(name, true, out KeyCode parsed) ? parsed : KeyCode.None;
        _keyMap[key] = code; return code;
    }

    // ── 钩子 ──────────────────────────────────────────────────

    private static bool _installed;
    /// <summary>Postfixes on the same key reads the typing guard prefixes (Input.GetKey* and ButtonControl's three
    /// properties). Postfix so the typing guard still wins while a text box has focus.</summary>
    internal static void Install(Harmony harmony)
    {
        if (_installed) return; _installed = true;
        Load();
        int count = 0;
        foreach (var (name, post) in new[] { ("GetKey", nameof(LegacyHeld)), ("GetKeyDown", nameof(LegacyDown)), ("GetKeyUp", nameof(LegacyUp)) })
        {
            var method = AccessTools.Method(typeof(Input), name, new[] { typeof(KeyCode) });
            if (method == null || method.GetMethodBody() == null) continue;
            try { harmony.Patch(method, postfix: new HarmonyMethod(typeof(GamepadCombos), post)); count++; } catch (Exception e) { Plugin.Log.LogWarning("[手柄组合] Input." + name + " 挂不上：" + e.Message); }
        }
        foreach (var (name, post) in new[] { ("isPressed", nameof(ButtonHeld)), ("wasPressedThisFrame", nameof(ButtonDown)), ("wasReleasedThisFrame", nameof(ButtonUp)) })
        {
            var getter = AccessTools.PropertyGetter(typeof(ButtonControl), name);
            if (getter == null || getter.GetMethodBody() == null) continue;
            try { harmony.Patch(getter, postfix: new HarmonyMethod(typeof(GamepadCombos), post)); count++; } catch (Exception e) { Plugin.Log.LogWarning("[手柄组合] ButtonControl." + name + " 挂不上：" + e.Message); }
        }
        // The game reads its own actions (Jump, Interact…) through InputAction; mask the combo button there.
        foreach (var name in new[] { "WasPressedThisFrame", "IsPressed", "WasPerformedThisFrame" })
        {
            var method = AccessTools.Method(typeof(InputAction), name, Type.EmptyTypes);
            if (method == null || method.GetMethodBody() == null) continue;
            try { harmony.Patch(method, postfix: new HarmonyMethod(typeof(GamepadCombos), nameof(ActionMask))); count++; } catch (Exception e) { Plugin.Log.LogWarning("[手柄组合] InputAction." + name + " 挂不上：" + e.Message); }
        }
        Plugin.Log.LogInfo("[手柄组合] 已挂载 " + count + "/9 个读键点。");
    }

    private static int _reentry;
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
    }
    private static void LegacyHeld(KeyCode key, ref bool __result)
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
    }
    private static void LegacyDown(KeyCode key, ref bool __result) { if (!__result && _reentry == 0 && _items.Count > 0) try { if (Down(key)) __result = true; } catch { } }
    private static void LegacyUp(KeyCode key, ref bool __result) { if (!__result && _reentry == 0 && _items.Count > 0) try { if (Up(key)) __result = true; } catch { } }
    private static void ButtonHeld(ButtonControl __instance, ref bool __result) => Button(__instance, ref __result, 0);
    private static void ButtonDown(ButtonControl __instance, ref bool __result) => Button(__instance, ref __result, 1);
    private static void ButtonUp(ButtonControl __instance, ref bool __result) => Button(__instance, ref __result, 2);
    private static void Button(ButtonControl control, ref bool result, int kind)
    {
        if (_reentry > 0 || _items.Count == 0) return;
        _reentry++;
        try
        {
            if (control is KeyControl keyControl && control.device is Keyboard && _held.Count + _upFrame.Count > 0)
            {
                if (result) return;
                var code = ToKeyCode(keyControl.keyCode); if (code == KeyCode.None) return;
                result = kind == 0 ? Held(code) : kind == 1 ? Down(code) : Up(code);
            }
            else if (!result && control.device is Gamepad && (_pads.Count > 0 || _padUp.Count > 0))
            {
                Tick();
                result = kind == 0 ? _pads.Contains(control)
                    : kind == 1 ? _padDown.TryGetValue(control, out int d) && d == Time.frameCount
                    : _padUp.TryGetValue(control, out int u) && u == Time.frameCount;
            }
        }
        catch { }
        finally { _reentry--; }
    }
    /// <summary>While LT is held, the combo's second button does not also fire the game's own action.</summary>
    private static void ActionMask(InputAction __instance, ref bool __result)
    {
        if (!__result || _reentry > 0 || _items.Count == 0 || TypingGuard.Active) return;
        _reentry++;
        try
        {
            var active = __instance.activeControl as ButtonControl;
            if (active != null && ComboButtonHeld(active)) __result = false;
        }
        finally { _reentry--; }
    }
}
