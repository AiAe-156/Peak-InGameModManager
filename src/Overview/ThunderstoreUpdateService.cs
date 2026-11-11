#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

internal sealed class ThunderstoreUpdateService : IDisposable
{
    private readonly OverviewRepository _repository;
    private readonly SemaphoreSlim _parallel = new(2);
    private readonly SemaphoreSlim _run = new(1);
    private readonly TimeSpan _cacheLifetime;
    private readonly Dictionary<string, CacheEntry> _cache;
    private readonly string _cachePath;
    private static readonly object CacheSaveGate = new();
    private static long _cacheRevision;
    internal static long CacheRevision => Interlocked.Read(ref _cacheRevision);
    internal sealed class CheckProgress
    {
        internal int ModCount, EligibleModCount, PackageCount;
        internal int Completed, Succeeded, Failed, UpdateCount;
        /// <summary>Failures worth retrying soon (network, timeouts, server errors). A deprecated, missing or mismatched
        /// package is a settled store answer and only counts in <see cref="Failed"/>.</summary>
        internal int TransientFailed;
        internal Dictionary<string, int[]> Groups = new();
        internal bool Cancelled;
        internal DateTime StartedUtc, FinishedUtc;
        internal string Error = "";
        internal string Summary => (Cancelled ? Loc.T("Update check cancelled. ", "更新检查已取消。", "更新檢查已取消。") : "")
            + (FinishedUtc == default ? Loc.F("Checking updates {0}/{1}", "正在检查更新 {0}/{1}", "正在檢查更新 {0}/{1}", Completed, ModCount)
                : Loc.F("Checked {0}/{1} mods; {2} mods have updates.", "共检查了 {0}/{1} 个模组，检查出 {2} 个可更新模组。", "共檢查了 {0}/{1} 個模組，檢查出 {2} 個可更新模組。", Completed, ModCount, UpdateCount))
            + (Error.Length > 0 ? "\n" + Loc.LocalizeMessage(Error) : "");
        internal string GroupProgress(string id, string displayName) => Groups.TryGetValue(id, out var value)
            ? displayName + ": " + Loc.F("Checking updates {0}/{1}", "检查更新 {0}/{1}", "檢查更新 {0}/{1}", value[0], value[1]) : "";
        internal CheckProgress Copy() { var copy = (CheckProgress)MemberwiseClone(); copy.Groups = Groups.ToDictionary(p => p.Key, p => (int[])p.Value.Clone()); return copy; }
    }
    private readonly object _progressGate = new();
    private CheckProgress _progress = new();
    internal CheckProgress LastProgress { get { lock (_progressGate) return _progress.Copy(); } }
    internal CheckProgress LastReport => LastProgress;
    internal sealed class CacheEntry
    {
        public string Namespace = "";
        public string Name = "";
        public string Version = "";
        public string Notes = "";
        public string Author = "", Homepage = "";
        public DateTime CheckedUtc;
        public DateTime LastSuccessfulUtc;
        public string Error = "";
        /// <summary>The error is the store's settled answer (deprecated, 404, identity mismatch), not a network failure.</summary>
        public bool Settled;
        public bool CategoriesKnown;
        public List<string> Categories = new();
    }
    public ThunderstoreUpdateService(OverviewRepository repository, TimeSpan? cacheLifetime = null)
    {
        _repository = repository; _cacheLifetime = cacheLifetime ?? TimeSpan.FromMinutes(30);
        _cachePath = Path.Combine(repository.RootDirectory, "updates.json");
        try { _cache = File.Exists(_cachePath) ? JsonConvert.DeserializeObject<Dictionary<string, CacheEntry>>(File.ReadAllText(_cachePath)) ?? new() : new(); }
        catch { _cache = new(); }
    }
    public async Task CheckAsync(IReadOnlyList<OverviewMod> mods, CancellationToken token, Action<int, int>? progress = null)
    {
        await ModSources.Coordinator.WaitAsync(token).ConfigureAwait(false);
        bool cancelled = false; string error = "";
        string context = ModSources.Context; long choiceRevision = ModSources.ChoiceRevision;
        // The caller's full, fixed catalogue is the unit of work, not HTTP identities or rendered rows.
        var infrastructure = InfrastructurePackages.Scan();
        var componentRows = infrastructure.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(g => {
            var p = g.OrderByDescending(p => p.ManifestEvidence != null).First(); var row = new OverviewMod { Guid = "component:" + g.Key, Id = "component:" + g.Key,
                RawName = p.Name, PackageName = p.Name, PackageRoot = p.Path, Location = p.Path, Author = p.Author, AuthorSource = p.ManifestEvidence != null ? "manifest" : "", HomepageUrl = p.Homepage,
                PackageVersion = p.Version, SourceNamespace = p.IdentityConfirmed ? p.Team : "", SourceName = p.IdentityConfirmed ? p.Name : "",
                Rule = new OverviewModRule { groupId = "__infrastructure" } };
            if (p.ManifestEvidence != null) ModSources.ReadManifestIdentity(row, p.ManifestEvidence, p.Path);
            return row;
        }).ToArray();
        var entries = mods.Concat(componentRows).ToArray(); var completed = new HashSet<OverviewMod>();
        // Group edits remain available while awaiting the catalogue; totals belong to this round.
        var groups = entries.Distinct().ToDictionary(m => m, m => m.Rule.groupId);
        bool componentsAllowed = _repository.GetGlobalUpdateAllowed();
        var components = new HashSet<OverviewMod>(componentRows);
        bool Allowed(OverviewMod mod) => components.Contains(mod) ? componentsAllowed : _repository.IsUpdateAllowed(mod.Rule);
        void Valid() {
            token.ThrowIfCancellationRequested();
            if (context != ModSources.Context || choiceRevision != ModSources.ChoiceRevision) throw new OperationCanceledException("Source context changed.", token);
        }
        void Complete(OverviewMod mod, bool failed = false, bool transient = false) {
            CheckProgress snapshot;
            lock (_progressGate) {
                if (!completed.Add(mod)) return;
                _progress.Completed++; if (failed) { _progress.Failed++; if (transient) _progress.TransientFailed++; } else _progress.Succeeded++;
                if (mod.HasUpdate) _progress.UpdateCount++;
                _progress.Groups[groups[mod]][0]++;
                snapshot = _progress.Copy();
            }
            progress?.Invoke(snapshot.Completed, snapshot.ModCount);
        }
        try {
            lock (_progressGate) _progress = new CheckProgress { ModCount = entries.Length, StartedUtc = DateTime.UtcNow,
                Groups = entries.GroupBy(m => groups[m]).ToDictionary(g => g.Key, g => new[] { 0, g.Count() }) };
            foreach (var mod in entries) { mod.HasUpdate = false; mod.LatestVersion = ""; mod.LastCheckedUtc = null; mod.UpdateNotes = ""; mod.CheckMessage = Loc.T("Checking updates", "正在检查更新", "正在檢查更新"); }
            progress?.Invoke(0, entries.Length);
            Valid();
            var discoverable = entries.Where(Allowed).ToArray();
            if (discoverable.Length > 0)
                await ModSources.Discover(discoverable, token, _cacheLifetime == TimeSpan.Zero).ConfigureAwait(false);
            Valid();
            var eligible = new List<OverviewMod>();
            foreach (var mod in entries) {
                Valid();
                if (!Allowed(mod)) { mod.CheckMessage = Loc.T("Update checks disabled", "禁止检查更新", "禁止檢查更新"); Complete(mod); }
                else if (!mod.SourceConfirmed && components.Contains(mod) && InfrastructureFallback(mod)
                    && !ShopCatalog.Cached().Any(p => p.Team.Equals(mod.SourceNamespace, StringComparison.OrdinalIgnoreCase)
                        && p.Name.Equals(mod.SourceName, StringComparison.OrdinalIgnoreCase))) eligible.Add(mod);
                else if (!mod.SourceConfirmed) { mod.CheckMessage = ModSources.ExplicitLocal(mod)
                    ? Loc.T("Explicit local source; automatic matching is off.", "本地自用，已停止自动匹配。", "本機自用，已停止自動配對。")
                    : Loc.T("Store source needs confirmation.", "商店来源待确认。", "商店來源待確認。"); Complete(mod); }
                else eligible.Add(mod);
            }
            var packages = eligible.GroupBy(m => m.SourceKey, StringComparer.OrdinalIgnoreCase).ToArray();
            lock (_progressGate) { _progress.EligibleModCount = eligible.Count; _progress.PackageCount = packages.Length; }
            var catalog = packages.Length > 0 ? await ReadCatalog(token, false).ConfigureAwait(false) : new Dictionary<string, ShopListing>();
            await Task.WhenAll(packages.Select(async package => {
                await _parallel.WaitAsync(token).ConfigureAwait(false);
                try {
                    Valid(); var first = package.First();
                    CacheEntry entry = await Fetch(first.SourceNamespace, first.SourceName, token, catalog,
                        infrastructureFallback: components.Contains(first) && InfrastructureFallback(first)).ConfigureAwait(false);
                    Valid();
                    foreach (var mod in package) {
                        if (entry.Error.Length == 0) { mod.SourceConfirmed = true; mod.SourceKind = ModSourceKind.Store; }
                        Apply(mod, entry); Complete(mod, entry.Error.Length > 0, entry.Error.Length > 0 && !entry.Settled);
                    }
                } finally { _parallel.Release(); }
            })).ConfigureAwait(false);
            Valid();
            foreach (var component in infrastructure) {
                var row = componentRows.FirstOrDefault(m => m.Guid == "component:" + component.Key);
                component.Latest = row != null && row.LastCheckedUtc != null && row.CheckMessage.Length > 0 ? row.LatestVersion : "";
            }
            InfrastructurePackages.SetResults(infrastructure); SaveCache();
        }
        catch (OperationCanceledException) { cancelled = true; throw; }
        catch (Exception ex) { error = ex.Message; throw; }
        finally {
            // Do not erase results already applied by completed packages on cancellation/failure.
            foreach (var mod in entries.Where(m => !completed.Contains(m))) {
                mod.HasUpdate = false; mod.LatestVersion = ""; mod.LastCheckedUtc = null; mod.UpdateNotes = "";
                mod.CheckMessage = cancelled ? Loc.T("Update check cancelled", "更新检查已取消", "更新檢查已取消")
                    : Loc.T("Check failed: ", "检查失败：", "檢查失敗：") + Loc.LocalizeMessage(error);
            }
            lock (_progressGate) { _progress.Cancelled = cancelled; _progress.Error = error; _progress.FinishedUtc = DateTime.UtcNow; }
            ModSources.Coordinator.Release();
        }
    }
    internal void ApplyCachedCategories(IEnumerable<OverviewMod> mods)
    {
        lock (_cache) MergeDiskCache();
        foreach (var mod in mods)
        {
            CacheEntry? entry = null;
            if (mod.SourceConfirmed && ValidPart(mod.SourceNamespace) && ValidPart(mod.SourceName))
                lock (_cache)
                    if (_cache.TryGetValue(mod.SourceKey, out var cached) && cached.Namespace == mod.SourceNamespace && cached.Name == mod.SourceName) entry = cached;
            if (entry != null && entry.Error.Length == 0 && DateTime.UtcNow - entry.CheckedUtc < _cacheLifetime) Apply(mod, entry);
            else { mod.HasUpdate = false; mod.LatestVersion = ""; mod.UpdateNotes = ""; }
            if (!_repository.IsUpdateAllowed(mod.Rule)) { mod.HasUpdate = false; mod.UpdateNotes = ""; mod.CheckMessage = Loc.T("Update checks disabled", "禁止检查更新", "禁止檢查更新"); }
            else if (!mod.SourceConfirmed) { mod.HasUpdate = false; mod.LatestVersion = ""; mod.UpdateNotes = ""; mod.CheckMessage = Loc.T("Waiting for a confirmed update source", "等待确认更新来源", "等待確認更新來源"); }
            mod.CategoriesKnown = entry?.CategoriesKnown == true;
            mod.Categories = mod.CategoriesKnown ? new List<string>(entry!.Categories ?? new()) : new();
        }
    }
    public async Task SyncCategoriesAsync(IReadOnlyList<OverviewMod> mods, CancellationToken token, Action<int, int>? progress = null)
    {
        await _run.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ApplyCachedCategories(mods);
            var packages = mods.Where(m => m.SourceConfirmed && ValidPart(m.SourceNamespace) && ValidPart(m.SourceName))
                .GroupBy(m => m.SourceKey, StringComparer.OrdinalIgnoreCase).ToList();
            var catalog = packages.Count > 0 ? await ReadCatalog(token, _cacheLifetime == TimeSpan.Zero).ConfigureAwait(false) : new Dictionary<string, ShopListing>();
            int completed = 0;
            await Task.WhenAll(packages.Select(async package =>
            {
                await _parallel.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var first = package.First();
                    await Fetch(first.SourceNamespace, first.SourceName, token, catalog, requireCategories: true).ConfigureAwait(false);
                }
                finally { _parallel.Release(); progress?.Invoke(Interlocked.Increment(ref completed), packages.Count); }
            })).ConfigureAwait(false);
            SaveCache();
            ApplyCachedCategories(mods);
        }
        finally { _run.Release(); }
    }
    public async Task<CacheEntry?> CheckPackAsync(OverviewPack pack, CancellationToken token)
    {
        if (!_repository.IsPackUpdateAllowed(pack) || !ValidPart(pack.sourceNamespace) || !ValidPart(pack.sourceName)) return null;
        await _run.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!_repository.IsPackUpdateAllowed(pack)) return null;
            var catalog = await ReadCatalog(token, false).ConfigureAwait(false);
            CacheEntry entry = await Fetch(pack.sourceNamespace, pack.sourceName, token, catalog).ConfigureAwait(false);
            if (_repository.IsPackUpdateAllowed(pack) && IsNewer(entry.Version, pack.version) && string.IsNullOrEmpty(entry.Error) && string.IsNullOrEmpty(entry.Notes)) await FetchNotes(entry, token).ConfigureAwait(false);
            SaveCache();
            return entry;
        }
        finally { _run.Release(); }
    }
    private void SaveCache()
    {
        lock (CacheSaveGate)
        lock (_cache)
        {
            MergeDiskCache();
            OverviewRepository.AtomicWrite(_cachePath, JsonConvert.SerializeObject(_cache, Formatting.Indented));
            Interlocked.Increment(ref _cacheRevision);
        }
    }
    // Callers hold _cache; page reloads can then see metadata saved by another service instance.
    private void MergeDiskCache()
    {
        try
        {
            var disk = File.Exists(_cachePath) ? JsonConvert.DeserializeObject<Dictionary<string, CacheEntry>>(File.ReadAllText(_cachePath)) : null;
            if (disk != null) foreach (var item in disk)
            {
                if (!_cache.TryGetValue(item.Key, out var local)) { _cache[item.Key] = item.Value; continue; }
                var newer = item.Value.CheckedUtc > local.CheckedUtc ? item.Value : local;
                var older = ReferenceEquals(newer, local) ? item.Value : local;
                if (!newer.CategoriesKnown && older.CategoriesKnown && newer.Namespace == older.Namespace && newer.Name == older.Name)
                { newer.CategoriesKnown = true; newer.Categories = new List<string>(older.Categories ?? new()); }
                _cache[item.Key] = newer;
            }
        }
        catch { }
    }
    private static async Task<Dictionary<string, ShopListing>> ReadCatalog(CancellationToken token, bool refresh)
        => (await ShopCatalog.Load(token, refresh).ConfigureAwait(false)).GroupBy(p => p.Team + "-" + p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    private async Task<CacheEntry> Fetch(string ns, string name, CancellationToken token, IReadOnlyDictionary<string, ShopListing> catalog, bool requireCategories = false, bool infrastructureFallback = false)
    {
        string key = ns + "-" + name; CacheEntry? cached;
        lock (_cache) _cache.TryGetValue(key, out cached);
        if (cached != null && (cached.Namespace != ns || cached.Name != name)) cached = null;
        var result = new CacheEntry
        {
            Namespace = ns, Name = name, CheckedUtc = DateTime.UtcNow, Version = cached?.Version ?? "", Notes = cached?.Notes ?? "", Author = cached?.Author ?? "", Homepage = cached?.Homepage ?? "",
            CategoriesKnown = cached?.CategoriesKnown == true, Categories = new List<string>(cached?.Categories ?? new()), LastSuccessfulUtc = cached?.LastSuccessfulUtc ?? default
        };
        try
        {
            // The PEAK catalogue is shared with the store and source picker. Cross-community
            // loader packages can be absent, so only those need the exact-package endpoint.
            if (catalog.TryGetValue(key, out var retired) && retired.Deprecated)
                throw new InvalidDataException(Loc.T("Store source identified, but this package is deprecated; automatic updates are unavailable.", "商店来源已确认，但该包已弃用，自动更新不可用。", "商店來源已確認，但該包已棄用，自動更新不可用。"));
            if (catalog.TryGetValue(key, out var indexed) && !indexed.Deprecated && (!requireCategories || indexed.CategoriesKnown))
            {
                if (!TryVersion(indexed.Version, out _)) throw new InvalidDataException("The server has no valid latest version.");
                if (indexed.Version != result.Version) result.Notes = "";
                result.Version = indexed.Version; result.Author = indexed.Team; result.Homepage = indexed.Homepage;
                if (indexed.CategoriesKnown) { result.CategoriesKnown = true; result.Categories = new List<string>(indexed.Categories); }
                result.LastSuccessfulUtc = result.CheckedUtc;
                lock (_cache) _cache[key] = result;
                return result;
            }
            if (!catalog.ContainsKey(key) && !(infrastructureFallback && KnownInfrastructure(ns, name))) throw new InvalidDataException("No active PEAK store identity was found.");
            JObject json = JObject.Parse(await Get(ApiUrl(ns, name), token).ConfigureAwait(false));
            string actualName = json.Value<string>("name") ?? "";
            string actualNs = json.Value<string>("namespace") ?? json.Value<string>("owner") ?? "";
            if (!actualName.Equals(name, StringComparison.OrdinalIgnoreCase) || !actualNs.Equals(ns, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(Loc.T("The server returned a package identity that differs from the source.", "服务器返回的包身份与来源不一致", "服務器返回的包身份與來源不一致"));
            if (json.Value<bool?>("is_deprecated") != false || json["latest"] is not JObject activeVersion
                || activeVersion.Value<bool?>("is_active") != true) throw new InvalidDataException("The package has no active latest version.");
            if (infrastructureFallback && StoreIdentityIndex.Repository(json["latest"]?.Value<string>("website_url") ?? "")
                != StoreIdentityIndex.Repository("https://github.com/MonoDetour/MonoDetour")) throw new InvalidDataException("Infrastructure repository differs from the declared source.");
            string latest = json["latest"]?.Value<string>("version_number") ?? "";
            if (!TryVersion(latest, out _)) throw new InvalidDataException(Loc.T("The server has no valid latest version.", "服务器没有有效的最新版本", "服務器沒有有效的最新版本"));
            if (latest != result.Version) result.Notes = "";
            result.Version = latest;
            result.Author = actualNs; result.Homepage = json["latest"]?.Value<string>("website_url") ?? "";
            var listing = (json["community_listings"] as JArray)?.OfType<JObject>()
                .FirstOrDefault(value => string.Equals(value.Value<string>("community"), "peak", StringComparison.OrdinalIgnoreCase));
            if (listing == null || listing.Value<bool?>("is_active") != true || listing.Value<bool?>("is_reviewed") != true)
                throw new InvalidDataException("The package has no active PEAK listing.");
            if (listing?["categories"] is JArray categories && categories.All(value => value.Type == JTokenType.String))
            {
                result.CategoriesKnown = true;
                result.Categories = categories.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            result.LastSuccessfulUtc = result.CheckedUtc;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        // A 4xx (other than rate limiting) and every InvalidDataException above are the store's own verdict on this
        // package; asking again in a minute cannot change them. Network, timeout and 5xx failures stay retryable.
        catch (StoreRequestException ex) when (ex.StatusCode == 404) { result.Error = "The store could not find this package (HTTP 404). Verify its source."; result.Settled = true; }
        catch (StoreRequestException ex) when (ex.StatusCode >= 400 && ex.StatusCode < 500 && ex.StatusCode != 408 && ex.StatusCode != 429) { result.Error = Loc.T("Check failed: ", "检查失败：", "檢查失敗：") + Loc.LocalizeMessage(ex.Message); result.Settled = true; }
        catch (InvalidDataException ex) { result.Error = Loc.T("Check failed: ", "检查失败：", "檢查失敗：") + Loc.LocalizeMessage(ex.Message); result.Settled = true; }
        catch (Exception ex) { result.Error = Loc.T("Check failed: ", "检查失败：", "檢查失敗：") + Loc.LocalizeMessage(ex.Message); }
        lock (_cache) _cache[key] = result;
        return result;
    }
    private static bool KnownInfrastructure(string ns, string name) => ns == "MonoDetour"
        && (name == "MonoDetour" || name == "MonoDetour_BepInEx_5");
    private static bool InfrastructureFallback(OverviewMod mod) => KnownInfrastructure(mod.SourceNamespace, mod.SourceName)
        && ModSources.ExplicitManifestIdentity(mod, mod.SourceNamespace, mod.SourceName)
        && StoreIdentityIndex.Repository(mod.HomepageUrl) == StoreIdentityIndex.Repository("https://github.com/MonoDetour/MonoDetour");
    private async Task FetchNotes(CacheEntry entry, CancellationToken token)
    {
        try
        {
            string body = await Get(ApiUrl(entry.Namespace, entry.Name) + entry.Version + "/changelog/", token).ConfigureAwait(false);
            JToken json = JToken.Parse(body);
            entry.Notes = json.Type == JTokenType.String ? json.Value<string>() ?? "" : json.Value<string>("markdown") ?? json.Value<string>("changelog") ?? "";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { /* Metadata remains valid when an author has no changelog. */ }
    }
    private async Task<string> Get(string url, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var client = ShopNetwork.Client();
        using var response = await ShopNetwork.Send(client, url, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new StoreRequestException((int)response.StatusCode);
        var bytes = await ShopNetwork.ReadBody(response, 8 * 1024 * 1024, timeout.Token).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
    private sealed class StoreRequestException : IOException
    {
        internal int StatusCode { get; }
        internal StoreRequestException(int statusCode) : base("Store request failed (HTTP " + statusCode + ").") { StatusCode = statusCode; }
    }
    private static void ApplyMetadata(OverviewMod mod, CacheEntry entry)
    {
        if (string.IsNullOrWhiteSpace(mod.Author) && entry.Author.Length > 0) { mod.Author = entry.Author; mod.AuthorSource = "store"; }
        if (string.IsNullOrWhiteSpace(mod.HomepageUrl) && Uri.TryCreate(entry.Homepage, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https")) mod.HomepageUrl = uri.AbsoluteUri;
    }
    private static void Apply(OverviewMod mod, CacheEntry entry)
    {
        ApplyMetadata(mod, entry);
        mod.CategoriesKnown = entry.CategoriesKnown;
        mod.Categories = entry.CategoriesKnown ? new List<string>(entry.Categories ?? new()) : new();
        mod.LatestVersion = entry.Version; mod.LastCheckedUtc = entry.CheckedUtc; mod.HasUpdate = false; mod.UpdateNotes = "";
        if (!string.IsNullOrEmpty(entry.Error)) { mod.LatestVersion = ""; mod.CheckMessage = Loc.LocalizeMessage(entry.Error) + " (" + entry.CheckedUtc.ToLocalTime().ToString("g") + ")"; return; }
        if (!TryVersion(mod.PackageVersion, out _)) { mod.CheckMessage = Loc.T("Latest package found. The local package version is unknown, so its DLL version is not used for comparison.", "已查到最新包版本，本地包版本不明，不能比较DLL版本", "已查到最新包版本，本地包版本不明，不能比較DLL版本"); return; }
        mod.HasUpdate = IsNewer(entry.Version, mod.PackageVersion);
        // 2.4.4: an update to this very version already finished, yet the scan still reads a lower one → a different file holds
        // the shown version. Offering the same update again cannot fix it (that is how MonoDetour nagged forever).
        if (mod.HasUpdate && ManifestCopies.Contradicts(mod.PackageVersion, entry.Version, OverviewUpdateQueue.CompletedVersion(mod.SourceNamespace, mod.SourceName)))
        {
            mod.HasUpdate = false; mod.UpdateNotes = "";
            mod.CheckMessage = Loc.F("Version information disagrees: the update to {0} already finished, but the version read here is {1}. Another copy of manifest.json is probably out of date.",
                "版本信息不一致：已更新到 {0}，但这里读到的仍是 {1}。很可能还有一份旧的 manifest.json 没被更新。",
                "版本資訊不一致：已更新到 {0}，但這裡讀到的仍是 {1}。很可能還有一份舊的 manifest.json 沒被更新。", OverviewUpdateQueue.CompletedVersion(mod.SourceNamespace, mod.SourceName), mod.PackageVersion);
            return;
        }
        mod.UpdateNotes = mod.HasUpdate ? entry.Notes : "";
        mod.CheckMessage = mod.HasUpdate ? Loc.T("Update available", "有新版本", "有新版本") : Loc.T("No newer version found", "未发现更高版本", "未發現更高版本");
    }
    internal static bool ValidPart(string value) => !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"^[A-Za-z0-9_]+$");
    public static bool TryParseSource(string value, out string ns, out string name)
    {
        ns = name = ""; value = (value ?? "").Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!(uri.Scheme == "https" || uri.Scheme == "http") || !(uri.Host == "thunderstore.io" || uri.Host.EndsWith(".thunderstore.io", StringComparison.OrdinalIgnoreCase))) return false;
            var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            int marker = Array.FindIndex(segments, s => s == "p" || s == "package");
            if (marker < 0 || marker + 2 >= segments.Length) return false;
            ns = segments[marker + 1]; name = segments[marker + 2];
        }
        else
        {
            Match match = Regex.Match(value, @"^([A-Za-z0-9_]+)-([A-Za-z0-9_]+)(?:-\d+\.\d+\.\d+)?$");
            if (!match.Success) return false; ns = match.Groups[1].Value; name = match.Groups[2].Value;
        }
        if (ValidPart(ns) && ValidPart(name)) return true; ns = name = ""; return false;
    }
    public static string HomeUrl(string ns, string name) => ValidPart(ns) && ValidPart(name) ? "https://thunderstore.io/c/peak/p/" + ns + "/" + name + "/" : "";
    private static string ApiUrl(string ns, string name) => "https://thunderstore.io/api/experimental/package/" + ns + "/" + name + "/";
    public static bool IsNewer(string latest, string current)
    {
        if (!TryVersion(latest, out var a) || !TryVersion(current, out var b)) return false;
        int compare = a.CompareTo(b); if (compare != 0) return compare > 0;
        string ap = Prerelease(latest), bp = Prerelease(current);
        if (ap.Length == 0 || bp.Length == 0) return ap.Length == 0 && bp.Length != 0;
        string[] aa = ap.Split('.'), bb = bp.Split('.');
        for (int i = 0; i < Math.Min(aa.Length, bb.Length); i++)
        {
            bool an = long.TryParse(aa[i], out long av), bn = long.TryParse(bb[i], out long bv);
            int c = an && bn ? av.CompareTo(bv) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(aa[i], bb[i]);
            if (c != 0) return c > 0;
        }
        return aa.Length > bb.Length;
    }
    private static string Prerelease(string value)
    {
        string plain = value.Split('+')[0]; int dash = plain.IndexOf('-');
        return dash < 0 ? "" : plain.Substring(dash + 1);
    }
    private static bool TryVersion(string value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (!Regex.IsMatch(value ?? "", @"^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$")) return false;
        return Version.TryParse((value ?? "").Split('-', '+')[0], out version!);
    }
    public void Dispose() { _parallel.Dispose(); _run.Dispose(); }
}





