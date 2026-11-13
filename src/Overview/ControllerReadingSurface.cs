#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>Focus navigates normally; A engages reading, B disengages without leaving the page.</summary>
internal class ControllerReadingSurface : Selectable, ISubmitHandler, ICancelHandler
{
    internal ScrollRect Scroll = null!;
    internal Func<float, bool>? TryMovePage;
    internal Action? Links;
    private static readonly List<ControllerReadingSurface> Surfaces = new();
    private static ControllerReadingSurface? _active;
    internal float StickSpeed = 900;
    private bool _browsing;
    private int _scrollFrame = -1;
    internal bool IsBrowsing => _browsing;
    public override void OnEnable() { base.OnEnable(); if (!Surfaces.Contains(this)) Surfaces.Add(this); }
    public override void OnDisable() { Engage(false); Surfaces.Remove(this); base.OnDisable(); }
    public override void OnDeselect(BaseEventData data) { Engage(false); base.OnDeselect(data); }
    public void OnSubmit(BaseEventData data)
    {
        if (!IsActive() || !IsInteractable()) return;
        if (GamepadSupport.IsGamepad && !_browsing) Engage(true);
        else Links?.Invoke();
        data.Use();
    }
    public void OnCancel(BaseEventData data) { if (ConsumeBack()) data.Use(); }
    private void Engage(bool value)
    {
        if (value && _active != null && _active != this) _active.Engage(false);
        _browsing = value;
        if (value) { _active = this; BackGuard.Reset(); } else if (_active == this) _active = null;
        var ring = GetComponent<GamepadSupport.FocusHighlight>(); if (ring != null) ring.SetEngaged(value);
        if (!value) GamepadIntent.Release(this);
    }
    internal static bool ConsumeBack()
    {
        if (_active == null || !_active._browsing || !GamepadSupport.IsGamepad) return false;
        _active.Engage(false);
        GamepadSupport.SwallowBackUntil = Time.frameCount + 1;
        return true;
    }
    public override void OnMove(AxisEventData data)
    {
        if (!_browsing || !GamepadSupport.IsGamepad) { base.OnMove(data); return; }
        if (data.moveDir is MoveDirection.Up or MoveDirection.Down) {
            var pad = Gamepad.current;
            bool dpad = pad != null && (data.moveDir == MoveDirection.Up ? pad.dpad.up.isPressed : pad.dpad.down.isPressed);
            MovePage(ReadingInput.Step(data.moveDir == MoveDirection.Up, dpad));
        }
        else if (data.moveDir is MoveDirection.Left or MoveDirection.Right && Scroll != null && Scroll.horizontal) {
            var pad = Gamepad.current; bool left = data.moveDir == MoveDirection.Left;
            bool dpad = pad != null && (left ? pad.dpad.left.isPressed : pad.dpad.right.isPressed);
            MoveHorizontal(ReadingInput.Step(left, dpad));
        }
        // Even at the edge, directions stay in the reader until B is pressed.
        data.Use();
    }
    private void Update()
    {
        if (!_browsing) return;
        var system = EventSystem.current;
        if (!GamepadSupport.IsGamepad || !IsInteractable() || system == null || system.currentSelectedGameObject != gameObject) { Engage(false); return; }
        var pad = Gamepad.current;
        if (pad != null && pad.buttonEast.wasPressedThisFrame || Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { ConsumeBack(); return; }
        GamepadIntent.HoldNavigation(this, GamepadIntent.RightStickHeld);
        ScrollRightStick();
    }
    internal bool ScrollRightStick()
    {
        if (!_browsing || !GamepadSupport.IsGamepad || _scrollFrame == Time.frameCount) return false;
        _scrollFrame = Time.frameCount;
        float y = GamepadIntent.ScrollY, x = GamepadIntent.ScrollX;
        float speed = GamepadIntent.ScrollSpeed(StickSpeed) * Time.unscaledDeltaTime;
        bool moved = y != 0 && MovePage(-y * speed);
        return (x != 0 && MoveHorizontal(x * speed)) || moved;
    }
    private bool MovePage(float amount)
    {
        if (TryMovePage != null) return TryMovePage(amount);
        if (Scroll == null || Scroll.content == null || Scroll.viewport == null || !Scroll.vertical) return false;
        float range = Scroll.content.rect.height - Scroll.viewport.rect.height; if (range <= 1) return false;
        float before = Scroll.verticalNormalizedPosition; Scroll.StopMovement(); Scroll.verticalNormalizedPosition = Mathf.Clamp01(before - amount / range);
        return Mathf.Abs(Scroll.verticalNormalizedPosition - before) > .0001f;
    }
    private bool MoveHorizontal(float amount)
    {
        if (Scroll == null || Scroll.content == null || Scroll.viewport == null || !Scroll.horizontal) return false;
        float range = Scroll.content.rect.width - Scroll.viewport.rect.width; if (range <= 1) return false;
        float before = Scroll.horizontalNormalizedPosition; Scroll.StopMovement(); Scroll.horizontalNormalizedPosition = Mathf.Clamp01(before + amount / range);
        return Mathf.Abs(Scroll.horizontalNormalizedPosition - before) > .0001f;
    }
    internal static ControllerReadingSurface? ForScroll(ScrollRect scroll)
    {
        foreach (var surface in Surfaces) if (surface != null && surface.isActiveAndEnabled && surface.Scroll == scroll) return surface;
        return null;
    }
}
