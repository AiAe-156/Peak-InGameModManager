using System.Net;
using System.Reflection;
using ModConfigEnhance;
using ModConfigEnhance.Overview;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine($"PASS {++passed} {name}");}
Loc.RootDirectory=Path.Combine(AppContext.BaseDirectory,"fixture_"+Guid.NewGuid().ToString("N"));
string runtime=Path.Combine(Loc.RootDirectory,"runtime");Directory.CreateDirectory(runtime);
string cache=Path.Combine(runtime,"shop-catalog.json"),legacy=Path.Combine(runtime,"store-catalog.json");
File.WriteAllText(legacy,JsonConvert.SerializeObject(new[]{new StorePackage {Name="Stale",Author="Old",Version="9.0.0",Versions=new(){"9.0.0"}}}));
File.WriteAllText(cache,new JArray(new JObject { ["Name"]="Legacy",["Team"]="Team",["Version"]="1.0.0",
    ["Versions"]=new JArray(new JObject {["Version"]="1.0.0",["Dependencies"]=new JArray()})}).ToString());
Check(ShopCatalog.Cached().Single().Key=="Team-Legacy","legacy shop cache is readable before a network refresh");
Check(!ShopCatalog.Fresh&&ShopCatalog.Lifetime==TimeSpan.FromMinutes(30),"incomplete old metadata expires into the shared 30-minute policy");
Check(ModSources.KnownVersion("Team","Legacy","1.0.0")&&!ModSources.KnownVersion("Old","Stale","9.0.0"),"version checks use shop snapshot and ignore obsolete source cache");
long oldRevision=ShopCatalog.Revision;

FakeApi.Block=new(TaskCreationOptions.RunContinuationsAsynchronously);
var shop=ShopCatalog.Load(CancellationToken.None,true);
var sources=ModSources.Catalog(CancellationToken.None,true);
using var canceled=new CancellationTokenSource();
var leaving=ModSources.Catalog(canceled.Token,true);
canceled.Cancel();
bool canceledOnly=false;try{await leaving;}catch(OperationCanceledException){canceledOnly=true;}
Check(canceledOnly&&FakeApi.IndexRequests==1,"parallel store/source refreshes share one request while a leaving caller cancels independently");
FakeApi.Block.SetResult(true);
var listings=await shop;var projected=await sources;
Check(FakeApi.Requests==3&&listings.Count==3&&projected.Count==3,"one index and two pages supply both complete catalog views");
Check(ShopCatalog.Fresh&&ShopCatalog.Revision==oldRevision+1&&ShopCatalog.LastRefreshError=="","successful shared refresh advances its revision once");
Check(ReferenceEquals(listings,ShopCatalog.Cached())&&ReferenceEquals(projected,await ModSources.Catalog(CancellationToken.None)),"repeat reads reuse the same snapshot and compatibility projection");
var alpha=listings.Single(p=>p.Name=="Alpha");var alphaSource=projected.Single(p=>p.Name=="Alpha");
Check(alpha.Version=="2.0.0"&&alpha.Versions.Select(v=>v.Version).SequenceEqual(new[]{"2.0.0","1.0.0"}),"latest version is semantic and all active versions are retained");
Check(alphaSource.Versions.SequenceEqual(new[]{"2.0.0","1.0.0"})&&alphaSource.Dependencies["1.0.0"].SequenceEqual(new[]{"Team-Base-1.0.0"}),"source projection keeps every version and its dependencies");
Check(alpha.Homepage=="https://example.com/alpha2"&&alphaSource.Homepage==alpha.Homepage&&alphaSource.Url=="https://thunderstore.io/c/peak/p/Team/Alpha/","homepage and public package URL survive projection");
Check(alpha.Versions[0].Description=="alpha 2"&&alpha.Versions[0].Url=="https://thunderstore.io/package/download/Team/Alpha/2.0.0/","version description and download URL are retained");
Check(alpha.CategoriesKnown&&alpha.Categories.SequenceEqual(new[]{"Tools"})&&listings.Single(p=>p.Name=="Beta").Categories.SequenceEqual(new[]{"Libraries","patchers"}),"string and object category forms parse without losing names");
Check(listings.Single(p=>p.Name=="Beta").CategoriesKnown&&!listings.Single(p=>p.Name=="NoCategory").CategoriesKnown,"missing categories stay unknown rather than becoming known-empty");
Check(ModSources.KnownVersion("team","alpha","1.0.0")&&!ModSources.KnownVersion("Team","Alpha","8.0.0"),"version lookup is case-insensitive for package identity");
var deps=ModSources.Dependencies("Team-Alpha-1.0.0").ToArray();
Check(deps.SequenceEqual(new[]{"Team-Base-1.0.0"}),"publishing dependency queries read the authoritative snapshot");
Check(ModSources.Exact(projected,new(){PackageName="Alpha",Author="Team",AuthorSource="manifest"})?.Name=="Alpha","source exact matching uses the shared catalog projection");
Check(File.ReadAllText(legacy).Contains("Old")&&new FileInfo(cache).Length>0,"old source cache remains untouched and the authoritative cache is written");
Check(FakeApi.Requests==3,"fresh version/source lookups do not issue per-view requests");

