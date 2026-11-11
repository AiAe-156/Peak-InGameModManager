#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ModConfigEnhance.Overview.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

internal interface IContentFiles
{
    string Root { get; }
    string Notice { get; }
    string SaveLabel { get; }
    bool IsIntroduction(string relative);
    bool CanEdit(string relative);
    string? Resolve(string relative);
    string EditablePath(string relative);
    void SaveText(string relative, string text);
    void RegisterAssets(string directory, string relativeDirectory);
}
internal interface IContentReadTransform
{
    string DisplayText(string relative, string source);
}

// All editable publication files belong to this workspace. Original plugin files are read only.
internal sealed class PublicationDrafts : IContentFiles
{
    private readonly OverviewRepository _repo;
    private readonly PublishRequest _request;
    public string Root { get; }
    public string Notice => ScopeNotice;
    public string SaveLabel => Loc.T("Store publication changes", "存储本次发布修改", "儲存本次發布修改");
    internal int Revision { get; private set; }
    internal static string ScopeNotice => Loc.T("These changes apply to this publication only. The original profile stays unchanged. Generate the ZIP before closing the management center; this draft is not restored when it is reopened.",
        "修改仅用于本次发布，原方案保持原样。关闭管理中心后不会恢复本次草稿，请先生成 ZIP 留存。",
        "修改僅用於本次發布，原方案維持原樣。關閉管理中心後不會還原本次草稿，請先產生 ZIP 留存。");
    internal PublicationDrafts(OverviewRepository repo, PublishRequest request)
    { _repo = repo; _request = request; Root = OverviewProfileAssets.CheckedPath(repo.ConfigRootDirectory, "data/runtime/publication-drafts/" + Guid.NewGuid().ToString("N")); }
    internal string PayloadPrefix => _request.Mode == PublishMode.Data ? "plugins/" + ModConfigEnhance.Updating.ManagerIdentity.PackFolder + "/" : "BepInEx/plugins/" + _request.Name + "/" + ModConfigEnhance.Updating.ManagerIdentity.PackFolder + "/";
    public bool IsIntroduction(string relative) => relative.StartsWith(PayloadPrefix + "profiles/" + OverviewProfileExport.PortableId(_repo.ViewedProfile.publicationId.Length > 0 ? _repo.ViewedProfile.publicationId : _repo.ViewedProfileId) + "/", StringComparison.OrdinalIgnoreCase)
        && (relative.EndsWith("/introduction.md", StringComparison.OrdinalIgnoreCase) || relative.EndsWith("/introduction.mcedoc.json", StringComparison.OrdinalIgnoreCase));
    public bool CanEdit(string relative) => !relative.StartsWith(PayloadPrefix, StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith(PayloadPrefix + "language/", StringComparison.OrdinalIgnoreCase);
    public string? Resolve(string relative)
    {
        if (_request.DraftFiles.TryGetValue(relative, out var draft)) return draft;
        if (relative.StartsWith(PayloadPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string tail = relative.Substring(PayloadPrefix.Length);
            if (tail == "conflicts.json") return Materialize(relative, PackConflictStore.Serialize(_request.Conflicts));
            var items = _repo.PrepareExport(_request.Profiles);
            if (tail.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = tail.Split('/');
                var item = items.FirstOrDefault(i => OverviewProfileExport.PortableId(i.Id) == parts[1]);
                if (item == null || parts.Length < 3) return null;
                string child = string.Join("/", parts.Skip(2));
                if (child == "config-values.json")
                {
                    var selected = _repo.Profiles.ListProfiles().First(p => p.id == _request.DefaultProfile);
                    if (item.Id == (selected.publicationId.Length > 0 ? selected.publicationId : selected.id))
                    {
                        var config = JsonConvert.DeserializeObject<OverviewConfigSnapshot>(item.ConfigValuesJson)!;
                        ApplyConfigEdits(_request, config); item.ConfigValuesJson = JsonConvert.SerializeObject(config, Formatting.Indented);
                    }
                }
                string? text = child switch { "profile.json" => OverviewProfileExport.PreviewProfile(item), "config-values.json" => item.ConfigValuesJson,
                    "introduction.mcedoc.json" => DocJson.Write(item.PreparedDocument!), "introduction.md" => DocMarkdown.Export(item.PreparedDocument!), _ => null };
                return text != null ? Materialize(relative, text) : Existing(OverviewProfileAssets.CheckedPath(item.Directory, child));
            }
            if (tail.StartsWith("language/", StringComparison.OrdinalIgnoreCase))
            {
                string name = tail.Substring("language/".Length); var languages = Loc.SnapshotLanguages(_request.Languages);
                if (languages.TryGetValue(name, out var text)) return Materialize(relative, text);
            }
            if (tail == "profiles.json") return Materialize(relative, new JObject { ["schemaVersion"] = 1, ["profiles"] = new JArray(items.Select(i => new JObject { ["id"] = OverviewProfileExport.PortableId(i.Id), ["name"] = i.Name, ["directory"] = "profiles/" + OverviewProfileExport.PortableId(i.Id) })) }.ToString());
            return null; // Release, source and signature records are produced by the publisher.
        }
        if (relative.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase))
        {
            string config = relative.Substring("BepInEx/config/".Length);
            var profile = _repo.Profiles.ListProfiles().First(p => p.id == _request.DefaultProfile);
            if (PackPublisher.ProfileConfigFiles(profile.config).Contains(config, StringComparer.OrdinalIgnoreCase))
            {
                string folder = OverviewProfileAssets.CheckedPath(Root, "generated-config"); PackPublisher.ProjectConfig(folder, profile.config);
                return Existing(OverviewProfileAssets.CheckedPath(folder, config));
            }
        }
        if (relative == "README.md") return Materialize(relative, PackPublisher.Readme(_request));
        if (relative == "CHANGELOG.md") return Materialize(relative, PackPublisher.Changelog(_request));
        if (relative == "icon.png") return Existing(_request.Icon);
        return Existing(OverviewProfileAssets.CheckedPath(PackPublisher.GameRoot, relative));
    }
    private static string? Existing(string path) => File.Exists(path) ? path : null;
    private string Materialize(string relative, string text)
    {
        string path = OverviewProfileAssets.CheckedPath(Root, "preview/" + relative); OverviewRepository.AtomicWrite(path, text); return path;
    }
    public string EditablePath(string relative) => OverviewProfileAssets.CheckedPath(Root, "edited/" + relative);
    public void SaveText(string relative, string text)
    {
        if (!CanEdit(relative)) throw new InvalidOperationException(Loc.T("Edit this record in its management tab.", "请在对应管理标签里编辑这项资料。", "請在對應管理標籤編輯這項資料。"));
        if (relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) JToken.Parse(text);
        if (relative.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase) && !_request.ExtraConfigFiles.Contains(relative.Substring("BepInEx/config/".Length)))
        {
            var snapshot = JsonConvert.DeserializeObject<OverviewConfigSnapshot>(JsonConvert.SerializeObject(_repo.Profiles.ListProfiles().First(p => p.id == _request.DefaultProfile).config))!;
            ApplyConfigText(snapshot, relative.Substring("BepInEx/config/".Length), text); _repo.Configs.PrepareApply(snapshot).Validate();
        }
        string path = OverviewProfileAssets.CheckedPath(Root, "snapshots/" + Guid.NewGuid().ToString("N") + "/" + relative); OverviewRepository.AtomicWrite(path, text);
        _request.DraftFiles[relative] = path; Revision++;
    }
    public void RegisterAssets(string directory, string relativeDirectory)
    {
        foreach (string path in OverviewRepository.SafeFiles(directory))
        {
            string relative = path.Substring(directory.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar).Replace('\\', '/');
            if (OverviewProfileAssets.SupportedMedia(relative) || relative.StartsWith("fonts/", StringComparison.OrdinalIgnoreCase))
            {
                string frozen = OverviewProfileAssets.CheckedPath(Root, "snapshots/" + Guid.NewGuid().ToString("N") + "/" + relative);
                Directory.CreateDirectory(Path.GetDirectoryName(frozen)!); File.Copy(path, frozen, false); _request.DraftFiles[relativeDirectory + relative] = frozen;
            }
        }
        Revision++;
    }
    internal static void ApplyConfigEdits(PublishRequest request, OverviewConfigSnapshot snapshot)
    {
        foreach (var pair in request.DraftFiles.Where(p => p.Key.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase)))
        {
            string relative = pair.Key.Substring("BepInEx/config/".Length);
            if (!request.ExtraConfigFiles.Contains(relative)) ApplyConfigFile(snapshot, relative, pair.Value);
        }
    }
    private static void ApplyConfigFile(OverviewConfigSnapshot snapshot, string relative, string path)
        => ApplyConfigText(snapshot, relative, File.ReadAllText(path, Encoding.UTF8));
    private static void ApplyConfigText(OverviewConfigSnapshot snapshot, string relative, string text)
    {
        var values = snapshot.values.Where(v => v.configFile.Replace('\\', '/').Equals(relative, StringComparison.OrdinalIgnoreCase)).ToList();
        if (values.Count == 0) return;
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal); string section = "";
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
            int equals = line.IndexOf('=');
            if (equals <= 0 || !parsed.TryAdd(section + "\n" + line.Substring(0, equals).Trim(), line.Substring(equals + 1).Trim()))
                throw new InvalidDataException(Loc.T("Invalid or duplicate configuration entry.", "配置格式无效，或存在重复配置项。", "設定格式無效，或有重複設定項目。"));
        }
        if (parsed.Count != values.Count || values.Any(v => !parsed.ContainsKey(v.section + "\n" + v.key)))
            throw new InvalidDataException(Loc.T("Keep the recorded sections and keys; edit their values only.", "请保留已收录的分区和配置项名称，只修改值。", "請保留已收錄的區段和設定名稱，只修改值。"));
        foreach (var value in values) value.serializedValue = parsed[value.section + "\n" + value.key];
    }
    internal static void Apply(PublishRequest request, string destinationRoot, string prefix, string? skipPrefix = null)
    {
        foreach (var pair in request.DraftFiles)
        {
            if (!pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || skipPrefix != null && pair.Key.StartsWith(skipPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            string destination = OverviewProfileAssets.CheckedPath(destinationRoot, pair.Key.Substring(prefix.Length));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(pair.Value, destination, true);
        }
    }
}

internal sealed class PublicationDocumentSource : IDocumentEditorSource
{
    private readonly IContentFiles _drafts;
    private readonly string _relative, _original;
    private readonly Action? _stored;
    public string RootDirectory => Path.GetDirectoryName(_drafts.EditablePath(_relative))!;
    public string ExportDirectory => _drafts.Root;
    public string? ReadNote => null;
    internal PublicationDocumentSource(IContentFiles drafts, string relative, string original, Action? stored = null) { _drafts = drafts; _relative = relative; _original = original; _stored = stored; }
    public Document Load()
    {
        string text = File.ReadAllText(_original, Encoding.UTF8);
        if (_drafts is IContentReadTransform transform) text = transform.DisplayText(_relative, text);
        return DocMarkdown.Import(text);
    }
    public Document ImportMarkdown(string source) => DocMarkdown.Import(source);
    public void Save(Document document) { _drafts.SaveText(_relative, DocMarkdown.Export(document)); _drafts.RegisterAssets(RootDirectory, _relative.Substring(0, _relative.LastIndexOf('/') + 1)); _stored?.Invoke(); }
    public string Export() => _drafts.EditablePath(_relative);
    public string ResolveMedia(string relative)
    {
        try
        {
            string own = OverviewProfileAssets.CheckedPath(RootDirectory, relative); if (File.Exists(own)) return own;
            string original = OverviewProfileAssets.CheckedPath(Path.GetDirectoryName(_original)!, relative); if (File.Exists(original)) return original;
            string folder = _relative.Substring(0, _relative.LastIndexOf('/') + 1);
            return OverviewProfileAssets.CheckedPath(PackPublisher.GameRoot, folder + relative);
        }
        catch { return ""; }
    }
}
