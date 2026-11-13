#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace ModConfigEnhance;

/// <summary>
/// 1.1.5（用户 2026-11-13「现有锁定机制不可靠」）：录手柄键时的全局锁。
/// 以前的锁是在各条返回路径上逐个拦截，漏一条就漏出去。现在在输入系统最底层（<c>InputSystem.onEvent</c>）
/// 把手柄事件里的按键与摇杆改写成「未按下」再交给输入系统：游戏的返回 / 暂停 / 导航、其他模组、本模组自己的
/// 手柄逻辑一律读不到手柄；只有录键器通过 <see cref="Pressed"/> / <see cref="WasPressed"/> 读原始按键。
/// 结束锁定时还按着的键继续屏蔽到松开为止，不会在松手前「冒」出一次按下。
/// 安全阀：锁的主人失活、插件卸载都会自动解锁；键盘 Esc 由录键页取消录制。
/// </summary>
internal static class GamepadLock
{
    private static readonly string[] Names = { "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder",
        "leftTrigger", "rightTrigger", "dpad/up", "dpad/down", "dpad/left", "dpad/right", "leftStickPress", "rightStickPress", "start", "select" };
    private static readonly Dictionary<string, bool> _raw = new();
    /// <summary>Buttons that went down since the last <see cref="DrainPresses"/>, in order (a press is never lost when a
    /// frame skips reading).</summary>
    private static readonly List<string> _presses = new();
    private static readonly HashSet<string> _sticky = new();
    private static GameObject? _owner;
    private static bool _subscribed;

    internal static bool Active { get; private set; }

    /// <summary>Starts swallowing controller input. <paramref name="owner"/>: the lock ends by itself when it goes inactive.</summary>
    internal static void Begin(GameObject owner)
    {
        _owner = owner; Active = true; _sticky.Clear(); _presses.Clear(); _raw.Clear();
        var pad = Gamepad.current;
        if (pad != null) foreach (var name in Names) { var c = GamepadCombos.Control(pad, name); _raw[name] = c != null && c.IsValueConsideredPressed(c.ReadValue()); }
        Subscribe();
    }

    /// <summary>Stops the lock. Buttons still held keep reading as released until they are let go.</summary>
    internal static void End()
    {
        if (!Active) return;
        Active = false; _owner = null;
        foreach (var pair in _raw) if (pair.Value) _sticky.Add(pair.Key);
        if (_sticky.Count == 0) Unsubscribe();
    }

    /// <summary>Plugin unload: drop everything.</summary>
    internal static void Shutdown() { Active = false; _owner = null; _sticky.Clear(); Unsubscribe(); }

    internal static bool Pressed(string name) => _raw.TryGetValue(name, out bool v) && v;
    /// <summary>Moves the presses collected so far into <paramref name="into"/> (buttons held when the lock began are not
    /// pressed until they are released and pressed again).</summary>
    internal static void DrainPresses(List<string> into) { into.AddRange(_presses); _presses.Clear(); }

    private static void Subscribe() { if (_subscribed) return; InputSystem.onEvent += OnEvent; _subscribed = true; }
    private static void Unsubscribe() { if (!_subscribed) return; InputSystem.onEvent -= OnEvent; _subscribed = false; }

    private static void OnEvent(InputEventPtr eventPtr, InputDevice device)
    {
        try
        {
            if (device is not Gamepad pad) return;
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;
            // Safety valve: the page that owns the lock went away without ending it.
            if (Active && (_owner == null || !_owner.activeInHierarchy)) { Plugin.Log.LogWarning("[手柄锁] 录键页已关闭，自动解锁。"); End(); }
            if (Active)
            {
                bool current = pad == Gamepad.current;
                foreach (var name in Names)
                {
                    var control = GamepadCombos.Control(pad, name); if (control == null) continue;
                    if (current && control.ReadValueFromEvent(eventPtr, out float value))
                    {
                        bool down = control.IsValueConsideredPressed(value);
                        bool was = _raw.TryGetValue(name, out bool w) && w;
                        if (down && !was) _presses.Add(name);
                        _raw[name] = down;
                    }
                    control.WriteValueIntoEvent(0f, eventPtr);
                }
                pad.leftStick.WriteValueIntoEvent(Vector2.zero, eventPtr);
                pad.rightStick.WriteValueIntoEvent(Vector2.zero, eventPtr);
                return;
            }
            if (_sticky.Count == 0) { Unsubscribe(); return; }
            // After the lock: a button held across the end stays released until the player lets go of it.
            List<string>? done = null;
            foreach (var name in _sticky)
            {
                var control = GamepadCombos.Control(pad, name); if (control == null) continue;
                if (control.ReadValueFromEvent(eventPtr, out float value) && !control.IsValueConsideredPressed(value)) (done ??= new()).Add(name);
                else control.WriteValueIntoEvent(0f, eventPtr);
            }
            if (done != null) foreach (var name in done) _sticky.Remove(name);
            if (_sticky.Count == 0) Unsubscribe();
        }
        catch (Exception error) { Plugin.Log.LogWarning("[手柄锁] 事件处理失败，已解锁：" + error.Message); Active = false; _owner = null; _sticky.Clear(); Unsubscribe(); }
    }
}
