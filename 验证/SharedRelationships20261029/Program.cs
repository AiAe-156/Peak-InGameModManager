using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
using Newtonsoft.Json.Linq;

int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine($"PASS {++passed} {name}");}
BepInEx.Paths.BepInExRootPath=Path.Combine(AppContext.BaseDirectory,"fixture_"+Guid.NewGuid().ToString("N"));
foreach(string area in new[]{"plugins","core","patchers"})Directory.CreateDirectory(Path.Combine(BepInEx.Paths.BepInExRootPath,area));
string resource=Path.Combine(BepInEx.Paths.PluginPath,"Team-Resource");Directory.CreateDirectory(resource);
File.WriteAllText(Path.Combine(resource,"manifest.json"),new JObject { ["namespace"]="Team",["name"]="Resource",["version_number"]="1.2.0",
    ["dependencies"]=new JArray("Team-Core-1.0.0","BepInEx-BepInExPack_PEAK-5.4.2100") }.ToString());
string corePath=Path.Combine(BepInEx.Paths.BepInExRootPath,"core","Team-Core");
var core=new InfrastructureOverviewModel {Key="Team-Core",Name="Core library",Version="1.0.0",Locations=new(){new(){Path=corePath,Area="core"}}};
var a=new OverviewMod {Guid="id.a",Name="Unconfirmed A",RawName="A",Location=Path.Combine(BepInEx.Paths.PluginPath,"A.dll"),PluginVersion="1.0.0"};
var b=new OverviewMod {Guid="id.b",Name="B",SourceConfirmed=true,SourceNamespace="Team",SourceName="B",PackageVersion="2.0.0",Location=Path.Combine(BepInEx.Paths.PluginPath,"B.dll")};
var c=new OverviewMod {Guid="id.c",Name="Unconfirmed C",Location=Path.Combine(BepInEx.Paths.PluginPath,"C.dll")};
var d=new OverviewMod {Name="Path only D",Location=Path.Combine(BepInEx.Paths.PluginPath,"D.dll")};
a.RuntimeDependencies.Add(new(){Id="id.b",MinimumVersion="1.0.0"});a.RuntimeDependencies.Add(new(){Id="id.core"});
a.PackageDependencies.Add("Team-B-1.0.0");a.PackageDependencies.Add("BepInEx-BepInExPack-5.4.2100");
b.PackageDependencies.Add("Team-Resource-1.0.0");
string xRoot=Path.Combine(BepInEx.Paths.PluginPath,"localized-translator");Directory.CreateDirectory(xRoot);
File.WriteAllText(Path.Combine(xRoot,"manifest.json"),new JObject{["namespace"]="RTLC",["name"]="XUnity_AutoTranslator",["version_number"]="5.6.11"}.ToString());
var x=new OverviewMod{Guid="gravydevsupreme.xunity.autotranslator",Name="Unity Auto Translator",PackageRoot=xRoot,PluginVersion="5.6.1"};
a.PackageDependencies.Add("RTLC-XUnity_AutoTranslator-5.6.11");
var mods=new[]{a,b,c,d,x};
a.DependencyMetadataKnown=true;
var early=SharedRelationshipSnapshot.Get(mods,new[]{core},null);
string earlyRoot=SharedRelationshipSnapshot.RootId(a,early);
var earlyRows=ComponentRelationshipTree.Build(early,earlyRoot,false,2,8);
Check(a.SourceKey=="-"&&earlyRoot=="guid:id.a"&&earlyRows.Any(r=>r.Id=="Team-B"),"unknown dash source still resolves runtime relationships without raw graph");
Check(SharedRelationshipSnapshot.RootId(c,early)=="guid:id.c"&&SharedRelationshipSnapshot.RootId(c,early)!=earlyRoot,"unconfirmed mods with empty package roots remain distinct");
Check(earlyRows.Any(r=>r.Id=="guid:"+x.Guid)&&earlyRows.Count(r=>r.Id=="guid:"+x.Guid)==1,"XUnity manifest package resolves to actual plugin even before source confirmation");
Check(earlyRows.Count(r=>r.Id=="Team-B")==1,"runtime and manifest evidence merge for a confirmed dependency");
var labels=new ComponentRelationshipLabels(early,mods,Array.Empty<InfrastructureOverviewModel>());
Check(ReferenceEquals(labels.Mod(earlyRoot),a)&&ReferenceEquals(labels.Mod("guid:id.c"),c),"labels resolve actual GUIDs rather than matching arbitrary dash package keys");
Check(earlyRows.Any(r=>r.Id=="Team-Resource")&&SharedRelationshipSnapshot.Metadata(early,"Team-Resource")?.Version=="1.2.0","resource-only manifest node and version are exported to the shared graph");
Check(ComponentRelationshipTree.Build(early,"Team-Core",true).Any(r=>r.Id=="Team-Resource"),"resource-only packages participate in reverse relationships");
Check(earlyRows.All(r=>!SharedRelationshipSnapshot.IsFramework(r.Id))&&!early.Edges.Any(e=>SharedRelationshipSnapshot.IsFramework(e.To)),"loader environment declarations are excluded from display relationships");
int scans=OverviewRepository.Scans;
Check(ReferenceEquals(early,SharedRelationshipSnapshot.Get(mods,Array.Empty<InfrastructureOverviewModel>(),null))&&OverviewRepository.Scans==scans,"hover and details reuse one snapshot without rescanning directories");
Check(SharedRelationshipSnapshot.Components(early).Count==1&&labels.Component("Team-Core")==core,"empty component argument falls back to the last registered component locations");

