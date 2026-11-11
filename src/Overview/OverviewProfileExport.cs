#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ModConfigEnhance.Overview.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Exports selected schemes and their referenced resources, independently of personal UI state.</summary>
internal static class OverviewProfileExport
{
    internal sealed class ExportItem
    {
        internal string Id = "";
        internal string Name = "";
        internal string Directory = "";
        internal string ProfileJson = "";
        internal string ConfigValuesJson = "";
        internal Document? PreparedDocument;
    }

    private const string DocumentFile = "introduction.mcedoc.json", MarkdownFile = "introduction.md";

    // Shared by the repository and isolated integration checks: use the loaded committed pair,
    // never re-read the independently updated config-values mirror during export preparation.
    internal static List<ExportItem> PrepareItems(IEnumerable<OverviewProfile> profiles, IEnumerable<string> ids)
    {
        var selected = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var items = profiles.Where(p => selected.Contains(p.id)).Select(p => new ExportItem
        {
            Id = p.publicationId.Length > 0 ? p.publicationId : p.id, Name = p.name, Directory = p.directory,
            ProfileJson = JsonConvert.SerializeObject(p, Formatting.Indented),
            ConfigValuesJson = JsonConvert.SerializeObject(p.config, Formatting.Indented)
        }).ToList();
        if (items.Count == 0) throw new InvalidOperationException(Loc.T("Select at least one profile.", "请至少选择一套方案。", "請至少選擇一套方案。"));
        foreach (var item in items) Prepare(item);
        return items;
    }

    internal static void Prepare(ExportItem item) { item.PreparedDocument = LoadDocument(item.Directory, JObject.Parse(item.ProfileJson)); ValidateReferences(item.Directory, item.PreparedDocument); }
    internal static IEnumerable<string> ResourceFiles(ExportItem item) => References(item.Directory, item.PreparedDocument ?? LoadDocument(item.Directory, JObject.Parse(item.ProfileJson))).Select(p => p.Relative);
    internal static string Export(IReadOnlyList<ExportItem> items, string configRoot, string languageRoot, IEnumerable<string>? languages = null, IReadOnlyDictionary<string, string>? frozenLanguages = null, string? parentDirectory = null, string? displayName = null)
    {
        if (items.Count == 0) throw new InvalidOperationException(Loc.T("Select at least one profile.", "请至少选择一个方案。", "請至少選擇一個方案。"));
        // Complete validation before creating an output directory. A failed copy leaves its files in place,
        // but profiles.json is written last so an incomplete export is not advertised as a usable pack.
        var selected = new List<(ExportItem Item, string Id, JObject Profile, JToken Config, Document Document)>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ExportItem item in items)
        {
            string id = PortableId(item.Id);
            if (!ids.Add(id)) throw new InvalidOperationException(Loc.F("Duplicate profile in export selection: {0}", "导出选择中包含重复方案：{0}", "導出選擇中包含重復方案：{0}", item.Name));
            JObject profile = JObject.Parse(item.ProfileJson);
            JToken config = JObject.Parse(item.ConfigValuesJson);
            Document document = item.PreparedDocument ?? LoadDocument(item.Directory, profile);
            ValidateReferences(item.Directory, document);
            CleanProfile(profile, id, item.Name);
            profile["schemaVersion"] = GroupTree.HasNesting(profile["state"]?["groups"]?.ToObject<List<OverviewGroup>>()) ? 2 : 1;
            selected.Add((item, id, profile, config, document));
        }

        string exportParent = parentDirectory == null ? OverviewProfileAssets.CheckedPath(configRoot, "output/packs") : Path.GetFullPath(parentDirectory);
        string exportRoot = Path.Combine(exportParent, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + PackPublisher.SafeLabel(displayName ?? items[0].Name) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        if (Directory.Exists(exportRoot) || File.Exists(exportRoot)) throw new IOException(Loc.T("The selected export folder already exists. Choose another folder and try again.", "生成目录发生冲突，请更换输出目录后重试。", "產生目錄發生衝突，請更換輸出資料夾後重試。"));
        string package = OverviewProfileAssets.CheckedPath(exportRoot, "plugins/MCEPack");
        Directory.CreateDirectory(package);
        var profiles = new JArray();
        foreach (var entry in selected)
        {
            string relative = "profiles/" + entry.Id;
            string directory = OverviewProfileAssets.CheckedPath(package, relative);
            Directory.CreateDirectory(directory);
            CopyReferences(entry.Item.Directory, directory, entry.Document);
            WriteJson(directory, "profile.json", entry.Profile);
            WriteJson(directory, "config-values.json", entry.Config);
            File.WriteAllText(OverviewProfileAssets.CheckedPath(directory, DocumentFile), DocJson.Write(entry.Document), new UTF8Encoding(false));
            File.WriteAllText(OverviewProfileAssets.CheckedPath(directory, MarkdownFile), DocMarkdown.Export(entry.Document), new UTF8Encoding(false));
            profiles.Add(new JObject { ["id"] = entry.Id, ["name"] = entry.Item.Name, ["directory"] = relative });
        }
        string languageTarget = OverviewProfileAssets.CheckedPath(package, "language");
        if (frozenLanguages == null) Loc.ExportLanguages(languageTarget, languages);
        else foreach (var file in frozenLanguages) OverviewRepository.AtomicWrite(OverviewProfileAssets.CheckedPath(languageTarget, file.Key), file.Value);
        WriteJson(package, "profiles.json", new JObject { ["schemaVersion"] = 1, ["profiles"] = profiles });
        return exportRoot;
    }

