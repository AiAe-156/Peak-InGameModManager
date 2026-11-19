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
/// 手柄组合键映射。0.0.2（2026-11-18，同步 MCE 1.1.0，用户要求）：取消「全局唯一修饰键」下拉，改成自由组合——
/// 先按住任意几个键（<see cref="Item.held"/>，可为空），最后按一个键（<see cref="Item.button"/>）触发，一组最多 4 个键。
/// 大多数模组的快捷键只认键盘（KeyCode / Key 类型），手柄按不出来。玩家在快捷键页给某个快捷键录一组手柄键，
/// 局内按下时，管理器替模组「按下」它的键盘键：
///   · 注入点就是打字保护已经挂上的读键钩子（Input.GetKey/GetKeyDown/GetKeyUp 与 ButtonControl 的三个属性），
///     被映射的键在这一帧读起来就是「按下」，模组完全不用改。
///   · 只屏蔽组合的最后一个键（游戏读的是 InputAction，在读值处屏蔽）；先按住的那些键游戏照常读到——做不到零干扰，
///     说明里写明，并提示 Steam 输入的动作集 / 组合键可以替代。
///   · 只在局内（有本地角色、不在暂停菜单）生效，所以与概览页自己的手柄操作（LT + LB / RB 全部展开 / 收起等）互不干扰。
///   · 保存在 config/InGameModManager/data/runtime/gamepad-combos.json（个人设置，不随整合包发出）。旧文件（单一 modifier）
///     读进来时按 <see cref="GamepadComboRules.LegacyModifier"/> 并入 held 最前面，下次保存写成新格式（version = 2）。
/// 判定规则在 GamepadComboRules.cs（纯逻辑，夹具 验证/MCE1.1.0同步_20261117/combo-checks/ 链接它）。
/// </summary>
internal static class GamepadCombos
{
    internal static readonly string[] Buttons = GamepadComboRules.Buttons;
    /// <summary>一组最多几个键（含最后一个）。</summary>
    internal const int MaxButtons = GamepadComboRules.MaxButtons;
    private const int StoreVersion = 2;

    /// <summary><see cref="modifier"/> 只在读旧文件时有值（0.0.2 以前的全局修饰键）；新文件写 <see cref="version"/>。</summary>
    private sealed class Store { public int? version; public string? modifier = null; public List<Item>? items; }
    private static readonly JsonSerializerSettings WriteSettings = new() { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented };

    /// <summary>One combo: <see cref="held"/> held + <see cref="button"/> pressed presses whatever key the mod's setting
    /// currently holds (resolved live, so re-recording the keyboard key keeps the combo working).</summary>
    [Serializable] internal sealed class Item
    {
        public string id = "", guid = "", section = "", key = "", button = "";
        public List<string> held = new();
        /// <summary>0.0.2 以前的单个修饰键（null = 1.1.2 写的，当时总是 LT）；读到时并入 <see cref="held"/>，之后不再写。</summary>
        public string? modifier;
        [JsonIgnore] internal IReadOnlyList<string> Held => held;
        [JsonIgnore] internal string SetKey => GamepadComboRules.SetKey(held, button);
        [JsonIgnore] internal ConfigEntryBase? Entry; [JsonIgnore] internal bool Resolved;
        [JsonIgnore] internal List<KeyCode>? Keys; [JsonIgnore] internal object? KeysFor;
    }

    private static List<Item> _items = new();
    private static HashSet<string> _heldButtons = new(StringComparer.Ordinal);
    private static bool _loaded;
    private static string FilePath => Path.Combine(Loc.RuntimeDirectory, "gamepad-combos.json");

    internal static IReadOnlyList<Item> Items { get { Load(); return _items; } }
    internal static Item? For(string id) { Load(); return _items.FirstOrDefault(i => i.id == id); }

