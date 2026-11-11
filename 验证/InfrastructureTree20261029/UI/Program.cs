using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
using ModConfigEnhance;
int n=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine($"PASS {++n} {label}");}
var graph=new ComponentDependencyGraph();
foreach(string id in new[]{"Team-MCE","Team-UI","Team-Core","Team-Patch","Team-Other","Team-Consumer","Team-Shared"}){
 var c=new ComponentDependencyGraph.Component{Id=id,PackageKey=id};if(id=="Team-MCE")c.PluginGuids.Add(ComponentDependencyGraph.MceGuid);graph.Components.Add(c);
}
void Edge(string from,string to,bool hard=true)=>graph.Edges.Add(new(){From=from,To=to,Hard=hard,Evidence="manifest 1.0.0"});
Edge("Team-MCE","Team-UI");Edge("Team-UI","Team-Core");Edge("Team-UI","Team-Patch");Edge("Team-Consumer","Team-Core");Edge("Team-Shared","Team-UI");Edge("Team-Shared","Team-Consumer");Edge("Team-Core","Team-Shared",false);
InfrastructureOverviewModel Model(string name,string area="core")=>InfrastructureOverviewModel.Create(new[]{new InfrastructurePackages.Package{Team="Team",Name=name,Area=area,Path="isolated/"+name,Version="1.0.0",Author="Author",Description="Description"}}).Single();
var core=Model("Core");var patch=Model("Patch","patchers");
Check(InfrastructureOverviewModel.GroupTitle=="Core/Patcher"&&core.TypeTitle=="Core"&&patch.TypeTitle=="Patcher","English labels use normal Core/Patcher spelling only");
Check(core.Author=="Author"&&core.Description=="Description","presentation retains author / description");
Check(core.Matches("Author Description")&&!core.Matches("unmatched"),"search includes ordinary metadata");
Check(!core.Capability(graph).CanUninstall&&core.Capability(graph).ProtectionPath.SequenceEqual(new[]{"Team-MCE","Team-UI","Team-Core"}),"MCE transitive chain is explicit");
Check(core.Capability(graph).CanUpdate&&patch.Capability(graph).CanUpdate,"fixed uninstall protection never vetoes update");
Check(Model("Other").Capability(graph).CanUninstall,"non-protected component can enter preview");
var tree=core.Consumers(graph,1,24);
Check(tree.Any(t=>t.Id=="Team-UI"&&t.Depth==1)&&tree.Any(t=>t.Id=="Team-Consumer")&&!tree.Any(t=>t.Id=="Team-Shared"),"first depth contains consumers, not own dependencies");
Check(tree.Any(t=>t.Truncated),"one-level expansion marks deferred depth");
var full=core.Consumers(graph,16,24);
Check(full.Any(t=>t.Cycle)&&full.Any(t=>t.Shared),"reverse cycles and shared nodes terminate explicitly");
Check(full.Where(t=>t.Cycle).All(t=>InfrastructureOverviewModel.TreeCaption(t).Contains("cycle"))&&full.Where(t=>t.Shared).All(t=>InfrastructureOverviewModel.TreeCaption(t).Contains("shared")),"tree captions expose cycle/shared");
Check(core.Consumers(graph,16,3).Count<=3&&core.Consumers(graph,16,3).Any(t=>t.Truncated),"entry budget bounds height");
Check(InfrastructureOverviewModel.TreeCaption(new(){Missing=true}).Contains("unknown"),"unknown marker remains visible");
var state=new ShopInstallationState();state.Reset("fixture");state.CompleteCurrentPack=true;state.DependencyUninstallReason=k=>graph.GetCapability(k).Reason;
var snapshot=new ShopInstalled.Snapshot{Context="fixture"};
foreach(string key in new[]{"Team-Core","Team-Patch","Team-Other"})snapshot.Packages[key]=new(){new(){Key=key,Version="1.0.0",Path="isolated/"+key,Area="core"}};
state.Complete(state.Generation,"fixture",snapshot);
Check(!state.CanUninstall("Team-Core",true)&&state.UninstallBlockReason("Team-Core").Contains("Team-MCE → Team-UI → Team-Core"),"owned protected core disabled with chain explanation");
Check(!state.CanUninstall("Team-Patch",false)&&state.CanInstall("Team-Patch"),"protected patcher uninstall disabled, version action remains enabled");
Check(state.CanUninstall("Team-Other",false)&&state.CanInstall("Team-Core"),"non-protected preview allowed and protected upgrade remains enabled");
graph.Failures.Add("scan failed");Check(!state.CanUninstall("Team-Other",true)&&state.UninstallBlockReason("Team-Other").Contains("unknown"),"unknown graph fails closed without blocking updates");
Check(state.CanInstall("Team-Other"),"unknown uninstall graph does not add update veto");
int ticket=state.Generation;state.Reset("new");Check(!state.Complete(ticket,"fixture",snapshot),"stale installation generation rejected");
string sourceRoot=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../src/Overview"));
string list=File.ReadAllText(Path.Combine(sourceRoot,"OverviewListView.cs"));
string ui=File.ReadAllText(Path.Combine(sourceRoot,"OverviewListView.Infrastructure.cs"));
string source=File.ReadAllText(Path.Combine(sourceRoot,"SourceUi.cs"));
string shop=File.ReadAllText(Path.Combine(sourceRoot,"ShopWindow.cs"));
Check(list.IndexOf("float y = BuildInfrastructure(0, width,")<list.IndexOf("foreach (OverviewMod mod in layoutRows)"),"fixed group precedes all editable groups");
Check(ui.Contains("const float height = RowHeight;")&&!ui.Contains("tree.Length")&&!ui.Contains("ConsumerBranch")&&!ui.Contains("_consumerDepth")&&ui.Contains("BindDependencyCard(name.gameObject, package)")&&ui.Contains("xVersion, 4, versionWidth - 7, 34, 20")&&ui.Contains("xAuthor, 4")&&ui.Contains("xDescription, 4"),"fixed 42 row (no inline tree/height growth), shared DependencyCard binding and version/author/description coordinates");
Check(!ui.Contains("OverviewUi.Check(")&&!ui.Contains("_dropAreas")&&!ui.Contains("OverviewRowInteraction")&&!ui.Contains("localScale"),"no fake toggle, editable drag group or hover scaling");
Check(ui.Contains("settings.interactable = false")&&ui.Contains("\"home\", Menu")&&ui.Contains("\"folder\""),"shared disabled gear / source / folder icons");
Check(ui.Contains("generation != _infrastructureGeneration")&&ui.Contains("revision != OverviewPageCache.Inventory")&&ui.Contains("context != SourceUi.DependencyContext"),"generation plus environment/revision rejects stale completion");
Check(ui.Contains("SelectMany(m => (m.AssemblyPaths")&&ui.IndexOf("var graphconfirmed")<ui.IndexOf("Task.Run"),"confirmed paths captured on UI thread before collection");
Check(ui.Split("InfrastructureDependencyCollector.Collect(").Length==2&&shop.Split("InfrastructureDependencyCollector.Collect(").Length==2,"one graph scan per overview/shop refresh, never per row");
Check(ui.Contains("_scroll.velocity")&&ui.Contains("IsEditingText")&&ui.Contains("selected.transform.IsChildOf(_root)"),"completion defers during scroll/edit/focus");
Check(source.Contains("ShopWindow.Open(repo, mods, false, package.Key, true)")&&source.Contains("string current = UninstallReason(package.Key)"),"component uninstall rechecks capability and routes to existing store");
Check(source.Contains("string reason=UninstallReason(mod.SourceKey)")&&source.Contains("Uninstall unavailable: "),"ordinary source menu also exposes MCE protection reason");
Check(shop.Contains("installedRows.Any(r=>r.Area==\"core\")")&&shop.Contains("VerificationPackages")&&shop.Contains("approve-download")&&shop.Contains("ShowUninstallConfirmation(_task)"),"core retained staged official ZIP consent then real uninstall preview");
Check(shop.Contains("Version=uninstall?_installation.Version(p.Key):_version"),"uninstall uses actual installed version, not catalog latest");
Check(!shop.Contains("&&!task.Request.Operation.Contains(\"uninstall\")"),"core uninstall tasks are rendered rather than silently excluded");
Loc.Language=1;
Check(InfrastructureOverviewModel.GroupTitle=="基础组件(Core/Patcher)"&&core.TypeTitle=="核心组件(Core)"&&patch.TypeTitle=="启动补丁(Patcher)","exact Simplified Chinese group/type labels");
Loc.Language=2;
Check(InfrastructureOverviewModel.GroupTitle=="基礎組件(Core/Patcher)"&&core.TypeTitle=="核心組件(Core)"&&patch.TypeTitle=="啟動補丁(Patcher)","exact Traditional Chinese group/type labels");
Loc.Language=0;
Check(source.Contains("Enabled = option.Item2 != \"uninstall\" || reason.Length == 0")&&source.Contains("DiagnosticSummary.DetailsLabel")&&source.Contains("ShowDependencyDetails(package.Key)"),"protected component menu is grey but full reason remains readable via explicit details entry");
Check(!ui.Contains("ConsumerBranch")&&!ui.Contains("node.gameObject")&&!ui.Contains("line.GetComponent")&&ui.Split("AddComponent<OverviewTextClick>").Length==3&&ui.Contains("Clicked = _ => Menu()"),"no inline tree branches exist; reverse consumers appear only in shared DependencyCard, rows open only their own menu");
Check(source.Contains("active?.RevisionStateHash")&&list.Contains("if (_opened) RefreshInfrastructure()"),"environment identity includes content revision hash");
Console.WriteLine($"{n} passed; isolated models and production wiring; Unity visuals not claimed.");
namespace BepInEx {internal static class Paths {internal static string BepInExRootPath=>throw new Exception("explicit sandbox only");}}
namespace ModConfigEnhance.Overview {internal static class ThunderstoreUpdateService {internal static bool IsNewer(string a,string b)=>false;}}
namespace ModConfigEnhance {internal static class Loc {internal static int Language; internal static string T(string en,string zh,string tw)=>Language==1?zh:Language==2?tw:en;}}
