#nullable enable
using System;
using System.Collections.Generic;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Zorro.Core;

namespace ModConfigEnhance;

/// <summary>
/// 1.1.0 手柄扩展（用户 2026-11-12 拍板，见 验证/手柄全量适配/方案草案.md）。
/// 只在 <see cref="GamepadSupport.IsGamepad"/> 时生效，鼠标路径不动。
///   · 按住 RT = 功能层：本帧起暂停 EventSystem 导航，方向键交给页面自己处理（跳分组、翻页），松开恢复。
///   · 右摇杆只滚动：游戏把右摇杆也绑在 UI/Navigate 上，页面里右摇杆一动就暂停导航，焦点不跟着跑。
///   · 不用 LT（peakReconnect 不在大厅时按 LT 即重连）、不用 View+RT（物品生成器增强）、不用 LB+RB（背队友）。
/// </summary>
internal static class GamepadIntent
{
    /// <summary>Hold threshold = the settings page's stick deadzone (0.15), so a light push never becomes a UI move.</summary>
    internal const float HoldDeadzone = .15f;
    /// <summary>Scrolling starts a little further out so resting drift does not creep the page.</summary>
    internal const float ScrollDeadzone = .25f;
    private const int ReleaseGraceFrames = 3;
    private static int _frame = -1, _releaseAt = -1;
    private static float _scrollSince = -1;
    private static bool _layer, _rightStick, _rightStickHeld;
    private static EventSystem? _suppressed;
    private static bool _savedNavigation;
    private static readonly List<UnityEngine.Object> _owners = new();

    /// <summary>当前是否按住 RT 功能层（手柄态、RT 压到底一半以上）。</summary>
    internal static bool Layer { get { Sample(); return _layer; } }
    /// <summary>右摇杆当前的竖直量（死区内为 0）。</summary>
    internal static float ScrollY { get { Sample(); return _rightStick ? Gamepad.current!.rightStick.ReadValue().y : 0; } }
    internal static float ScrollX { get { Sample(); return _rightStick ? Gamepad.current!.rightStick.ReadValue().x : 0; } }
    /// <summary>Scroll speed that grows the longer the stick is held: 1× at first, 2× after ~0.6 s, up to 5× after
    /// ~2 s (user 2026-11-12: "越来越快"). RT still multiplies on top.</summary>
    internal static float ScrollSpeed(float baseSpeed)
    {
        Sample();
        float held = _scrollSince < 0 ? 0 : Time.unscaledTime - _scrollSince;
        float ramp = Mathf.Min(5f, 1f + held * held * 1.0f + held * 0.6f);
        return baseSpeed * ramp * (_layer ? 2.5f : 1f);
    }
    /// <summary>Right stick is (or just was) pushed past the navigation deadzone, in any direction.</summary>
    internal static bool RightStickHeld { get { Sample(); return _rightStickHeld; } }