var raw=new ComponentDependencyGraph();
raw.Components.Add(new(){Id="Team-A",PackageKey="Team-A",PluginGuids={"id.a"},Locations={"plugins/A.dll"}});
raw.Components.Add(new(){Id="Team-B",PackageKey="Team-B",PluginGuids={"id.b"},Locations={"plugins/B.dll"}});
raw.Components.Add(new(){Id="file:core/Team-Core/Core.dll",PluginGuids={"id.core"},Locations={"core/Team-Core/Core.dll"}});
raw.Components.Add(new(){Id="Team-Path",PackageKey="Team-Path",Locations={"plugins/D.dll"}});
raw.Components.Add(new(){Id="file:core/BepInEx.dll",Locations={"core/BepInEx.dll"}});
raw.Components.Add(new(){Id="BepInEx-BepInExPack_PEAK",PackageKey="BepInEx-BepInExPack_PEAK"});
raw.Edges.Add(new(){From="Team-A",To="Team-B",Evidence="assembly ref"});
raw.Edges.Add(new(){From="Team-A",To="file:core/Team-Core/Core.dll",Evidence="assembly ref Core"});
raw.Edges.Add(new(){From="Team-A",To="file:core/BepInEx.dll",Evidence="assembly ref BepInEx"});
raw.Edges.Add(new(){From="Team-A",To="BepInEx-BepInExPack_PEAK",Evidence="manifest"});
raw.Edges.Add(new(){From="Team-A",To="missing:BepInEx",Evidence="unresolved platform ref"});
var full=SharedRelationshipSnapshot.Get(mods,Array.Empty<InfrastructureOverviewModel>(),raw);
string root=SharedRelationshipSnapshot.RootId(a,full);
Check(root=="Team-A"&&SharedRelationshipSnapshot.Canonical(full,earlyRoot)==root,"actual GUID metadata joins an unconfirmed mod to its raw package root");
Check(SharedRelationshipSnapshot.RootId(d,full)=="Team-Path","exact assembly path joins a mod without inventing a GUID");
Check(OverviewRepository.Scans==scans,"raw graph completion rebuilds the display projection without repeating manifest scans");
var fullRows=ComponentRelationshipTree.Build(full,root,false);
Check(fullRows.Any(r=>r.Id=="Team-B")&&fullRows.Any(r=>r.Id=="Team-Resource")&&fullRows.Any(r=>r.Id=="Team-Core"),"complete details contain all registered and raw dependency paths");
Check(fullRows.Count(r=>r.Id=="Team-B")==1&&!fullRows.Any(r=>r.Id=="guid:id.core"&&r.Missing),"raw GUID and file metadata remove duplicate or falsely missing provider nodes");
Check(!full.Components.Any(n=>SharedRelationshipSnapshot.IsFramework(n.Id))&&!fullRows.Any(r=>r.Id.Contains("BepInEx",StringComparison.OrdinalIgnoreCase)),"core BepInEx file and environment packages never appear as missing mods");
Check(raw.Components.Count==6&&raw.Edges.Count==5&&raw.Failures.Count==0&&!ReferenceEquals(full,raw),"display projection never mutates or replaces the backend graph");
Check(!full.GetCapability("Team-B").CanUninstall&&full.GetCapability("Team-B").Unknown,"display graph cannot grant uninstall capability if queried accidentally");
var richLabels=new ComponentRelationshipLabels(full,mods,Array.Empty<InfrastructureOverviewModel>());
Check(richLabels.Name(root)=="Unconfirmed A"&&richLabels.Version("Team-B")=="2.0.0"&&richLabels.Component("Team-Core")==core,"shared labels retain original mod and component metadata after root rebasing");
var forwardHover=ComponentRelationshipTree.Build(full,root,false,2,8);
Check(forwardHover.All(r=>r.Depth<=2)&&forwardHover.Count<=8&&forwardHover.Any(r=>r.Truncated),"hover uses bounded traversal of the exact graph used by full details");
var direct=forwardHover.Single(r=>r.Id=="Team-B");
Check(SharedRelationshipSnapshot.Item(full,direct,false).Required=="1.0.0","per-relationship version requirement survives shared projection");
var inbound=ComponentRelationshipTree.Build(full,"Team-B",true,2,8).Single(r=>r.Id==root);
Check(SharedRelationshipSnapshot.Item(full,inbound,true).Required=="1.0.0","reverse row retrieves the original directional version requirement");
Check(ReferenceEquals(full,SharedRelationshipSnapshot.Get(mods,Array.Empty<InfrastructureOverviewModel>(),full)),"passing a display graph to details does not recursively project or rescan it");
var pending=SharedRelationshipSnapshot.Get(mods,Array.Empty<InfrastructureOverviewModel>(),null);
Check(ComponentRelationshipTree.Build(pending,SharedRelationshipSnapshot.RootId(a,pending),false).Any(r=>r.Id=="Team-B"),"dependency details retain registered edges while a raw graph is invalidated and rebuilding");
var ready=SharedRelationshipSnapshot.Get(mods,Array.Empty<InfrastructureOverviewModel>(),raw);
Check(SharedRelationshipSnapshot.RootId(a,ready)=="Team-A"&&OverviewRepository.Scans==scans,"raw graph re-publication re-resolves the root without rescanning ordinary manifests");

