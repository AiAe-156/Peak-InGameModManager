#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance;

/// <summary>
/// 1.1.3 按键小方块（用户 2026-11-12）：把「A」「RB」「LT」「F5」这类按键画成本模组风格的圆角小方块，
/// 不借游戏的手柄精灵（之前取不到对应字形时会显示成表情符号，见用户截图里的笑脸）。
///   · 手柄键：圆角底 + 键名；A/B/X/Y 用 Xbox 配色字，其余中性；
///   · 键盘键：同样的圆角底，深一点；
///   · 组合（修饰键 + 键）：修饰键那块调暗，功能键那块亮，中间不写「+」。
/// 设置页的默认值、快捷键页的按键按钮都走这里。
/// </summary>
internal static class KeyChips
{
    internal static readonly Color Bright = new(0.92f, 0.89f, 0.80f, 1f);
    internal static readonly Color Dim = new(0.92f, 0.89f, 0.80f, 0.42f);
    private static readonly Color Ink = new(0.12f, 0.13f, 0.15f, 1f);

    /// <summary>A short label for a controller control path or name ("buttonSouth" → "A", "&lt;Gamepad&gt;/rightTrigger" → "RT").</summary>
    internal static string GamepadName(string control)
    {
        string c = control;
        int slash = c.LastIndexOf('>'); if (slash >= 0) c = c.Substring(slash + 1).TrimStart('/');
        c = c.ToLowerInvariant().Replace(" ", "");
        return c switch
        {
            "buttonsouth" or "south" or "a" or "cross" => "A", "buttoneast" or "east" or "b" or "circle" => "B",
            "buttonwest" or "west" or "x" or "square" => "X", "buttonnorth" or "north" or "y" or "triangle" => "Y",
            "leftshoulder" => "LB", "rightshoulder" => "RB", "lefttrigger" => "LT", "righttrigger" => "RT",
            "leftstickpress" or "leftstick" => "LS", "rightstickpress" or "rightstick" => "RS",
            "start" => "Start", "select" => "Back",
            "dpad/up" or "dpadup" => "↑", "dpad/down" or "dpaddown" => "↓", "dpad/left" or "dpadleft" => "←", "dpad/right" or "dpadright" => "→",
            _ => control
        };
    }

    /// <summary>A readable name for a stored key value: KeyCode / Key names, "&lt;Keyboard&gt;/f5", "&lt;Gamepad&gt;/select",
    /// GamepadButton names. Returns (label, isGamepad).</summary>
    internal static (string Label, bool Gamepad) Describe(string raw)
    {
        string text = raw.Trim();
        if (text.Length == 0) return ("", false);
        if (text.StartsWith("<Gamepad>", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<XInputController>", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("<DualShockGamepad>", StringComparison.OrdinalIgnoreCase))
            return (GamepadName(text), true);
        if (text.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase)) text = text.Substring(11);
        else if (text.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase)) text = text.Substring(8) switch { "leftButton" => "Mouse0", "rightButton" => "Mouse1", "middleButton" => "Mouse2", var other => other };
        if (Enum.TryParse(text, true, out UnityEngine.InputSystem.LowLevel.GamepadButton _) && !Enum.TryParse(text, true, out KeyCode _))
            return (GamepadName(text), true);
        if (text.StartsWith("Joystick", StringComparison.OrdinalIgnoreCase)) return (text.Replace("Joystick", "Pad "), true);
        string key = text;
        if (key.StartsWith("Alpha", StringComparison.Ordinal) && key.Length == 6) key = key.Substring(5);
        else if (key.StartsWith("digit", StringComparison.OrdinalIgnoreCase) && key.Length == 6) key = key.Substring(5);
        key = key switch
        {
            "LeftControl" or "leftCtrl" => "LCtrl", "RightControl" or "rightCtrl" => "RCtrl", "LeftShift" or "leftShift" => "LShift", "RightShift" or "rightShift" => "RShift",
            "LeftAlt" or "leftAlt" => "LAlt", "RightAlt" or "rightAlt" => "RAlt", "Return" or "enter" => "Enter", "Escape" or "escape" => "Esc",
            "BackQuote" or "backquote" => "`", "Space" or "space" => "Space", "Mouse0" => "LMB", "Mouse1" => "RMB", "Mouse2" => "MMB",
            "UpArrow" or "upArrow" => "↑", "DownArrow" or "downArrow" => "↓", "LeftArrow" or "leftArrow" => "←", "RightArrow" or "rightArrow" => "→",
            _ => key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key.Substring(1)
        };
        return (key, false);
    }

