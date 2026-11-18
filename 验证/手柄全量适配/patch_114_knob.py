# -*- coding: utf-8 -*-
"""1.1.4 (user choice "small dot + thin ring"): the knob is a 14 px cream dot with a dark rim; the game's slider
transition (scale-up / yellow tint on select) is switched off. With the controller on the slider a thin ring appears
around the dot (cream), orange while adjusting (SliderGate engaged)."""
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

edit('SliderLook.cs', [
('''    private const float TrackHeight = 6f, KnobSize = 20f;''', '''    private const float TrackHeight = 6f, KnobSize = 14f, RingSize = 24f;'''),
('''        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, KnobSize + 4f);''', '''        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, RingSize + 2f);
        // 1.1.4 (user): no scale-up / yellow tint when selected; the ring below shows focus instead.
        slider.transition = Selectable.Transition.None;
        var animator = slider.GetComponent<Animator>(); if (animator != null) animator.enabled = false;
        if (slider.handleRect != null) slider.handleRect.localScale = Vector3.one;'''),
('''            if (knob != null) { knob.sprite = Circle(); knob.type = Image.Type.Simple; knob.preserveAspect = true; knob.color = new Color(0.96f, 0.93f, 0.84f, 1f); }
        }''', '''            if (knob != null) { knob.sprite = Circle(); knob.type = Image.Type.Simple; knob.preserveAspect = true; knob.color = new Color(0.96f, 0.93f, 0.84f, 1f); }
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
        }'''),
('''    private static Sprite? _circle;''', '''    private static Sprite? _ring;
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
    private static Sprite? _circle;'''),
])
print('ok')
