#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
namespace ModConfigEnhance.Overview;
internal static partial class ModSources
{
    private static StoreIdentityIndex? _index;
    private static long _choiceRevision;
    internal static long ChoiceRevision => Interlocked.Read(ref _choiceRevision);
    internal static readonly SemaphoreSlim Coordinator = new(1);
    private static readonly SemaphoreSlim DiscoveryGate = new(1);
    private static readonly Dictionary<string, ModSourceRecord> Derived = new(StringComparer.OrdinalIgnoreCase);
    private static string _derivedContext = "";
    private static readonly Dictionary<string, (long Catalog, long Choice, DateTime Until)> Misses = new();
    internal static string Context => Loc.RootDirectory + "|" + (PackLibrary.Managed ? PackLibrary.Workspace + "|" + (PackLibrary.Active?.RevisionStateHash ?? "") : "unmanaged");
    // Keep unmodified local metadata separate from display/source overlays. Never promote cached authors to user evidence.
    private sealed class LocalEvidence
    {
        internal readonly string Author, AuthorSource, Homepage, Namespace, Name, Version;
        internal LocalEvidence(OverviewMod m) { Author = m.Author; AuthorSource = m.AuthorSource; Homepage = m.HomepageUrl; Namespace = m.SourceNamespace; Name = m.SourceName; Version = m.PackageVersion; }
        internal void Restore(OverviewMod m) { m.Author = Author; m.AuthorSource = AuthorSource; m.HomepageUrl = Homepage; m.SourceNamespace = Namespace; m.SourceName = Name; m.PackageVersion = Version; }
    }
    private static readonly ConditionalWeakTable<OverviewMod, LocalEvidence> Evidence = new();
    private static LocalEvidence Original(OverviewMod mod) => Evidence.GetValue(mod, m => new LocalEvidence(m));
    private static string Instance(OverviewMod mod)
    {
        var e = Original(mod);
        string manifest = "";
        try {
            string directory = Path.GetDirectoryName(mod.Location) ?? mod.PackageRoot;
            while (!string.IsNullOrEmpty(directory)) {
                string path = Path.Combine(directory, "manifest.json");
                if (!File.Exists(path)) path += ".old";
                if (File.Exists(path)) { manifest = path + "|" + Stamp(path) + "|" + File.ReadAllText(path); break; }
                if (directory.Equals(mod.PackageRoot, StringComparison.OrdinalIgnoreCase)) break;
                directory = Path.GetDirectoryName(directory) ?? "";
            }
        } catch { manifest = "unreadable"; }
        string value = string.Join("|", Context, PackLibrary.Managed ? PackLibrary.Active?.RevisionStateHash ?? "" : "", mod.Guid,
            mod.PackageRoot, mod.Location, e.Version, mod.PluginVersion, Stamp(mod.Location), manifest,
            e.Author, e.AuthorSource, e.Homepage, mod.PackageName, mod.RawName, e.Namespace, e.Name);
        using var hash = SHA256.Create();
        return Context + "|" + Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
    private static OverviewMod ExportProjection(OverviewMod mod)
    {
        var e = Original(mod);
        var copy = new OverviewMod { Id = mod.Id, Guid = mod.Guid, Name = mod.Name, RawName = mod.RawName,
            PackageName = mod.PackageName, PackageRoot = mod.PackageRoot, Location = mod.Location, PluginVersion = mod.PluginVersion,
            PackageVersion = e.Version, SourceNamespace = e.Namespace, SourceName = e.Name,
            Author = e.Author, AuthorSource = e.AuthorSource, HomepageUrl = e.Homepage, IdentityConflict = mod.IdentityConflict };
        if (ManifestIdentities.TryGetValue(mod, out var manifest)) ManifestIdentities.Add(copy, manifest);
        return copy;
    }
    internal static string InstallationToken(OverviewMod mod) => Instance(mod);
    private static bool PersonalScope(ModSourceRecord record, OverviewMod mod) =>
        (record.environment.Length == 0 || record.environment == Context)
        && (record.installation.Length == 0 || record.installation.Equals(mod.PackageRoot, StringComparison.OrdinalIgnoreCase));
    internal static bool UserChoice(OverviewMod mod, string ns, string name)
    {
        lock (Gate) return Records.TryGetValue(mod.Guid, out var r) && r.note == "user" && PersonalScope(r, mod)
            && r.ns.Equals(ns, StringComparison.OrdinalIgnoreCase) && r.name.Equals(name, StringComparison.OrdinalIgnoreCase);
    }
    private static string Stamp(string path) { try { var f = new FileInfo(path); return f.Exists ? f.Length + ":" + f.LastWriteTimeUtc.Ticks : ""; } catch { return ""; } }
    private static void ReadDerived()
    {
        string context = Context; if (_derivedContext == context) return;
        Derived.Clear(); _derivedContext = context;
        try { var saved = JsonConvert.DeserializeObject<Dictionary<string, ModSourceRecord>>(File.ReadAllText(Path.Combine(Loc.RuntimeDirectory, "source-identities.json")));
            if (saved != null) foreach (var p in saved.Where(p => p.Key.StartsWith(context + "|", StringComparison.Ordinal))) Derived[p.Key] = p.Value; } catch { }
    }
    private static void SaveDerived() => OverviewRepository.AtomicWrite(Path.Combine(Loc.RuntimeDirectory, "source-identities.json"),
        JsonConvert.SerializeObject(Derived.Take(2048).ToDictionary(p => p.Key, p => p.Value), Formatting.Indented));
    private static bool Applies(ModSourceRecord record, OverviewMod mod) => record.instance.Length > 0
        ? record.environment == Context && record.instance == Instance(mod)
        : record.note != "user" && (record.environment.Length == 0 || record.environment == Context)
          && (record.installation.Length == 0 || record.installation.Equals(mod.PackageRoot, StringComparison.OrdinalIgnoreCase));
    internal static bool ExplicitLocal(OverviewMod mod)
    {
        // A completed store installation is direct provenance, even with a stale local override.
        if (HasInstalledStore(mod)) return false;
        // Personal sources.json is a user-choice store, including pre-fingerprint versions.
        // Published sources.json is distribution metadata and never grants a personal opt-out.
        lock (Gate) return Records.TryGetValue(mod.Guid, out var record) && record.kind == ModSourceKind.Local
            && record.note != "auto"
            && PersonalScope(record, mod);
    }
    internal static bool Verified(string ns, string name) => Index(Project(ShopCatalog.Cached())).Identity(ns, name) != null;
    private static StoreIdentityIndex Index(List<StorePackage> catalog) { lock (Gate) return ReferenceEquals(catalog, _catalog) ? _index ??= new(catalog) : new(catalog); }
    internal static async Task Discover(IReadOnlyList<OverviewMod> mods, CancellationToken token, bool force = false, IReadOnlyList<StorePackage>? catalogSnapshot = null)
    {
        string context = Context; long revision = ChoiceRevision;
        var instances = mods.Select(m => (Mod: m, Token: Instance(m))).ToArray();
        void ValidInstances() { if (instances.Any(p => p.Token != Instance(p.Mod))) throw new OperationCanceledException("Source installation changed.", token); }
        await DiscoveryGate.WaitAsync(token).ConfigureAwait(false);
        try {
            var catalog = catalogSnapshot ?? await Catalog(token, force).ConfigureAwait(false); ValidInstances(); var index = Index(catalog as List<StorePackage> ?? catalog.ToList());
            var updates = new Dictionary<string, ModSourceRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in mods) {
                token.ThrowIfCancellationRequested();
                if (context != Context || revision != ChoiceRevision) throw new OperationCanceledException("Source context changed.", token);
                Apply(mod, catalog);
                if (TryInstalledStore(mod)) continue;
                if (ExplicitLocal(mod)) { mod.SourceConfirmed = false; mod.SourceKind = ModSourceKind.Local; continue; }
                string instance = Instance(mod);
                lock (Gate) if (!force && !mod.SourceConfirmed && Misses.TryGetValue(instance, out var miss)
                    && miss.Catalog == ShopCatalog.Revision && miss.Choice == revision && miss.Until > DateTime.UtcNow) continue;
                var match = index.Resolve(mod);
                if (match == null) {
                    mod.SourceConfirmed = false; mod.SourceKind = ModSourceKind.Pending;
                    lock (Gate) { if (Misses.Count > 2048) Misses.Clear(); Misses[instance] = (ShopCatalog.Revision, revision, DateTime.UtcNow.AddMinutes(5)); }
                    continue;
                }
                mod.SourceNamespace = match.Author; mod.SourceName = match.Name; mod.SourceConfirmed = true; mod.SourceKind = ModSourceKind.Store;
                if (UserChoice(mod, match.Author, match.Name)) continue;
                updates[Instance(mod)] = new ModSourceRecord { kind = ModSourceKind.Store, ns = match.Author, name = match.Name, homepage = mod.HomepageUrl, packageVersion = mod.PackageVersion };
            }
            lock (Gate) {
                token.ThrowIfCancellationRequested();
                if (context != Context || revision != ChoiceRevision) throw new OperationCanceledException("Source context changed.", token);
                ValidInstances(); ReadDerived(); foreach (var pair in updates) Derived[pair.Key] = pair.Value;
                // Derived, replaceable data only; never modify signed sources or user choices.
                SaveDerived();
            }
        } finally { DiscoveryGate.Release(); }
    }
}
