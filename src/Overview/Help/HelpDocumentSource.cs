#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ModConfigEnhance.Overview.Editor;

namespace ModConfigEnhance.Overview.Help;
internal sealed class HelpDocumentSource : IDocumentEditorSource
{
    internal string PageId = "overview", Language = "en-us";
    private static readonly string[] Languages = { "en-us", "zh-cn", "zh-tw" };
    internal static string BaseDirectory => Updating.MceDirectories.Help(Loc.RootDirectory);
    private static string BundledBaseDirectory => Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, "Help");
    private string BundledDirectory => Path.Combine(BundledBaseDirectory, Language);
    internal string FontsDirectory => Directory.Exists(Path.Combine(RootDirectory, "fonts")) ? Path.Combine(RootDirectory, "fonts") : Path.Combine(BundledDirectory, "fonts");
    public string RootDirectory => Path.Combine(BaseDirectory, Language);
    public string ExportDirectory => Loc.PackOutputDirectory;
    public string? ReadNote { get; private set; }
    private string JsonPath => Path.Combine(RootDirectory, PageId + ".mcedoc.json");
    private string MarkdownPath => Path.Combine(RootDirectory, PageId + ".md");
    internal bool HasOverride => File.Exists(JsonPath) || File.Exists(MarkdownPath);
    private void Validate()
    { if (!Languages.Contains(Language) || HelpDocuments.Find(PageId) == null) throw new InvalidDataException("Invalid help document target."); }
    internal static HelpDocuments.Article? ReadOverride(string id, string language)
    {
        if (!Languages.Contains(language) || HelpDocuments.Find(id) == null) return null;
        string root = Path.Combine(BaseDirectory, language), json = Path.Combine(root, id + ".mcedoc.json"), markdown = Path.Combine(root, id + ".md");
        if (File.Exists(json)) return HelpDocuments.FromDocument(DocJson.Read(File.ReadAllText(json, Encoding.UTF8)));
        return File.Exists(markdown) ? HelpDocuments.Parse(File.ReadAllText(markdown, Encoding.UTF8)) : null;
    }
    public Document Load()
    {
        Validate();
        foreach (string folder in new[] { "media", "fonts" })
        foreach (string file in OverviewRepository.SafeFiles(Path.Combine(BundledDirectory, folder)))
        {
            string relative = file.Substring(BundledDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string target = OverviewProfileAssets.CheckedPath(RootDirectory, relative);
            if (!File.Exists(target)) { Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, false); }
        }
        ReadNote = null;
        HelpDocuments.Article? local;
        try { local = ReadOverride(PageId, Language); }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning("[说明编辑] " + ex.Message); local = null;
            ReadNote = Loc.T("The local guide could not be read. The built-in text is shown; restore this page before saving. The unreadable files will be backed up.", "本地说明无法读取，当前显示内置文本。请先恢复本页内置，再保存；原资料会移入备份。", "無法讀取本機說明，目前顯示內建文字。請先還原本頁內建內容再儲存；原資料會移入備份。");
        }
        return (local ?? HelpDocuments.Parse(HelpDocuments.BuiltinMarkdown(PageId, Language))).Document.Clone();
    }
    public Document ImportMarkdown(string source) => HelpDocuments.Parse(source).Document;
    public string ResolveMedia(string relative)
    {
        try { Validate(); string local = OverviewProfileAssets.CheckedPath(RootDirectory, relative); if (File.Exists(local)) return local;
            string bundled = OverviewProfileAssets.CheckedPath(BundledDirectory, relative); return File.Exists(bundled) ? bundled : local; }
        catch { return ""; }
    }
    public void Save(Document document)
    {
        Validate();
        if (ReadNote != null) throw new InvalidDataException(ReadNote);
        var used = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        int next = 1;
        foreach (var p in document.Blocks.OfType<ParagraphBlock>())
        {
            if (p.Style is not (ParaStyle.Heading1 or ParaStyle.Heading2 or ParaStyle.Heading3)) continue;
            if (p.AnchorId.Length == 0 || !Regex.IsMatch(p.AnchorId, "^[a-z0-9-]+$") || !used.Add(p.AnchorId))
            { do { p.AnchorId = "section-" + next++; } while (!used.Add(p.AnchorId)); }
        }
        HelpDocuments.FromDocument(document);
        string json = DocJson.Write(document), markdown = DocMarkdown.Export(document);
        if (File.Exists(JsonPath) && File.Exists(MarkdownPath) && File.ReadAllText(JsonPath, Encoding.UTF8) == json && File.ReadAllText(MarkdownPath, Encoding.UTF8) == markdown) return;
        Backup(false);
        OverviewRepository.AtomicWrite(JsonPath, json);
        OverviewRepository.AtomicWrite(MarkdownPath, markdown);
    }
    private void Backup(bool move)
    {
        Validate(); if (!HasOverride) return;
        string backup = Path.Combine(Loc.BackupDirectory, "help", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6), Language);
        Directory.CreateDirectory(backup);
        foreach (string file in new[] { JsonPath, MarkdownPath })
            if (File.Exists(file)) { string target = Path.Combine(backup, Path.GetFileName(file)); if (move) File.Move(file, target); else File.Copy(file, target); }
    }
    internal void RestoreBuiltin() { Backup(true); ReadNote = null; }
    public string Export()
    {
        Validate();
        string output = Path.Combine(ExportDirectory, "help_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(output);
        foreach (string language in Languages)
        {
            string destination = Path.Combine(output, "Help", language); Directory.CreateDirectory(destination);
            foreach (var page in HelpDocuments.Pages)
            {
                var local = ReadOverride(page.Id, language);
                File.WriteAllText(Path.Combine(destination, page.Id + ".md"), local == null ? HelpDocuments.BuiltinMarkdown(page.Id, language) : DocMarkdown.Export(local.Document), new UTF8Encoding(false));
                if (local != null) File.WriteAllText(Path.Combine(destination, page.Id + ".mcedoc.json"), DocJson.Write(local.Document), new UTF8Encoding(false));
            }
            foreach (string root in new[] { Path.Combine(BundledBaseDirectory, language), Path.Combine(BaseDirectory, language) })
            {
            foreach (string folder in new[] { "media", "fonts" })
            foreach (string file in OverviewRepository.SafeFiles(Path.Combine(root, folder)))
            {
                if (file.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)) continue;
                string relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = OverviewProfileAssets.CheckedPath(destination, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
            }
            }
        }
        return output;
    }
}
