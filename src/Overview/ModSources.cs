#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
namespace ModConfigEnhance.Overview;
internal enum ModSourceKind { Pending, Store, Local }
internal sealed class ModSourceRecord
{
    public ModSourceKind kind;
    public bool explicitLocal;
    public string distribution = "", installation = "", environment = "", instance = "";
    public string ns = "", name = "", author = "", homepage = "", packageVersion = "", note = "";
}
internal sealed class StorePackage
{
    public string Name = "", Author = "", Version = "", Homepage = "", Url = "";
    public DateTime Updated;
    public bool Deprecated;
    public List<string> Versions = new();
    public Dictionary<string, List<string>> Dependencies = new();
}
internal static partial class ModSources
{
    private static readonly object Gate = new();
    private static Dictionary<string, ModSourceRecord>? _records;
    private static Dictionary<string, ModSourceRecord>? _published;
    private static List<StorePackage>? _catalog;
    private static List<ShopListing>? _catalogSnapshot;
    private static string FilePath => Path.Combine(Loc.RuntimeDirectory, "sources.json");
    internal static void InvalidateRecords() { lock (Gate) { _records = null; _published = null; _derivedContext = ""; Interlocked.Increment(ref _choiceRevision); } }
    private static Dictionary<string, ModSourceRecord> Records
    {
        get
        {
            if (_records != null) return _records;
            _records = new(StringComparer.OrdinalIgnoreCase);
            foreach (string path in new[] { FilePath })
                if (File.Exists(path)) try { foreach (var pair in JsonConvert.DeserializeObject<Dictionary<string, ModSourceRecord>>(File.ReadAllText(path)) ?? new()) _records[pair.Key] = pair.Value; } catch (Exception e) { Plugin.Log.LogWarning("[来源] " + e.Message); }
            return _records;
        }
    }
    internal static ModSourceRecord? Get(string guid)
    {
        lock (Gate)
        {
            // Automatic mode may use published identity hints, never its Local classification as a personal choice.
            if (Records.TryGetValue(guid, out var record) && record.note != "auto") return record;
            if (_published == null)
            {
                _published = new(StringComparer.OrdinalIgnoreCase);
                foreach (string path in PublishedPack.Packs.Where(p => p.Valid).Select(p => Path.Combine(p.Root, "sources.json")))
                    if (File.Exists(path)) try { foreach (var pair in JsonConvert.DeserializeObject<Dictionary<string, ModSourceRecord>>(File.ReadAllText(path)) ?? new()) _published[pair.Key] = pair.Value; } catch (Exception e) { Plugin.Log.LogWarning("[来源] " + e.Message); }
            }
            return _published.TryGetValue(guid, out record) ? record : null;
        }
    }
    internal static void Set(IEnumerable<OverviewMod> mods, ModSourceRecord? record)
    {
        lock (Gate)
        {
            Interlocked.Increment(ref _choiceRevision);
            foreach (var mod in mods)
            {
                var choice = record == null ? new ModSourceRecord { kind = ModSourceKind.Pending, note = "auto" }
                    : JsonConvert.DeserializeObject<ModSourceRecord>(JsonConvert.SerializeObject(record))!;
                choice.explicitLocal = choice.kind == ModSourceKind.Local;
                choice.installation = mod.PackageRoot; choice.environment = Context; choice.instance = Instance(mod);
                Records[mod.Guid] = choice;
                // Personal choices must never survive as automatic cache authority.
                ReadDerived(); Derived.Remove(Instance(mod));
                Misses.Remove(Instance(mod));
            }
            SaveDerived();
            OverviewRepository.AtomicWrite(FilePath, JsonConvert.SerializeObject(Records, Formatting.Indented));
        }
    }
    internal static void Apply(OverviewMod mod) => Apply(mod, Project(ShopCatalog.Cached()));
    internal static void Apply(OverviewMod mod, IReadOnlyList<StorePackage> catalog)
    {
        var identityIndex = Index(catalog as List<StorePackage> ?? catalog.ToList());
        Original(mod).Restore(mod);
        if (TryInstalledStore(mod)) return;
        var record = Get(mod.Guid);
        bool local = ExplicitLocal(mod);
        if (record != null && (record.note == "user" ? PersonalScope(record, mod) : Applies(record, mod)) && record.note != "auto") {
            mod.SourceNamespace = record.ns; mod.SourceName = record.name;
            // A selected mapping is authority only through UserChoice and its installation fingerprint.
            if (record.author.Length > 0 && mod.Author.Length == 0) { mod.Author = record.author; mod.AuthorSource = "legacy"; }
            if (record.homepage.Length > 0 && (mod.HomepageUrl.Length == 0 || UserChoice(mod, record.ns, record.name))) mod.HomepageUrl = record.homepage;
            if (mod.PackageVersion.Length == 0) mod.PackageVersion = record.packageVersion;
        }
        lock (Gate) {
            ReadDerived();
            string instance = Instance(mod);
            if (!local && (record == null || record.note == "auto") && Derived.TryGetValue(instance, out var derived))
            {
                string ns = mod.SourceNamespace, name = mod.SourceName;
                mod.SourceNamespace = derived.ns; mod.SourceName = derived.name;
                // Replaceable cache is only a hint: revalidate against current catalogue and local evidence.
                if (identityIndex.Resolve(mod) == null)
                { Derived.Remove(instance); mod.SourceNamespace = ns; mod.SourceName = name; }
            }
        }
        var resolved = local ? null : identityIndex.Resolve(mod);
        // Published/legacy hints cannot veto independent scanner evidence. Manual and manifest identities remain authoritative.
        if (resolved == null && !local && Original(mod).Namespace.Length == 0
            && !(record != null && record.note == "user" && PersonalScope(record, mod)))
        {
            string hintedNs = mod.SourceNamespace, hintedName = mod.SourceName;
            mod.SourceNamespace = mod.SourceName = "";
            resolved = identityIndex.Resolve(mod);
            if (resolved == null) { mod.SourceNamespace = hintedNs; mod.SourceName = hintedName; }
        }
        mod.SourceConfirmed = resolved != null;
        if (resolved != null) { mod.SourceNamespace = resolved.Author; mod.SourceName = resolved.Name; }
        mod.SourceKind = local ? ModSourceKind.Local : mod.SourceConfirmed ? ModSourceKind.Store : ModSourceKind.Pending;
    }
    internal static List<OverviewMod> PendingMods(IEnumerable<OverviewMod> mods)
    {
        var pending = new List<OverviewMod>();
        foreach (var mod in mods) { Apply(mod); if (!ExplicitLocal(mod) && !mod.SourceConfirmed) pending.Add(mod); }
        return pending;
    }
    internal static void Confirm(OverviewMod mod, StorePackage package) { if (!Verified(package.Author, package.Name)) throw new InvalidDataException("Store identity is not present in the PEAK catalog."); Set(new[] { mod }, new ModSourceRecord { kind = ModSourceKind.Store, ns = package.Author, name = package.Name, note = "user", author = package.Author, homepage = ThunderstoreUpdateService.TryParseSource(mod.HomepageUrl, out _, out _) ? package.Homepage : mod.HomepageUrl.Length > 0 ? mod.HomepageUrl : package.Homepage, packageVersion = mod.PackageVersion }); }
    internal static void Export(string root, IEnumerable<OverviewMod> mods)
    {
        var result = new Dictionary<string, ModSourceRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var original in mods)
        {
            // Resolve a detached projection. Export must not mutate the live UI rows.
            var mod = ExportProjection(original);
            Apply(mod);
            var copy = new ModSourceRecord { kind = mod.SourceKind, ns = mod.SourceNamespace, name = mod.SourceName,
                author = mod.Author, homepage = mod.HomepageUrl, packageVersion = mod.PackageVersion };
            // Personal local opt-outs, fingerprints and notes belong to this installation only.
            // Embedded distribution is recorded separately by the publisher, not as Local.
            if (result.TryGetValue(mod.Guid, out var existing) && (existing.kind != copy.kind
                || !existing.ns.Equals(copy.ns, StringComparison.OrdinalIgnoreCase) || !existing.name.Equals(copy.name, StringComparison.OrdinalIgnoreCase)
                || existing.packageVersion != copy.packageVersion))
                throw new InvalidDataException("Conflicting exported sources for plugin GUID: " + mod.Guid);
            result[mod.Guid] = copy;
        }
        OverviewRepository.AtomicWrite(Path.Combine(root, "sources.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
    }
    internal static async Task<List<StorePackage>> Catalog(CancellationToken token, bool force = false)
    {
        return Project(await ShopCatalog.Load(token, force).ConfigureAwait(false));
    }
    private static List<StorePackage> Project(List<ShopListing> snapshot)
    {
        lock (Gate)
        {
            if (_catalog != null && ReferenceEquals(_catalogSnapshot, snapshot)) return _catalog;
            _catalog = snapshot.Select(package => new StorePackage {
                Name = package.Name, Author = package.Team, Version = package.Version, Homepage = package.Homepage,
                Url = package.Url, Updated = package.Updated, Deprecated = package.Deprecated, Versions = package.Versions.Select(v => v.Version).Distinct().ToList(),
                Dependencies = package.Versions.GroupBy(v => v.Version).ToDictionary(g => g.Key, g => g.First().Dependencies.ToList())
            }).ToList();
            _catalogSnapshot = snapshot; _index = new(_catalog);
            return _catalog;
        }
    }
    // Only scanner-read manifest declarations enter this table. Folder names, source overlays,
    // exported records and derived caches must never manufacture explicit local evidence.
    private sealed class ManifestIdentity { internal string Namespace = "", Name = ""; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<OverviewMod, ManifestIdentity> ManifestIdentities = new();
    internal static void ReadManifestIdentity(OverviewMod mod, Newtonsoft.Json.Linq.JObject data, string root)
    {
        ManifestIdentities.Remove(mod);
        string ns = data.Value<string>("namespace") ?? data.Value<string>("author") ?? "";
        string name = data.Value<string>("name") ?? "";
        if (!ThunderstoreUpdateService.ValidPart(ns) || !ThunderstoreUpdateService.ValidPart(name)) return;
        string full = data.Value<string>("FullName") ?? "";
        string folder = Path.GetFileName(root);
        bool conflict = full.Length > 0 && !full.Equals(ns + "-" + name, StringComparison.OrdinalIgnoreCase);
        // Descriptive/renamed folders are not competing package declarations.
        if (ThunderstoreUpdateService.TryParseSource(folder, out var folderNs, out var folderName)
            && folderName.Equals(name, StringComparison.OrdinalIgnoreCase))
            conflict |= !folderNs.Equals(ns, StringComparison.OrdinalIgnoreCase);
        if (conflict) { mod.IdentityConflict = "Manifest identity conflicts with its package directory or FullName."; return; }
        ManifestIdentities.Add(mod, new ManifestIdentity { Namespace = ns, Name = name });
    }
    internal static bool ExplicitManifestIdentity(OverviewMod mod, string ns, string name)
        => mod.IdentityConflict.Length == 0 && ManifestIdentities.TryGetValue(mod, out var evidence)
            && evidence.Namespace.Equals(ns, StringComparison.OrdinalIgnoreCase) && evidence.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
            && mod.PackageName.Equals(name, StringComparison.OrdinalIgnoreCase)
            && mod.AuthorSource == "manifest" && mod.Author.Equals(ns, StringComparison.OrdinalIgnoreCase);
    internal static string SearchName(OverviewMod mod) => mod.PackageName.Length > 0 ? mod.PackageName : mod.RawName;
    internal static bool KnownVersion(string ns, string name, string version)
    {
        return ShopCatalog.Cached().Any(p => p.Team.Equals(ns, StringComparison.OrdinalIgnoreCase)
            && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Versions.Any(v => v.Version == version));
    }
    internal static IEnumerable<string> Dependencies(string declaration)
    {
        string[] parts = declaration.Split('-'); if (parts.Length != 3) return Array.Empty<string>();
        var package = ShopCatalog.Cached().FirstOrDefault(p => p.Team.Equals(parts[0], StringComparison.OrdinalIgnoreCase) && p.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
        return package?.Versions.FirstOrDefault(v => v.Version == parts[2])?.Dependencies.ToArray() ?? Array.Empty<string>();
    }
    internal static StorePackage? Exact(IEnumerable<StorePackage> catalog, OverviewMod mod)
        => Index(catalog as List<StorePackage> ?? catalog.ToList()).Resolve(mod);
    internal static List<StorePackage> Candidates(IEnumerable<StorePackage> catalog, string query, OverviewMod? mod = null)
        => Index(catalog as List<StorePackage> ?? catalog.ToList()).Candidates(query, mod);
}