typeof(ShopCatalog).GetField("_cachedAt",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,DateTime.UtcNow-TimeSpan.FromMinutes(31));
Check(!ShopCatalog.Fresh,"shared snapshot expires after thirty minutes");
FakeApi.Latest="3.0.0";
var refreshed=await ShopCatalog.Load(CancellationToken.None);
Check(ModSources.KnownVersion("Team","Alpha","3.0.0"),"direct store refresh is immediately visible to source version checks");
var reprojected=await ModSources.Catalog(CancellationToken.None);
Check(!ReferenceEquals(projected,reprojected)&&reprojected.Single(p=>p.Name=="Alpha").Version=="3.0.0","source projection rebuilds after authoritative snapshot replacement");
Check(FakeApi.IndexRequests==2,"expiry refresh is global rather than per source view");

long successRevision=ShopCatalog.Revision;string cacheBytes=File.ReadAllText(cache);
FakeApi.Fail=true;
bool failed=false;try{await ModSources.Catalog(CancellationToken.None,true);}catch(HttpRequestException){failed=true;}
Check(failed&&ShopCatalog.LastRefreshError.Length>0,"failed source refresh propagates its error rather than reporting old data as fresh");
Check(ReferenceEquals(refreshed,ShopCatalog.Cached())&&ShopCatalog.Revision==successRevision&&File.ReadAllText(cache)==cacheBytes,"failed refresh preserves the previous snapshot, revision and disk cache");
FakeApi.Fail=false;FakeApi.Malformed=true;
bool malformed=false;try{await ShopCatalog.Load(CancellationToken.None,true);}catch(InvalidDataException){malformed=true;}
Check(malformed&&ReferenceEquals(refreshed,ShopCatalog.Cached())&&File.ReadAllText(cache)==cacheBytes,"malformed partial catalog cannot replace the complete cache");
Console.WriteLine($"{passed} shared catalog checks passed; isolated HTTP and filesystem only.");

