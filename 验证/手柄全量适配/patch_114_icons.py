# -*- coding: utf-8 -*-
"""1.1.4 (user decisions 2026-11-13): key chips use the game's own key icons (InputSpriteData sprite sheets: keyboard /
Xbox / PS), falling back to a rounded chip in the game UI font without outline only when the sheet has no icon (and for
"Not set"); key cells lose every trace of the ribbon; key names (Period, Comma ...) are translated in the manager's own
dictionary, pure Chinese names."""
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

edit('GamepadIntent.cs', [
('''    private static TMP_SpriteAsset? SpriteSet(InputSpriteData data)''', '''    internal static TMP_SpriteAsset? SpriteSet(InputSpriteData data)'''),
])

edit('KeyChips.cs', [
# fallback font: game UI font, no outline
('''    private static void SetRegular(TextMeshProUGUI text, TMP_FontAsset? fallback, string label)
    {
        var sans = TMP_Settings.defaultFontAsset;
        if (sans != null && sans.HasCharacters(label, out _, false, false)) { text.font = sans; text.fontSharedMaterial = sans.material; return; }
        if (fallback != null) text.font = fallback;
        Overview.MceTextWeight.Apply(text);
    }''', '''    private static void SetRegular(TextMeshProUGUI text, TMP_FontAsset? fallback, string label)
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
    internal static string LocalizeKeys(string text) => string.Join(" + ", text.Split('+').Select(part => part.Trim()).Where(part => part.Length > 0).Select(LocalName));'''),
# per-chip: official icon when there is one
('''        foreach (var (label, gamepad) in keys)
        {
            var root = new GameObject("KeyChip", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(host, false);
            var shape = root.gameObject.AddComponent<Overview.RoundedChip>(); shape.raycastTarget = false;
            bool dim = muted || (dimFirst && chips.Count < keys.Count - 1);''', '''        foreach (var (label, gamepad) in keys)
        {
            var root = new GameObject("KeyChip", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(host, false);
            bool dim = muted || (dimFirst && chips.Count < keys.Count - 1);
            if (!muted && OfficialIcon(label, gamepad, out string iconTag, out var sheet))
            {
                var icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                icon.transform.SetParent(root, false); icon.spriteAsset = sheet; icon.richText = true; icon.parseCtrlCharacters = false;
                icon.text = iconTag; icon.fontSize = height; icon.enableAutoSizing = false; icon.alignment = TextAlignmentOptions.Midline;
                icon.textWrappingMode = TextWrappingModes.NoWrap; icon.raycastTarget = false; icon.color = dim ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                var iconRect = icon.rectTransform; iconRect.anchorMin = Vector2.zero; iconRect.anchorMax = Vector2.one; iconRect.offsetMin = iconRect.offsetMax = Vector2.zero;
                float iconWidth = Mathf.Max(height * .8f, Mathf.Ceil(icon.GetPreferredValues(iconTag, Mathf.Infinity, height).x) + 2f);
                chips.Add((root, iconWidth));
                continue;
            }
            var shape = root.gameObject.AddComponent<Overview.RoundedChip>(); shape.raycastTarget = false;'''),
('''            text.text = label; text.fontSize = fontSize;''', '''            label = gamepad ? label : LocalName(label);
            text.text = label; text.fontSize = fontSize;'''),
])
# KeyChips needs Linq for LocalizeKeys
p = os.path.join(SRC, 'KeyChips.cs'); s = io.open(p, encoding='utf-8').read()
if 'using System.Linq;' not in s:
    s = s.replace('using System.Collections.Generic;', 'using System.Collections.Generic;\nusing System.Linq;', 1); io.open(p, 'w', encoding='utf-8', newline='').write(s)

# display labels everywhere (hover, menus, conflict notes, profile preset text)
edit('Overview/OverviewHotkeyAdapter.cs', [
('''        return IsPath && TryPath(Convert.ToString(value) ?? "", out string device, out string control) ? device + " / " + control : Convert.ToString(value) ?? "";''',
 '''        // 1.1.4 (user): key names through the manager dictionary ("Period" → "句号").
        return IsPath && TryPath(Convert.ToString(value) ?? "", out string device, out string control) ? device + " / " + KeyChips.LocalName(control) : KeyChips.LocalizeKeys(Convert.ToString(value) ?? "");'''),
])
edit('SettingCellExtras.cs', [
('''        if (value is KeyboardShortcut shortcut) return shortcut.MainKey == KeyCode.None ? Loc.T("None", "无", "無") : shortcut.ToString();''',
 '''        if (value is KeyboardShortcut shortcut) return shortcut.MainKey == KeyCode.None ? Loc.T("None", "无", "無") : KeyChips.LocalizeKeys(shortcut.ToString());
        if (value is KeyCode code) return KeyChips.LocalName(code.ToString());'''),
])

# key cells: no ribbon at all, a clear hit area keeps clicks and the focus ring on the cell rect
edit('Overview/OverviewHotkeyView.cs', [
('''        var peak=button.GetComponent<PEAKLib.UI.Elements.PeakMenuButton>();if(peak==null)return;
        foreach(var image in new[]{peak.Panel,peak.BorderTop,peak.BorderBottom})if(image!=null){var c=image.color;c.a=0;image.color=c;}''',
 '''        // 1.1.4 (user: "still a faint ribbon - just delete it"): every image of the ribbon (shadow, panel, borders, glow) is
        // switched off; a clear image on the cell rect keeps the click target and the focus ring the same size as before.
        foreach(var image in button.GetComponentsInChildren<Image>(true))if(image.gameObject.name!="MCE_KeyHit")image.enabled=false;
        if(button.transform.Find("MCE_KeyHit")==null){var hit=OverviewUi.Rect("MCE_KeyHit",button.transform);OverviewUi.Fill(hit);hit.SetAsFirstSibling();var img=hit.gameObject.AddComponent<Image>();img.color=Color.clear;img.raycastTarget=true;button.targetGraphic=img;}
        button.transition=Selectable.Transition.None;'''),
])
print('ok')
