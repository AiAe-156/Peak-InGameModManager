#nullable enable
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 给 TMP_InputField 加撤销／重做：Ctrl+Z 撤销；Ctrl+Shift+Y、Ctrl+Y、Ctrl+Shift+Z 重做。
/// 历史由纯逻辑的 <see cref="TextUndoHistory"/> 保存，这里只负责读键盘、监听文本变化、把结果写回输入框。
/// </summary>
internal sealed class TextInputUndoBinding : MonoBehaviour
{
    private TMP_InputField? _input;
    private TextUndoHistory _history = new("");
    private bool _applying;

    internal static TextInputUndoBinding Attach(TMP_InputField input, string initial)
    {
        var binding = input.GetComponent<TextInputUndoBinding>() ?? input.gameObject.AddComponent<TextInputUndoBinding>();
        binding._input = input; binding._history = new TextUndoHistory(initial);
        input.onValueChanged.AddListener(binding.OnChanged);
        input.onSelect.AddListener(_ => binding._history.BreakCoalescing());
        return binding;
    }

    /// <summary>程序性替换全部文本（如“撤销修改”按钮），同样可被 Ctrl+Z 撤回。</summary>
    internal void ReplaceText(string text)
    {
        if (_input == null) return;
        _history.BreakCoalescing();
        _history.Record(text, Time.unscaledTime);
        _history.BreakCoalescing();
        Apply(_history.Text, text.Length);
    }

    /// <summary>保存之后让下一次输入单独成一步。</summary>
    internal void MarkSaved() => _history.BreakCoalescing();

    private void OnChanged(string value)
    {
        if (_applying || Input.compositionString.Length > 0) return;
        _history.Record(value, Time.unscaledTime);
    }

    private void Update()
    {
        if (_input == null || !_input.isFocused || _input.readOnly) return;
        int command = TypingGuard.ReadKeyboard(ReadCommand);
        if (command == 0 || Input.compositionString.Length > 0) return;
        // 先把尚未记录的输入落账，再撤销；否则会丢最近一次输入。
        if (!string.Equals(_input.text, _history.Text)) _history.Record(_input.text, Time.unscaledTime);
        bool ok = command < 0 ? _history.Undo(out string text, out int caret) : _history.Redo(out text, out caret);
        if (ok) Apply(text, caret);
    }

    /// <summary>-1 撤销，1 重做，2 表示粘贴／剪切（只断开合并），0 无命令。</summary>
    private int ReadCommand()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !(keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed) || keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed) return 0;
        bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        if (keyboard.zKey.wasPressedThisFrame) return shift ? 1 : -1;
        if (keyboard.yKey.wasPressedThisFrame) return 1;
        if (keyboard.vKey.wasPressedThisFrame || keyboard.xKey.wasPressedThisFrame) _history.BreakCoalescing();
        return 0;
    }

    private void Apply(string text, int caret)
    {
        if (_input == null) return;
        _applying = true;
        try
        {
            _input.SetTextWithoutNotify(text);
            _input.stringPosition = Mathf.Clamp(caret, 0, text.Length);
            _input.ForceLabelUpdate();
        }
        finally { _applying = false; }
    }
}
