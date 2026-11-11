#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace ModConfigEnhance.Overview;
// One immutable index per official PEAK catalogue snapshot. Automatic matches require unique high-confidence evidence.
internal sealed class StoreIdentityIndex
{
    private readonly Dictionary<string, List<StorePackage>> _identities;
    private readonly Dictionary<string, List<StorePackage>> _names;
    private readonly Dictionary<string, int> _repositoryCounts;
    private readonly (StorePackage Package, string Name, string[] Tokens)[] _search;
    internal StoreIdentityIndex(IEnumerable<StorePackage> packages)
    {
        var all = packages.Where(p => Regex.IsMatch(p.Version, @"^\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.-]+)?$") && p.Versions.Contains(p.Version)).ToArray();
        _identities = all.GroupBy(p => p.Author + "-" + p.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        _names = all.GroupBy(p => Normalize(p.Name)).ToDictionary(g => g.Key, g => g.ToList());
        _search = all.Select(p => (p, Normalize(p.Name), Tokens(p.Name))).ToArray();
        _repositoryCounts = all.Where(p => Repository(p.Homepage).Length > 0).GroupBy(p => Repository(p.Homepage)).ToDictionary(g => g.Key, g => g.Count());
    }
    internal StorePackage? Identity(string ns, string name) => _identities.TryGetValue(ns + "-" + name, out var items) && items.Count == 1 ? items[0] : null;
    internal StorePackage? Resolve(OverviewMod mod)
    {
        if (mod.IdentityConflict.Length > 0) return null;
        var explicitIdentity = Identity(mod.SourceNamespace, mod.SourceName);
        if (mod.SourceNamespace.Length > 0 || mod.SourceName.Length > 0)
        {
            // An absent/invalid explicit identity is not silently replaced with a similar name.
            if (explicitIdentity == null) return null;
            if (ModSources.UserChoice(mod, explicitIdentity.Author, explicitIdentity.Name)) return explicitIdentity;
            if (ThunderstoreUpdateService.TryParseSource(mod.HomepageUrl, out var ns, out var name)
                && !(ns.Equals(explicitIdentity.Author, StringComparison.OrdinalIgnoreCase) && name.Equals(explicitIdentity.Name, StringComparison.OrdinalIgnoreCase))) return null;
            // Revalidate an automatically cached fuzzy match by the same confidence rules.
            if (Normalize(ModSources.SearchName(mod)) != Normalize(explicitIdentity.Name))
                return ReferenceEquals(FuzzyResolve(mod), explicitIdentity) ? explicitIdentity : null;
            string repository = Repository(mod.HomepageUrl);
            bool authorEvidence = TrustedAuthor(mod) && Normalize(mod.Author) == Normalize(explicitIdentity.Author);
            bool repositoryEvidence = repository.Length > 0 && repository == Repository(explicitIdentity.Homepage);
            if (TrustedAuthor(mod) && Normalize(mod.Author) != Normalize(explicitIdentity.Author)) return null;
            if (repository.Length > 0 && Repository(explicitIdentity.Homepage).Length > 0 && repository != Repository(explicitIdentity.Homepage)) return null;
            return (authorEvidence || repositoryEvidence || UniqueExactName(mod, explicitIdentity)) && (!RepositoryBranches(explicitIdentity)
                || ModSources.ExplicitManifestIdentity(mod, explicitIdentity.Author, explicitIdentity.Name)) ? explicitIdentity : null;
        }
        string localName = Normalize(ModSources.SearchName(mod));
        if (!_names.TryGetValue(localName, out var sameName)) return FuzzyResolve(mod);
        string repo = Repository(mod.HomepageUrl);
        var strong = sameName.Where(p => (TrustedAuthor(mod) && Normalize(p.Author) == Normalize(mod.Author))
            || repo.Length > 0 && Repository(p.Homepage) == repo).ToArray();
        // Even strong evidence must not hide an author/repository conflict.
        strong = strong.Where(p => !(TrustedAuthor(mod) && Normalize(p.Author) != Normalize(mod.Author))
            && !(repo.Length > 0 && Repository(p.Homepage).Length > 0 && Repository(p.Homepage) != repo)).ToArray();
        if (strong.Length == 1 && !RepositoryBranches(strong[0])) return strong[0];
        return sameName.Count == 1 && UniqueExactName(mod, sameName[0]) && !RepositoryBranches(sameName[0]) ? sameName[0] : null;
    }
    private bool UniqueExactName(OverviewMod mod, StorePackage package)
    {
        string name = Normalize(ModSources.SearchName(mod));
        if (name.Length < 5 || !_names.TryGetValue(name, out var matches) || matches.Count != 1) return false;
        if (TrustedAuthor(mod) && Normalize(mod.Author) != Normalize(package.Author)) return false;
        string localRepo = Repository(mod.HomepageUrl), storeRepo = Repository(package.Homepage);
        return localRepo.Length == 0 || storeRepo.Length == 0 || localRepo == storeRepo;
    }
    private StorePackage? FuzzyResolve(OverviewMod mod)
    {
        // Never search translated display names or GUIDs for a fuzzy automatic decision.
        string[] names = new[] { mod.PackageName, mod.RawName }.Select(Normalize).Where(n => n.Length >= 6).Distinct().ToArray();
        if (names.Length == 0 || mod.Author.Length == 0) return null;
        string repo = Repository(mod.HomepageUrl);
        var ranked = _search.Where(p => !p.Package.Deprecated && Normalize(p.Package.Author) == Normalize(mod.Author)
            && !RepositoryBranches(p.Package) && !(repo.Length > 0 && Repository(p.Package.Homepage).Length > 0 && repo != Repository(p.Package.Homepage)))
            .Select(p => (p.Package, Score: names.Max(n => 1d - (double)Distance(n, p.Name) / Math.Max(n.Length, p.Name.Length))))
            .OrderByDescending(p => p.Score).ToArray();
        return ranked.Length > 0 && ranked[0].Score >= .90 && (ranked.Length == 1 || ranked[0].Score - ranked[1].Score >= .08)
            ? ranked[0].Package : null;
    }
    private static bool TrustedAuthor(OverviewMod mod) => mod.Author.Length > 0 && mod.AuthorSource == "manifest";
    private bool RepositoryBranches(StorePackage package)
    {
        string repo = Repository(package.Homepage);
        return repo.Length > 0 && _repositoryCounts.TryGetValue(repo, out int count) && count > 1;
    }
    internal string Reason(StorePackage package, OverviewMod mod)
    {
        var reasons = new List<string>();
        reasons.Add(Normalize(ModSources.SearchName(mod)) == Normalize(package.Name)
            ? Loc.T("Exact original name", "原名精确匹配", "原名精確匹配") : Loc.T("Similar name only", "仅名称相似", "僅名稱相似"));
        if (TrustedAuthor(mod)) reasons.Add(Normalize(mod.Author) == Normalize(package.Author)
            ? Loc.T("Declared author agrees", "声明作者一致", "宣告作者一致") : Loc.T("Declared author differs", "声明作者不同", "宣告作者不同"));
        string repo = Repository(mod.HomepageUrl), target = Repository(package.Homepage);
        if (repo.Length > 0 && target.Length > 0) reasons.Add(repo == target
            ? Loc.T("Same full repository", "完整仓库一致", "完整倉庫一致") : Loc.T("Repository differs", "仓库不同", "倉庫不同"));
        if (RepositoryBranches(package) && !ModSources.ExplicitManifestIdentity(mod, package.Author, package.Name)) reasons.Add(Loc.T("Repository has multiple branches; choose explicitly", "同仓库存在多个分支，须手选", "同倉庫存在多個分支，須手選"));
        reasons.Add(Loc.T("Store team: ", "商店团队：", "商店團隊：") + package.Author);
        return string.Join(" · ", reasons);
    }
    internal List<StorePackage> Candidates(string query, OverviewMod? mod = null)
    {
        string term = Normalize(query); if (term.Length == 0) return new();
        var tokens = Tokens(query); string repo = Repository(mod?.HomepageUrl ?? "");
        return _search.Select(p => {
            int distance = Distance(term, p.Name);
            double similarity = 1.0 - (double)distance / Math.Max(term.Length, p.Name.Length);
            double score = p.Name == term ? 1000 : similarity * 100 + tokens.Count(t => p.Tokens.Contains(t)) * 25;
            if (mod != null && Normalize(mod.Author) == Normalize(p.Package.Author)) score += 20;
            if (repo.Length > 0 && Repository(p.Package.Homepage) == repo) score += 40;
            if (mod != null && mod.PackageVersion.Length > 0 && p.Package.Versions.Contains(mod.PackageVersion)) score += 5;
            return (p.Package, Score: score, Relevant: p.Name == term || similarity >= .45 || tokens.Intersect(p.Tokens).Any() || Normalize(p.Package.Author).Contains(term));
        }).Where(p => p.Relevant).OrderByDescending(p => p.Score).ThenBy(p => p.Package.Author, StringComparer.OrdinalIgnoreCase)
          .ThenBy(p => p.Package.Name, StringComparer.OrdinalIgnoreCase).Take(100).Select(p => p.Package).ToList();
    }
    internal static string Normalize(string value) => Regex.Replace(value ?? "", @"[\s_-]", "").ToUpperInvariant();
    private static string[] Tokens(string value) => Regex.Matches(Regex.Replace(value, @"([a-z0-9])([A-Z])", "$1 $2"), @"[\p{L}\p{N}]+").Cast<Match>().Select(m => m.Value.ToUpperInvariant()).Distinct().ToArray();
    internal static string Repository(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !(uri.Scheme == "https" || uri.Scheme == "http")) return "";
        if (!(uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("codeberg.org", StringComparison.OrdinalIgnoreCase))) return "";
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) return "";
        return (uri.Host + "/" + parts[0] + "/" + Regex.Replace(parts[1], @"\.git$", "", RegexOptions.IgnoreCase)).ToUpperInvariant();
    }
    private static int Distance(string a, string b)
    {
        // Bounded input prevents malformed metadata from making candidate ranking unbounded.
        if (a.Length > 256 || b.Length > 256) return Math.Max(a.Length, b.Length);
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++) { int previous = row[0]; row[0] = i;
            for (int j = 1; j <= b.Length; j++) { int old = row[j]; row[j] = Math.Min(Math.Min(row[j] + 1, row[j-1] + 1), previous + (a[i-1] == b[j-1] ? 0 : 1)); previous = old; } }
        return row[b.Length];
    }
}
