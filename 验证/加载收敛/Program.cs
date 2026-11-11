using System.Collections.Generic;
using System.Linq;
using ModConfigEnhance.Overview;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
class Program {
 static int n; static void Check(bool b,string name){if(!b)throw new Exception(name);n++;Console.WriteLine("PASS "+name);}
 static void Main(){
  var cache=new DisplayRevisionCache<string>();int reads=0;string Read()=>"env"+(++reads);
  cache.Refresh(0,0,true,Read);for(int i=0;i<10000;i++)cache.Refresh(0,i/100d,true,Read);
  Check(reads==1,"10000 stable display checks perform one read");cache.Refresh(1,100,true,Read);Check(reads==2,"revision invalidates immediately");
  cache.Refresh(1,101,false,Read);Check(reads==2,"unreliable watcher fallback bounded");cache.Refresh(1,102,false,Read);Check(reads==3,"unreliable watcher retries after two seconds");
  cache.Refresh(1,102,true,Read,true);Check(reads==4,"explicit open forces live read");
  try{cache.Refresh(2,103,true,()=>throw new Exception());}catch{}Check(cache.Value=="env4","failed read never publishes partial value");cache.Refresh(2,103,true,Read);Check(reads==5,"failed revision retry succeeds");
  var budget=new RowBuildBudget(1000,16);for(int i=0;i<16;i++)Check(budget.TryTake(1,0),"cheap step allowed "+i);Check(!budget.TryTake(1,0),"step ceiling");Check(budget.TryTake(2,20),"new frame resets");Check(!budget.TryTake(2,22),"2ms time ceiling");Check(budget.TryTake(2,22,true),"one target immediate shell");Check(!budget.TryTake(2,22),"immediate does not grant children unlimited budget");
  var h=new SchedulerHarness(70);h.Tick(1);Check(h.Completed.SequenceEqual(new[]{0,1}),"first frame completes front rows rather than spraying shells");int work=h.Work;h.Visible=false;h.Tick(2);Check(h.Work==work,"hidden tab pauses construction");h.Visible=true;h.Tick(3);Check(h.Completed.Count==5 && h.Completed.SequenceEqual(Enumerable.Range(0,5)),"tab resumes partial row and cursor");for(int i=4;i<100;i++)h.Tick(i);Check(h.Completed.Count==70,"offscreen rows finish");work=h.Work;h.Tick(101);Check(h.Work==work,"completed list does no construction");
  var priority=new SchedulerHarness(70);priority.Target(60,1);Check(priority.Completed.SequenceEqual(new[]{0,1}),"focus request does not reorder fixed build sequence");for(int i=2;i<100;i++)priority.Tick(i);Check(priority.Completed.Distinct().Count()==70,"focus request preserves every resident row");
  var scrolling=new SchedulerHarness(70);var still=new SchedulerHarness(70);
  for(int frame=1;frame<=12;frame++){scrolling.Target((frame*23)%70,frame);still.Tick(frame);Check(scrolling.Work==still.Work&&scrolling.Completed.SequenceEqual(still.Completed),"viewport/focus movement preserves exact partial progress frame "+frame);}
  for(int frame=13;frame<100;frame++){scrolling.Tick(frame);still.Tick(frame);}Check(scrolling.Completed.SequenceEqual(still.Completed)&&scrolling.Completed.Count==70,"all rows finish once in unchanged order after viewport movement");
  bool target=true,tick=true;int syncs=0;var toggle=new Toggle();toggle.Children.Add(new OverviewToggleVisual{Sync=v=>{tick=v;syncs++;}});toggle.Children.Add(new EnablementBorderTint());
  var text=new TMP_Text{color=new Color(9)};var icon=new OverviewIconGraphic{color=new Color(8)};var marks=new ProfilePendingMarkers();var binding=new OverviewEnablementBinding{Mod=new OverviewMod{Loaded=true},Target=()=>target,Checkbox=toggle};binding.Children.AddRange(new UnityEngine.Component[]{toggle,text,icon,marks});binding.Capture();binding.Refresh(null);
  target=false;binding.Refresh(null);Check(!toggle.isOn&&!tick,"programmatic batch toggle updates custom tick");Check(text.color.Equals(OverviewUi.Muted),"disabled text muted");target=true;binding.Refresh(null);Check(toggle.isOn&&tick,"failure/model rollback restores custom tick");Check(text.color.Equals(new Color(9)),"original text color restored");Check(toggle.Notify==0,"synchronization never recursively invokes model change");tick=false;toggle.isOn=false;int before=marks.Calls;binding.Refresh(null);Check(tick&&toggle.isOn,"failed direct click restores visual even when model target is unchanged");Check(marks.Calls==before,"unchanged row skips marker and color writes");
  var pending=new ProfileItemPending{Changing=true};binding.Refresh(pending);Check(toggle.targetGraphic!.color.Equals(ModEnablementPresentation.Changing),"changed pending state refreshes border");
  Check(ResidentBuildOrder.Create(5,2).SequenceEqual(new[]{2,3,1,4,0}),"fixed middle-out order retained");
  Console.WriteLine("RESULT "+n+" runtime checks passed");
 }
}
namespace ModConfigEnhance.Overview {
 partial class SchedulerHarness {
  class VirtualRow {internal int Id,Steps=6;}
  bool _disposed=false,_opened=true,_building=false,_infrastructureLoading=false,_residentPending=true;
  internal bool Visible=true;bool ListVisible=>_opened&&Visible;
  readonly List<VirtualRow> _buildOrder=new();int _buildCursor;
  (VirtualRow Row,int Column)? _layerFocus=null;
  readonly RowBuildBudget budget=new(1000,16);int frame;internal int Work;internal List<int> Completed=new();
  internal SchedulerHarness(int count){for(int i=0;i<count;i++)_buildOrder.Add(new VirtualRow{Id=i});}
  void CaptureBuildOrder(){}
  bool BuildResidentRow(VirtualRow row,bool immediate=false){while(row.Steps>0){if(!budget.TryTake(frame,0))return false;row.Steps--;Work++;if(row.Steps==0)Completed.Add(row.Id);}return true;}
  internal void Tick(int f){frame=f;RefreshVirtualRows();}internal void Target(int id,int f){frame=f;RefreshVirtualRows(_buildOrder[id]);}
 }
}
