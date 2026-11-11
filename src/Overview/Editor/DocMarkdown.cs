#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MdParagraph = Markdig.Syntax.ParagraphBlock;

namespace ModConfigEnhance.Overview.Editor;

/// <summary>
/// Markdown is an interchange format only. Import accepts plain Markdown and every 1.2.x introduction
/// (block comments <c>&lt;!--mce:{...}--&gt;</c> and <c>span data-mce-*</c>). Export writes standard
/// Markdown and adds the 1.2.x comment only when a block has non-default layout, so older MCE builds
/// can still show the page.
/// </summary>
internal static class DocMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseEmphasisExtras().Build();
    private static readonly Regex MetaComment = new(@"^\s*<!--mce:(.*?)-->\s*", RegexOptions.Singleline);
    private static readonly Regex SpanOpen = new("^<span\\s+data-mce-(color|size|mark|font)=\"([^\"]*)\"\\s*>$", RegexOptions.IgnoreCase);

    // ---------------- Import ----------------

    public static Document Import(string source)
    {
        var document = new Document();
        source = (source ?? "").Replace("\r\n", "\n");
        var ast = Markdown.Parse(source, Pipeline);
        JObject? pending = null;
        foreach (var node in ast)
        {
            if (node is HtmlBlock html)
            {
                string raw = Slice(source, html);
                var match = MetaComment.Match(raw);
                if (match.Success)
                {
                    try { pending = JObject.Parse(match.Groups[1].Value); } catch { pending = null; }
                    string rest = raw.Substring(match.Length).Trim();
                    if (rest.Length == 0) continue;
                    // 1.2.x wrote the comment directly above HTML-only content; keep it as text.
                    AddText(document, rest, pending); pending = null; continue;
                }
                AddText(document, raw.Trim(), pending); pending = null; continue;
            }
            ImportBlock(document, node, pending, 0, source);
            pending = null;
        }
        document.Normalize();
        return document;
    }

    private static string Slice(string source, Block node)
    {
        int start = Math.Max(0, Math.Min(source.Length, node.Span.Start)), end = Math.Max(start, Math.Min(source.Length, node.Span.End + 1));
        return source.Substring(start, end - start);
    }

    private static void ImportBlock(Document document, Block node, JObject? meta, int depth, string source)
    {
        string type = Meta(meta, "Type") ?? "text";
        switch (node)
        {
            case ThematicBreakBlock: document.Blocks.Add(new DividerBlock()); return;
            case HeadingBlock heading:
            {
                var p = Paragraph(meta); p.Style = heading.Level <= 1 ? ParaStyle.Heading1 : heading.Level == 2 ? ParaStyle.Heading2 : ParaStyle.Heading3;
                if (heading.Inline != null) AppendInlines(p, heading.Inline, TextStyle.Plain);
                var anchor = Regex.Match(p.Text, @"\s+\{#([a-z0-9-]+)\}\s*$");
                if (anchor.Success) { p.AnchorId = anchor.Groups[1].Value; p.Delete(anchor.Index, p.Length); }
                document.Blocks.Add(p); return;
            }
            case MdParagraph paragraph:
            {
                if (paragraph.Inline?.FirstChild is LinkInline image && image.IsImage && image.NextSibling == null)
                {
                    var media = new MediaBlock { Source = image.Url ?? "", Caption = PlainInline(image), Width = Clamp(MetaInt(meta, "Width") ?? 100, 10, 100),
                        Align = DocJson.Align(Meta(meta, "Align"), "center") };
                    media.Kind = type == "gif" ? MediaKind.Gif : type == "video" ? MediaKind.Video : type == "image" ? MediaKind.Image : MediaBlock.KindFromPath(media.Source);
                    AddMedia(document, media, MetaBool(meta, "InlineWithPrevious"));
                    return;
                }
                if (type == "image" || type == "gif" || type == "video")
                {
                    string path = Meta(meta, "Media") ?? "";
                    if (path.Length != 0) { AddMedia(document, new MediaBlock { Source = path, Kind = type == "gif" ? MediaKind.Gif : type == "video" ? MediaKind.Video : MediaKind.Image, Width = Clamp(MetaInt(meta, "Width") ?? 100, 10, 100), Align = DocJson.Align(Meta(meta, "Align"), "center") }, MetaBool(meta, "InlineWithPrevious")); return; }
                }
                if (type == "rule") { document.Blocks.Add(new DividerBlock()); return; }
                var p = Paragraph(meta);
                if (paragraph.Inline != null) AppendInlines(p, paragraph.Inline, TextStyle.Plain);
                // Export writes an empty paragraph as &nbsp; because Markdown has no empty paragraphs.
                if (p.Text == "\u00A0") p.Delete(0, 1);
                document.Blocks.Add(p); return;
            }
            case ListBlock list:
            {
                int number = 0;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    number++;
                    bool first = true;
                    foreach (var child in item)
                    {
                        if (child is ListBlock nested) { ImportBlock(document, nested, null, depth + 1, source); continue; }
                        if (child is MdParagraph or HeadingBlock)
                        {
                            int before = document.Blocks.Count;
                            ImportBlock(document, child, first ? meta : null, depth, source);
                            for (int i = before; i < document.Blocks.Count; i++)
                                if (document.Blocks[i] is ParagraphBlock p)
                                {
                                    p.Style = list.IsOrdered ? ParaStyle.Numbered : ParaStyle.Bullet;
                                    p.Indent = Math.Max(p.Indent, depth);
                                }
                            first = false;
                            continue;
                        }
                        ImportBlock(document, child, null, depth + 1, source);
                    }
                }
                return;
            }
            case QuoteBlock quote:
            {
                string style = Meta(meta, "style") ?? "";
                foreach (var child in quote)
                {
                    int before = document.Blocks.Count;
                    ImportBlock(document, child, meta, depth, source);
                    for (int i = before; i < document.Blocks.Count; i++)
                        if (document.Blocks[i] is ParagraphBlock p && p.Style == ParaStyle.Normal) p.Style = style == "callout" ? ParaStyle.Callout : ParaStyle.Quote;
                    meta = null;
                }
                return;
            }
            case CodeBlock code:
            {
                var p = Paragraph(meta);
                p.Insert(0, string.Join("\n", code.Lines.Lines.Take(code.Lines.Count).Select(l => l.ToString())).TrimEnd('\n'), TextStyle.Plain);
                document.Blocks.Add(p); return;
            }
            case ContainerBlock container:
                foreach (var child in container) ImportBlock(document, child, null, depth, source);
                return;
            default:
                AddText(document, Slice(source, node).Trim(), meta); return;
        }
    }

    private static void AddText(Document document, string text, JObject? meta)
    {
        if (text.Length == 0) return;
        var p = Paragraph(meta); p.Insert(0, text, TextStyle.Plain); document.Blocks.Add(p);
    }

    private static void AddMedia(Document document, MediaBlock media, bool inline)
    {
        if (media.Source.Length == 0) return;
        if (inline && document.Blocks.Count > 0)
        {
            var last = document.Blocks[document.Blocks.Count - 1];
            if (last is RowBlock row) { row.Cells.Add(media); return; }
            if (last is MediaBlock previous)
            {
                var created = new RowBlock { Align = previous.Align }; created.Cells.Add(previous); created.Cells.Add(media);
                document.Blocks[document.Blocks.Count - 1] = created; return;
            }
        }
        document.Blocks.Add(media);
    }

    private static ParagraphBlock Paragraph(JObject? meta)
    {
        var p = new ParagraphBlock { Align = DocJson.Align(Meta(meta, "Align"), "left"), Indent = Clamp(MetaInt(meta, "Indent") ?? 0, 0, 8), Background = DocJson.Color(Meta(meta, "Background")) };
        string? style = Meta(meta, "style");
        if (style != null) p.Style = DocJson.ParseStyle(style);
        return p;
    }

    private static void AppendInlines(ParagraphBlock p, ContainerInline root, TextStyle style)
    {
        var spans = new Stack<TextStyle>();
        TextStyle current = style;
        foreach (var node in root)
        {
            switch (node)
            {
                case LiteralInline literal: p.Insert(p.Length, literal.Content.ToString(), current); break;
                case LineBreakInline: p.Insert(p.Length, "\n", current); break;
                case CodeInline code: p.Insert(p.Length, code.Content, current); break;
                case EmphasisInline emphasis:
                {
                    TextStyle inner = emphasis.DelimiterChar == '~' ? current.With(Mark.Strike, true)
                        : emphasis.DelimiterCount >= 2 ? current.With(Mark.Bold, true) : current.With(Mark.Italic, true);
                    AppendInlines(p, emphasis, inner); break;
                }
                case AutolinkInline auto: p.Insert(p.Length, auto.Url, current.WithLink(auto.Url)); break;
                case LinkInline link:
                    if (link.IsImage) { string alt = PlainInline(link); p.Insert(p.Length, alt.Length > 0 ? alt : Loc.T("[Image]", "[图片]", "[圖片]"), current); }
                    else AppendInlines(p, link, current.WithLink(link.Url ?? ""));
                    break;
                case HtmlInline html:
                {
                    string tag = html.Tag.Trim();
                    var span = SpanOpen.Match(tag);
                    if (span.Success)
                    {
                        spans.Push(current);
                        string value = span.Groups[2].Value;
                        switch (span.Groups[1].Value.ToLowerInvariant())
                        {
                            case "color": current = current.WithColor(DocJson.Color(value)); break;
                            case "mark": current = current.WithHighlight(DocJson.Color(value)); break;
                            case "font": current = current.WithFont(value); break;
                            default:
                                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float size)) current = current.WithSize(Clamp((int)Math.Round(size), 0, 96));
                                break;
                        }
                        break;
                    }
                    switch (tag.ToLowerInvariant())
                    {
                        case "</span>": if (spans.Count > 0) current = spans.Pop(); break;
                        case "<u>": spans.Push(current); current = current.With(Mark.Underline, true); break;
                        case "<s>": case "<del>": spans.Push(current); current = current.With(Mark.Strike, true); break;
                        case "<b>": case "<strong>": spans.Push(current); current = current.With(Mark.Bold, true); break;
                        case "<i>": case "<em>": spans.Push(current); current = current.With(Mark.Italic, true); break;
                        case "</u>": case "</s>": case "</del>": case "</b>": case "</strong>": case "</i>": case "</em>": if (spans.Count > 0) current = spans.Pop(); break;
                        case "<br>": case "<br/>": case "<br />": p.Insert(p.Length, "\n", current); break;
                        default: p.Insert(p.Length, tag, current); break;
                    }
                    break;
                }
                case HtmlEntityInline entity: p.Insert(p.Length, entity.Transcoded.ToString(), current); break;
                case ContainerInline container: AppendInlines(p, container, current); break;
            }
        }
    }

    private static string PlainInline(ContainerInline root)
    {
        var builder = new StringBuilder();
        foreach (var node in root)
        {
            if (node is LiteralInline literal) builder.Append(literal.Content.ToString());
            else if (node is CodeInline code) builder.Append(code.Content);
            else if (node is ContainerInline child) builder.Append(PlainInline(child));
        }
        return builder.ToString();
    }

    private static string? Meta(JObject? meta, string key)
    {
        if (meta == null) return null;
        var token = meta.GetValue(key, StringComparison.OrdinalIgnoreCase);
        return token?.Type == JTokenType.String ? token.Value<string>() : token?.ToString();
    }
    private static int? MetaInt(JObject? meta, string key) => int.TryParse(Meta(meta, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;
    private static bool MetaBool(JObject? meta, string key) => string.Equals(Meta(meta, key), "true", StringComparison.OrdinalIgnoreCase);
    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

    // ---------------- Export ----------------

    public static string Export(Document document)
    {
        var parts = new List<string>();
        var numbers = new Dictionary<int, int>();
        ParaStyle? previousList = null;
        for (int index = 0; index < document.Blocks.Count; index++)
        {
            var block = document.Blocks[index];
            bool trailingEmpty = index == document.Blocks.Count - 1 && block is ParagraphBlock last && last.Length == 0;
            if (trailingEmpty) break;
            switch (block)
            {
                case ParagraphBlock p:
                {
                    if (!p.IsList) { numbers.Clear(); previousList = null; }
                    var meta = new JObject();
                    if (p.Align != "left") meta["Align"] = p.Align;
                    if (p.Background.Length != 0) meta["Background"] = p.Background;
                    if (p.Indent != 0 && !p.IsList) meta["Indent"] = p.Indent;
                    if (p.Style == ParaStyle.Callout) meta["style"] = "callout";
                    string body = Inline(p);
                    string text;
                    switch (p.Style)
                    {
                        case ParaStyle.Heading1: text = "# " + body; break;
                        case ParaStyle.Heading2: text = "## " + body; break;
                        case ParaStyle.Heading3: text = "### " + body; break;
                        case ParaStyle.Quote: case ParaStyle.Callout: text = "> " + body.Replace("\n", "\n> "); break;
                        case ParaStyle.Bullet: case ParaStyle.Numbered:
                        {
                            foreach (int deeper in numbers.Keys.Where(k => k > p.Indent).ToList()) numbers.Remove(deeper);
                            string pad = new string(' ', p.Indent * 3);
                            string marker;
                            if (p.Style == ParaStyle.Numbered) { numbers.TryGetValue(p.Indent, out int n); numbers[p.Indent] = ++n; marker = n + ". "; }
                            else { numbers.Remove(p.Indent); marker = "- "; }
                            text = pad + marker + body.Replace("\n", "\n" + pad + new string(' ', marker.Length));
                            // Consecutive list items stay in one Markdown list.
                            if (previousList != null && parts.Count > 0) { parts[parts.Count - 1] += "\n" + Prefix(meta) + text; previousList = p.Style; continue; }
                            previousList = p.Style;
                            break;
                        }
                        default: text = body.Length == 0 ? "&nbsp;" : body; break;
                    }
                    if (p.AnchorId.Length > 0 && p.Style is ParaStyle.Heading1 or ParaStyle.Heading2 or ParaStyle.Heading3) text += " {#" + p.AnchorId + "}";
                    parts.Add(Prefix(meta) + text);
                    break;
                }
                case DividerBlock: numbers.Clear(); previousList = null; parts.Add("---"); break;
                case MediaBlock m: numbers.Clear(); previousList = null; parts.Add(Media(m, m.Align, false)); break;
                case RowBlock r:
                    numbers.Clear(); previousList = null;
                    for (int i = 0; i < r.Cells.Count; i++) parts.Add(Media(r.Cells[i], r.Align, i > 0));
                    break;
            }
        }
        return string.Join("\n\n", parts) + "\n";
    }

    private static string Prefix(JObject meta)
    {
        if (!meta.HasValues) return "";
        var full = new JObject { ["v"] = 1, ["Type"] = "text" };
        foreach (var property in meta.Properties()) full[property.Name] = property.Value;
        return "<!--mce:" + full.ToString(Formatting.None) + "-->\n";
    }

    private static string Media(MediaBlock m, string align, bool inline)
    {
        string type = m.Kind == MediaKind.Gif ? "gif" : m.Kind == MediaKind.Video ? "video" : "image";
        var meta = new JObject { ["v"] = 1, ["Type"] = type, ["Media"] = m.Source, ["Align"] = align, ["Width"] = m.Width, ["InlineWithPrevious"] = inline };
        string caption = Escape(m.Caption);
        string url = m.Source.Replace("\\", "/");
        if (url.IndexOfAny(new[] { ' ', '(', ')' }) >= 0) url = "<" + url + ">";
        return "<!--mce:" + meta.ToString(Formatting.None) + "-->\n![" + caption + "](" + url + ")";
    }

    // Wrapper order is outermost first. Bold sits outside italic so "**a *b***" never needs
    // two asterisk delimiters to touch at a boundary.
    private enum Wrap { Link, Font, Size, Color, Highlight, Underline, Strike, Bold, Italic }

    private static string Inline(ParagraphBlock p)
    {
        var output = new StringBuilder();
        var open = new List<(Wrap Kind, string Value)>();
        var runs = p.Runs().ToList();
        for (int r = 0; r < runs.Count; r++)
        {
            var wanted = Wrappers(runs[r].Style);
            int common = 0;
            while (common < open.Count && common < wanted.Count && open[common].Equals(wanted[common])) common++;
            for (int i = open.Count - 1; i >= common; i--) output.Append(Close(open[i]));
            string text = runs[r].Text;
            // Markdown emphasis cannot start or end with whitespace; keep spaces outside the delimiters.
            int lead = 0, trail = 0;
            if (wanted.Count > common) while (lead < text.Length && char.IsWhiteSpace(text[lead])) lead++;
            if (wanted.Count > 0) while (trail < text.Length - lead && char.IsWhiteSpace(text[text.Length - 1 - trail])) trail++;
            output.Append(Escape(text.Substring(0, lead), output.Length == 0));
            for (int i = common; i < wanted.Count; i++) output.Append(Open(wanted[i]));
            output.Append(Escape(text.Substring(lead, text.Length - lead - trail), output.Length == 0));
            if (trail > 0)
            {
                // Trailing spaces sit outside every wrapper; the next run reopens what it needs.
                for (int i = wanted.Count - 1; i >= 0; i--) output.Append(Close(wanted[i]));
                output.Append(Escape(text.Substring(text.Length - trail), false));
                open = new List<(Wrap, string)>();
                continue;
            }
            open = wanted;
        }
        for (int i = open.Count - 1; i >= 0; i--) output.Append(Close(open[i]));
        return output.ToString().Replace("\n", "\\\n");
    }

    private static List<(Wrap Kind, string Value)> Wrappers(TextStyle s)
    {
        var list = new List<(Wrap, string)>();
        if (s.Link.Length != 0) list.Add((Wrap.Link, s.Link));
        if (s.Font.Length != 0) list.Add((Wrap.Font, s.Font));
        if (s.Size != 0) list.Add((Wrap.Size, s.Size.ToString(CultureInfo.InvariantCulture)));
        if (s.Color.Length != 0) list.Add((Wrap.Color, s.Color));
        if (s.Highlight.Length != 0) list.Add((Wrap.Highlight, s.Highlight));
        if (s.Underline) list.Add((Wrap.Underline, ""));
        if (s.Strike) list.Add((Wrap.Strike, ""));
        if (s.Bold) list.Add((Wrap.Bold, ""));
        if (s.Italic) list.Add((Wrap.Italic, ""));
        return list;
    }

    private static string Open((Wrap Kind, string Value) w) => w.Kind switch
    {
        Wrap.Link => "[", Wrap.Font => "<span data-mce-font=\"" + Attr(w.Value) + "\">", Wrap.Size => "<span data-mce-size=\"" + w.Value + "\">",
        Wrap.Color => "<span data-mce-color=\"" + w.Value + "\">", Wrap.Highlight => "<span data-mce-mark=\"" + w.Value + "\">",
        Wrap.Underline => "<u>", Wrap.Strike => "~~", Wrap.Bold => "**", _ => "*"
    };
    private static string Close((Wrap Kind, string Value) w) => w.Kind switch
    {
        Wrap.Link => "](" + LinkUrl(w.Value) + ")", Wrap.Font or Wrap.Size or Wrap.Color or Wrap.Highlight => "</span>",
        Wrap.Underline => "</u>", Wrap.Strike => "~~", Wrap.Bold => "**", _ => "*"
    };
    private static string Attr(string value) => value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
    private static string LinkUrl(string url) => url.IndexOfAny(new[] { ' ', '(', ')', '<', '>' }) >= 0 ? "<" + url.Replace("<", "%3C").Replace(">", "%3E") + ">" : url;

    internal static string Escape(string text, bool lineStart = false)
    {
        var builder = new StringBuilder(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if ("\\`*_{}[]<>#!~|".IndexOf(c) >= 0) builder.Append('\\');
            else if ((lineStart && builder.Length == 0 || i > 0 && text[i - 1] == '\n') && (c == '-' || c == '+' || c == '=')) builder.Append('\\');
            else if (c == '.' && i > 0 && char.IsDigit(text[i - 1]) && LeadingDigits(text, i, lineStart)) builder.Append('\\');
            builder.Append(c);
        }
        return builder.ToString();
    }

    private static bool LeadingDigits(string text, int dot, bool lineStart)
    {
        int i = dot - 1;
        while (i >= 0 && char.IsDigit(text[i])) i--;
        return i < 0 ? lineStart : text[i] == '\n';
    }
}