internal static class FakeApi
{
    internal static int Requests,IndexRequests;
    internal static bool Fail,Malformed;
    internal static string Latest="2.0.0";
    internal static TaskCompletionSource<bool>? Block;
    internal sealed class Handler:HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Interlocked.Increment(ref Requests);string path=request.RequestUri!.AbsolutePath;
            if(path.Contains("package-listing-index"))
            {
                Interlocked.Increment(ref IndexRequests);if(Block!=null)await Block.Task;
                if(Fail)return new(HttpStatusCode.BadGateway){Content=new StringContent("unavailable")};
                return Json(new JArray("https://thunderstore.io/catalog/page1/","https://thunderstore.io/catalog/page2/"));
            }
            if(Malformed)return Json(new JArray(42));
            JObject Version(string number,string description)=>new(){["version_number"]=number,["description"]=description,["is_active"]=true,
                ["website_url"]="https://example.com/alpha2",["download_url"]="https://thunderstore.io/package/download/Team/Alpha/"+number+"/",
                ["dependencies"]=new JArray("Team-Base-1.0.0"),["file_size"]=123,["date_created"]="2026-10-29T00:00:00Z"};
            JObject Listing(string name)=>new(){["owner"]="Team",["name"]=name,["date_updated"]="2026-10-29T00:00:00Z",
                ["versions"]=new JArray(Version("1.0.0","alpha 1"),Version(Latest,"alpha 2"),new JObject {["is_active"]=false,["version_number"]="8.0.0"})};
            if(path.Contains("page1")){var alpha=Listing("Alpha");alpha["categories"]=new JArray("Tools");return Json(new JArray(alpha));}
            var beta=Listing("Beta");beta["categories"]=new JArray(new JObject {["name"]="Libraries"},new JObject {["slug"]="patchers"});
            return Json(new JArray(beta,Listing("NoCategory")));
        }
        private static HttpResponseMessage Json(JToken content)=>new(HttpStatusCode.OK){Content=new StringContent(content.ToString())};
    }
}
namespace ModConfigEnhance
{
    internal static class Loc {internal static string RootDirectory="";internal static string T(string en,string zh,string tw)=>en;}
    internal static class Plugin {internal static readonly Logger Log=new();internal sealed class Logger {internal void LogWarning(string text) {}}}
}
namespace ModConfigEnhance.Overview
{
    internal static class OverviewRepository {internal static void AtomicWrite(string path,string data){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,data);}}
    internal static class ThunderstoreUpdateService
    {
        internal static bool TryParseSource(string value,out string ns,out string name) { ns=name="";return false; }
        internal static bool ValidPart(string value)=>System.Text.RegularExpressions.Regex.IsMatch(value,@"^[A-Za-z0-9_]+$");
        internal static string HomeUrl(string team,string name)=>"https://thunderstore.io/c/peak/p/"+team+"/"+name+"/";
    }
    internal sealed class OverviewMod
    {
        internal string PackageRoot="",Location="",PluginVersion="",IdentityConflict="",Guid="",SourceNamespace="",SourceName="",Author="",AuthorSource="",HomepageUrl="",PackageVersion="",PackageName="",RawName="";
        internal bool SourceConfirmed;internal ModSourceKind SourceKind;internal List<string> PluginGuids=new();internal string SourceKey=>SourceNamespace+"-"+SourceName;
    }
    internal static class PackLibrary { internal static bool Managed; internal static string Workspace=""; internal static Pack? Active; internal sealed class Pack { internal string RevisionStateHash=""; } }
    internal static class PublishedPack {internal static List<Pack> Packs=new();internal sealed class Pack {internal bool Valid=false;internal string Root="";}}
    internal static class ShopUrlPolicy {internal static bool TryNormalize(string input,out string normalized){normalized=input;return Uri.TryCreate(input,UriKind.Absolute,out var u)&&u.Host=="thunderstore.io";}internal static string PrepareMarkdown(string source)=>source;}
    internal static class ShopNetwork
    {
        internal static HttpClient Client()=>new(new FakeApi.Handler());internal static void Report(string message) {}
        internal static Task<HttpResponseMessage> Send(HttpClient client,string url,CancellationToken token)=>client.GetAsync(url,token);
        internal static async Task<byte[]> ReadBody(HttpResponseMessage response,int limit,CancellationToken token){response.EnsureSuccessStatusCode();return await response.Content.ReadAsByteArrayAsync(token);}
        internal static async Task<T> WithCancellation<T>(Task<T> task,CancellationToken token)=>await task.WaitAsync(token);
    }
    internal static class ShopResourceCache {internal static Task<byte[]> Get(string url,int limit,CancellationToken token=default)=>throw new Exception("Not used by catalog tests");}
}
