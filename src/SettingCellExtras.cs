#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using PEAKLib.ModConfig;
using PEAKLib.ModConfig.Components;
using PEAKLib.ModConfig.SettingOptions.SettingUI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zorro.Settings;

namespace ModConfigEnhance;

/// <summary>
/// 1.1.1 模组设置页单元格（用户 2026-11-12 截图反馈）。
///   · 默认值：配置名后面、控件左边用暗色小字显示「默认：x」；配置名太长时默认值让位（先缩字号，再整段隐藏），
///     不和配置名重叠。
///   · 快捷键重复：原生那行显眼的橙红「此键已绑定给：…」改为一个小「!」标记，悬停 / 手柄选中时在说明框里列出
///     和谁重复，点击或 A 跳到对方那一格。
///   · 统一：快捷键格（原生为 50 高的白框、默认 / 清空上下叠放）改成与其他格相同的 30 高控件、
///     默认 / 清空并排；保存钮由 <see cref="Overview.NativeConfigurationDraft"/> 统一加在每种格上。
/// </summary>
internal static class SettingCellExtras
{
    private const float LabelGap = 8f, DefaultGap = 10f;
    /// <summary>The three button columns, measured from the control host's centre (ModConfig puts its Default button at
    /// x = +186 from the centre). Right to left: Clear, Save, Default. Each 84 wide, 6 apart.</summary>
    /// <summary>1.1.4 (user, old MCE screenshot): one button column, Default on top and Clear under it when the setting
    /// can be cleared; a row with only Default centres it. No Save button (edits take effect at once).</summary>
    internal enum Column { Default, Clear }
    private const float ButtonWidth = 76f, ButtonHeight = 18f, RightEdge = 186f + 45f;
    internal static float ColumnCentre(Column column) => RightEdge - ButtonWidth / 2f;
    /// <summary>Left edge of the reserved button area; stretched controls end a little before it.</summary>
    internal static float ButtonsLeft => RightEdge - ButtonWidth;
    internal static void PlaceButton(RectTransform button, Column column, bool stacked = false)
    {
        button.anchorMin = button.anchorMax = button.pivot = new Vector2(.5f, .5f);
        button.sizeDelta = new Vector2(ButtonWidth, stacked ? ButtonHeight : 24f);
        float y = !stacked ? 0f : column == Column.Default ? ButtonHeight / 2f + 1f : -ButtonHeight / 2f - 1f;
        button.anchoredPosition = new Vector2(ColumnCentre(column), y);
    }
    /// <summary>Make a stretched control end before the button columns (works for any anchoring).</summary>
    internal static void FitControl(RectTransform control)
    {
        if (control.parent is not RectTransform host || host.rect.width < 1) return;
        float hostRight = host.rect.width / 2f; // host-centre space
        float wanted = ButtonsLeft - 8f;
        float currentRight = hostRight * (control.anchorMax.x * 2f - 1f) + control.offsetMax.x;
        control.offsetMax = new Vector2(control.offsetMax.x + (wanted - currentRight), control.offsetMax.y);
    }
    private static readonly Color DefaultColor = new(0.72f, 0.72f, 0.70f, 0.78f);
    private static readonly Color ConflictColor = new(0.95f, 0.62f, 0.30f, 1f);

    internal static void Apply(SettingsUICell cell)
    {
        try
        {
            if (cell == null || cell.m_settingsContentParent == null) return;
            IBepInExProperty? property = Property(cell);
            if (property == null) return;
            var extras = cell.GetComponent<CellExtras>() ?? cell.gameObject.AddComponent<CellExtras>();
            extras.Bind(cell, property);
        }
        catch (Exception error) { WarnOnce("[设置格] 默认值/冲突标记挂载失败：" + error.Message); }
    }

    internal static IBepInExProperty? Property(SettingsUICell cell)
    {
        try
        {
            var input = cell.m_settingsContentParent != null ? cell.m_settingsContentParent.GetComponentInChildren<SettingInputUICell>(true) : null;
            return input != null ? input._listeningSetting as IBepInExProperty : null;
        }
        catch { return null; }
    }

