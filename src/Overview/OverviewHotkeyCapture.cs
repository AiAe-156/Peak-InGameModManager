#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.EventSystems;

namespace ModConfigEnhance.Overview;

internal sealed class OverviewHotkeyCapture : MonoBehaviour
{
    public static int ConsumedCancelFrame { get; private set; } = -1;
    public bool IsCapturing => _entry != null;
    private OverviewHotkeyEntry? _entry;
    private Action<object>? _accept;
    private Action? _cancel;
    private bool _released;
    private int _beginFrame;
    private InputControl? _held;
    private float _heldSeconds;
    private KeyControl? _modifier;
    private readonly HashSet<ButtonControl> _initialHeld = new();
    private EventSystem? _eventSystem;
    private bool _navigationEnabled;
    public void Begin(OverviewHotkeyEntry entry, Action<object> accept, Action cancel)
    {
        CancelCapture();
        _entry = entry; _accept = accept; _cancel = cancel;
        _released = false; _beginFrame = Time.frameCount; _held = null; _heldSeconds = 0; _modifier = null;
        _legacyHeld = ""; _legacySeconds = 0;
        _initialHeld.Clear(); foreach (var button in Buttons()) if (button.isPressed) _initialHeld.Add(button);
        _eventSystem = EventSystem.current;
        if (_eventSystem != null) { _navigationEnabled = _eventSystem.sendNavigationEvents; _eventSystem.sendNavigationEvents = false; }
    }
    public bool CancelCapture()
    {
        if (_entry == null) return false;
        var cancel = _cancel; Reset(); cancel?.Invoke(); return true;
    }
    private void Reset()
    {
        if (_eventSystem != null) _eventSystem.sendNavigationEvents = _navigationEnabled;
        _eventSystem = null; _entry = null; _accept = null; _cancel = null; _initialHeld.Clear(); _held = null; _modifier = null;
    }
    private static IEnumerable<ButtonControl> Buttons() => InputSystem.devices.Where(d => d is Keyboard || d is Mouse || d is Gamepad || d is Joystick)
        .SelectMany(d => d.allControls).OfType<ButtonControl>().Where(c => !c.synthetic && c.name != "anyKey");
    private static bool Modifier(Key key) => key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftMeta || key == Key.RightMeta;
    private static IEnumerable<KeyCode> Modifiers()
    {
        var keyboard = Keyboard.current; if (keyboard == null) yield break;
        if (keyboard.leftCtrlKey.isPressed) yield return KeyCode.LeftControl;
        if (keyboard.rightCtrlKey.isPressed) yield return KeyCode.RightControl;
        if (keyboard.leftShiftKey.isPressed) yield return KeyCode.LeftShift;
        if (keyboard.rightShiftKey.isPressed) yield return KeyCode.RightShift;
        if (keyboard.leftAltKey.isPressed) yield return KeyCode.LeftAlt;
        if (keyboard.rightAltKey.isPressed) yield return KeyCode.RightAlt;
        if (keyboard.leftMetaKey.isPressed) yield return KeyCode.LeftCommand;
        if (keyboard.rightMetaKey.isPressed) yield return KeyCode.RightCommand;
    }
    private void Update()
    {
        if (_entry == null) return;
        if (Time.frameCount == _beginFrame) return;
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true || Gamepad.current?.buttonEast.wasPressedThisFrame == true || InputSystem.actions?.FindAction("Pause")?.WasPressedThisFrame() == true)
        { ConsumedCancelFrame = Time.frameCount; CancelCapture(); return; }
        if (!_released)
        {
            // Include the opening frame's held controls, and wait through any additional
            // confirmation click held during the transition. Never record the opening click.
            if (_initialHeld.Any(c => c.isPressed) || Buttons().Any(c => c.isPressed) || Input.anyKey) return;
            _initialHeld.Clear(); _released = true; return;
        }
        var entry = _entry;
        foreach (var button in Buttons())
        {
            bool gamepadHold = GamepadSupport.IsGamepad && entry.Config.SettingType == typeof(KeyCode);
            if (!(gamepadHold ? button.isPressed : button.wasPressedThisFrame)) continue;
            if (entry.Adapter.IsShortcut && button is KeyControl modifier && Modifier(modifier.keyCode)) { _modifier = modifier; continue; }
            object? value = entry.Adapter.FromControl(button, Modifiers()); if (value == null || !entry.Adapter.TryConvert(value, out value, out _)) continue;
            if (gamepadHold)
            {
                if (_held != button) { _held = button; _heldSeconds = 0; }
                _heldSeconds += Time.unscaledDeltaTime; if (_heldSeconds < .55f) return;
            }
            Complete(value); return;
        }
        if (_modifier != null && !_modifier.isPressed)
        {
            object? value = entry.Adapter.FromControl(_modifier, Array.Empty<KeyCode>());
            if (value != null && entry.Adapter.TryConvert(value, out value, out _)) { Complete(value); return; }
            _modifier = null;
        }
        if (_held is ButtonControl held && !held.isPressed) { _held = null; _heldSeconds = 0; }
        // Legacy joystick KeyCode values do not have an InputSystem name conversion.
        if (entry.Config.SettingType == typeof(KeyCode) || entry.Adapter.IsShortcut)
        foreach (KeyCode legacy in Enum.GetValues(typeof(KeyCode)))
        {
            if ((int)legacy < (int)KeyCode.JoystickButton0 || !Input.GetKey(legacy)) continue;
            if (GamepadSupport.IsGamepad)
            {
                string id = legacy.ToString(); if (_legacyHeld != id) { _legacyHeld = id; _legacySeconds = 0; }
                _legacySeconds += Time.unscaledDeltaTime; if (_legacySeconds < .55f) return;
            }
            else if (!Input.GetKeyDown(legacy)) continue;
            object value = entry.Adapter.IsShortcut ? new BepInEx.Configuration.KeyboardShortcut(legacy, Modifiers().ToArray()) : legacy;
            if (entry.Adapter.TryConvert(value, out value, out _)) { Complete(value); return; }
        }
        if (!Input.anyKey) { _legacyHeld = ""; _legacySeconds = 0; }
    }
    private string _legacyHeld = "";
    private float _legacySeconds;
    private void Complete(object value) { var accept = _accept; Reset(); accept?.Invoke(value); }
    private void OnDisable() => CancelCapture();
    private void OnDestroy() => CancelCapture();
}
