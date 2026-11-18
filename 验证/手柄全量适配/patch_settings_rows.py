# -*- coding: utf-8 -*-
"""1.1.3 settings rows: fixed button columns (Default | Save | Clear), default value chip right-aligned against the
control, conflict as a corner badge on the key box, taller controls, string rows (manager profile dir) fixed."""
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

# --- column layout constants live in SettingCellExtras; tree patch defers to it ---
edit('ModConfigTreeLayoutPatch.cs', [
('''    internal const float CompactControlHeight = 30f;''', '''    internal const float CompactControlHeight = 36f; // 1.1.3 (user): white boxes a little taller'''),
('''            if (stackedActions)
            {
                // Save sits left of the side-by-side Default/Clear pair.
                foreach (Transform save in controls.GetComponentsInChildren<Transform>(true))
                    if (save.name == "MCE_SaveSetting" && save is RectTransform saveRect) saveRect.anchoredPosition = new Vector2(186f - 96f - 45f - 6f - 35f, 0f);
            }
''', ''),
('''                // 2.3.11: a lone Default button sits on the row's centre line, next to MCE's Save button.
                // 1.1.1 (user): keybind cells no longer stack Default over Clear; they sit side by side on the centre
                // line like every other cell (Clear rightmost, Default left of it, Save left of Default).
                if (!stackedActions) button.RectTransform.anchoredPosition = new Vector2(button.RectTransform.anchoredPosition.x, 0f);
                else button.RectTransform.anchoredPosition = new Vector2(button.name == "UI_MainMenuButton_ClearButton" ? 186f : 186f - 96f, 0f);''',
'''                // 1.1.3 (user): every row has the same three button columns, reserved even when empty:
                // Default | Save | Clear (rightmost). SettingCellExtras.Columns places them.
                SettingCellExtras.PlaceButton(button.RectTransform, button.name == "UI_MainMenuButton_ClearButton" ? SettingCellExtras.Column.Clear : SettingCellExtras.Column.Default);'''),
('''                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, control is TMP_InputField ? CompactControlHeight : Mathf.Clamp(rect.rect.height * .85f, 28f, 36f));''',
 '''                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, control is TMP_InputField ? CompactControlHeight : Mathf.Clamp(rect.rect.height * .85f, 28f, 36f));
                // Stretched controls (text box, slider row, dropdown) end at the left of the reserved button columns.
                if (control is TMP_InputField or Slider && rect.anchorMax.x - rect.anchorMin.x >= .5f) SettingCellExtras.FitControl(rect);'''),
('''                    dropdownRect.anchoredPosition = new Vector2(dropdownRect.anchoredPosition.x, 0f);''',
 '''                    dropdownRect.anchoredPosition = new Vector2(dropdownRect.anchoredPosition.x, 0f);
                    SettingCellExtras.FitControl(dropdownRect);'''),
('''            float minimum = 44f; // 1.1.1: keybind cells are one line now too''', '''            float minimum = 50f; // 1.1.1: keybind cells are one line now too; 1.1.3: taller controls'''),
])

# Save button goes to the middle column; no more SaveSlotMarker shifting
edit('Overview/NativeConfigurationDraft.cs', [
('''        rect.anchorMin = rect.anchorMax = rect.pivot = new UnityEngine.Vector2(.5f, .5f);
        rect.anchoredPosition = new UnityEngine.Vector2(186f - 45f - 6f - 35f, 0f);
        SaveSlotMarker.Mark(rect);''',
'''        // 1.1.3 (user): fixed middle column (Default | Save | Clear), the same on every row.
        SettingCellExtras.PlaceButton(rect, SettingCellExtras.Column.Save);'''),
])

# --- SettingCellExtras: columns, default chip, corner badge ---
p = os.path.join(SRC, 'SettingCellExtras.cs')
s = io.open(p, encoding='utf-8').read()
def rep(a, b):
    global s
    assert s.count(a) == 1, (a[:80], s.count(a)); s = s.replace(a, b)

