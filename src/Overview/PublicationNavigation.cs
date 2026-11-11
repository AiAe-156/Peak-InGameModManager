#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;
internal sealed class PublicationNavigation : MonoBehaviour
{
    private bool _queued;
    internal static void Attach(RectTransform panel)
    { var navigation = panel.GetComponent<PublicationNavigation>() ?? panel.gameObject.AddComponent<PublicationNavigation>(); navigation.Schedule(); }
    private void OnEnable() => Schedule();
    private void OnDisable() { StopAllCoroutines(); _queued = false; }
    private void Schedule() { if (_queued || !isActiveAndEnabled) return; _queued = true; StartCoroutine(Configure()); }
    private IEnumerator Configure()
    {
        yield return null; _queued = false;
        var controls = GetComponentsInChildren<Selectable>(false).Where(s => s is not Editor.DocSurface && s.IsActive() && s.IsInteractable()).ToList();
        if (controls.Count == 0) yield break;
        int Section(Selectable control)
        {
            if (control.GetComponentInParent<ScrollRect>() is ScrollRect scroll && scroll.transform.IsChildOf(transform)) return 1;
            Transform root = control.transform; while (root.parent != null && root.parent != transform) root = root.parent;
            return root is RectTransform rect && rect.anchorMin.y == 0 && rect.anchorMax.y == 0 ? 2 : 0;
        }
        Vector2 Position(Selectable control)
        { var scroll = control.GetComponentInParent<ScrollRect>(); return scroll != null && scroll.content != null ? (Vector2)scroll.content.InverseTransformPoint(control.transform.position) : (Vector2)transform.InverseTransformPoint(control.transform.position); }
        controls = controls.OrderBy(Section).ThenByDescending(c => Position(c).y).ThenBy(c => Position(c).x).ToList();
        for (int i = 0; i < controls.Count; i++)
        {
            var control = controls[i]; int section = Section(control); Vector2 position = Position(control);
            Selectable? Vertical(bool up) => controls.Where(c => c != control && Section(c) == section && (up ? Position(c).y > position.y + 2 : Position(c).y < position.y - 2))
                .OrderBy(c => Mathf.Abs(Position(c).y - position.y) + Mathf.Abs(Position(c).x - position.x) * .25f).FirstOrDefault();
            var previous = controls[(i + controls.Count - 1) % controls.Count]; var next = controls[(i + 1) % controls.Count];
            control.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = previous, selectOnRight = next, selectOnUp = Vertical(true) ?? previous, selectOnDown = Vertical(false) ?? next };
            var host = control.GetComponentInParent<ScrollRect>(); GamepadSupport.MarkSelectable(control.gameObject, host != null && host.transform.IsChildOf(transform) ? host : null, (RectTransform)control.transform);
        }
        var system = EventSystem.current;
        if (system != null && (system.currentSelectedGameObject == null || !system.currentSelectedGameObject.transform.IsChildOf(transform))) system.SetSelectedGameObject(controls[0].gameObject);
    }
}
