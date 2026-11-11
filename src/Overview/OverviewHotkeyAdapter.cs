#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ModConfigEnhance.Overview;

/// <summary>Only recognize explicit input types and defaults, never setting-name heuristics.</summary>
internal sealed class OverviewHotkeyAdapter
{
    internal readonly ConfigEntryBase Config;
    private readonly string[]? _choices;
    internal bool IsPath { get; }
    internal bool IsShortcut => Config.SettingType == typeof(KeyboardShortcut);
    private OverviewHotkeyAdapter(ConfigEntryBase config, bool path, string[]? choices) { Config = config; IsPath = path; _choices = choices; }
    internal static OverviewHotkeyAdapter? Create(ConfigEntryBase config)
    {
        if (config.Description?.Tags?.Any(t => t is string s && s.Equals("Hidden", StringComparison.OrdinalIgnoreCase)) == true) return null;
        Type type = config.SettingType;
        if (type == typeof(KeyCode) || type == typeof(Key) || type == typeof(GamepadButton) || type == typeof(KeyboardShortcut)) return new(config, false, null);
        if (type != typeof(string)) return null;
        if (TryPath(Convert.ToString(config.DefaultValue) ?? "", out _, out _)) return new(config, true, null);
        if (config.Description?.AcceptableValues is AcceptableValueList<string> list && list.AcceptableValues.Length > 0 &&
            list.AcceptableValues.All(v => TryNamedKey(v, out _, out _))) return new(config, false, list.AcceptableValues);
        return null;
    }
    internal bool CanClear => TryConvert(EmptyValue, out _, out _);
    internal object EmptyValue => IsShortcut ? new KeyboardShortcut(KeyCode.None) : Config.SettingType == typeof(KeyCode) ? KeyCode.None :
        Config.SettingType == typeof(Key) ? Key.None : Config.SettingType == typeof(GamepadButton) ? (object)"None" : IsPath ? "" : _choices?.FirstOrDefault(IsNone) ?? "None";
    internal static bool IsNone(string value) => string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase);
    internal bool TryConvert(object value, out object converted, out string error)
    {
        converted = value; error = "";
        try
        {
            Type type = Config.SettingType;
            if (type == typeof(string))
            {
                if (value is not string text) throw new ArgumentException(Loc.T("This setting only accepts its original string format.", "该配置项只接受原有字符串格式。", "該配置項只接受原有字符串格式。"));
                if (IsPath && !IsNone(text) && !TryPath(text, out _, out _)) throw new ArgumentException(Loc.T("Unrecognized input path.", "无法识别这个输入路径。", "無法識別這個輸入路徑。"));
                if (_choices != null)
                {
                    string? choice = _choices.FirstOrDefault(c => c.Equals(text, StringComparison.OrdinalIgnoreCase));
                    if (choice == null) throw new ArgumentException(Loc.T("This key is not allowed by the original mod.", "这个键不在原模组允许的按键清单中。", "這個鍵不在原模組允許的按鍵清單中。"));
                    converted = choice;
                }
            }
            else if (!type.IsInstanceOfType(value))
            {
                if (value is not string serialized) throw new ArgumentException(Loc.T("This setting cannot save this key type or key combination.", "该配置项不能保存这种按键类型或组合键。", "該配置項不能保存這種按鍵類型或組合鍵。"));
                converted = TomlTypeConverter.ConvertToValue(serialized, type);
            }
            if (type.IsEnum && !Enum.IsDefined(type, converted)) throw new ArgumentException(Loc.T("The original mod does not support this key.", "原模组不支持这个按键。", "原模組不支持這個按鍵。"));
            if (Config.Description?.AcceptableValues is AcceptableValueBase acceptable && !acceptable.IsValid(converted))
                throw new ArgumentException(Loc.T("The original mod does not allow this key to be saved.", "原模组不允许保存这个按键。", "原模組不允許保存這個按鍵。"));
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }
    internal string Serialize(object value) => TomlTypeConverter.ConvertToString(value, Config.SettingType);
    internal object Deserialize(string value) => TomlTypeConverter.ConvertToValue(value, Config.SettingType);
    internal string Label(object value)
    {
        if (Signature(value).Length == 0) return Loc.T("None", "未绑定", "未綁定");
        return IsPath && TryPath(Convert.ToString(value) ?? "", out string device, out string control) ? device + " / " + control : Convert.ToString(value) ?? "";
    }
    internal string Signature(object value)
    {
        if (value is KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return "";
            string main = Canonical(shortcut.MainKey.ToString());
            string device = main.StartsWith("mouse") ? "mouse" : main.StartsWith("joystick") ? "gamepad-legacy" : "keyboard";
            return device + ":" + main + ":" + string.Join("+", shortcut.Modifiers.Select(k => Canonical(k.ToString())).Distinct().OrderBy(s => s, StringComparer.Ordinal));
        }
        string text = Convert.ToString(value) ?? "";
        if (IsNone(text)) return "";
        if (IsPath)
        {
            if (!TryPath(text, out string device, out string control)) return "unknown:" + text + ":";
            return device.ToLowerInvariant() + ":" + Canonical(control) + ":";
        }
        if (Config.SettingType == typeof(GamepadButton)) return "gamepad:" + GamepadToken(text) + ":";
        if (Config.SettingType == typeof(string) && TryNamedKey(text, out string namedDevice, out string namedKey)) return namedDevice + ":" + namedKey + ":";
        string key = Canonical(text);
        return (key.StartsWith("mouse") ? "mouse" : key.StartsWith("joystick") ? "gamepad-legacy" : "keyboard") + ":" + key + ":";
    }
    internal static bool Overlaps(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        var x = a.Split(':'); var y = b.Split(':');
        if (x.Length != 3 || y.Length != 3 || x[0] != y[0] || x[1] != y[1]) return false;
        // Modifier-less bindings may still trigger under modifiers. Modifier sets with a
        // subset relationship can overlap; opposing Shift/Ctrl-only shortcuts stay distinct.
        var xm = x[2].Split(new[] {'+'}, StringSplitOptions.RemoveEmptyEntries);
        var ym = y[2].Split(new[] {'+'}, StringSplitOptions.RemoveEmptyEntries);
        return xm.All(ym.Contains) || ym.All(xm.Contains);
    }
    internal static string Canonical(string key)
    {
        string k = key.ToLowerInvariant().Replace(" ", "");
        if (k.StartsWith("alpha")) k = "digit" + k.Substring(5);
        if (k.StartsWith("keypad")) k = "numpad" + k.Substring(6);
        if (k.Length == 1 && char.IsDigit(k[0])) k = "digit" + k;
        return k switch { "leftcontrol" => "leftctrl", "rightcontrol" => "rightctrl", "leftcommand" or "leftwindows" => "leftmeta", "rightcommand" or "rightwindows" => "rightmeta",
            "return" => "enter", "uparrow" => "uparrow", "downarrow" => "downarrow", "leftarrow" => "leftarrow", "rightarrow" => "rightarrow",
            "south" or "a" when key.Length > 1 => "buttonsouth", "east" => "buttoneast", "west" => "buttonwest", "north" => "buttonnorth",
            "dpadup" => "dpad/up", "dpaddown" => "dpad/down", "dpadleft" => "dpad/left", "dpadright" => "dpad/right",
            "leftbutton" => "mouse0", "rightbutton" => "mouse1", "middlebutton" => "mouse2", "forwardbutton" => "mouse3", "backbutton" => "mouse4", _ => k };
    }
    private static string GamepadToken(string key) => key.ToLowerInvariant() switch { "a" => "buttonsouth", "b" => "buttoneast", "x" => "buttonwest", "y" => "buttonnorth", "leftstick" => "leftstickpress", "rightstick" => "rightstickpress", _ => Canonical(key) };
    internal static bool TryNamedKey(string value, out string device, out string key)
    {
        device = "keyboard"; key = "";
        if (IsNone(value)) return true;
        if (Enum.TryParse(value, true, out KeyCode legacy) && Enum.IsDefined(typeof(KeyCode), legacy))
        { key = Canonical(legacy.ToString()); device = key.StartsWith("mouse") ? "mouse" : key.StartsWith("joystick") ? "gamepad-legacy" : "keyboard"; return true; }
        if (Enum.TryParse(value, true, out Key input) && Enum.IsDefined(typeof(Key), input)) { key = Canonical(input.ToString()); return true; }
        if (Enum.TryParse(value, true, out GamepadButton button) && Enum.IsDefined(typeof(GamepadButton), button)) { key = GamepadToken(button.ToString()); device = "gamepad"; return true; }
        return false;
    }
    internal static bool TryPath(string path, out string device, out string control)
    {
        device = ""; control = "";
        Match match = Regex.Match(path, @"^(?:<(?<d>[A-Za-z0-9_-]+)>|/(?<d>[A-Za-z0-9_-]+))/(?<c>[A-Za-z0-9/_-]+)$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        string layout = match.Groups["d"].Value; control = match.Groups["c"].Value;
        string normalized = "<" + layout + ">/" + control;
        try
        {
            if (!PEAKLib.ModConfig.InputBindingPath.IsValid(normalized)) return false;
            device = PEAKLib.ModConfig.InputBindingPath.GetDevice(normalized).ToString().ToLowerInvariant();
            return true;
        }
        catch { return false; }
    }
    internal object? FromControl(InputControl control, IEnumerable<KeyCode> modifiers)
    {
        if (IsPath) return "<" + (control.device is Keyboard ? "Keyboard" : control.device is Mouse ? "Mouse" : control.device is Gamepad ? "Gamepad" : control.device.layout) + ">/" + control.path.Substring(control.device.path.Length + 1);
        string name = control.name;
        if (Config.SettingType == typeof(Key) && control is UnityEngine.InputSystem.Controls.KeyControl kc) return kc.keyCode;
        if (Config.SettingType == typeof(GamepadButton) && control.device is Gamepad)
        {
            string enumName = name switch { "buttonSouth" => "South", "buttonEast" => "East", "buttonWest" => "West", "buttonNorth" => "North", "leftStickPress" => "LeftStick", "rightStickPress" => "RightStick", _ => name };
            if (control.parent?.name == "dpad") enumName = "Dpad" + name;
            if (Enum.TryParse(enumName, true, out GamepadButton button) && Enum.IsDefined(typeof(GamepadButton), button)) return button;
            return null;
        }
        if (_choices != null) return _choices.FirstOrDefault(c => TryNamedKey(c, out string d, out string k) && d == (control.device is Keyboard ? "keyboard" : control.device is Mouse ? "mouse" : "gamepad") && k == Canonical(name));
        KeyCode legacyKey;
        if (control.device is Mouse) name = Canonical(name);
        else if (control.device is Gamepad) return null;
        if (name.StartsWith("digit", StringComparison.OrdinalIgnoreCase)) name = "Alpha" + name.Substring(5);
        if (name.StartsWith("numpad", StringComparison.OrdinalIgnoreCase)) name = "Keypad" + name.Substring(6);
        name = name switch { "enter" => "Return", "leftCtrl" => "LeftControl", "rightCtrl" => "RightControl", "leftMeta" => "LeftCommand", "rightMeta" => "RightCommand", _ => name };
        if (!Enum.TryParse(name, true, out legacyKey) || !Enum.IsDefined(typeof(KeyCode), legacyKey)) return null;
        return IsShortcut ? new KeyboardShortcut(legacyKey, modifiers.Where(m => m != legacyKey).ToArray()) : legacyKey;
    }
}
