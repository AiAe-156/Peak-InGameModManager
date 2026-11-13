#nullable enable
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.1.7 diagnosis (user: "moving left / right along the toolbar sometimes jumps up to the tab row"). Every toolbar
/// button links Up to the nearest page tab, so the jump is an Up move; this records what the pad reported in the
/// frames around it. It only writes a log line and changes nothing.
/// </summary>
internal sealed partial class OverviewPage
{
    private const int JumpFrames = 8, JumpLogLimit = 20;
    private readonly Vector2[] _jumpStick = new Vector2[JumpFrames], _jumpDpad = new Vector2[JumpFrames];
    private int _jumpCursor, _jumpLogged;
    private GameObject? _jumpLast;
    private bool IsPageTab(GameObject target) => target == _back.gameObject || target == _listTab.gameObject
        || target == _introTab.gameObject || target == _hotkeyTab.gameObject;
    private void WatchToolbarJump()
    {
        var pad = Gamepad.current; var system = EventSystem.current;
        if (pad == null || system == null || _jumpLogged >= JumpLogLimit) return;
        _jumpCursor = (_jumpCursor + 1) % JumpFrames;
        _jumpStick[_jumpCursor] = pad.leftStick.ReadValue(); _jumpDpad[_jumpCursor] = pad.dpad.ReadValue();
        var selected = system.currentSelectedGameObject; var previous = _jumpLast; _jumpLast = selected;
        if (selected == previous || selected == null || previous == null) return;
        if (!previous.transform.IsChildOf(_controls) || !IsPageTab(selected)) return;
        _jumpLogged++;
        try
        {
            var stick = new StringBuilder(); var dpad = new StringBuilder();
            for (int i = 1; i <= JumpFrames; i++)
            {
                int index = (_jumpCursor + i) % JumpFrames;
                stick.Append(' ').Append(_jumpStick[index].x.ToString("0.00")).Append(',').Append(_jumpStick[index].y.ToString("0.00"));
                dpad.Append(' ').Append(_jumpDpad[index].x.ToString("0")).Append(',').Append(_jumpDpad[index].y.ToString("0"));
            }
            string move = "无";
            if (system.currentInputModule is InputSystemUIInputModule module && module.move != null && module.move.action != null)
                move = module.move.action.ReadValue<Vector2>().ToString("0.00");
            var mouse = Mouse.current;
            Plugin.Log.LogInfo("[焦点] 工具条跳到页签行：" + previous.name + " → " + selected.name + "，帧=" + Time.frameCount
                + "，上一帧耗时=" + (Time.unscaledDeltaTime * 1000f).ToString("0") + "ms，手柄=" + pad.displayName
                + "，左摇杆近 " + JumpFrames + " 帧(旧→新)=" + stick + "，十字键=" + dpad + "，界面方向值=" + move
                + "，鼠标左键=" + (mouse != null && mouse.leftButton.isPressed) + "，判定手柄=" + GamepadSupport.IsGamepad
                + "，功能层=" + GamepadIntent.Layer + "，页签=" + _tab);
        }
        catch (System.Exception error) { Plugin.Log.LogWarning("[焦点] 诊断记录失败：" + error.Message); }
    }
}
