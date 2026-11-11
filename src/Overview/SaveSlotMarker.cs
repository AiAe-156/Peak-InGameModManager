#nullable enable
namespace ModConfigEnhance.Overview;

/// <summary>2.3.11: when MCE adds its Save button beside ModConfig's Default button, the cell's own controls (slider,
/// text box, dropdown) are shifted left by the Save button's width so nothing overlaps.</summary>
internal sealed class SaveSlotMarker : UnityEngine.MonoBehaviour
{
    internal const float Width = 76f;
    internal static void Mark(UnityEngine.RectTransform save)
    {
        var host = save.parent as UnityEngine.RectTransform; if (host == null || host.GetComponent<SaveSlotMarker>() != null) return;
        host.gameObject.AddComponent<SaveSlotMarker>();
        foreach (var control in host.GetComponentsInChildren<UnityEngine.UI.Selectable>(true))
        {
            if (control.transform.parent != host || control.transform == save || control.GetComponent<PEAKLib.UI.Elements.PeakMenuButton>() != null) continue;
            if (control.transform is not UnityEngine.RectTransform rect) continue;
            // Only controls stretched across the cell (slider, dropdown, long text box) end at the button column; the
            // small fixed-width number box beside the slider keeps its size.
            if (rect.anchorMax.x - rect.anchorMin.x < .5f) continue;
            rect.offsetMax = new UnityEngine.Vector2(rect.offsetMax.x - Width, rect.offsetMax.y);
        }
    }
}