rep('''    private const float LabelGap = 8f;''', '''    private const float LabelGap = 8f;
    /// <summary>The three button columns, measured from the control host's centre (ModConfig puts its Default button at
    /// x = +186 from the centre). Right to left: Clear, Save, Default. Each 84 wide, 6 apart.</summary>
    internal enum Column { Clear, Save, Default }
    private const float ButtonWidth = 84f, ButtonGap = 6f, RightEdge = 186f + 45f;
    internal static float ColumnCentre(Column column) => RightEdge - ButtonWidth / 2f - (int)column * (ButtonWidth + ButtonGap);
    /// <summary>Left edge of the reserved button area; stretched controls end a little before it.</summary>
    internal static float ButtonsLeft => RightEdge - 3 * ButtonWidth - 2 * ButtonGap;
    internal static void PlaceButton(RectTransform button, Column column)
    {
        button.anchorMin = button.anchorMax = button.pivot = new Vector2(.5f, .5f);
        button.sizeDelta = new Vector2(ButtonWidth, 28f);
        button.anchoredPosition = new Vector2(ColumnCentre(column), 0f);
    }
    /// <summary>Make a stretched control end before the button columns (works for any anchoring).</summary>
    internal static void FitControl(RectTransform control)
    {
        if (control.parent is not RectTransform host || host.rect.width < 1) return;
        float hostRight = host.rect.width / 2f; // host-centre space
        float wanted = ButtonsLeft - 8f;
        float currentRight = hostRight * (control.anchorMax.x * 2f - 1f) + control.offsetMax.x;
        control.offsetMax = new Vector2(control.offsetMax.x + (wanted - currentRight), control.offsetMax.y);
    }''')

rep('''                if (_default == null)
                {
                    _default = UnityEngine.Object.Instantiate(cell.m_text, cell.m_text.transform.parent);
                    _default.name = "MCE_DefaultValue";
                    foreach (var extra in _default.GetComponents<Component>())
                        if (extra is not TextMeshProUGUI && extra is not RectTransform && extra is not CanvasRenderer) Destroy(extra);
                    _default.raycastTarget = false; _default.richText = false; _default.parseCtrlCharacters = false;
                    _default.color = DefaultColor; _default.fontStyle = FontStyles.Normal;
                    _default.alignment = TextAlignmentOptions.MidlineRight;
                    _default.textWrappingMode = TextWrappingModes.NoWrap; _default.overflowMode = TextOverflowModes.Ellipsis;
                    _default.enableAutoSizing = false;
                }
                _default.text = Loc.Get("META_DEFAULT") + "：" + value;''',
'''                if (_default == null)
                {
                    _default = UnityEngine.Object.Instantiate(cell.m_text, cell.m_text.transform.parent);
                    _default.name = "MCE_DefaultValue";
                    foreach (var extra in _default.GetComponents<Component>())
                        if (extra is not TextMeshProUGUI && extra is not RectTransform && extra is not CanvasRenderer) Destroy(extra);
                    _default.raycastTarget = false; _default.richText = false; _default.parseCtrlCharacters = false;
                    _default.color = DefaultColor; _default.fontStyle = FontStyles.Normal;
                    _default.alignment = TextAlignmentOptions.MidlineRight;
                    _default.textWrappingMode = TextWrappingModes.NoWrap; _default.overflowMode = TextOverflowModes.Ellipsis;
                    _default.enableAutoSizing = false;
                }
                // 1.1.3 (user): "默认：" stays plain text; the value itself sits in a rounded chip (keys as key chips),
                // the whole group right-aligned against the left edge of the white box.
                _default.text = Loc.Get("META_DEFAULT") + "：";
                _defaultValue = value;
                _defaultKeys = KeyValues(property.ConfigBase);''')
rep('''        private string _conflicts = "";''', '''        private string _conflicts = "";
        private string _defaultValue = "";
        private List<(string, bool)>? _defaultKeys;
        private RectTransform? _defaultChips;''')