    /// <summary>For key settings, the default as key chips (modifiers + key, gamepad flagged); null for other settings.</summary>
    internal static List<(string, bool)>? KeyValues(ConfigEntryBase entry)
    {
        if (Overview.OverviewHotkeyAdapter.Create(entry) == null || entry.DefaultValue == null) return null;
        string raw = entry.DefaultValue is KeyboardShortcut shortcut ? shortcut.ToString() : Convert.ToString(entry.DefaultValue) ?? "";
        if (raw.Length == 0 || raw.Equals("None", StringComparison.OrdinalIgnoreCase)) return null;
        return raw.Split('+').Select(part => part.Trim()).Where(part => part.Length > 0).Select(KeyChips.Describe).ToList();
    }
    /// <summary>The value as the settings page shows it ("On", "0.5", "F3"); null when there is none worth showing.</summary>
    internal static string? DefaultText(ConfigEntryBase entry)
    {
        object? value = entry.DefaultValue;
        if (value == null) return null;
        if (value is bool flag) return flag ? Loc.Get("META_ON") : Loc.Get("META_OFF");
        if (value is float f) return f.ToString("0.###");
        if (value is double d) return d.ToString("0.###");
        if (value is KeyboardShortcut shortcut) return shortcut.MainKey == KeyCode.None ? Loc.T("None", "无", "無") : KeyChips.LocalizeKeys(shortcut.ToString());
        if (value is KeyCode code) return KeyChips.LocalName(code.ToString());
        string text = Convert.ToString(value) ?? "";
        if (text.Length == 0) return Loc.T("(empty)", "（空）", "（空）");
        return text.Length > 24 ? text.Substring(0, 23) + "…" : text;
    }

    private static bool _warned;
    internal static void WarnOnce(string message) { if (_warned) return; _warned = true; Plugin.Log.LogWarning(message); }

    /// <summary>Per-cell helper: lays out the default label every frame the title width changes, and owns the conflict chip.</summary>
    internal sealed class CellExtras : MonoBehaviour
    {
        private SettingsUICell? _cell;
        private IBepInExProperty? _property;
        private TextMeshProUGUI? _default;
        private RectTransform? _chip;
        private TextMeshProUGUI? _nativeWarning;
        private string _conflicts = "";
        private string _defaultValue = "";
        private List<(string, bool)>? _defaultKeys;
        private RectTransform? _defaultChips;
        private float _laidOutWidth = -1;
        private string _laidOutTitle = "";

        internal void Bind(SettingsUICell cell, IBepInExProperty property)
        {
            _cell = cell; _property = property;
            string? value = DefaultText(property.ConfigBase);
            if (value != null && cell.m_text != null)
            {
                if (_default == null)
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
                _defaultKeys = KeyValues(property.ConfigBase);
            }
            if (cell.m_settingsContentParent.GetComponentInChildren<InputBindingSettingUI>(true) is InputBindingSettingUI binding)
                BindConflicts(binding);
            _laidOutWidth = -1;
        }

        private bool _keyBoxDone;
        /// <summary>Keybind cells: the white key box was 50 high and ran under the buttons; make it the same 30-high
        /// box as other cells, ending just before the Save / Default buttons.</summary>
        private void FitKeyBox()
        {
            if (_keyBoxDone || _cell?.m_settingsContentParent == null) return;
            var binding = _cell.m_settingsContentParent.GetComponentInChildren<InputBindingSettingUI>(true);
            if (binding == null) { _keyBoxDone = true; return; }
            var host = (RectTransform)binding.transform;
            var box = host.Find("Key Button Input") as RectTransform;
            if (box == null || host.rect.width < 1 || box.rect.width < 1) return;
            float leftmost = float.MaxValue;
            foreach (Transform child in host)
                if (child is RectTransform button && (child.name == "MCE_SaveSetting" || child.GetComponent<PeakMenuButton>() != null) && child.gameObject.activeSelf)
                    leftmost = Mathf.Min(leftmost, button.anchoredPosition.x - button.rect.width / 2f);
            if (leftmost == float.MaxValue) leftmost = 186f - 45f;
            var corners = new Vector3[4]; box.GetWorldCorners(corners);
            float left = host.InverseTransformPoint(corners[0]).x - host.rect.center.x;
            float right = leftmost - 8f;
            if (right - left < 60f) right = left + 60f; // still make it one line high; narrow pages accept a short box
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(.5f, .5f);
            box.sizeDelta = new Vector2(right - left, ModConfigTreeLayoutPatch.CompactControlHeight);
            box.anchoredPosition = new Vector2((left + right) / 2f, 0f);
            if (binding.KeyText != null) { binding.KeyText.fontSizeMax = 18f; binding.KeyText.fontSizeMin = 12f; }
            _keyBoxDone = true;
        }

        private void LateUpdate()
        {
            FitKeyBox();
            if (_cell == null || _cell.m_text == null) return;
            var title = _cell.m_text;
            float width = ((RectTransform)title.transform).rect.width;
            if (Mathf.Abs(width - _laidOutWidth) < .5f && title.text == _laidOutTitle) { RefreshConflict(); return; }
            _laidOutWidth = width; _laidOutTitle = title.text;
            LayoutDefault(title, width);
            RefreshConflict();
        }