    /// <summary>Thin key letters: the game ships LiberationSans SDF (TMP's default font), a plain regular sans. The OS-font
    /// route (MceRegularFont) fails on some PCs ("Unable to load font face for [Arial]"), which left chips on the heavy
    /// Daruma font. Labels LiberationSans cannot draw (Chinese "Not set", arrows) keep the localised game font.</summary>
    private static void SetRegular(TextMeshProUGUI text, TMP_FontAsset? fallback, string label)
    {
        // 1.1.4 (user choice): the game UI font, no outline (the earlier outline is what made it look heavy).
        var game = FontFallbackSwapper.instance != null && FontFallbackSwapper.instance.mainBaseFont != null ? FontFallbackSwapper.instance.mainBaseFont : fallback;
        if (game != null) { text.font = game; text.fontSharedMaterial = game.material; }
        text.outlineWidth = 0f;
        Overview.MceTextWeight.Apply(text);
    }

    /// <summary>1.1.4 (user choice "use the official icons"): the game draws key prompts from sprite sheets
    /// (InputSpriteData: keyboard, Xbox, PS), not with a font. Returns the sprite tag and sheet for a chip label, or
    /// false when the sheet has no icon for it (the chip then falls back to a rounded text chip).</summary>
    internal static bool OfficialIcon(string label, bool gamepad, out string tag, out TMP_SpriteAsset? sheet)
    {
        tag = ""; sheet = null;
        try
        {
            var data = Zorro.Core.SingletonAsset<InputSpriteData>.Instance; if (data == null) return false;
            if (gamepad)
            {
                string? control = label switch
                {
                    "A" => "buttonSouth", "B" => "buttonEast", "X" => "buttonWest", "Y" => "buttonNorth",
                    "LB" => "leftShoulder", "RB" => "rightShoulder", "LT" => "leftTrigger", "RT" => "rightTrigger",
                    "LS" => "leftStickPress", "RS" => "rightStickPress", "Start" => "start", "Back" => "select",
                    "↑" => "up", "↓" => "down", "←" => "left", "→" => "right", _ => null
                };
                if (control == null) return false;
                string? found = data.GetSpriteTagFromInputPathGamepad("<Gamepad>/" + control);
                sheet = GamepadIntent.SpriteSet(data);
                if (string.IsNullOrEmpty(found) || sheet == null) return false;
                tag = found!; return true;
            }
            string path = label switch
            {
                "LCtrl" => "<Keyboard>/leftCtrl", "RCtrl" => "<Keyboard>/rightCtrl", "LShift" => "<Keyboard>/leftShift", "RShift" => "<Keyboard>/rightShift",
                "LAlt" => "<Keyboard>/leftAlt", "RAlt" => "<Keyboard>/rightAlt", "Enter" => "<Keyboard>/enter", "Esc" => "<Keyboard>/esc",
                "`" => "<Keyboard>/backquote", "Space" => "<Keyboard>/space", "LMB" => "<Mouse>/leftButton", "RMB" => "<Mouse>/rightButton", "MMB" => "<Mouse>/middleButton",
                "↑" => "<Keyboard>/upArrow", "↓" => "<Keyboard>/downArrow", "←" => "<Keyboard>/leftArrow", "→" => "<Keyboard>/rightArrow",
                _ when label.StartsWith("Keypad", StringComparison.Ordinal) && label.Length > 6 => "<Keyboard>/numpad" + label.Substring(6),
                _ when label.Length > 0 => "<Keyboard>/" + char.ToLowerInvariant(label[0]) + label.Substring(1),
                _ => ""
            };
            if (path.Length == 0) return false;
            const string unknown = "<sprite=124 tint=1>"; // the sheet's "no icon" tag
            string result = data.GetSpriteTagFromInputPathKeyboard(path);
            if (string.IsNullOrEmpty(result) || result == unknown || data.keyboardSprites == null) return false;
            tag = result; sheet = data.keyboardSprites; return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>1.1.4 (user): key names in the manager's own dictionary, pure Chinese names. Letters, digits and F keys
    /// stay as they are. Accepts KeyCode names and Input System control names (any case).</summary>
    internal static string LocalName(string key)
    {
        string k = key.Trim(); string lower = k.ToLowerInvariant().Replace(" ", "");
        if (lower.StartsWith("keypad") && lower.Length > 6) lower = "numpad" + lower.Substring(6);
        if (lower.StartsWith("numpad") && lower.Length == 7 && char.IsDigit(lower[6])) return Loc.T("Numpad ", "小键盘 ", "數字鍵盤 ") + lower[6];
        return lower switch
        {
            "period" => Loc.T("Period", "句号", "句號"), "comma" => Loc.T("Comma", "逗号", "逗號"), "semicolon" => Loc.T("Semicolon", "分号", "分號"),
            "quote" => Loc.T("Quote", "引号", "引號"), "slash" => Loc.T("Slash", "斜杠", "斜線"), "backslash" => Loc.T("Backslash", "反斜杠", "反斜線"),
            "minus" => Loc.T("Minus", "减号", "減號"), "equals" => Loc.T("Equals", "等号", "等號"), "plus" => Loc.T("Plus", "加号", "加號"),
            "leftbracket" => Loc.T("Left bracket", "左方括号", "左方括號"), "rightbracket" => Loc.T("Right bracket", "右方括号", "右方括號"),
            "backquote" => Loc.T("Backquote", "反引号", "反引號"), "space" => Loc.T("Space", "空格", "空白鍵"),
            "return" or "enter" => Loc.T("Enter", "回车", "回車"), "escape" or "esc" => Loc.T("Esc", "退出键", "退出鍵"),
            "backspace" => Loc.T("Backspace", "退格", "退格"), "tab" => Loc.T("Tab", "制表键", "定位鍵"),
            "capslock" => Loc.T("Caps Lock", "大写锁定", "大寫鎖定"), "numlock" => Loc.T("Num Lock", "数字锁定", "數字鎖定"), "scrolllock" => Loc.T("Scroll Lock", "滚动锁定", "捲動鎖定"),
            "insert" => Loc.T("Insert", "插入", "插入"), "delete" => Loc.T("Delete", "删除", "刪除"), "home" => Loc.T("Home", "行首", "行首"), "end" => Loc.T("End", "行尾", "行尾"),
            "pageup" => Loc.T("Page Up", "上翻页", "上翻頁"), "pagedown" => Loc.T("Page Down", "下翻页", "下翻頁"),
            "pause" => Loc.T("Pause", "暂停键", "暫停鍵"), "print" or "printscreen" => Loc.T("Print Screen", "截屏键", "截圖鍵"),
            "uparrow" => Loc.T("Up arrow", "上方向键", "上方向鍵"), "downarrow" => Loc.T("Down arrow", "下方向键", "下方向鍵"),
            "leftarrow" => Loc.T("Left arrow", "左方向键", "左方向鍵"), "rightarrow" => Loc.T("Right arrow", "右方向键", "右方向鍵"),
            "leftshift" => Loc.T("Left Shift", "左 Shift", "左 Shift"), "rightshift" => Loc.T("Right Shift", "右 Shift", "右 Shift"),
            "leftcontrol" or "leftctrl" => Loc.T("Left Ctrl", "左 Ctrl", "左 Ctrl"), "rightcontrol" or "rightctrl" => Loc.T("Right Ctrl", "右 Ctrl", "右 Ctrl"),
            "leftalt" => Loc.T("Left Alt", "左 Alt", "左 Alt"), "rightalt" => Loc.T("Right Alt", "右 Alt", "右 Alt"),
            "leftcommand" or "leftwindows" or "leftmeta" => Loc.T("Left Win", "左 Win", "左 Win"), "rightcommand" or "rightwindows" or "rightmeta" => Loc.T("Right Win", "右 Win", "右 Win"),
            "numpadplus" => Loc.T("Numpad +", "小键盘 +", "數字鍵盤 +"), "numpadminus" => Loc.T("Numpad -", "小键盘 -", "數字鍵盤 -"),
            "numpadmultiply" => Loc.T("Numpad *", "小键盘 *", "數字鍵盤 *"), "numpaddivide" => Loc.T("Numpad /", "小键盘 /", "數字鍵盤 /"),
            "numpadenter" => Loc.T("Numpad Enter", "小键盘回车", "數字鍵盤回車"), "numpadperiod" => Loc.T("Numpad .", "小键盘 .", "數字鍵盤 ."),
            "mouse0" or "leftbutton" => Loc.T("Left mouse button", "鼠标左键", "滑鼠左鍵"), "mouse1" or "rightbutton" => Loc.T("Right mouse button", "鼠标右键", "滑鼠右鍵"),
            "mouse2" or "middlebutton" => Loc.T("Middle mouse button", "鼠标中键", "滑鼠中鍵"),
            "mouse3" or "backbutton" => Loc.T("Mouse button 4", "鼠标第 4 键", "滑鼠第 4 鍵"), "mouse4" or "forwardbutton" => Loc.T("Mouse button 5", "鼠标第 5 键", "滑鼠第 5 鍵"),
            "none" => Loc.T("None", "无", "無"),
            _ => k.StartsWith("Alpha", StringComparison.Ordinal) && k.Length == 6 ? k.Substring(5) : k
        };
    }
    /// <summary>"LeftControl + Period" → "左 Ctrl + 句号". Display only (signatures are computed from the raw value).</summary>
    internal static string LocalizeKeys(string text) => string.Join(" + ", text.Split('+').Select(part => part.Trim()).Where(part => part.Length > 0).Select(LocalName));

    /// <summary>Builds one or more chips into <paramref name="host"/>, right-aligned (or centred), each sized to its text.
    /// Returns the total width used. <paramref name="dimFirst"/> dims all but the last chip (modifier + key).</summary>
    internal static float Build(RectTransform host, IReadOnlyList<(string Label, bool Gamepad)> keys, TMP_FontAsset? font, float height,
        bool dimFirst = false, bool centre = false, float fontSize = 14f, bool muted = false, bool officialIcons = true)
    {
        for (int i = host.childCount - 1; i >= 0; i--) { var child = host.GetChild(i); if (child.name == "KeyChip") { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); } }
        const float gap = 3f;
        var widths = new List<float>();
        var chips = new List<(RectTransform Rect, float Width)>();
        foreach (var (label, gamepad) in keys)
        {
            var root = new GameObject("KeyChip", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(host, false);
            bool dim = !muted && dimFirst && chips.Count < keys.Count - 1;
            if (!muted && officialIcons && OfficialIcon(label, gamepad, out string iconTag, out var sheet))
            {
                var icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                icon.transform.SetParent(root, false); if (TMP_Settings.defaultFontAsset != null) icon.font = TMP_Settings.defaultFontAsset;
                icon.spriteAsset = sheet; icon.richText = true; icon.parseCtrlCharacters = false;
                // 1.1.4 (user): the official F-key icons draw "F3" much smaller than a letter key; show them a bit larger.
                bool fKey = !gamepad && label.Length >= 2 && label.Length <= 3 && label[0] == 'F' && char.IsDigit(label[1]);
                icon.text = iconTag; icon.fontSize = fKey ? height * 1.25f : height; icon.enableAutoSizing = false; icon.alignment = TextAlignmentOptions.Midline;
                icon.textWrappingMode = TextWrappingModes.NoWrap; icon.raycastTarget = false; icon.color = dim ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                var iconRect = icon.rectTransform; iconRect.anchorMin = Vector2.zero; iconRect.anchorMax = Vector2.one; iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
                float iconWidth = Mathf.Max(height * .8f, Mathf.Ceil(icon.GetPreferredValues(iconTag, Mathf.Infinity, icon.fontSize).x) + 2f);
                chips.Add((root, iconWidth));
                continue;
            }
            // 1.1.4 (user: "don't grey out Not set, too ugly"): an empty slot = a thin dashed cream outline with cream text,
            // no fill, the same size as a key chip.
            if (muted)
            {
                var slot = root.gameObject.AddComponent<DashedOutline>(); slot.raycastTarget = false; slot.color = new Color(0.93f, 0.89f, 0.78f, 0.55f);
                var empty = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                empty.transform.SetParent(root, false);
                SetRegular(empty, font, label);
                empty.text = label; empty.fontSize = fontSize; empty.fontStyle = FontStyles.Normal; empty.richText = false; empty.parseCtrlCharacters = false;
                empty.alignment = TextAlignmentOptions.Midline; empty.raycastTarget = false; empty.textWrappingMode = TextWrappingModes.NoWrap;
                empty.color = new Color(0.93f, 0.89f, 0.78f, 0.75f);
                var emptyRect = empty.rectTransform; emptyRect.anchorMin = Vector2.zero; emptyRect.anchorMax = Vector2.one; emptyRect.offsetMin = emptyRect.offsetMax = Vector2.zero;
                chips.Add((root, Mathf.Max(height, Mathf.Ceil(empty.GetPreferredValues(label, Mathf.Infinity, height).x) + 16f)));
                continue;
            }
            var shape = root.gameObject.AddComponent<Overview.RoundedChip>(); shape.raycastTarget = false;
            // 1.1.4 (user: "look how clear the official glyphs are"): the game's key glyph look: a cream rounded square
            // with dark, fairly heavy letters, a little larger than before.
            Color face = new Color(0.93f, 0.89f, 0.78f, 1f);
            shape.color = dim ? new Color(face.r, face.g, face.b, 0.38f) : face;
            var text = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(root, false);
            // Fallback chip (no official icon): the game UI font without outline; key names through the manager dictionary.
            string shown = gamepad ? label : LocalName(label);
            SetRegular(text, font, shown);
            text.text = shown; text.fontSize = fontSize; text.fontStyle = FontStyles.Normal; text.richText = false; text.parseCtrlCharacters = false;
            text.alignment = TextAlignmentOptions.Midline; text.raycastTarget = false; text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = gamepad ? FaceInk(shown) : Ink;
            if (dim) text.color = new Color(text.color.r, text.color.g, text.color.b, 0.55f);
            var textRect = text.rectTransform; textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            float width = Mathf.Max(height, Mathf.Ceil(text.GetPreferredValues(shown, Mathf.Infinity, height).x) + 10f);
            chips.Add((root, width));
        }
        // 1.1.4 (user): a "+" between keys of a combo.
        var plus = new List<RectTransform>(); float plusWidth = 0;
        for (int i = 1; i < chips.Count; i++)
        {
            var sign = new GameObject("KeyChip", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            sign.transform.SetParent(host, false);
            SetRegular(sign, font, "+");
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
        return total;
    }

    /// <summary>1.1.4: an empty key slot, a thin dashed rounded-corner outline (no fill).</summary>
    private sealed class DashedOutline : MaskableGraphic
    {
        public override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); Rect r = rectTransform.rect; const float thick = 1.5f, dash = 4f, gap = 3f, inset = 1f;
            float x0 = r.xMin + inset, x1 = r.xMax - inset, y0 = r.yMin + inset, y1 = r.yMax - inset, radius = Mathf.Min(4f, (y1 - y0) * .5f);
            void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d) { int i = mesh.currentVertCount; mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero); mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero); mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3); }
            void Horizontal(float y, float from, float to) { for (float x = from; x < to; x += dash + gap) { float e = Mathf.Min(to, x + dash); Quad(new(x, y), new(e, y), new(e, y + thick), new(x, y + thick)); } }
            void Vertical(float x, float from, float to) { for (float y = from; y < to; y += dash + gap) { float e = Mathf.Min(to, y + dash); Quad(new(x, y), new(x + thick, y), new(x + thick, e), new(x, e)); } }
            Horizontal(y0, x0 + radius, x1 - radius); Horizontal(y1 - thick, x0 + radius, x1 - radius);
            Vertical(x0, y0 + radius, y1 - radius); Vertical(x1 - thick, y0 + radius, y1 - radius);
            // rounded corners: short solid arcs
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 c = new(corner == 0 || corner == 3 ? x1 - radius : x0 + radius, corner < 2 ? y1 - radius : y0 + radius);
                for (int step = 0; step < 4; step++)
                {
                    float a0 = (corner * 90 + step * 22.5f) * Mathf.Deg2Rad, a1 = (corner * 90 + (step + 1) * 22.5f) * Mathf.Deg2Rad;
                    Vector2 o0 = new(Mathf.Cos(a0), Mathf.Sin(a0)), o1 = new(Mathf.Cos(a1), Mathf.Sin(a1));
                    Quad(c + o0 * radius, c + o1 * radius, c + o1 * (radius - thick), c + o0 * (radius - thick));
                }
            }
        }
    }

    /// <summary>Xbox face-button colours on the text, so A/B/X/Y read at a glance.</summary>
    private static Color FaceInk(string label) => label switch
    {
        "A" => new Color(0.22f, 0.50f, 0.18f), "B" => new Color(0.66f, 0.18f, 0.16f), "X" => new Color(0.16f, 0.36f, 0.66f), "Y" => new Color(0.62f, 0.48f, 0.06f),
        _ => Ink
    };
}
