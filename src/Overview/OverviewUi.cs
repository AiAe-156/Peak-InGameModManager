#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>借用游戏字体、缎带按钮和纸面材质。图标是本模组自绘的矢量网格，不依赖网页或外部字体。</summary>
internal static partial class OverviewUi
{
    // 2.4.0：骨架色随主题（ModConfigLook.Surface）；Blue / Green 是内容色，两套主题都不变。
    internal static Color Ivory => ModConfigLook.Surface(new(.94f, .91f, .81f));
    internal static Color Muted => ModConfigLook.Surface(new(.70f, .70f, .64f));
    internal static readonly Color Blue = new(.40f, .72f, .82f);
    internal static readonly Color Green = new(.50f, .77f, .49f);
    internal static Color Brown => ModConfigLook.Surface(new(.16f, .13f, .10f, .96f));
    internal static Color Gold => ModConfigLook.Surface(new(.70f, .60f, .37f));
    internal static readonly string[] Palette = { "#EEE8CE", "#7CC7E6", "#69B87B", "#DDC374", "#DEA775", "#C77772", "#B091C5", "#82B5AC", "#A5BE78", "#C6A793", "#A4AEC3", "#BB91AA", "#E0B29B", "#8399B8", "#B5B2A1", "#A1C2CA" };
    internal static Transform? ModalParent;
    private static GameObject? _modal;
    private static Func<bool>? _isModalDirty;
    private static Action? _modalCloseHandler;
    private static GameObject? _confirmation;
    private static Action? _continueEditing;
    private static GameObject? _previousSelection;
    private static CanvasGroup? _suspendedModal;
    private static bool _wasModalInteractable;
    private static TextMeshProUGUI? _referenceText;
    internal static bool ManagementOpen;
    internal static bool HasDialog => _modal != null || _confirmation != null;
    internal static bool HasModal => ManagementOpen || _modal != null || _confirmation != null;
    internal static void SetModalDirtyCheck(Func<bool>? check) => _isModalDirty = check;
    /// <summary>1.1.0: the topmost open dialog (confirmation over modal), for gamepad scrolling.</summary>
    internal static GameObject? TopLayer => _confirmation != null ? _confirmation : _modal;
    /// <summary>Put the focus on a text field. With a mouse it starts typing at once (as before); on a gamepad it is only
    /// selected, and A starts typing (or opens the Steam keyboard), matching the settings page.</summary>
    internal static void FocusInput(TMP_InputField field)
    {
        if (GamepadSupport.IsGamepad) GamepadSupport.MarkSelectable(field.gameObject, null, null); // select-only + A to type / Steam keyboard
        EventSystem.current?.SetSelectedGameObject(field.gameObject);
        if (!GamepadSupport.IsGamepad) field.ActivateInputField();
    }
    internal static void SetModalCloseHandler(Action? handler) => _modalCloseHandler = handler;
    private static GameObject? _modalOpener;

