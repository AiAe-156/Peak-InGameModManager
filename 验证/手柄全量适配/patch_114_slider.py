# -*- coding: utf-8 -*-
"""1.1.4 sliders: slim track, round knob, longer, sane step for huge ranges (0..float.MaxValue), gamepad left/right."""
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

# --- step: use an effective range so 0..3.4e38 sliders step sensibly ---
edit('GamepadSupport.cs', [
('''        float range = __instance.maxValue - __instance.minValue;
        if (range <= 0f)
        {
            return;
        }

        __result = __instance.wholeNumbers
            ? Mathf.Max(1f, Mathf.Round(range * 0.005f))
            : Mathf.Min(__result, range * 0.005f);''',
'''        float range = EffectiveRange(__instance);
        if (range <= 0f)
        {
            return;
        }

        __result = __instance.wholeNumbers
            ? Mathf.Max(1f, Mathf.Round(range * 0.005f))
            : Mathf.Min(__result, range * 0.005f);'''),
('''            float range = _slider!.maxValue - _slider.minValue;
            if (range <= 0f)
            {
                return;
            }

            bool reversed = _slider.direction is Slider.Direction.RightToLeft or Slider.Direction.TopToBottom;
            float step = _slider.wholeNumbers
                ? Mathf.Max(1f, Mathf.Round(range * 0.005f))
                : Mathf.Min(range * 0.1f, range * 0.005f);
            _slider.value = Mathf.Clamp(_slider.value + step * dir * (reversed ? -1 : 1), _slider.minValue, _slider.maxValue);''',
'''            float range = EffectiveRange(_slider!);
            if (range <= 0f)
            {
                return;
            }

            bool reversed = _slider.direction is Slider.Direction.RightToLeft or Slider.Direction.TopToBottom;
            float step = _slider.wholeNumbers
                ? Mathf.Max(1f, Mathf.Round(range * 0.005f))
                : StepFor(range);
            float max = _slider.minValue + range; // never jump into the absurd part of a 0..float.MaxValue slider
            float next = Mathf.Clamp(_slider.value + step * dir * (reversed ? -1 : 1), _slider.minValue, Mathf.Max(max, _slider.value));
            _slider.value = next;'''),
('''    /// <summary>步进上限：最多区间的 0.5%（原版 0.1，0-10000 的滑条一步 1000）。整数档最少 1 步。</summary>''',
 '''    /// <summary>1.1.4：有的模组把上限设成 float.MaxValue（3.4e38，用户截图「自动保存间隔」），按 0.5% 一步就直接到顶、
    /// 滑块位置也算不出来。有效区间取「默认值 / 当前值的 4 倍、至少 100」与真实上限中较小的一个。</summary>
    internal static float EffectiveRange(Slider slider)
    {
        float range = slider.maxValue - slider.minValue;
        if (range <= 0f || range < 1e6f) return range;
        float anchor = Mathf.Max(Mathf.Abs(slider.value - slider.minValue), 25f);
        return Mathf.Min(range, Mathf.Max(100f, anchor * 4f));
    }
    /// <summary>A round step (0.01 / 0.05 / 0.1 / 0.5 / 1 / 5 …) close to 0.5% of the range.</summary>
    internal static float StepFor(float range)
    {
        float raw = range * 0.005f;
        float power = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(raw, 1e-6f))));
        float scaled = raw / power;
        return (scaled < 2f ? 1f : scaled < 5f ? 2f : 5f) * power;
    }
    /// <summary>步进上限：最多区间的 0.5%（原版 0.1，0-10000 的滑条一步 1000）。整数档最少 1 步。</summary>'''),
])

# --- look: slim track, round knob, longer slider; applied in the compact cell pass ---
edit('ModConfigTreeLayoutPatch.cs', [
('''            foreach (TMP_Dropdown dropdown in controls.GetComponentsInChildren<TMP_Dropdown>(true))
            {''', '''            foreach (Slider slider in controls.GetComponentsInChildren<Slider>(true)) SliderLook.Apply(slider);
            foreach (TMP_Dropdown dropdown in controls.GetComponentsInChildren<TMP_Dropdown>(true))
            {'''),
])
p = os.path.join(SRC, 'SliderLook.cs')
io.open(p, 'w', encoding='utf-8', newline='\n').write('''#nullable enable
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
    private const float TrackHeight = 6f, KnobSize = 20f;

    internal static void Apply(Slider slider)
    {
        if (slider == null || slider.GetComponent<Marker>() != null) return;
        slider.gameObject.AddComponent<Marker>();
        var rect = (RectTransform)slider.transform;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, KnobSize + 4f);

        // Track: the slider's own background image (first Image that is neither fill nor handle).
        Image? track = null;
        foreach (var image in slider.GetComponentsInChildren<Image>(true))
        {
            if (slider.fillRect != null && image.transform.IsChildOf(slider.fillRect)) continue;
            if (slider.handleRect != null && image.transform.IsChildOf(slider.handleRect)) continue;
            track = image; break;
        }
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
''')
print('ok')
