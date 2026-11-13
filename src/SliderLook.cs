#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance;

/// <summary>
/// 1.1.4 设置页滑条外观（用户 2026-11-12：「又粗还是椭圆跟个橄榄球」「太短」）：
/// 细轨道（6 高、圆角）、已填充段用主题强调色、圆形滑块（20×20）；滑条占满数字框到按钮区之间的宽度。
/// 只改外观与尺寸，不改 Slider 的值逻辑；滑块仍可鼠标拖动。
/// </summary>
internal static class SliderLook
{
    private const float TrackHeight = 6f, KnobSize = 14f, RingSize = 24f;

    internal static void Apply(Slider slider)
    {
        if (slider == null || slider.GetComponent<Marker>() != null) return;
        slider.gameObject.AddComponent<Marker>();
        var rect = (RectTransform)slider.transform;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, RingSize + 2f);
        // 1.1.4 (user): no scale-up / yellow tint when selected; the ring below shows focus instead.
        slider.transition = Selectable.Transition.None;
        var animator = slider.GetComponent<Animator>(); if (animator != null) animator.enabled = false;
        if (slider.handleRect != null) slider.handleRect.localScale = Vector3.one;

        // Track: the slider's own background image (first Image that is neither fill nor handle).
        // 1.1.4 (user: "is the slider drawn twice? one long, one short, different colours"): the prefab has more than one
        // background image (its own plus a "Background" child). Keep one as the track; the others become invisible
        // (still raycast targets, so dragging anywhere on the slider works).
        Image? track = null; var extras = new List<Image>();
        foreach (var image in slider.GetComponentsInChildren<Image>(true))
        {
            if (slider.fillRect != null && image.transform.IsChildOf(slider.fillRect)) continue;
            if (slider.handleRect != null && image.transform.IsChildOf(slider.handleRect)) continue;
            if (slider.fillRect != null && slider.fillRect.parent != null && image.transform == slider.fillRect.parent) continue;
            if (slider.handleRect != null && slider.handleRect.parent != null && image.transform == slider.handleRect.parent) continue;
            if (track == null || (image.gameObject.name.IndexOf("Background", System.StringComparison.OrdinalIgnoreCase) >= 0 && track.gameObject.name.IndexOf("Background", System.StringComparison.OrdinalIgnoreCase) < 0)) { if (track != null) extras.Add(track); track = image; }
            else extras.Add(image);
        }
        foreach (var extra in extras) { var c = extra.color; c.a = 0f; extra.color = c; }
        var rounded = ModConfigSidebarWidgets.RoundedSprite;
        if (track != null)
        {
            track.sprite = rounded; track.type = Image.Type.Sliced; track.pixelsPerUnitMultiplier = 3f;
            track.color = new Color(0.62f, 0.60f, 0.55f, 0.55f);
            var t = track.rectTransform; t.anchorMin = new Vector2(0, .5f); t.anchorMax = new Vector2(1, .5f);
            t.sizeDelta = new Vector2(t.sizeDelta.x, TrackHeight); t.anchoredPosition = new Vector2(t.anchoredPosition.x, 0);
        }
        if (slider.fillRect != null)
        {
            var fillArea = slider.fillRect.parent as RectTransform;
            if (fillArea != null) { fillArea.anchorMin = new Vector2(0, .5f); fillArea.anchorMax = new Vector2(1, .5f); fillArea.sizeDelta = new Vector2(fillArea.sizeDelta.x, TrackHeight); fillArea.anchoredPosition = new Vector2(fillArea.anchoredPosition.x, 0); }
            var fill = slider.fillRect.GetComponent<Image>();
            if (fill != null) { fill.sprite = rounded; fill.type = Image.Type.Sliced; fill.pixelsPerUnitMultiplier = 3f; fill.color = ModConfigSidebarWidgets.Cream; }
        }
        if (slider.handleRect != null)
        {
            var handleArea = slider.handleRect.parent as RectTransform;
            if (handleArea != null) { handleArea.anchorMin = new Vector2(0, .5f); handleArea.anchorMax = new Vector2(1, .5f); handleArea.sizeDelta = new Vector2(handleArea.sizeDelta.x, KnobSize); handleArea.anchoredPosition = new Vector2(handleArea.anchoredPosition.x, 0); }
            slider.handleRect.sizeDelta = new Vector2(KnobSize, 0);
            var knob = slider.handleRect.GetComponent<Image>();
            if (knob != null) { knob.sprite = Circle(); knob.type = Image.Type.Simple; knob.preserveAspect = true; knob.color = new Color(0.96f, 0.93f, 0.84f, 1f); }
            // Thin focus ring around the dot (hidden until the controller is on this slider).
            var ringRect = slider.handleRect.Find("MCE_KnobRing") as RectTransform;
            if (ringRect == null)
            {
                var ringObject = new GameObject("MCE_KnobRing", typeof(RectTransform), typeof(Image)); ringRect = (RectTransform)ringObject.transform;
                ringRect.SetParent(slider.handleRect, false); ringRect.anchorMin = ringRect.anchorMax = ringRect.pivot = new Vector2(.5f, .5f);
                ringRect.sizeDelta = new Vector2(RingSize, RingSize); ringRect.anchoredPosition = Vector2.zero;
                var ring = ringObject.GetComponent<Image>(); ring.sprite = Ring(); ring.raycastTarget = false; ring.preserveAspect = true;
                ringObject.SetActive(false);
            }
            slider.gameObject.AddComponent<KnobRing>().Ring = ringRect.gameObject;
        }
        // 1.1.4 (user: "one long, one short"): the prefab insets the fill area and the handle area differently from the track,
        // so the cream fill and the grey track did not line up. Track, fill and knob path now share one span: the fill and
        // the knob travel the track inset by the knob radius, so the knob centre sits on the fill end at every value.
        if (track != null && track.rectTransform.parent == rect)
        {
            var t = track.rectTransform; float r = KnobSize / 2f;
            void Span(RectTransform? area)
            {
                if (area == null || area.parent != rect) return;
                area.anchorMin = new Vector2(0, area.anchorMin.y); area.anchorMax = new Vector2(1, area.anchorMax.y);
                area.offsetMin = new Vector2(t.offsetMin.x + r, area.offsetMin.y); area.offsetMax = new Vector2(t.offsetMax.x - r, area.offsetMax.y);
            }
            Span(slider.fillRect != null ? slider.fillRect.parent as RectTransform : null);
            Span(slider.handleRect != null ? slider.handleRect.parent as RectTransform : null);
            // The fill still starts at the track's left end (Slider only drives the fill's anchors, not its offsets).
            if (slider.fillRect != null) { slider.fillRect.offsetMin = new Vector2(-r, slider.fillRect.offsetMin.y); slider.fillRect.offsetMax = new Vector2(0, slider.fillRect.offsetMax.y); }
        }
    }

    /// <summary>Make <paramref name="slider"/> start <paramref name="gap"/> after the right edge of <paramref name="before"/>
    /// (same parent), keeping its right edge.</summary>
    internal static void StretchAfter(RectTransform slider, RectTransform before, float gap)
    {
        if (slider.parent is not RectTransform host || host.rect.width < 1) return;
        var corners = new Vector3[4]; before.GetWorldCorners(corners);
        float beforeRight = host.InverseTransformPoint(corners[2]).x;           // host-local x
        float anchorLeft = host.rect.xMin + host.rect.width * slider.anchorMin.x; // where offsetMin.x is measured from
        slider.offsetMin = new Vector2(beforeRight + gap - anchorLeft, slider.offsetMin.y);
    }

    private static Sprite? _ring;
    /// <summary>A 2 px ring sprite for the focus halo.</summary>
    private static Sprite Ring()
    {
        if (_ring != null) return _ring;
        const int size = 64; var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "MCE_SliderRing" };
        var pixels = new Color32[size * size]; float c = (size - 1) / 2f, outer = size / 2f - 1f, inner = outer - 5f;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float a = Mathf.Clamp01(outer - d + .5f) * Mathf.Clamp01(d - inner + .5f);
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        texture.SetPixels32(pixels); texture.Apply(false, true);
        _ring = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f); _ring.name = "MCE_SliderRing";
        return _ring;
    }
    /// <summary>Shows the ring while the controller is on the slider: cream when selected, orange while adjusting.</summary>
    private sealed class KnobRing : MonoBehaviour
    {
        internal GameObject? Ring; private Image? _image; private Slider? _slider;
        private static readonly Color Selected = new(0.96f, 0.93f, 0.84f, 0.9f), Adjusting = new(0.98f, 0.62f, 0.25f, 1f);
        private void Awake() { _slider = GetComponent<Slider>(); }
        private void Update()
        {
            if (Ring == null || _slider == null) return;
            var system = UnityEngine.EventSystems.EventSystem.current;
            bool focused = GamepadSupport.IsGamepad && system != null && system.currentSelectedGameObject == gameObject;
            if (Ring.activeSelf != focused) Ring.SetActive(focused);
            if (!focused) return;
            if (_image == null) _image = Ring.GetComponent<Image>();
            var gate = GetComponent<GamepadSupport.SliderGate>();
            _image.color = gate != null && gate.Engaged ? Adjusting : Selected;
        }
    }
    private static Sprite? _circle;
    private static Sprite Circle()
    {
        if (_circle != null) return _circle;
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "MCE_SliderKnob" };
        var pixels = new Color32[size * size];
        float r = size / 2f - 1f, c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = Mathf.Clamp01(r - d + .5f);
                // a thin darker rim so the knob reads on the cream fill
                byte shade = (byte)(d > r - 3f ? 150 : 255);
                pixels[y * size + x] = new Color32(shade, shade, shade, (byte)(a * 255));
            }
        texture.SetPixels32(pixels); texture.Apply(false, true);
        _circle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
        _circle.name = "MCE_SliderKnob";
        return _circle;
    }

    private sealed class Marker : MonoBehaviour { }
}
