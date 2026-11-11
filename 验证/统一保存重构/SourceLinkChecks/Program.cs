using ModConfigEnhance.Overview;
int n=0;void Check(bool b,string label){if(!b)throw new Exception(label);n++;}
var m=new OverviewMod{HomepageUrl="https://github.com/team/project",SourceNamespace="Team",SourceName="Package"};
Check(SourceUi.HomepageHint(m).Contains("待定")&&SourceUi.HomepageHint(m).Contains("GitHub:"),"GitHub alone remains pending with project link");
m.SourceConfirmed=true;
Check(SourceUi.HomepageHint(m).Contains("https://thunderstore.io/c/peak/p/Team/Package/")&&SourceUi.HomepageHint(m).Contains("https://github.com/team/project"),"confirmed source shows both links");
m.HomepageUrl="";ShopCatalog.Items.Add(new(){Team="Team",Name="Package",Homepage="https://github.com/team/from-catalog"});
Check(SourceUi.GitHubUrl(m).EndsWith("from-catalog"),"catalog project GitHub retained separately");
m.Local=true;
Check(SourceUi.HomepageHint(m).StartsWith("本地")&&!SourceUi.HomepageHint(m).Contains("thunderstore.io"),"local source does not advertise store classification");
m.Local=false;m.SourceConfirmed=false;m.SourceName="none";
Check(SourceUi.HomepageHint(m)=="未识别到来源（待定）","no link remains pending");
m.HomepageUrl="https://github.com.evil.invalid/team/project";
Check(SourceUi.GitHubUrl(m)=="","lookalike domain rejected");
Console.WriteLine($"PASS {n} source-link checks");
namespace ModConfigEnhance{internal static class Loc{internal static string T(string a,string b,string c)=>b;}}
namespace ModConfigEnhance.Overview{
internal sealed class OverviewMod{internal string HomepageUrl="",SourceNamespace="",SourceName="";internal bool SourceConfirmed,Local;}
internal sealed class Listing{internal string Team="",Name="",Homepage="";}
internal static class ShopCatalog{internal static List<Listing> Items=new();internal static List<Listing> Cached()=>Items;}
internal static class ModSources{internal static bool ExplicitLocal(OverviewMod m)=>m.Local;}
internal static class ThunderstoreUpdateService{internal static string HomeUrl(string ns,string name)=>$"https://thunderstore.io/c/peak/p/{ns}/{name}/";}
}
