#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;
using Zorro.ControllerSupport;

namespace ModConfigEnhance;

/// <summary>
/// 1.1.5 diagnosis (user: "after Back + Start the mouse cursor was gone"). The game's CursorHandler hides and locks the
/// cursor whenever its control scheme is not keyboard/mouse, so the cursor only comes back once the game switches the
/// scheme. This only records the state the first time the mouse moves while the cursor stays hidden; it changes nothing.
/// </summary>
internal static class CursorWatch
{
    private static float _since = -1f;
    private static bool _logged;
    internal static void Tick()
    {
        try
        {
            var mouse = Mouse.current;
            bool moving = mouse != null && mouse.delta.ReadValue().sqrMagnitude > 4f;
            bool hidden = !Cursor.visible || Cursor.lockState == CursorLockMode.Locked;
            if (!hidden) { _since = -1f; _logged = false; return; }
            if (!moving) return;
            if (_since < 0f) { _since = Time.unscaledTime; return; }
            if (_logged || Time.unscaledTime - _since < 1.5f) return;
            _logged = true;
            Plugin.Log.LogWarning("[光标] 鼠标在移动但光标仍被隐藏：方案=" + InputHandler.GetCurrentUsedInputScheme()
                + "，lockState=" + Cursor.lockState + "，visible=" + Cursor.visible
                + "，手柄锁=" + GamepadLock.Active + "，管理器判定手柄=" + GamepadSupport.IsGamepad
                + "，导航事件=" + (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.sendNavigationEvents));
        }
        catch { }
    }
}