    internal static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        if (parent.gameObject == _modal || parent.gameObject == _confirmation) PublicationNavigation.AttachBoundary(rect);
        else PublicationNavigation.Invalidate(parent);
        return rect;
    }
    internal static RectTransform Panel(string name, Transform parent, Color color)
    {
        var rect = Rect(name, parent); var image = rect.gameObject.AddComponent<Image>(); image.color = color;
        ModConfigPaperStyle.Apply(image, 6); rect.gameObject.AddComponent<OverviewPaperPanel>(); return rect;
    }
    /// <summary>A plain solid strip (group colour rails). Not a Panel: a panel carries the paper's rounded 9-slice
    /// sprite, whose corners meet across a 2–3px width and turn the strip into a lens shape.</summary>
    internal static RectTransform Rail(string name, Transform parent, Color color)
    {
        var rect = Rect(name, parent); var image = rect.gameObject.AddComponent<Image>();
        image.sprite = null; image.type = Image.Type.Simple; image.color = color; image.raycastTarget = false;
        return rect;
    }
    internal static void Fill(RectTransform rect, float padding = 0)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(padding, padding); rect.offsetMax = new Vector2(-padding, -padding);
    }
    internal static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
    /// <summary>
    /// 省略号溢出模式下，TMP 遇到比框还高的首行会整行不渲染；中日韩字形走更高的回退字体，按视觉高度估的框经常不够。
    /// 这里按真实行高放宽框再居中回原槽位，文字稍后再改也够用（空串按「国」量）。
    /// </summary>
    internal static void PlaceClipped(TextMeshProUGUI text, float x, float y, float width, float height)
    {
        text.overflowMode = TextOverflowModes.Ellipsis;
        float line = Mathf.Ceil(text.GetPreferredValues(text.text.Length > 0 ? text.text : "国").y) + 4;
        float grown = Mathf.Max(height, line);
        Place(text.rectTransform, x, y - (grown - height) / 2, width, grown);
    }
    private static string? _caret, _pointer;
    private static TMP_FontAsset? _glyphFont;
    /// <summary>下拉指示：游戏字体和本地化回退未必有 ▾，缺字会显示成豆腐块，这里退到更常见的字形。</summary>
    internal static string Caret => Glyph(ref _caret, "▾", "▼", "v");
    /// <summary>「正在查看」标记，同样做缺字兜底。</summary>
    internal static string Pointer => Glyph(ref _pointer, "▸", "▶", "●");
    private static string Glyph(ref string? cached, params string[] candidates)
    {
        var font = FontFallbackSwapper.instance != null ? FontFallbackSwapper.instance.mainBaseFont : null;
        if (cached != null && font == _glyphFont) return cached;
        _glyphFont = font;
        cached = candidates[candidates.Length - 1];
        foreach (string candidate in candidates)
            // 必须让 TMP 真的去源字体取字：动态图集里没渲染过的字形不在字符表里，只查表会把有的字也判成缺字。
            if (font == null || font.HasCharacters(candidate, out _, true, true)) { cached = candidate; break; }
        return cached;
    }
    internal static TextMeshProUGUI Text(Transform parent, string content, float size = 24)
    {
        var text = MenuAPI.CreateText(content, "MCE_Text").ParentTo(parent).TextMesh;
        text.enableAutoSizing = false; text.fontSize = size; text.color = Ivory; text.richText = false; text.parseCtrlCharacters = false;
        text.alignment = TextAlignmentOptions.TopLeft; text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow; text.raycastTarget = false;
        // MenuAPI provides the game's Daruma font; the running game's localized fallback is preferable.
        if (FontFallbackSwapper.instance != null && FontFallbackSwapper.instance.mainBaseFont != null) text.font = FontFallbackSwapper.instance.mainBaseFont;
        if (_referenceText != null && _referenceText.font == text.font)
        {
            // Keep the font asset's own atlas material; button stencil/material caches belong to that button.
            var nativeShadow = _referenceText.GetComponent<Shadow>();
            if (nativeShadow != null) { var shadow = text.gameObject.AddComponent<Shadow>(); shadow.effectColor = nativeShadow.effectColor; shadow.effectDistance = nativeShadow.effectDistance; shadow.useGraphicAlpha = nativeShadow.useGraphicAlpha; }
        }
        MceRegularFont.ApplyDefault(text);
        MceTextWeight.Apply(text);
        Loc.BindInline(text);
        return text;
    }
    internal static void ReadableName(TextMeshProUGUI text, float maximumWidth)
    {
        text.richText = true; text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular;
        foreach (var shadow in text.GetComponents<Shadow>()) shadow.enabled = false;
        MceTextWeight.Apply(text);
        text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Min(maximumWidth, text.GetPreferredValues(text.text).x + 8));
    }
    internal static Button Button(Transform parent, string label, Action action, float width = 120, float height = 36, UiActionRole role = UiActionRole.Neutral)
    {
        var peak = MenuAPI.CreateMenuButton(label).ParentTo(parent).SetColor(ModConfigLook.Surface(new Color(.29f, .23f, .16f)));
        var button = peak.Button;
        if (button.navigation.mode == Navigation.Mode.None) button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        UiActionStyle.Set(button, role);
        ModConfigPaperStyle.ApplyWobble(peak.Panel);
        // Replace the event container: RemoveAllListeners alone leaves persistent prefab callbacks intact.
        button.onClick = new Button.ButtonClickedEvent(); button.onClick.AddListener(() => Run(action));
        foreach (var localized in peak.GetComponentsInChildren<LocalizedText>(true)) UnityEngine.Object.Destroy(localized);
        // 1.1.4 (user: first list load sounds wrong, the intro page is the right crisp click): every button from the game
        // template carries "SFX Appear", which plays when the button is enabled. A list builds dozens at once and their
        // appear sounds pile up over the row fade-in sound. Manager buttons stay silent on appear; hover and click keep theirs.
        // OnEnable already started its delayed Play when the template was instantiated; changing the flag is too late.
        foreach (var sfx in peak.GetComponentsInChildren<SFX_PlayOneShot>(true))
            if (sfx.gameObject.name.IndexOf("Appear", StringComparison.OrdinalIgnoreCase) >= 0) { sfx.StopAllCoroutines(); sfx.enabled = false; UnityEngine.Object.Destroy(sfx); }
        Place(peak.RectTransform, 0, 0, width, height);
        OverviewButtonLayout.CenterPivot(peak.RectTransform);
        ModConfigSidebarWidgets.FitBorders(peak);
        var layout = peak.gameObject.AddComponent<OverviewButtonLayout>(); layout.Peak = peak;
        var caption = peak.Text as TextMeshProUGUI;
        if (caption != null)
        {
            if (_referenceText == null) _referenceText = caption; caption.fontSize = Math.Min(30, height * .42f); caption.enableAutoSizing = false;
            caption.textWrappingMode = TextWrappingModes.NoWrap; caption.overflowMode = TextOverflowModes.Overflow; caption.color = Ivory;
            caption.alignment = TextAlignmentOptions.Midline; caption.raycastTarget = false; Fill(caption.rectTransform, 0);
            caption.rectTransform.localRotation = Quaternion.identity; caption.rectTransform.localScale = Vector3.one;
            caption.margin = new Vector4(5, 0, 5, 0);
            caption.fontStyle = FontStyles.Normal;
            MceRegularFont.ApplyDefault(caption);
            MceTextWeight.Apply(caption);
            caption.text = label; caption.alpha = 1; Loc.BindInline(caption);
        }
        AnimatorRestGate.Attach(button);
        PublicationNavigation.Invalidate(parent);
        return button;
    }
    internal static Button IconButton(Transform parent, string icon, Action action, float width = 32, float height = 32)
    {
        Button button = Button(parent, "", action, width, height);
        foreach (var text in button.GetComponentsInChildren<TMP_Text>(true)) text.gameObject.SetActive(false);
        var peak = button.GetComponent<PeakMenuButton>();
        peak.BorderTop.gameObject.SetActive(false); peak.BorderBottom.gameObject.SetActive(false);
        var glow = button.transform.Find("Glow"); if (glow != null) UnityEngine.Object.Destroy(glow.gameObject);
        Fill(peak.Panel.rectTransform); ModConfigPaperStyle.Apply(peak.Panel, 4);
        var glyph = Rect("Icon_" + icon, button.transform); Fill(glyph, 6);
        var graphic = glyph.gameObject.AddComponent<OverviewIconGraphic>(); graphic.Kind = icon; graphic.color = Ivory; graphic.raycastTarget = false;
        return button;
    }
    /// <summary>
    /// 2.4.2：把描边图标换成与模组设置页同一张 sprite（主题钮的实心半月），金色与设置页方钮一致：
    /// 蓝色主题亮金 #EFC95F、紫色主题浅金。概览在主题变化后会重建（<see cref="ModConfigLook.Epoch"/>），建时取色即可。
    /// </summary>
    internal static void UseSpriteIcon(Button button, Sprite sprite)
    {
        var stroke = button.GetComponentInChildren<OverviewIconGraphic>(true);
        Transform host = button.transform;
        if (stroke != null) { host = stroke.transform; UnityEngine.Object.DestroyImmediate(stroke); } // 同一对象上不能有两个 Graphic，必须立即移除
        var image = host.gameObject.GetComponent<Image>() ?? host.gameObject.AddComponent<Image>();
        image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
        image.color = ModConfigLook.IsDark ? ModConfigSidebarWidgets.SelfRowTint.NightSelected : ModConfigSidebarWidgets.SelfRowTint.Day;
    }
    internal static Toggle Check(Transform parent, bool value, Action<bool> changed)
    {
        var existing = ModConfigSidebarWidgets.MakeCheckBox("MCE_OverviewCheck", parent, Vector2.zero, value, null);
        var root = existing.gameObject; var oldButton = root.GetComponent<Button>(); oldButton.onClick = new Button.ButtonClickedEvent();
        // Selectable forbids two selectable controls on one object. This Button was created above by us.
        UnityEngine.Object.DestroyImmediate(oldButton);
        // Reuse the same Enhance square and tick geometry; a Toggle supplies normal gamepad/keyboard navigation.
        var toggle = root.AddComponent<Toggle>(); toggle.targetGraphic = existing.Border; toggle.graphic = null; toggle.SetIsOnWithoutNotify(value);
        toggle.onValueChanged.AddListener(v => { existing.SetSilently(v); Run(() => changed(v)); });
        root.AddComponent<OverviewToggleVisual>().Sync = existing.SetSilently;
        Place((RectTransform)root.transform, 0, 0, 28, 28); return toggle;
    }
    internal static void SyncCheck(Toggle toggle, bool value) { toggle.SetIsOnWithoutNotify(value); toggle.GetComponent<OverviewToggleVisual>()?.Sync?.Invoke(value); }
    internal static TMP_InputField Input(Transform parent, string value, bool multiline = false, string placeholder = "")
    {
        var root = Panel("MCE_Input", parent, ModConfigLook.Surface(new Color(.11f, .10f, .08f, .98f)));
        root.gameObject.SetActive(false);
        var viewport = Rect("Viewport", root); Fill(viewport, 6); viewport.gameObject.AddComponent<RectMask2D>();
        if (!multiline) { viewport.offsetMin = new Vector2(6, 1); viewport.offsetMax = new Vector2(-6, -1); }
        var text = Text(viewport, "", 22); Fill(text.rectTransform); text.overflowMode = TextOverflowModes.Overflow;
        text.margin = Vector4.zero;
        text.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = multiline ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        var input = root.gameObject.AddComponent<TMP_InputField>(); input.textViewport = viewport; input.textComponent = text;
        input.onFocusSelectAll = false; input.customCaretColor = true; input.caretColor = Ivory; input.caretWidth = 2;
        input.selectionColor = new Color(.28f, .65f, .86f, .72f); input.caretBlinkRate = .8f;
        input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        // TMP otherwise restores the focus-time value before the page handles ESC, hiding unsaved changes.
        input.restoreOriginalTextOnEscape = false;
        input.richText = false;
        // Only single-line fields use the shared visual caret. Multiline editors retain TMP line placement.
        if (!multiline) root.gameObject.AddComponent<InputCaretLayout>();
        if (placeholder.Length > 0) SetPlaceholder(input, placeholder);
        input.SetTextWithoutNotify(value); root.gameObject.SetActive(true); return input;
    }
    internal static void SetPlaceholder(TMP_InputField input, string value)
    {
        if (input.placeholder == null)
        {
            var hint = Text(input.textViewport, value, input.textComponent.fontSize); Fill(hint.rectTransform);
            hint.alignment = input.textComponent.alignment; hint.margin = input.textComponent.margin;
            hint.color = ModConfigLook.Surface(new Color(.55f, .55f, .55f, 1)); input.placeholder = hint;
        }
        else if (input.placeholder is TMP_Text hint) hint.text = value;
        if (input.GetComponent<InputPlaceholderFocus>() == null) input.gameObject.AddComponent<InputPlaceholderFocus>();
    }
    internal static void Ask(string title, (string Label, string Value)[] fields, Action<string[]> saved, UiActionRole role = UiActionRole.Commit)
        => AskValidated(title, fields, null, saved, role);
    internal static void AskValidated(string title, (string Label, string Value)[] fields, Func<string[], string?>? validate, Action<string[]> saved, UiActionRole role = UiActionRole.Commit)
    {
        CloseModal(); if (ModalParent == null) return;
        var shade = Panel("MCE_Modal", ModalParent, ModConfigLook.Surface(new Color(0, 0, 0, .77f))); Fill(shade); _modal = shade.gameObject; shade.SetAsLastSibling();
        // Sized to what it holds (2.3.3). It used to fill 66%x80% of the screen whatever it held, so a one-line name prompt
        // was a huge empty sheet. Height = heading + fields + footer, capped by the screen (the field list scrolls).
        var screen = ((RectTransform)ModalParent).rect;
        float width = Mathf.Min(fields.Length == 0 ? 660 : 820, Mathf.Max(380, screen.width - 60));
        int columns = Math.Max(1, Math.Min(16, (int)((width - 50) / 35)));
        float fieldsHeight = 0;
        foreach (var field in fields) fieldsHeight += ColorUtility.TryParseHtmlString(field.Value, out _) && field.Value.StartsWith("#") ? 83 + 35 * ((Palette.Length + columns - 1) / columns) : 82;
        var panel = Panel("Dialog", shade, Brown); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f); panel.anchoredPosition = Vector2.zero;
        var heading = Text(panel, title, 27); heading.textWrappingMode = TextWrappingModes.Normal;
        // A confirmation carries its whole question in the title, so let it wrap instead of running off one line.
        float headingHeight = Mathf.Min(Mathf.Max(40, Mathf.Ceil(heading.GetPreferredValues(title, width - 40, 0).y) + 4), 240);
        Place(heading.rectTransform, 20, 16, width - 40, headingHeight); heading.rectTransform.anchorMax = Vector2.one; heading.rectTransform.sizeDelta = new Vector2(-40, headingHeight);
        float top = 22 + headingHeight;
        panel.sizeDelta = new Vector2(width, Mathf.Min(top + (fields.Length == 0 ? 0 : Mathf.Max(fieldsHeight, 82)) + 120, Mathf.Max(260, screen.height - 60)));
        var host = Rect("Fields", panel); Fill(host); host.offsetMin = new Vector2(20, 120); host.offsetMax = new Vector2(-30, -top);
        var scroll = host.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        var view = Rect("Viewport", host); Fill(view); view.gameObject.AddComponent<RectMask2D>(); view.gameObject.AddComponent<Image>().color = Color.clear; scroll.viewport = view;
        var content = Rect("Content", view); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1); content.sizeDelta = new Vector2(0, Math.Max(fields.Length * 82, 80)); scroll.content = content;
        var inputs = new List<TMP_InputField>();
        float fieldY = 0;
        for (int i = 0; i < fields.Length; i++)
        {
            var label = Text(content, fields[i].Label, 18); Place(label.rectTransform, 0, fieldY, 700, 26); label.rectTransform.anchorMax = Vector2.one; label.rectTransform.sizeDelta = new Vector2(0, 26);
            var input = Input(content, fields[i].Value); input.characterLimit = 16384;
            var rect = (RectTransform)input.transform; rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(0, -fieldY - 27); rect.sizeDelta = new Vector2(0, 42); inputs.Add(input);
            if (bool.TryParse(fields[i].Value, out bool boolean))
            {
                input.SetTextWithoutNotify(boolean ? "true" : "false");
                input.gameObject.SetActive(false);
                var checkRow=Rect("CheckRow",content);checkRow.anchorMin=new Vector2(0,1);checkRow.anchorMax=Vector2.one;checkRow.pivot=new Vector2(0,1);checkRow.sizeDelta=new Vector2(0,32);checkRow.anchoredPosition=new Vector2(0,-fieldY-29);
                var box = Check(checkRow, boolean, v => input.SetTextWithoutNotify(v ? "true" : "false")); Place((RectTransform)box.transform, 4, 1, 30, 30);
                var hint = Text(checkRow, Loc.T("Checked = allow / on", "勾选表示允许／开启", "勾選表示允許／開啟"), 19);Fill(hint.rectTransform);hint.rectTransform.offsetMin=new Vector2(42,0);hint.alignment=TextAlignmentOptions.MidlineLeft;
            }
            if (ColorUtility.TryParseHtmlString(fields[i].Value, out _) && fields[i].Value.StartsWith("#"))
            {
                for (int p = 0; p < Palette.Length; p++) { string choice = Palette[p]; var swatch = IconButton(content, "color", () => input.SetTextWithoutNotify(choice), 28, 28); Place((RectTransform)swatch.transform, (p % columns) * 35, fieldY + 73 + (p / columns) * 35, 28, 28); if (ColorUtility.TryParseHtmlString(choice, out Color tint)) swatch.GetComponentInChildren<OverviewIconGraphic>().color = tint; }
                fieldY += 83 + 35 * ((Palette.Length + columns - 1) / columns);
            }
            else fieldY += 82;
        }
        content.sizeDelta = new Vector2(0, Math.Max(fieldY, 80));
        var initialValues = inputs.ConvertAll(f => f.text).ToArray();
        _isModalDirty = () =>
        {
            for (int i = 0; i < inputs.Count; i++)
                if (!string.Equals(inputs[i].text, initialValues[i], StringComparison.Ordinal)) return true;
            return false;
        };
        var footer=Rect("Footer",panel);footer.anchorMin=Vector2.zero;footer.anchorMax=new Vector2(1,0);footer.pivot=Vector2.zero;footer.sizeDelta=new Vector2(0,110);
        var errorArea=Rect("Validation",footer);errorArea.anchorMin=new Vector2(0,1);errorArea.anchorMax=Vector2.one;errorArea.pivot=new Vector2(0,1);errorArea.sizeDelta=new Vector2(-40,44);errorArea.anchoredPosition=new Vector2(20,0);errorArea.gameObject.AddComponent<RectMask2D>();
        var errorText = Text(errorArea, "", 17); errorText.color = new Color(.95f, .57f, .43f); errorText.textWrappingMode = TextWrappingModes.Normal;Fill(errorText.rectTransform);
        var buttonArea=Rect("Actions",footer);buttonArea.anchorMin=Vector2.zero;buttonArea.anchorMax=new Vector2(1,0);buttonArea.pivot=Vector2.zero;buttonArea.sizeDelta=new Vector2(0,60);
        var save = Button(buttonArea, Loc.T("Save", "保存", "儲存"), () => {
            try {
                var values = inputs.ConvertAll(f => f.text).ToArray(); string? error = validate?.Invoke(values);
                if (!string.IsNullOrWhiteSpace(error)) { errorText.text = error; return; }
                saved(values); if (_modal == shade.gameObject) CloseModal();
            } catch (Exception ex) { errorText.text = Loc.T("Could not save: ", "保存失败：", "儲存失敗：") + ex.Message; }
        }, 130, 36, role);
        var cancel = Button(buttonArea, Loc.T("Cancel", "取消", "取消"), RequestCloseModal, 130, 36);
        Bottom((RectTransform)save.transform, 20, 18); Bottom((RectTransform)cancel.transform, 160, 18);
        var first = inputs.Find(f => f.gameObject.activeSelf); if (first != null) FocusInput(first);
    }
    internal static void Confirm(string title, Action confirmed, UiActionRole role = UiActionRole.Commit) => Ask(title, Array.Empty<(string, string)>(), confirmedValues => confirmed(), role);
    internal static RectTransform? CreateModalPanel(string name, float width, float height)
    {
        // A dialog replacing another keeps the first opener (the one outside every dialog), not a button of the old dialog.
        var opener = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (_modal != null && opener != null && opener.transform.IsChildOf(_modal.transform)) opener = _modalOpener;
        var keep = opener; _modalOpener = null;
        CloseModal(); if (ModalParent == null) return null;
        var shade = Panel(name, ModalParent, ModConfigLook.Surface(new Color(0, 0, 0, .72f))); Fill(shade); shade.SetAsLastSibling(); _modal = shade.gameObject;
        _modalOpener = keep;
        var panel = Panel("Window", shade, Brown); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        panel.anchoredPosition = Vector2.zero; panel.sizeDelta = new Vector2(Mathf.Min(width, Mathf.Max(380, ((RectTransform)ModalParent).rect.width - 60)), Mathf.Min(height, Mathf.Max(320, ((RectTransform)ModalParent).rect.height - 60)));
        return panel;
    }
    internal static void ConfirmText(string title, string body, Action confirmed, UiActionRole role = UiActionRole.Commit, string? confirmLabel = null, Action? cancelled = null)
    {
        var panel = CreateModalPanel("MCE_TextConfirmation", 780, 460); if (panel == null) return;
        var owner = _modal;
        SaveDialogHeader(panel, title, Loc.T("Review the details, then choose how to continue.", "核对详情后选择下一步操作。", "核對詳情後選擇下一步操作。"));
        SaveDialogBody(panel, body, attention: true);
        var accept = SaveDialogAction(panel, confirmLabel ?? Loc.T("Confirm", "确认", "確認"), () => { confirmed(); if (_modal == owner) CloseModal(); }, 20, 190, role);
        var cancel = SaveDialogAction(panel, Loc.T("Back", "返回", "返回"), cancelled ?? CloseModal, 220, 100, UiActionRole.Neutral);
        EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
    }
    internal static void ConfirmUpdatePolicy(string warning, Action<bool> confirmed)
    {
        CloseModal(); if (ModalParent == null) return;
        var shade = Panel("MCE_UpdateWarning", ModalParent, ModConfigLook.Surface(new Color(0, 0, 0, .8f))); Fill(shade); _modal = shade.gameObject; shade.SetAsLastSibling();
        var panel = Panel("Warning", shade, Brown); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f); panel.anchoredPosition = Vector2.zero;
        { var screen = ((RectTransform)ModalParent).rect; panel.sizeDelta = new Vector2(Mathf.Min(820, Mathf.Max(380, screen.width - 60)), Mathf.Min(360, Mathf.Max(260, screen.height - 60))); }
        var message = Text(panel, warning, 25); Fill(message.rectTransform); message.rectTransform.offsetMin = new Vector2(24, 112); message.rectTransform.offsetMax = new Vector2(-24, -22); message.textWrappingMode = TextWrappingModes.Normal;
        bool suppress = false; var row = Rect("Suppress", panel); row.anchorMin = row.anchorMax = row.pivot = Vector2.zero; row.sizeDelta = new Vector2(600, 34); row.anchoredPosition = new Vector2(24, 64);
        var check = Check(row, false, value => suppress = value); Place((RectTransform)check.transform, 0, 1, 30, 30);
        var label = Text(row, Loc.T("Do not show again", "以后不再显示", "以後不再顯示"), 21); Place(label.rectTransform, 40, 0, 500, 34); label.alignment = TextAlignmentOptions.MidlineLeft;
        var error = Text(panel, "", 17); error.color = new Color(.95f, .57f, .43f); error.rectTransform.anchorMin = error.rectTransform.anchorMax = error.rectTransform.pivot = Vector2.zero; error.rectTransform.anchoredPosition = new Vector2(24, 98); error.rectTransform.sizeDelta = new Vector2(600, 30);
        var yes = Button(panel, Loc.T("Enable", "确认开启", "確認開啟"), () => { try { confirmed(suppress); if (_modal == shade.gameObject) CloseModal(); } catch (Exception ex) { error.text = ex.Message; } }, 150, 36, UiActionRole.Commit); Bottom((RectTransform)yes.transform, 24, 18);
        var cancel = Button(panel, Loc.T("Cancel", "取消", "取消"), CloseModal, 130, 36); Bottom((RectTransform)cancel.transform, 184, 18);
    }
    internal static void Choose(string title, IReadOnlyList<(string Label, string Value)> options, string current, Action<string> chosen, Func<string, UiActionRole>? role = null)
    {
        if (options.Count <= 16 && UnityEngine.EventSystems.EventSystem.current is UnityEngine.EventSystems.EventSystem eventSystem && eventSystem.currentSelectedGameObject != null && eventSystem.currentSelectedGameObject.transform is RectTransform selectedAnchor)
        {
            CompactDropdown(selectedAnchor, title, options.Select(item => new UiMenuEntry {
                Label = item.Label, Checked = item.Value == current, Role = role?.Invoke(item.Value) ?? UiActionRole.Edit,
                Invoke = () => chosen(item.Value)
            }).ToList());
            return;
        }
        CloseModal(); if (ModalParent == null) return;
        var shade = Panel("MCE_Choice", ModalParent, ModConfigLook.Surface(new Color(0, 0, 0, .77f))); Fill(shade); _modal = shade.gameObject; shade.SetAsLastSibling();
        var panel = Panel("ChoicePanel", shade, Brown); panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f); panel.anchoredPosition = Vector2.zero;
        { var screen = ((RectTransform)ModalParent).rect; panel.sizeDelta = new Vector2(Mathf.Min(640, Mathf.Max(380, screen.width - 60)), Mathf.Min(60 + options.Count * 45 + 65 + 10, Mathf.Max(260, screen.height - 60))); }
        var heading = Text(panel, title, 27); Place(heading.rectTransform, 20, 15, 600, 42); heading.rectTransform.anchorMax = Vector2.one; heading.rectTransform.sizeDelta = new Vector2(-40, 42);
        var scrollRoot = Rect("Choices", panel); Fill(scrollRoot); scrollRoot.offsetMin = new Vector2(20, 60); scrollRoot.offsetMax = new Vector2(-20, -65);
        var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 40; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = Rect("Viewport", scrollRoot); Fill(viewport); viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var content = Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0, 1); content.sizeDelta = new Vector2(0, options.Count * 45);
        scroll.viewport = viewport; scroll.content = content;
        for (int i = 0; i < options.Count; i++)
        {
            var item = options[i]; var button = Button(content, (item.Value == current ? "✓ " : "") + item.Label, () => { CloseModal(); chosen(item.Value); }, 400, 36, role?.Invoke(item.Value) ?? UiActionRole.Edit);
            var rect = (RectTransform)button.transform; rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0, 1); rect.sizeDelta = new Vector2(0, 36); rect.anchoredPosition = new Vector2(0, -45 * i);
        }
        var cancel = Button(panel, Loc.T("Cancel", "取消", "取消"), CloseModal, 130, 36); Bottom((RectTransform)cancel.transform, 20, 16);
    }
    /// <summary>贴着控件正下方展开的下拉列表：点空白或 ESC 关闭；下方放不下就翻到控件上方。</summary>
    internal static void Dropdown(RectTransform? anchor, string title, IReadOnlyList<(string Label, string Value)> options, string current, Action<string> chosen)
    {
        if (anchor == null) { Choose(title, options, current, chosen); return; }
        Dropdown(anchor, title, options.Select(item => new UiMenuEntry { Label = item.Label, Checked = item.Value == current, Role = UiActionRole.Edit, Invoke = () => chosen(item.Value) }).ToList());
    }
    internal static RectTransform? Dropdown(RectTransform anchor, string title, IReadOnlyList<UiMenuEntry> options, float preferredWidth = 0)
    {
        if (ModalParent == null || anchor == null) return null;
        if (options.Count(item => !item.Header) <= 16)
            return CompactDropdown(anchor, title, options);
        var host = (RectTransform)ModalParent;
        var corners = new Vector3[4]; anchor.GetWorldCorners(corners);
        Vector3 bottomLeft = host.InverseTransformPoint(corners[0]), topRight = host.InverseTransformPoint(corners[2]);
        Rect area = host.rect;
        CloseModal();
        var shade = Panel("MCE_Dropdown", ModalParent, ModConfigLook.Surface(new Color(0, 0, 0, .35f))); Fill(shade); _modal = shade.gameObject; shade.SetAsLastSibling();
        shade.gameObject.AddComponent<OverviewTextClick>().Clicked = _ => CloseModal();
        var panel = Panel("List", shade, Brown);
        panel.gameObject.AddComponent<RectMask2D>();
        panel.gameObject.AddComponent<DropdownFocusReturn>().Anchor = anchor;
        panel.gameObject.AddComponent<OverviewTextClick>();
        var heading = Text(panel, title, 16); heading.color = Muted; heading.alignment = TextAlignmentOptions.MidlineLeft;
        heading.textWrappingMode = TextWrappingModes.NoWrap; heading.enableAutoSizing = true;
        heading.fontSizeMin = 12; heading.fontSizeMax = 16; heading.overflowMode = TextOverflowModes.Ellipsis;
        string Caption(UiMenuEntry item) => ((item.Checked ? "✓ " : "") + item.Label
            + (!item.Enabled && item.DisabledReason.Length > 0 ? " · " + item.DisabledReason : ""))
            .Replace('\r', ' ').Replace('\n', ' ');
        float availableWidth = Mathf.Max(1, area.width - 16);
        float measuredWidth = options.Select(item => heading.GetPreferredValues(Caption(item)).x + 48)
            .Append(heading.GetPreferredValues(title).x + 32).DefaultIfEmpty(220).Max();
        // The anchor determines position only: a wide source row must not stretch its menu.
        float width = Mathf.Clamp(preferredWidth > 0 ? preferredWidth : measuredWidth,
            Mathf.Min(180, availableWidth), Mathf.Min(480, availableWidth));
        float caption = title.Length > 0 ? 32 : 0;
        float contentHeight = options.Sum(item => item.Header ? 30 : 36);
        float wanted = contentHeight + 16 + caption;
        var bounds = UiGeometry.Popover(area.width, area.height,
            new UiGeometry.Box(bottomLeft.x - area.xMin, area.yMax - topRight.y,
                topRight.x - bottomLeft.x, topRight.y - bottomLeft.y), width, wanted);
        width = bounds.Width;
        Place(panel, bounds.X, bounds.Y, width, bounds.Height);
        heading.gameObject.SetActive(caption > 0);
        if (caption > 0) PlaceClipped(heading, 16, 4, Mathf.Max(1, width - 32), 28);
        var scrollRoot = Rect("Choices", panel); Fill(scrollRoot);
        scrollRoot.offsetMin = new Vector2(12, 8); scrollRoot.offsetMax = new Vector2(-12, -(8 + caption));
        var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.scrollSensitivity = 40;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.inertia = false;
        var viewport = Rect("Viewport", scrollRoot); Fill(viewport);
        viewport.gameObject.AddComponent<RectMask2D>(); viewport.gameObject.AddComponent<Image>().color = Color.clear;
        var content = Rect("Content", viewport); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0, 1); content.sizeDelta = new Vector2(0, Mathf.Max(contentHeight, 1));
        scroll.viewport = viewport; scroll.content = content;
        float rowWidth = Mathf.Max(1, width - 24), itemY = 0;
        GameObject? focus = null;
        var enabled = new List<Button>();
        foreach (var item in options)
        {
            if (item.Header)
            {
                var group = Text(content, item.Label, 15); group.color = Muted; group.alignment = TextAlignmentOptions.MidlineLeft;
                group.enableAutoSizing = true; group.fontSizeMin = 12; group.fontSizeMax = 15;
                group.textWrappingMode = TextWrappingModes.NoWrap;
                PlaceClipped(group, 12, itemY, Mathf.Max(1, rowWidth - 24), 28); itemY += 30; continue;
            }
            var button = FlatListRow.Create(content, () => {
                if (!item.Enabled || item.CanInvoke?.Invoke() == false) return;
                CloseModal(); item.Execute();
            }, rowWidth, 34, UiActionStyle.ColorFor(item.Role, ModConfigLook.IsDark));
            button.interactable = item.Enabled;
            var rect = (RectTransform)button.transform; Place(rect, 0, itemY, rowWidth, 34); itemY += 36;
            var label = Text(button.transform, Caption(item), 16);
            label.alignment = TextAlignmentOptions.MidlineLeft; label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true; label.fontSizeMin = 12; label.fontSizeMax = 16;
            label.margin = Vector4.zero; label.overflowMode = TextOverflowModes.Ellipsis;
            // Keep the caption inside both the flat item and the paper panel's safe edge.
            PlaceClipped(label, 16, 2, Mathf.Max(1, rowWidth - 32), 30);
            GamepadSupport.MarkSelectable(button.gameObject, scroll, rect);
            if (item.Enabled) {
                enabled.Add(button);
                if (item.Checked || focus == null) focus = button.gameObject;
            }
        }
        for (int i = 0; i < enabled.Count; i++)
            enabled[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnUp = i > 0 ? enabled[i - 1] : null,
                selectOnDown = i + 1 < enabled.Count ? enabled[i + 1] : null };
        if (focus != null) EventSystem.current?.SetSelectedGameObject(focus);
        return panel;
    }

    internal static void ShowText(string title, string body)
    {
        var panel = CreateModalPanel("MCE_ReadDialog", 820, 500); if (panel == null) return;
        SaveDialogHeader(panel, title, Loc.T("Details · select Copy to share the complete message.", "详情 · 可复制完整信息用于反馈。", "詳情 · 可複製完整資訊供回報。"));
        SaveDialogBody(panel, body);
        SaveDialogAction(panel, Loc.T("Close", "关闭", "關閉"), CloseModal, 20, 110, UiActionRole.Neutral);
        SaveDialogAction(panel, Loc.T("Copy details", "复制详情", "複製詳情"), () => GUIUtility.systemCopyBuffer = body, 140, 120, UiActionRole.Neutral);
    }
    /// <summary>Consumes one back request. A dirty form stays alive underneath its discard confirmation.</summary>
    internal static bool TryCloseTopModal()
    {
        if (GamepadSupport.BackSwallowed || ControllerReadingSurface.ConsumeBack()) return true;
        if (WindowPopover.TryCloseTop()) return true;
        if (_confirmation != null) { CloseConfirmation(true); return true; }
        if (_modal == null) return false;
        if (_modalCloseHandler != null) { _modalCloseHandler(); return true; }
        if (_isModalDirty?.Invoke() == true)
            ConfirmDiscard(Loc.T("Discard unsaved changes?", "放弃未保存的修改？", "放棄未儲存的修改？"), CloseModal);
        else CloseModal();
        return true;
    }
    internal static void RequestCloseModal() => TryCloseTopModal();

    internal static void ConfirmDiscard(string title, Action discard, Action? stay = null)
        => ShowUnsavedConfirmation(title, null, discard, stay);

    internal static void ConfirmUnsaved(string title, Action saveAndLeave, Action discardAndLeave, Action stay)
        => ShowUnsavedConfirmation(title, saveAndLeave, discardAndLeave, stay);

    private static void ShowUnsavedConfirmation(string title, Action? save, Action discard, Action? stay)
    {
        // This is a second layer, never a replacement for the edited form and its input controls.
        if (_confirmation != null || ModalParent == null) return;
        _continueEditing = stay;
        _previousSelection = EventSystem.current?.currentSelectedGameObject;
        _previousSelection?.GetComponent<TMP_InputField>()?.DeactivateInputField();
        _suspendedModal = _modal != null
            ? _modal.GetComponent<CanvasGroup>() ?? _modal.AddComponent<CanvasGroup>()
            : _previousSelection?.GetComponentInParent<CanvasGroup>();
        if (_suspendedModal != null)
        {
            _wasModalInteractable = _suspendedModal.interactable; _suspendedModal.interactable = false;
        }
        var shade = Panel("MCE_UnsavedConfirmation", ModalParent, ModConfigLook.Surface(new Color(0, 0, 0, .8f)));
        Fill(shade); shade.SetAsLastSibling(); _confirmation = shade.gameObject;
        var confirmationCanvas = shade.gameObject.AddComponent<CanvasGroup>(); confirmationCanvas.ignoreParentGroups = true;
        var panel = Panel("Confirmation", shade, DialogSurface);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
        var bounds = ((RectTransform)ModalParent).rect;
        panel.sizeDelta = new Vector2(Mathf.Min(740, bounds.width - 40), Mathf.Min(370, bounds.height - 40));
        SaveDialogHeader(panel, title, Loc.T("Unsaved workspace", "工作区有未提交的编辑", "工作區有未提交的編輯"));
        SaveDialogBody(panel, save != null
            ? Loc.T("Save commits your draft before leaving.\nDiscard returns to the last saved state.\nContinue editing keeps this page and all changes.", "保存：提交当前草稿后离开。\n放弃：恢复最近保存的内容。\n继续编辑：留在当前页面，保留所有修改。", "儲存：提交目前草稿後離開。\n放棄：恢復最近儲存的內容。\n繼續編輯：留在目前頁面，保留所有修改。")
            : Loc.T("Discard these edits and continue, or return to editing.", "放弃本次编辑并继续，或返回保留修改。", "放棄本次編輯並繼續，或返回保留修改。"));
        var actions = Rect("Actions", panel); actions.anchorMin = Vector2.zero; actions.anchorMax = new Vector2(1, 0);
        actions.pivot = Vector2.zero; actions.sizeDelta = new Vector2(-40, 56); actions.anchoredPosition = new Vector2(20, 6);
        var layout = actions.gameObject.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 10; layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = true; layout.childControlHeight = false; layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
        void AddAction(string label, Action action, UiActionRole role)
        {
            var button = Button(actions, label, action, 170, 42, role);
            var sizing = button.gameObject.AddComponent<LayoutElement>(); sizing.minWidth = 80; sizing.preferredWidth = 170; sizing.flexibleWidth = 1;
        }
        AddAction(Loc.T("Discard changes", "放弃修改", "放棄修改"), () => { CloseConfirmation(false); discard(); }, UiActionRole.Danger);
        var continueButton = Button(actions, Loc.T("Continue editing", "继续编辑", "繼續編輯"), () => CloseConfirmation(true), 170, 42, UiActionRole.Edit);
        var continueSizing = continueButton.gameObject.AddComponent<LayoutElement>(); continueSizing.minWidth = 80; continueSizing.preferredWidth = 170; continueSizing.flexibleWidth = 1;
        if (save != null) AddAction(Loc.T("Save and leave", "保存并离开", "儲存並離開"), () => { CloseConfirmation(false); save(); }, UiActionRole.Commit);
        EventSystem.current?.SetSelectedGameObject(continueButton.gameObject);
    }

    private static void CloseConfirmation(bool continueEditing)
    {
        if (_confirmation != null) { _confirmation.SetActive(false); UnityEngine.Object.Destroy(_confirmation); }
        _confirmation = null;
        if (_suspendedModal != null) _suspendedModal.interactable = _wasModalInteractable;
        _suspendedModal = null;
        var stay = _continueEditing; var selection = _previousSelection;
        _continueEditing = null; _previousSelection = null;
        if (!continueEditing) return;
        if (selection != null && selection.activeInHierarchy)
        {
            EventSystem.current?.SetSelectedGameObject(selection);
            if (selection.GetComponent<TMP_InputField>() is TMP_InputField field) FocusInput(field);
        }
        stay?.Invoke();
    }

    /// <summary>Forced cleanup for successful saves, page disposal and deliberate modal replacement.</summary>
    internal static void CloseModal()
    {
        CloseConfirmation(false);
        bool hadModal = _modal != null;
        if (_modal != null) { _modal.SetActive(false); UnityEngine.Object.Destroy(_modal); }
        _modal = null; _isModalDirty = null; _modalCloseHandler = null;
        // 1.1.4 (gamepad review): the focus went nowhere after a dialog closed; give it back to the button that opened it.
        var opener = _modalOpener; _modalOpener = null;
        if (hadModal && opener != null && opener.activeInHierarchy && GamepadSupport.IsGamepad) EventSystem.current?.SetSelectedGameObject(opener);
    }
    internal static void Bottom(RectTransform rect, float x, float y) { rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero; rect.anchoredPosition = new Vector2(x, y); }
    private static void Run(Action action) { try { action(); } catch (Exception ex) { Plugin.Log.LogError("[模组概览] 操作失败：" + ex); } }
}

