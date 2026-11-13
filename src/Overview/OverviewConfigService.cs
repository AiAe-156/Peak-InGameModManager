#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace ModConfigEnhance.Overview;

/// <summary>Captures and validates ordinary BepInEx config. It never writes until a prepared transaction is applied.</summary>
internal sealed class OverviewConfigService
{
    private readonly string _configRoot;
    private readonly string _backupRoot;
    private readonly Func<IEnumerable<(string Guid, ConfigFile File)>>? _configProvider;

    public OverviewConfigService(string? configRoot = null, string? runtimeDirectory = null)
    {
        _configRoot = Path.GetFullPath(configRoot ?? Paths.ConfigPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _backupRoot = runtimeDirectory ?? Path.Combine(Updating.MceDirectories.Runtime(Path.Combine(_configRoot, ModConfigEnhance.Updating.ManagerIdentity.ConfigDirectory)), "config-backups");
    }
    internal OverviewConfigService(string configRoot, string runtimeDirectory, Func<IEnumerable<(string Guid, ConfigFile File)>> configProvider)
        : this(configRoot, runtimeDirectory) { _configProvider = configProvider; }

    public IReadOnlyList<OverviewConfigFileStatus> Scan(OverviewConfigSnapshot? existing = null)
    {
        var catalog = BuildCatalog();
        return ScanFiles(catalog, existing).OrderBy(x => x.pluginGuid).ThenBy(x => x.configFile).ToList();
    }

    public OverviewConfigSnapshot Capture(OverviewConfigSnapshot? existing = null)
    {
        var catalog = BuildCatalog();
        var snapshot = new OverviewConfigSnapshot { capturedUtc = DateTime.UtcNow };
        foreach (LiveFile file in catalog.Files)
        foreach (var pair in file.Config)
        {
            ConfigEntryBase entry = pair.Value;
            snapshot.values.Add(new OverviewConfigValue
            {
                pluginGuid = file.Guid,
                configFile = file.RelativePath,
                section = pair.Key.Section,
                key = pair.Key.Key,
                type = TypeName(entry.SettingType),
                serializedValue = entry.GetSerializedValue()
            });
        }

        var liveKeys = new HashSet<string>(snapshot.values.Select(v => v.Identity), StringComparer.Ordinal);
        if (existing != null)
        foreach (OverviewConfigValue old in existing.values ?? new())
        {
            if (IsExcludedGuid(old.pluginGuid) || IsExcluded(old.configFile) || Absolute(old.configFile).Length == 0) continue;
            if (liveKeys.Contains(old.Identity)) continue;
            OverviewConfigValue retained = Clone(old);
            retained.missing = true;
            retained.missingReason = File.Exists(Absolute(retained.configFile)) ? Loc.T("The mod or setting is not loaded; its recorded value is retained.", "模组或配置项当前未加载；已保留记录。", "模組或設定項目前未載入；已保留記錄。") : Loc.T("The config file is missing; its recorded values are retained.", "配置文件当前不存在；已保留记录。", "設定檔目前不存在；已保留記錄。");
            string diskPath = Absolute(retained.configFile);
            if (diskPath.Length > 0 && TryReadRawValue(diskPath, retained.section, retained.key, out string diskValue))
            { retained.serializedValue = diskValue; retained.missing = false; retained.missingReason = ""; }
            snapshot.values.Add(retained);
        }
        // One read and parse per file, not per value: a capture covers thousands of values in a few hundred files.
        var documents = new Dictionary<string, ConfigurationDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in snapshot.values)
        {
            string path = Absolute(value.configFile);
            value.diskBaselineKnown = true;
            // Do not swallow I/O failures: an unreadable baseline is not an absent field.
            if (!documents.TryGetValue(path, out var document))
                documents[path] = document = new ConfigurationDocument(File.Exists(path) ? File.ReadAllText(path) : "");
            value.diskExists = document.TryGet(new ConfigurationDocument.Key(value.section, value.key), out string raw);
            value.diskValue = raw ?? "";
        }
        snapshot.files = ScanFiles(catalog, existing).ToList();
        return snapshot;
    }

