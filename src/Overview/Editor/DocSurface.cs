#nullable enable
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview.Editor;

/// <summary>
/// The page's input surface. Keyboard and IME text are read exactly like TMP_InputField does
/// (<c>Event.PopEvent</c> while selected, composition from the input module), so Chinese input
/// behaves the same as in the game's own text fields. Pointer gestures place the caret,
/// select words/paragraphs and extend selections across paragraphs.
/// </summary>
internal sealed class DocSurface : Selectable, IUpdateSelectedHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IScrollHandler
{
    /// <summary>True while the page is editable; the typing guard then keeps other mods' hotkeys quiet.</summary>
    internal bool TypingEnabled;
    internal bool HoldEditingFocus;
    internal Action<PointerEventData>? Scrolled;
    internal Action<Event>? Key;
    internal Action<PointerEventData, int>? Press;
    internal Action<PointerEventData>? DragTo, DragEnd, Click;
    internal Action? Focused, Tick;
    internal bool Dragging { get; private set; }
    internal PointerEventData? LastPointer { get; private set; }
    private readonly Event _event = new();
    private int _clicks;
    private float _lastPress = -10;
    private Vector2 _lastPressPosition;
    private bool _ownsEditingFocus;
    private int _keyFrame = -1;

    internal BaseInput? InputModuleInput => EventSystem.current != null && EventSystem.current.currentInputModule != null ? EventSystem.current.currentInputModule.input : null;
    internal string CompositionString { get { var input = InputModuleInput; return input != null ? input.compositionString : Input.compositionString; } }
    internal bool IsSelected => EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;

    public void Focus()
    {
        if (EventSystem.current == null || !isActiveAndEnabled) return;
        _ownsEditingFocus = TypingEnabled;
        if (EventSystem.current.currentSelectedGameObject != gameObject) EventSystem.current.SetSelectedGameObject(gameObject);
    }

    public override void OnSelect(BaseEventData eventData)
    {
        base.OnSelect(eventData);
        _ownsEditingFocus = TypingEnabled;
        ApplyImeMode();
        Focused?.Invoke();
    }
    internal void ApplyImeMode()
    {
        if (!IsSelected) return;
        var mode = TypingEnabled ? IMECompositionMode.On : IMECompositionMode.Auto;
        var input = InputModuleInput; if (input != null) input.imeCompositionMode = mode; else Input.imeCompositionMode = mode;
    }
    public override void OnDeselect(BaseEventData eventData)
    {
        base.OnDeselect(eventData);
        // Explicit clicks and modal inputs may take focus. A navigation refresh or
        // background deselection must not cancel an ongoing guide edit.
        bool clickedElsewhere = Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame);
        if (!HoldEditingFocus || !TypingEnabled || clickedElsewhere || OverviewUi.HasModal) _ownsEditingFocus = false;
        var input = InputModuleInput; if (input != null) input.imeCompositionMode = IMECompositionMode.Auto; else Input.imeCompositionMode = IMECompositionMode.Auto;
    }

    internal void SetCompositionCursor(Vector2 screenPoint)
    {
        var position = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        var input = InputModuleInput; if (input != null) input.compositionCursorPos = position; else Input.compositionCursorPos = position;
    }

    public void OnUpdateSelected(BaseEventData eventData)
    {
        if (_keyFrame == Time.frameCount) return;
        _keyFrame = Time.frameCount;
        bool consumed = false;
        while (Event.PopEvent(_event))
        {
            if (_event.rawType == EventType.KeyDown) { consumed = true; Key?.Invoke(_event); }
            else if ((_event.rawType == EventType.ValidateCommand || _event.rawType == EventType.ExecuteCommand) && _event.commandName == "SelectAll") consumed = true;
        }
        if (consumed || CompositionString.Length > 0) eventData.Use();
    }

    private void Update() => Tick?.Invoke();

    private void LateUpdate()
    {
        if (!HoldEditingFocus || !TypingEnabled || !_ownsEditingFocus || OverviewUi.HasModal) return;
        var system = EventSystem.current;
        if (system == null) return;
        var selected = system.currentSelectedGameObject;
        if (GamepadSupport.IsGamepad && selected != gameObject)
        { _ownsEditingFocus = false; return; }
        // Source mode and other real text fields own their own input lifecycle.
        if (selected != null && selected != gameObject && selected.GetComponent<TMPro.TMP_InputField>() != null)
        { _ownsEditingFocus = false; return; }
        if (!IsSelected) { Focus(); Tick?.Invoke(); }
        // Some menu input modules omit update-selected events for custom surfaces.
        // Use the same queue once per frame, after restoring the editor's focus.
        if (IsSelected && _keyFrame != Time.frameCount) OnUpdateSelected(new BaseEventData(system));
    }

    public override void OnDisable()
    {
        _ownsEditingFocus = false;
        base.OnDisable();
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        Focus();
        float now = Time.unscaledTime;
        _clicks = now - _lastPress < .4f && (eventData.position - _lastPressPosition).sqrMagnitude < 36 ? Math.Min(3, _clicks + 1) : 1;
        _lastPress = now; _lastPressPosition = eventData.position; LastPointer = eventData;
        Press?.Invoke(eventData, _clicks);
    }
    public void OnBeginDrag(PointerEventData eventData) { if (eventData.button != PointerEventData.InputButton.Left) return; Dragging = true; eventData.eligibleForClick = false; LastPointer = eventData; }
    public void OnDrag(PointerEventData eventData) { if (!Dragging) return; LastPointer = eventData; DragTo?.Invoke(eventData); }
    public void OnEndDrag(PointerEventData eventData) { if (!Dragging) return; Dragging = false; DragEnd?.Invoke(eventData); }
    public void OnPointerClick(PointerEventData eventData) { if (eventData.button == PointerEventData.InputButton.Left) { Focus(); Click?.Invoke(eventData); } }
    // The page scroll view is a child of this surface, so wheel events over it are forwarded down.
    public void OnScroll(PointerEventData eventData) => Scrolled?.Invoke(eventData);

    public override void OnMove(AxisEventData eventData) { }
    public override void OnPointerEnter(PointerEventData eventData) { }
    public override void OnPointerExit(PointerEventData eventData) { }
}
