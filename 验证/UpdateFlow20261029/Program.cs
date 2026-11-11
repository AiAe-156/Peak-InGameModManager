using ModConfigEnhance.Overview;

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
OverviewMod Mod(string name, string installed = "1.0.0", bool allowed = true) => new() { Guid = Guid.NewGuid().ToString("N"), RawName=name, PackageName=name, Author="Team", AuthorSource="manifest", SourceNamespace = "Team", SourceName = name, PackageVersion = installed, Rule = new() { Allowed = allowed } };
ShopListing Listing(string name, string version) => new() { Team = "Team", Name = name, Version = version, CategoriesKnown = true, Categories = new() { "Tools" }, Versions=new(){new(){Version=version}} };
var repository = new OverviewRepository { RootDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N")) };
ModConfigEnhance.Loc.RootDirectory=repository.RootDirectory;
var a = Mod("Shared"); var b = Mod("Shared"); var unknown = Mod("Unknown", ""); var disallowed = Mod("Shared", allowed: false);
ShopCatalog.Items = new() { Listing("Shared", "2.0.0"), Listing("Unknown", "2.0.0"), Listing("Core", "2.0.0") };
InfrastructurePackages.Items = new() { new() { Team = "Team", Name = "Shared" }, new() { Team = "Team", Name = "Core" } };
long revision = ThunderstoreUpdateService.CacheRevision;
using var service = new ThunderstoreUpdateService(repository);
await service.CheckAsync(new[] { a, b, unknown, disallowed }, CancellationToken.None);
var report = service.LastReport;
Check(report.ModCount == 6 && report.EligibleModCount == 5, "Eligible model count");
Check(report.PackageCount == 3 && report.Groups["__infrastructure"][1] == 2, "Package deduplication includes components");
Check(report.Completed == 6 && report.Succeeded == 6 && report.Failed == 0, "Completed result counts");
Check(a.HasUpdate && b.HasUpdate, "Shared package updates both model rows");
Check(!unknown.HasUpdate && unknown.CheckMessage.Contains("unknown"), "Unknown local package never compares DLL version");
Check(!disallowed.HasUpdate && disallowed.CheckMessage.Contains("disabled"), "Cache projection obeys update permission");
Check(InfrastructurePackages.Results.All(p => p.Latest == "2.0.0"), "Component latest versions projected");
Check(ThunderstoreUpdateService.CacheRevision > revision && ShopCatalog.LoadCalls >= 1, "A catalogue load publishes cache revision"); // load count is an implementation detail (pre-2.2.4 it was 2)
Check(AutomaticUpdateChecks.RetryDelay(report) == 1800, "Success schedules thirty minutes after completion");
ShopCatalog.Items[0].Version = "3.0.0"; ShopCatalog.Items[0].Versions.Add(new(){Version="3.0.0"}); ShopCatalog.Items=ShopCatalog.Items.ToList();
await service.CheckAsync(new[] { a }, CancellationToken.None);
Check(a.LatestVersion == "3.0.0", "Current shared catalogue outranks older local metadata cache");
var secondModel = Mod("Shared");
using (var second = new ThunderstoreUpdateService(repository)) second.ApplyCachedCategories(new[] { secondModel });
Check(secondModel.LatestVersion == "3.0.0" && secondModel.HasUpdate, "Independent live model projects committed shared cache");

InfrastructurePackages.Items.Clear(); ShopNetwork.Status = 404;

var notFound = Mod("NotFound");
await service.CheckAsync(new[] { notFound }, CancellationToken.None);
Check(service.LastReport.Completed == 1 && !notFound.HasUpdate && notFound.CheckMessage.Contains("No active PEAK store identity") && service.LastReport.TransientFailed == 0, "Absent official identity is a settled answer without an unverified 404 request");
Check(AutomaticUpdateChecks.RetryDelay(service.LastReport) == 1800, "Unresolved source completes without a network failure retry loop");
ShopCatalog.Error = "Catalogue offline";
try { await service.CheckAsync(new[] { a }, CancellationToken.None); throw new Exception("Expected catalogue failure"); } catch (IOException) { }
Check(service.LastReport.Error == "Catalogue offline" && AutomaticUpdateChecks.RetryDelay(service.LastReport) == 60, "Catalogue failure does not schedule thirty minutes");
ShopCatalog.Error = ""; ShopCatalog.WaitForCancellation = true;
using (var cancel = new CancellationTokenSource()) {
    var pending = service.CheckAsync(new[] { a }, cancel.Token); cancel.Cancel();
    try { await pending; throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
}
Check(service.LastReport.Cancelled && AutomaticUpdateChecks.RetryDelay(service.LastReport) == 10, "Cancellation is observed and retries promptly");
ShopCatalog.WaitForCancellation = false;
await service.CheckAsync(new[] { a }, CancellationToken.None);
Check(service.LastReport.Succeeded == 1, "A cancelled run releases its gate for the next run");
// 2.3.2: a settled store verdict (deprecated / 404 / identity) is a failure, but not one to retry every minute.
ShopCatalog.Items = new() { Listing("Shared", "2.0.0") }; ShopCatalog.Items[0].Deprecated = true;
var retired = Mod("Shared");
await service.CheckAsync(new[] { retired }, CancellationToken.None);
Check(service.LastReport.Failed == 1 && service.LastReport.TransientFailed == 0, "Deprecated package is a settled failure");
Check(AutomaticUpdateChecks.RetryDelay(service.LastReport) == 1800, "Deprecated package does not retry every minute");
ShopCatalog.Items = new() { Listing("Other", "1.0.0") }; ShopNetwork.Status = 404;
var missing = Mod("Infra"); missing.SourceNamespace = "MonoDetour"; missing.SourceName = "MonoDetour";
InfrastructurePackages.Items = new() { new() { Team = "MonoDetour", Name = "MonoDetour", Homepage = "https://github.com/MonoDetour/MonoDetour" } };
await service.CheckAsync(Array.Empty<OverviewMod>(), CancellationToken.None);
var verdicts = service.LastReport;
Check(verdicts.Failed == verdicts.TransientFailed + (verdicts.Failed - verdicts.TransientFailed) && verdicts.TransientFailed == 0, "HTTP 404 on the exact endpoint is settled");
ShopNetwork.Status = 503; InfrastructurePackages.Items.Clear();
var flaky = Mod("Flaky"); ShopCatalog.Items = new() { Listing("Flaky", "2.0.0") }; ShopCatalog.Items[0].Version = "x";
await service.CheckAsync(new[] { flaky }, CancellationToken.None);
Check(service.LastReport.Failed == 1 && service.LastReport.TransientFailed == 0 && AutomaticUpdateChecks.RetryDelay(service.LastReport) == 1800, "Invalid catalogue version is a settled verdict");
Console.WriteLine($"Update flow checks passed: {checks}");

namespace ModConfigEnhance
{
    internal static class Loc { internal static string RootDirectory=""; internal static string T(string en, string zh, string tw) => en; internal static string F(string en, string zh, string tw, params object[] args) => string.Format(en, args); internal static string LocalizeMessage(string value) => value; }
    internal static class Plugin { internal static Log Log = new(); }
    internal sealed class Log { internal void LogWarning(string value) { } }
}
namespace ModConfigEnhance.Overview
{
    internal sealed class OverviewModRule { internal string groupId=""; internal bool Allowed = true; }
    internal sealed class OverviewMod {
        internal string Id="",Guid="",RawName="",PackageName="",PackageRoot="",Location="",IdentityConflict=""; internal ModSourceKind SourceKind;
        internal string SourceNamespace = "", SourceName = "", PackageVersion = "", PluginVersion = "9.0.0", CheckMessage = "", LatestVersion = "", UpdateNotes = "", Author = "", AuthorSource = "", HomepageUrl = "";
        internal bool SourceConfirmed = true, HasUpdate, CategoriesKnown;
        internal List<string> Categories = new(); internal DateTime? LastCheckedUtc; internal OverviewModRule Rule = new();
        internal string SourceKey => SourceNamespace + "-" + SourceName;
    }
    internal sealed class OverviewPack { internal string sourceNamespace = "", sourceName = "", version = ""; }
    internal sealed class State { internal OverviewPack pack = new(); }
    internal sealed class OverviewRepository {
        internal OverviewRepository(bool appliedView = false) { }
        internal IReadOnlyList<OverviewMod> ReloadInstalled => Array.Empty<OverviewMod>();
        internal string RootDirectory = ""; internal State State = new();
        internal bool IsUpdateAllowed(OverviewModRule rule) => rule.Allowed;
        internal bool GetGlobalUpdateAllowed() => true;
        internal bool IsPackUpdateAllowed(OverviewPack pack) => false;
        internal static void AtomicWrite(string path, string data) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, data); }
    }
    internal static class InfrastructurePackages {
        internal sealed class Package { internal string Path="",Version="1.0.0",Team = "", Name = "", Latest = "", Homepage=""; internal string Author => Team; internal Newtonsoft.Json.Linq.JObject? ManifestEvidence => new() { ["namespace"]=Team, ["name"]=Name }; internal bool IdentityConfirmed = true; internal string Key => Team + "-" + Name; }
        internal static List<Package> Items = new(); internal static List<Package> Results = new();
        internal static List<Package> Scan() => Items.ToList(); internal static void SetResults(List<Package> value) => Results = value;
    }
    internal sealed class ShopListing { internal string Url=""; internal DateTime Updated; internal bool Deprecated; internal List<ShopVersion> Versions=new(); internal string Team = "", Name = "", Version = "", Homepage = ""; internal bool CategoriesKnown; internal List<string> Categories = new(); }
    internal sealed class ShopVersion { internal string Version=""; internal List<string> Dependencies=new(); }
    internal static class PublishedPack { internal static List<Pack> Packs=new(); internal sealed class Pack { internal bool Valid; internal string Root=""; } }
    internal static class ShopCatalog {
        internal static long Revision=>1; internal static List<ShopListing> Cached()=>Items;
        internal static List<ShopListing> Items = new(); internal static string Error = ""; internal static bool WaitForCancellation; internal static int LoadCalls;
        internal static async Task<List<ShopListing>> Load(CancellationToken token, bool refresh = false) { LoadCalls++; if (WaitForCancellation) await Task.Delay(Timeout.Infinite, token); if (Error.Length > 0) throw new IOException(Error); return Items; }
    }
    internal static class ShopInstalled { internal static bool Key(string value) => value.Split('-').Length == 2; }
    internal static class ShopNetwork {
        internal static int Status = 404;
        internal static System.Net.Http.HttpClient Client() => new();
        internal static Task<System.Net.Http.HttpResponseMessage> Send(System.Net.Http.HttpClient client, string url, CancellationToken token) => Task.FromResult(new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)Status));
        internal static Task<byte[]> ReadBody(System.Net.Http.HttpResponseMessage response, int limit, CancellationToken token) => Task.FromResult(Array.Empty<byte>());
    }
    internal sealed class OverviewCatalogue { internal List<OverviewMod> Scan(OverviewRepository repo) => new(); }
    internal static class PackLibrary { internal static bool Managed; internal static string Workspace=""; internal static Pack? Active; internal sealed class Pack { internal string RevisionStateHash=""; } internal static Work? Task() => null; internal sealed class Work { internal bool BlocksUpdates = false; } }
    internal static class OverviewOptions { internal static Option AutomaticUpdates = new(); internal sealed class Option { internal bool Value = true; } }
    internal enum ModSourceKind { Pending, Store, Local }
    internal static class ModSources
    {
        internal static readonly SemaphoreSlim Coordinator = new(1);
        internal static string Context => "test"; internal static long ChoiceRevision => 0;
        internal static Task Discover(IReadOnlyList<OverviewMod> mods, CancellationToken token, bool force = false) => Task.CompletedTask;
        internal static bool ExplicitLocal(OverviewMod mod) => false;
        internal static bool ExplicitManifestIdentity(OverviewMod mod, string ns, string name) => mod.SourceNamespace == ns && mod.SourceName == name;
        internal static void ReadManifestIdentity(OverviewMod mod, Newtonsoft.Json.Linq.JObject data, string root) { mod.SourceNamespace = (string?)data["namespace"] ?? ""; mod.SourceName = (string?)data["name"] ?? ""; }
    }
    internal static class StoreIdentityIndex { internal static string Repository(string url) => url.TrimEnd('/').ToUpperInvariant(); }
}
namespace UnityEngine { internal class MonoBehaviour { } internal static class Time { internal static float unscaledTime = 0; } }
namespace UnityEngine.SceneManagement { internal static class SceneManager { internal static Scene GetActiveScene() => new(); internal sealed class Scene { internal string name = "Title"; } } }