internal sealed class OverviewToggleVisual : MonoBehaviour { internal Action<bool>? Sync; }

internal sealed class OverviewButtonLayout : MonoBehaviour
{
    internal PeakMenuButton Peak = null!;
    private float _height = -1;
    private static readonly Vector3[] Corners=new Vector3[4];
    internal static void CenterPivot(RectTransform rect)
    {
        if(rect.pivot==new Vector2(.5f,.5f))return;
        Vector3 scale=rect.localScale;rect.localScale=Vector3.one;rect.GetWorldCorners(Corners);Vector3 corner=Corners[0];rect.pivot=new Vector2(.5f,.5f);rect.GetWorldCorners(Corners);rect.position+=corner-Corners[0];rect.localScale=scale;
    }
    private void OnEnable()=>OverviewMaintenance.RegisterEvent(this,Refresh);
    private void OnDisable()=>OverviewMaintenance.Remove(this);
    private void OnRectTransformDimensionsChange()=>OverviewMaintenance.Mark(this,Refresh);
    private void Refresh()
    {
        if (Peak == null || Peak.RectTransform == null) return;
        CenterPivot(Peak.RectTransform);
        float height = Peak.RectTransform.rect.height;
        if (Math.Abs(height - _height) < .1f) return;
        _height = height; ModConfigSidebarWidgets.FitBorders(Peak);
        if (Peak.Text != null && !Peak.Text.enableAutoSizing) Peak.Text.fontSize = Math.Min(30, height * .42f);
    }
}

