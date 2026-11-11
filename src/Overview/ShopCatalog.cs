#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace ModConfigEnhance.Overview;
internal sealed class ShopListing
{
    public string Name="",Team="",Description="",Icon="",Version="",Homepage="",Url="";
    public bool Deprecated,Nsfw,CategoriesKnown;
    public long Downloads;
    public DateTime Updated;
    public List<string> Categories=new();
    public List<ShopVersion> Versions=new();
    public string Key=>Team+"-"+Name;
}
internal sealed class ShopVersion
{
    public string Version="",Description="",Homepage="",Url="";
    public long Bytes;
    public DateTime Date;
    public List<string> Dependencies=new();
}
internal static class ShopCatalog
{
    private static readonly object Gate=new();
    private static List<ShopListing>? _memory;
    private static DateTime _cachedAt;
    private static bool _cacheRead;
    private static long _revision;
    private static string _lastRefreshError="";
    private static Task<List<ShopListing>>? _refresh;
    private static string CachePath=>Path.Combine(Loc.RuntimeDirectory,"shop-catalog.json");
    // The store, source finder and updater all observe this one snapshot and expiry.
    internal static TimeSpan Lifetime=>TimeSpan.FromMinutes(30);
    internal static bool Fresh {get {lock(Gate){ReadCache();return IsFresh;}}}
    internal static long Revision {get {lock(Gate){ReadCache();return _revision;}}}
    internal static string LastRefreshError {get {lock(Gate)return _lastRefreshError;}}
    private static bool IsFresh=>_memory!=null&&DateTime.UtcNow-_cachedAt<Lifetime;
    internal static List<ShopListing> Cached()
    {
        lock(Gate){ReadCache();return _memory??new();}
    }
    private static void ReadCache()
    {
        if(_cacheRead)return;
        _cacheRead=true;
        try
        {
            if(!File.Exists(CachePath))return;
            var stored=JArray.Parse(File.ReadAllText(CachePath));
            var cached=stored.ToObject<List<ShopListing>>();
            if(cached==null||cached.Count==0||cached.Any(p=>p==null||!ThunderstoreUpdateService.ValidPart(p.Team)
                ||!ThunderstoreUpdateService.ValidPart(p.Name)||p.Versions==null||p.Versions.Count==0
                ||p.Versions.Any(v=>v==null||!System.Version.TryParse(v.Version,out _)||v.Dependencies==null)))
                throw new InvalidDataException("Store catalog cache is incomplete.");
            // Older shop caches predate these fields; derive only the public package URL.
            foreach(var package in cached)
            {
                if(string.IsNullOrEmpty(package.Url))package.Url=ThunderstoreUpdateService.HomeUrl(package.Team,package.Name);
                if(package.Categories?.Count>0)package.CategoriesKnown=true;
            }
            bool completeMetadata=stored.OfType<JObject>().All(p=>p["Homepage"]!=null&&p["Url"]!=null&&p["CategoriesKnown"]!=null);
            _memory=cached;_cachedAt=completeMetadata?File.GetLastWriteTimeUtc(CachePath):DateTime.MinValue;_revision++;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException)
        {Plugin.Log.LogWarning("[Store cache] "+e.Message);}
    }
    internal static HttpClient Client() => ShopNetwork.Client();
    internal static async Task<string> Text(HttpClient client,string url,CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        using var response = await ShopNetwork.Send(client, url, timeout.Token).ConfigureAwait(false);
        var bytes = await ShopNetwork.ReadBody(response, 64*1024*1024, timeout.Token).ConfigureAwait(false);
        ShopNetwork.Report("body wireBytes=" + bytes.Length);
        using var stream = new MemoryStream(bytes);
        if (bytes.Length > 2 && bytes[0] == 31 && bytes[1] == 139)
        {
            using var gz = new GZipStream(stream, CompressionMode.Decompress);
            using var output = new MemoryStream();
            byte[] buffer = new byte[16384]; int count;
            while ((count = await gz.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (output.Length + count > 64*1024*1024) throw new IOException("Store response exceeds size limit.");
                output.Write(buffer, 0, count);
            }
            bytes = output.ToArray();
            ShopNetwork.Report("decode=gzip decodedBytes=" + bytes.Length + " limit=" + (64*1024*1024));
        }
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
    internal static async Task<List<ShopListing>> Load(CancellationToken token,bool refresh=false)
    {
        token.ThrowIfCancellationRequested();
        Task<List<ShopListing>> pending;
        lock(Gate)
        {
            ReadCache();
            if(_refresh!=null&&!_refresh.IsCompleted)pending=_refresh;
            else if(!refresh&&IsFresh)return _memory!;
            else pending=_refresh=Refresh();
        }
        // A caller leaving its window must not cancel a refresh shared with another window.
        return await ShopNetwork.WithCancellation(pending,token).ConfigureAwait(false);
    }
    private static async Task<List<ShopListing>> Refresh()
    {
        try
        {
        using var client=Client();var index=JArray.Parse(await Text(client,"https://thunderstore.io/c/peak/api/v1/package-listing-index/",CancellationToken.None).ConfigureAwait(false));var result=new List<ShopListing>();
        foreach(var page in index)
        {
            string? url=page.Type==JTokenType.String?page.Value<string>():null;
            if(!ShopUrlPolicy.TryNormalize(url ?? "", out var catalogUrl)) throw new IOException(Loc.T("The store catalog returned an invalid address.", "商店目录返回无效地址。", "商店目錄返回無效地址。"));
            foreach(var entry in JArray.Parse(await Text(client,catalogUrl,CancellationToken.None).ConfigureAwait(false)))
            {
                if(entry is not JObject item||item["versions"] is not JArray availableVersions
                    ||availableVersions.Any(v=>v is not JObject))throw InvalidCatalog();
                var versions=availableVersions.OfType<JObject>().Where(v=>v.Value<bool?>("is_active")!=false)
                    .OrderByDescending(v=>System.Version.TryParse(v.Value<string>("version_number"),out var parsed)?parsed:new System.Version()).ToList();if(versions.Count==0)continue;
                string team=item.Value<string>("owner")??"",name=item.Value<string>("name")??"";
                if(!ThunderstoreUpdateService.ValidPart(team)||!ThunderstoreUpdateService.ValidPart(name)
                    ||versions.Any(v=>!System.Version.TryParse(v.Value<string>("version_number"),out _)
                        ||v["dependencies"] is not JArray dependencies||dependencies.Any(d=>d.Type!=JTokenType.String)))
                    throw InvalidCatalog();
                var latest=versions[0];DateTime.TryParse(item.Value<string>("date_updated"),out var date);
                var categories=item["categories"] as JArray;
                var categoryNames=categories?.Select(category=>category.Type==JTokenType.String ? category.Value<string>()
                    :category is JObject c ? c.Value<string>("name")??c.Value<string>("slug") :null).ToArray();
                var listing=new ShopListing{Name=name,Team=team,Description=latest.Value<string>("description")??"",Icon=latest.Value<string>("icon")??"",Version=latest.Value<string>("version_number")??"",Homepage=latest.Value<string>("website_url")??"",Url=ThunderstoreUpdateService.HomeUrl(team,name),Updated=date,Deprecated=item.Value<bool?>("is_deprecated")==true,Nsfw=item.Value<bool?>("has_nsfw_content")==true,Downloads=versions.Sum(v=>v.Value<long?>("downloads")??0),CategoriesKnown=categoryNames!=null&&categoryNames.All(s=>!string.IsNullOrWhiteSpace(s)),Categories=categoryNames?.Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s!).Distinct(StringComparer.OrdinalIgnoreCase).ToList()??new()};
                foreach(var v in versions){DateTime.TryParse(v.Value<string>("date_created"),out var created);listing.Versions.Add(new ShopVersion{Version=v.Value<string>("version_number")??"",Description=v.Value<string>("description")??"",Homepage=v.Value<string>("website_url")??"",Url=v.Value<string>("download_url")??"",Bytes=v.Value<long?>("file_size")??0,Date=created,Dependencies=(v["dependencies"] as JArray??new()).Values<string>().Where(s=>s!=null).Select(s=>s!).ToList()});}result.Add(listing);
            }
        }
        if(result.Count==0)throw new IOException(Loc.T("The store catalog was empty. The previous cache was kept.", "商店目录为空，已保留旧缓存。", "商店目錄為空，已保留舊緩存。"));
        Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);OverviewRepository.AtomicWrite(CachePath,JsonConvert.SerializeObject(result));
        lock(Gate){_memory=result;_cachedAt=DateTime.UtcNow;_lastRefreshError="";_revision++;}
        return result;
        }
        catch(Exception error){lock(Gate)_lastRefreshError=error.Message;throw;}
    }
    private static InvalidDataException InvalidCatalog()=>new(Loc.T("The store catalog contains invalid package data. The previous cache was kept.",
        "商店目录包含无效的资源包资料，已保留旧缓存。", "商店目錄包含無效的套件資料，已保留舊快取。"));
    internal static async Task<string> Markdown(ShopListing p,string version,bool changelog,CancellationToken token)
    {
        string path=changelog?"changelog":"readme";
        string url="https://thunderstore.io/api/experimental/package/"+p.Team+"/"+p.Name+"/"+version+"/"+path+"/";
        string json=System.Text.Encoding.UTF8.GetString(await ShopResourceCache.Get(url,8*1024*1024,token).ConfigureAwait(false));
        var data=JToken.Parse(json);string markdown=data.Type==JTokenType.String?data.Value<string>()??"":data.Value<string>("markdown")??data.Value<string>(path)??"";
        markdown=ShopUrlPolicy.PrepareMarkdown(markdown);
        var images=System.Text.RegularExpressions.Regex.Matches(markdown,@"!\[[^\]]*\]\((https://[^\s\)]+)").Cast<System.Text.RegularExpressions.Match>().Select(m=>m.Groups[1].Value).Distinct().Take(12);
        foreach (string image in images)
        {
            token.ThrowIfCancellationRequested();
            if (ShopUrlPolicy.TryNormalize(image, out var safeImage))
                _ = Prefetch(safeImage);
        }
        return markdown;
    }
    private static async Task Prefetch(string url)
    {
        try { await ShopResourceCache.Get(url,16*1024*1024).ConfigureAwait(false); }
        catch { /* Best effort only; failed entries are removed and can be retried. */ }
    }
}
