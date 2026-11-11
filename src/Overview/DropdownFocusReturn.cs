#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
namespace ModConfigEnhance.Overview;
internal sealed class DropdownFocusReturn : MonoBehaviour
{
    internal RectTransform? Anchor;
    private void OnDisable()
    {
        var system = EventSystem.current;
        if (Anchor == null || !Anchor.gameObject.activeInHierarchy || system == null) return;
        var selected = system.currentSelectedGameObject;
        if (selected == null || selected.transform.IsChildOf(transform)) system.SetSelectedGameObject(Anchor.gameObject);
    }
}
