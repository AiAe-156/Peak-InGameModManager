#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ModConfigEnhance.Overview.Editor;

// Pure document model. No Unity types so it can be unit-tested outside the game.

internal enum Mark { Bold, Italic, Underline, Strike }

internal sealed class TextStyle : IEquatable<TextStyle>
{
    public static readonly TextStyle Plain = new();
    public readonly bool Bold, Italic, Underline, Strike;
    public readonly string Color, Highlight, Font, Link;
    public readonly int Size;

    public TextStyle(bool bold = false, bool italic = false, bool underline = false, bool strike = false,
        string? color = null, string? highlight = null, int size = 0, string? font = null, string? link = null)
    {
        Bold = bold; Italic = italic; Underline = underline; Strike = strike;
        Color = color ?? ""; Highlight = highlight ?? ""; Size = Math.Max(0, size); Font = font ?? ""; Link = link ?? "";
    }

    public bool Has(Mark mark) => mark switch { Mark.Bold => Bold, Mark.Italic => Italic, Mark.Underline => Underline, _ => Strike };
    public TextStyle With(Mark mark, bool on) => new(mark == Mark.Bold ? on : Bold, mark == Mark.Italic ? on : Italic,
        mark == Mark.Underline ? on : Underline, mark == Mark.Strike ? on : Strike, Color, Highlight, Size, Font, Link);
    public TextStyle WithColor(string value) => new(Bold, Italic, Underline, Strike, value, Highlight, Size, Font, Link);
    public TextStyle WithHighlight(string value) => new(Bold, Italic, Underline, Strike, Color, value, Size, Font, Link);
    public TextStyle WithSize(int value) => new(Bold, Italic, Underline, Strike, Color, Highlight, value, Font, Link);
    public TextStyle WithFont(string value) => new(Bold, Italic, Underline, Strike, Color, Highlight, Size, value, Link);
    public TextStyle WithLink(string value) => new(Bold, Italic, Underline, Strike, Color, Highlight, Size, Font, value);
    public bool IsPlain => Equals(Plain);

    public bool Equals(TextStyle? other) => other is not null && Bold == other.Bold && Italic == other.Italic && Underline == other.Underline
        && Strike == other.Strike && Size == other.Size && Color == other.Color && Highlight == other.Highlight && Font == other.Font && Link == other.Link;
    public override bool Equals(object? obj) => obj is TextStyle other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (Bold ? 1 : 0) | (Italic ? 2 : 0) | (Underline ? 4 : 0) | (Strike ? 8 : 0);
            hash = hash * 397 ^ Size; hash = hash * 397 ^ Color.GetHashCode(); hash = hash * 397 ^ Highlight.GetHashCode();
            hash = hash * 397 ^ Font.GetHashCode(); hash = hash * 397 ^ Link.GetHashCode(); return hash;
        }
    }
}

internal enum ParaStyle { Normal, Heading1, Heading2, Heading3, Quote, Callout, Bullet, Numbered }
internal enum MediaKind { Image, Gif, Video }

internal abstract class DocBlock
{
    public abstract DocBlock Clone();
}

internal sealed class ParagraphBlock : DocBlock
{
    public ParaStyle Style;
    public string Align = "left";
    public int Indent;
    public string Background = "";
    public string AnchorId = "";
    private readonly StringBuilder _text = new();
    // One style per UTF-16 unit; adjacent equal styles are grouped into runs only for output.
    private readonly List<TextStyle> _styles = new();

    public string Text => _text.ToString();
    public int Length => _text.Length;
    public char this[int index] => _text[index];
    public TextStyle StyleAt(int index) => _styles[index];
    public bool IsList => Style == ParaStyle.Bullet || Style == ParaStyle.Numbered;

    public ParagraphBlock() { }
    public ParagraphBlock(string text, TextStyle? style = null) { Insert(0, text, style ?? TextStyle.Plain); }

    public void Insert(int at, string text, TextStyle style)
    {
        at = Math.Max(0, Math.Min(Length, at));
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        _text.Insert(at, text); _styles.InsertRange(at, Enumerable.Repeat(style, text.Length));
    }
    public void InsertRuns(int at, IEnumerable<(string Text, TextStyle Style)> runs)
    {
        foreach (var run in runs) { Insert(at, run.Text, run.Style); at += run.Text.Length; }
    }
    public void Delete(int start, int end)
    {
        start = Math.Max(0, Math.Min(Length, start)); end = Math.Max(start, Math.Min(Length, end));
        _text.Remove(start, end - start); _styles.RemoveRange(start, end - start);
    }
    public void Restyle(int start, int end, Func<TextStyle, TextStyle> change)
    {
        start = Math.Max(0, start); end = Math.Min(Length, end);
        for (int i = start; i < end; i++) _styles[i] = change(_styles[i]);
    }
    public ParagraphBlock CopyAttributes() => new() { Style = Style, Align = Align, Indent = Indent, Background = Background, AnchorId = AnchorId };
    public ParagraphBlock Slice(int start, int end)
    {
        var copy = CopyAttributes(); start = Math.Max(0, Math.Min(Length, start)); end = Math.Max(start, Math.Min(Length, end));
        copy._text.Append(_text.ToString(start, end - start)); copy._styles.AddRange(_styles.GetRange(start, end - start)); return copy;
    }
    public void Append(ParagraphBlock other) { _text.Append(other._text); _styles.AddRange(other._styles); }
    public IEnumerable<(string Text, TextStyle Style)> Runs()
    {
        int start = 0;
        for (int i = 1; i <= Length; i++)
            if (i == Length || !_styles[i].Equals(_styles[start])) { yield return (_text.ToString(start, i - start), _styles[start]); start = i; }
    }
    public override DocBlock Clone() => Slice(0, Length);
}

