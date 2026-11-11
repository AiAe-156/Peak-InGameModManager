class Program
{
 static int n;static void Check(bool b,string why){if(!b)throw new Exception(why);n++;Console.WriteLine("PASS "+why);}
 static void Main(){
 var h=new Harness();var a=new OverviewMod{Id="A"};var b=new OverviewMod{Id="B"};h.Mods.AddRange(new[]{a,b});
 object rowA=new(),rowB=new();h.Rows.AddRange(new[]{rowA,rowB});h.Scroll=.37f;
 for(int i=0;i<100;i++)h.Click(a,i%2==0);
 Check(h.Patches==100,"100 toggles patch state only");Check(h.Repo.Calls==100,"100 model operations");Check(ReferenceEquals(h.Rows[0],rowA)&&ReferenceEquals(h.Rows[1],rowB),"row identity retained");Check(h.Scroll==.37f,"scroll retained");
 h.Selection.UnionWith(new[]{"A","B"});h.Click(a,false);Check(h.Repo.Last.Count==2,"selected row batch updates selection");
 h.Selection.Clear();h.Selection.Add("B");h.Click(a,true);Check(h.Repo.Last.Count==1&&h.Repo.Last[0]==a,"unselected checkbox affects itself");
 h.Repo.Throw=true;int before=h.Patches;h.Click(a,false);Check(h.Patches==before+1,"failure restores checkbox from model without rebuild");Check(h.Message.Contains("fixture"),"failure reason retained");
 h.Building=true;before=h.Patches;h.Click(a,true);Check(h.Patches==before,"build guard retained");
 Console.WriteLine("RESULT "+n+" production-toggle checks passed");
 }
}
class OverviewMod {public string Id="";}
class Repo {public int Calls;public bool Throw;public List<OverviewMod> Last=new();public void SetPlannedEnabled(List<OverviewMod> mods,bool value){Calls++;if(Throw)throw new Exception("fixture");Last=mods;}}
static class Loc {public static string T(string e,string z,string t)=>e;public static string LocalizeMessage(string s)=>s;}
partial class Harness
{
 internal readonly List<OverviewMod> Mods=new();internal readonly List<object> Rows=new();internal float Scroll;
 internal readonly HashSet<string> Selection=new();internal Repo Repo=new();internal int Patches;internal string Message="";internal bool Building;
 bool _building=>Building;bool _disposed=false;HashSet<string> _selected=>Selection;Repo _repo=>Repo;
 void _status(string text)=>Message=text;static string T(string e,string z)=>e;
 List<OverviewMod> Selected(OverviewMod? fallback=null)=>Mods.Where(m=>Selection.Contains(m.Id)).ToList();
 void RefreshEnablementRows()=>Patches++;
 public void Click(OverviewMod mod,bool enabled)=>Toggle(mod,enabled);
}
