#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace ModConfigEnhance.Overview;
// Counts are plugin installations (GUID + package root + DLL), not catalog cache entries.
internal sealed class SourceIdentificationBatch
{
    internal static string Key(OverviewMod m) => m.Guid + "|" + m.PackageRoot + "|" + m.Location;
    internal sealed class Row
    {
        internal readonly OverviewMod Mod;
        internal readonly string Key, Name, Original, Version, Author, Namespace, Package, Reason;
        internal readonly ModSourceKind Kind;
        internal readonly bool NewlyIdentified, Deprecated;
        internal Row(OverviewMod m, bool previouslyConfirmed, IReadOnlyList<StorePackage> catalog)
        {
            Mod = m; Key = SourceIdentificationBatch.Key(m); Name = OverviewLanguageBridge.GetDisplayName(m);
            Original = m.RawName.Length > 0 ? m.RawName : m.Name;
            Version = m.PackageVersion.Length > 0 ? m.PackageVersion : m.PluginVersion; Author = m.Author;
            Kind = m.SourceConfirmed ? ModSourceKind.Store : m.SourceKind == ModSourceKind.Local ? ModSourceKind.Local : ModSourceKind.Pending;
            Namespace = m.SourceNamespace; Package = m.SourceName;
            Deprecated = catalog.Any(p => p.Author.Equals(Namespace, StringComparison.OrdinalIgnoreCase) && p.Name.Equals(Package, StringComparison.OrdinalIgnoreCase) && p.Deprecated);
            NewlyIdentified = Kind == ModSourceKind.Store && !previouslyConfirmed;
            Reason = Kind == ModSourceKind.Pending ? ModSources.UnresolvedReason(m, catalog) : "";
        }
    }
    internal readonly IReadOnlyList<Row> Rows;
    internal readonly string Environment;
    internal readonly IReadOnlyList<StorePackage> Catalog;
    private readonly HashSet<string> _before;
    internal int Identified => Rows.Count(r => r.Kind == ModSourceKind.Store);
    internal int NewlyIdentified => Rows.Count(r => r.NewlyIdentified);
    internal int Pending => Rows.Count(r => r.Kind == ModSourceKind.Pending);
    internal int Local => Rows.Count(r => r.Kind == ModSourceKind.Local);
    internal string Summary => Loc.F("Sources identified: {0} (new this run: {1}) | Need manual review: {2} | Local: {3}",
        "成功识别来源：{0}（本轮新增：{1}）｜需手动核对：{2}｜个人本地：{3}",
        "成功識別來源：{0}（本輪新增：{1}）｜需手動核對：{2}｜個人本機：{3}", Identified, NewlyIdentified, Pending, Local);
    internal static HashSet<string> CaptureBefore(IEnumerable<OverviewMod> mods) => new(mods.Where(m => m.SourceConfirmed).Select(Key), StringComparer.OrdinalIgnoreCase);
    internal SourceIdentificationBatch(IEnumerable<OverviewMod> mods, HashSet<string> before, IReadOnlyList<StorePackage> catalog, string environment)
    {
        _before = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase); Catalog = catalog; Environment = environment;
        Rows = Array.AsReadOnly(mods.GroupBy(Key, StringComparer.OrdinalIgnoreCase).Select(g => new Row(g.First(), _before.Contains(g.Key), catalog)).ToArray());
    }
    // Explicit user actions produce a new result. Reading a result never reruns matching.
    internal SourceIdentificationBatch AfterManualAction()
    {
        var mods = Rows.Select(r => r.Mod).ToArray();
        foreach (var m in mods) ModSources.Apply(m, Catalog);
        return new SourceIdentificationBatch(mods, _before, Catalog, Environment);
    }
}