    private static void Sample()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;
        var pad = Gamepad.current;
        bool gamepad = pad != null && GamepadSupport.IsGamepad;
        _layer = gamepad && pad!.rightTrigger.ReadValue() > .5f;
        float magnitude = gamepad ? pad!.rightStick.ReadValue().sqrMagnitude : 0;
        _rightStick = magnitude > ScrollDeadzone * ScrollDeadzone;
        if (!_rightStick) _scrollSince = -1; else if (_scrollSince < 0) _scrollSince = Time.unscaledTime;
        // Keep holding a few frames after the stick returns: on release the UI module would otherwise send one move.
        if (magnitude > HoldDeadzone * HoldDeadzone) { _rightStickHeld = true; _releaseAt = Time.frameCount + ReleaseGraceFrames; }
        else if (_rightStickHeld && Time.frameCount >= _releaseAt) _rightStickHeld = false;
        // Edge tracking for the left stick runs every frame, so holding the stick before pressing RT is not a fresh press.
        if (pad != null) StickEdge(pad.leftStick.ReadValue());
    }

    /// <summary>方向键按下（功能层用）：十字键或左摇杆越过死区的那一帧。</summary>
    internal static MoveDirection PressedDirection()
    {
        var pad = Gamepad.current; if (pad == null) return MoveDirection.None;
        if (pad.dpad.up.wasPressedThisFrame) return MoveDirection.Up;
        if (pad.dpad.down.wasPressedThisFrame) return MoveDirection.Down;
        if (pad.dpad.left.wasPressedThisFrame) return MoveDirection.Left;
        if (pad.dpad.right.wasPressedThisFrame) return MoveDirection.Right;
        Sample(); return _stickCache;
    }
    private static Vector2 _lastStick;
    private static int _lastStickFrame = -1;
    private static MoveDirection _stickCache;
    private static MoveDirection StickEdge(Vector2 now)
    {
        if (_lastStickFrame == Time.frameCount) return _stickCache;
        bool wasOut = _lastStick.sqrMagnitude > .5f * .5f, isOut = now.sqrMagnitude > .5f * .5f;
        _stickCache = MoveDirection.None;
        if (isOut && !wasOut)
            _stickCache = Mathf.Abs(now.x) > Mathf.Abs(now.y) ? (now.x > 0 ? MoveDirection.Right : MoveDirection.Left) : (now.y > 0 ? MoveDirection.Up : MoveDirection.Down);
        _lastStick = now; _lastStickFrame = Time.frameCount;
        return _stickCache;
    }

    /// <summary>页面每帧调用：RT 功能层或右摇杆滚动期间暂停 EventSystem 的方向导航，结束后原样恢复。
    /// <paramref name="owner"/> 是页面根对象；同一时刻多个页面都要求暂停时，最后一个释放的才恢复。</summary>
    internal static void HoldNavigation(UnityEngine.Object owner, bool hold)
    {
        var system = EventSystem.current;
        if (hold)
        {
            if (system == null) return;
            _owners.RemoveAll(o => o == null);
            // A new scene's EventSystem: the old one is gone, so its saved state no longer applies.
            if (_suppressed != null && _suppressed != system) { _suppressed = null; _owners.Clear(); }
            if (!_owners.Contains(owner)) _owners.Add(owner);
            if (_suppressed == null) { _suppressed = system; _savedNavigation = system.sendNavigationEvents; system.sendNavigationEvents = false; }
            return;
        }
        _owners.RemoveAll(o => o == null);
        if (!_owners.Remove(owner) || _owners.Count > 0) return;
        Restore();
    }
    /// <summary>Drops every hold and restores the saved state right now. Hotkey recording calls this before it saves
    /// <c>sendNavigationEvents</c> itself, so the two never record each other's "paused" value.</summary>
    internal static void ReleaseAll() => Restore();
    /// <summary>页面关闭时调用，保证不会把导航留在关闭状态。</summary>
    internal static void Release(UnityEngine.Object owner) => HoldNavigation(owner, false);
    private static void Restore()
    {
        _owners.Clear();
        if (_suppressed != null) { _suppressed.sendNavigationEvents = _savedNavigation; _suppressed = null; }
    }

    /// <summary>1.1.0：右摇杆滚动 <paramref name="root"/> 里最合适的滚动区（焦点所在的那个优先，其次是内容最长的）。
    /// 弹窗正文、问题报告（左右推 = 横向）、管理中心各页、更新队列都走这里。返回是否滚动了。</summary>
    internal static bool ScrollWithin(Transform root)
    {
        float y = ScrollY, x = ScrollX;
        if (y == 0 && x == 0) return false;
        ScrollRect? best = null;
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && selected.transform.IsChildOf(root)) {
            var focusedReader = selected.GetComponent<Overview.ControllerReadingSurface>();
            best = focusedReader != null && focusedReader.Scroll != null ? focusedReader.Scroll : selected.GetComponentInParent<ScrollRect>();
        }
        if (best == null || !Scrollable(best))
        {
            float most = 0;
            foreach (var scroll in root.GetComponentsInChildren<ScrollRect>(false))
            {
                if (!Scrollable(scroll)) continue;
                float extra = scroll.content.rect.height - scroll.viewport.rect.height + scroll.content.rect.width - scroll.viewport.rect.width;
                if (extra > most) { most = extra; best = scroll; }
            }
        }
        if (best == null || !Scrollable(best)) return false;
        var reader = Overview.ControllerReadingSurface.ForScroll(best);
        if (reader != null) return reader.ScrollRightStick();
        best.StopMovement();
        float speed = ScrollSpeed(900) * Time.unscaledDeltaTime;
        if (best.vertical && Mathf.Abs(y) > 0)
        {
            float range = best.content.rect.height - best.viewport.rect.height;
            if (range > 1) best.verticalNormalizedPosition = Mathf.Clamp01(best.verticalNormalizedPosition + y * speed / range);
        }
        if (best.horizontal && Mathf.Abs(x) > 0)
        {
            float range = best.content.rect.width - best.viewport.rect.width;
            if (range > 1) best.horizontalNormalizedPosition = Mathf.Clamp01(best.horizontalNormalizedPosition + x * speed / range);
        }
        return true;
    }
    private static bool Scrollable(ScrollRect scroll) => scroll != null && scroll.isActiveAndEnabled && scroll.content != null && scroll.viewport != null
        && (scroll.vertical && scroll.content.rect.height > scroll.viewport.rect.height + 1 || scroll.horizontal && scroll.content.rect.width > scroll.viewport.rect.width + 1);

    /// <summary>页面在本帧是否应暂停导航：RT 功能层或右摇杆在推（任意方向）。</summary>
    internal static bool WantsHold { get { Sample(); return _layer || _rightStickHeld; } }

    // ── 按键图标 ──────────────────────────────────────────────

    /// <summary>手柄按键在提示条里的写法：能拿到游戏的手柄图标精灵就用精灵，否则用文字缩写。</summary>
    internal static string Glyph(string control, out TMP_SpriteAsset? sprites)
    {
        sprites = null;
        try
        {
            var data = SingletonAsset<InputSpriteData>.Instance;
            if (data != null)
            {
                string? tag = data.GetSpriteTagFromInputPathGamepad("<Gamepad>/" + control);
                sprites = SpriteSet(data);
                if (!string.IsNullOrEmpty(tag) && sprites != null) return tag!;
            }
        }
        catch (Exception error) { WarnOnce("[手柄] 读取按键图标失败：" + error.Message); }
        sprites = null;
        return control switch
        {
            "buttonSouth" => "A", "buttonEast" => "B", "buttonWest" => "X", "buttonNorth" => "Y",
            "leftShoulder" => "LB", "rightShoulder" => "RB", "rightTrigger" => "RT", "leftTrigger" => "LT",
            "select" => "View", "start" => "Menu", "leftStickPress" => "L3", "rightStickPress" => "R3", "up" => "↑", "down" => "↓", "left" => "←", "right" => "→", _ => control
        };
    }
    internal static TMP_SpriteAsset? SpriteSet(InputSpriteData data)
    {
        string name = ((Gamepad.current?.layout ?? "") + " " + (Gamepad.current?.displayName ?? "")).ToLowerInvariant();
        if (name.Contains("dualsense")) return data.ps5Sprites;
        if (name.Contains("dualshock")) return data.ps4Sprites;
        if (name.Contains("switch")) return data.switchSprites;
        return data.xboxSprites;
    }
    private static bool _warned;
    private static void WarnOnce(string message) { if (_warned) return; _warned = true; Plugin.Log.LogWarning(message); }

    // ── Steam 屏幕键盘 ────────────────────────────────────────

    /// <summary>Steam 大屏幕模式或 Steam Deck 上弹 Steam 屏幕键盘（游戏加入房间页的做法）。
    /// 普通桌面模式返回 false，调用方照常激活输入框。</summary>
    internal static bool TrySteamKeyboard(TMP_InputField field)
    {
        try
        {
            if (!(Portningsbolaget.Platforms.PlatformManager.Platform is SteamRuntimeManager steam)) return false;
            if (!steam.UsingBigPictureMode && !steam.OnSteamDeck) return false;
            string title = field.placeholder is TMP_Text hint && !string.IsNullOrWhiteSpace(hint.text) ? hint.text : Loc.T("Enter text", "输入文字", "輸入文字");
            // Steam's keyboard holds at most a few thousand characters; longer text stays with the normal field.
            if ((field.text?.Length ?? 0) > SteamKeyboard.MaxLength) return false;
            uint max = field.characterLimit > 0 ? (uint)Math.Min(field.characterLimit, SteamKeyboard.MaxLength) : (uint)SteamKeyboard.MaxLength;
            // The game's own text-input callback (join-room page, report dialog) is never cleared and Steam sends every
            // dismissal to all listeners; clear it so our keyboard cannot run the join-room or report handler again.
            steam.m_onDialogDone = null;
            return SteamKeyboard.Open(field, title, field.lineType != TMP_InputField.LineType.SingleLine, max);
        }
        catch (Exception error)
        {
            WarnOnce("[手柄] Steam 屏幕键盘不可用：" + error.Message);
            return false;
        }
    }

    private static class SteamKeyboard
    {
        internal const int MaxLength = 2048;
        private static Callback<GamepadTextInputDismissed_t>? _callback;
        private static TMP_InputField? _target;
        internal static bool Open(TMP_InputField field, string title, bool multiline, uint max)
        {
            _callback ??= Callback<GamepadTextInputDismissed_t>.Create(Dismissed);
            _target = field;
            bool shown = SteamUtils.ShowGamepadTextInput(EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                multiline ? EGamepadTextInputLineMode.k_EGamepadTextInputLineModeMultipleLines : EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine,
                title, max, field.text ?? "");
            if (!shown) _target = null;
            return shown;
        }
        private static void Dismissed(GamepadTextInputDismissed_t result)
        {
            var field = _target; _target = null;
            if (field == null || !result.m_bSubmitted || !field.gameObject.activeInHierarchy || !field.interactable) return;
            uint length = SteamUtils.GetEnteredGamepadTextLength();
            if (!SteamUtils.GetEnteredGamepadTextInput(out string text, length + 1)) return;
            if (field.characterLimit > 0 && text.Length > field.characterLimit) text = text.Substring(0, field.characterLimit);
            field.text = text;
            field.onEndEdit?.Invoke(text);
            field.onSubmit?.Invoke(text);
        }
    }
}