internal sealed class MediaBlock : DocBlock
{
    public MediaKind Kind;
    public string Source = "";
    public int Width = 100;
    public string Align = "center";
    public string Caption = "";
    public override DocBlock Clone() => new MediaBlock { Kind = Kind, Source = Source, Width = Width, Align = Align, Caption = Caption };
    public static MediaKind KindFromPath(string path)
    {
        string lower = path.Split('?')[0].ToLowerInvariant();
        if (lower.EndsWith(".gif")) return MediaKind.Gif;
        foreach (string ext in new[] { ".mp4", ".webm", ".mov", ".m4v", ".avi", ".wmv", ".ogv" }) if (lower.EndsWith(ext)) return MediaKind.Video;
        return MediaKind.Image;
    }
}

internal sealed class DividerBlock : DocBlock
{
    public override DocBlock Clone() => new DividerBlock();
}

// Side-by-side media. Cell widths are page percentages; the layout scales them down when they exceed the row.
internal sealed class RowBlock : DocBlock
{
    public readonly List<MediaBlock> Cells = new();
    public string Align = "center";
    public override DocBlock Clone() { var row = new RowBlock { Align = Align }; row.Cells.AddRange(Cells.Select(c => (MediaBlock)c.Clone())); return row; }
}

internal readonly struct DocPos : IComparable<DocPos>, IEquatable<DocPos>
{
    public readonly int Block, Offset;
    public DocPos(int block, int offset) { Block = block; Offset = offset; }
    public int CompareTo(DocPos other) => Block != other.Block ? Block.CompareTo(other.Block) : Offset.CompareTo(other.Offset);
    public bool Equals(DocPos other) => Block == other.Block && Offset == other.Offset;
    public override bool Equals(object? obj) => obj is DocPos other && Equals(other);
    public override int GetHashCode() => Block * 7919 + Offset;
    public static bool operator <(DocPos a, DocPos b) => a.CompareTo(b) < 0;
    public static bool operator >(DocPos a, DocPos b) => a.CompareTo(b) > 0;
    public static bool operator <=(DocPos a, DocPos b) => a.CompareTo(b) <= 0;
    public static bool operator >=(DocPos a, DocPos b) => a.CompareTo(b) >= 0;
    public override string ToString() => Block + ":" + Offset;
}

/// <summary>A media, divider or row cell chosen as an object instead of text.</summary>
internal readonly struct DocObject : IEquatable<DocObject>
{
    public static readonly DocObject None = new(-1, -1);
    public readonly int Block, Cell;
    public DocObject(int block, int cell = -1) { Block = block; Cell = cell; }
    public bool IsNone => Block < 0;
    public bool Equals(DocObject other) => Block == other.Block && Cell == other.Cell;
    public override bool Equals(object? obj) => obj is DocObject other && Equals(other);
    public override int GetHashCode() => Block * 31 + Cell;
}

internal struct DocSelection
{
    public DocPos Anchor, Focus;
    public DocObject Object;
    public DocSelection(DocPos caret) { Anchor = Focus = caret; Object = DocObject.None; }
    public DocSelection(DocPos anchor, DocPos focus) { Anchor = anchor; Focus = focus; Object = DocObject.None; }
    public bool Collapsed => Anchor.Equals(Focus);
    public DocPos Start => Anchor <= Focus ? Anchor : Focus;
    public DocPos End => Anchor <= Focus ? Focus : Anchor;
}

internal sealed class Document
{
    public readonly List<DocBlock> Blocks = new();
    public Document Clone() { var copy = new Document(); copy.Blocks.AddRange(Blocks.Select(b => b.Clone())); return copy; }

    /// <summary>Editing needs a text position before and after every object.</summary>
    public void Normalize()
    {
        for (int i = 0; i < Blocks.Count; i++)
            if (Blocks[i] is RowBlock row)
            {
                if (row.Cells.Count == 0) { Blocks.RemoveAt(i--); continue; }
                if (row.Cells.Count == 1) { var only = row.Cells[0]; only.Align = row.Align; Blocks[i] = only; }
            }
        if (Blocks.Count == 0 || Blocks[Blocks.Count - 1] is not ParagraphBlock) Blocks.Add(new ParagraphBlock());
    }

    public IEnumerable<MediaBlock> AllMedia()
    {
        foreach (var block in Blocks)
        {
            if (block is MediaBlock media) yield return media;
            else if (block is RowBlock row) foreach (var cell in row.Cells) yield return cell;
        }
    }

    public IEnumerable<string> FontIds() => Blocks.OfType<ParagraphBlock>().SelectMany(p => Enumerable.Range(0, p.Length).Select(p.StyleAt))
        .Select(s => s.Font).Where(f => f.Length > 0).Distinct(StringComparer.Ordinal);

    public string PlainText() => string.Join("\n", Blocks.OfType<ParagraphBlock>().Select(p => p.Text));
}
