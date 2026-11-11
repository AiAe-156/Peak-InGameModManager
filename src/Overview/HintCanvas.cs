#nullable enable
using UnityEngine;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;

/// <summary>Batch boundary only: inherit sorting, masks, camera and page lifetime unchanged.</summary>
internal static class HintCanvas
{
    internal static void Attach(RectTransform rect, bool interactive)
    {
        var inherited = rect.GetComponentInParent<Canvas>();
        var canvas = rect.gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = false;
        if (inherited != null)
        {
            canvas.additionalShaderChannels = inherited.additionalShaderChannels;
            canvas.pixelPerfect = inherited.pixelPerfect;
        }
        if (interactive) rect.gameObject.AddComponent<GraphicRaycaster>();
    }
    internal static void Position(RectTransform rect, float x, float y)
    {
        var wanted = new Vector2(x, -y);
        if ((rect.anchoredPosition - wanted).sqrMagnitude > .01f) rect.anchoredPosition = wanted;
    }
}