    private static Document LoadDocument(string directory, JObject profile)
    {
        JObject? pack = (profile["state"] as JObject)?["pack"] as JObject;
        string documentRelative = pack?.Value<string>("introDocument") ?? "";
        string markdownRelative = pack?.Value<string>("introFile") ?? "";
        string document = OverviewProfileAssets.CheckedPath(directory, string.IsNullOrWhiteSpace(documentRelative) ? DocumentFile : Uri.UnescapeDataString(documentRelative));
        string markdown = OverviewProfileAssets.CheckedPath(directory, string.IsNullOrWhiteSpace(markdownRelative) ? MarkdownFile : Uri.UnescapeDataString(markdownRelative));
        if (File.Exists(document)) return DocJson.Read(File.ReadAllText(document, Encoding.UTF8));
        if (File.Exists(markdown)) return DocMarkdown.Import(File.ReadAllText(markdown, Encoding.UTF8));
        if (pack?.Value<bool?>("introEnabled") == true || !string.IsNullOrWhiteSpace(documentRelative))
            throw new FileNotFoundException(Loc.T("The profile has no introduction file.", "方案缺少介绍文件。", "方案缺少介紹文件。"), document);
        return new Document();
    }
    internal static string PreviewProfile(ExportItem item)
    {
        var profile = JObject.Parse(item.ProfileJson); CleanProfile(profile, PortableId(item.Id), item.Name); return profile.ToString(Formatting.Indented);
    }

