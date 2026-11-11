#nullable enable
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview.Editor;

/// <summary>
/// Storage format <c>introduction.mcedoc.json</c>. Default-valued properties are omitted to keep files diffable.
/// Unknown properties and block types are tolerated so newer files still open as far as possible.
/// </summary>
internal static class DocJson
{
    internal const string Format = "mce-doc";
    internal const int Version = 1;

    public static string Write(Document document)
    {
        var blocks = new JArray(document.Blocks.Select(WriteBlock));
        var root = new JObject { ["format"] = Format, ["version"] = Version, ["blocks"] = blocks };
        return root.ToString(Formatting.Indented);
    }

    public static Document Read(string json)
    {
        var root = JObject.Parse(json);
        if (root.Value<string>("format") is string format && format != Format) throw new InvalidDataException(Loc.T("Not a manager document: ", "不是管理器文档：", "不是管理器文檔：") + format);
        int version = root.Value<int?>("version") ?? 1;
        if (version > Version) throw new InvalidDataException(Loc.F("Document version {0} is newer than this mod. Update In-Game Mod Manager.", "文档版本 {0} 比当前模组新，请更新游戏内模组管理器。", "文件版本 {0} 比目前模組新，請更新遊戲內模組管理器。", version));
        var document = new Document();
        if (root["blocks"] is JArray blocks)
            foreach (var token in blocks.OfType<JObject>())
            {
                var block = ReadBlock(token);
                if (block != null) document.Blocks.Add(block);
            }
        document.Normalize();
        return document;
    }

    private static JObject WriteBlock(DocBlock block)
    {
        switch (block)
        {
            case ParagraphBlock p:
            {
                var o = new JObject { ["type"] = "p" };
                if (p.Style != ParaStyle.Normal) o["style"] = StyleName(p.Style);
                if (p.Align != "left") o["align"] = p.Align;
                if (p.Indent != 0) o["indent"] = p.Indent;
                if (p.Background.Length != 0) o["background"] = p.Background;
                if (p.AnchorId.Length != 0) o["anchor"] = p.AnchorId;
                o["runs"] = new JArray(p.Runs().Select(WriteRun));
                return o;
            }
            case MediaBlock m: return WriteMedia(m, true);
            case RowBlock r:
            {
                var o = new JObject { ["type"] = "row" };
                if (r.Align != "center") o["align"] = r.Align;
                o["cells"] = new JArray(r.Cells.Select(c => WriteMedia(c, false)));
                return o;
            }
            default: return new JObject { ["type"] = "divider" };
        }
    }

    private static JObject WriteMedia(MediaBlock m, bool standalone)
    {
        var o = new JObject { ["type"] = "media", ["kind"] = m.Kind.ToString().ToLowerInvariant(), ["src"] = m.Source };
        if (m.Width != 100 || !standalone) o["width"] = m.Width;
        if (standalone && m.Align != "center") o["align"] = m.Align;
        if (m.Caption.Length != 0) o["caption"] = m.Caption;
        return o;
    }

    private static JObject WriteRun((string Text, TextStyle Style) run)
    {
        var s = run.Style; var o = new JObject { ["text"] = run.Text };
        if (s.Bold) o["b"] = true;
        if (s.Italic) o["i"] = true;
        if (s.Underline) o["u"] = true;
        if (s.Strike) o["s"] = true;
        if (s.Color.Length != 0) o["color"] = s.Color;
        if (s.Highlight.Length != 0) o["mark"] = s.Highlight;
        if (s.Size != 0) o["size"] = s.Size;
        if (s.Font.Length != 0) o["font"] = s.Font;
        if (s.Link.Length != 0) o["link"] = s.Link;
        return o;
    }

    private static DocBlock? ReadBlock(JObject o)
    {
        switch (o.Value<string>("type"))
        {
            case "p":
            {
                var p = new ParagraphBlock { Style = ParseStyle(o.Value<string>("style")), Align = Align(o.Value<string>("align"), "left"),
                    Indent = Math.Max(0, Math.Min(8, o.Value<int?>("indent") ?? 0)), Background = Color(o.Value<string>("background")), AnchorId = o.Value<string>("anchor") ?? "" };
                if (o["runs"] is JArray runs)
                    foreach (var run in runs.OfType<JObject>())
                    {
                        string text = run.Value<string>("text") ?? "";
                        if (text.Length == 0) continue;
                        p.Insert(p.Length, text, new TextStyle(run.Value<bool?>("b") ?? false, run.Value<bool?>("i") ?? false, run.Value<bool?>("u") ?? false,
                            run.Value<bool?>("s") ?? false, Color(run.Value<string>("color")), Color(run.Value<string>("mark")),
                            Math.Max(0, Math.Min(96, run.Value<int?>("size") ?? 0)), run.Value<string>("font") ?? "", run.Value<string>("link") ?? ""));
                    }
                return p;
            }
            case "media": return ReadMedia(o);
            case "row":
            {
                var row = new RowBlock { Align = Align(o.Value<string>("align"), "center") };
                if (o["cells"] is JArray cells) foreach (var cell in cells.OfType<JObject>()) if (ReadMedia(cell) is MediaBlock m) row.Cells.Add(m);
                return row;
            }
            case "divider": return new DividerBlock();
            default: return null;
        }
    }

    private static MediaBlock? ReadMedia(JObject o)
    {
        string src = o.Value<string>("src") ?? "";
        if (src.Length == 0) return null;
        MediaKind kind = (o.Value<string>("kind") ?? "") switch { "gif" => MediaKind.Gif, "video" => MediaKind.Video, "image" => MediaKind.Image, _ => MediaBlock.KindFromPath(src) };
        return new MediaBlock { Kind = kind, Source = src, Width = Math.Max(10, Math.Min(100, o.Value<int?>("width") ?? 100)),
            Align = Align(o.Value<string>("align"), "center"), Caption = o.Value<string>("caption") ?? "" };
    }

    internal static string StyleName(ParaStyle style) => style switch
    {
        ParaStyle.Heading1 => "h1", ParaStyle.Heading2 => "h2", ParaStyle.Heading3 => "h3", ParaStyle.Quote => "quote",
        ParaStyle.Callout => "callout", ParaStyle.Bullet => "bullet", ParaStyle.Numbered => "numbered", _ => "normal"
    };
    internal static ParaStyle ParseStyle(string? name) => name switch
    {
        "h1" => ParaStyle.Heading1, "h2" => ParaStyle.Heading2, "h3" => ParaStyle.Heading3, "quote" => ParaStyle.Quote,
        "callout" => ParaStyle.Callout, "bullet" => ParaStyle.Bullet, "numbered" => ParaStyle.Numbered, _ => ParaStyle.Normal
    };
    internal static string Align(string? value, string fallback) => value == "left" || value == "center" || value == "right" ? value : fallback;
    internal static string Color(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        string v = value!.Trim();
        if (v[0] != '#' || (v.Length != 7 && v.Length != 9)) return "";
        for (int i = 1; i < v.Length; i++) if (!Uri.IsHexDigit(v[i])) return "";
        return v.ToUpperInvariant();
    }
}
