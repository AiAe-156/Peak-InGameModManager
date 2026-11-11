using Loc = ModConfigEnhance.Overview.Loc;
using ModConfigEnhance.Overview;
using Newtonsoft.Json;
int checks=0;
void Check(bool ok,string name) { if(!ok)throw new Exception(name); Console.WriteLine("PASS "+name);checks++; }
Loc.RootDirectory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../runs/"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));
Directory.CreateDirectory(Loc.RootDirectory);
string fixture=Path.Combine(Loc.RootDirectory,"fixture");Directory.CreateDirectory(fixture);
string dll=Path.Combine(fixture,"feature.dll");File.WriteAllText(dll,"fixture");
OverviewMod Mod(string id)=>new(){Guid=id,Id=id,RawName="BetterPing",PackageName="BetterPing",PackageVersion="1.0.0",Author="Alice",AuthorSource="manifest",PackageRoot=fixture,Location=dll};
ShopListing Listing(string team)=>new(){Team=team,Name="BetterPing",Version="2.0.0",Versions=new(){new(){Version="2.0.0"}}};
ShopCatalog.Items=new(){Listing("Alice"),Listing("TeamX")};
string cache=Path.Combine(Loc.RootDirectory,"runtime/source-identities.json");Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
void Seed(OverviewMod mod,string team){File.WriteAllText(cache,JsonConvert.SerializeObject(new Dictionary<string,ModSourceRecord>{{ModSources.InstallationToken(mod),new(){kind=ModSourceKind.Store,ns=team,name="BetterPing"}}}));ModSources.InvalidateRecords();}
var stale=Mod("stale");Seed(stale,"Ghost");ModSources.Apply(stale);
Check(stale.SourceConfirmed&&stale.SourceNamespace=="Alice","missing derived identity falls back to original evidence");
await ModSources.Discover(new[]{stale},CancellationToken.None,true);
Check(stale.SourceConfirmed&&stale.SourceNamespace=="Alice","forced discovery repairs stale derived identity");
Check(!File.ReadAllText(cache).Contains("Ghost"),"invalid hint is replaced on disk");
var mismatch=Mod("mismatch");Seed(mismatch,"TeamX");ModSources.Apply(mismatch);
Check(mismatch.SourceConfirmed&&mismatch.SourceNamespace=="Alice","catalog-present but evidence-conflicting cache cannot pin identity");
var manual=Mod("manual");ModSources.Confirm(manual,new(){Author="TeamX",Name="BetterPing"});
await ModSources.Discover(new[]{manual},CancellationToken.None);
Check(manual.SourceConfirmed&&manual.SourceNamespace=="TeamX","personal verified choice remains active");
Check(!JsonConvert.DeserializeObject<Dictionary<string,ModSourceRecord>>(File.ReadAllText(cache))!.ContainsKey(ModSources.InstallationToken(manual)),"manual authority never enters derived cache");
Seed(manual,"TeamX");ModSources.Set(new[]{manual},null);ModSources.Apply(manual);
Check(manual.SourceConfirmed&&manual.SourceNamespace=="Alice","automatic mode immediately restores original identity even with legacy pollution");
await ModSources.Discover(new[]{manual},CancellationToken.None,true);
Check(manual.SourceConfirmed&&manual.SourceNamespace=="Alice","forced automatic mode keeps recovered identity");
Check(!File.ReadAllText(cache).Contains("TeamX"),"legacy manual cache pollution is removed durably");
var explicitBad=Mod("explicit");explicitBad.SourceNamespace="Ghost";explicitBad.SourceName="BetterPing";ModSources.Apply(explicitBad);
Check(!explicitBad.SourceConfirmed&&explicitBad.SourceNamespace=="Ghost","invalid original explicit identity is not silently replaced");
var local=Mod("local");ModSources.Set(new[]{local},new(){kind=ModSourceKind.Local});ModSources.Apply(local);
Check(ModSources.ExplicitLocal(local)&&!local.SourceConfirmed,"explicit local remains protected");
foreach(var input in new[]{(false,"1.0.0","2.0.0"),(true,"unknown","2.0.0"),(true,"1.0.0","unknown"),(true,"2.0.0","1.0.0")}){
 Check(!UpdateQueuePolicy.LatestTarget(input.Item1,input.Item2,input.Item3,out _,out var reason),"policy rejects unsafe target");
 var term=ModConfigEnhance.Loc.UpdaterTerms.Single(t=>t.English==reason);
 Check(term.SimplifiedChinese.Length>0&&term.TraditionalChinese.Length>0,"policy reason registered in both Chinese languages");
}
Check(ModConfigEnhance.Loc.UpdaterTerms.Any(t=>t.English=="Update to checked latest version")&&ModConfigEnhance.Loc.UpdaterTerms.Any(t=>t.English=="Restore modpack version {0}"),"separate menu phrases registered");
Console.WriteLine($"TOTAL {checks} production integration checks passed; sandbox {Loc.RootDirectory}");
namespace ModConfigEnhance { internal static partial class Loc { internal readonly struct Term {internal readonly string Key,English,SimplifiedChinese,TraditionalChinese;internal Term(string key,string en,string zh,string tw){Key=key;English=en;SimplifiedChinese=zh;TraditionalChinese=tw;}} } }