    private static IEnumerable<(string Source, string Relative)> References(string directory, Document document)
    {
        var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MediaBlock media in document.AllMedia())
        {
            string relative = Uri.UnescapeDataString(media.Source).Replace('\\', '/');
            if (Path.IsPathRooted(relative) || Uri.TryCreate(relative, UriKind.Absolute, out _)) throw new IOException(Loc.F("Media must use profile-relative paths: {0}", "媒体必须使用方案内相对路径：{0}", "媒體必須使用方案內相對路徑：{0}", relative));
            relative = string.Join("/", relative.Split('/').Where(segment => segment.Length != 0 && segment != "."));
            if (relative.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)) throw new IOException(Loc.F("The introduction references an incomplete asset: {0}", "介绍仍在引用未完成的素材：{0}", "介紹仍在引用未完成的素材：{0}", relative));
            if (!OverviewProfileAssets.SupportedMedia(relative)) throw new IOException(Loc.F("The introduction references an unsupported media format: {0}", "介绍引用了不支持的媒体格式：{0}", "介紹引用了不支持的媒體格式：{0}", relative));
            if (!relativePaths.Add(relative)) continue;
            if (IsReserved(relative)) throw new IOException(Loc.F("Media cannot use profile data file paths: {0}", "媒体不能使用方案资料文件路径：{0}", "媒體不能使用方案資料文件路徑：{0}", relative));
            string source = OverviewProfileAssets.CheckedPath(directory, relative);
            if (!File.Exists(source)) throw new FileNotFoundException(Loc.F("The profile is missing media referenced by its introduction: {0}", "方案缺少介绍中的媒体：{0}", "方案缺少介紹中的媒體：{0}", relative), source);
            yield return (source, relative);
        }
        foreach (string id in document.FontIds().Where(f => f.StartsWith("file:", StringComparison.Ordinal)))
        {
            string name = id.Substring(5), relative = "fonts/" + name;
            if (!relativePaths.Add(relative)) continue;
            string fontsDirectory = OverviewProfileAssets.CheckedPath(directory, "fonts");
            string? source = IntroductionFontRegistry.FindFontFile(name, fontsDirectory);
            if (source == null) throw new FileNotFoundException(Loc.F("The profile is missing a font referenced by its introduction: {0}", "方案缺少介绍中的字体：{0}", "方案缺少介紹中的字體：{0}", name), name);
            yield return (source, relative);
        }
    }

    private static void ValidateReferences(string directory, Document document)
    {
        foreach (var reference in References(directory, document))
        {
            if (reference.Source.Length == 0) throw new IOException(Loc.T("Invalid asset path.", "素材路径无效。", "素材路徑無效。"));
            if (new FileInfo(reference.Source).Length > OverviewProfileAssets.FileLimitBytes) throw new IOException(Loc.F("File exceeds the 1 GB export limit: {0}", "单文件超过1GB导出上限：{0}", "單文件超過1GB導出上限：{0}", reference.Relative));
        }
    }

    private static void CopyReferences(string sourceDirectory, string targetDirectory, Document document)
    {
        foreach (var reference in References(sourceDirectory, document))
        {
            string target = OverviewProfileAssets.CheckedPath(targetDirectory, reference.Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = File.OpenRead(reference.Source);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output, 81920);
        }
    }

    private static bool IsReserved(string relative) => new[] { "profile.json", "config-values.json", DocumentFile, MarkdownFile, "profiles.json", "state.json" }
        .Contains(relative, StringComparer.OrdinalIgnoreCase);

    private static void CleanProfile(JObject profile, string id, string name)
    {
        profile["id"] = id; profile["name"] = name; profile["kind"] = (int)OverviewProfileKind.Author;
        profile["readOnly"] = true;
        foreach (string field in new[] { "directory", "source", "config", "_pairedConfig", "loadError", "IsValid" }) profile.Remove(field);
        if (profile["state"] is not JObject state) throw new InvalidDataException(Loc.T("The profile data is incomplete.", "方案缺少完整资料。", "方案缺少完整資料。"));
        foreach (string field in new[] { "packOverridden", "packNameOverride", "globalUpdateOverridden", "updatePermissionVersion", "updateWarningsDismissed",
                     "automaticUpdateChecks", "hideDisabled", "modTagFilter", "hotkeyTagFilter", "customColors", "collapsedGroups", "collapsedHotkeyMods",
                     "hotkeyPresets", "hotkeyAuthorPresetsImported", "ignoredHotkeyConflicts", "ignoredPackConflicts", "packHotkeyPresetModeOverridden" }) state.Remove(field);
        if (state["mods"] is JArray mods) foreach (JObject mod in mods.OfType<JObject>())
            foreach (string field in new[] { "alias", "description", "descriptionOverridden", "updateOverridden", "sourceNamespace", "sourceName" }) mod.Remove(field);
        if (state["pack"] is JObject pack) { pack["introDocument"] = DocumentFile; pack["introFile"] = MarkdownFile; }
        // 2.3.1: which nested mods were on before their outer was disabled is this machine's memory, not profile content.
        if (profile["mods"] is JArray targets) foreach (JObject target in targets.OfType<JObject>()) target.Remove("nestedEnabled");
    }

    private static void CopyLanguage(string sourceDirectory, string targetDirectory)
    {
        if (!System.IO.Directory.Exists(sourceDirectory)) return;
        string prefix = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string file in OverviewRepository.SafeFiles(sourceDirectory).Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            string relative = file.Substring(prefix.Length);
            string source = OverviewProfileAssets.CheckedPath(sourceDirectory, relative), destination = OverviewProfileAssets.CheckedPath(targetDirectory, relative);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, false);
        }
    }

    internal static string PortableId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException(Loc.T("The profile ID is empty.", "方案标识为空。", "方案標識為空。"));
        string lower = id.ToLowerInvariant();
        bool reserved = lower == "con" || lower == "prn" || lower == "aux" || lower == "nul" ||
            lower.Length == 4 && (lower.StartsWith("com", StringComparison.Ordinal) || lower.StartsWith("lpt", StringComparison.Ordinal)) && lower[3] >= '1' && lower[3] <= '9';
        if (!reserved && id.Length <= 128 && id.All(c => c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_')) return id;
        using var hash = SHA256.Create();
        return "profile-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(id))).Replace("-", "").Substring(0, 32).ToLowerInvariant();
    }

    private static void WriteJson(string directory, string filename, JToken json) => File.WriteAllText(OverviewProfileAssets.CheckedPath(directory, filename), json.ToString(Formatting.Indented), new UTF8Encoding(false));
}