    public IReadOnlyList<OverviewConfigDiff> Diff(OverviewConfigSnapshot profile, OverviewConfigSnapshot current)
    {
        var left = (profile.values ?? new()).GroupBy(v => v.Identity, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var right = (current.values ?? new()).GroupBy(v => v.Identity, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var result = new List<OverviewConfigDiff>();
        foreach (string key in left.Keys.Union(right.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            left.TryGetValue(key, out OverviewConfigValue? wanted); right.TryGetValue(key, out OverviewConfigValue? actual);
            if (wanted == null)
                result.Add(new OverviewConfigDiff { Kind = OverviewConfigDiffKind.CurrentOnlyNotRecorded, CurrentValue = actual, Message = Loc.T("Unrecorded in this profile; applying keeps the current value.", "方案未记录，应用时保持当前值。", "方案未記錄，套用時保留目前值。") });
            else if (actual == null || actual.missing)
                result.Add(new OverviewConfigDiff { Kind = OverviewConfigDiffKind.ProfileOnlyMissing, ProfileValue = wanted, CurrentValue = actual, Message =
                    actual?.missingReason.Length > 0 ? actual.missingReason : Loc.T("The corresponding mod or setting is unavailable.", "当前缺少对应模组或配置项。", "目前缺少對應模組或設定項。") });
            else if (!string.Equals(wanted.type, actual.type, StringComparison.Ordinal))
                result.Add(new OverviewConfigDiff { Kind = OverviewConfigDiffKind.Unsupported, ProfileValue = wanted, CurrentValue = actual,
                    Message = Loc.T("The setting type has changed; its original target is retained.", "配置项类型已改变；已保留原方案目标。", "設定項類型已變更；已保留原方案目標。") });
            else if (!string.Equals(wanted.serializedValue, actual.serializedValue, StringComparison.Ordinal))
                result.Add(new OverviewConfigDiff { Kind = OverviewConfigDiffKind.Changed, ProfileValue = wanted, CurrentValue = actual });
            else result.Add(new OverviewConfigDiff { Kind = OverviewConfigDiffKind.Equal, ProfileValue = wanted, CurrentValue = actual });
        }
        return result;
    }

    public bool TryValidateDraft(OverviewConfigValue draft, string serializedValue, out string normalized, out string error)
    {
        normalized = ""; error = "";
        LiveEntry? live = FindLive(draft);
        if (live == null)
        {
            string path = Absolute(draft.configFile);
            if (path.Length == 0 || IsExcludedGuid(draft.pluginGuid) || IsExcluded(draft.configFile) || !draft.configFile.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
            { error = Loc.T("Select a supported configuration file within the config directory.", "请选择配置目录内受支持的配置文件。", "請選擇設定目錄內受支援的設定檔。"); return false; }
            try
            {
                // Validate the document representation, not an unavailable third-party type.
                // Original serialized targets (including custom enums) remain editable offline.
                var document = new ConfigurationDocument("");
                document.Merge(new[] { new ConfigurationDocument.Edit { Key = new ConfigurationDocument.Key(draft.section, draft.key), Value = serializedValue } });
                normalized = serializedValue; return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        if (!string.Equals(draft.type, TypeName(live.Entry.SettingType), StringComparison.Ordinal)) { error = Loc.T("The setting type has changed. Refresh the current settings before editing.", "配置项类型已改变，请刷新当前配置后再编辑。", "設定項類型已變更，請重新整理目前設定後再編輯。"); return false; }
        return TryConvert(live.Entry, serializedValue, out _, out normalized, out error);
    }
    public ConfigEntryBase? FindLoadedEntry(OverviewConfigValue value) => FindLive(value)?.Entry;

    private string _offlineSignature = "";
    private IReadOnlyDictionary<string, string> _offlineRead = new Dictionary<string, string>();
    private DateTime _offlineReadAt;
    internal IReadOnlyDictionary<string, string> ReadOfflineValues(OverviewConfigSnapshot snapshot)
    {
        string signature = string.Join("\n", snapshot.values.Select(v => v.Identity));
        if (signature == _offlineSignature && DateTime.UtcNow < _offlineReadAt) return _offlineRead;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var live = KnownLoadedEntries();
        foreach (var group in snapshot.values.Where(v => !live.ContainsKey(v.Identity)).GroupBy(v => v.configFile, StringComparer.OrdinalIgnoreCase))
        {
            string path = Absolute(group.Key);
            if (path.Length == 0 || !File.Exists(path) || !group.Key.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) || new FileInfo(path).Length > 16 * 1024 * 1024) continue;
            try
            {
                var document = new ConfigurationDocument(File.ReadAllText(path));
                foreach (var value in group)
                    if (document.TryGet(new ConfigurationDocument.Key(value.section, value.key), out string serialized)) result[value.Identity] = serialized;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        _offlineSignature = signature; _offlineReadAt = DateTime.UtcNow.AddSeconds(1); _offlineRead = result;
        return result;
    }
    public IReadOnlyDictionary<string, ConfigEntryBase> LoadedEntries() => BuildCatalog().Entries.ToDictionary(pair => pair.Key, pair => pair.Value.Entry, StringComparer.Ordinal);
    // Status updates reuse the catalogue established while opening/refreshing the page.
    // Live entry objects still expose subsequent native edits without rescanning plugins.
    internal IReadOnlyDictionary<string, ConfigEntryBase> KnownLoadedEntries()
        => (_catalog ?? BuildCatalog()).Entries.ToDictionary(pair => pair.Key, pair => pair.Value.Entry, StringComparer.Ordinal);

    public bool TryEditDraft(OverviewConfigSnapshot snapshot, OverviewConfigValue key, string serializedValue, out string error)
    {
        if (!TryValidateDraft(key, serializedValue, out string normalized, out error)) return false;
        OverviewConfigValue? target = snapshot.values.FirstOrDefault(v => v.Identity.Equals(key.Identity, StringComparison.Ordinal));
        if (target == null)
        {
            target = Clone(key); snapshot.values.Add(target);
        }
        target.serializedValue = normalized; target.missing = false; target.missingReason = "";
        return true;
    }

    internal string? Reconcile(OverviewConfigSnapshot baseline, OverviewConfigSnapshot draft, Func<OverviewConfigValue, bool> match, bool overwrite)
    {
        var current = Capture(draft).values.ToDictionary(v => v.Identity, StringComparer.Ordinal);
        var originals = baseline.values.ToDictionary(v => v.Identity, StringComparer.Ordinal);
        // A later conflict must not leave earlier automatic merges looking like user edits.
        var reconciled = draft.values.Where(match).Select(Clone).ToList();
        var remove = new List<OverviewConfigValue>();
        foreach (var target in reconciled)
        {
            if (!current.TryGetValue(target.Identity, out var now)) continue;
            if (originals.TryGetValue(target.Identity, out var start))
            {
                bool edited = target.serializedValue != start.serializedValue;
                bool liveChanged = !now.missing && now.serializedValue != start.serializedValue;
                bool diskChanged = start.diskBaselineKnown
                    ? now.diskExists != start.diskExists || now.diskExists && now.diskValue != start.diskValue
                    // Old recovery drafts did not record disk state. Treat divergence from
                    // their live baseline conservatively rather than silently consuming it.
                    : !now.diskExists || now.diskValue != start.serializedValue;
                if (edited && !overwrite && (liveChanged && now.serializedValue != target.serializedValue
                    || diskChanged && (!now.diskExists || now.diskValue != target.serializedValue)))
                    return "MCE_EXTERNAL_CONFLICT: " + target.pluginGuid + " / [" + target.section + "] " + target.key;
                if (!edited)
                {
                    bool competingExternalValues = diskChanged && liveChanged && (!now.diskExists || now.diskValue != now.serializedValue);
                    if (competingExternalValues && !overwrite)
                        return "MCE_EXTERNAL_CONFLICT: " + target.pluginGuid + " / [" + target.section + "] " + target.key;
                    if (competingExternalValues) { /* Explicit keep-mine keeps the displayed draft value. */ }
                    else if (diskChanged)
                    {
                        if (!now.diskExists) { remove.Add(target); continue; }
                        target.serializedValue = now.diskValue;
                    }
                    else if (liveChanged) target.serializedValue = now.serializedValue;
                }
            }
            // Bind preparation to exactly the generation inspected above, not a later read.
            target.diskBaselineKnown = true; target.diskExists = now.diskExists; target.diskValue = now.diskValue;
            target.liveBaselineKnown = FindLive(target) != null; target.liveValue = now.serializedValue;
        }
        var removed = new HashSet<string>(remove.Select(v => v.Identity), StringComparer.Ordinal);
        var replacements = reconciled.ToDictionary(v => v.Identity, StringComparer.Ordinal);
        draft.values = draft.values.Where(v => !removed.Contains(v.Identity))
            .Select(v => replacements.TryGetValue(v.Identity, out var updated) ? updated : v).ToList();
        return null;
    }

    public OverviewConfigTransaction PrepareApply(OverviewConfigSnapshot snapshot)
    {
        if (snapshot == null || snapshot.schemaVersion != 1 || snapshot.values == null || snapshot.files == null)
            throw new InvalidOperationException(Loc.T("The config snapshot format is invalid.", "配置快照格式无效，拒绝应用。", "設定快照格式無效，拒絕套用。"));
        LiveCatalog catalog = BuildCatalog();
        var changes = new List<PreparedChange>();
        var offlineValues = new List<OverviewConfigValue>();
        var deferred = new OverviewConfigSnapshot { capturedUtc = snapshot.capturedUtc };
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (OverviewConfigValue value in snapshot.values)
        {
            if (!identities.Add(value.Identity)) throw new InvalidDataException(Loc.T("The config snapshot contains a duplicate parameter: ", "配置快照包含重复参数：", "設定快照包含重複參數：") + value.pluginGuid + " / [" + value.section + "] " + value.key);
            if (IsExcludedGuid(value.pluginGuid) || IsExcluded(value.configFile) || Absolute(value.configFile).Length == 0) continue;
            LiveEntry? live = catalog.Find(value);
            if (live == null)
            {
                // A disabled plugin is not a reason to defer persistence until after startup.
                // Never offline-rewrite a file that has a live ConfigFile owner: its next Save
                // would overwrite unknown keys. Such schema changes remain explicit issues.
                bool liveOwner = catalog.Files.Any(f => f.RelativePath.Equals(value.configFile, StringComparison.OrdinalIgnoreCase))
                    || catalog.UnsupportedFiles.Any(f => f.configFile.Equals(value.configFile, StringComparison.OrdinalIgnoreCase));
                if (!liveOwner && value.configFile.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase))
                { offlineValues.Add(Clone(value)); continue; }
                var retained = Clone(value); retained.missing = true;
                retained.missingReason = Loc.T("The setting is missing from a loaded or shared config file; its target is retained for review.", "已加载或共享配置文件中缺少此项；保留目标供检查。", "已載入或共用設定檔中缺少此項；保留目標供檢查。");
                deferred.values.Add(retained); continue;
            }
            if (value.liveBaselineKnown && value.liveValue != live.Entry.GetSerializedValue()
                && value.serializedValue != live.Entry.GetSerializedValue())
                throw new InvalidDataException("MCE_EXTERNAL_CONFLICT: live value changed during preparation: " + value.Identity);
            if (!string.Equals(value.type, TypeName(live.Entry.SettingType), StringComparison.Ordinal))
            {
                var retained = Clone(value); retained.missing = true;
                retained.missingReason = Loc.T("The setting type has changed; its original target is retained.", "配置项类型已改变；已保留原方案目标。", "設定項類型已變更；已保留原方案目標。");
                deferred.values.Add(retained); continue;
            }
            if (!TryConvert(live.Entry, value.serializedValue, out object converted, out string normalized, out string error))
            {
                var retained = Clone(value); retained.missing = true;
                retained.missingReason = Loc.T("Invalid value; this item was skipped: ", "值无效，已跳过此项：", "值無效，已略過此項：") + error;
                deferred.values.Add(retained); continue;
            }
            if (!string.Equals(live.Entry.GetSerializedValue(), normalized, StringComparison.Ordinal)
                || value.diskBaselineKnown && (!value.diskExists || value.diskValue != normalized))
                changes.Add(new PreparedChange(live.File, live.Entry, converted, normalized, value));
        }
        var offline = offlineValues.GroupBy(v => v.configFile, StringComparer.OrdinalIgnoreCase).Select(group => {
            string path = Absolute(group.Key);
            return new OfflineConfigurationTransaction(path, group.Select(v => (v.section, v.key, v.serializedValue)), () => SafeConfigPath(_configRoot, path), group.Where(v => v.diskBaselineKnown).Select(v => (v.section, v.key, v.diskExists, v.diskValue)));
        }).ToList();
        return new OverviewConfigTransaction(changes, _backupRoot, _configRoot, deferred, offline);
    }

    private LiveEntry? FindLive(OverviewConfigValue value) => BuildCatalog().Find(value);

    private LiveCatalog? _catalog;
    private int _catalogFrame = -1;

    /// <summary>
    /// 目录要遍历 60 多个插件实例的全部字段（含私有与基类）找 ConfigFile，代价不小；
    /// 而一次打开概览里 Reload、Capture、参数比对会连着要好几次。同一帧内复用同一份目录，
    /// 跨帧仍然重建，所以运行中新绑定的配置项最迟下一帧就能看到。
    /// </summary>
    private LiveCatalog BuildCatalog()
    {
        int frame = UnityEngine.Time.frameCount;
        if (_catalog != null && _catalogFrame == frame) return _catalog;
        var result = new LiveCatalog();
        var candidates = (_configProvider?.Invoke() ?? EnumerateOwnedConfigs())
            .Where(pair => !IsExcludedGuid(pair.Guid)).Select(pair => (pair.Guid, pair.File, RelativePath: Relative(pair.File.ConfigFilePath)))
            .Where(pair => pair.RelativePath.Length > 0 && !IsExcluded(pair.RelativePath)).ToList();
        foreach (var group in candidates.GroupBy(pair => pair.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var owners = group.Select(pair => pair.Guid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var instances = group.Select(pair => pair.File).Distinct().ToList();
            if (owners.Count != 1 || instances.Count != 1)
            {
                result.UnsupportedFiles.Add(new OverviewConfigFileStatus { configFile = group.Key, supported = false, message = Loc.T("This config file is shared by multiple owners or instances and is excluded from profiles.", "该配置文件由多个模组或配置实例共用，未纳入方案。", "此設定檔由多個模組或設定實例共用，未納入方案。") });
                continue;
            }
            var first = group.First(); var live = new LiveFile(first.Guid, first.RelativePath, first.File); result.Files.Add(live);
            foreach (var pair in first.File) result.Entries[Identity(first.Guid, first.RelativePath, pair.Key.Section, pair.Key.Key)] = new LiveEntry(live, pair.Value);
        }
        _catalog = result; _catalogFrame = frame;
        return result;
    }

    private static IEnumerable<(string Guid, ConfigFile File)> EnumerateOwnedConfigs()
    {
        foreach (PluginInfo info in Chainloader.PluginInfos.Values.OrderBy(x => x.Metadata.GUID, StringComparer.OrdinalIgnoreCase))
        {
            string guid = info.Metadata.GUID ?? "";
            if (IsExcludedGuid(guid) || info.Instance == null) continue;
            var files = new HashSet<ConfigFile>();
            AddConfig(files, info.Instance.Config);
            Type type = info.Instance.GetType();
            foreach (FieldInfo field in Fields(type))
            {
                // A beforefieldinit type can have an instance while its static initializer
                // has not run. Reading such a static field would activate lazy config creation.
                if (field.IsStatic && field.DeclaringType != null && (field.DeclaringType.Attributes & TypeAttributes.BeforeFieldInit) != 0) continue;
                try
                {
                    if (typeof(ConfigFile).IsAssignableFrom(field.FieldType)) AddConfig(files, field.GetValue(info.Instance) as ConfigFile);
                    else if (typeof(ConfigEntryBase).IsAssignableFrom(field.FieldType)) AddConfig(files, (field.GetValue(info.Instance) as ConfigEntryBase)?.ConfigFile);
                }
                catch { }
            }
            // Auto-properties are already covered by their backing fields. Never call
            // a third-party getter: it may create, bind or save a ConfigFile on first use.
            foreach (ConfigFile config in files) yield return (guid, config);
        }
    }

    private IEnumerable<OverviewConfigFileStatus> ScanFiles(LiveCatalog catalog, OverviewConfigSnapshot? existing)
    {
        var statuses = new Dictionary<string, OverviewConfigFileStatus>(StringComparer.OrdinalIgnoreCase);
        foreach (var unsupported in catalog.UnsupportedFiles) statuses[unsupported.configFile] = unsupported;
        foreach (LiveFile file in catalog.Files)
        {
            string key = file.Guid + "\n" + file.RelativePath;
            statuses[key] = new OverviewConfigFileStatus { pluginGuid = file.Guid, configFile = file.RelativePath, loaded = true };
        }
        var priorFiles = (existing?.values ?? new()).GroupBy(v => v.configFile, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().pluginGuid, StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_configRoot)) return statuses.Values;
        foreach (string path in SafeConfigFiles())
        {
            string relative = Relative(path); if (relative.Length == 0 || IsExcluded(relative)) continue;
            if (catalog.Files.Any(f => f.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase)) || catalog.UnsupportedFiles.Any(f => f.configFile.Equals(relative, StringComparison.OrdinalIgnoreCase))) continue;
            string guid = ReadGuidHeader(path) ?? (priorFiles.TryGetValue(relative, out var mapped) ? mapped : "");
            if (IsExcludedGuid(guid) && guid.Length > 0) continue;
            bool recognized = guid.Length > 0;
            string key = guid + "\n" + relative;
            statuses[key] = new OverviewConfigFileStatus
            {
                pluginGuid = guid,
                configFile = relative,
                loaded = false,
                supported = recognized && priorFiles.ContainsKey(relative),
                message = !recognized ? Loc.T("The owner is unknown; the file is excluded from profiles.", "无法确认归属，未纳入方案。", "無法確認歸屬，未納入方案。") : priorFiles.ContainsKey(relative)
                    ? Loc.T("The mod is not loaded; the existing mapping and recorded values are retained.", "模组当前未加载；沿用已有快照映射并保留记录。", "模組目前未載入；沿用現有快照對應並保留記錄。")
                    : Loc.T("The GUID header identifies the owner, but parameter types are unavailable.", "已从 GUID 头识别归属，但没有可靠类型资料，暂不纳入参数。", "已從 GUID 標頭識別歸屬，但沒有可靠類型資料，暫不納入參數。")
            };
        }
        return statuses.Values;
    }

    private static bool TryConvert(ConfigEntryBase entry, string serialized, out object converted, out string normalized, out string error)
    {
        converted = null!; normalized = ""; error = "";
        try
        {
            converted = TomlTypeConverter.ConvertToValue(serialized, entry.SettingType);
            if (converted == null) throw new InvalidDataException(Loc.T("The value cannot be converted to null.", "不能转换为空值。", "無法轉換為空值。"));
            AcceptableValueBase? acceptable = entry.Description?.AcceptableValues;
            if (acceptable != null && !acceptable.IsValid(converted)) throw new InvalidDataException(Loc.F("The value is outside the mod's allowed range or choices ({0}).", "数值不在原模组允许的范围或选项中（{0}）。", "數值不在原模組允許的範圍或選項中（{0}）。", acceptable.ToDescriptionString()));
            if (entry.SettingType.IsEnum && !Enum.IsDefined(entry.SettingType, converted))
            {
                bool validFlags = entry.SettingType.IsDefined(typeof(FlagsAttribute), false) &&
                    (Convert.ToDecimal(converted) == 0 || Enum.Format(entry.SettingType, converted, "F").Split(',').All(name => Enum.IsDefined(entry.SettingType, name.Trim())));
                if (!validFlags) throw new InvalidDataException(Loc.T("The enum value is not one of the mod's defined choices.", "枚举值不是原模组定义的有效选项。", "列舉值不是原模組定義的有效選項。"));
            }
            normalized = TomlTypeConverter.ConvertToString(converted, entry.SettingType);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private string Relative(string path)
    {
        try
        {
            string full = SafeConfigPath(_configRoot, path);
            string prefix = _configRoot + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full.Substring(prefix.Length).Replace('\\', '/') : "";
        }
        catch { return ""; }
    }

    private string Absolute(string relative)
    {
        try { return SafeConfigPath(_configRoot, Path.Combine(_configRoot, relative.Replace('/', Path.DirectorySeparatorChar))); }
        catch { return ""; }
    }

    private static bool IsExcludedGuid(string guid) => string.IsNullOrWhiteSpace(guid)
        || guid.Equals(Plugin.PluginGuid, StringComparison.OrdinalIgnoreCase) || guid.Equals(ModConfigSupport.Guid, StringComparison.OrdinalIgnoreCase)
        || guid.IndexOf("XUnity", StringComparison.OrdinalIgnoreCase) >= 0 || guid.IndexOf("AutoTranslator", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsExcluded(string relative) => !PublicationFilePolicy.ProfileConfigPath(relative);

    private static string? ReadGuidHeader(string path)
    {
        try
        {
            foreach (string line in File.ReadLines(path).Take(12))
            {
                Match explicitGuid = Regex.Match(line, @"(?:plugin\s+guid|guid)\s*[:=]\s*(?<guid>[A-Za-z0-9_.-]+)", RegexOptions.IgnoreCase);
                if (explicitGuid.Success) return explicitGuid.Groups["guid"].Value.Trim();
                Match standard = Regex.Match(line, @"created by plugin\s+(?<guid>[^\s]+)(?:\s+v\S+)?", RegexOptions.IgnoreCase);
                if (standard.Success && standard.Groups["guid"].Value.Contains('.')) return standard.Groups["guid"].Value.Trim();
            }
        }
        catch { }
        return null;
    }

    private IEnumerable<string> SafeConfigFiles()
    {
        if (!Directory.Exists(_configRoot)) yield break;
        var pending = new Stack<string>(); pending.Push(_configRoot);
        while (pending.Count > 0)
        {
            string current = pending.Pop(); string[] files; string[] directories;
            try { files = Directory.GetFiles(current); directories = Directory.GetDirectories(current); } catch { continue; }
            foreach (string file in files)
                if (new[] { ".cfg", ".ini", ".json", ".yaml", ".yml" }.Contains(Path.GetExtension(file).ToLowerInvariant()) && Relative(file).Length > 0) yield return file;
            foreach (string directory in directories)
            {
                string name = Path.GetFileName(directory);
                if (name.Equals("backup", StringComparison.OrdinalIgnoreCase) || name.Equals("backups", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase)
                    || name.Equals(".trash", StringComparison.OrdinalIgnoreCase)) continue;
                bool ordinary = false;
                try { ordinary = (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0; } catch { }
                if (ordinary) pending.Push(directory);
            }
        }
    }

    private static bool TryReadRawValue(string path, string wantedSection, string wantedKey, out string value)
    {
        value = ""; string section = "";
        try
        {
            foreach (string raw in File.ReadLines(path))
            {
                string trimmed = raw.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) { section = trimmed.Substring(1, trimmed.Length - 2); continue; }
                if (!section.Equals(wantedSection, StringComparison.Ordinal)) continue;
                int equals = raw.IndexOf('='); if (equals < 0 || !raw.Substring(0, equals).Trim().Equals(wantedKey, StringComparison.Ordinal)) continue;
                value = raw.Substring(equals + 1).Trim(); return true;
            }
        }
        catch { }
        return false;
    }

    private static IEnumerable<FieldInfo> Fields(Type type)
    {
        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) yield return field;
    }

    private static void AddConfig(HashSet<ConfigFile> files, ConfigFile? config) { if (config != null) files.Add(config); }
    private static string TypeName(Type type) => type.FullName ?? type.Name;
    private static string Identity(string guid, string file, string section, string key) => guid.ToUpperInvariant() + "\n" + file.Replace('\\', '/').ToUpperInvariant() + "\n" + section + "\n" + key;
    private static T Clone<T>(T value) => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(Newtonsoft.Json.JsonConvert.SerializeObject(value))!;
    // 配置目录每遍历一次就要对每个文件的每一级祖先各做三次文件系统调用；一百多个文件叠起来是几千次。
    // 文件本身每次都查，祖先目录在同一帧内按目录记住结果——跨帧重来，链接是后来才建的也能发现。
    private static readonly Dictionary<string, bool> CheckedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private static int _checkedFrame = -1;
    private static string SafeConfigPath(string root, string path)
    {
        string full = Path.GetFullPath(path), prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Loc.T("The config path is outside the config directory.", "配置路径不在配置目录中。", "設定路徑不在設定目錄中。"));
        int frame = UnityEngine.Time.frameCount;
        if (frame != _checkedFrame) { CheckedDirectories.Clear(); _checkedFrame = frame; }
        if (IsReparsePoint(full)) throw new InvalidDataException(Loc.T("The config path contains a link or reparse point.", "配置路径包含链接或重解析点。", "設定路徑包含連結或重新解析點。"));
        for (string? part = Path.GetDirectoryName(full); part != null; part = Path.GetDirectoryName(part))
        {
            if (InstallationPaths.AtGameRoot(part)) break;
            // 缓存命中说明这一级连同它的祖先都查过了，可以收工。
            if (CheckedDirectories.TryGetValue(part, out bool ordinary))
            {
                if (!ordinary) throw new InvalidDataException(Loc.T("The config path contains a link or reparse point.", "配置路径包含链接或重解析点。", "設定路徑包含連結或重新解析點。"));
                break;
            }
            bool reparse = IsReparsePoint(part);
            CheckedDirectories[part] = !reparse;
            if (reparse) throw new InvalidDataException(Loc.T("The config path contains a link or reparse point.", "配置路径包含链接或重解析点。", "設定路徑包含連結或重新解析點。"));
        }
        return full;
    }
    private static bool IsReparsePoint(string part)
        => (File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0;

    internal sealed class LiveCatalog
    {
        public readonly List<LiveFile> Files = new();
        public readonly List<OverviewConfigFileStatus> UnsupportedFiles = new();
        public readonly Dictionary<string, LiveEntry> Entries = new(StringComparer.Ordinal);
        public LiveEntry? Find(OverviewConfigValue value) => Entries.TryGetValue(value.Identity, out var found) ? found : null;
    }
    internal sealed class LiveFile
    {
        public readonly string Guid; public readonly string RelativePath; public readonly ConfigFile Config;
        public LiveFile(string guid, string relativePath, ConfigFile config) { Guid = guid; RelativePath = relativePath; Config = config; }
    }
    internal sealed class LiveEntry
    {
        public readonly LiveFile File; public readonly ConfigEntryBase Entry;
        public LiveEntry(LiveFile file, ConfigEntryBase entry) { File = file; Entry = entry; }
    }
    internal sealed class PreparedChange
    {
        public readonly LiveFile File; public readonly ConfigEntryBase Entry; public readonly object Value; public readonly string Serialized;
        public readonly OverviewConfigValue Target;
        public PreparedChange(LiveFile file, ConfigEntryBase entry, object value, string serialized, OverviewConfigValue target)
        { File = file; Entry = entry; Value = value; Serialized = serialized; Target = Clone(target); }
    }

    internal sealed class OverviewConfigTransaction
    {
        private readonly List<PreparedChange> _changes;
        private readonly List<OfflineConfigurationTransaction> _offline;
        private readonly string _backupRoot;
        private readonly string _configRoot;
        private readonly Dictionary<ConfigEntryBase, object> _oldValues = new();
        private readonly Dictionary<ConfigEntryBase, string> _oldSerialized = new();
        private readonly Dictionary<ConfigFile, bool> _oldSaveModes = new();
        private readonly Dictionary<ConfigFile, FileBackup> _backups = new();
        private readonly Dictionary<ConfigFile, byte[]> _writtenFiles = new();
        private readonly List<OfflineConfigurationTransaction> _liveDocuments = new();
        private bool _applied;
        private bool _completed;
        private bool _mutationStarted;
        public string BackupDirectory { get; private set; } = "";
        public int ChangeCount => _changes.Count;
        public bool HasDeferredChanges => DeferredSnapshot.values.Count > 0;
        public OverviewConfigSnapshot DeferredSnapshot { get; }
        public OverviewConfigSnapshot RestartSnapshot { get; } = new();
        public Dictionary<string, string> RestartPrevious { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ConfigurationEffectTiming> RestartTiming { get; } = new(StringComparer.Ordinal);

        internal OverviewConfigTransaction(List<PreparedChange> changes, string backupRoot, string configRoot, OverviewConfigSnapshot deferred, List<OfflineConfigurationTransaction>? offline = null)
        {
            _changes = changes; _offline = offline ?? new(); _backupRoot = backupRoot; _configRoot = configRoot; DeferredSnapshot = deferred;
            foreach (var file in changes.Select(c => c.File).Distinct())
            {
                string path = SafeConfigPath(configRoot, file.Config.ConfigFilePath);
                bool existed = File.Exists(path);
                _backups[file.Config] = new FileBackup(path, "", existed, existed ? File.ReadAllBytes(path) : Array.Empty<byte>());
                foreach (var pair in file.Config)
                { _oldValues[pair.Value] = pair.Value.BoxedValue; _oldSerialized[pair.Value] = pair.Value.GetSerializedValue(); }
                var fileChanges = changes.Where(c => ReferenceEquals(c.File.Config, file.Config)).ToList();
                _liveDocuments.Add(new OfflineConfigurationTransaction(path, fileChanges.Select(c => (c.Entry.Definition.Section, c.Entry.Definition.Key, c.Serialized)), () => SafeConfigPath(configRoot, path), fileChanges.Select(c => c.Target).Where(v => v.diskBaselineKnown).Select(v => (v.section, v.key, v.diskExists, v.diskValue))));
            }
            foreach (var change in changes.Where(c => OverviewRuntimeSettingsBridge.Timing(c.Entry) is ConfigurationEffectTiming.GameRestart or ConfigurationEffectTiming.NextRound))
            {
                var target = Clone(change.Target); target.serializedValue = change.Serialized; target.missing = false; target.missingReason = "";
                RestartSnapshot.values.Add(target); RestartPrevious[target.Identity] = _oldSerialized[change.Entry];
                RestartTiming[target.Identity] = OverviewRuntimeSettingsBridge.Timing(change.Entry);
            }
        }

        public void Validate()
        {
            if (_completed || _applied) throw new InvalidOperationException(Loc.T("This transaction has already run or finished. Prepare a new transaction.", "事务已经执行或结束，请重新预检。", "交易已執行或結束，請重新預檢。"));
            foreach (var pair in _backups)
            {
                var backup = pair.Value; SafeConfigPath(_configRoot, backup.Path);
                if (pair.Key.Count != _oldValues.Keys.Count(entry => ReferenceEquals(entry.ConfigFile, pair.Key)))
                    throw new IOException(Loc.T("The setting catalogue changed after preparation. Apply the profile again: ", "配置项在预检后发生变化，请重新应用方案：", "設定項在預檢後發生變更，請重新套用方案：") + backup.Path);
            }
            foreach (var pair in _oldSerialized)
                if (!string.Equals(pair.Key.GetSerializedValue(), pair.Value, StringComparison.Ordinal)) throw new IOException(Loc.T("A live value changed after preparation. Apply the profile again: ", "当前参数在预检后发生变化，请重新应用方案：", "目前參數在預檢後發生變更，請重新套用方案：") + pair.Key.Definition);
            foreach (var change in _changes)
                if (!TryConvert(change.Entry, change.Serialized, out _, out _, out string error)) throw new InvalidDataException(Loc.T("Parameter validation failed: ", "参数预检未通过：", "參數預檢未通過：") + error);
            foreach (var file in _offline.Concat(_liveDocuments)) file.Validate();
        }

        public void Apply()
        {
            if (_completed) throw new InvalidOperationException(Loc.T("This transaction has finished.", "事务已经结束。", "交易已結束。"));
            if (_applied) return;
            Validate();
            if (_changes.Count == 0 && !_offline.Any(f => f.HasChanges)) { _applied = true; return; }
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            BackupDirectory = Path.Combine(_backupRoot, stamp + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(BackupDirectory);
            try
            {
                foreach (LiveFile file in _changes.Select(c => c.File).Distinct())
                {
                    ConfigFile config = file.Config;
                    _oldSaveModes[config] = config.SaveOnConfigSet;
                    config.SaveOnConfigSet = false;
                    FileBackup original = _backups[config];
                    string backupPath = Path.Combine(BackupDirectory, SafeBackupName(file.Guid + "__" + file.RelativePath));
                    if (original.Existed) { Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!); File.WriteAllBytes(backupPath, original.Bytes); }
                    _backups[config] = new FileBackup(original.Path, backupPath, original.Existed, original.Bytes);
                }
                Validate();
                _mutationStarted = true;
                using var notifications = new ConfigurationNotificationGate.Scope(_changes.Select(c => c.Entry));
                foreach (PreparedChange change in _changes)
                {
                    change.Entry.BoxedValue = change.Value;
                    if (!string.Equals(change.Entry.GetSerializedValue(), change.Serialized, StringComparison.Ordinal))
                        throw new InvalidOperationException(Loc.T("A mod rejected or changed the requested value; application stopped: ", "模组拒绝或改变了参数值，已停止应用：", "模組拒絕或變更了參數值，已停止套用：") + change.Entry.Definition);
                }
                OverviewRuntimeSettingsBridge.Synchronize(_changes.Select(c => c.Entry));
                foreach (PreparedChange change in _changes)
                    if (!string.Equals(change.Entry.GetSerializedValue(), change.Serialized, StringComparison.Ordinal))
                        throw new InvalidOperationException(Loc.T("A mod callback rejected or changed the requested value: ", "模组回调拒绝或改变了目标值：", "模組回呼拒絕或變更了目標值：") + change.Entry.Definition);
                // Merge touched fields into the latest document, preserving unrelated
                // external edits/comments instead of serializing the entire ConfigFile.
                foreach (var file in _liveDocuments) file.Apply(BackupDirectory);
                foreach (ConfigFile config in _oldSaveModes.Keys)
                    _writtenFiles[config] = File.ReadAllBytes(config.ConfigFilePath);
                foreach (var file in _offline) file.Apply(BackupDirectory);
                _applied = true;
            }
            catch (Exception applyError)
            {
                try { Restore(); }
                catch (Exception rollbackError) { throw new AggregateException(Loc.T("Parameter application failed and rollback was incomplete. Backup: ", "参数应用失败且部分回退未完成；备份：", "參數套用失敗且部分回復未完成；備份：") + BackupDirectory, applyError, rollbackError); }
                throw;
            }
            finally { RestoreSaveModes(); }
        }

        public void Rollback()
        {
            Restore();
            _completed = true;
        }

        public void Commit()
        {
            if (!_applied) throw new InvalidOperationException(Loc.T("This transaction has not been applied.", "事务尚未应用。", "交易尚未套用。"));
            if (_completed) throw new InvalidOperationException(Loc.T("This transaction has finished.", "事务已经结束。", "交易已結束。"));
            foreach (var change in _changes) ConfigurationNotificationGate.ResetDelivery(change.Entry);
            _completed = true; // Persistent backups intentionally remain recoverable.
        }

        private void Restore()
        {
            if (!_mutationStarted) { RestoreSaveModes(); _applied = false; return; }
            var errors = new List<string>();
            foreach (var file in _offline.AsEnumerable().Reverse())
                try { file.Rollback(); } catch (Exception error) { errors.Add(error.Message); }
            var blocked = new HashSet<ConfigFile>();
            foreach (var pair in _writtenFiles)
            {
                try
                {
                    SafeConfigPath(_configRoot, pair.Key.ConfigFilePath);
                    if (!File.Exists(pair.Key.ConfigFilePath) || !File.ReadAllBytes(pair.Key.ConfigFilePath).SequenceEqual(pair.Value))
                        throw new IOException(Loc.T("The config file was changed externally after application; the new content was kept: ", "配置文件在应用后被外部修改，未覆盖新内容：", "設定檔在套用後被外部修改，已保留新內容：") + pair.Key.ConfigFilePath);
                }
                catch (Exception error) { errors.Add(error.Message); blocked.Add(pair.Key); }
            }
            foreach (ConfigFile config in _oldSaveModes.Keys) config.SaveOnConfigSet = false;
            using var notifications = new ConfigurationNotificationGate.Scope(_changes.Select(c => c.Entry));
            try
            {
                foreach (var old in _oldValues)
                    if (!blocked.Contains(old.Key.ConfigFile))
                        try
                        {
                            old.Key.BoxedValue = old.Value;
                            if (!string.Equals(old.Key.GetSerializedValue(), _oldSerialized[old.Key], StringComparison.Ordinal)) throw new IOException(Loc.T("A mod rejected restoring a parameter: ", "模组拒绝恢复参数：", "模組拒絕還原參數：") + old.Key.Definition);
                        }
                        catch (Exception error) { errors.Add(error.Message); }
                try { OverviewRuntimeSettingsBridge.Synchronize(_changes.Where(c => !blocked.Contains(c.Entry.ConfigFile)).Select(c => c.Entry)); }
                catch (Exception error) { errors.Add(error.Message); }
                foreach (var pair in _backups)
                {
                    if (blocked.Contains(pair.Key) || !_oldSaveModes.ContainsKey(pair.Key)) continue;
                    FileBackup backup = pair.Value;
                    try
                    {
                        SafeConfigPath(_configRoot, backup.Path);
                        var document = _liveDocuments.FirstOrDefault(d => d.Path.Equals(backup.Path, StringComparison.OrdinalIgnoreCase));
                        if (document != null) { document.Rollback(); continue; }
                        if (backup.Existed) File.WriteAllBytes(backup.Path, backup.Bytes);
                        else if (File.Exists(backup.Path))
                        {
                            string created = backup.BackupPath + ".rollback-created";
                            Directory.CreateDirectory(Path.GetDirectoryName(created)!); File.Move(backup.Path, created);
                        }
                    }
                    catch (Exception error) { errors.Add(error.Message); }
                }
            }
            finally { RestoreSaveModes(); }
            if (errors.Count > 0) throw new IOException(Loc.F("Parameter rollback was incomplete. Backup: {0}; {1}", "参数回退未完成；备份：{0}；{1}", "參數回復未完成；備份：{0}；{1}", BackupDirectory, string.Join("; ", errors)));
            _writtenFiles.Clear();
            _mutationStarted = false;
            _applied = false;
        }

        private void RestoreSaveModes()
        {
            foreach (var pair in _oldSaveModes) try { pair.Key.SaveOnConfigSet = pair.Value; } catch { }
        }

        private static string SafeBackupName(string value)
        {
            string hash;
            using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").Substring(0, 16);
            value = value.Replace('/', '_').Replace('\\', '_');
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            if (value.Length > 100) value = value.Substring(value.Length - 100);
            return hash + "_" + value + ".bak";
        }

        private readonly struct FileBackup
        {
            public readonly string Path; public readonly string BackupPath; public readonly bool Existed; public readonly byte[] Bytes;
            public FileBackup(string path, string backupPath, bool existed, byte[] bytes) { Path = path; BackupPath = backupPath; Existed = existed; Bytes = bytes; }
        }
    }
}