internal sealed class OverviewIconGraphic : MaskableGraphic
{
    internal string Kind = "home";
    public override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var r = rectTransform.rect; float s = Math.Min(r.width, r.height); Vector2 origin = r.center - new Vector2(s / 2, s / 2);
        void Line(float ax, float ay, float bx, float by, float thickness = .075f)
        {
            Vector2 a = origin + new Vector2(ax, ay) * s, b = origin + new Vector2(bx, by) * s;
            Vector2 n = (b - a).normalized; n = new Vector2(-n.y, n.x) * s * thickness / 2;
            int index = vh.currentVertCount; vh.AddVert(a - n, color, Vector2.zero); vh.AddVert(a + n, color, Vector2.zero); vh.AddVert(b + n, color, Vector2.zero); vh.AddVert(b - n, color, Vector2.zero); vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
        }
        if (Kind == "plus") { Line(.18f, .50f, .82f, .50f); Line(.50f, .18f, .50f, .82f); }
        else if (Kind == "arrow-right") { Line(.18f, .50f, .80f, .50f); Line(.52f, .24f, .82f, .52f); Line(.52f, .76f, .82f, .48f); } // 2.4.7：与 plus 同笔画
        else if (Kind == "triangle-right" || Kind == "triangle-down")
        {
            Vector2 a = Kind == "triangle-right" ? new Vector2(.30f,.18f) : new Vector2(.18f,.68f);
            Vector2 b = Kind == "triangle-right" ? new Vector2(.30f,.82f) : new Vector2(.82f,.68f);
            Vector2 c = Kind == "triangle-right" ? new Vector2(.78f,.50f) : new Vector2(.50f,.20f);
            vh.AddVert(origin+a*s,color,Vector2.zero); vh.AddVert(origin+b*s,color,Vector2.zero); vh.AddVert(origin+c*s,color,Vector2.zero); vh.AddTriangle(0,1,2);
        }
        else if (Kind == "import")
        {
            Line(.50f, .90f, .50f, .35f, .095f);
            Line(.28f, .57f, .50f, .35f, .095f); Line(.50f, .35f, .72f, .57f, .095f);
            Line(.15f, .38f, .15f, .12f); Line(.15f, .12f, .85f, .12f); Line(.85f, .12f, .85f, .38f);
        }
        else if (Kind == "gear")
        {
            for (int i=0;i<20;i++) { float a=i*Mathf.PI/10,b=a+Mathf.PI/10; Line(.5f+.29f*Mathf.Cos(a),.5f+.29f*Mathf.Sin(a),.5f+.29f*Mathf.Cos(b),.5f+.29f*Mathf.Sin(b),.095f); }
            for (int i=0;i<8;i++) { float a=i*Mathf.PI/4; Line(.5f+.25f*Mathf.Cos(a),.5f+.25f*Mathf.Sin(a),.5f+.43f*Mathf.Cos(a),.5f+.43f*Mathf.Sin(a),.12f); }
        }
        else if (Kind == "palette")
        {
            for (int i = 0; i < 20; i++) { float a = i * Mathf.PI / 10, b = a + Mathf.PI / 10; Line(.5f + .4f * Mathf.Cos(a), .5f + .4f * Mathf.Sin(a), .5f + .4f * Mathf.Cos(b), .5f + .4f * Mathf.Sin(b), .065f); }
            Line(.32f, .66f, .34f, .66f, .13f); Line(.63f, .70f, .65f, .70f, .13f); Line(.73f, .40f, .75f, .40f, .13f); Line(.24f, .39f, .26f, .39f, .13f); Line(.45f, .20f, .60f, .20f, .15f);
        }
        else if (Kind == "color") { Line(.20f, .50f, .80f, .50f, .60f); }
        else if (Kind == "pencil") { Line(.12f, .12f, .76f, .76f, .18f); Line(.15f, .08f, .03f, .03f, .10f); Line(.79f, .79f, .89f, .89f, .18f); }
        else if (Kind == "hide")
        {
            // Keep the compact ring and X centered; both strokes use visible dash gaps.
            for (int i = 0; i < 10; i++) { float a = i * Mathf.PI / 5, b = a + Mathf.PI / 8; Line(.5f + .27f * Mathf.Cos(a), .5f + .27f * Mathf.Sin(a), .5f + .27f * Mathf.Cos(b), .5f + .27f * Mathf.Sin(b), .065f); }
            for (int i = 0; i < 3; i++) { float a = .30f + i * .15f, b = a + .10f; Line(a, a, b, b, .10f); Line(a, 1 - a, b, 1 - b, .10f); }
        }
        else if (Kind == "speaker" || Kind == "speaker-muted")
        {
            void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                int index = vh.currentVertCount;
                vh.AddVert(origin + a * s, color, Vector2.zero); vh.AddVert(origin + b * s, color, Vector2.zero); vh.AddVert(origin + c * s, color, Vector2.zero); vh.AddVert(origin + d * s, color, Vector2.zero);
                vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
            }
            Quad(new Vector2(.10f, .38f), new Vector2(.10f, .62f), new Vector2(.28f, .62f), new Vector2(.28f, .38f));
            Quad(new Vector2(.28f, .38f), new Vector2(.28f, .62f), new Vector2(.50f, .84f), new Vector2(.50f, .16f));
            if (Kind == "speaker-muted") { Line(.62f, .36f, .88f, .64f, .09f); Line(.62f, .64f, .88f, .36f, .09f); }
            else
                foreach (float radius in new[] { .17f, .31f })
                    for (int i = 0; i < 6; i++)
                    {
                        float a = -.75f + i * .25f, b = a + .25f;
                        Line(.50f + radius * Mathf.Cos(a), .50f + radius * Mathf.Sin(a), .50f + radius * Mathf.Cos(b), .50f + radius * Mathf.Sin(b), .07f);
                    }
        }
        else if (Kind == "folder")
        {
            // Outline folder: body plus a tab on the upper left.
            Line(.10f, .18f, .90f, .18f); Line(.90f, .18f, .90f, .68f); Line(.90f, .68f, .10f, .68f); Line(.10f, .68f, .10f, .18f);
            Line(.10f, .68f, .14f, .80f); Line(.14f, .80f, .40f, .80f); Line(.40f, .80f, .46f, .68f);
        }
        else if (Kind == "check") { Line(.16f, .52f, .40f, .26f, .13f); Line(.40f, .26f, .86f, .78f, .13f); }
        else if (Kind == "cross") { Line(.20f, .20f, .80f, .80f, .13f); Line(.20f, .80f, .80f, .20f, .13f); }
        else if (Kind == "warn")
        {
            Line(.50f, .88f, .08f, .14f, .09f); Line(.08f, .14f, .92f, .14f, .09f); Line(.92f, .14f, .50f, .88f, .09f);
            Line(.50f, .34f, .50f, .62f, .10f); Line(.50f, .22f, .50f, .25f, .11f);
        }
        else if (Kind == "star") { for (int i = 0; i < 5; i++) { float a1 = Mathf.PI / 2 + i * 4 * Mathf.PI / 5, a2 = Mathf.PI / 2 + (i + 1) * 4 * Mathf.PI / 5; Line(.5f + .42f * Mathf.Cos(a1), .5f + .42f * Mathf.Sin(a1), .5f + .42f * Mathf.Cos(a2), .5f + .42f * Mathf.Sin(a2), .08f); } }
        else if (Kind == "file")
        {
            // Page with a folded corner and two text lines.
            Line(.20f, .10f, .20f, .90f, .08f); Line(.20f, .90f, .62f, .90f, .08f); Line(.62f, .90f, .82f, .70f, .08f); Line(.82f, .70f, .82f, .10f, .08f); Line(.82f, .10f, .20f, .10f, .08f);
            Line(.62f, .90f, .62f, .70f, .07f); Line(.62f, .70f, .82f, .70f, .07f); Line(.32f, .52f, .70f, .52f, .07f); Line(.32f, .32f, .70f, .32f, .07f);
        }
        else if (Kind == "mod")
        {
            // A cube outline, used for mods and plugin packages.
            Line(.50f, .92f, .12f, .72f, .08f); Line(.12f, .72f, .12f, .28f, .08f); Line(.12f, .28f, .50f, .08f, .08f); Line(.50f, .08f, .88f, .28f, .08f); Line(.88f, .28f, .88f, .72f, .08f); Line(.88f, .72f, .50f, .92f, .08f);
            Line(.12f, .72f, .50f, .52f, .07f); Line(.50f, .52f, .88f, .72f, .07f); Line(.50f, .52f, .50f, .08f, .07f);
        }
        else if (Kind == "gearfile") { Line(.20f, .10f, .20f, .90f, .08f); Line(.20f, .90f, .82f, .90f, .08f); Line(.82f, .90f, .82f, .10f, .08f); Line(.82f, .10f, .20f, .10f, .08f); Line(.32f, .66f, .70f, .66f, .07f); Line(.32f, .50f, .70f, .50f, .07f); Line(.32f, .34f, .56f, .34f, .07f); }
        else if (Kind == "dot") { Line(.44f, .50f, .56f, .50f, .24f); }
        else if (Kind == "refresh")
        {
            // Two clockwise arcs with matching arrowheads, kept inside the same icon square.
            for (int arc = 0; arc < 2; arc++)
            {
                float start = .18f + arc * Mathf.PI, sweep = 2.25f, radius = .34f;
                for (int i = 0; i < 10; i++)
                {
                    float a = start - i * sweep / 10, b = start - (i + 1) * sweep / 10;
                    Line(.5f + radius * Mathf.Cos(a), .5f + radius * Mathf.Sin(a), .5f + radius * Mathf.Cos(b), .5f + radius * Mathf.Sin(b), .08f);
                }
                float end = start - sweep;
                Vector2 tip = new(.5f + radius * Mathf.Cos(end), .5f + radius * Mathf.Sin(end));
                Vector2 back = new(-Mathf.Sin(end), Mathf.Cos(end)), side = new(Mathf.Cos(end), Mathf.Sin(end));
                Vector2 left = tip + back * .14f + side * .09f, right = tip + back * .14f - side * .09f;
                Line(tip.x, tip.y, left.x, left.y, .08f); Line(tip.x, tip.y, right.x, right.y, .08f);
            }
        }
        else { Line(.08f, .54f, .5f, .91f); Line(.5f, .91f, .92f, .54f); Line(.21f, .59f, .21f, .12f); Line(.79f, .59f, .79f, .12f); Line(.21f, .12f, .38f, .12f); Line(.38f, .12f, .38f, .43f); Line(.38f, .43f, .62f, .43f); Line(.62f, .43f, .62f, .12f); Line(.62f, .12f, .79f, .12f); }
    }
}

