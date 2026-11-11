#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Mono.Cecil;

namespace ModConfigEnhance.Overview;

/// <summary>Reads a stored revision only. Never imports, downloads, schedules a switch or writes its index.</summary>
internal static class StoredPackDetails
{
    private static readonly PackDetailsCache<PackTask> Cache = new();
    internal static string CacheKey(PackRecord pack) => Content(pack) + "|" + pack.ModifiedUtc + "|" + pack.Files.Count;
    internal static Task<PackTask> LoadAsync(PackRecord pack, bool refresh = false)
    {
        string key = CacheKey(pack), root = Content(pack);
        if (refresh) Cache.Invalidate(key);
        return Cache.Get(key, () => ReadCached(pack, root, key, refresh));
    }
    internal static void ClearMemoryForChecks(PackRecord pack) => Cache.Invalidate(CacheKey(pack));
    private sealed class DiskEntry
    {
        public int Schema = 3; // 3: language groups are also discovered for temporary snapshots
        public string Key = "", Intro = "";
        public string DisplayName = "", Version = "", CreatedUtc = "";
        public PackReport? Report;
    }
    // UI cache only: never writes the pack index, task or signed author material.
    private static PackTask ReadCached(PackRecord pack, string root, string key, bool refresh)
    {
        using var sha = SHA256.Create();
        string name = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "");
        string file = Path.Combine(PackLibrary.Home, ".view-cache", name + ".json");
        try
        {
            if (!refresh && File.Exists(file))
            {
                var hit = JsonConvert.DeserializeObject<DiskEntry>(File.ReadAllText(file));
                if (hit?.Schema == 3 && hit.Key == key && hit.Report != null)
                {
                    hit.Report.Name = pack.Name; hit.Report.Version = pack.Version;
                    return new PackTask { Id = "stored-" + key, State = "preview", Request = new PackRequest { Operation = "view-stored", PackId = pack.Id }, Proposed = WithDefaultLanguage(pack, hit.Report.Languages), Report = hit.Report, StagingDirectory = root, Message = hit.Intro };
                }
            }
        }
        catch (Exception e) when (e is IOException || e is JsonException) { }
        var result = Read(pack);
        result.Message = Introduction(result);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(new DiskEntry { Key = key, Report = result.Report, Intro = result.Message, DisplayName = pack.Name, Version = pack.Version, CreatedUtc = DateTime.UtcNow.ToString("O") }));
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        }
        catch (IOException) { } // A cache write failure must not prevent browsing.
        return result;
    }
    internal static string Content(PackRecord pack)
    {
        if (!PackLibrary.ValidId(pack.Id)) throw new IOException("整合包标识无效。");
        string[] parts = pack.Content.Replace((char)92, '/').Split('/');
        if (parts.Length != 2 || parts[0] != "revisions" || !PackLibrary.ValidId(parts[1])) throw new IOException("整合包内容路径无效。");
        return OverviewProfileAssets.CheckedPath(PackLibrary.Home, pack.Id + "/" + pack.Content);
    }
    internal static PackTask Read(PackRecord pack)
    {
        string root = Content(pack);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("已保存的整合包文件缺失：" + pack.Name);
        // Clone so changing a preview language never mutates the in-memory library record.
        var copy = JsonConvert.DeserializeObject<PackRecord>(JsonConvert.SerializeObject(pack))!;
        var report = copy.Report ?? new PackReport();
        report.Name = pack.Name; report.Version = pack.Version; report.FileCount = pack.Files.Count;
        report.Verdict = pack.Problems.Count == 0 ? "ready" : "attention";
        report.Headline = "已保存到整合包库。查看内容不会下载文件或切换整合包。";
        report.Kind = "stored";
        if (report.KindText.Length == 0) report.KindText = pack.PublicationId.Length == 0 ? "类型：临时整合包快照。" : "类型：已导入整合包。";
        report.Files = pack.Files.Select(f => new PackReportFile { Path = f.Path.Replace((char)92, '/'), Group = f.Path.StartsWith("config/", StringComparison.OrdinalIgnoreCase) ? "配置文件" : f.Path.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase) ? "启动补丁" : f.Path.IndexOf("/MCEPack/", StringComparison.OrdinalIgnoreCase) >= 0 ? "作者资料" : "模组文件" }).ToList();
        if (copy.Report == null)
        {
            report.Lines.AddRange(pack.Problems.Select(PackReportText.Problem));
            foreach (var file in report.Files)
            {
                string full = OverviewProfileAssets.CheckedPath(root, file.Path);
                if (!File.Exists(full)) continue;
                if (Path.GetFileName(full).Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    try { string version = JObject.Parse(File.ReadAllText(full)).Value<string>("version_number") ?? ""; if (version.Length > 0) report.FolderVersions[file.Path[..^"/manifest.json".Length]] = version; } catch (JsonException) { }
                }
                if (!PluginFileNames.IsAssembly(file.Path)) continue;
                try
                {
                    using var resolver = OverviewAssemblyReader.ResolverFor(full);
                    using var assembly = AssemblyDefinition.ReadAssembly(full, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false });
                    foreach (var type in assembly.MainModule.Types)
                    foreach (var a in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInPlugin" && a.ConstructorArguments.Count == 3))
                    {
                        string[] parts = file.Path.Split('/'); string version = a.ConstructorArguments[2].Value?.ToString() ?? "";
                        report.Lines.Add(new PackReportLine { Section = "mod", State = file.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "ok" : "info", Group = parts.Length > 2 ? parts[1] : "", Title = a.ConstructorArguments[1].Value + " " + version });
                        string folder = file.Path.Substring(0, file.Path.LastIndexOf('/')); if (!report.FolderVersions.ContainsKey(folder)) report.FolderVersions[folder] = version;
                    }
                }
                catch (Exception e) when (e is BadImageFormatException || e is AssemblyResolutionException) { }
            }
            report.ModCount = report.Lines.Count(l => l.Section == "mod"); report.EnabledCount = report.Lines.Count(l => l.Section == "mod" && l.State == "ok");
            foreach (var req in pack.Requirements) report.Lines.Add(new PackReportLine { Section = "prereq", State = "info", Title = req.Guid + " " + req.Version, Detail = "固定宿主依赖；切换时检查版本。" });
        }
        // Snapshot reports may contain file counts only. Recover mod identities from their saved assemblies as well.
        if (report.ModCount == 0 && copy.Report != null && pack.Files.Any(f => f.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var legacy = JsonConvert.DeserializeObject<PackRecord>(JsonConvert.SerializeObject(pack))!; legacy.Report = null;
            var recovered = Read(legacy).Report;
            report.Lines.AddRange(recovered.Lines.Where(l => l.Section == "mod"));
            report.ModCount = recovered.ModCount; report.EnabledCount = recovered.EnabledCount;
            foreach (var pair in recovered.FolderVersions) report.FolderVersions[pair.Key] = pair.Value;
        }
        // Packs, temporary snapshots and older snapshots share one discovery: author dictionaries inside the revision plus the snapshot's own workspace/language.
        if (report.Languages.Count == 0) report.Languages = Languages(pack, root);
        copy.Language = DefaultLanguage(copy, report.Languages);
        copy.Report = report;
        return new PackTask { Id = "stored-" + pack.Id + "-" + pack.Content, State = "preview", Request = new PackRequest { Operation = "view-stored", PackId = pack.Id }, Proposed = copy, Report = report, StagingDirectory = root };
    }
    private const string AuthorLanguageFolder = "/MCEPack/language/";
    /// <summary>
    /// Language groups of a stored revision, grouped by the shared PackLanguages rules. Read-only; never writes the library.
    /// Sources: the declared author root, any MCEPack/language folder listed in the revision (older temporary snapshots), and workspace/language (snapshots saved with the player's own dictionaries).
    /// </summary>
    internal static List<PackLanguage> Languages(PackRecord pack, string root)
    {
        var directories = new List<string>();
        void Add(string path) { if (path.Length > 0 && Directory.Exists(path) && !directories.Contains(path, StringComparer.OrdinalIgnoreCase)) directories.Add(path); }
        try { if (pack.AuthorRoot.Length > 0) Add(OverviewProfileAssets.CheckedPath(root, pack.AuthorRoot + "/language")); } catch (IOException) { }
        foreach (var file in pack.Files)
        {
            string path = file.Path.Replace((char)92, '/'); int at = path.IndexOf(AuthorLanguageFolder, StringComparison.OrdinalIgnoreCase);
            if (at < 0 || !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || path.IndexOf('/', at + AuthorLanguageFolder.Length) >= 0) continue;
            try { Add(OverviewProfileAssets.CheckedPath(root, path.Substring(0, at + AuthorLanguageFolder.Length - 1))); } catch (IOException) { }
        }
        try { Add(OverviewProfileAssets.CheckedPath(PackLibrary.Home, pack.Id + "/workspace/language")); } catch (IOException) { }
        var merged = new Dictionary<string, PackLanguage>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in directories)
            foreach (var group in PackLanguages.Scan(directory))
            {
                if (!merged.TryGetValue(group.Key, out var target)) merged[group.Key] = target = new PackLanguage { Key = group.Key, Code = group.Code };
                foreach (string name in group.Files) if (!target.Files.Contains(name, StringComparer.OrdinalIgnoreCase)) target.Files.Add(name);
                if (target.Name.Length == 0) target.Name = group.Name;
            }
        return merged.Values.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }
    /// <summary>The recorded language if the pack still offers it; otherwise the player's current selection, then the game language. Display only.</summary>
    private static string DefaultLanguage(PackRecord pack, IReadOnlyList<PackLanguage> languages)
    {
        if (languages.Count == 0 || languages.Any(l => string.Equals(l.Key, pack.Language, StringComparison.OrdinalIgnoreCase))) return pack.Language;
        string selection = Plugin.LanguageFile?.Value?.Trim().ToLowerInvariant() ?? "";
        return languages.FirstOrDefault(l => selection.Length > 0 && l.Key == selection)?.Key ?? PackLanguages.Choose(languages, Loc.FollowStem())?.Key ?? "";
    }
    private static PackRecord WithDefaultLanguage(PackRecord pack, IReadOnlyList<PackLanguage> languages)
    {
        string language = DefaultLanguage(pack, languages); if (language == pack.Language) return pack;
        var copy = JsonConvert.DeserializeObject<PackRecord>(JsonConvert.SerializeObject(pack))!; copy.Language = language; return copy;
    }
    internal static string Introduction(PackTask task)
    {
        if (task.Request.Operation == "view-stored" && task.Message.Length > 0) return task.Message;
        string root = task.Request.Operation == "view-stored" ? task.StagingDirectory : Path.Combine(task.StagingDirectory, "content");
        if (!Directory.Exists(root)) return "此整合包没有介绍。";
        // Prefer the author's default profile, then the first referenced introduction. Read text only, never execute embedded material.
        var files = task.Proposed.Files.Select(f => f.Path.Replace((char)92, '/')).Where(p => p.EndsWith("/introduction.md", StringComparison.OrdinalIgnoreCase)).ToList();
        string chosen = files.FirstOrDefault() ?? "";
        if (task.Proposed.AuthorRoot.Length > 0)
        {
            string release = OverviewProfileAssets.CheckedPath(root, task.Proposed.AuthorRoot + "/release.json");
            if (File.Exists(release))
            {
                string profile = JObject.Parse(File.ReadAllText(release)).Value<string>("defaultProfile") ?? "";
                chosen = files.FirstOrDefault(f => f.Contains("/profiles/" + profile + "/")) ?? chosen;
            }
        }
        if (chosen.Length == 0) return "此整合包没有介绍。";
        string path = OverviewProfileAssets.CheckedPath(root, chosen);
        return File.Exists(path) ? File.ReadAllText(path) : "介绍文件缺失。";
    }
}
