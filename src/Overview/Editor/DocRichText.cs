#nullable enable
using System;
using System.Globalization;
using System.Text;

namespace ModConfigEnhance.Overview.Editor;

/// <summary>
/// Turns a paragraph into TMP rich text and records, for every source character of that string,
/// which model offset it displays. TMP reports <c>characterInfo[i].index</c> as the source index,
/// so caret placement and hit testing never have to parse tags back.
/// </summary>
internal static class DocRichText
{
    internal const char Sentinel = '\u200B';
    internal const int CompositionChar = -2;
    internal const string LinkColor = "#6CB7F0";

    internal readonly struct Result
    {
        public readonly string Rich;
        /// <summary>Per character of <see cref="Rich"/>: model offset, -1 for markup, <see cref="CompositionChar"/> for IME text.</summary>
        public readonly int[] SourceToModel;
        public Result(string rich, int[] map) { Rich = rich; SourceToModel = map; }
    }

    /// <param name="fontName">Maps a stable font id to the TMP font asset name for <c>&lt;font&gt;</c>; empty to skip.</param>
    public static Result Build(ParagraphBlock p, Func<string, string> fontName, int compositionAt = -1, string composition = "", TextStyle? compositionStyle = null)
    {
        var rich = new StringBuilder(p.Length * 2 + 16);
        var map = new System.Collections.Generic.List<int>(p.Length * 2 + 16);
        void Markup(string tag) { rich.Append(tag); for (int i = 0; i < tag.Length; i++) map.Add(-1); }
        void Char(char c, int model)
        {
            if (c == '<') { Markup("<noparse>"); rich.Append('<'); map.Add(model); Markup("</noparse>"); return; }
            if (c == '\u000B') c = '\n';
            rich.Append(c); map.Add(model);
        }
        void Composition()
        {
            if (compositionAt < 0 || composition.Length == 0) return;
            Open(compositionStyle ?? TextStyle.Plain, Markup, fontName); Markup("<u>");
            foreach (char c in composition) { if (c == '<') { Markup("<noparse>"); rich.Append(c); map.Add(CompositionChar); Markup("</noparse>"); } else { rich.Append(c); map.Add(CompositionChar); } }
            Markup("</u>"); Close(compositionStyle ?? TextStyle.Plain, Markup, fontName);
        }

        int offset = 0;
        foreach (var (text, style) in p.Runs())
        {
            Open(style, Markup, fontName);
            for (int i = 0; i < text.Length; i++, offset++)
            {
                if (offset == compositionAt) { Close(style, Markup, fontName); Composition(); Open(style, Markup, fontName); }
                Char(text[i], offset);
            }
            Close(style, Markup, fontName);
        }
        if (compositionAt >= p.Length) Composition();
        // The sentinel gives an empty paragraph a line, and the end of text a caret position.
        rich.Append(Sentinel); map.Add(p.Length);
        return new Result(rich.ToString(), map.ToArray());
    }

    private static void Open(TextStyle s, Action<string> markup, Func<string, string> fontName)
    {
        if (s.Link.Length != 0) markup("<link=\"" + s.Link.Replace("\"", "%22") + "\">");
        if (s.Font.Length != 0) { string name = fontName(s.Font); if (name.Length != 0) markup("<font=\"" + name + "\">"); }
        if (s.Size != 0) markup("<size=" + s.Size.ToString(CultureInfo.InvariantCulture) + ">");
        if (s.Color.Length != 0) markup("<color=" + s.Color + ">"); else if (s.Link.Length != 0) markup("<color=" + LinkColor + ">");
        if (s.Highlight.Length != 0) markup("<mark=" + (s.Highlight.Length == 7 ? s.Highlight + "66" : s.Highlight) + ">");
        if (s.Bold) markup("<b>");
        if (s.Italic) markup("<i>");
        if (s.Underline || s.Link.Length != 0) markup("<u>");
        if (s.Strike) markup("<s>");
    }

    private static void Close(TextStyle s, Action<string> markup, Func<string, string> fontName)
    {
        if (s.Strike) markup("</s>");
        if (s.Underline || s.Link.Length != 0) markup("</u>");
        if (s.Italic) markup("</i>");
        if (s.Bold) markup("</b>");
        if (s.Highlight.Length != 0) markup("</mark>");
        if (s.Color.Length != 0 || s.Link.Length != 0) markup("</color>");
        if (s.Size != 0) markup("</size>");
        if (s.Font.Length != 0 && fontName(s.Font).Length != 0) markup("</font>");
        if (s.Link.Length != 0) markup("</link>");
    }
}
