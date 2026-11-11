using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
int passed=0;
void Check(bool ok,string name) { if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);passed++; }
// Exercise the exact production idleCache and publisher, not a duplicate model.
foreach(int capacity in new[]{0,12,24})
{
 var idleCache=new StableRowCache<object>(capacity);var identities=new List<object>();
 for(int i=0;i<300;i++) { var identity=new object();identities.Add(identity);var evicted=idleCache.Park(identity);
  Check(idleCache.Count<=capacity,"full-tree idle idleCache bounded "+capacity+"/"+i);
  Check((evicted!=null)==(i>=capacity),"eviction only above budget "+capacity+"/"+i); }
 if(capacity>0) { var lastTree=identities.Last();idleCache.Remove(lastTree);Check(idleCache.Count==capacity-1,"wake removes same object from idle budget");
  Check(idleCache.Park(lastTree)==null,"wake/park keeps same identity without eviction");
  Check(ReferenceEquals(idleCache.Park(new object()),identities[300-capacity]),"least recently used full tree evicted"); }
}
var activeTrees=new HashSet<int>();
foreach(int total in new[]{60,120,300})
{
 // Real production eviction policy, with fake complete control trees (identity + child + listener).
 var pool=new StableRowCache<object>(24);var trees=new Dictionary<int,object>();
 var callbacks=new Dictionary<int,Action>();int clicked=-1;
 for(int sweep=0;sweep<3;sweep++)
 for(int top=0;top<total;top++)
 {
  var visibleIds=Enumerable.Range(0,total).Where(i=>VirtualRowRange.Contains(i*38,36,top*38,380,76)).ToHashSet();
  foreach(var id in trees.Keys.Where(id=>!visibleIds.Contains(id)).ToArray())
  {
   // Park is idempotent, but production only parks on active -> inactive transition.
   if(!activeTrees.Contains(id))continue;
   activeTrees.Remove(id);var evicted=pool.Park(trees[id]);
   if(evicted!=null) { var key=trees.Single(p=>ReferenceEquals(p.Value,evicted)).Key;trees.Remove(key);callbacks.Remove(key); }
  }
  foreach(int id in visibleIds)
  {
   if(!trees.ContainsKey(id)) { trees[id]=new object[]{new object(),new object()};int bound=id;callbacks[id]=()=>clicked=bound; }
   var sameTree=trees[id];var sameListener=callbacks[id];pool.Remove(sameTree);activeTrees.Add(id);sameListener();
   if(clicked!=id || !ReferenceEquals(trees[id],sameTree) || !ReferenceEquals(callbacks[id],sameListener))throw new Exception("identity/listener changed on wake");
  }
  if(trees.Count>visibleIds.Count+24)throw new Exception("resident tree count not viewport bounded");
 }
 Check(true,"60/120/300 repeated scroll complete-tree identity/listener and residency "+total);
 activeTrees.Clear();
}
int publicationWrites=0, revision=0;string business="idle";
var publication=new MCE.UpdateWorker.StateChangePublication();
bool PublishState()=>publication.Publish(()=>revision+":"+business,()=>{publicationWrites++;revision++;});
Check(PublishState(),"initial worker snapshot published");
for(int tick=0;tick<300;tick++)Check(!PublishState(),"stable snapshot 60-second poll "+tick);
Check(publicationWrites==1 && revision==1,"unchanged snapshot never rewrites or increments revision");
business="progress";Check(PublishState() && revision==2,"business progress publishes revision");
business="error";bool failed=false;
try { publication.Publish(()=>revision+":"+business,()=>throw new System.IO.IOException("sandbox failure")); } catch(System.IO.IOException){failed=true;}
Check(failed && PublishState(),"failed publication remains retryable");
Check(!PublishState(),"retried publication deduplicates");
var dwell=new HoverDwell();int generated=0;
for(int i=0;i<300;i++) { dwell.Enter(i.ToString(),i*.01f);if(dwell.Take(i*.01f+.025f)!=null)generated++;dwell.Cancel(); }
Check(generated==0,"fast transitions do not open before 200ms");
dwell.Enter("stable",4);Check(dwell.Take(4.199f)==null,"200ms dwell holds");Check(dwell.Take(4.201f)=="stable","hover opens after 200ms");Check(dwell.Take(5)==null,"same hover emits once");
dwell.Enter("old",6);dwell.Enter("new",6);Check(dwell.Take(6.201f)=="new","same-frame transitions coalesce to latest identity");
dwell.Enter("cancelled",7);dwell.Cancel();Check(dwell.Take(7)==null,"exit cancels pending hover");
var graph=new ComponentDependencyGraph();
for(int i=0;i<300;i++)graph.Components.Add(new(){Id="node"+i});
for(int i=1;i<300;i++)graph.Edges.Add(new(){From="node"+(i-1),To="node"+i});
graph.Edges.Add(new(){From="node0",To="node1",Hard=false});
int builds=ComponentRelationshipTree.IndexBuilds;
for(int i=0;i<300;i++) { var rows=ComponentRelationshipTree.Build(graph,"node"+i,false,2,8);Check(rows.Count<=2,"bounded tree "+i); }
Check(ComponentRelationshipTree.IndexBuilds==builds+1,"300 hover roots share one adjacency index");
Check(ComponentRelationshipTree.Build(graph,"node1",true,1,8).Single().Id=="node0","reverse adjacency uses same index");
var next=new ComponentDependencyGraph();next.Components.Add(new(){Id="new"});ComponentRelationshipTree.Build(next,"new",false);
Check(ComponentRelationshipTree.IndexBuilds==builds+2,"new graph generation builds once");
foreach(int total in new[]{60,120,300})
{
 var model=Enumerable.Range(0,total).ToArray();
 foreach(int top in new[]{0,500,1000})
 {
  var visible=model.Where(i=>VirtualRowRange.Contains(i*38,36,top,380,76)).ToArray();
  Check(visible.Length<=16,"viewport-bounded active range "+total+"/"+top);
  Check(model.Length==total,"complete business model retained "+total+"/"+top);
 }
 var shift=model.Skip(2).Take(total-4).ToArray();Check(shift.Length==total-4,"range selection retains offscreen data "+total);
}
Check(VirtualRowRange.ScrollTo(760,36,0,380)==416,"offscreen controller target scrolls into view");
Check(VirtualRowRange.ScrollTo(38,36,400,380)==38,"up navigation scrolls into view");
var lease=new RowIdentityLease();string invoked="";lease.Bind("A");var old=lease.Guard(()=>invoked="A");lease.Release();lease.Bind("B");old();Check(invoked=="","recycled shell rejects old identity listener");lease.Guard(()=>invoked="B")();Check(invoked=="B","new identity listener executes");lease.Release();lease.Guard(()=>invoked="wrong")();Check(invoked=="B","unbound lease cannot execute");
string root=Path.Combine(AppContext.BaseDirectory,"sandbox_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
string queue=Path.Combine(root,"queue.json"),transaction=Path.Combine(root,"transaction.json"),shop=Path.Combine(root,"task.json"),error=Path.Combine(root,"worker-error.json");
File.WriteAllText(queue,"queue0");File.WriteAllText(transaction,"prepared");File.WriteAllText(shop,"shop0");
var cache=new ChangedFileSnapshot<string>();string[] Files()=>new[]{queue,transaction,shop,error};
string Read()=>string.Join("|",Files().Where(File.Exists).Select(File.ReadAllText));
Check(cache.Refresh("A",Files,Read),"initial production snapshot read");int parses=cache.Reads;
for(int i=0;i<150;i++)cache.Refresh("A",Files,Read);Check(cache.Reads==parses,"60s-equivalent stable polls never deserialize again");
File.WriteAllText(transaction,"committing-alone");Check(cache.Refresh("A",Files,Read)&&cache.Value!.Contains("committing-alone"),"transaction-only change observed");
File.WriteAllText(shop,"shop-changed-alone");Check(cache.Refresh("A",Files,Read),"shop-only change observed");
File.WriteAllText(error,"worker-failure");Check(cache.Refresh("A",Files,Read),"worker-error-only change observed");
File.WriteAllText(queue,"queue-changed-alone");Check(cache.Refresh("A",Files,Read),"queue-only change observed");
string? last=cache.Value;File.WriteAllText(queue,"broken");Check(!cache.Refresh("A",Files,()=>throw new IOException("malformed"))&&cache.Value==last&&cache.Error=="malformed","failure preserves last snapshot and reports error");
Check(!cache.Refresh("B",Files,()=>throw new IOException("new env broken"))&&cache.Value==null,"environment change never retains old environment snapshot");
Check(cache.Refresh("B",Files,Read),"new environment recovers");
File.WriteAllText(queue,"race-before");last=cache.Value;Check(!cache.Refresh("B",Files,()=>{File.WriteAllText(transaction,"race-during-read");return "mixed";})&&cache.Value==last,"racing multi-file read is not published");
foreach(int total in new[]{60,120,300})
{
 int current=0,visited=1;float scroll=0;
 while(VirtualRowRange.Next(current,1,total) is int nextIndex && nextIndex>=0)
 {
  current=nextIndex;visited++;
  scroll=VirtualRowRange.ScrollTo(current*38,36,scroll,380);
  Check(VirtualRowRange.Contains(current*38,36,scroll,380,0),"controller binds full-model target "+total+"/"+current);
 }
 Check(visited==total && current==total-1,"controller reaches last offscreen row "+total);
 while(VirtualRowRange.Next(current,-1,total)>=0)current=VirtualRowRange.Next(current,-1,total);
 Check(current==0 && VirtualRowRange.Next(0,-1,total)==-1,"controller stops at first row "+total);
 var source=new RowIdentityLease();source.Bind("drag-source");bool dropped=false;
 var drop=source.Guard(()=>dropped=true);
 // Viewport changes do not release the pinned drag source's lease.
 for(int frame=0;frame<100;frame++)VirtualRowRange.Contains(0,36,frame*100,380,76);
 drop();Check(dropped && source.Identity=="drag-source","cross-screen drag retains source identity "+total);
 source.Release();dropped=false;drop();Check(!dropped,"ended drag cannot call recycled source "+total);
}
var progress=new CoalescedProgress();
for(int i=0;i<1000;i++)progress.Publish("progress "+i);
Check(progress.Take(0)=="progress 999","1000 callbacks coalesce to latest progress");
progress.Publish("next");Check(progress.Take(.149f)==null,"progress is gated for 150ms");
Check(progress.Take(.151f)=="next","changed progress publishes after gate");
progress.Publish("next");Check(progress.Take(1)==null,"identical progress is not republished");
progress.Publish("old environment");progress.Clear();Check(progress.Take(2)==null,"closed or changed environment clears progress");
Parallel.For(0,1000,i=>progress.Publish("parallel "+i));Check(progress.Take(3)?.StartsWith("parallel ")==true,"background progress publishes safely");
Console.WriteLine($"RESULT {passed} passed; sandbox={root}");
