#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>Profile persistence only. Selecting or saving never changes a live mod/config.</summary>
internal sealed partial class OverviewProfileStore
{
    private const int SchemaVersion = 1;
    private readonly object _gate = new();
    private readonly string _pluginsRoot;
    public string RootDirectory { get; }
    /// <summary>同级的运行目录：整合包工作区是 workspace\runtime；未托管时 profiles 在 config\ModConfigEnhance 下，运行目录是 data\runtime（2.5.2）。</summary>
    public string RuntimeDirectory => SamePath(Path.GetDirectoryName(RootDirectory)!, Loc.RootDirectory)
        ? Updating.MceDirectories.Runtime(Path.GetDirectoryName(RootDirectory)!) : Path.Combine(Path.GetDirectoryName(RootDirectory)!, "runtime");
    private static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
    public string IndexPath => Path.Combine(RootDirectory, "index.json");
    private OverviewProfileIndex _index = new();
    private bool _indexRecovered;
    private readonly Dictionary<string, OverviewProfile> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public OverviewProfileStore(string? rootDirectory = null, string? pluginsDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(Paths.ConfigPath, ModConfigEnhance.Updating.ManagerIdentity.ConfigDirectory, "profiles");
        _pluginsRoot = pluginsDirectory ?? Paths.PluginPath;
    }

    public OverviewProfile Viewed => GetRequired(_index.viewedProfileId);
    public OverviewProfile Applied => GetRequired(_index.appliedProfileId);
    public string ViewedId => _index.viewedProfileId;
    public string AppliedId => _index.appliedProfileId;

    public OverviewProfileInitializationResult Initialize(OverviewState legacyState, string? legacyResourceRoot,
        OverviewConfigSnapshot initialConfigSnapshot, IEnumerable<OverviewMod> mods)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(RuntimeDirectory);
            bool existed = File.Exists(IndexPath);
            _index = ReadIndex();
            _loaded.Clear();

            var modList = SnapshotMods(mods).ToList();
            OverviewProfile @default;
            OverviewProfile current;
            if (!existed || !_index.profiles.Any(p => p.id.Equals("default", StringComparison.OrdinalIgnoreCase)))
            {
                string defaultDirectory = Path.Combine(RootDirectory, "default");
                if (File.Exists(Path.Combine(defaultDirectory, "profile.json")))
                {
                    var existingDefault = new OverviewProfileReference { id = "default", name = OverviewProfileNames.DefaultName, kind = OverviewProfileKind.Default, directory = defaultDirectory };
                    _index.profiles.Add(existingDefault); @default = Load(existingDefault);
                }
                else
                {
                    @default = NewProfile("default", OverviewProfileNames.DefaultName, OverviewProfileKind.Default,
                        CleanState(legacyState, clearGroupsAndPack: true), Clone(initialConfigSnapshot), modList);
                    SaveFiles(@default); UpsertReference(@default);
                }
            }
            else @default = Load(_index.profiles.First(p => p.id.Equals("default", StringComparison.OrdinalIgnoreCase)));

            OverviewProfileReference? existingCurrent = _index.profiles.FirstOrDefault(p => p.id.Equals("current", StringComparison.OrdinalIgnoreCase))
                ?? _index.profiles.FirstOrDefault(p => p.kind == OverviewProfileKind.Personal && p.id.Equals(_index.appliedProfileId, StringComparison.OrdinalIgnoreCase))
                ?? _index.profiles.FirstOrDefault(p => p.kind == OverviewProfileKind.Personal);
            if (!existed)
            {
                string currentName = !string.IsNullOrWhiteSpace(legacyState.packNameOverride) ? legacyState.packNameOverride
                    : !string.IsNullOrWhiteSpace(legacyState.pack.name) ? legacyState.pack.name : Loc.T("Current configuration", "现有配置", "現有配置");
                current = NewProfile(Guid.NewGuid().ToString("N"), currentName, OverviewProfileKind.Personal,
                    CleanState(legacyState, clearGroupsAndPack: false), Clone(initialConfigSnapshot), modList);
                SaveFiles(current);
                CopyLegacyResources(legacyResourceRoot, current.directory);
                UpsertReference(current);
                _index.viewedProfileId = current.id;
                _index.appliedProfileId = current.id;
                // A fresh workspace of an installed (non-snapshot) modpack: this profile is the pack's own configuration, not something the player authored.
                if (ManagedPackId() is string originPack) RecordOrigin(current.id, originPack);
            }
            else current = existingCurrent == null ? @default : Load(existingCurrent);

