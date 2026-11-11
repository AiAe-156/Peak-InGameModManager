using ModConfigEnhance.Overview;
int n=0;void Check(bool b,string why){if(!b)throw new Exception(why);n++;}
StorePackage P(string name,string author="AiAeT",string repo="")=>new(){Name=name,Author=author,Version="1.1.5",Versions=new(){"1.1.5"},Homepage=repo};
var actual=P("BetterPlayerDistance");var m=new OverviewMod{PackageName="BetterPlayerDistance",RawName="Better Player Distance",Author="AiAeT",AuthorSource="legacy",PackageVersion="1.1.6"};
Check(new StoreIdentityIndex(new[]{actual}).Resolve(m)==actual,"screenshot original package exact unique, legacy author, local version newer");
m.SourceNamespace="AiAeT";m.SourceName=actual.Name;
Check(new StoreIdentityIndex(new[]{actual}).Resolve(m)==actual,"legacy source hint with exact unique package reverified");
m.SourceNamespace=m.SourceName="";m.Author="SomeoneElse";m.AuthorSource="manifest";
Check(new StoreIdentityIndex(new[]{actual}).Resolve(m)==null,"conflicting declared author blocks match");
m.Author="AiAeT";m.AuthorSource="legacy";
Check(new StoreIdentityIndex(new[]{actual,P(actual.Name,"OtherTeam")}).Resolve(m)==null,"multiple exact teams need explicit evidence");
m.PackageName="BetterPlayerDistanc";
Check(new StoreIdentityIndex(new[]{actual}).Resolve(m)==actual,"one-character typo and author agree");
m.SourceNamespace="AiAeT";m.SourceName=actual.Name;
Check(new StoreIdentityIndex(new[]{actual}).Resolve(m)==actual,"cached fuzzy identity remains confirmed on refresh");
m.SourceNamespace=m.SourceName="";
Check(new StoreIdentityIndex(new[]{actual,P("BetterPlayerDistances")}).Resolve(m)==null,"near-equal top fuzzy candidates require manual choice");
m.Author="Unknown";
Check(new StoreIdentityIndex(new[]{actual}).Resolve(m)==null,"fuzzy name without same author not automated");
m.PackageName="更好的玩家距离";m.RawName="更好的玩家距离";m.Author="AiAeT";
Check(new StoreIdentityIndex(new[]{actual}).Resolve(m)==null,"translated name not guessed into original package");
m.PackageName=actual.Name;m.RawName="Better Player Distance";m.HomepageUrl="https://github.com/Other/Repo";
Check(new StoreIdentityIndex(new[]{P(actual.Name,"AiAeT","https://github.com/AiAe/Repo")}).Resolve(m)==null,"repository conflict blocks exact match");
m.HomepageUrl="";
var shared=new[]{P(actual.Name,"AiAeT","https://github.com/Common/Repo"),P("OtherFork","ForkTeam","https://github.com/Common/Repo")};
Check(new StoreIdentityIndex(shared).Resolve(m)==null,"shared repository fork family stays manual");
Console.WriteLine($"PASS {n} production identity match checks");
namespace ModConfigEnhance{internal static class Loc{internal static string T(string a,string b,string c)=>b;}}
namespace ModConfigEnhance.Overview{
internal sealed class StorePackage{internal string Name="",Author="",Version="",Homepage="";internal bool Deprecated=false;internal List<string> Versions=new();}
internal sealed class OverviewMod{internal string IdentityConflict="",SourceNamespace="",SourceName="",HomepageUrl="",Author="",AuthorSource="",PackageName="",RawName="",PackageVersion="";}
internal static class ModSources{internal static bool UserChoice(OverviewMod m,string a,string b)=>false;internal static bool ExplicitManifestIdentity(OverviewMod m,string a,string b)=>false;internal static string SearchName(OverviewMod m)=>m.PackageName.Length>0?m.PackageName:m.RawName;}
internal static class ThunderstoreUpdateService{internal static bool TryParseSource(string s,out string ns,out string name){ns=name="";return false;}}
}
