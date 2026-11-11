using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
using Newtonsoft.Json.Linq;

if (args.Contains("--live"))
{
    var live = InfrastructureOverviewModel.Create(InfrastructurePackages.Scan(@"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx"));
    Console.WriteLine($"LIVE packages={live.Count} locations={live.Sum(p=>p.LocationCount)}");
    foreach(var package in live)
    {
        Console.WriteLine($"{package.Key} | {package.Version} | confirmed={package.IdentityConfirmed} | {package.TypeTitle}");
        foreach(var location in package.Locations)Console.WriteLine($"  {location.Area} | manifest={location.ManifestVerified} | {location.Path}");
    }
    return;
}
string root=Path.Combine(AppContext.BaseDirectory,"fixture_"+Guid.NewGuid().ToString("N"));
int passed=0;
void Check(bool ok,string title){if(!ok)throw new Exception(title);Console.WriteLine($"PASS {++passed} {title}");}
string Folder(string area,string folder){string path=Path.Combine(root,area,folder);Directory.CreateDirectory(path);return path;}
string Package(string area,string folder,string key,params (string Name,string Bytes)[] files)
{
    string path=Folder(area,folder);int split=key.IndexOf('-');
    File.WriteAllText(Path.Combine(path,"manifest.json"),new JObject {
        ["namespace"]=key.Substring(0,split),["name"]=key.Substring(split+1),["version_number"]="1.0.0",
        ["dependencies"]=new JArray("Team-Dependency-1.0.0") }.ToString());
    foreach(var file in files)File.WriteAllText(Path.Combine(path,file.Name),file.Bytes);
    return path;
}
Package("core","Team-One","Team-One",("One.dll","one"));
Package("core","Team-Split","Team-Split",("Library.dll","split-library"));
Package("patchers","Team-Split","Team-Split",("Patcher.dll","split-patcher"));
Package("core","Team-Base","Team-Base",("Base.dll","base"));
string bridge=Package("patchers","Team-Bridge","Team-Bridge",("Compat.dll","compat"),("Entry.dll","entry"));
string bits=Folder("core","ArbitraryFolderName");File.WriteAllText(Path.Combine(bits,"Compat.dll"),"compat");
Package("patchers","Team-Fix","Team-Fix",("Fix.dll","fix"));
File.WriteAllText(Path.Combine(root,"core","Loader.dll"),"baseline loader");
var initial=InfrastructurePackages.Scan(root);
var models=InfrastructureOverviewModel.Create(initial);
Check(initial.Count==7&&models.Count==5&&models.Sum(p=>p.LocationCount)==7,"seven locations project into five component packages");
var splitPackage=models.Single(p=>p.Key=="Team-Split");
Check(splitPackage.LocationCount==2&&splitPackage.Areas.SequenceEqual(new[]{"core","patchers"}),"cross-area package retains both locations");
Check(splitPackage.TypeTitle=="Core + Patcher"&&splitPackage.Matches("CORE patchers"),"combined area label and search");
var bridgePackage=models.Single(p=>p.Key=="Team-Bridge");
Check(bridgePackage.LocationCount==2&&bridgePackage.IdentityConfirmed,"missing manifest associated from exact unique payload");
Check(bridgePackage.Locations.Single(p=>p.Path==bits).IdentityConfirmed&&!bridgePackage.Locations.Single(p=>p.Path==bits).ManifestVerified,"matched location is distinguished from a verified manifest");
Check(bridgePackage.Matches(bits),"secondary location paths are searchable");
Check(!initial.Any(p=>p.Path==Path.Combine(root,"core")),"loader baseline does not become an orphan package");
Check(InfrastructureOverviewModel.Create(initial.Concat(initial)).Sum(p=>p.LocationCount)==7,"repeated input cannot duplicate installation locations");
initial.Single(p=>p.Path==bridge).Dependencies[0]="changed";
Check(bridgePackage.Dependencies.SequenceEqual(new[]{"Team-Dependency-1.0.0"}),"merged dependencies are an immutable snapshot");

string orphan=Folder("core","Team-Bridge");File.WriteAllText(Path.Combine(orphan,"Different.dll"),"unknown");
string extra=Folder("patchers","MixedBits");File.WriteAllText(Path.Combine(extra,"Compat.dll"),"compat");File.WriteAllText(Path.Combine(extra,"Extra.dll"),"extra");
string invalid=Package("core","Team-Untrusted","Other-Untrusted",("Compat.dll","compat"));
Package("core","Team-CommonA","Team-CommonA",("Common.dll","common"));
Package("patchers","Team-CommonB","Team-CommonB",("Common.dll","common"));
string ambiguous=Folder("core","CommonBits");File.WriteAllText(Path.Combine(ambiguous,"Common.dll"),"common");
var rescanned=InfrastructurePackages.Scan(root);
foreach(string path in new[]{orphan,extra,invalid,ambiguous})
{
    var unknown=rescanned.Single(p=>p.Path==path);
    Check(!unknown.IdentityConfirmed&&!unknown.ManifestVerified&&unknown.Key.StartsWith("file:"),"unconfirmed directory stays visible without invented identity: "+Path.GetFileName(path));
    var unknownModel=InfrastructureOverviewModel.Create(new[]{unknown}).Single();
    var capability=unknownModel.Capability(new ComponentDependencyGraph());
    Check(!capability.CanUninstall&&!capability.CanUpdate&&capability.Unknown,"unconfirmed presentation never grants actions: "+Path.GetFileName(path));
}
Check(!InfrastructureOverviewModel.Create(new[]{new InfrastructurePackages.Package {Team="Team",Name="Plugin",Area="plugins",Path="x"}}).Any(),"ordinary plugins cannot enter component group");
string disabled=Folder("patchers","DisabledBits");File.WriteAllText(Path.Combine(disabled,"Compat.dll.old"),"compat");
Check(InfrastructurePackages.Scan(root).Single(p=>p.Path==disabled).Key=="Team-Bridge","disabled payload suffix does not hide a verified file association");
Console.WriteLine($"{passed} inventory checks passed.");

namespace BepInEx {internal static class Paths {internal static string BepInExRootPath=>throw new Exception("Explicit root required");}}
namespace ModConfigEnhance.Overview {internal static class ThunderstoreUpdateService {internal static bool IsNewer(string a,string b)=>false;}}
namespace ModConfigEnhance {internal static class Loc {internal static string T(string en,string zh,string tw)=>en;}}
