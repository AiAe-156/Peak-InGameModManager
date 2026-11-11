// Independent review stubs. Deliberately separate from 验证/SourceUpdateChecks/Stubs.cs so the
// existing 55 production checks keep their own fixtures untouched.
using System.Net.Http;
namespace ModConfigEnhance.Overview;
internal sealed class OverviewModRule { public bool updateAllowed = true; public string groupId = ""; }
internal sealed class OverviewPack { public string sourceNamespace = "", sourceName = "", version = ""; }
internal sealed class OverviewMod
{
    public string Id = "", Guid = "", Name = "", RawName = "", PackageName = "", PackageRoot = "", Location = "",
        PackageVersion = "", PluginVersion = "", SourceNamespace = "", SourceName = "", IdentityConflict = "",
        Author = "", AuthorSource = "", HomepageUrl = "", CheckMessage = "", LatestVersion = "", UpdateNotes = "";
    public bool SourceConfirmed, HasUpdate, CategoriesKnown;
    public ModSourceKind SourceKind;
    public DateTime? LastCheckedUtc;
    public List<string> Categories = new(), PluginGuids = new();
    public OverviewModRule Rule = new();
    public string SourceKey => SourceNamespace + "-" + SourceName;
}
internal static class Loc
{
    public static string RootDirectory = "";
    public static string T(string a, string b, string c) => a;
    public static string F(string a, string b, string c, params object[] args) => string.Format(a, args);
    public static string LocalizeMessage(string s) => s;
}
internal static class PackLibrary
{
    public sealed class Pack { public string RevisionStateHash = ""; }
    public static Pack? Active;
    public static bool Managed;
    public static string Workspace = "";
}
internal static class PublishedPack
{
    public sealed class Pack { public bool Valid = true; public string Root = ""; }
    public static List<Pack> Packs = new();
}
internal static class Plugin
{
    internal static Logger Log = new();
    internal class Logger { public void LogWarning(string s) { } }
}
internal sealed class OverviewRepository
{
    public string RootDirectory => Loc.RootDirectory;
    public bool IsUpdateAllowed(OverviewModRule r) => r.updateAllowed;
    public bool IsPackUpdateAllowed(OverviewPack p) => true;
    public bool GetGlobalUpdateAllowed() => true;
    public static void AtomicWrite(string p, string s) { Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, s); }
}
internal sealed class ShopListing
{
    public string Name = "", Team = "", Version = "", Homepage = "", Url = "";
    public DateTime Updated;
    public bool Deprecated, CategoriesKnown;
    public List<string> Categories = new();
    public List<ShopVersion> Versions = new();
}
internal sealed class ShopVersion { public string Version = ""; public List<string> Dependencies = new(); }
internal static class ShopCatalog
{
    public static List<ShopListing> Items = new();
    public static int Loads;
    // Review stub: revision is mutable so negative-cache invalidation can be exercised.
    public static long CurrentRevision = 1;
    public static long Revision => CurrentRevision;
    public static Exception? Error;
    public static Action? Loading;
    public static List<ShopListing> Cached() => Items;
    public static Task<List<ShopListing>> Load(CancellationToken t, bool f = false)
    {
        t.ThrowIfCancellationRequested(); Loads++; Loading?.Invoke();
        if (Error != null) throw Error;
        return Task.FromResult(Items);
    }
}
internal static class InfrastructurePackages
{
    internal sealed class Package
    {
        internal string Team = "", Name = "", Version = "", Path = "", Latest = "", Author = "", Homepage = "";
        internal Newtonsoft.Json.Linq.JObject? ManifestEvidence;
        internal bool IdentityConfirmed = true;
        internal string Key => Team + "-" + Name;
    }
    internal static List<Package> Items = new();
    internal static List<Package> Scan() { foreach(var p in Items) { p.Author=p.Team; p.ManifestEvidence ??=new Newtonsoft.Json.Linq.JObject{["namespace"]=p.Team,["name"]=p.Name}; } return Items; }
    internal static void SetResults(List<Package> p) { }
}
internal static class ShopNetwork
{
    public static int Requests;
    public static HttpClient Client() => new();
    public static Task<HttpResponseMessage> Send(HttpClient c, string u, CancellationToken t) { Requests++; throw new IOException("network disabled in review fixtures"); }
    public static Task<byte[]> ReadBody(HttpResponseMessage r, int l, CancellationToken t) => Task.FromResult(Array.Empty<byte>());
}
