using ModConfigEnhance.Overview;
using UnityEngine;

int passed=0;
void Check(bool value,string name) { if(!value)throw new Exception(name); Console.WriteLine("PASS "+name);passed++; }
Check(ResidentBuildOrder.Create(5,0).SequenceEqual(new[]{0,1,2,3,4}),"top origin stays sequential");
Check(ResidentBuildOrder.Create(7,3).SequenceEqual(new[]{3,4,2,5,1,6,0}),"middle origin expands down and up");
Check(ResidentBuildOrder.Create(4,3).SequenceEqual(new[]{3,2,1,0}),"bottom origin expands upward");
Check(ResidentBuildOrder.Create(0,0).Length==0,"empty list order");
for(int count=1;count<100;count++)for(int first=0;first<count;first++){var order=ResidentBuildOrder.Create(count,first);if(order.Length!=count || order.Distinct().Count()!=count || order.Min()!=0 || order.Max()!=count-1)throw new Exception("order coverage");}
Check(true,"4950 origin/count cases cover each row exactly once");
var budget=new RowBuildBudget(1000);
Check(budget.TryTake(1,0),"first row progresses");
Check(budget.TryTake(1,1),"second cheap row allowed");
Check(!budget.TryTake(1,1),"same frame third call blocked");
Check(budget.TryTake(2,10),"next frame resumes");
Check(!budget.TryTake(2,12),"2ms elapsed yields");
Check(budget.TryTake(2,12,true),"only explicit navigation target bypasses");
Check(!budget.TryTake(2,12),"navigation does not reset normal budget");
Check(budget.TryTake(3,50),"expensive prior frame does not starve next");
var rect=new RectTransform {anchorMin=new(0,1),anchorMax=new(0,1),pivot=new(0,1),sizeDelta=new(180,43),anchoredPosition=new(30,-40),rect=new(){height=43}};
var baseline=new SettingsEntryLayout(rect,20,1);
Check(baseline.Equals(new SettingsEntryLayout(rect,20,1)),"unchanged layout cached");
rect.localScale=new(1.2f,1.2f);rect.rotation=25;
Check(baseline.Equals(new SettingsEntryLayout(rect,20,1)),"hover scale and rotation ignored");
Check(!baseline.Equals(new SettingsEntryLayout(rect,20,2)),"language invalidates");
Check(!baseline.Equals(new SettingsEntryLayout(rect,21,1)),"configured font invalidates");
rect.anchoredPosition=new(40,-40);
Check(!baseline.Equals(new SettingsEntryLayout(rect,20,1)),"base position invalidates");
rect.anchoredPosition=new(30,-40);rect.rect=new(){height=50};
Check(!baseline.Equals(new SettingsEntryLayout(rect,20,1)),"parent resize height invalidates");
var flow=new PublicationHarness();
flow.Pending=true;flow.Loading=true;flow.Flush();Check(flow.Renders==0 && flow.Pending,"scan blocks commit");
flow.Loading=false;flow.Opened=false;flow.Flush();Check(flow.Renders==0 && flow.Pending,"closed page retains result");
flow.Opened=true;flow.Editing=true;flow.Flush();Check(flow.Renders==0 && flow.Pending,"text editing postpones");
flow.Editing=false;flow.Dragging=true;flow.Flush();Check(flow.Renders==0 && flow.Pending,"drag postpones");
flow.Dragging=false;flow.Speed=2;flow.Flush();Check(flow.Renders==0 && flow.Pending,"scroll postpones");
flow.Speed=0;flow.Reject=true;flow.Flush();Check(flow.Renders==0 && flow.Pending,"rejected render keeps pending");
flow.Reject=false;flow.Flush();Check(flow.Renders==1 && !flow.Pending,"result commits once");
flow.Flush();Check(flow.Renders==1,"no duplicate commit next tick");
flow.Pending=true;flow.Flush();Check(flow.Renders==2,"later refresh can commit");
Console.WriteLine($"RESULT {passed} runtime checks passed (narrow Unity stubs, not in-game timings)");

namespace UnityEngine {
 public readonly record struct Vector2(float x,float y);
 public struct Rect {public float height;}
 public class RectTransform {public Vector2 anchorMin,anchorMax,pivot,sizeDelta,anchoredPosition,localScale;public float rotation;public Rect rect;}
}
partial class PublicationHarness {
 private bool _infrastructureRedraw,_infrastructureLoading,_opened=true,_building=false;
 private bool IsEditingText; private object? _dragged;
 private readonly Scroll _scroll=new();
 public int Renders; public bool Reject;
 public bool Pending {get=>_infrastructureRedraw;set=>_infrastructureRedraw=value;}
 public bool Loading {set=>_infrastructureLoading=value;}
 public bool Opened {set=>_opened=value;}
 public bool Editing {set=>IsEditingText=value;}
 public bool Dragging {set=>_dragged=value?new object():null;}
 public float Speed {set=>_scroll.velocity.sqrMagnitude=value;}
 public void Flush()=>FlushInfrastructureRedraw();
 private void Render() {if(Reject)return;_infrastructureRedraw=false;Renders++;}
 private class Scroll {public Velocity velocity=new();}
 private class Velocity {public float sqrMagnitude;}
}