            ScanAuthorProfiles();
            RepairSelection();
            SaveIndex();
            return new OverviewProfileInitializationResult { Default = @default, Current = current, MigratedLegacy = !existed };
        }
    }

    // ---------------- Where a profile came from ----------------
    // Stored beside the index (origins.json: profile id -> pack id) so the profile format and published data stay untouched.
    // Only profiles generated automatically from a modpack's installed configuration are recorded; profiles the player creates, copies or imports never are.
    private string OriginsPath => Path.Combine(RootDirectory, "origins.json");
    private Dictionary<string, string>? _origins;

    private Dictionary<string, string> Origins()
    {
        if (_origins != null) return _origins;
        _origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(OriginsPath) && JObject.Parse(File.ReadAllText(OriginsPath, Encoding.UTF8))["origins"] is JObject items)
                foreach (var pair in items) _origins[pair.Key] = pair.Value?.Value<string>() ?? "";
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[方案] 来源记录读取失败，按个人配置显示：" + ex.Message); }
        return _origins;
    }

    private void RecordOrigin(string profileId, string packId)
    {
        Origins()[profileId] = packId; SaveOrigins();
    }

    private void SaveOrigins()
    {
        try { AtomicWrite(OriginsPath, new JObject { ["schemaVersion"] = 1, ["origins"] = JObject.FromObject(Origins()) }.ToString(Formatting.Indented)); }
        catch (Exception ex) { Plugin.Log.LogWarning("[方案] 来源记录保存失败：" + ex.Message); }
    }

    /// <summary>Id of the active installed modpack when this store is that pack's workspace. Temporary snapshots are the player's own setup and never count.</summary>
    private string? ManagedPackId()
    {
        try
        {
            var pack = PackLibrary.Active;
            if (pack == null || pack.Report?.Kind == "snapshot") return null;
            string own = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar), workspace = Path.GetFullPath(PackLibrary.ProfileRoot).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(own, workspace, StringComparison.OrdinalIgnoreCase) ? pack.Id : null;
        }
        catch { return null; }
    }

    /// <summary>True for author (read-only) profiles and for the profile created automatically from a modpack's installed configuration. Default and the player's own profiles are personal.</summary>
    public bool IsPackConfiguration(OverviewProfile profile)
    {
        lock (_gate)
        {
            if (profile.kind == OverviewProfileKind.Author || profile.readOnly && profile.kind != OverviewProfileKind.Default || HasAuthorWorkingProfile(profile.id)) return true;
            if (profile.kind != OverviewProfileKind.Personal || profile.id.Length == 0) return false;
            // The record only counts inside the workspace of the pack it names: a snapshot copies this folder, and its copy must read as personal.
            if (Origins().TryGetValue(profile.id, out string? origin)) return origin.Length > 0 && string.Equals(origin, ManagedPackId(), StringComparison.OrdinalIgnoreCase);
            return LooksAutoGenerated(profile);
        }
    }

    /// <summary>
    /// Workspaces created before origins were recorded: recognised only when every sign of the automatic creation is present
    /// (installed non-snapshot pack, untouched generated name, no source, created in the same instant as Default). Evaluated in memory, never written.
    /// </summary>
    private bool LooksAutoGenerated(OverviewProfile profile)
    {
        if (profile.source.Length > 0 || ManagedPackId() == null) return false;
        if (profile.name != "Current configuration" && profile.name != "现有配置" && profile.name != "現有配置") return false;
        var @default = _index.profiles.FirstOrDefault(p => p.id.Equals("default", StringComparison.OrdinalIgnoreCase));
        var reference = _index.profiles.FirstOrDefault(p => p.id.Equals(profile.id, StringComparison.OrdinalIgnoreCase));
        return @default != null && reference != null && Math.Abs((reference.createdUtc - @default.createdUtc).TotalSeconds) < 5;
    }

    public IReadOnlyList<OverviewProfile> ListProfiles()
    {
        lock (_gate) return _index.profiles.Select(Load).OrderBy(p => p.kind).ThenBy(p => p.name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public OverviewProfile Select(string id)
    {
        lock (_gate)
        {
            OverviewProfile profile = GetRequired(id);
            if (!profile.IsValid) throw new InvalidOperationException(Loc.F("Profile data could not be read and cannot be overwritten. Restore valid data first: {0}", "方案资料读取失败，不能覆盖；请先恢复有效资料：{0}", "方案資料讀取失敗，不能覆蓋；請先恢復有效資料：{0}", profile.loadError));
            _index.viewedProfileId = profile.id;
            SaveIndex();
            return profile;
        }
    }

    public OverviewProfileSaveResult Save(string id, OverviewState state, OverviewConfigSnapshot config,
        IEnumerable<OverviewMod>? mods = null, string? forkName = null, string? unifiedName = null)
    {
        lock (_gate)
        {
            if (config == null || config.schemaVersion != SchemaVersion || config.values == null || config.files == null) throw new InvalidOperationException(Loc.T("Invalid configuration snapshot format. Cannot save.", "配置快照格式无效，不能保存。", "配置快照格式無效，不能保存。"));
            OverviewProfile profile = GetRequired(id);
            if (!profile.IsValid) throw new InvalidDataException(profile.loadError);
            bool forked = false;
            if (profile.readOnly || profile.kind == OverviewProfileKind.Author) profile = AuthorWorkingProfile(profile);
            // Do not mutate the cached committed generation before disk persistence succeeds.
            profile = Clone(profile);
            if (unifiedName != null) profile.name = NormalizeName(unifiedName);
            profile.state = CleanState(state, clearGroupsAndPack: false);
            if (profile.kind == OverviewProfileKind.Default)
            {
                profile.state.pack.id = "default"; profile.state.pack.name = OverviewProfileNames.DefaultName;
                profile.state.pack.introEnabled = false; profile.state.pack.introFile = ""; profile.state.pack.introDocument = "";
                profile.state.pack.sourceNamespace = ""; profile.state.pack.sourceName = "";
            }
            profile.config = Clone(config);
            if (mods != null)
            {
                var present = SnapshotMods(mods).ToList();
                foreach (OverviewProfileMod missing in profile.mods.Where(old => !present.Any(now => SameMod(old, now))))
                { OverviewProfileMod kept = Clone(missing); kept.installed = false; present.Add(kept); }
                profile.mods = present;
            }
            profile.modifiedUtc = DateTime.UtcNow;
            SaveFiles(profile);
            UpsertReference(profile);
            _index.viewedProfileId = profile.id;
            SaveIndex();
            return new OverviewProfileSaveResult { Profile = profile, Forked = forked };
        }
    }

    /// <summary>Persist only an installation's grouping delta, never the UI's unrelated draft.</summary>
    internal void SaveInstallationGrouping(string id, Action<OverviewState> apply)
    {
        lock (_gate)
        {
            var profile = Clone(GetRequired(id));
            if (!profile.IsValid) throw new InvalidOperationException("Installation grouping requires a valid configuration: " + id);
            if (profile.readOnly || profile.kind == OverviewProfileKind.Author) profile = AuthorWorkingProfile(profile);
            apply(profile.state);
            profile.modifiedUtc = DateTime.UtcNow;
            SaveFiles(profile);
            UpsertReference(profile);
            SaveIndex();
        }
    }

    public OverviewProfileSaveResult Save(OverviewState state, OverviewConfigSnapshot config,
        IEnumerable<OverviewMod>? mods = null, string? forkName = null) => Save(ViewedId, state, config, mods, forkName);

    public OverviewProfile Copy(string sourceId, string name)
    {
        lock (_gate)
        {
            OverviewProfile result = CopyInternal(GetRequired(sourceId), name);
            _index.viewedProfileId = result.id;
            SaveIndex();
            return result;
        }
    }

    public OverviewProfile ImportDirectory(string directory)
    {
        lock (_gate)
        {
            string sourceRoot = ValidateImportDirectory(directory);
            string profilePath = Path.Combine(sourceRoot, "profile.json");
            string configPath = Path.Combine(sourceRoot, "config-values.json");
            // Keep the import path/link validation, then read the committed pair (including old two-file exports).
            _ = ReadImportObject(profilePath);
            if (File.Exists(configPath)) _ = ReadImportObject(configPath);
            var importedPair = ProfilePairPersistence.Read(profilePath, configPath);
            JObject profileJson = importedPair.Profile;
            JObject configJson = importedPair.Config;
            RequireImportSchema(profileJson, profilePath);
            RequireImportSchema(configJson, configPath);

            var item = new OverviewProfileExport.ExportItem
            {
                Id = profileJson.Value<string>("id") ?? "imported",
                Name = profileJson.Value<string>("name") ?? "",
                Directory = sourceRoot,
                ProfileJson = profileJson.ToString(Formatting.None),
                ConfigValuesJson = configJson.ToString(Formatting.None)
            };
            OverviewProfileExport.Prepare(item);
            OverviewConfigSnapshot config = DeserializeJson<OverviewConfigSnapshot>(item.ConfigValuesJson);
            if (config.schemaVersion != SchemaVersion || config.values == null || config.files == null)
                throw new InvalidDataException(Loc.T("The configuration snapshot is incomplete.", "配置快照内容不完整。", "設定快照內容不完整。"));
            OverviewProfile importedSource = DeserializeJson<OverviewProfile>(item.ProfileJson);
            if (!GroupTree.SupportedSchema(importedSource.schemaVersion))
                throw new InvalidDataException(Loc.T("The profile data format is unsupported.", "方案资料格式不受支持。", "方案資料格式不受支持。"));

            string id;
            do { id = Guid.NewGuid().ToString("N"); }
            while (_index.profiles.Any(p => p.id.Equals(id, StringComparison.OrdinalIgnoreCase))
                || Directory.Exists(Path.Combine(RootDirectory, id)) || File.Exists(Path.Combine(RootDirectory, id)));
            OverviewState state = CleanState(importedSource.state, clearGroupsAndPack: false);
            var imported = NewProfile(id, importedSource.name, OverviewProfileKind.Personal, state, config, importedSource.mods ?? new());
            imported.source = "";
            imported.readOnly = false;
            imported.modifiedUtc = DateTime.UtcNow;

            string[] resourceFiles = OverviewProfileExport.ResourceFiles(item).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var copies = new HashSet<string>(resourceFiles, StringComparer.OrdinalIgnoreCase);
            AddIntroductionFile(copies, sourceRoot, state.pack.introDocument, "introduction.mcedoc.json");
            AddIntroductionFile(copies, sourceRoot, state.pack.introFile, "introduction.md");

            // The new GUID directory is intentionally retained if a copy fails, but it is not
            // made visible as a usable profile until every referenced file is safely copied.
            SaveFiles(imported);
            CopyResources(sourceRoot, imported.directory, copies);
            UpsertReference(imported);
            try { SaveIndex(); }
            catch
            {
                _index.profiles.RemoveAll(p => p.id.Equals(id, StringComparison.OrdinalIgnoreCase));
                _loaded.Remove(id);
                throw;
            }
            return imported;
        }
    }

    public OverviewProfile Fork(string sourceId, string? name = null)
    {
        lock (_gate) { var source = GetRequired(sourceId); var copy = CopyInternal(source, name ?? (source.name + Loc.T(" - personal copy", " - 个人副本", " - 個人副本")), true); _index.viewedProfileId = copy.id; SaveIndex(); return copy; }
    }

    public OverviewProfile Rename(string id, string name)
    {
        lock (_gate)
        {
            OverviewProfile profile = GetRequired(id);
            if (profile.readOnly || profile.kind == OverviewProfileKind.Author) throw new InvalidOperationException(Loc.T("Author profiles are read-only. Create a personal copy first.", "作者方案只读；请先创建个人副本。", "作者方案只讀；請先創建個人副本。"));
            if (!profile.IsValid) throw new InvalidDataException(profile.loadError);
            name = NormalizeName(name);
            profile = Clone(profile);
            profile.name = name;
            profile.modifiedUtc = DateTime.UtcNow;
            SaveFiles(profile);
            UpsertReference(profile);
            SaveIndex();
            return profile;
        }
    }

    /// <summary>Must only be called after explicit UI confirmation. Content is moved to a recoverable trash folder.</summary>
    public string Trash(string id)
    {
        lock (_gate)
        {
            OverviewProfile profile = GetRequired(id);
            if (profile.id.Equals("default", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(Loc.T("The Default profile cannot be removed.", "Default 方案不能回收。", "Default 方案不能回收。"));
            if (profile.readOnly || profile.kind == OverviewProfileKind.Author) throw new InvalidOperationException(Loc.T("Author profiles come from the mod directory and cannot be removed here.", "作者方案来自模组目录，不能回收。", "作者方案來自模組目錄，不能回收。"));
            string source = SafeDirectory(profile.directory);
            string trash = Path.Combine(RootDirectory, ".trash", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + SafeSegment(profile.id));
            Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
            if (Directory.Exists(source)) Directory.Move(source, trash);
            _index.profiles.RemoveAll(p => p.id.Equals(profile.id, StringComparison.OrdinalIgnoreCase));
            _loaded.Remove(profile.id);
            if (Origins().Remove(profile.id)) SaveOrigins();
            RepairSelection();
            SaveIndex();
            return trash;
        }
    }

    public void MarkApplied(string id)
    {
        lock (_gate) { _ = GetRequired(id); _index.appliedProfileId = id; SaveIndex(); }
    }

    public bool IsDirty(string id, OverviewState draftState, OverviewConfigSnapshot draftConfig)
    {
        OverviewProfile stored = GetRequired(id);
        string a = JsonConvert.SerializeObject(new { state = CleanState(draftState, false), config = ComparableValues(draftConfig) });
        string b = JsonConvert.SerializeObject(new { state = CleanState(stored.state, false), config = ComparableValues(stored.config) });
        return !string.Equals(a, b, StringComparison.Ordinal);
    }

    private OverviewProfile CopyInternal(OverviewProfile source, string name, bool preservePublication = false)
    {
        if (!source.IsValid) throw new InvalidOperationException(Loc.F("Profile data could not be read. Empty data cannot be copied: {0}", "方案资料读取失败，不能复制空资料：{0}", "方案資料讀取失敗，不能復制空資料：{0}", source.loadError));
        string id = Guid.NewGuid().ToString("N");
        var copy = NewProfile(id, NormalizeName(name), OverviewProfileKind.Personal, Clone(source.state), Clone(source.config), Clone(source.mods));
        copy.source = source.readOnly ? source.id : source.source;
        if (!preservePublication) copy.source = "";
        if (preservePublication) copy.publicationId = source.publicationId.Length > 0 ? source.publicationId : source.id.Substring(source.id.LastIndexOf(':') + 1);
        SaveFiles(copy);
        CopyResources(source.directory, copy.directory);
        UpsertReference(copy);
        return copy;
    }

    private void ScanAuthorProfiles()
    {
        if (!Directory.Exists(_pluginsRoot)) return;
        foreach (string manifestPath in PublishedPack.Packs.Select(p => Path.Combine(p.Root, "profiles.json")))
        {
            string? packDir = Path.GetDirectoryName(manifestPath);
            if (packDir == null || !ModConfigEnhance.Updating.ManagerIdentity.IsPackFolder(Path.GetFileName(packDir))) continue;
            if (!PublishedPack.CanRead(packDir)) continue;
            try
            {
                JToken root = JToken.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
                if (root is JObject obj && obj["schemaVersion"] is JToken versionToken &&
                    (versionToken.Type != JTokenType.Integer || versionToken.Value<int>() != SchemaVersion))
                    throw new InvalidDataException(Loc.T("Author profile index schemaVersion must be integer 1.", "作者方案索引 schemaVersion 必须是整数 1。", "作者方案索引 schemaVersion 必須是整數 1。"));
                JArray list = root as JArray ?? (root["profiles"] as JArray ?? new JArray());
                string source = RelativePluginPath(packDir);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var references = new List<OverviewProfileReference>();
                foreach (JToken item in list)
                {
                    string id = item.Value<string>("id")?.Trim() ?? "";
                    if (id.Length == 0) continue;
                    if (!seen.Add(id)) throw new InvalidDataException(Loc.F("Duplicate ID in author profile index: {0}", "作者方案索引包含重复 ID：{0}", "作者方案索引包含重復 ID：{0}", id));
                    string relative = item.Value<string>("path")?.Trim() ?? item.Value<string>("directory")?.Trim() ?? Path.Combine("profiles", id);
                    string directory = SafeChild(packDir, relative);
                    if (directory.Length == 0 || !Directory.Exists(directory)) continue;
                    string key = "author:" + StableAuthorSource(packDir) + ":" + id.ToLowerInvariant();
                    var reference = new OverviewProfileReference
                    {
                        id = key, name = item.Value<string>("name")?.Trim() ?? id, kind = OverviewProfileKind.Author,
                        directory = directory, source = source, readOnly = true
                    };
                    references.Add(reference);
                }
                foreach (var reference in references) { UpsertAuthorReference(reference); _loaded.Remove(reference.id); }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[方案] 作者方案索引读取失败，已跳过并保留原文件：" + manifestPath + "：" + ex.Message); }
        }
        ScanLegacyAuthorPacks();
    }

    private OverviewProfile Load(OverviewProfileReference reference)
    {
        // Personal profiles belong to this installation. Old absolute paths may still
        // resolve through a Steam junction after the game has moved.
        if (!reference.readOnly && reference.kind != OverviewProfileKind.Author && reference.id.Length > 0 && SafeSegment(reference.id) == reference.id)
        {
            string localDirectory = Path.Combine(RootDirectory, reference.id);
            if (File.Exists(Path.Combine(localDirectory, "profile.json")) || File.Exists(Path.Combine(localDirectory, "profile.json.lastvalid")))
                reference.directory = localDirectory;
        }
        if (!reference.readOnly && reference.id.StartsWith("author:", StringComparison.OrdinalIgnoreCase))
        {
            string working = AuthorWorkingDirectory(reference.id);
            if (!HasAuthorWorkingProfile(reference.id)) throw new InvalidDataException("Author working profile has no verified local origin record.");
            reference.directory = working;
        }
        if (reference.readOnly && !PublishedPack.CanRead(reference.directory)) return new OverviewProfile { id = reference.id, name = reference.name, directory = reference.directory, source = reference.source, readOnly = true, kind = reference.kind, loadError = Loc.T("Author data validation failed. Personal configuration was preserved.", "作者发布资料校验失败，个人配置保持原样。", "作者發布資料校驗失敗，個人配置保持原樣。") };
        if (_loaded.TryGetValue(reference.id, out var cached)) { OverviewProfileNames.Normalize(cached); reference.name = cached.name; return cached; }
        try
        {
            if (reference.legacyFile.Length > 0)
            {
                var legacy = ReadLegacyProfile(reference); OverviewProfileNames.Normalize(legacy, true); reference.name = legacy.name;
                return _loaded[reference.id] = legacy;
            }
            string profilePath = Path.Combine(reference.directory, "profile.json");
            string configPath = Path.Combine(reference.directory, "config-values.json");
            var pair = ProfilePairPersistence.Read(profilePath, configPath);
            var profile = DeserializeJson<OverviewProfile>(pair.Profile.ToString(Formatting.None));
            if (profile.publicationId.Length == 0) profile.publicationId = profile.id.Substring(profile.id.LastIndexOf(':') + 1);
            if (!GroupTree.SupportedSchema(profile.schemaVersion)) throw new InvalidDataException(Loc.F("Unsupported profile version {0}", "不支持的方案版本 {0}", "不支持的方案版本 {0}", profile.schemaVersion));
            profile.id = reference.id; profile.name = reference.name.Length > 0 ? reference.name : profile.name;
            profile.kind = reference.kind; profile.directory = reference.directory; profile.source = reference.source; profile.readOnly = reference.readOnly;
            profile.config = DeserializeJson<OverviewConfigSnapshot>(pair.Config.ToString(Formatting.None));
            if (profile.config.schemaVersion != SchemaVersion) throw new InvalidDataException(Loc.F("Unsupported configuration snapshot version {0}", "不支持的配置快照版本 {0}", "不支持的配置快照版本 {0}", profile.config.schemaVersion));
            profile.state ??= new(); profile.mods ??= new(); profile.config.values ??= new(); profile.config.files ??= new();
            OverviewProfileNames.Normalize(profile, true); reference.name = profile.name;
            _loaded[reference.id] = profile;
            return profile;
        }
        catch (Exception ex)
        {
            string lastValid = Path.Combine(reference.directory, "profile.json.lastvalid");
            string lastValidConfig = Path.Combine(reference.directory, "config-values.json.lastvalid");
            if (File.Exists(lastValid))
            {
                try
                {
                    var pair = ProfilePairPersistence.Read(lastValid, lastValidConfig);
                    var recovered = DeserializeJson<OverviewProfile>(pair.Profile.ToString(Formatting.None));
                    recovered.config = DeserializeJson<OverviewConfigSnapshot>(pair.Config.ToString(Formatting.None));
                    recovered.id = reference.id; recovered.name = reference.name; recovered.kind = reference.kind; recovered.directory = reference.directory;
                    recovered.source = reference.source; recovered.readOnly = reference.readOnly; recovered.loadError = Loc.T("The current file is damaged. The last valid version was loaded: ", "当前文件损坏，已载入最后有效版本：", "當前文件損壞，已載入最后有效版本：") + ex.Message;
                    OverviewProfileNames.Normalize(recovered, true); reference.name = recovered.name;
                    return _loaded[reference.id] = recovered;
                }
                catch { }
            }
            return _loaded[reference.id] = new OverviewProfile { id = reference.id, name = reference.name, kind = reference.kind,
                directory = reference.directory, source = reference.source, readOnly = true, loadError = ex.Message };
        }
    }

    private void SaveFiles(OverviewProfile profile)
    {
        if (profile.readOnly || profile.kind == OverviewProfileKind.Author) throw new InvalidOperationException(Loc.T("Published author data is read-only. Create a personal copy first.", "作者发布资料只读，请先创建个人副本。", "作者發布資料只讀，請先創建個人副本。"));
        OverviewProfileNames.Normalize(profile);
        string configPath = Path.Combine(profile.directory, "config-values.json");
        if (profile.config == null || profile.config.schemaVersion != SchemaVersion || profile.config.values == null || profile.config.files == null)
            throw new InvalidOperationException(Loc.T("Invalid configuration snapshot format. The existing profile cannot be overwritten.", "配置快照格式无效，不能覆盖已有方案。", "配置快照格式無效，不能覆蓋已有方案。"));
        Directory.CreateDirectory(profile.directory);
        string profilePath = Path.Combine(profile.directory, "profile.json");
        var persisted = Clone(profile); persisted.config = new OverviewConfigSnapshot(); persisted.directory = ""; persisted.loadError = "";
        persisted.schemaVersion = GroupTree.SchemaFor(persisted.state?.groups);
        ProfilePairPersistence.Write(profile.directory, JObject.FromObject(persisted), JObject.FromObject(profile.config));
        profile.loadError = "";
        _loaded[profile.id] = profile;
    }

    private OverviewProfileIndex ReadIndex()
    {
        if (!File.Exists(IndexPath)) return new OverviewProfileIndex();
        try
        {
            var index = ReadStrictVersioned<OverviewProfileIndex>(IndexPath);
            if (index.schemaVersion != SchemaVersion) throw new InvalidDataException(Loc.F("Unsupported profile index version {0}", "不支持的方案索引版本 {0}", "不支持的方案索引版本 {0}", index.schemaVersion));
            index.profiles ??= new();
            return index;
        }
        catch (Exception ex)
        {
            string lastValid = IndexPath + ".lastvalid";
            if (File.Exists(lastValid))
            {
                var recovered = ReadStrictVersioned<OverviewProfileIndex>(lastValid);
                if (recovered.schemaVersion == SchemaVersion) { _indexRecovered = true; return recovered; }
            }
            throw new InvalidDataException(Loc.T("The profile index is damaged and no last-valid copy is available. The original was kept and initialization stopped.", "方案索引损坏且没有可用的 last-valid，已保留原文件并停止初始化。", "方案索引損壞且沒有可用的 last-valid，已保留原文件并停止初始化。"), ex);
        }
    }

    private void SaveIndex()
    {
        string json = JsonConvert.SerializeObject(_index, Formatting.Indented);
        if (!_indexRecovered && File.Exists(IndexPath) && File.ReadAllText(IndexPath) == json)
        {
            if (!File.Exists(IndexPath + ".lastvalid")) File.Copy(IndexPath, IndexPath + ".lastvalid", false);
            return;
        }
        if (!_indexRecovered && File.Exists(IndexPath)) File.Copy(IndexPath, IndexPath + ".lastvalid", true);
        AtomicWrite(IndexPath, json);
        if (!File.Exists(IndexPath + ".lastvalid")) File.Copy(IndexPath, IndexPath + ".lastvalid", false);
        _indexRecovered = false;
    }

    private void UpsertReference(OverviewProfile profile)
    {
        var reference = _index.profiles.FirstOrDefault(p => p.id.Equals(profile.id, StringComparison.OrdinalIgnoreCase));
        if (reference == null) { reference = new OverviewProfileReference(); _index.profiles.Add(reference); }
        reference.id = profile.id; reference.name = profile.name; reference.kind = profile.kind; reference.directory = profile.directory;
        reference.source = profile.source; reference.readOnly = profile.readOnly; reference.createdUtc = profile.createdUtc; reference.modifiedUtc = profile.modifiedUtc;
    }

    private void UpsertAuthorReference(OverviewProfileReference incoming)
    {
        if (HasAuthorWorkingProfile(incoming.id) && _index.profiles.Any(p => p.id == incoming.id && !p.readOnly)) return;
        OverviewProfileReference? existing = _index.profiles.FirstOrDefault(p => p.id.Equals(incoming.id, StringComparison.OrdinalIgnoreCase));
        if (existing == null) _index.profiles.Add(incoming);
        else
        {
            existing.name = incoming.name; existing.kind = OverviewProfileKind.Author; existing.directory = incoming.directory;
            existing.source = incoming.source; existing.readOnly = true; existing.legacyFile = incoming.legacyFile;
        }
    }

    private void RepairSelection()
    {
        if (!_index.profiles.Any(p => p.id.Equals(_index.viewedProfileId, StringComparison.OrdinalIgnoreCase))) _index.viewedProfileId = "default";
        if (!_index.profiles.Any(p => p.id.Equals(_index.appliedProfileId, StringComparison.OrdinalIgnoreCase))) _index.appliedProfileId = "default";
    }

    private OverviewProfile GetRequired(string id)
    {
        OverviewProfileReference? reference = _index.profiles.FirstOrDefault(p => p.id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (reference == null) throw new KeyNotFoundException(Loc.T("Profile not found: ", "找不到方案：", "找不到方案：") + id);
        return Load(reference);
    }

    private OverviewProfile NewProfile(string id, string name, OverviewProfileKind kind, OverviewState state, OverviewConfigSnapshot config, List<OverviewProfileMod> mods)
    {
        DateTime now = DateTime.UtcNow;
        List<OverviewProfileMod> profileMods = Clone(mods);
        foreach (OverviewModRule rule in state.mods ?? new())
            if (!profileMods.Any(mod => mod.id.Equals(rule.id, StringComparison.OrdinalIgnoreCase) || mod.guid.Equals(rule.id, StringComparison.OrdinalIgnoreCase)))
                profileMods.Add(new OverviewProfileMod { id = rule.id, guid = rule.id, name = rule.id, enabled = false, installed = false });
        var profile = new OverviewProfile { id = id, publicationId = id, name = name, kind = kind, directory = Path.Combine(RootDirectory, SafeSegment(id)),
            createdUtc = now, modifiedUtc = now, state = state, config = config, mods = profileMods };
        OverviewProfileNames.Normalize(profile); return profile;
    }

    private static OverviewState CleanState(OverviewState source, bool clearGroupsAndPack)
    {
        OverviewState state = Clone(source ?? new OverviewState());
        state.pack ??= new(); state.groups ??= new(); state.mods ??= new(); state.hotkeyPresets ??= new(); state.packHotkeyPresets ??= new();
        state.collapsedGroups = new(); state.collapsedHotkeyMods = new(); state.ignoredHotkeyConflicts ??= new();
        state.hideDisabled = false; state.modTagFilter = new(); state.hotkeyTagFilter = new();
        state.customColors = new(); state.ignoredHotkeyConflicts = new(); state.updateWarningsDismissed = false; state.hotkeyAuthorPresetsImported = false;
        foreach (OverviewModRule rule in state.mods ?? new()) { rule.alias = ""; rule.description = ""; rule.descriptionOverridden = false; }
        if (clearGroupsAndPack)
        {
            state.groups = new();
            state.pack = new OverviewPack { id = "default", name = "Default", introEnabled = false, introFile = "", introDocument = "" };
            foreach (OverviewModRule rule in state.mods ?? new()) { rule.groupId = ""; rule.order = 0; }
        }
        state.mods!.RemoveAll(rule => string.IsNullOrWhiteSpace(rule.groupId) && rule.order == 0 && string.IsNullOrWhiteSpace(rule.versionLabel)
            && rule.updateAllowed && !rule.updateOverridden && string.IsNullOrWhiteSpace(rule.sourceNamespace) && string.IsNullOrWhiteSpace(rule.sourceName));
        return state;
    }

    private static IEnumerable<OverviewProfileMod> SnapshotMods(IEnumerable<OverviewMod> mods) => mods.Select(m => new OverviewProfileMod
    {
        id = m.Id, guid = m.Guid, name = m.Name, version = m.DisplayVersion, pluginVersion = m.PluginVersion, packageVersion = m.PackageVersion, packageNamespace = m.PackageNamespace,
        packageName = m.PackageName, enabled = m.TargetEnabled, installed = true
    });

    private static bool SameMod(OverviewProfileMod left, OverviewProfileMod right) =>
        left.guid.Length > 0 && right.guid.Length > 0
            ? left.guid.Equals(right.guid, StringComparison.OrdinalIgnoreCase)
            : left.id.Equals(right.id, StringComparison.OrdinalIgnoreCase);

    private void CopyLegacyResources(string? source, string destination)
    {
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source)) return;
        CopyResources(source, destination);
    }

    private static void CopyResources(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        string sourceRoot = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (string file in SafeResourceFiles(source))
        {
            string relative = file.Substring(sourceRoot.Length);
            if (relative.Equals("profile.json", StringComparison.OrdinalIgnoreCase) || relative.Equals("config-values.json", StringComparison.OrdinalIgnoreCase)) continue;
            string target = SafeChild(destination, relative); if (target.Length == 0 || File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, false);
        }
    }

    private static void CopyResources(string source, string destination, IEnumerable<string> relativePaths)
    {
        foreach (string relative in relativePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string sourceFile = OverviewProfileAssets.CheckedPath(source, relative);
            if (!File.Exists(sourceFile)) throw new FileNotFoundException(Loc.F("A referenced profile file is missing: {0}", "方案引用的文件不存在：{0}", "方案引用的檔案不存在：{0}", relative), sourceFile);
            if ((File.GetAttributes(sourceFile) & FileAttributes.ReparsePoint) != 0)
                throw new IOException(Loc.F("Referenced profile files cannot be links: {0}", "方案引用的文件不能是链接：{0}", "方案引用的檔案不能是鏈接：{0}", relative));
            string target = SafeChild(destination, relative);
            if (target.Length == 0) throw new IOException(Loc.F("Invalid imported profile resource path: {0}", "导入方案素材路径无效：{0}", "導入方案素材路徑無效：{0}", relative));
            if (File.Exists(target)) throw new IOException(Loc.F("The imported profile target already exists: {0}", "导入方案目标文件已存在：{0}", "導入方案目標檔案已存在：{0}", target));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }

    private static string ValidateImportDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException(Loc.T("Choose a profile export folder.", "请选择方案资料目录。", "請選擇方案資料資料夾。"));
        string full = Path.GetFullPath(directory.Trim().Trim('"'));
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(Loc.F("Profile export folder not found: {0}", "找不到方案资料目录：{0}", "找不到方案資料資料夾：{0}", full));
        for (var current = new DirectoryInfo(full); current != null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException(Loc.T("Profile import folders cannot use directory links or junctions.", "导入方案目录不能使用目录链接或联接点。", "匯入方案資料夾不能使用目錄鏈接或聯接點。"));
        return full;
    }

    private static JObject ReadImportObject(string path)
    {
        string safe = OverviewProfileAssets.CheckedPath(Path.GetDirectoryName(path)!, Path.GetFileName(path));
        if (!File.Exists(safe)) throw new FileNotFoundException(Loc.T("The export is missing profile.json or config-values.json.", "导出目录缺少 profile.json 或 config-values.json。", "匯出資料夾缺少 profile.json 或 config-values.json。"), safe);
        if ((File.GetAttributes(safe) & FileAttributes.ReparsePoint) != 0)
            throw new IOException(Loc.F("Profile export files cannot be links: {0}", "方案导出文件不能是链接：{0}", "方案匯出檔案不能是鏈接：{0}", safe));
        return JObject.Parse(File.ReadAllText(safe, Encoding.UTF8));
    }

    private static void RequireImportSchema(JObject json, string path)
    {
        JToken? schema = json["schemaVersion"];
        bool profileDocument = Path.GetFileName(path).Equals("profile.json", StringComparison.OrdinalIgnoreCase);
        if (schema == null || schema.Type != JTokenType.Integer || !(profileDocument ? GroupTree.SupportedSchema(schema.Value<int>()) : schema.Value<int>() == SchemaVersion))
            throw new InvalidDataException(Loc.F("Unsupported export schema in {0}; schemaVersion must be integer 1.", "导出文件 {0} 的 schemaVersion 必须是整数 1。", "匯出檔案 {0} 的 schemaVersion 必須是整數 1。", Path.GetFileName(path)));
    }

    private static void AddIntroductionFile(HashSet<string> files, string sourceRoot, string? declaredPath, string fallback)
    {
        string relative = string.IsNullOrWhiteSpace(declaredPath) ? fallback : Uri.UnescapeDataString(declaredPath!).Replace('\\', '/');
        string source = OverviewProfileAssets.CheckedPath(sourceRoot, relative);
        if (source.Length == 0) throw new IOException(Loc.F("Invalid introduction file path: {0}", "介绍文件路径无效：{0}", "介紹檔案路徑無效：{0}", relative));
        if (File.Exists(source)) files.Add(relative);
    }

    private static IEnumerable<string> SafeResourceFiles(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            string[] files; string[] directories;
            try { files = Directory.GetFiles(current); directories = Directory.GetDirectories(current); } catch { continue; }
            foreach (string file in files)
            {
                bool ordinary = false;
                try { ordinary = (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0; } catch { }
                if (ordinary) yield return file;
            }
            foreach (string directory in directories)
                try { if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0) pending.Push(directory); } catch { }
        }
    }

    private static IEnumerable<string> SafeFiles(string root, string fileName)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            string candidate = Path.Combine(directory, fileName); if (File.Exists(candidate)) yield return candidate;
            string[] children; try { children = Directory.GetDirectories(directory); } catch { continue; }
            foreach (string child in children)
                try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child); } catch { }
        }
    }

    private string RelativePluginPath(string path)
    {
        string root = Path.GetFullPath(_pluginsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length).Replace('\\', '/') : Path.GetFileName(path);
    }

    private string StableAuthorSource(string packDirectory)
    {
        var published = PublishedPack.Packs.FirstOrDefault(p => p.Valid && p.Id.Length > 0 && Path.GetFullPath(p.Root).Equals(Path.GetFullPath(packDirectory), StringComparison.OrdinalIgnoreCase));
        if (published != null) return "pack-" + published.Id;
        string relative = RelativePluginPath(packDirectory);
        return System.Text.RegularExpressions.Regex.Replace(relative, @"-\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?(?=/|$)", "").ToLowerInvariant();
    }

    private string SafeDirectory(string path)
    {
        string root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(Loc.T("The profile directory is outside the local profile root.", "方案目录不在本地方案根目录中。", "方案目錄不在本地方案根目錄中。"));
        return full;
    }

    private static string SafeChild(string root, string relative)
    {
        try
        {
            if (Path.IsPathRooted(relative)) return "";
            return OverviewRepository.SafePath(root, relative);
        }
        catch { return ""; }
    }

    private static string SafeSegment(string value)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value.Trim().Length == 0 ? Guid.NewGuid().ToString("N") : value.Trim();
    }

    private static string NormalizeName(string value)
    {
        value = (value ?? "").Trim();
        if (value.Length == 0) throw new ArgumentException(Loc.T("Profile name cannot be empty.", "方案名称不能为空。", "方案名稱不能為空。"));
        return value.Length <= 80 ? value : value.Substring(0, 80);
    }

    private static T ReadStrict<T>(string path)
    {
        using var reader = new JsonTextReader(new StreamReader(path, Encoding.UTF8)) { DateParseHandling = DateParseHandling.DateTime };
        var serializer = JsonSerializer.Create(new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore });
        return serializer.Deserialize<T>(reader) ?? throw new InvalidDataException(Loc.T("JSON content is empty.", "JSON 内容为空。", "JSON 內容為空。"));
    }

    private static T ReadStrictVersioned<T>(string path)
    {
        JObject json = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
        JToken? version = json["schemaVersion"];
        if (version == null || version.Type != JTokenType.Integer || version.Value<int>() != SchemaVersion)
            throw new InvalidDataException(Loc.T("schemaVersion must be integer 1.", "schemaVersion 必须是整数 1。", "schemaVersion 必須是整數 1。"));
        return DeserializeJson<T>(json.ToString(Formatting.None));
    }

    // Older tools wrote whole-number ordering as 7.0. Accept only exact integral values,
    // never round fractions or silently replace an invalid document with an empty object.
    internal static T DeserializeJson<T>(string text)
        => JsonConvert.DeserializeObject<T>(text, new IntegralValueConverter()) ?? throw new InvalidDataException(Loc.T("JSON content is empty.", "JSON 内容为空。", "JSON 內容為空。"));

    private sealed class IntegralValueConverter : JsonConverter
    {
        public override bool CanConvert(Type type) => type == typeof(int);
        public override bool CanWrite => false;
        public override object ReadJson(JsonReader reader, Type type, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.Integer && reader.TokenType != JsonToken.Float) throw new InvalidDataException(Loc.F("Invalid integer setting format: {0}", "整数参数格式无效：{0}", "整數參數格式無效：{0}", reader.Path));
            decimal value = Convert.ToDecimal(reader.Value, System.Globalization.CultureInfo.InvariantCulture);
            if (decimal.Truncate(value) != value || value < int.MinValue || value > int.MaxValue) throw new InvalidDataException(Loc.F("Integer settings cannot contain fractions or exceed their range: {0}", "整数参数不能含小数或超出范围：{0}", "整數參數不能含小數或超出范圍：{0}", reader.Path));
            return (int)value;
        }
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new NotSupportedException();
    }

    private static object ComparableValues(OverviewConfigSnapshot config) => (config.values ?? new())
        .OrderBy(v => v.Identity, StringComparer.Ordinal).Select(v => new { identity = v.Identity, v.type, v.serializedValue }).ToList();

    private static void AtomicWrite(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp"; File.WriteAllText(temporary, text, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporary, path, path + ".bak"); else File.Move(temporary, path);
    }

    private static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;

    private void ScanLegacyAuthorPacks()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string fileName in new[] { "packinfo.cfg", "packinfo.json", "pack.json" })
        foreach (string file in SafeFiles(_pluginsRoot, fileName))
        {
            string? packDir = Path.GetDirectoryName(file);
            if (packDir == null || !ModConfigEnhance.Updating.ManagerIdentity.IsPackFolder(Path.GetFileName(packDir))) continue;
            if (!seen.Add(packDir)) continue;
            if (!PublishedPack.CanRead(packDir)) continue;
            string source = RelativePluginPath(packDir);
            string id = "author:" + StableAuthorSource(packDir) + ":legacy";
            UpsertAuthorReference(new OverviewProfileReference { id = id, name = Loc.T("Author preset", "作者预设", "作者預設"), kind = OverviewProfileKind.Author,
                directory = packDir, source = source, legacyFile = file, readOnly = true });
        }
    }

    private static OverviewProfile ReadLegacyProfile(OverviewProfileReference reference)
    {
        OverviewState state;
        if (reference.legacyFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            JObject json = JObject.Parse(File.ReadAllText(reference.legacyFile, Encoding.UTF8));
            state = DeserializeJson<OverviewState>(json.ToString(Formatting.None));
            state.pack = (json["packInfo"] ?? json["PackInfo"] ?? json["pack"] ?? json).ToObject<OverviewPack>() ?? new OverviewPack();
        }
        else state = ReadLegacyCfg(reference.legacyFile);
        string name = state.pack.name.Length > 0 ? state.pack.name : reference.name;
        return new OverviewProfile { id = reference.id, name = name, kind = OverviewProfileKind.Author, directory = reference.directory,
            source = reference.source, readOnly = true, state = CleanState(state, false), config = new OverviewConfigSnapshot() };
    }

    private static OverviewState ReadLegacyCfg(string file)
    {
        var state = new OverviewState(); string section = "";
        var fields = new JObject(); var sections = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in File.ReadAllLines(file, Encoding.UTF8))
        {
            string line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
            if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
            int equals = line.IndexOf('='); if (equals < 1) continue;
            string key = line.Substring(0, equals).Trim(); string value = line.Substring(equals + 1).Trim();
            JObject destination = fields;
            if (section.StartsWith("group:", StringComparison.OrdinalIgnoreCase) || section.StartsWith("mod:", StringComparison.OrdinalIgnoreCase)
                || section.StartsWith("hotkey:", StringComparison.OrdinalIgnoreCase) || section.StartsWith("packhotkey:", StringComparison.OrdinalIgnoreCase))
            {
                if (!sections.TryGetValue(section, out destination!)) sections[section] = destination = new JObject();
            }
            else if (section.Length != 0 && !section.Equals("PackInfo", StringComparison.OrdinalIgnoreCase) && !section.Equals("pack", StringComparison.OrdinalIgnoreCase) && !section.Equals("general", StringComparison.OrdinalIgnoreCase)) continue;
            if (key.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase) && value != "1") throw new InvalidDataException(Loc.T("Unsupported legacy preset version.", "不支持的旧预设版本。", "不支持的舊預設版本。"));
            if (bool.TryParse(value, out bool boolean)) destination[key] = boolean;
            else if (key.Equals("order", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int order)) destination[key] = order;
            else destination[key] = value;
        }
        state.globalUpdateAllowed = fields.Value<bool?>("globalUpdateAllowed") ?? true;
        state.automaticUpdateChecks = fields.Value<bool?>("automaticUpdateChecks") ?? true;
        state.packHotkeyPresetMode = fields.Value<bool?>("packHotkeyPresetMode") ?? false;
        state.pack = DeserializeJson<OverviewPack>(fields.ToString(Formatting.None));
        foreach (var pair in sections)
        {
            pair.Value["id"] = pair.Key.Substring(pair.Key.IndexOf(':') + 1).Trim(); string json = pair.Value.ToString(Formatting.None);
            if (pair.Key.StartsWith("group:", StringComparison.OrdinalIgnoreCase)) state.groups.Add(DeserializeJson<OverviewGroup>(json));
            else if (pair.Key.StartsWith("mod:", StringComparison.OrdinalIgnoreCase)) state.mods.Add(DeserializeJson<OverviewModRule>(json));
            else if (pair.Key.StartsWith("hotkey:", StringComparison.OrdinalIgnoreCase)) state.hotkeyPresets.Add(DeserializeJson<OverviewHotkeyPreset>(json));
            else state.packHotkeyPresets.Add(DeserializeJson<OverviewHotkeyPreset>(json));
        }
        return state;
    }
}