        /// <summary>1.1.3: "默认：[value]" is right-aligned against the left edge of the white box (same x on every row).
        /// The value sits in a rounded chip; long values are cut with "…"; the option name keeps its room.</summary>
        private void LayoutDefault(TextMeshProUGUI title, float width)
        {
            if (_default == null || _cell?.m_settingsContentParent == null) return;
            var host = (RectTransform)title.transform.parent;
            // Right edge = the leftmost visible control's left edge, in the title's parent space.
            float right = ControlsLeft(host) - DefaultGap;
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
            _defaultChips.sizeDelta = new Vector2(0, 24f);
            _defaultChips.anchoredPosition = new Vector2(right - host.rect.xMin, 0f);
            // 1.1.4 (user: styles mixed, gaps uneven): every default value is the same cream card (no official icons here),
            // a fixed gap from the box, and "默认：" a fixed gap before the card.
            float used = KeyChips.Build(_defaultChips, shown!, title.font, 22f, fontSize: 13f, officialIcons: false);
            var rect = (RectTransform)_default.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, .5f); rect.pivot = new Vector2(1, .5f);
            rect.sizeDelta = new Vector2(labelWidth, 28f);
            rect.anchoredPosition = new Vector2(right - host.rect.xMin - used - 6f, 0f);
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
                var binding = control.GetComponent<InputBindingSettingUI>();
                var target = binding != null ? (binding.transform.Find("Key Button Input") as RectTransform ?? (RectTransform)control.transform) : (RectTransform)control.transform;
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
        }

        // ── 快捷键重复 ──────────────────────────────────────────

        private void BindConflicts(InputBindingSettingUI binding)
        {
            _nativeWarning = binding.DuplicateWarningText;
            if (_chip != null || _cell?.m_text == null) return;
            // 1.1.3 (user): a small corner badge on the key box (like the overview's status chips), not a "!" block.
            var host = (RectTransform)(binding.transform.Find("Key Button Input") ?? binding.transform);
            var chip = new GameObject("MCE_KeyConflict", typeof(RectTransform), typeof(Image), typeof(Button));
            _chip = (RectTransform)chip.transform; _chip.SetParent(host, false);
            var image = chip.GetComponent<Image>(); image.color = Color.clear; // the 18x18 box is the hit area
            var dot = new GameObject("Badge", typeof(RectTransform)).AddComponent<Overview.RoundedChip>();
            dot.transform.SetParent(_chip, false); dot.color = ConflictColor; dot.raycastTarget = false;
            var dotRect = dot.rectTransform; dotRect.anchorMin = dotRect.anchorMax = dotRect.pivot = new Vector2(.5f, .5f); dotRect.sizeDelta = new Vector2(9, 9);
            var button = chip.GetComponent<Button>(); button.targetGraphic = image;
            button.onClick.AddListener(JumpToConflict);
            chip.AddComponent<ConflictHover>().Owner = this;
            GamepadSupport.MarkSelectable(chip, null, null);
            chip.SetActive(false);
        }

