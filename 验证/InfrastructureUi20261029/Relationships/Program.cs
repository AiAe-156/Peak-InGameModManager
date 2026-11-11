using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;

int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine($"PASS {++passed} {name}");}
ComponentDependencyGraph Graph(params string[] ids)
{
    var graph=new ComponentDependencyGraph();
    foreach(string id in ids)graph.Components.Add(new(){Id=id,PackageKey=id});
    return graph;
}
void Edge(ComponentDependencyGraph graph,string from,string to,bool hard=true,string evidence="assembly ref")
    =>graph.Edges.Add(new(){From=from,To=to,Hard=hard,Evidence=evidence});

var repeated=Graph("Team-Owner","Team-Child");
Edge(repeated,"Team-Owner","Team-Child",true,"assembly ref One");
Edge(repeated,"team-owner","TEAM-CHILD",false,"BepInDependency");
Edge(repeated,"Team-Owner","Team-Child",true,"manifest 1.0.0");
Edge(repeated,"Team-Owner","Team-Owner");
var single=ComponentRelationshipTree.Build(repeated,"Team-Owner",false);
Check(single.Count==1&&single[0].Id=="Team-Child","repeated relationship evidence and case variants yield one display row");
Check(!single[0].Optional,"any hard requirement wins when hard and optional evidence merge");
Check(!single[0].Cycle&&!single[0].Shared,"repeated evidence is not mislabeled as cycle or shared");
Check(single.All(r=>r.Id!="Team-Owner"),"package self reference is omitted");
var reverse=ComponentRelationshipTree.Build(repeated,"Team-Child",true);
Check(reverse.Count==1&&reverse[0].Id=="Team-Owner"&&!reverse[0].Optional,"reverse relationships use the same deduplication and hard merge");
Check(repeated.Edges.Count==4&&repeated.Edges.Count(e=>!e.Hard)==1,"display projection leaves authoritative evidence unchanged");

var optional=Graph("Team-A","Team-B");
Edge(optional,"Team-A","Team-B",false,"soft plugin requirement");
Edge(optional,"Team-A","Team-B",false,"optional manifest hint");
Check(ComponentRelationshipTree.Build(optional,"Team-A",false).Single().Optional,"soft-only relationship remains optional");

var cyclic=Graph("Team-A","Team-B","Team-C");
Edge(cyclic,"Team-A","Team-B");Edge(cyclic,"Team-B","Team-C");Edge(cyclic,"Team-C","Team-A");
var cycle=ComponentRelationshipTree.Build(cyclic,"Team-A",false);
Check(cycle.Count==3&&cycle.Last().Id=="Team-A"&&cycle.Last().Cycle,"real cycle remains visibly marked");
Check(cycle.Last().Depth==3&&!cycle.Last().HasChildren&&!cycle.Last().Truncated,"cycle stops recursion without a false deferred-depth marker");
Check(cycle.Select(r=>r.Path).Distinct().Count()==cycle.Count,"cycle rows retain distinct stable branch paths");
Check(cycle[0].Parent==""&&cycle[1].Parent==cycle[0].Path&&cycle[2].Parent==cycle[1].Path,"tree path and parent preserve the branch structure");

var diamond=Graph("Team-A","Team-B","Team-C","Team-D","Team-E");
Edge(diamond,"Team-A","Team-B");Edge(diamond,"Team-A","Team-C");
Edge(diamond,"Team-B","Team-D");Edge(diamond,"Team-C","Team-D");Edge(diamond,"Team-D","Team-E");
var shared=ComponentRelationshipTree.Build(diamond,"Team-A",false);
var sharedRows=shared.Where(r=>r.Id=="Team-D").ToArray();
Check(sharedRows.Length==2&&sharedRows.Count(r=>r.Shared)==1,"same component reached through different branches keeps one shared reference");
Check(shared.Count(r=>r.Id=="Team-E")==1,"shared descendants expand once");
Check(sharedRows.Single(r=>r.Shared).HasChildren==false&&!sharedRows.Single(r=>r.Shared).Cycle,"shared reference terminates without inventing a cycle");

var deep=Graph("Team-A","Team-B","Team-C","Team-D","Team-E");
Edge(deep,"Team-A","Team-B");Edge(deep,"Team-B","Team-C");Edge(deep,"Team-C","Team-D");Edge(deep,"Team-D","Team-E");
var shallow=ComponentRelationshipTree.Build(deep,"Team-A",false,2,8);
Check(shallow.Count==2&&shallow.All(r=>r.Depth<=2),"hover projection never exceeds two relationship levels");
Check(shallow.Last().Truncated&&shallow.Last().HasChildren,"remaining deeper relationships have a deferred marker");
var wide=Graph("Team-Owner");
for(int i=0;i<20;i++){string id="Team-Child"+i.ToString("D2");wide.Components.Add(new(){Id=id});Edge(wide,"Team-Owner",id);}
var bounded=ComponentRelationshipTree.Build(wide,"Team-Owner",false,2,8);
Check(bounded.Count==8&&bounded.All(r=>r.Depth==1),"eight-row hover budget bounds a wide graph");
Check(bounded.Last().Truncated,"entry-budget truncation is visible");
Check(bounded.Select(r=>r.Id).SequenceEqual(bounded.Select(r=>r.Id).OrderBy(id=>id,StringComparer.OrdinalIgnoreCase)),"siblings are deterministically ordered");

const string fileA="file:core/Team-A/Interop.dll";
const string fileB="file:patchers/Team-B/Entry.dll";
var mapped=Graph("Team-A","Team-B",fileA,fileB);
Edge(mapped,"Team-A",fileA);Edge(mapped,fileA,"Team-A");Edge(mapped,fileA,fileB);
Edge(mapped,"Team-A","Team-B",false,"optional separate evidence");
string Canonical(string id)=>id==fileA?"Team-A":id==fileB?"Team-B":id;
var canonical=ComponentRelationshipTree.Build(mapped,"Team-A",false,2,8,Canonical);
Check(canonical.Count==1&&canonical[0].Id=="Team-B","canonical file-to-package mapping collapses all evidence into its package");
Check(canonical.All(r=>r.Id!="Team-A")&&!canonical[0].Cycle&&!canonical[0].Shared,"canonical owner references disappear before traversal");
Check(!canonical[0].Optional&&!canonical[0].Missing,"canonical installed package retains merged hard requirement");
Check(ComponentRelationshipTree.Build(mapped,fileA,false,2,8,Canonical).Single().Id=="Team-B","canonicalization also applies to the requested root");
var mappedReverse=ComponentRelationshipTree.Build(mapped,"Team-B",true,2,8,Canonical);
Check(mappedReverse.Count==1&&mappedReverse[0].Id=="Team-A","reverse traversal canonicalizes before selecting consumers");

var missing=Graph("Team-Owner");Edge(missing,"Team-Owner","missing:Absent");
var absent=ComponentRelationshipTree.Build(missing,"Team-Owner",false).Single();
Check(absent.Missing&&!absent.HasChildren&&absent.Id=="missing:Absent","unavailable target remains visible as missing");
Check(ComponentRelationshipTree.Build(Graph("Team-Empty"),"Team-Empty",false).Count==0,"empty relationship set produces no fabricated rows");
Console.WriteLine($"{passed} relationship checks passed.");
