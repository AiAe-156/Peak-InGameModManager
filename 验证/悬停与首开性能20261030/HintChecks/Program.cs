using System;
using ModConfigEnhance.Overview;
int passed=0;
void Check(bool ok,string title) { if(!ok)throw new Exception(title);passed++; }
var lru=new HintLru<string,int>(2,StringComparer.OrdinalIgnoreCase);
lru.Set("A",1);lru.Set("B",2);Check(lru.TryGet("a",out int a)&&a==1,"comparer and promotion");
lru.Set("C",3);Check(!lru.TryGet("B",out _),"evict oldest");Check(lru.Count==2,"bounded");
lru.Set("A",9);Check(lru.TryGet("a",out a)&&a==9,"replace");
for(int i=0;i<10000;i++){lru.Set(i.ToString(),i);Check(lru.Count<=2,"bounded churn");}
lru.Clear();Check(lru.Count==0&&!lru.TryGet("A",out _),"generation clear");
var random=new Random(1234);
for(int run=0;run<5000;run++)
{
    int count=random.Next(1,1000);float limit=random.Next(0,5000);var heights=new float[count+1];
    for(int i=1;i<=count;i++)heights[i]=heights[i-1]+random.Next(10,100);
    int expected=1;for(int i=2;i<=count;i++)if(heights[i]<=limit)expected=i;
    int probes=0;int got=HintPrefix.Fit(count,limit,n=>{probes++;return heights[n];});
    Check(got==expected,"exact logical prefix");Check(probes<=10,"logarithmic probes");
}
Check(HintPrefix.Fit(0,0,_=>throw new Exception())==0,"empty");
var hover=new HoverDwell();hover.Enter("first",1);hover.Enter("last",1);
Check(HoverDwell.Delay==.200f,"shared 200ms delay");
Check(hover.Take(1.051f)==null,"old 50ms deadline no longer opens");
Check(hover.Take(1.199f)==null,"200ms dwell holds");Check(hover.Take(1.201f)=="last","latest transition after dwell");Check(hover.Take(1.3f)==null,"one emission");
hover.Enter("same",3);hover.Enter("same",3.15f);Check(hover.Take(3.199f)==null,"same target holds until original deadline");Check(hover.Take(3.201f)=="same","repeated same target does not starve deadline");
hover.Enter("gone",2);hover.Cancel();Check(hover.Take(2.3f)==null,"cancel survives deadline");
hover.Enter("old",4);hover.Enter("new",4.15f);Check(hover.Take(4.201f)==null,"different target restarts dwell");Check(hover.Take(4.351f)=="new","replacement opens after its own deadline");
hover.Enter("exit",5);hover.Cancel();hover.Enter("exit",5.1f);Check(hover.Take(5.201f)==null,"reenter after exit restarts dwell");Check(hover.Take(5.301f)=="exit","reentered target opens once");
Console.WriteLine($"RESULT {passed} passed");
