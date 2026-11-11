#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ModConfigEnhance.Overview.Editor;

namespace ModConfigEnhance.Overview.Help;

/// <summary>Built-in publications and per-language personal overrides, independent of configuration profiles.</summary>
internal static class HelpDocuments
{
    internal sealed class Page
    {
        internal readonly string Id;
        private readonly string _english, _simplified, _traditional;
        internal string Title => Loc.T(_english, _simplified, _traditional);
        internal Page(string id, string english, string simplified, string traditional)
        { Id = id; _english = english; _simplified = simplified; _traditional = traditional; }
    }
    internal static readonly Page[] Pages =
    {
        new("overview", "Feature overview", "功能速览", "功能速覽"),
        new("mods", "Mod management", "模组管理", "模組管理"),
        new("profiles", "Profiles and hotkeys", "配置与快捷键", "設定與快捷鍵"),
        new("translation", "Translation", "翻译模块", "翻譯模組"),
        new("packs", "Modpack features", "整合包功能", "整合包功能"),
        new("authoring", "Build a modpack", "如何打包", "如何打包"),
        new("sources", "Sources and updates", "来源与更新", "來源與更新"),
        new("export", "Export and publish", "导出与发布", "匯出與發佈"),
        new("faq", "FAQ", "常见问题", "常見問題"),
        new("changelog", "Changelog", "更新日志", "更新日誌")
    };
    internal sealed class Article
    {
        internal readonly Document Document;
        internal readonly Dictionary<string, int> Anchors;
        internal readonly List<(string Label, string Target)> Links;
        internal Article(Document document, Dictionary<string, int> anchors, List<(string Label, string Target)> links)
        { Document = document; Anchors = anchors; Links = links; }
    }
    private static readonly Dictionary<string, Article> Cache = new(StringComparer.Ordinal);

    internal static Page? Find(string id) => Array.Find(Pages, p => p.Id == id);
    internal static string LanguageCode => Code(Loc.UiLanguage);
    private static string Code(Loc.UiLang language) => language switch
    { Loc.UiLang.ZhCn => "zh-cn", Loc.UiLang.ZhTw => "zh-tw", _ => "en-us" };
    internal static Article Load(string id) => Load(id, Loc.UiLanguage);
    internal static Article Load(string id, Loc.UiLang language)
    {
        string code = Code(language);
        try { var local = HelpDocumentSource.ReadOverride(id, code); if (local != null) return local; }
        catch (Exception ex) { Plugin.Log.LogWarning("[说明覆盖] " + code + "/" + id + ": " + ex.Message); }
        return LoadBuiltin(id, language);
    }
    internal static string BuiltinMarkdown(string id, string code)
    {
        if (Find(id) == null) throw new ArgumentException("Unknown help page: " + id);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MCE.Help." + code + "." + id + ".md") ?? throw new InvalidDataException("Missing built-in help: " + code + "/" + id);
        using var reader = new StreamReader(stream, Encoding.UTF8); return reader.ReadToEnd();
    }
    internal static Article LoadBuiltin(string id, Loc.UiLang language)
    {
        string code = Code(language), key = code + "/" + id;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        if (Find(id) == null) throw new ArgumentException(Loc.F("Unknown help page: {0}", "未知说明分页：{0}", "未知說明分頁：{0}", id));
        return Cache[key] = Parse(BuiltinMarkdown(id, code));
    }
    internal static Article Parse(string source) => FromDocument(DocMarkdown.Import(source));
    internal static Article FromDocument(Document document)
    {
        var anchors = new Dictionary<string, int>(StringComparer.Ordinal) { ["top"] = 0 };
        for (int i = 0; i < document.Blocks.Count; i++)
            if (document.Blocks[i] is ParagraphBlock paragraph && paragraph.AnchorId.Length > 0)
            {
                if (paragraph.AnchorId != "top" && anchors.ContainsKey(paragraph.AnchorId)) throw new InvalidDataException("Duplicate help anchor: " + paragraph.AnchorId);
                anchors[paragraph.AnchorId] = i;
            }
        var links = new List<(string Label, string Target)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var paragraph in document.Blocks.OfType<ParagraphBlock>())
        {
            string target = "", label = "";
            void Flush() { if (target.Length > 0 && seen.Add(target)) links.Add((label, target)); }
            foreach (var run in paragraph.Runs())
            {
                if (run.Style.Link != target) { Flush(); target = run.Style.Link; label = ""; }
                label += run.Text;
            }
            Flush();
        }
        return new Article(document, anchors, links);
    }
    internal static bool TryResolve(string currentPage, string target, out string page, out string anchor)
    {
        int split = target.IndexOf('#');
        page = split < 0 ? target : target.Substring(0, split);
        anchor = split < 0 ? "top" : target.Substring(split + 1);
        if (page.Length == 0) page = currentPage;
        if (page.EndsWith(".md", StringComparison.Ordinal)) page = page.Substring(0, page.Length - 3);
        if (anchor.Length == 0) anchor = "top";
        return Find(page) != null && Load(page).Anchors.ContainsKey(anchor);
    }
}