rep('''        /// <summary>The default label sits at the right end of the title area, before the control. The title wins:
        /// when both do not fit, the label shrinks (down to 11) and then hides.</summary>
        private void LayoutDefault(TextMeshProUGUI title, float width)
        {
            if (_default == null) return;
            var rect = (RectTransform)_default.transform; var titleRect = (RectTransform)title.transform;
            rect.anchorMin = titleRect.anchorMin; rect.anchorMax = titleRect.anchorMax; rect.pivot = titleRect.pivot;
            rect.offsetMin = titleRect.offsetMin; rect.offsetMax = titleRect.offsetMax;
            float chip = _chip != null && _chip.gameObject.activeSelf ? 24f : 0f;
            float titleWidth = title.GetPreferredValues(title.text, Mathf.Infinity, 0).x + title.margin.x;
            float free = width - titleWidth - LabelGap - chip;
            float size = Mathf.Min(title.fontSize, 15f);
            _default.fontSize = size;
            float need = _default.GetPreferredValues(_default.text, Mathf.Infinity, 0).x;
            while (need > free && size > 11f) { size -= 1f; _default.fontSize = size; need = _default.GetPreferredValues(_default.text, Mathf.Infinity, 0).x; }
            bool fits = need <= free && free > 30f;
            _default.gameObject.SetActive(fits);
            if (fits) _default.margin = new Vector4(0, 0, chip, 0);
        }''',
'''        /// <summary>1.1.3: "默认：[value]" is right-aligned against the left edge of the white box (same x on every row).
        /// The value sits in a rounded chip; long values are cut with "…"; the option name keeps its room.</summary>
        private void LayoutDefault(TextMeshProUGUI title, float width)
        {
            if (_default == null || _cell?.m_settingsContentParent == null) return;
            var host = (RectTransform)title.transform.parent;
            // Right edge = the leftmost visible control's left edge, in the title's parent space.
            float right = ControlsLeft(host) - 6f;
            float titleLeft = ((RectTransform)title.transform).offsetMin.x + host.rect.xMin; // approx
            float titleWidth = title.GetPreferredValues(title.text, Mathf.Infinity, 0).x + 12f;
            float titleRight = TitleLeft(title, host) + titleWidth;
            float room = right - titleRight - LabelGap;
            float size = Mathf.Min(title.fontSize, 14f);
            _default.fontSize = size;
            float labelWidth = _default.GetPreferredValues(_default.text, Mathf.Infinity, 0).x + 2f;
            if (_defaultChips == null) { _defaultChips = new GameObject("MCE_DefaultChips", typeof(RectTransform)).GetComponent<RectTransform>(); _defaultChips.SetParent(host, false); }
            // Value chips: keys as key chips, anything else as one rounded text chip, cut to fit.
            var keys = _defaultKeys ?? new List<(string, bool)> { (_defaultValue, false) };
            float chipRoom = Mathf.Max(0, room - labelWidth);
            var shown = FitValue(keys, chipRoom, size);
            bool visible = shown != null && room > labelWidth + 24f;
            _default.gameObject.SetActive(visible); _defaultChips.gameObject.SetActive(visible);
            if (!visible) return;
            _defaultChips.anchorMin = _defaultChips.anchorMax = new Vector2(0, .5f); _defaultChips.pivot = new Vector2(1, .5f);
            _defaultChips.sizeDelta = new Vector2(0, 22f);
            _defaultChips.anchoredPosition = new Vector2(right - host.rect.xMin, 0f);
            float used = KeyChips.Build(_defaultChips, shown!, title.font, 22f, fontSize: size - 1f);
            var rect = (RectTransform)_default.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, .5f); rect.pivot = new Vector2(1, .5f);
            rect.sizeDelta = new Vector2(labelWidth, 28f);
            rect.anchoredPosition = new Vector2(right - host.rect.xMin - used - 4f, 0f);
            _default.margin = Vector4.zero; _default.alignment = TextAlignmentOptions.MidlineRight;
        }
        private static float TitleLeft(TextMeshProUGUI title, RectTransform host)
        {
            var corners = new Vector3[4]; title.rectTransform.GetWorldCorners(corners);
            return host.InverseTransformPoint(corners[0]).x + title.margin.x;
        }
        /// <summary>Left edge (host space) of the leftmost visible control in the cell (white box, dropdown, slider, key box).</summary>
        private float ControlsLeft(RectTransform host)
        {
            float left = float.MaxValue; var corners = new Vector3[4];
            foreach (var control in _cell!.m_settingsContentParent.GetComponentsInChildren<Selectable>(false))
            {
                if (control is Scrollbar || control.GetComponent<PeakMenuButton>() != null || control.name == "MCE_SaveSetting" || control.name == "MCE_KeyConflict") continue;
                if (control.GetComponentInParent<TMP_Dropdown>() is TMP_Dropdown parent && parent != control) continue;
                var target = control is InputBindingSettingUI binding ? (binding.transform.Find("Key Button Input") as RectTransform ?? (RectTransform)control.transform) : (RectTransform)control.transform;
                target.GetWorldCorners(corners);
                left = Mathf.Min(left, host.InverseTransformPoint(corners[0]).x);
            }
            return left == float.MaxValue ? host.rect.xMax - 300f : left;
        }
        /// <summary>The value chips cut to <paramref name="room"/> (text values get "…"); null when nothing fits.</summary>
        private static List<(string, bool)>? FitValue(List<(string Label, bool Gamepad)> keys, float room, float size)
        {
            if (room < 24f) return null;
            float Estimate(string text) => text.Length * size * .62f + 10f;
            float total = keys.Sum(k => Estimate(k.Label)) + 3f * (keys.Count - 1);
            if (total <= room) return keys.Select(k => (k.Label, k.Gamepad)).ToList();
            if (keys.Count > 1) return null;
            string text = keys[0].Label;
            int chars = Mathf.FloorToInt((room - 10f) / (size * .62f)) - 1;
            if (chars < 2) return null;
            return new List<(string, bool)> { (text.Substring(0, Math.Min(text.Length, chars)) + "…", keys[0].Gamepad) };
        }''')