a.RuntimeDependencies.Add(new(){Id="id.c",Optional=true});
// Production publishers invalidate display metadata once per changed data generation.
OverviewPageCache.Content++;
var changed=SharedRelationshipSnapshot.Get(mods,Array.Empty<InfrastructureOverviewModel>(),raw);
Check(!ReferenceEquals(full,changed)&&ComponentRelationshipTree.Build(changed,SharedRelationshipSnapshot.RootId(a,changed),false).Any(r=>r.Id=="guid:id.c"&&r.Optional),"dependency content changes invalidate the shared snapshot");
Check(OverviewRepository.Scans>scans,"registered dependency source refreshes once after content changes");
int beforeInventory=OverviewRepository.Scans;OverviewPageCache.Inventory++;
var inventory=SharedRelationshipSnapshot.Get(mods,Array.Empty<InfrastructureOverviewModel>(),raw);
Check(!ReferenceEquals(changed,inventory)&&OverviewRepository.Scans>beforeInventory,"inventory changes invalidate cached manifest evidence");
SourceUi.DependencyContext="fixture-other-context";
var context=SharedRelationshipSnapshot.Get(mods,Array.Empty<InfrastructureOverviewModel>(),null);
Check(!ReferenceEquals(inventory,context)&&SharedRelationshipSnapshot.Components(context).Count==0,"context switch drops component locations from the previous environment");
var crowded = new ComponentDependencyGraph();
crowded.Components.Add(new(){Id="manager"});
foreach(string id in new[]{"A","XUnity"}) { crowded.Components.Add(new(){Id=id}); crowded.Edges.Add(new(){From="manager",To=id,Hard=true}); }
for(int i=0;i<20;i++) { string id="child"+i;crowded.Components.Add(new(){Id=id});crowded.Edges.Add(new(){From="A",To=id,Hard=true}); }
Check(!ComponentRelationshipTree.Build(crowded,"manager",false,2,8).Any(r=>r.Id=="XUnity"),"reproduce old eight-row DFS hiding XUnity direct dependency");
var expanded=ComponentRelationshipTree.Build(crowded,"manager",false,32,2048);
Check(expanded.Count==22&&expanded.Any(r=>r.Id=="XUnity"&&r.Depth==1),"expanded dependency policy retains all direct dependencies and children");
for(int i=0;i<16;i++) { string id="user"+i;crowded.Components.Add(new(){Id=id});crowded.Edges.Add(new(){From=id,To="manager",Hard=true}); }
var inboundGraph=new ComponentDependencyGraph();inboundGraph.Components.AddRange(crowded.Components);inboundGraph.Edges.AddRange(crowded.Edges);
var concise=ComponentRelationshipTree.Build(inboundGraph,"manager",true,1,8);
Check(concise.Count==8&&concise.All(r=>r.Depth==1)&&concise.Any(r=>r.Truncated),"dependants remain concise with explicit limit marker");
var twoLevels=ComponentRelationshipTree.Build(crowded,"manager",false,2,2048);
Check(twoLevels.All(r=>r.Depth<=2)&&twoLevels.Any(r=>r.Id=="XUnity"),"two-level dependencies retain all direct items without eight-row cap");
var bundleGraph=new ComponentDependencyGraph();
bundleGraph.Components.Add(new(){Id="manager",PluginGuids={"id.a"},Assemblies={"Manager","0Harmony"}});
bundleGraph.Components.Add(new(){Id="voice",PluginGuids={"id.b"},Assemblies={"Voice","0Harmony"}});
bundleGraph.Components.Add(new(){Id="translator",PluginGuids={x.Guid},Assemblies={"Translator","0Harmony"}});
bundleGraph.Edges.Add(new(){From="manager",To="voice",Evidence="assembly ref 0Harmony",Hard=true});
bundleGraph.Edges.Add(new(){From="manager",To="translator",Evidence="assembly ref 0Harmony",Hard=true});
bundleGraph.Edges.Add(new(){From="translator",To="manager",Evidence="assembly ref 0Harmony",Hard=true});
bundleGraph.Edges.Add(new(){From="manager",To="translator",Evidence="manifest RTLC-XUnity_AutoTranslator-5.6.11",Hard=true});
var display=SharedRelationshipSnapshot.Get(mods,Array.Empty<InfrastructureOverviewModel>(),bundleGraph);
string managerId=SharedRelationshipSnapshot.RootId(a,display),translatorId=SharedRelationshipSnapshot.RootId(x,display);
Check(display.Edges.Any(e=>e.From==managerId&&e.To==translatorId&&e.Evidence.StartsWith("manifest")),"manager really depends on translator through manifest");
Check(!display.Edges.Any(e=>e.From==translatorId&&e.To==managerId),"bundled Harmony does not invent translator dependency on manager");
Check(display.Edges.Any(e=>e.From==managerId&&e.To=="assembly:0Harmony"),"bundled provider references displayed as library node");
Check(!ComponentRelationshipTree.Build(display,managerId,true,2,2048).Any(r=>r.Id==translatorId),"forward dependency does not leak into manager dependants");
Check(ComponentRelationshipTree.Build(display,translatorId,true,2,2048).Any(r=>r.Id==managerId),"reverse translator dependants contain manager");
Check(bundleGraph.Edges.Count==4&&!bundleGraph.Edges.Any(e=>e.To=="assembly:0Harmony"),"backend ownership graph remains unchanged");
Console.WriteLine($"{passed} shared relationship checks passed; isolated files only.");

