using ModConfigEnhance.Overview;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
int checks=0;
void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);checks++;}
var source=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../.."));
Loc.RootDirectory=Path.Combine(source,"runs",DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));Directory.CreateDirectory(Loc.RootDirectory);
var official=JsonConvert.DeserializeObject<List<ShopListing>>(File.ReadAllText(Path.Combine(source,"official-mono.json")))!;
ShopCatalog.Items=official;
InfrastructurePackages.Package Component(string area,string name){var root=Path.Combine(source,"fixtures",area,"MonoDetour-"+name);var data=JObject.Parse(File.ReadAllText(Path.Combine(root,"manifest.json")));return new(){Team=(string)data["namespace"]!,Name=(string)data["name"]!,Version=(string)data["version_number"]!,Author=(string?)data["author"]??(string)data["namespace"]!,Homepage=(string)data["website_url"]!,Path=root,ManifestEvidence=data};}
OverviewMod Row(InfrastructurePackages.Package p){var m=new OverviewMod{Guid=p.Name,Id=p.Name,RawName=p.Name,PackageName=p.Name,PackageRoot=p.Path,Location=p.Path,SourceNamespace=p.Team,SourceName=p.Name,Author=p.Author,AuthorSource="manifest",HomepageUrl=p.Homepage,PackageVersion=p.Version};ModSources.ReadManifestIdentity(m,p.ManifestEvidence!,p.Path);return m;}
var core=Component("core","MonoDetour");var patch=Component("patchers","MonoDetour_BepInEx_5");var rows=new[]{Row(core),Row(patch)};
Check(rows.All(m=>m.PackageVersion=="0.7.16"),"real read-only manifests copied to sandbox at 0.7.16");
await ModSources.Discover(rows,CancellationToken.None,true);
Check(rows.All(m=>m.SourceConfirmed),"both explicit manifests confirm against official same-repository catalogue");
using(var service=new ThunderstoreUpdateService(new(),TimeSpan.Zero)){await service.CheckAsync(rows,CancellationToken.None);Check(rows.All(m=>m.HasUpdate&&m.LatestVersion=="0.7.1701"),"two green-dot states: 0.7.16 -> 0.7.1701");Check(service.LastReport.UpdateCount==2&&service.LastReport.Completed==2&&service.LastReport.PackageCount==2,"K=2 and exactly two package comparisons");}
Check(ShopNetwork.Requests==0,"official cached metadata needs no exact HTTP request");
var projected=official.Select(p=>new StorePackage{Author=p.Team,Name=p.Name,Version=p.Version,Homepage=p.Homepage,Versions=p.Versions.Select(v=>v.Version).ToList()}).ToList();var index=new StoreIdentityIndex(projected);
var guess=Row(core);guess=new OverviewMod{PackageName=guess.PackageName,RawName=guess.RawName,Author=guess.Author,AuthorSource="manifest",SourceNamespace=guess.SourceNamespace,SourceName=guess.SourceName,HomepageUrl=guess.HomepageUrl};Check(index.Resolve(guess)==null,"ns/name plus trusted author without explicit manifest evidence retains fork ambiguity");
var forged=Row(core);ModSources.ReadManifestIdentity(forged,core.ManifestEvidence!,Path.Combine(source,"Mallory-MonoDetour"));Check(index.Resolve(forged)==null&&forged.IdentityConflict.Length>0,"forged directory conflicting with explicit namespace rejected");
var badFull=Row(core);var altered=(JObject)core.ManifestEvidence!.DeepClone();altered["FullName"]="Mallory-MonoDetour";ModSources.ReadManifestIdentity(badFull,altered,core.Path);Check(index.Resolve(badFull)==null,"conflicting FullName rejected");
var badAuthor=Row(core);badAuthor.Author="Mallory";Check(index.Resolve(badAuthor)==null,"explicit manifest cannot override author conflict");
var badRepo=Row(core);badRepo.HomepageUrl="https://github.com/Mallory/Fork";Check(index.Resolve(badRepo)==null,"explicit manifest cannot override repository conflict");
var duplicate=new StoreIdentityIndex(projected.Concat(new[]{projected[0]}));Check(duplicate.Resolve(Row(patch))==null,"duplicate official identity not silently selected");
var api=new JObject{["namespace"]="MonoDetour",["name"]="MonoDetour",["is_deprecated"]=false,["latest"]=new JObject{["version_number"]="0.7.1701",["is_active"]=true,["website_url"]=core.Homepage},["community_listings"]=new JArray(new JObject{["community"]="peak",["is_active"]=true,["is_reviewed"]=true,["categories"]=new JArray("Libraries")})};
InfrastructurePackages.Items=new(){core,patch};ShopCatalog.Items=new();
ShopNetwork.Respond=url=>{var payload=(JObject)api.DeepClone();payload["name"]=url.Contains("MonoDetour_BepInEx_5")?patch.Name:core.Name;return new(HttpStatusCode.OK){Content=new StringContent(payload.ToString())};};
using(var service=new ThunderstoreUpdateService(new(),TimeSpan.Zero)){int before=ShopNetwork.Requests;await service.CheckAsync(Array.Empty<OverviewMod>(),CancellationToken.None);Check(service.LastReport.UpdateCount==2&&service.LastReport.PackageCount==2&&service.LastReport.Failed==0&&core.Latest=="0.7.1701"&&patch.Latest=="0.7.1701"&&ShopNetwork.Requests-before==2,"missing catalogue allows only two exact manifest-backed infrastructure requests and truthful K");}
async Task Reject(string label,Action<JObject> mutate){ShopNetwork.Respond=url=>{var body=(JObject)api.DeepClone();mutate(body);return new(HttpStatusCode.OK){Content=new StringContent(body.ToString())};};InfrastructurePackages.Items=new(){core};using var service=new ThunderstoreUpdateService(new(),TimeSpan.Zero);await service.CheckAsync(Array.Empty<OverviewMod>(),CancellationToken.None);Check(service.LastReport.Failed==1&&service.LastReport.UpdateCount==0&&core.Latest=="",label);}
await Reject("API mismatched identity rejected",j=>j["namespace"]="Mallory");
await Reject("API missing PEAK community rejected",j=>j["community_listings"]=new JArray());
await Reject("API inactive PEAK listing rejected",j=>j["community_listings"]![0]!["is_active"]=false);
await Reject("API unreviewed PEAK listing rejected",j=>j["community_listings"]![0]!["is_reviewed"]=false);
await Reject("API inactive latest version rejected",j=>j["latest"]!["is_active"]=false);
await Reject("API missing active version field rejected",j=>((JObject)j["latest"]!).Remove("is_active"));
await Reject("API deprecated package rejected",j=>j["is_deprecated"]=true);
await Reject("API invalid version rejected",j=>j["latest"]!["version_number"]="invalid");
await Reject("API repository conflict rejected",j=>j["latest"]!["website_url"]="https://github.com/Mallory/Fork");
ShopNetwork.Respond=url=>new(HttpStatusCode.NotFound){Content=new StringContent("{}")};
using(var service=new ThunderstoreUpdateService(new(),TimeSpan.Zero)){await service.CheckAsync(Array.Empty<OverviewMod>(),CancellationToken.None);Check(service.LastReport.Failed==1&&service.LastReport.UpdateCount==0&&core.Latest=="","HTTP 404 remains failed, no stale cached latest");var cache=JsonConvert.DeserializeObject<Dictionary<string,ThunderstoreUpdateService.CacheEntry>>(File.ReadAllText(Path.Combine(Loc.RootDirectory,"updates.json")))!;Check(cache["MonoDetour-MonoDetour"].Error.Contains("HTTP 404"),"cache preserves real HTTP 404 status");}
var ordinary=Row(core);InfrastructurePackages.Items.Clear();int requests=ShopNetwork.Requests;
using(var service=new ThunderstoreUpdateService(new(),TimeSpan.Zero)){await service.CheckAsync(new[]{ordinary},CancellationToken.None);Check(ShopNetwork.Requests==requests&&!ordinary.SourceConfirmed&&!ordinary.HasUpdate,"ordinary mod cannot use component-only missing-catalogue fallback");}
InfrastructurePackages.Items=new(){core,patch};int loads=ShopCatalog.Loads;requests=ShopNetwork.Requests;
using(var service=new ThunderstoreUpdateService(new(){GlobalUpdateAllowed=false},TimeSpan.Zero)){await service.CheckAsync(Array.Empty<OverviewMod>(),CancellationToken.None);Check(ShopNetwork.Requests==requests&&ShopCatalog.Loads==loads&&service.LastReport.UpdateCount==0,"global permission off makes zero catalogue/API requests");}
Console.WriteLine($"TOTAL {checks} MonoDetour production checks passed");
