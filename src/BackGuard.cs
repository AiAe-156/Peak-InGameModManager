#nullable enable
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ModConfigEnhance;

/// <summary>
/// 1.1.5（用户 2026-11-13）：本模组的页面与大窗口（概览、帮助、模组设置页、商店、日志、管理中心、更新队列）
/// 用手柄离开时要快速连按两次 B（0.5 秒内），防止误触；第一次按下在屏幕底部提示「快速连按两次「B」退出页面」。
/// 只在「真的要离开」的地方调用 <see cref="Allow"/>：关弹层、退出输入框这类逐层返回仍是按一次。
/// 键鼠、点击按钮不受影响（不是手柄的返回键按下就直接放行）。
/// </summary>
internal static class BackGuard
{
    private const float Window = .5f, HintSeconds = 1.6f;
    private static float _firstAt = -10f;
    private static int _frame = -1;
    private static bool _result;
    private static GameObject? _hint;
    private static CanvasGroup? _group;
    private static TextMeshProUGUI? _text;
    private static float _shownAt = -10f;

    /// <summary>True: leave now. False: this was the first press; the hint is shown and the caller stays.</summary>
    internal static bool Allow()
    {
        if (GamepadSupport.BackSwallowed || Overview.ControllerReadingSurface.ConsumeBack()) return false;
        if (_frame == Time.frameCount) return _result; // several back paths can ask for the same press
        _frame = Time.frameCount;
        var pad = Gamepad.current;
        bool padBack = GamepadSupport.IsGamepad && pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame);
        if (!padBack) return _result = true;
        if (Time.unscaledTime - _firstAt <= Window) { _firstAt = -10f; HideNow(); return _result = true; }
        _firstAt = Time.unscaledTime; Show(); return _result = false;
    }

    internal static void Reset() { _firstAt = -10f; _frame = -1; HideNow(); }

    private static void Show()
    {
        try
        {
            if (_hint == null) Build();
            if (_text != null)
            {
                string glyph = GamepadIntent.Glyph("buttonEast", out var sprites);
                if (sprites != null) _text.spriteAsset = sprites; else glyph = "B";
                _text.text = Loc.F("Press {0} twice quickly to leave this page", "快速连按两次「{0}」退出页面", "快速連按兩次「{0}」退出頁面", glyph);
            }
            _shownAt = Time.unscaledTime; _hint!.SetActive(true);
            if (_group != null) _group.alpha = 1f;
        }
        catch (System.Exception error) { Plugin.Log.LogWarning("[返回保护] 提示显示失败：" + error.Message); }
    }
    private static void HideNow() { if (_hint != null) _hint.SetActive(false); }

    private static void Build()
    {
        _hint = new GameObject("IGMM_BackGuardHint", typeof(RectTransform));
        Object.DontDestroyOnLoad(_hint);
        var canvas = _hint.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
        var scaler = _hint.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 1f;
        _group = _hint.AddComponent<CanvasGroup>(); _group.interactable = false; _group.blocksRaycasts = false;
        _hint.AddComponent<Fader>();
        var plate = new GameObject("Plate", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        plate.SetParent(_hint.transform, false); plate.anchorMin = plate.anchorMax = new Vector2(.5f, 0f); plate.pivot = new Vector2(.5f, 0f);
        plate.anchoredPosition = new Vector2(0f, 64f); plate.sizeDelta = new Vector2(420f, 46f);
        var image = plate.GetComponent<Image>(); image.sprite = ModConfigSidebarWidgets.RoundedSprite; image.type = Image.Type.Sliced; image.color = new Color(.06f, .07f, .08f, .9f); image.raycastTarget = false;
        _text = Overview.OverviewUi.Text(plate, "", 20); _text.richText = true; _text.alignment = TextAlignmentOptions.Center; _text.raycastTarget = false;
        var tr = _text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(12f, 0f); tr.offsetMax = new Vector2(-12f, 0f);
        _hint.SetActive(false);
    }

    private sealed class Fader : MonoBehaviour
    {
        private void Update()
        {
            float age = Time.unscaledTime - _shownAt;
            if (age >= HintSeconds) { gameObject.SetActive(false); return; }
            if (_group != null) _group.alpha = age < HintSeconds - .4f ? 1f : Mathf.Clamp01((HintSeconds - age) / .4f);
        }
    }
}