rep('''    /// <summary>The value as the settings page shows it ("On", "0.5", "F3"); null when there is none worth showing.</summary>''',
'''    /// <summary>For key settings, the default as key chips (modifiers + key, gamepad flagged); null for other settings.</summary>
    internal static List<(string, bool)>? KeyValues(ConfigEntryBase entry)
    {
        if (Overview.OverviewHotkeyAdapter.Create(entry) == null || entry.DefaultValue == null) return null;
        string raw = entry.DefaultValue is KeyboardShortcut shortcut ? shortcut.ToString() : Convert.ToString(entry.DefaultValue) ?? "";
        if (raw.Length == 0 || raw.Equals("None", StringComparison.OrdinalIgnoreCase)) return null;
        return raw.Split('+').Select(part => part.Trim()).Where(part => part.Length > 0).Select(KeyChips.Describe).ToList();
    }
    /// <summary>The value as the settings page shows it ("On", "0.5", "F3"); null when there is none worth showing.</summary>''')

# conflict: corner badge on the key box
rep('''            var host = (RectTransform)_cell.m_text.transform.parent;
            var chip = new GameObject("MCE_KeyConflict", typeof(RectTransform), typeof(Image), typeof(Button));
            _chip = (RectTransform)chip.transform; _chip.SetParent(host, false);
            var image = chip.GetComponent<Image>(); image.color = ConflictColor;''',
'''            // 1.1.3 (user): a small corner badge on the key box (like the overview's status chips), not a "!" block.
            var host = (RectTransform)(binding.transform.Find("Key Button Input") ?? binding.transform);
            var chip = new GameObject("MCE_KeyConflict", typeof(RectTransform), typeof(Image), typeof(Button));
            _chip = (RectTransform)chip.transform; _chip.SetParent(host, false);
            var image = chip.GetComponent<Image>(); image.color = Color.clear; // the 18x18 box is the hit area
            var dot = new GameObject("Badge", typeof(RectTransform)).AddComponent<Overview.RoundedChip>();
            dot.transform.SetParent(_chip, false); dot.color = ConflictColor; dot.raycastTarget = false;
            var dotRect = dot.rectTransform; dotRect.anchorMin = dotRect.anchorMax = dotRect.pivot = new Vector2(.5f, .5f); dotRect.sizeDelta = new Vector2(9, 9);''')
rep('''            var mark = new GameObject("Mark", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            mark.transform.SetParent(_chip, false); mark.text = "!"; mark.fontSize = 15; mark.fontStyle = FontStyles.Bold;
            mark.color = new Color(.18f, .12f, .05f); mark.alignment = TextAlignmentOptions.Center; mark.raycastTarget = false;
            if (_cell.m_text.font != null) mark.font = _cell.m_text.font;
            var markRect = mark.rectTransform; markRect.anchorMin = Vector2.zero; markRect.anchorMax = Vector2.one; markRect.offsetMin = markRect.offsetMax = Vector2.zero;
''', '')
rep('''            var title = (RectTransform)_cell.m_text.transform;
            _chip.anchorMin = _chip.anchorMax = new Vector2(title.anchorMax.x, .5f); _chip.pivot = new Vector2(1, .5f);
            _chip.sizeDelta = new Vector2(18, 18);
            _chip.anchoredPosition = new Vector2(title.offsetMax.x - 2f, 0);''',
'''            _chip.anchorMin = _chip.anchorMax = _chip.pivot = new Vector2(1, 1);
            _chip.sizeDelta = new Vector2(18, 18);
            _chip.anchoredPosition = new Vector2(-1f, -1f);'''),
rep('''            float chip = _chip != null && _chip.gameObject.activeSelf ? 24f : 0f;
''', '') if '            float chip = _chip != null && _chip.gameObject.activeSelf ? 24f : 0f;\n' in s else None
io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('ok')
