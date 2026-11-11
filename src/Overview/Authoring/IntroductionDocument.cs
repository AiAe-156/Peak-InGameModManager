#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

internal sealed class IntroductionBlock
{
    public string Source = "";
    public string Rich = "";
    public string Type = "text";
    public string Media = "";
    public string Align = "left";
    public int Width = 100;
    public bool InlineWithPrevious;
    public int Indent;
    public string Background = "";
    public bool Dirty;
    public bool PreserveBody;
    public string RawBody = "";
    public string OriginalMetadata = "";
    public bool Mergeable = true;
}

internal static class IntroductionDocument
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseEmphasisExtras().Build();
    private static readonly Regex Meta = new(@"^<!--mce:(.*?)-->\s*", RegexOptions.Singleline);
    public static List<IntroductionBlock> Parse(string source)
    {
        var result = new List<IntroductionBlock>();
        // Markdig supplies source spans. Preserve unknown source verbatim until explicitly edited.
        var ast = Markdown.Parse(source, Pipeline);
        int position = 0;
        IntroductionBlock? pending = null;
        string pendingSource = "";
        string pendingMetadata = "";
        foreach (var node in ast)
        {
            int end = Math.Min(source.Length, node.Span.End + 1);
            string raw = source.Substring(position, end - position).Trim('\r', '\n'); position = end;
            Match metadata = Meta.Match(raw);
            if (metadata.Success)
            {
                try { pending = JsonConvert.DeserializeObject<IntroductionBlock>(metadata.Groups[1].Value); } catch { pending = null; }
                pendingMetadata = metadata.Groups[1].Value;
                string remainder = raw.Substring(metadata.Length);
                if (remainder.Length == 0 && pending != null) { pendingSource = raw; continue; }
                raw = remainder;
            }
            var item = pending ?? new IntroductionBlock(); pending = null;
            item.Source = pendingSource.Length == 0 ? raw : pendingSource + "\n" + raw;
            pendingSource = "";
            item.RawBody = raw;
            item.OriginalMetadata = pendingMetadata;pendingMetadata = "";
            item.Width = Math.Max(10, Math.Min(100, item.Width));
            if (item.Type == "image" || item.Type == "gif" || item.Type == "video") { result.Add(item); continue; }
            if (node is ThematicBreakBlock) item.Type = "rule";
            else if (node is LeafBlock leaf && leaf.Inline != null)
            {
                if (leaf.Inline.FirstChild is LinkInline image && image.IsImage && image.NextSibling == null)
                {
                    item.Media = image.Url ?? "";
                    string path = item.Media.Split('?')[0].ToLowerInvariant();
                    item.Type = path.EndsWith(".gif") ? "gif" : Regex.IsMatch(path, @"\.(mp4|webm|mov|m4v|avi|wmv)$") ? "video" : "image";
                }
                else item.Rich = Inline(leaf.Inline);
                if (node is HeadingBlock heading) item.Rich = "<size=" + (heading.Level==1?36:heading.Level==2?30:26) + "><color="+(heading.Level<=2?"#DDC374":"#EAE5D8")+">" + item.Rich + "</color></size>";
            }
            else if (node is ListBlock || node is QuoteBlock) item.Rich = RenderContainer((ContainerBlock)node);
            else { item.Rich = Escape(raw); item.PreserveBody = true; }
            if(Regex.Matches(raw,@"<[^>]+>").Cast<Match>().Any(tag => !Regex.IsMatch(tag.Value,@"^<(?:span data-mce-(?:color|size|font)=""[^""]+""|/span|/?u|/?s)>$")))item.PreserveBody=true;
            item.Mergeable = node is ParagraphBlock && item.Type == "text" && !item.PreserveBody;
            result.Add(item);
        }
        if (pending != null) {pending.Source=pendingSource;pending.OriginalMetadata=pendingMetadata;result.Add(pending);}
        if (position < source.Length && source.Substring(position).Trim().Length != 0)
            result.Add(new IntroductionBlock { Source = source.Substring(position), Rich = Escape(source.Substring(position)) });
        return result;
    }
    public static string Serialize(IEnumerable<IntroductionBlock> blocks) => string.Join("\n\n", blocks.Select(SerializeBlock));
    internal static void MergeParagraphs(List<IntroductionBlock> blocks)
    {
        bool Eligible(IntroductionBlock block)
        {
            if(block.Type!="text"||block.PreserveBody||!block.Mergeable)return false;
            if(string.IsNullOrEmpty(block.OriginalMetadata))return true;
            try{var metadata=JObject.Parse(block.OriginalMetadata);string[] known={"v","Type","Media","Align","Width","Indent","Background","InlineWithPrevious"};return metadata.Properties().All(p=>known.Contains(p.Name,StringComparer.OrdinalIgnoreCase))&&(metadata.Value<int?>("v")??1)==1;}catch{return false;}
        }
        for(int i=0;i+1<blocks.Count;)
        {
            var first=blocks[i];var next=blocks[i+1];
            if(!Eligible(first)||!Eligible(next)||first.Width!=next.Width||first.Indent!=next.Indent||!string.Equals(first.Align,next.Align,StringComparison.OrdinalIgnoreCase)||!string.Equals(first.Background,next.Background,StringComparison.OrdinalIgnoreCase)){i++;continue;}
            string preserved=Serialize(new[]{first,next});first.Rich+="\n\n"+next.Rich;first.Source=preserved;first.Dirty=false;blocks.RemoveAt(i+1);
        }
    }
    private static string SerializeBlock(IntroductionBlock b)
    {
        if (!b.Dirty) return b.Source;
        JObject metadata;try{metadata=JObject.Parse(b.OriginalMetadata);}catch{metadata=new JObject();}
        if(metadata["v"]==null)metadata["v"]=1;metadata["Type"]=b.Type;metadata["Media"]=b.Media;metadata["Align"]=b.Align;metadata["Width"]=b.Width;metadata["Indent"]=b.Indent;metadata["Background"]=b.Background;metadata["InlineWithPrevious"]=b.InlineWithPrevious;
        string body = b.PreserveBody ? b.RawBody : b.Type == "rule" ? "---" : b.Type == "text" ? RichToMarkdown(b.Rich) : "![媒体](" + b.Media.Replace("\\", "/") + ")";
        return "<!--mce:" + metadata.ToString(Formatting.None) + "-->\n" + body;
    }
    private static string RenderContainer(ContainerBlock block)
    {
        return string.Join("\n", block.Select(b => b is LeafBlock leaf && leaf.Inline != null ? Inline(leaf.Inline) : b is ContainerBlock child ? (block is ListBlock ? "• " : "│ ") + RenderContainer(child) : ""));
    }
    private static string Inline(ContainerInline root) => Inline(root, new Stack<string>());
    private static string Inline(ContainerInline root, Stack<string> spans)
    {
        string result = "";
        foreach (var node in root)
        {
            if (node is LiteralInline literal) result += Escape(literal.Content.ToString());
            else if (node is LineBreakInline) result += "\n";
            else if (node is CodeInline code) result += Escape(code.Content);
            else if (node is EmphasisInline emphasis)
            { string tag = emphasis.DelimiterChar == '~' ? "s" : emphasis.DelimiterCount >= 2 ? "b" : "i"; result += "<" + tag + ">" + Inline(emphasis, spans) + "</" + tag + ">"; }
            else if (node is LinkInline link)
            { result += link.IsImage ? Loc.F("[Image: {0}]", "[图片：{0}]", "[圖片：{0}]", Escape(link.Url ?? "")) : "<link=\"" + EscapeAttribute(link.Url ?? "") + "\"><color=#2689D8><u>" + Inline(link, spans) + "</u></color></link>"; }
            else if (node is HtmlInline html)
            {
                string tag = html.Tag;
                var span = Regex.Match(tag, "^<span data-mce-(color|size|font)=\"([^\"]+)\">$");
                string kind = span.Groups[1].Value, value = WebUtility.HtmlDecode(span.Groups[2].Value);
                if (span.Success && (kind == "font" || (kind == "color" ? Regex.IsMatch(value, "^#[A-Fa-f0-9]{6,8}$") : float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))))
                { spans.Push(kind); result += "<" + kind + "=" + (kind == "font" ? "\"" + IntroductionFontRegistry.RegistryName(value) + "\"" : value) + ">"; }
                else if (tag == "</span>") result += spans.Count > 0 ? "</" + spans.Pop() + ">" : "";
                else if (tag == "<u>" || tag == "</u>" || tag == "<s>" || tag == "</s>") result += tag;
                else result += Escape(tag);
            }
            else if (node is ContainerInline container) result += Inline(container, spans);
        }
        return result;
    }
    public static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    private static string EscapeAttribute(string s) => s.Replace("\"", "%22").Replace("<", "%3C").Replace(">", "%3E");
    private static string RichToMarkdown(string rich)
    {
        rich = Regex.Replace(rich, @"<[^>]+>|[^<]+", m => m.Value.StartsWith("<") ? m.Value : Regex.Replace(m.Value.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&"), @"[\\`*_{}\[\]<>#!]", e => "\\" + e.Value));
        rich = Regex.Replace(rich, @"<link=""([^""]*)"">(.*?)</link>", m => "[" + Regex.Replace(m.Groups[2].Value, "</?(color|u)[^>]*>", "") + "](" + m.Groups[1].Value + ")", RegexOptions.Singleline);
        rich = Regex.Replace(rich, @"<(color|size|font)=([^>]+)>|</(color|size|font)>", m => m.Groups[1].Success ? "<span data-mce-" + m.Groups[1].Value + "=\"" + WebUtility.HtmlEncode(m.Groups[1].Value == "font" ? IntroductionFontRegistry.StableId(m.Groups[2].Value) : m.Groups[2].Value) + "\">" : "</span>");
        // Text entities were decoded above; decoding again here would corrupt font filenames
        // such as literal "a&amp;b.ttf" inside HTML attributes.
        return rich.Replace("<b>", "**").Replace("</b>", "**").Replace("<i>", "*").Replace("</i>", "*").Replace("\r\n","\n").Replace("\n","\\\n");
    }
}
