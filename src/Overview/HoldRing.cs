#nullable enable
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.1.4（用户）：录键锁定时底部居中的「长按 B 退出」提示：B 图标 + 顺时针圆环。按住时圆环按时间增长，
/// 松开后平滑回落；满 1 秒由调用方退出锁定。progress &lt; 0 表示没按 B（圆环回落到 0）。
/// </summary>
internal sealed class HoldRing : MonoBehaviour
{
    private const float Size = 34f, Rise = 1f, Fall = 3f;
    private Image? _fill;
    private float _shown, _target;
    internal static void Show(RectTransform host, float progress)
    {
        if (host == null) return;
        var existing = host.Find("MCE_HoldRing");
        HoldRing? found = existing != null ? existing.GetComponent<HoldRing>() : null;
        var ring = found != null ? found : Create(host);
        if (!ring.gameObject.activeSelf) ring._shown = 0f; // a new hold starts empty
        ring.gameObject.SetActive(true); ring.transform.SetAsLastSibling();
        ring._target = Mathf.Max(0f, progress);
        if (progress >= 0f && progress > ring._shown) ring._shown = progress; // the hold itself is exact
    }
    internal static void Hide(RectTransform host)
    {
        if (host == null) return;
        var ring = host.Find("MCE_HoldRing"); if (ring != null) ring.gameObject.SetActive(false);
    }
    private static HoldRing Create(RectTransform host)
    {
        var root = new GameObject("MCE_HoldRing", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(host, false); root.anchorMin = root.anchorMax = new Vector2(.5f, 0f); root.pivot = new Vector2(.5f, 0f);
        root.sizeDelta = new Vector2(240f, Size + 8f); root.anchoredPosition = new Vector2(0f, 10f);
        var plate = root.gameObject.AddComponent<Image>(); plate.sprite = ModConfigSidebarWidgets.RoundedSprite; plate.type = Image.Type.Sliced;
        plate.color = new Color(.06f, .07f, .08f, .88f); plate.raycastTarget = false;
        Image Disc(string name, Color color, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); var r = (RectTransform)go.transform; r.SetParent(root, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, .5f); r.pivot = new Vector2(0f, .5f); r.sizeDelta = new Vector2(size, size); r.anchoredPosition = new Vector2(8f + (Size - size) / 2f, 0f);
            var image = go.GetComponent<Image>(); image.sprite = RingSprite(); image.color = color; image.raycastTarget = false; return image;
        }
        Disc("Track", new Color(1f, 1f, 1f, .18f), Size);
        var ring = root.gameObject.AddComponent<HoldRing>();
        ring._fill = Disc("Fill", new Color(.98f, .62f, .25f, 1f), Size);
        ring._fill.type = Image.Type.Filled; ring._fill.fillMethod = Image.FillMethod.Radial360; ring._fill.fillOrigin = (int)Image.Origin360.Top; ring._fill.fillClockwise = true; ring._fill.fillAmount = 0f;
        var glyph = new GameObject("B", typeof(RectTransform)).AddComponent<TextMeshProUGUI>(); var g = glyph.rectTransform; g.SetParent(root, false);
        g.anchorMin = g.anchorMax = new Vector2(0f, .5f); g.pivot = new Vector2(.5f, .5f); g.sizeDelta = new Vector2(Size, Size); g.anchoredPosition = new Vector2(8f + Size / 2f, 0f);
        string text = GamepadIntent.Glyph("buttonEast", out var sprites); if (sprites != null) glyph.spriteAsset = sprites; else text = "B";
        glyph.text = text; glyph.richText = true; glyph.fontSize = 20f; glyph.alignment = TextAlignmentOptions.Center; glyph.raycastTarget = false;
        var label = OverviewUi.Text(root, Loc.T("Hold to exit recording", "长按退出录制", "長按退出錄製"), 16); label.raycastTarget = false;
        var lr = label.rectTransform; lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(1f, 1f); lr.offsetMin = new Vector2(Size + 16f, 0f); lr.offsetMax = new Vector2(-10f, 0f);
        label.alignment = TextAlignmentOptions.MidlineLeft; label.color = OverviewUi.Ivory;
        return ring;
    }
    private void Update()
    {
        // Rise follows the hold exactly (set in Show); release drains smoothly.
        if (_target < _shown) _shown = Mathf.Max(_target, _shown - Fall * Time.unscaledDeltaTime);
        else _shown = Mathf.MoveTowards(_shown, _target, Rise * 4f * Time.unscaledDeltaTime);
        if (_fill != null) _fill.fillAmount = _shown;
    }
    private static Sprite? _ring;
    private static Sprite RingSprite()
    {
        if (_ring != null) return _ring;
        const int size = 96; var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "MCE_HoldRing" };
        var pixels = new Color32[size * size]; float c = (size - 1) / 2f, outer = size / 2f - 1f, inner = outer - 9f;
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
            float a = Mathf.Clamp01(outer - d + .5f) * Mathf.Clamp01(d - inner + .5f);
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        texture.SetPixels32(pixels); texture.Apply(false, true);
        _ring = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f); _ring.name = "MCE_HoldRing";
        return _ring;
    }
}
