#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview.Editor;

internal sealed class DocFragment
{
    public readonly List<DocBlock> Blocks = new();
    public string PlainText = "";
}

internal enum DropPlacement { Before, After, LeftOf, RightOf }

/// <summary>
/// All document mutations go through here. Each command records an undo snapshot first; typing
/// within a short interval is coalesced into one step. Positions always point into paragraphs;
/// media, dividers and rows are selected as objects.
/// </summary>
internal sealed class DocEditor
{
    private sealed class Snapshot { public Document Doc = null!; public DocSelection Selection; }
    private readonly List<Snapshot> _undo = new(), _redo = new();
    private readonly Func<double> _clock;
    private string _lastKind = "";
    private double _lastTime = -10;
    private const int HistoryLimit = 200;

    public Document Doc { get; private set; }
    public DocSelection Selection;
    /// <summary>Style for the next typed text at a collapsed caret (Word-like toggles without a selection).</summary>
    public TextStyle? Pending;
    public int Revision { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public DocEditor(Document document, Func<double> clock) { _clock = clock; Doc = document; Doc.Normalize(); Selection = new DocSelection(FirstPos()); }

    public void Reset(Document document)
    {
        Doc = document; Doc.Normalize(); _undo.Clear(); _redo.Clear(); _lastKind = ""; Pending = null;
        Selection = new DocSelection(FirstPos()); Revision++;
    }

    /// <summary>Replaces the whole document as one undoable step (source edits, demo, imports).</summary>
    public void ReplaceDocument(Document document)
    {
        Checkpoint("structure");
        Doc = document; Doc.Normalize(); Selection = new DocSelection(FirstPos()); Pending = null; Touch();
    }

    // ---------------- history ----------------

    /// <summary>Records an undo point unless this continues the same kind of typing.</summary>
    public void Checkpoint(string kind)
    {
        double now = _clock();
        bool coalesce = kind == "typing" && _lastKind == "typing" && now - _lastTime < 1.0;
        _lastKind = kind; _lastTime = now;
        if (coalesce) return;
        _undo.Add(new Snapshot { Doc = Doc.Clone(), Selection = Selection });
        if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
        _redo.Clear();
    }
    public void BreakCoalescing() => _lastKind = "";

    public bool Undo()
    {
        if (_undo.Count == 0) return false;
        _redo.Add(new Snapshot { Doc = Doc.Clone(), Selection = Selection });
        Restore(_undo[_undo.Count - 1]); _undo.RemoveAt(_undo.Count - 1); return true;
    }
    public bool Redo()
    {
        if (_redo.Count == 0) return false;
        _undo.Add(new Snapshot { Doc = Doc.Clone(), Selection = Selection });
        Restore(_redo[_redo.Count - 1]); _redo.RemoveAt(_redo.Count - 1); return true;
    }
    private void Restore(Snapshot snapshot)
    {
        Doc = snapshot.Doc.Clone(); Doc.Normalize(); Selection = snapshot.Selection; Pending = null; _lastKind = "";
        Selection.Anchor = Clamp(Selection.Anchor); Selection.Focus = Clamp(Selection.Focus);
        if (!Selection.Object.IsNone && !IsObject(Selection.Object)) Selection.Object = DocObject.None;
        Touch();
    }
    private void Touch() => Revision++;

    // ---------------- positions ----------------

    public ParagraphBlock? Para(int block) => block >= 0 && block < Doc.Blocks.Count ? Doc.Blocks[block] as ParagraphBlock : null;
    public bool IsObject(DocObject o)
    {
        if (o.Block < 0 || o.Block >= Doc.Blocks.Count) return false;
        var block = Doc.Blocks[o.Block];
        return block is RowBlock row ? o.Cell >= 0 && o.Cell < row.Cells.Count : block is not ParagraphBlock && o.Cell < 0;
    }
    public MediaBlock? MediaOf(DocObject o)
    {
        if (!IsObject(o)) return null;
        var block = Doc.Blocks[o.Block];
        return block is RowBlock row ? row.Cells[o.Cell] : block as MediaBlock;
    }

    public DocPos FirstPos() { for (int i = 0; i < Doc.Blocks.Count; i++) if (Doc.Blocks[i] is ParagraphBlock) return new DocPos(i, 0); return new DocPos(0, 0); }
    public DocPos LastPos() { for (int i = Doc.Blocks.Count - 1; i >= 0; i--) if (Doc.Blocks[i] is ParagraphBlock p) return new DocPos(i, p.Length); return new DocPos(0, 0); }
    public DocPos Clamp(DocPos pos)
    {
        if (Doc.Blocks.Count == 0) return new DocPos(0, 0);
        int block = Math.Max(0, Math.Min(Doc.Blocks.Count - 1, pos.Block));
        if (Para(block) is ParagraphBlock p) return new DocPos(block, Math.Max(0, Math.Min(p.Length, pos.Offset)));
        var next = NextParagraph(block); if (next != null) return new DocPos(next.Value, 0);
        var previous = PreviousParagraph(block); return previous != null ? new DocPos(previous.Value, Para(previous.Value)!.Length) : FirstPos();
    }
    public int? NextParagraph(int block) { for (int i = block + 1; i < Doc.Blocks.Count; i++) if (Doc.Blocks[i] is ParagraphBlock) return i; return null; }
    public int? PreviousParagraph(int block) { for (int i = block - 1; i >= 0; i--) if (Doc.Blocks[i] is ParagraphBlock) return i; return null; }

    public DocPos MoveHorizontal(DocPos pos, int direction, bool word)
    {
        var p = Para(pos.Block); if (p == null) return Clamp(pos);
        if (direction < 0)
        {
            if (pos.Offset == 0) { var previous = PreviousParagraph(pos.Block); return previous == null ? pos : new DocPos(previous.Value, Para(previous.Value)!.Length); }
            int offset = word ? WordStart(p, pos.Offset) : pos.Offset - 1;
            if (offset > 0 && char.IsLowSurrogate(p[offset]) && char.IsHighSurrogate(p[offset - 1])) offset--;
            return new DocPos(pos.Block, offset);
        }
        if (pos.Offset >= p.Length) { var next = NextParagraph(pos.Block); return next == null ? pos : new DocPos(next.Value, 0); }
        int forward = word ? WordEnd(p, pos.Offset) : pos.Offset + 1;
        if (forward < p.Length && char.IsLowSurrogate(p[forward]) && char.IsHighSurrogate(p[forward - 1])) forward++;
        return new DocPos(pos.Block, forward);
    }

    private static int Class(char c) => char.IsWhiteSpace(c) ? 0 : char.IsLetterOrDigit(c) || c == '_' ? 1 : 2;
    private static int WordStart(ParagraphBlock p, int offset)
    {
        int i = offset; while (i > 0 && Class(p[i - 1]) == 0) i--;
        if (i == 0) return 0; int kind = Class(p[i - 1]); while (i > 0 && Class(p[i - 1]) == kind) i--; return i;
    }
    private static int WordEnd(ParagraphBlock p, int offset)
    {
        int i = offset; if (i >= p.Length) return p.Length; int kind = Class(p[i]);
        while (i < p.Length && Class(p[i]) == kind) i++;
        while (i < p.Length && Class(p[i]) == 0) i++; return i;
    }
    public (int Start, int End) WordAt(DocPos pos)
    {
        var p = Para(pos.Block); if (p == null || p.Length == 0) return (0, 0);
        int at = Math.Min(pos.Offset, p.Length - 1); int kind = Class(p[at]);
        int start = at, end = at + 1;
        while (start > 0 && Class(p[start - 1]) == kind && p[start - 1] != '\n') start--;
        while (end < p.Length && Class(p[end]) == kind && p[end] != '\n') end++;
        return (start, end);
    }

    public void SetCaret(DocPos pos, bool extend)
    {
        pos = Clamp(pos);
        Selection = extend && Selection.Object.IsNone ? new DocSelection(Selection.Anchor, pos) : new DocSelection(pos);
        Pending = null; _lastKind = "";
    }
    public void SelectRange(DocPos anchor, DocPos focus) { Selection = new DocSelection(Clamp(anchor), Clamp(focus)); Pending = null; _lastKind = ""; }
    public void SelectObject(DocObject o)
    {
        if (!IsObject(o)) return;
        var caret = Clamp(new DocPos(o.Block, 0));
        Selection = new DocSelection(caret) { Object = o }; Pending = null; _lastKind = "";
    }
    public void SelectAll() => SelectRange(FirstPos(), LastPos());

    // ---------------- text editing ----------------

    public TextStyle StyleAtCaret()
    {
        if (Pending != null && Selection.Collapsed) return Pending;
        var pos = Selection.Start; var p = Para(pos.Block);
        if (p == null || p.Length == 0) return Pending ?? TextStyle.Plain;
        if (!Selection.Collapsed) return p.StyleAt(Math.Min(pos.Offset, p.Length - 1));
        var style = p.StyleAt(pos.Offset > 0 ? pos.Offset - 1 : 0);
        // Typing after a link should not extend the link.
        return pos.Offset >= p.Length && style.Link.Length != 0 ? style.WithLink("") : style;
    }

    /// <summary>Deletes the selected range or object without recording history.</summary>
    private bool DeleteSelectionRaw()
    {
        if (!Selection.Object.IsNone) { RemoveObjectRaw(Selection.Object); return true; }
        if (Selection.Collapsed) return false;
        var start = Selection.Start; var end = Selection.End;
        var first = Para(start.Block)!; var last = Para(end.Block)!;
        if (start.Block == end.Block) first.Delete(start.Offset, end.Offset);
        else
        {
            var tail = last.Slice(end.Offset, last.Length);
            first.Delete(start.Offset, first.Length); first.Append(tail);
            Doc.Blocks.RemoveRange(start.Block + 1, end.Block - start.Block);
        }
        Selection = new DocSelection(start); Touch(); return true;
    }

    private void RemoveObjectRaw(DocObject o)
    {
        if (!IsObject(o)) return;
        var block = Doc.Blocks[o.Block];
        if (block is RowBlock row) { row.Cells.RemoveAt(o.Cell); }
        else Doc.Blocks.RemoveAt(o.Block);
        Doc.Normalize();
        Selection = new DocSelection(Clamp(new DocPos(o.Block, 0))); Touch();
    }

    public void InsertText(string text, string kind = "typing")
    {
        if (text.Length == 0) return;
        if (!Selection.Object.IsNone) { if (!InsertParagraphAfterObject()) return; }
        Checkpoint(kind);
        var style = StyleAtCaret();
        DeleteSelectionRaw();
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    ");
        string[] lines = text.Split('\n');
        var pos = Selection.Focus; var p = Para(pos.Block)!;
        p.Insert(pos.Offset, lines[0], style); pos = new DocPos(pos.Block, pos.Offset + lines[0].Length);
        for (int i = 1; i < lines.Length; i++)
        {
            var tail = SplitRaw(pos); tail.Insert(0, lines[i], style);
            pos = new DocPos(pos.Block + 1, lines[i].Length);
        }
        Selection = new DocSelection(pos); Pending = style.Equals(StyleAtCaretRaw(pos)) ? null : style; Touch();
        if (kind == "typing" && text == " ") TryMarkdownShortcut();
    }

    private TextStyle StyleAtCaretRaw(DocPos pos) { var p = Para(pos.Block); return p == null || pos.Offset == 0 || p.Length == 0 ? TextStyle.Plain : p.StyleAt(pos.Offset - 1); }

    private bool InsertParagraphAfterObject()
    {
        var o = Selection.Object; if (!IsObject(o)) { Selection.Object = DocObject.None; return true; }
        Checkpoint("structure");
        int at = o.Block + 1;
        if (Para(at) is not ParagraphBlock) Doc.Blocks.Insert(at, new ParagraphBlock());
        Selection = new DocSelection(new DocPos(at, 0)); Touch(); return true;
    }

    // Typing "# " / "- " / "1. " / "> " at the start of a plain paragraph converts it, like Word and Notion.
    private void TryMarkdownShortcut()
    {
        var pos = Selection.Focus; var p = Para(pos.Block);
        if (p == null || p.Style != ParaStyle.Normal) return;
        string prefix = p.Text.Substring(0, pos.Offset);
        // Chinese IMEs often produce full-width punctuation for the same keys.
        string normalized = prefix.Replace('＃', '#').Replace('－', '-').Replace('＞', '>').Replace('》', '>').Replace('．', '.').Replace('。', '.');
        ParaStyle? style = normalized switch
        {
            "# " => ParaStyle.Heading1, "## " => ParaStyle.Heading2, "### " => ParaStyle.Heading3,
            "- " or "* " or "+ " => ParaStyle.Bullet, "1. " => ParaStyle.Numbered, "> " => ParaStyle.Quote, _ => null
        };
        if (style == null) return;
        Checkpoint("format");
        p.Delete(0, prefix.Length); p.Style = style.Value;
        Selection = new DocSelection(new DocPos(pos.Block, 0)); Touch();
    }

    /// <summary>Splits the paragraph at pos, returns the new second half (inserted after).</summary>
    private ParagraphBlock SplitRaw(DocPos pos)
    {
        var p = Para(pos.Block)!;
        var tail = p.Slice(pos.Offset, p.Length); p.Delete(pos.Offset, p.Length);
        if (pos.Offset == 0) p.AnchorId = ""; else tail.AnchorId = "";
        Doc.Blocks.Insert(pos.Block + 1, tail); return tail;
    }

    public void SplitParagraph()
    {
        if (!Selection.Object.IsNone) { InsertParagraphAfterObject(); return; }
        Checkpoint("structure");
        DeleteSelectionRaw();
        var pos = Selection.Focus; var p = Para(pos.Block)!;
        if (p.Length == 0 && p.Style != ParaStyle.Normal && p.Style != ParaStyle.Heading1 && p.Style != ParaStyle.Heading2 && p.Style != ParaStyle.Heading3)
        {
            // Enter on an empty list item, quote or callout leaves that structure.
            p.Style = ParaStyle.Normal; if (p.Indent > 0 && p.Background.Length == 0) p.Indent = Math.Max(0, p.Indent - 1); Touch(); return;
        }
        if (p.Style == ParaStyle.Normal && p.Text == "---")
        {
            Doc.Blocks[pos.Block] = new DividerBlock();
            var after = new ParagraphBlock { Align = p.Align }; Doc.Blocks.Insert(pos.Block + 1, after);
            Selection = new DocSelection(new DocPos(pos.Block + 1, 0)); Touch(); return;
        }
        var carry = StyleAtCaret();
        var tail = SplitRaw(pos);
        if ((p.Style == ParaStyle.Heading1 || p.Style == ParaStyle.Heading2 || p.Style == ParaStyle.Heading3) && tail.Length == 0) tail.Style = ParaStyle.Normal;
        Selection = new DocSelection(new DocPos(pos.Block + 1, 0));
        Pending = tail.Length == 0 && !carry.IsPlain ? carry.WithLink("") : null;
        Touch();
    }

    /// <summary>Shift+Enter: a line break inside the same paragraph.</summary>
    public void InsertSoftBreak()
    {
        if (!Selection.Object.IsNone) { InsertParagraphAfterObject(); return; }
        Checkpoint("typing");
        var style = StyleAtCaret(); DeleteSelectionRaw();
        var pos = Selection.Focus; Para(pos.Block)!.Insert(pos.Offset, "\n", style);
        Selection = new DocSelection(new DocPos(pos.Block, pos.Offset + 1)); Touch();
    }

    public void DeleteBackward(bool word)
    {
        if (!Selection.Object.IsNone || !Selection.Collapsed) { Checkpoint("delete"); DeleteSelectionRaw(); return; }
        var pos = Selection.Focus; var p = Para(pos.Block)!;
        if (pos.Offset > 0)
        {
            Checkpoint(word ? "delete" : "typing");
            var to = MoveHorizontal(pos, -1, word);
            p.Delete(to.Offset, pos.Offset); Selection = new DocSelection(to); Touch(); return;
        }
        if (p.Style != ParaStyle.Normal && p.Style != ParaStyle.Heading1 && p.Style != ParaStyle.Heading2 && p.Style != ParaStyle.Heading3)
        { Checkpoint("format"); p.Style = ParaStyle.Normal; Touch(); return; }
        if (p.Indent > 0) { Checkpoint("format"); p.Indent--; Touch(); return; }
        if (pos.Block == 0) return;
        var previous = Doc.Blocks[pos.Block - 1];
        if (previous is ParagraphBlock before)
        {
            Checkpoint("structure");
            int at = before.Length; before.Append(p); Doc.Blocks.RemoveAt(pos.Block);
            Selection = new DocSelection(new DocPos(pos.Block - 1, at)); Touch(); return;
        }
        // Backspace after an object first selects it (dropping an empty line); a second press deletes it.
        if (p.Length == 0 && pos.Block + 1 < Doc.Blocks.Count) { Checkpoint("structure"); Doc.Blocks.RemoveAt(pos.Block); Touch(); }
        SelectObject(previous is RowBlock row ? new DocObject(pos.Block - 1, row.Cells.Count - 1) : new DocObject(pos.Block - 1));
    }

    public void DeleteForward(bool word)
    {
        if (!Selection.Object.IsNone || !Selection.Collapsed) { Checkpoint("delete"); DeleteSelectionRaw(); return; }
        var pos = Selection.Focus; var p = Para(pos.Block)!;
        if (pos.Offset < p.Length)
        {
            Checkpoint(word ? "delete" : "typing");
            var to = MoveHorizontal(pos, 1, word);
            p.Delete(pos.Offset, to.Offset); Touch(); return;
        }
        if (pos.Block + 1 >= Doc.Blocks.Count) return;
        var next = Doc.Blocks[pos.Block + 1];
        if (next is ParagraphBlock after)
        {
            Checkpoint("structure"); p.Append(after); Doc.Blocks.RemoveAt(pos.Block + 1); Touch(); return;
        }
        SelectObject(next is RowBlock ? new DocObject(pos.Block + 1, 0) : new DocObject(pos.Block + 1));
    }

    public void DeleteObject() { if (Selection.Object.IsNone) return; Checkpoint("structure"); RemoveObjectRaw(Selection.Object); }

    // ---------------- character styles ----------------

    private IEnumerable<(ParagraphBlock P, int Start, int End)> SelectedSpans()
    {
        var start = Selection.Start; var end = Selection.End;
        for (int b = start.Block; b <= end.Block && b < Doc.Blocks.Count; b++)
        {
            if (Para(b) is not ParagraphBlock p) continue;
            int s = b == start.Block ? start.Offset : 0, e = b == end.Block ? end.Offset : p.Length;
            if (e > s) yield return (p, s, e);
        }
    }

    public bool AllHave(Func<TextStyle, bool> test)
    {
        if (Selection.Collapsed) return test(StyleAtCaret());
        bool any = false;
        foreach (var (p, s, e) in SelectedSpans()) for (int i = s; i < e; i++) { any = true; if (!test(p.StyleAt(i))) return false; }
        return any;
    }

    public void ToggleMark(Mark mark)
    {
        bool on = !AllHave(s => s.Has(mark));
        ApplyStyle(s => s.With(mark, on));
    }

    public void ApplyStyle(Func<TextStyle, TextStyle> change)
    {
        if (!Selection.Object.IsNone) return;
        if (Selection.Collapsed)
        {
            Pending = change(StyleAtCaret()); _lastKind = ""; Touch(); return;
        }
        Checkpoint("format");
        foreach (var (p, s, e) in SelectedSpans().ToList()) p.Restyle(s, e, change);
        Touch();
    }

    /// <summary>Range of the link under the caret, for editing an existing link without a selection.</summary>
    public (DocPos Start, DocPos End)? LinkRangeAtCaret()
    {
        var pos = Selection.Focus; var p = Para(pos.Block); if (p == null || p.Length == 0) return null;
        int at = Math.Min(pos.Offset, p.Length - 1); string link = p.StyleAt(at).Link;
        if (link.Length == 0 && pos.Offset > 0) { at = pos.Offset - 1; link = p.StyleAt(at).Link; }
        if (link.Length == 0) return null;
        int s = at, e = at + 1;
        while (s > 0 && p.StyleAt(s - 1).Link == link) s--;
        while (e < p.Length && p.StyleAt(e).Link == link) e++;
        return (new DocPos(pos.Block, s), new DocPos(pos.Block, e));
    }

    public void InsertLink(string text, string url)
    {
        if (text.Length == 0) text = url;
        Checkpoint("format");
        if (!Selection.Object.IsNone) InsertParagraphAfterObject();
        DeleteSelectionRaw();
        var pos = Selection.Focus; var p = Para(pos.Block)!; var style = StyleAtCaret().WithLink(url);
        p.Insert(pos.Offset, text, style); Selection = new DocSelection(new DocPos(pos.Block, pos.Offset + text.Length)); Pending = null; Touch();
    }

    // ---------------- paragraph attributes ----------------

    private IEnumerable<ParagraphBlock> SelectedParagraphs()
    {
        if (!Selection.Object.IsNone) yield break;
        for (int b = Selection.Start.Block; b <= Selection.End.Block && b < Doc.Blocks.Count; b++) if (Para(b) is ParagraphBlock p) yield return p;
    }
    public ParagraphBlock? CurrentParagraph => Selection.Object.IsNone ? Para(Selection.Focus.Block) : null;

    public void SetParagraphStyle(ParaStyle style)
    {
        var list = SelectedParagraphs().ToList(); if (list.Count == 0) return;
        Checkpoint("format");
        bool toggleOff = (style == ParaStyle.Bullet || style == ParaStyle.Numbered || style == ParaStyle.Quote || style == ParaStyle.Callout) && list.All(p => p.Style == style);
        foreach (var p in list) p.Style = toggleOff ? ParaStyle.Normal : style;
        Touch();
    }
    public void SetAlign(string align)
    {
        if (!Selection.Object.IsNone) { SetMediaAlign(Selection.Object, align); return; }
        Checkpoint("format"); foreach (var p in SelectedParagraphs()) p.Align = align; Touch();
    }
    public void ChangeIndent(int delta)
    {
        Checkpoint("format"); foreach (var p in SelectedParagraphs()) p.Indent = Math.Max(0, Math.Min(8, p.Indent + delta)); Touch();
    }
    public void SetBackground(string color)
    {
        Checkpoint("format"); foreach (var p in SelectedParagraphs()) p.Background = color; Touch();
    }

    // ---------------- blocks and objects ----------------

    /// <summary>Inserts an object below the caret paragraph (splitting it when the caret is mid-text) and keeps a paragraph after it.</summary>
    public void InsertBlock(DocBlock block)
    {
        Checkpoint("structure");
        int at;
        if (!Selection.Object.IsNone) at = Selection.Object.Block + 1;
        else
        {
            DeleteSelectionRaw();
            var pos = Selection.Focus; var p = Para(pos.Block)!;
            if (p.Length == 0) { Doc.Blocks[pos.Block] = block; at = pos.Block + 1; goto inserted; }
            if (pos.Offset == 0) { at = pos.Block; Doc.Blocks.Insert(at, block); Selection = new DocSelection(new DocPos(at + 1, 0)); Touch(); return; }
            if (pos.Offset < p.Length) SplitRaw(pos);
            at = pos.Block + 1;
        }
        Doc.Blocks.Insert(at, block); at++;
        inserted:
        if (Para(at) is not ParagraphBlock) Doc.Blocks.Insert(at, new ParagraphBlock());
        Selection = new DocSelection(new DocPos(at, 0)); Touch();
    }

    public void SetMediaWidth(DocObject o, int width)
    {
        var media = MediaOf(o); if (media == null) return;
        width = Math.Max(10, Math.Min(100, width)); if (media.Width == width) return;
        media.Width = width; Touch();
    }
    public void SetMediaAlign(DocObject o, string align)
    {
        if (!IsObject(o)) return;
        Checkpoint("format");
        var block = Doc.Blocks[o.Block];
        if (block is RowBlock row) row.Align = align; else if (block is MediaBlock m) m.Align = align;
        Touch();
    }
    public void ReplaceMedia(DocObject o, string source, MediaKind kind)
    {
        var media = MediaOf(o); if (media == null) return;
        Checkpoint("structure"); media.Source = source; media.Kind = kind; Touch();
    }
    public void SetMediaCaption(DocObject o, string caption)
    {
        var media = MediaOf(o); if (media == null) return;
        Checkpoint("format"); media.Caption = caption.Trim(); Touch();
    }

    /// <summary>Moves a block (or a row cell) next to a target block. LeftOf/RightOf join media into a row.</summary>
    public bool MoveObject(DocObject source, int target, int targetCell, DropPlacement placement)
    {
        if (source.Block < 0 || source.Block >= Doc.Blocks.Count || target < 0 || target >= Doc.Blocks.Count) return false;
        var moving = Doc.Blocks[source.Block];
        bool sideways = placement == DropPlacement.LeftOf || placement == DropPlacement.RightOf;
        MediaBlock? cell = moving is RowBlock sourceRow && source.Cell >= 0 && source.Cell < sourceRow.Cells.Count ? sourceRow.Cells[source.Cell] : null;
        DocBlock carried = cell ?? moving;
        if (sideways && carried is not MediaBlock) return false;
        var targetBlock = Doc.Blocks[target];
        if (sideways && targetBlock is not MediaBlock && targetBlock is not RowBlock) return false;
        if (ReferenceEquals(carried, targetBlock)) return false;
        Checkpoint("structure");
        // Detach.
        if (cell != null) ((RowBlock)moving).Cells.Remove(cell); else Doc.Blocks.Remove(moving);
        int index = Doc.Blocks.IndexOf(targetBlock);
        if (index < 0) { Doc.Blocks.Add(carried); Doc.Normalize(); Touch(); return true; }
        if (!sideways)
        {
            if (carried is MediaBlock m && cell != null) m.Align = ((RowBlock)moving).Align;
            Doc.Blocks.Insert(placement == DropPlacement.After ? index + 1 : index, carried);
        }
        else
        {
            var media = (MediaBlock)carried;
            RowBlock row;
            if (targetBlock is RowBlock existing) row = existing;
            else { var only = (MediaBlock)targetBlock; row = new RowBlock { Align = only.Align }; row.Cells.Add(only); Doc.Blocks[index] = row; targetCell = 0; }
            int cellIndex = Math.Max(0, Math.Min(row.Cells.Count, (targetCell < 0 ? row.Cells.Count - 1 : targetCell) + (placement == DropPlacement.RightOf ? 1 : 0)));
            row.Cells.Insert(cellIndex, media);
            if (row.Cells.Sum(c => c.Width) > 100) { int each = Math.Max(10, 100 / row.Cells.Count); foreach (var c in row.Cells) c.Width = each; }
        }
        // Collapse rows left with one cell, then make sure text positions exist around objects.
        for (int i = 0; i < Doc.Blocks.Count; i++) if (Doc.Blocks[i] is RowBlock r && r.Cells.Count == 1) { r.Cells[0].Align = r.Align; Doc.Blocks[i] = r.Cells[0]; }
        Doc.Normalize();
        int landed = IndexOfMedia(carried);
        if (landed >= 0)
        {
            Selection = new DocSelection(Clamp(new DocPos(landed, 0)));
            Selection.Object = Doc.Blocks[landed] is RowBlock landedRow ? new DocObject(landed, landedRow.Cells.IndexOf((MediaBlock)carried)) : new DocObject(landed);
        }
        Touch(); return true;
    }

    private int IndexOfMedia(DocBlock block)
    {
        for (int i = 0; i < Doc.Blocks.Count; i++)
            if (ReferenceEquals(Doc.Blocks[i], block) || Doc.Blocks[i] is RowBlock r && r.Cells.Any(c => ReferenceEquals(c, block))) return i;
        return -1;
    }

    public bool MoveParagraphs(int from, int target, DropPlacement placement)
    {
        if (Para(from) == null || target < 0 || target >= Doc.Blocks.Count || from == target) return false;
        Checkpoint("structure");
        var block = Doc.Blocks[from]; var targetBlock = Doc.Blocks[target];
        Doc.Blocks.RemoveAt(from);
        int index = Doc.Blocks.IndexOf(targetBlock);
        Doc.Blocks.Insert(placement == DropPlacement.After ? index + 1 : index, block);
        Doc.Normalize();
        int landed = Doc.Blocks.IndexOf(block);
        Selection = new DocSelection(new DocPos(landed, Math.Min(Selection.Focus.Offset, ((ParagraphBlock)block).Length))); Touch(); return true;
    }

    // ---------------- clipboard ----------------

    public DocFragment? Copy()
    {
        var fragment = new DocFragment();
        if (!Selection.Object.IsNone)
        {
            var media = MediaOf(Selection.Object);
            var block = media != null ? media.Clone() : Doc.Blocks[Selection.Object.Block].Clone();
            fragment.Blocks.Add(block); fragment.PlainText = media != null ? media.Source : "---"; return fragment;
        }
        if (Selection.Collapsed) return null;
        var start = Selection.Start; var end = Selection.End;
        var lines = new List<string>();
        for (int b = start.Block; b <= end.Block; b++)
        {
            var block = Doc.Blocks[b];
            if (block is ParagraphBlock p)
            {
                int s = b == start.Block ? start.Offset : 0, e = b == end.Block ? end.Offset : p.Length;
                var slice = p.Slice(s, e); fragment.Blocks.Add(slice); lines.Add(slice.Text.Replace('\u000B', '\n'));
            }
            else { fragment.Blocks.Add(block.Clone()); if (block is DividerBlock) lines.Add("---"); }
        }
        fragment.PlainText = string.Join("\n", lines);
        return fragment;
    }

    public void Cut() { if (Selection.Collapsed && Selection.Object.IsNone) return; Checkpoint("delete"); DeleteSelectionRaw(); }

    public void Paste(DocFragment fragment)
    {
        if (fragment.Blocks.Count == 0) return;
        if (fragment.Blocks.Count == 1 && fragment.Blocks[0] is ParagraphBlock single)
        {
            if (!Selection.Object.IsNone) InsertParagraphAfterObject();
            Checkpoint("paste"); DeleteSelectionRaw();
            var pos = Selection.Focus; var p = Para(pos.Block)!;
            p.InsertRuns(pos.Offset, single.Runs().ToList()); Selection = new DocSelection(new DocPos(pos.Block, pos.Offset + single.Length)); Touch(); return;
        }
        Checkpoint("paste");
        if (!Selection.Object.IsNone) { int after = Selection.Object.Block + 1; Doc.Blocks.Insert(after, new ParagraphBlock()); Selection = new DocSelection(new DocPos(after, 0)); }
        DeleteSelectionRaw();
        var at = Selection.Focus; var current = Para(at.Block)!;
        var tail = SplitRaw(at);
        var blocks = fragment.Blocks.Select(b => b.Clone()).ToList();
        int insertAt = at.Block + 1;
        if (blocks[0] is ParagraphBlock head) { current.InsertRuns(current.Length, head.Runs().ToList()); if (current.Length == head.Length) { current.Style = head.Style; current.Align = head.Align; current.Indent = head.Indent; current.Background = head.Background; } blocks.RemoveAt(0); }
        ParagraphBlock? lastText = blocks.Count > 0 ? blocks[blocks.Count - 1] as ParagraphBlock : null;
        Doc.Blocks.InsertRange(insertAt, blocks);
        DocPos caret;
        if (lastText != null)
        {
            int length = lastText.Length; lastText.Append(tail); Doc.Blocks.Remove(tail);
            caret = new DocPos(Doc.Blocks.IndexOf(lastText), length);
        }
        else caret = new DocPos(Doc.Blocks.IndexOf(tail), 0);
        Doc.Normalize(); Selection = new DocSelection(Clamp(caret)); Touch();
    }
}
