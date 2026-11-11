using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
int checks=0;
void Check(bool value,string name) { if(!value) throw new Exception(name); Console.WriteLine($"PASS {++checks}: {name}"); }
string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
string Read(string name)=>File.ReadAllText(Path.Combine(root,"src/Overview",name));
string hints=Read("OverviewDependencyHints.cs"), components=Read("OverviewDependencyHints.Components.cs"), row=Read("OverviewListView.Infrastructure.cs"), list=Read("OverviewListView.cs");
Check(hints.Contains("Bind(target, _ => Lines(mod))")&&components.Contains("=> Bind(target, chains => {"),"ordinary/component use one renderer and lifecycle");
Check(hints.Split("Panel(\"DependencyCard\"").Length==2&&!components.Contains("Panel("),"one DependencyCard, no alternate tooltip/tree panels");
Check(list.Contains("RowHeight = 42")&&row.Contains("const float height = RowHeight;")&&!row.Contains("ConsumerBranch")&&!row.Contains("_consumerDepth")&&!row.Contains("More levels"),"source row always 42; no inline tree");
Check(row.Contains("BindDependencyCard(name.gameObject, package)")&&!row.Contains("AddHint(name.gameObject")&&!row.Contains(".Reason")&&!row.Contains(".Failures"),"name has dependency binding only; diagnostics absent from tooltip wiring");
var capability=new ComponentDependencyGraph.Capability{Unknown=true,Reason=new string('X',4096)+"C:/secret/long/path"};
for(int language=0;language<3;language++) { ModConfigEnhance.Loc.Language=language; string text=InfrastructureOverviewModel.ProtectionHint(capability); Check(text.Length<120&&!text.Contains("XXXX")&&!text.Contains("secret"),$"4k diagnostics excluded, language {language}"); }
ModConfigEnhance.Loc.Language=0;
Check(InfrastructureOverviewModel.ShortText(new string('x',4096)).Length==141,"description bounded");
foreach(float scale in new[]{0.5f,1f,1.5f,2f})
foreach(float x in new[]{-2000f,0f,300f,8000f})
foreach(float y in new[]{-2000f,0f,300f,8000f}) {
 var box=DependencyCardGeometry.Position(13,27,640/scale,360/scale,x,y,760,2000);
 if(box.X<13||box.Y<27||box.X+box.Width>13+640/scale+0.01f||box.Y+box.Height>27+360/scale+0.01f)throw new Exception("bbox");
}
Check(true,"64 scaled/edge bbox cases fit visible intersection");
var tiny=DependencyCardGeometry.Position(10,10,2,3,20,20,760,3000);
Check(tiny.X+tiny.Width<=12&&tiny.Y+tiny.Height<=13,"tiny viewport clamps");
string wrapped=DependencyCardGeometry.WrapTokens("<size=17>"+new string('A',4096)+" &lt;</size>");
Check(wrapped.Contains('\u200B')&&wrapped.StartsWith("<size=17>")&&wrapped.EndsWith("&lt;</size>"),"long tokens break without corrupting TMP tags/entities");
Check(hints.Contains("RectMask2D")&&hints.Contains("TextOverflowModes.Masking")&&!hints.Contains("65536")&&!hints.Contains("float.PositiveInfinity"),"real mask and finite rect measurement, no giant overflow mesh");
Check(components.Contains("rootCanvas")&&components.Contains("GetComponentsInParent<RectMask2D>")&&components.Contains("InverseTransformPoint"),"canvas/source viewport intersection in local units");
Check(hints.Contains("fullHeight = MeasureContent(visible, contentWidth)")&&hints.Contains("WrapTokens(string.Join"),"follow-mouse commits measured visible lines");
Check(hints.Contains("_scroll.enabled = _fixed")&&hints.Contains("_scroll.viewport = _viewport")&&hints.Contains("_scroll.content = _text.rectTransform")&&hints.Contains("_fixed && _onCard")&&hints.Contains("Time.unscaledTime + .35f"),"fixed scroll and source-to-card hover bridge/hide wiring");
Check(components.Contains("chains ? 12 : 1, 512")&&components.Contains("DisplayNameWithOriginal")&&components.Contains("_number(mod)")&&components.Contains("GroupBy"),"component chains config, translated/original names, numbering/groups");
var graph=new ComponentDependencyGraph();
foreach(string id in new[]{"Root","A","B","Shared"})graph.Components.Add(new(){Id=id});
void Edge(string from,string to)=>graph.Edges.Add(new(){From=from,To=to});
Edge("A","Root");Edge("B","Root");Edge("Shared","A");Edge("Shared","B");Edge("Root","Shared");
var tree=graph.ReverseTree("Root",12,512);
Check(tree.Any(e=>e.Cycle)&&tree.Any(e=>e.Shared)&&tree.Count<20,"graph cyclic/shared reverse dependencies terminate");
Check(graph.ReverseTree("Root",12,3).Count==3&&graph.ReverseTree("Root",12,3).Any(e=>e.Truncated),"graph traversal budget marker");
Check(graph.ReverseTree("Root",1,512).All(e=>e.Depth<=1),"chains disabled direct consumers only");
Check(row.Contains("() => _hint.gameObject.SetActive(false)")&&components.Contains("beforeShow?.Invoke()"),"component hover hides stale ordinary hint before opening shared card");
Console.WriteLine($"{checks} checks passed. Pure geometry/model/source-wiring checks only, NOT Unity visual acceptance.");
namespace ModConfigEnhance { internal static class Loc { internal static int Language; internal static string T(string en,string zh,string tw)=>Language==1?zh:Language==2?tw:en; } }
namespace ModConfigEnhance.Overview {
 internal static class ShopInstalled { internal static bool Key(string key)=>true; }
 internal static class InfrastructurePackages { internal sealed class Package { internal string Key="",Name="",Area="",Author="",Description="",Version="",Path=""; internal List<string> Dependencies=new(); } }
}