        /// <summary>1.1.3 review: called from ModConfig's own duplicate recompute (GetDuplicateControls postfix), never per
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
            if (!show) return;
            _chip.anchorMin = _chip.anchorMax = _chip.pivot = new Vector2(1, 1);
            _chip.sizeDelta = new Vector2(18, 18);
            _chip.anchoredPosition = new Vector2(-1f, -1f);
        }


        internal string Explanation()
        {
            string targets = _conflicts;
            int colon = targets.IndexOf('：'); if (colon < 0) colon = targets.IndexOf(':');
            if (colon >= 0) targets = targets.Substring(colon + 1).Trim();
            return Loc.T("Same key as:", "与这些快捷键重复：", "與這些快捷鍵重複：") + "\n" + targets.Replace(", ", "\n").Replace("，", "\n")
                + "\n" + Loc.T("Click or press A to jump there.", "点击或按 A 跳到对方。", "點擊或按 A 跳到對方。");
        }

        /// <summary>Finds the other setting with the same key on this page (or switches to its mod) and selects it.</summary>
        internal void JumpToConflict()
        {
            if (_property == null) return;
            var menu = GetComponentInParent<ModSettingsMenu>(true) ?? UnityEngine.Object.FindAnyObjectByType<ModSettingsMenu>();
            if (menu == null) return;
            var target = FindConflictCell(menu);
            if (target != null) { Focus(target); return; }
            // On another mod's page: switch to that mod, then select the cell once it is built.
            var other = OtherProperty(menu);
            if (other == null || menu.ModTabController == null || menu.ModTabs == null) return;
            var row = menu.ModTabController.Tabs.Select(t => t != null ? t.GetComponent<ModdedTABSButton>() : null)
                .FirstOrDefault(b => b != null && b.category == other.GetCategory());
            if (row == null) return;
            menu.ModTabs.Select(row);
            menu.StartCoroutine(FocusLater(menu, other));
        }
        private System.Collections.IEnumerator FocusLater(ModSettingsMenu menu, IBepInExProperty other)
        {
            for (int i = 0; i < 30; i++)
            {
                yield return null;
                foreach (var cell in menu.m_spawnedCells)
                    if (cell != null && cell.gameObject.activeInHierarchy && ReferenceEquals(Property(cell), other)) { Focus(cell); yield break; }
            }
        }
        private SettingsUICell? FindConflictCell(ModSettingsMenu menu)
        {
            var other = OtherProperty(menu);
            if (other == null) return null;
            foreach (var cell in menu.m_spawnedCells)
                if (cell != null && cell.gameObject.activeInHierarchy && ReferenceEquals(Property(cell), other)) return cell;
            return null;
        }
        private IBepInExProperty? OtherProperty(ModSettingsMenu menu)
        {
            if (_property == null || menu.settings == null) return null;
            string mine = KeyText(_property);
            if (mine.Length == 0) return null;
            return menu.settings.FirstOrDefault(s => !ReferenceEquals(s, _property) && KeyText(s) == mine);
        }
        private static string KeyText(IBepInExProperty property)
        {
            try
            {
                if (property is PEAKLib.ModConfig.SettingOptions.BepInExKeyCode code) return code.Value == KeyCode.None ? "" : InputBindingDisplay.GetSpriteTagText(code.Value);
                if (property is PEAKLib.ModConfig.SettingOptions.BepInExKeyPath path) return string.IsNullOrEmpty(path.Value) ? "" : InputBindingDisplay.GetSpriteTagText(path.Value);
            }
            catch { }
            return "";
        }
        private static void Focus(SettingsUICell cell)
        {
            var binding = cell.m_settingsContentParent != null ? cell.m_settingsContentParent.GetComponentInChildren<InputBindingSettingUI>(true) : null;
            GameObject target = binding != null ? binding.gameObject : cell.gameObject;
            var scroll = cell.GetComponentInParent<ScrollRect>();
            if (scroll != null && scroll.content != null && scroll.viewport != null)
            {
                var corners = new Vector3[4]; ((RectTransform)cell.transform).GetWorldCorners(corners);
                float top = -scroll.content.InverseTransformPoint(corners[1]).y;
                float range = scroll.content.rect.height - scroll.viewport.rect.height;
                if (range > 1) scroll.verticalNormalizedPosition = Mathf.Clamp01(1f - (top - 20f) / range);
            }
            cell.GetComponentInParent<GamepadSupport.PanelFocus>()?.EnterOptions();
            EventSystem.current?.SetSelectedGameObject(target);
            ConflictFlash.Play(cell);
        }
    }

    /// <summary>Hover / gamepad focus on the "!" chip shows the conflicting keys in the description panel.</summary>
    private sealed class ConflictHover : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        internal CellExtras? Owner;
        public void OnPointerEnter(PointerEventData eventData) => Show();
        public void OnSelect(BaseEventData eventData) => Show();
        private void Show() { var panel = ModConfigDescriptionPanel.Current; if (Owner != null && panel != null) panel.ShowNotice(Owner.Explanation(), warning: true); }
    }

    /// <summary>Short highlight on the cell we jumped to.</summary>
    private sealed class ConflictFlash : MonoBehaviour
    {
        private Image? _image; private Color _original; private float _until;
        internal static void Play(SettingsUICell cell)
        {
            var image = cell.GetComponentInChildren<Image>(); if (image == null) return;
            var flash = cell.GetComponent<ConflictFlash>() ?? cell.gameObject.AddComponent<ConflictFlash>();
            if (flash._image == null) { flash._image = image; flash._original = image.color; }
            flash._until = Time.unscaledTime + .9f; flash.enabled = true;
        }
        private void Update()
        {
            if (_image == null) { enabled = false; return; }
            float left = _until - Time.unscaledTime;
            if (left <= 0) { _image.color = _original; enabled = false; return; }
            _image.color = Color.Lerp(_original, ConflictColor, Mathf.PingPong(left * 4f, 1f) * .45f);
        }
    }
}
