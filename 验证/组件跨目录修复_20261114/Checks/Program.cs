using System.IO.Compression;
using ModConfigEnhance.Overview;
using Newtonsoft.Json.Linq;

int passed=0;
void Check(bool value,string name) { if(!value)throw new Exception(name);Console.WriteLine($"PASS {++passed} {name}"); }
string NewRoot(string name) { string p=Path.Combine(Path.GetTempPath(),"igmm-cross-area-"+name+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(p);return p; }
void FileAt(string root,string relative,string contents) { string p=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,contents); }
void Manifest(string root,string relative,string key,string version="0.7.1701",string? full=null) {
 int split=key.IndexOf('-');FileAt(root,relative,new JObject { ["namespace"]=key[..split],["name"]=key[(split+1)..],["version_number"]=version,["FullName"]=full??key,["website_url"]="https://github.com/MonoDetour/MonoDetour",["dependencies"]=new JArray() }.ToString());
}
const string mono="MonoDetour-MonoDetour",bridge="MonoDetour-MonoDetour_BepInEx_5";
string bundle=NewRoot("bundle");
Manifest(bundle,$"plugins/{mono}/manifest.json",mono);Manifest(bundle,$"plugins/{bridge}/manifest.json",bridge);
FileAt(bundle,$"core/{mono}/com.github.MonoDetour.dll","main");FileAt(bundle,$"core/{mono}/com.github.MonoDetour.Reflection.dll","reflection");FileAt(bundle,$"core/{mono}/com.github.MonoDetour.Bindings.Reorg.dll","bindings");
FileAt(bundle,$"core/{bridge}/com.github.MonoDetour.Interop.HarmonyX.dll","interop");FileAt(bundle,$"patchers/{bridge}/0.com.github.MonoDetour.BepInEx.5.dll","patcher");
var scan=InfrastructurePackages.Scan(bundle);var shown=InfrastructureOverviewModel.Create(scan);
Check(scan.Count==3&&shown.Count==2,"split all-in-one layout resolves three runtime locations into two package rows");
Check(shown.All(p=>p.IdentityConfirmed&&p.Version=="0.7.1701"),"both package identities and versions come from valid plugins manifests");
var b=shown.Single(p=>p.Key==bridge);
Check(b.LocationCount==2&&b.Areas.Contains("core")&&b.Areas.Contains("patchers"),"bridge keeps core and patcher locations");
Check(b.RuntimeFiles.Count==2&&b.Enabled,"bridge runtime files stay enabled");
Check(scan.All(p=>!p.ManifestVerified&&p.ManifestEvidence!=null),"external identity evidence remains separate from a local manifest");
BepInEx.Paths.BepInExRootPath=bundle;InfrastructurePackages.SetResults(new());
Check(InfrastructurePackages.Scan().Where(p=>p.IdentityConfirmed).Select(p=>p.Key).Distinct().Count()==2,"fresh local catalogue exclusion works without online Results");
string old=NewRoot("old-layout");Manifest(old,$"core/{mono}/manifest.json",mono,"0.7.16");FileAt(old,"core/com.github.MonoDetour.dll","old");Manifest(old,$"plugins/{mono}/manifest.json",mono);
var legacy=InfrastructurePackages.Scan(old).Single();
Check(legacy.IdentityConfirmed&&legacy.Version=="0.7.1701"&&legacy.StaleManifests.Count==1,"legacy loose DLL layout retains highest manifest version and stale metadata");
string direct=NewRoot("direct");Manifest(direct,"core/Team-Own/manifest.json","Team-Own","1.2.3");FileAt(direct,"core/Team-Own/Own.dll","own");
Check(InfrastructurePackages.Scan(direct).Single().Key=="Team-Own","normal colocated manifests remain supported");
string disabled=NewRoot("disabled");Manifest(disabled,$"plugins/{bridge}/manifest.json",bridge);FileAt(disabled,$"core/{bridge}/com.github.MonoDetour.Interop.HarmonyX.dll.d","off");FileAt(disabled,$"patchers/{bridge}/0.com.github.MonoDetour.BepInEx.5.dll.d","off");
Check(!InfrastructureOverviewModel.Create(InfrastructurePackages.Scan(disabled)).Single().Enabled,"disabled suffixes retain identity without becoming enabled");
string nested=NewRoot("nested");Manifest(nested,$"plugins/DependencyGroup/{mono}/manifest.json",mono);FileAt(nested,$"core/{mono}/com.github.MonoDetour.dll","main");
Check(InfrastructurePackages.Scan(nested).Single().IdentityConfirmed,"grouped plugins metadata can supply known split components");
string latest=NewRoot("latest");Manifest(latest,$"plugins/old/{mono}/manifest.json",mono,"0.7.16");Manifest(latest,$"plugins/new/{mono}/manifest.json",mono);FileAt(latest,$"core/{mono}/com.github.MonoDetour.dll","main");
Check(InfrastructurePackages.Scan(latest).Single().Version=="0.7.1701","duplicate external manifests use ManifestCopies highest-version policy");
string bad=NewRoot("bad");Manifest(bad,$"plugins/{mono}/manifest.json",mono,full:"Other-Package");FileAt(bad,$"core/{mono}/com.github.MonoDetour.dll","main");
Check(!InfrastructurePackages.Scan(bad).Single().IdentityConfirmed,"contradictory external FullName never confirms a component");
string invalid=NewRoot("invalid");FileAt(invalid,$"plugins/{mono}/manifest.json","not json");FileAt(invalid,$"core/{mono}/com.github.MonoDetour.dll","main");
Check(!InfrastructurePackages.Scan(invalid).Single().IdentityConfirmed,"invalid external manifest remains unknown");
string localBad=NewRoot("local-bad");Manifest(localBad,$"plugins/{mono}/manifest.json",mono);Manifest(localBad,$"core/{mono}/manifest.json",mono,full:"Other-Package");FileAt(localBad,$"core/{mono}/com.github.MonoDetour.dll","main");
Check(!InfrastructurePackages.Scan(localBad).Single().IdentityConfirmed,"a contradictory local manifest cannot be overridden by external metadata");
string mixed=NewRoot("mixed");Manifest(mixed,$"plugins/{mono}/manifest.json",mono);FileAt(mixed,$"core/{mono}/com.github.MonoDetour.dll","main");FileAt(mixed,$"core/{mono}/Unknown.dll","unknown");
Check(!InfrastructurePackages.Scan(mixed).Single().IdentityConfirmed,"extra unrelated DLL prevents a guessed package identity");
string fake=NewRoot("folder-only");Manifest(fake,"plugins/Team-Fake/manifest.json","Team-Fake","1.0.0");FileAt(fake,"core/Team-Fake/Anything.dll","anything");
Check(!InfrastructurePackages.Scan(fake).Single().IdentityConfirmed,"arbitrary matching directory and manifest names do not confer identity");
string unmatched=NewRoot("unmatched");FileAt(unmatched,"core/UnknownFolder/Unknown.dll","unknown");
Check(!InfrastructurePackages.Scan(unmatched).Single().IdentityConfirmed,"unknown payload stays visible as unknown");
string exact=NewRoot("exact");Manifest(exact,"patchers/Team-Bridge/manifest.json","Team-Bridge","1.0.0");FileAt(exact,"patchers/Team-Bridge/Exact.dll","same");FileAt(exact,"core/MissingMetadata/Exact.dll","same");
Check(InfrastructurePackages.Scan(exact).All(p=>p.IdentityConfirmed),"existing exact unique-byte association is preserved");
if(args.Length>0) {
 string real=NewRoot("real-zip");
 using(var z=ZipFile.OpenRead(args[0]))foreach(var entry in z.Entries.Where(e=>e.FullName.StartsWith("BepInEx/",StringComparison.Ordinal)&&!e.FullName.EndsWith('/'))) {
  string target=Path.Combine(real,entry.FullName[8..].Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(target)!);entry.ExtractToFile(target);
 }
 var rows=InfrastructureOverviewModel.Create(InfrastructurePackages.Scan(real));
 Check(rows.Count==2&&rows.All(p=>p.IdentityConfirmed&&p.Version=="0.7.1701"),"actual user's complete ZIP resolves exactly two known component packages");
 Check(rows.Sum(p=>p.LocationCount)==3&&rows.Sum(p=>p.RuntimeFiles.Count)==5,"actual ZIP retains all three locations and five runtime DLLs");
}
Console.WriteLine($"PASS {passed} production scan/model integration checks");
