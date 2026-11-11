#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ModConfigEnhance;

/// <summary>
/// 打字保护：模组设置页里有 TMP 输入框（搜索框、字符串选项）拿着焦点时，别的模组轮询键盘不该触发快捷键。
/// 游戏自己的动作在暂停菜单里已被挡住，漏的是各模组直接读 <c>Input.GetKeyDown</c> / <c>Keyboard.current[key].wasPressedThisFrame</c>。
/// 这里给这两条路加 Prefix：聚焦期间键盘一律「没按」。鼠标、手柄不拦，UI 输入模块走 InputAction 也不经过这些读法。
/// </summary>
internal static class TypingGuard
{
    /// <summary>当前是否有输入框拿着焦点（由 <see cref="Driver"/> 每帧维护）。</summary>
    internal static bool Active;

    [ThreadStatic] private static int _uiReadDepth;
    /// <summary>只有本模组的编辑器或 TMP 自己处理输入时允许读真按键；其他模组仍受打字保护。</summary>
    internal static T ReadKeyboard<T>(Func<T> read)
    {
        _uiReadDepth++;
        try { return read(); } finally { _uiReadDepth--; }
    }

    private static bool _patched;

    internal static void TryApply(Harmony harmony)
    {
        if (_patched)
        {
            return;
        }

        _patched = true;
        int legacy = 0;
        foreach (string name in new[] { "GetKey", "GetKeyDown", "GetKeyUp" })
        {
            legacy += PatchLegacy(harmony, name, typeof(KeyCode));
            legacy += PatchLegacy(harmony, name, typeof(string));
        }

        int modern = 0;
        foreach (string name in new[] { "isPressed", "wasPressedThisFrame", "wasReleasedThisFrame" })
        {
            MethodInfo? getter = AccessTools.PropertyGetter(typeof(ButtonControl), name);
            if (getter == null || getter.GetMethodBody() == null)
            {
                continue;
            }

            try
            {
                harmony.Patch(getter, prefix: new HarmonyMethod(typeof(TypingGuard), nameof(ButtonPrefix)));
                modern++;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[打字保护] ButtonControl.{name} 挂不上: {ex.Message}");
            }
        }

        Plugin.Log.LogInfo($"[打字保护] 已挂载：旧 Input {legacy}/6，InputSystem 按键 {modern}/3。");
        foreach (string name in new[] { "LateUpdate", "OnUpdateSelected", "KeyPressed" })
        {
            MethodInfo? inputMethod = AccessTools.Method(typeof(TMP_InputField), name);
            if (inputMethod == null || inputMethod.GetMethodBody() == null) continue;
            try { harmony.Patch(inputMethod, prefix: new HarmonyMethod(typeof(TypingGuard), name == "KeyPressed" ? nameof(BeginUiKeyRead) : nameof(BeginUiRead)), finalizer: new HarmonyMethod(typeof(TypingGuard), nameof(EndUiRead))); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[打字保护] TMP 输入通路 {name} 挂载失败: {ex.Message}"); }
        }
    }

    private static void BeginUiRead(out bool __state) { __state = true; _uiReadDepth++; }
    private static bool BeginUiKeyRead(TMP_InputField __instance, Event evt, ref TMP_InputField.EditState __result, out bool __state)
    {
        BeginUiRead(out __state);
        if (__instance is Overview.IntroductionTextInput && __instance.isFocused && __instance.lineType == TMP_InputField.LineType.MultiLineNewline
            && !evt.control && Input.compositionString.Length == 0 && evt.character == '\0' && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)) evt.character = '\n';
        if (!evt.control || Input.compositionString.Length > 0) return true;
        var editor = __instance.GetComponentInParent<Overview.IntroductionEditorKeyboard>();
        if (editor == null || editor.View == null || !editor.View.HasKeyboardFocus) return true;
        bool formatting = evt.keyCode == KeyCode.B || evt.keyCode == KeyCode.I || evt.keyCode == KeyCode.U;
        bool history = evt.keyCode == KeyCode.Z || evt.keyCode == KeyCode.Y;
        bool document = evt.keyCode == KeyCode.S || evt.keyCode == KeyCode.K;
        if (!formatting && !history && !document) return true;
        // The editor's scoped keyboard driver handles these. TMP must not append Ctrl+I as a tab.
        __result = TMP_InputField.EditState.Continue; return false;
    }
    private static Exception? EndUiRead(Exception? __exception, bool __state) { if (__state) _uiReadDepth--; return __exception; }

    private static int PatchLegacy(Harmony harmony, string name, Type argument)
    {
        MethodInfo? method = AccessTools.Method(typeof(Input), name, new[] { argument });
        if (method == null || method.GetMethodBody() == null)
        {
            return 0; // extern（InternalCall）Harmony 挂不上
        }

        try
        {
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(TypingGuard), nameof(LegacyPrefix)));
            return 1;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[打字保护] Input.{name}({argument.Name}) 挂不上: {ex.Message}");
            return 0;
        }
    }

    private static bool LegacyPrefix(ref bool __result)
    {
        if (!Active || _uiReadDepth > 0)
        {
            return true;
        }

        __result = false;
        return false;
    }

    private static bool ButtonPrefix(ButtonControl __instance, ref bool __result)
    {
        if (!Active || _uiReadDepth > 0 || __instance.device is not Keyboard)
        {
            return true;
        }

        __result = false;
        return false;
    }

    /// <summary>挂在模组设置页上：页面开着时每帧看焦点是不是 TMP 输入框。</summary>
    internal sealed class Driver : MonoBehaviour
    {
        private void Update()
        {
            GameObject? selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            TMP_InputField? field = selected != null ? selected.GetComponent<TMP_InputField>() : null;
            var document = selected != null ? selected.GetComponent<Overview.Editor.DocSurface>() : null;
            Active = (field != null && field.isFocused) || (document != null && document.TypingEnabled && document.isActiveAndEnabled);
        }

        private void OnDisable()
        {
            Active = false;
        }
    }
}
