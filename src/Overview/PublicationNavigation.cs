#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;

/// <summary>One owner per window; lifecycle changes coalesce into a rebuild after layout. No idle polling.</summary>
internal sealed class PublicationNavigation : MonoBehaviour
{
    private bool _queued, _linking, _configured, _restoreScroll;
    private Bookmark? _focus;
    private List<Selectable> _controls = new();
    private readonly Dictionary<string, Vector2> _scrollPositions = new();
    internal static void Attach(RectTransform panel)
    {
        var owner = panel.GetComponentInParent<PublicationNavigation>();
        if (owner == null) owner = panel.gameObject.AddComponent<PublicationNavigation>();
        owner.Schedule();
    }
    internal static void AttachBoundary(RectTransform panel)
    {
        var owner = panel.GetComponent<PublicationNavigation>();
        if (owner == null) owner = panel.gameObject.AddComponent<PublicationNavigation>();
        owner.Schedule();
        if (panel.parent != null) Invalidate(panel.parent);
    }
    internal static void Invalidate(Transform anywhere)
    { if (anywhere == null) return; var owner = anywhere.GetComponentInParent<PublicationNavigation>(); if (owner != null) owner.Schedule(); }
    internal static void Preserve(Transform anywhere)
    {
        if (anywhere == null) return;
        var owner = anywhere.GetComponentInParent<PublicationNavigation>();
        if (owner == null) return;
        owner.RememberSelection();
        if (owner._restoreScroll) return;
        owner._scrollPositions.Clear();
        foreach (var scroll in owner.GetComponentsInChildren<ScrollRect>(false))
            if (scroll.content != null && scroll.GetComponentInParent<PublicationNavigation>() == owner)
                owner._scrollPositions[Path(owner.transform, scroll.transform)] = scroll.content.anchoredPosition;
        owner._restoreScroll = true;
    }
    private void OnEnable() => Schedule();
    private void OnDisable() { StopAllCoroutines(); _queued = false; }
    private void OnTransformChildrenChanged() => Schedule();
    private void OnRectTransformDimensionsChange() => Schedule();
    private void Schedule()
    { if (_queued || _linking || !isActiveAndEnabled) return; _queued = true; StartCoroutine(Configure()); }
    internal void Remember(Selectable control)
    {
        if (control == null || control.GetComponentInParent<PublicationNavigation>() != this) return;
        var scroll = ContentScroll(control);
        _focus = new Bookmark { Path = Path(transform, control.transform), Area = scroll == null ? "" : Path(transform, scroll.transform),
            Caption = Caption(control), Kind = control.GetType().Name,
            Position = transform.InverseTransformPoint(control.transform.position) };
    }
    private void RememberSelection()
    {
        var system = EventSystem.current;
        if (system == null || system.currentSelectedGameObject == null) return;
        var control = system.currentSelectedGameObject.GetComponent<Selectable>();
        if (control != null) Remember(control);
    }
    private static string Caption(Selectable control)
    { var text = control.GetComponentsInChildren<TMP_Text>(false).FirstOrDefault(t => !string.IsNullOrEmpty(t.text)); return text == null ? "" : text.text; }
    private static string Path(Transform root, Transform? child)
    {
        var parts = new List<string>();
        while (child != null && child != root) {
            int ordinal = 0;
            if (child.parent != null) for (int i = 0; i < child.GetSiblingIndex(); i++)
                if (child.parent.GetChild(i).name == child.name) ordinal++;
            parts.Add(child.name + "#" + ordinal); child = child.parent;
        }
        parts.Reverse(); return string.Join("/", parts);
    }
    private ScrollRect? ContentScroll(Selectable control)
    {
        for (Transform? parent = control.transform.parent; parent != null && parent != transform; parent = parent.parent) {
            var scroll = parent.GetComponent<ScrollRect>();
            if (scroll != null && scroll.content != null && control.transform.IsChildOf(scroll.content)) return scroll;
        }
        return null;
    }
    private static bool Safe(Selectable control)
    { var style = control.GetComponent<UiActionStyle>(); return style == null || style.Role != UiActionRole.Danger; }
    private bool Managed(Selectable control)
    {
        if (control == null || control is Scrollbar || control is Editor.DocSurface
            || control.navigation.mode == Navigation.Mode.None || control.GetComponentInParent<PublicationNavigation>() != this) return false;
        var dropdown = control.GetComponentInParent<TMP_Dropdown>();
        return dropdown == null || dropdown == control; // Dropdown items keep their own vertical navigation.
    }
    private bool Eligible(Selectable control) => Managed(control) && control.IsActive() && control.IsInteractable();
    private IEnumerator Configure()
    {
        yield return null; _queued = false; _linking = true;
        try {
            var root = (RectTransform)transform; LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            var all = GetComponentsInChildren<Selectable>(false).Where(Managed).ToList();
            foreach (var control in all) {
                var observer = control.GetComponent<PublicationNavigationTarget>();
                if (observer == null) observer = control.gameObject.AddComponent<PublicationNavigationTarget>();
                observer.Owner = this; observer.Control = control;
                GamepadSupport.MarkSelectable(control.gameObject, ContentScroll(control), (RectTransform)control.transform);
            }
            var controls = all.Where(Eligible).ToList();
            if (controls.Count == 0) yield break;
            if (_restoreScroll) {
                _restoreScroll = false;
                foreach (var scroll in GetComponentsInChildren<ScrollRect>(false))
                    if (scroll.content != null && scroll.GetComponentInParent<PublicationNavigation>() == this
                        && _scrollPositions.TryGetValue(Path(transform, scroll.transform), out var position)) {
                        if (scroll.vertical && scroll.viewport != null)
                            position.y = Mathf.Clamp(position.y, 0, Mathf.Max(0, scroll.content.rect.height - scroll.viewport.rect.height));
                        scroll.content.anchoredPosition = position; scroll.StopMovement();
                    }
            }
            _controls = controls;
            Geometry(controls, out var regions, out var cells);
            var links = NavigationGrid.Build(cells, regions);
            for (int i = 0; i < controls.Count; i++) {
                var observer = controls[i].GetComponent<PublicationNavigationTarget>(); observer.CrossMask = 0;
                Selectable? At(int direction) {
                    int target = links[i][direction]; if (target < 0) return null;
                    if (cells[i].Area != cells[target].Area) { observer.CrossMask |= 1 << direction; return null; }
                    return controls[target];
                }
                controls[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnLeft = At(0), selectOnRight = At(1), selectOnUp = At(2), selectOnDown = At(3) };
            }
            var system = EventSystem.current; var selected = system == null ? null : system.currentSelectedGameObject;
            bool owned = selected != null && selected.transform.IsChildOf(transform);
            bool valid = selected != null && selected.activeInHierarchy;
            bool childScope = owned && selected!.GetComponentInParent<PublicationNavigation>() != this;
            var currentControl = selected == null ? null : selected.GetComponent<Selectable>();
            bool needFocus = !valid || owned && !childScope && currentControl != null && (!currentControl.IsActive() || !currentControl.IsInteractable());
            if (system != null && !childScope && (needFocus || !_configured && !owned)) {
                Selectable? next = null; var bookmark = _focus;
                if (bookmark != null) {
                    var candidates = controls.Where(c => c.GetType().Name == bookmark.Kind).ToList();
                    string Area(Selectable c) { var scroll = ContentScroll(c); return scroll == null ? "" : Path(transform, scroll.transform); }
                    next = candidates.FirstOrDefault(c => Path(transform, c.transform) == bookmark.Path && Caption(c) == bookmark.Caption)
                        ?? candidates.Where(c => Area(c) == bookmark.Area && bookmark.Caption.Length > 0 && Caption(c) == bookmark.Caption)
                            .OrderBy(c => Vector2.SqrMagnitude((Vector2)root.InverseTransformPoint(c.transform.position) - bookmark.Position)).FirstOrDefault()
                        ?? candidates.FirstOrDefault(c => Path(transform, c.transform) == bookmark.Path && Safe(c))
                        ?? candidates.Where(c => Area(c) == bookmark.Area && Safe(c))
                            .OrderBy(c => Vector2.SqrMagnitude((Vector2)root.InverseTransformPoint(c.transform.position) - bookmark.Position)).FirstOrDefault();
                }
                next ??= controls.FirstOrDefault(Safe);
                if (next != null) system.SetSelectedGameObject(next.gameObject);
            }
            _configured = true; RememberSelection();
        }
        finally { _linking = false; }
    }
    private void Geometry(IReadOnlyList<Selectable> controls, out Dictionary<int, NavigationGrid.Box> regions, out List<NavigationGrid.Cell> cells)
    {
        var root = (RectTransform)transform;
        regions = new(); cells = new(); var areas = new Dictionary<ScrollRect, int>(); var corners = new Vector3[4];
        foreach (var control in controls) {
            var rect = (RectTransform)control.transform; Vector2 position = root.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            var scroll = ContentScroll(control); int area = 0; Vector2 local = position;
            if (scroll != null && scroll.content != null) {
                if (!areas.TryGetValue(scroll, out area)) {
                    area = areas.Count + 1; areas.Add(scroll, area);
                    var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
                    viewport.GetWorldCorners(corners);
                    Vector2 bottom = root.InverseTransformPoint(corners[0]), top = root.InverseTransformPoint(corners[2]);
                    regions.Add(area, new NavigationGrid.Box(bottom.x, bottom.y, top.x, top.y));
                }
                Vector2 origin = root.InverseTransformPoint(scroll.content.TransformPoint(Vector3.zero)); local = position - origin;
            }
            float halfWidth = 0, halfHeight = 0;
            if (control is ControllerReadingSurface) {
                rect.GetWorldCorners(corners); Vector2 lo = root.InverseTransformPoint(corners[0]), hi = root.InverseTransformPoint(corners[2]);
                halfWidth = Mathf.Abs(hi.x - lo.x) / 2; halfHeight = Mathf.Abs(hi.y - lo.y) / 2;
            }
            cells.Add(new NavigationGrid.Cell(area, local.x, local.y, position.x, position.y, halfWidth, halfHeight));
        }
    }
    internal bool MoveAcross(Selectable source, MoveDirection direction)
    {
        int axis = direction switch { MoveDirection.Left => 0, MoveDirection.Right => 1, MoveDirection.Up => 2, MoveDirection.Down => 3, _ => -1 };
        if (axis < 0 || EventSystem.current == null || EventSystem.current.currentSelectedGameObject != source.gameObject) return false;
        var controls = _controls.Where(Eligible).ToList(); int index = controls.IndexOf(source); if (index < 0) return false;
        Geometry(controls, out var regions, out var cells);
        int target = NavigationGrid.Find(cells, regions, index, axis);
        if (target < 0) return false;
        EventSystem.current.SetSelectedGameObject(controls[target].gameObject); return true;
    }
    private sealed class Bookmark
    { internal string Path = "", Area = "", Caption = "", Kind = ""; internal Vector2 Position; }
}
internal sealed class PublicationNavigationTarget : MonoBehaviour, ISelectHandler, IMoveHandler
{
    internal PublicationNavigation? Owner;
    internal Selectable? Control;
    internal int CrossMask;
    public void OnMove(AxisEventData data)
    {
        if (data.used || Owner == null || Control == null) return;
        int direction = data.moveDir switch { MoveDirection.Left => 0, MoveDirection.Right => 1, MoveDirection.Up => 2, MoveDirection.Down => 3, _ => -1 };
        if (direction >= 0 && (CrossMask & (1 << direction)) != 0 && Owner.MoveAcross(Control, data.moveDir)) data.Use();
    }
    public void OnSelect(BaseEventData data) { if (Owner != null && Control != null) Owner.Remember(Control); }
    private void OnEnable() { if (Owner != null) PublicationNavigation.Invalidate(Owner.transform); }
    private void OnDisable() {
        if (Owner == null) return;
        if (Control != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject) Owner.Remember(Control);
        PublicationNavigation.Invalidate(Owner.transform);
    }
    private void OnCanvasGroupChanged() { if (Owner != null) PublicationNavigation.Invalidate(Owner.transform); }
}