    private static void Load()
    {
        if (_loaded) return; _loaded = true;
        bool migrated = false;
        try
        {
            if (File.Exists(FilePath))
            {
                // 1.1.3 review: 1.1.2 wrote this with JsonUtility, which drops List<Item> (files are "{}"); Newtonsoft now.
                var store = JsonConvert.DeserializeObject<Store>(File.ReadAllText(FilePath));
                _items = store?.items ?? new();
                bool legacy = store != null && store.version == null;
                foreach (var item in _items)
                {
                    if (item == null) continue;
                    item.held ??= new();
                    string? modifier = item.modifier;
                    if (legacy)
                    {
                        modifier = GamepadComboRules.LegacyModifier(item.modifier, store!.modifier, item.button ?? "");
                        if (modifier == null) { item.id = ""; continue; } // 旧版也会丢掉这条
                        migrated = true;
                    }
                    item.held = GamepadComboRules.NormalizeHeld(item.held, modifier, item.button ?? "");
                    item.modifier = null;
                }
            }
            _items.RemoveAll(i => i == null || string.IsNullOrEmpty(i.guid) || i.section == null || i.key == null
                || !GamepadComboRules.IsUsable(i.id, i.button, i.held.Count));
            Dedupe();
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning("[手柄组合] 读取失败，按空清单处理：" + error.Message);
            _items = new();
            // 读坏的文件先留一份 .bad，免得下次保存把玩家的原文件覆盖掉。
            try { if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".bad", true); } catch { }
        }
        Reindex();
        if (migrated)
        {
            // 0.0.2：旧格式先留一份原样副本，再写成新格式。
            try { string old = FilePath + ".before-free-combos"; if (!File.Exists(old)) File.Copy(FilePath, old); } catch { }
            Save();
            Plugin.Log.LogInfo("[手柄组合] 旧格式（全局修饰键）已迁移为自由组合：" + _items.Count + " 组。");
        }
    }

    private static void Dedupe()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        _items.RemoveAll(i => !seen.Add(i.SetKey));
    }

    private static void Reindex() => _heldButtons = new HashSet<string>(_items.SelectMany(i => i.held), StringComparer.Ordinal);

    /// <summary>最近一次写 gamepad-combos.json 是否失败（本局已生效、下次启动会丢）。快捷键页据此在状态行补一句。</summary>
    internal static bool LastSaveFailed { get; private set; }
    private static void Save()
    {
        Reindex();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(new Store { version = StoreVersion, items = _items }, WriteSettings));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
            LastSaveFailed = false;
        }
        catch (Exception error) { LastSaveFailed = true; Plugin.Log.LogWarning("[手柄组合] 保存失败：" + error.Message); }
    }

    private static Item NewItem(string id, string guid, string section, string key, IEnumerable<string> held, string button) =>
        new() { id = id, guid = guid, section = section, key = key, button = button, held = held.Where(h => h != button).Distinct().ToList() };

    /// <summary>Assigns <paramref name="held"/> + <paramref name="button"/> to the hotkey <paramref name="id"/>. One combo
    /// per exact button set: assigning it elsewhere moves it. Returns the id that lost the combo, if any.</summary>
    internal static string? Set(string id, string guid, string section, string key, IEnumerable<string> held, string button)
    {
        Load();
        var item = NewItem(id, guid, section, key, held, button);
        string? moved = _items.FirstOrDefault(i => i.SetKey == item.SetKey && i.id != id)?.id;
        _items.RemoveAll(i => i.id == id || i.SetKey == item.SetKey);
        _items.Add(item);
        Save(); return moved;
    }
    internal static void Clear(string id) { Load(); if (_items.RemoveAll(i => i.id == id) > 0) Save(); }

    /// <summary>方案里的手柄预设（作者在「方案预设」栏录的，随整合包发出）。旧方案的单个 modifier 读到时并入 held。</summary>
    internal static Item? PresetFor(Overview.OverviewState state, string id)
    {
        var preset = state.gamepadComboPresets?.FirstOrDefault(i => i.id == id);
        if (preset != null) NormalizePreset(preset);
        return preset;
    }
    private static void NormalizePreset(Item preset)
    {
        if (preset.modifier == null && preset.held != null) return;
        preset.held = GamepadComboRules.NormalizeHeld(preset.held, preset.modifier, preset.button ?? "");
        preset.modifier = null;
    }
    internal static void SetPreset(Overview.OverviewState state, string id, string guid, string section, string key, IEnumerable<string> held, string button)
    {
        state.gamepadComboPresets ??= new();
        foreach (var preset in state.gamepadComboPresets) NormalizePreset(preset);
        var item = NewItem(id, guid, section, key, held, button);
        state.gamepadComboPresets.RemoveAll(i => i.id == id || i.SetKey == item.SetKey);
        state.gamepadComboPresets.Add(item);
    }
    /// <summary>Profile applied: its controller presets replace this PC's combos for the same hotkeys.
    /// 0.0.2：预设按它自己录下的键生效（不再换成本机的共用修饰键）。</summary>
    internal static void ApplyPresets(Overview.OverviewState state)
    {
        if (state.gamepadComboPresets == null || state.gamepadComboPresets.Count == 0) return;
        Load();
        foreach (var preset in state.gamepadComboPresets)
        {
            if (preset == null) continue;
            NormalizePreset(preset);
            if (!GamepadComboRules.IsUsable(preset.id, preset.button, preset.held.Count)) continue;
            var item = NewItem(preset.id, preset.guid, preset.section, preset.key, preset.held, preset.button);
            _items.RemoveAll(i => i.id == item.id || i.SetKey == item.SetKey);
            _items.Add(item);
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

    /// <summary>The value to store in a controller-native setting for <paramref name="button"/>; null when it cannot hold it.</summary>
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

    /// <summary>The controller button a controller-native setting currently holds (a combo for such a setting presses
    /// that button for the mod).</summary>
    internal static ButtonControl? NativeControl(ConfigEntryBase entry, Gamepad pad)
    {
        string raw = Convert.ToString(entry.BoxedValue) ?? "";
        if (raw.Length == 0 || raw.Equals("None", StringComparison.OrdinalIgnoreCase)) return null;
        string token = raw.StartsWith("<", StringComparison.Ordinal) ? raw.Substring(raw.IndexOf('/') + 1) : raw;
        if (token.Length == 0) return null; // "<Gamepad>/" 之类：下面 other[0] 会越界
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
    internal static IEnumerable<string> ButtonsOf(Item item) => item.held.Append(item.button);
    internal static string Label(Item item) => string.Join(" + ", ButtonsOf(item).Select(ButtonName));
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
    /// <summary>Controller buttons pressed virtually for controller-native settings.</summary>
    private static readonly HashSet<ButtonControl> _pads = new(), _nowPads = new();
    private static readonly Dictionary<ButtonControl, int> _padDown = new(), _padUp = new();
    private static readonly Dictionary<Item, int> _pressedAt = new();
    private static readonly Dictionary<Key, KeyCode> _keyMap = new();
    private static readonly Dictionary<string, int> _heldSince = new(StringComparer.Ordinal);
    private static int _frame = -1;

    /// <summary>Updates the injected key states once per frame (called from the key-read hooks).</summary>
    internal static void Tick()
    {
        if (_frame == Time.frameCount) return; _frame = Time.frameCount;
        Load();
        if (_items.Count == 0 && _held.Count == 0 && _pads.Count == 0) return;
        try
        {
            var pad = Gamepad.current;
            _nowHeld.Clear(); _nowPads.Clear();
            if (pad != null) TrackHeld(pad);
            if (pad != null && !Typing() && _items.Count > 0 && InGame())
                foreach (var item in _items)
                {
                    // 第 5 章（二次审查）：逐条保护，一条设置读坏只跳过它，不让整批组合失效。
                    try
                    {
                        var control = Control(pad, item.button);
                        if (control == null || !Pressed(control)) { _pressedAt.Remove(item); continue; }
                        if (!_pressedAt.ContainsKey(item)) _pressedAt[item] = Time.frameCount;
                        if (!Satisfied(pad, item)) continue;
                        var entry = Resolve(item); if (entry == null) continue;
                        if (NativeGamepad(entry)) { var native = NativeControl(entry, pad); if (native != null) _nowPads.Add(native); }
                        else foreach (var code in CachedKeys(item, entry)) _nowHeld.Add(code);
                    }
                    catch (Exception error) { WarnOnce("[手柄组合] 「" + item.guid + " / " + item.key + "」更新失败：" + error.Message); }
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

    /// <summary>
    /// 这一组的「先按住」键全部按着，并且没有别的「先按住」键多按着（多按了说明玩家想要的是另一组，比如
    /// 「LT + A」不该同时触发单键「A」）。
    /// </summary>
    private static bool Satisfied(Gamepad pad, Item item) =>
        GamepadComboRules.Satisfied(item.held, item.button, _heldButtons, name => Control(pad, name) is { } control && Pressed(control));

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

    /// <summary>输入框聚焦时（打字保护）不注入、不屏蔽。MCE 另加了「局内别的模组的输入框聚焦」判断（TypingGuard.FieldFocusedNow），
    /// IGMM 的打字保护没有这个成员，0.0.2 未搬，见同步报告。</summary>
    private static bool Typing() => TypingGuard.Active;

    /// <summary>Combos act only while playing, not in the pause menu (where this mod's pages are used).</summary>
    private static bool InGame()
    {
        try { return Character.localCharacter != null && !GUIManager.InPauseMenu; } catch { return false; }
    }

    internal static bool Held(KeyCode code) { Tick(); return _held.Contains(code); }
    internal static bool Down(KeyCode code) { Tick(); return _downFrame.TryGetValue(code, out int f) && f == Time.frameCount; }
    internal static bool Up(KeyCode code) { Tick(); return _upFrame.TryGetValue(code, out int f) && f == Time.frameCount; }

    /// <summary>The control is the last button of a multi-button combo whose held buttons are down: the game should not
    /// also act on it (LT + A must not jump). Single-button combos are not masked: the game keeps that button too.</summary>
    internal static bool ComboButtonHeld(ButtonControl control)
    {
        var pad = Gamepad.current; if (pad == null || control.device != pad || _items.Count == 0 || !InGame()) return false;
        foreach (var item in _items)
        {
            if (item.held.Count == 0 || Control(pad, item.button) != control || !Satisfied(pad, item)) continue;
            // Only a press that began after every held button was down: holding RT to climb and then pressing LT must
            // not make the game drop RT.
            int? since = _pressedAt.TryGetValue(item, out int at) ? at : null;
            if (GamepadComboRules.MaskLastButton(item.held, since, h => _heldSince.TryGetValue(h, out int down) ? down : null)) return true;
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
    /// <summary>Postfixes on the key reads (Input.GetKey* and ButtonControl's three properties) and on InputAction's
    /// reads. Postfix so the typing guard still wins while a text box has focus.</summary>
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
        // The game reads its own actions (Jump, Interact…) through InputAction; mask the combo's last button there.
        foreach (var name in new[] { "WasPressedThisFrame", "IsPressed", "WasPerformedThisFrame" })
        {
            var method = AccessTools.Method(typeof(InputAction), name, Type.EmptyTypes);
            if (method == null || method.GetMethodBody() == null) continue;
            try { harmony.Patch(method, postfix: new HarmonyMethod(typeof(GamepadCombos), nameof(ActionMask))); count++; } catch (Exception e) { Plugin.Log.LogWarning("[手柄组合] InputAction." + name + " 挂不上：" + e.Message); }
        }
        Plugin.Log.LogInfo("[手柄组合] 已挂载 " + count + "/9 个读键点。");
    }

    private static int _reentry;
    /// <summary>Frame each "held" button went down.</summary>
    private static void TrackHeld(Gamepad pad)
    {
        foreach (var name in _heldButtons)
        {
            var control = Control(pad, name); bool down = control != null && Pressed(control);
            if (down && !_heldSince.ContainsKey(name)) _heldSince[name] = Time.frameCount;
            else if (!down) _heldSince.Remove(name);
        }
    }

    private static void LegacyHeld(KeyCode key, ref bool __result)
    {
        if (_reentry != 0 || _items.Count == 0) return;
        try
        {
            if (!__result && Held(key)) __result = true;
            // BepInEx KeyboardShortcut.IsDown() also requires "no other key held", and the legacy input reports the
            // controller's face/shoulder buttons as JoystickButton*: hide those while an injected key is down.
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

    /// <summary>While a combo's held buttons are down, its last button does not also fire the game's own action.</summary>
    private static void ActionMask(InputAction __instance, ref bool __result)
    {
        if (!__result || _reentry > 0 || _items.Count == 0 || Typing()) return;
        _reentry++;
        try
        {
            var active = __instance.activeControl as ButtonControl;
            if (active != null && ComboButtonHeld(active)) __result = false;
        }
        finally { _reentry--; }
    }
}