namespace BepInEx {internal static class Paths {internal static string BepInExRootPath="";internal static string PluginPath=>Path.Combine(BepInExRootPath,"plugins");}}
namespace ModConfigEnhance {internal static class Loc {internal static string T(string en,string zh,string tw)=>en;internal static string F(string en,string zh,string tw,params object[] args)=>string.Format(en,args);}internal static class Plugin {internal static Logger Log=new();internal sealed class Logger {internal void LogDebug(string text){}}}}
namespace ModConfigEnhance.Overview
{
    internal static class SourceUi {internal static string DependencyContext="fixture";internal static string DisplayDependencyContext=>DependencyContext;}
    internal static class OverviewPageCache {internal static int Inventory; internal static int Content;}
    internal static class OverviewRepository {internal static int Scans;internal static IEnumerable<string> SafeFiles(string root){Scans++;return Directory.GetFiles(root,"*",SearchOption.AllDirectories);}}
    internal static class ThunderstoreUpdateService {internal static bool ValidPart(string value)=>System.Text.RegularExpressions.Regex.IsMatch(value,@"^[A-Za-z0-9_]+$");internal static bool TryParseSource(string? value,out string team,out string name){var parts=(value??"").Split('-');team=parts[0];name=parts.Length==2?parts[1]:"";return parts.Length==2&&ValidPart(team)&&ValidPart(name);}}
    internal static class ShopInstalled {internal static bool Key(string key){var parts=key.Split('-');return parts.Length==2&&parts.All(ThunderstoreUpdateService.ValidPart);}}
    internal static class OverviewLanguageBridge {internal static string GetDisplayName(OverviewMod mod)=>mod.Name;}
    internal sealed partial class OverviewMod
    {
        internal string Guid="",SourceNamespace="",SourceName="",IdentityConflict="",Location="",PackageRoot="",PackageVersion="",PluginVersion="",Name="",RawName="",PackageName="";
        internal bool SourceConfirmed=false,Loaded=false,TargetEnabled=true;internal List<string> PluginGuids=new(),AssemblyPaths=new();internal string SourceKey=>SourceNamespace+"-"+SourceName;
    }
    internal sealed class InfrastructureOverviewModel
    {
        internal string Key="",Name="",Version="";internal bool IdentityConfirmed=true;
        internal List<Location> Locations=new();internal List<string> Dependencies=new();
        internal sealed class Location {internal string Path="",Area="";}
    }
}