internal sealed class OverviewTabGraphic : MaskableGraphic
{
    public override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear(); var r = rectTransform.rect; float slope = 12;
        vh.AddVert(new Vector3(r.xMin, r.yMin), color, Vector2.zero); vh.AddVert(new Vector3(r.xMin + slope, r.yMax), color, Vector2.zero);
        vh.AddVert(new Vector3(r.xMax, r.yMax), color, Vector2.zero); vh.AddVert(new Vector3(r.xMax - slope, r.yMin), color, Vector2.zero);
        vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);
    }
}

internal sealed class OverviewFlowingName : MonoBehaviour
{
    internal TextMeshProUGUI Text = null!;
    private string _reported = "";
    private void LateUpdate()
    {
        if (Text == null) return;
        if (Text.havePropertiesChanged) Text.ForceMeshUpdate();
        if (Text.text != _reported && Text.text.Length > 0) Report();
        if (Text.textInfo.characterCount == 0 || Text.canvasRenderer.materialCount < 1) return;
        for (int i = 0; i < Text.textInfo.characterCount; i++)
        {
            var c = Text.textInfo.characterInfo[i]; if (!c.isVisible || c.materialReferenceIndex >= Text.textInfo.meshInfo.Length) continue;
            var colors = Text.textInfo.meshInfo[c.materialReferenceIndex].colors32;
            if (colors == null || c.vertexIndex + 3 >= colors.Length) continue;
            for (int v = 0; v < 4; v++) { float p = (Mathf.Sin(Time.unscaledTime * .85f - i * .19f + v * .07f) + 1) / 2; colors[c.vertexIndex + v] = Color.Lerp(new Color(.86f, .72f, .43f), new Color(.53f, .79f, .78f), p); }
        }
        Text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }
    private void Report()
    {
        _reported = Text.text;
        int visible = 0; for (int i = 0; i < Text.textInfo.characterCount; i++) if (Text.textInfo.characterInfo[i].isVisible) visible++;
        uint[]? missing = null;
        string missingText = Text.font != null && !Text.font.HasCharacters(Text.text, out missing, true, false) && missing != null
            ? string.Concat(missing.Select(c => char.ConvertFromUtf32((int)c))) : "";
        var mask = Text.GetComponentInParent<RectMask2D>();
        var rect = Text.rectTransform.rect;
        Plugin.Log.LogDebug($"[概览名称] 「{Text.text}」字体 {Text.font?.name}，字符 {Text.textInfo.characterCount}（可见 {visible}），子网格 {Text.GetComponentsInChildren<TMP_SubMeshUI>(true).Length}，" +
            $"尺寸 {rect.width:0}×{rect.height:0}，激活 {Text.isActiveAndEnabled}，透明度 {Text.alpha:0.##}，缺字「{missingText}」，裁剪遮罩 {(mask != null ? mask.name : "无")}。");
    }
}
