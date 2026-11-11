#nullable enable
using System;
using System.Collections.Generic;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 纯文本撤销／重做模型（不依赖 Unity，可单独测试）。
/// 只保存每次修改的差异片段，而不是整份文本快照，所以 2 MB 的大文件也不会因为历史而成倍占内存。
/// 连续输入、连续退格、连续 Delete 在时间窗口内合并成一步。
/// </summary>
internal sealed class TextUndoHistory
{
    private sealed class Edit
    {
        internal int Index;
        internal string Removed, Inserted;
        internal double Time;
        internal Edit(int index, string removed, string inserted, double time) { Index = index; Removed = removed; Inserted = inserted; Time = time; }
        internal int Weight => Removed.Length + Inserted.Length + 16;
    }

    private readonly List<Edit> _undo = new(), _redo = new();
    private readonly int _maxEntries;
    private readonly long _maxChars;
    private readonly double _coalesceSeconds;
    private long _chars;
    private bool _sealed;

    internal string Text { get; private set; }
    internal bool CanUndo => _undo.Count > 0;
    internal bool CanRedo => _redo.Count > 0;
    internal int UndoCount => _undo.Count;
    internal int RedoCount => _redo.Count;

    internal TextUndoHistory(string initial, int maxEntries = 200, long maxChars = 4_000_000, double coalesceSeconds = .7)
    {
        Text = initial ?? ""; _maxEntries = Math.Max(1, maxEntries); _maxChars = Math.Max(64, maxChars); _coalesceSeconds = coalesceSeconds;
    }

    /// <summary>以新文本重新开始，并清空全部历史。</summary>
    internal void Reset(string text) { Text = text ?? ""; _undo.Clear(); _redo.Clear(); _chars = 0; _sealed = false; }

    /// <summary>下一次记录不再与上一步合并（例如获得焦点、粘贴、保存之后）。</summary>
    internal void BreakCoalescing() => _sealed = true;

    /// <summary>记录文本变成了 <paramref name="text"/>。文本没变返回 false。成功记录会清空重做栈。</summary>
    internal bool Record(string text, double now)
    {
        text ??= "";
        if (string.Equals(text, Text, StringComparison.Ordinal)) return false;
        var edit = Diff(Text, text, now);
        Text = text;
        if (_redo.Count > 0) _redo.Clear();
        if (!_sealed && _undo.Count > 0 && TryMerge(_undo[_undo.Count - 1], edit, now, out int delta)) _chars += delta;
        else { _undo.Add(edit); _chars += edit.Weight; }
        _sealed = false;
        Trim();
        return true;
    }

    /// <summary>撤销一步。<paramref name="caret"/> 为撤销后光标应在的位置。</summary>
    internal bool Undo(out string text, out int caret)
    {
        text = Text; caret = 0;
        if (_undo.Count == 0) return false;
        var edit = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1); _chars -= edit.Weight;
        Text = Text.Substring(0, edit.Index) + edit.Removed + Text.Substring(edit.Index + edit.Inserted.Length);
        _redo.Add(edit); _chars += edit.Weight; _sealed = true; Trim();
        text = Text; caret = edit.Index + edit.Removed.Length;
        return true;
    }

    /// <summary>重做一步。</summary>
    internal bool Redo(out string text, out int caret)
    {
        text = Text; caret = 0;
        if (_redo.Count == 0) return false;
        var edit = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1); _chars -= edit.Weight;
        Text = Text.Substring(0, edit.Index) + edit.Inserted + Text.Substring(edit.Index + edit.Removed.Length);
        _undo.Add(edit); _chars += edit.Weight; _sealed = true; Trim();
        text = Text; caret = edit.Index + edit.Inserted.Length;
        return true;
    }

    private static Edit Diff(string oldText, string newText, double now)
    {
        int max = Math.Min(oldText.Length, newText.Length), prefix = 0;
        while (prefix < max && oldText[prefix] == newText[prefix]) prefix++;
        if (prefix > 0 && char.IsHighSurrogate(oldText[prefix - 1])) prefix--;
        int suffix = 0, limit = max - prefix;
        while (suffix < limit && oldText[oldText.Length - 1 - suffix] == newText[newText.Length - 1 - suffix]) suffix++;
        if (suffix > 0 && char.IsLowSurrogate(oldText[oldText.Length - suffix])) suffix--;
        return new Edit(prefix, oldText.Substring(prefix, oldText.Length - prefix - suffix), newText.Substring(prefix, newText.Length - prefix - suffix), now);
    }

    private bool TryMerge(Edit previous, Edit next, double now, out int weightDelta)
    {
        weightDelta = 0;
        if (now - previous.Time > _coalesceSeconds || now < previous.Time) return false;
        bool typing = previous.Removed.Length == 0 && next.Removed.Length == 0 && next.Index == previous.Index + previous.Inserted.Length;
        bool backspace = previous.Inserted.Length == 0 && next.Inserted.Length == 0 && next.Index + next.Removed.Length == previous.Index;
        bool forward = previous.Inserted.Length == 0 && next.Inserted.Length == 0 && next.Index == previous.Index;
        if (!typing && !backspace && !forward) return false;
        int before = previous.Weight;
        if (typing) previous.Inserted += next.Inserted;
        else if (backspace) { previous.Index = next.Index; previous.Removed = next.Removed + previous.Removed; }
        else previous.Removed += next.Removed;
        previous.Time = now; weightDelta = previous.Weight - before;
        return true;
    }

    private void Trim()
    {
        while (_undo.Count > 1 && (_undo.Count > _maxEntries || _chars > _maxChars)) { _chars -= _undo[0].Weight; _undo.RemoveAt(0); }
        while (_redo.Count > 1 && (_redo.Count > _maxEntries || _chars > _maxChars)) { _chars -= _redo[0].Weight; _redo.RemoveAt(0); }
    }
}
