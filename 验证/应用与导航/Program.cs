using Newtonsoft.Json.Linq;
Checks.Run();
partial class Checks
{
 sealed class PluginRecord { public string Path="",Guid="",Version="1.0.0"; public List<(string Guid,string Minimum)> Dependencies=new(); }
 sealed class Plan { public string PluginRoot=""; public Dictionary<string,bool> Files=new(),PackageRoots=new(); public Dictionary<string,string> PackageIdentities=new(); }
 static readonly Dictionary<string,List<PluginRecord>> plugins=new();
 static IEnumerable<string> SafeFiles(string root,string directory)=>Directory.GetFiles(directory,"*",SearchOption.AllDirectories);
 static string? OriginalDllIfPlugin(string file)=>file.EndsWith(".dll")?file:null;
 static string OriginalDll(string file)=>file;
 static string ExistingFile(string file)=>file;
 static void Observe(Plan p,string file){}
 static List<PluginRecord> ReadPlugins(string file)=>plugins.TryGetValue(file,out var value)?value:new();
 static bool TryPackageVersion(string text,out Version version)=>Version.TryParse(text,out version!);
 static int passed;
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);Console.WriteLine("PASS "+ ++passed+" "+why);}
 static bool Fails(Action check){try{check();return false;}catch(InvalidOperationException){return true;}}
 static string Package(string root,string folder,string name,string? team,params string[] dependencies)
 {
  string dir=Path.Combine(root,folder);Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"owner.dll"),"");
  var m=new JObject{["name"]=name,["version_number"]="1.0.0",["dependencies"]=new JArray(dependencies)};
  if(team!=null)m["namespace"]=team;
  File.WriteAllText(Path.Combine(dir,"manifest.json"),m.ToString());return dir;
 }
 internal static void Run()
 {
  string root=Path.Combine(AppContext.BaseDirectory,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string owner=Package(root,"Owner","Owner","Team","RTLC-XUnity_AutoTranslator-1.0.0");
  string translator=Package(root,"translated-folder","XUnity_AutoTranslator",null);
  string other=Package(root,"Other","Other","Team");
  var p=new Plan{PluginRoot=root};p.PackageRoots[other]=false;CheckPackageDependencies(p);Check(true,"unrelated existing missing package permits edit");
  p.PackageRoots[owner]=true;Check(Fails(()=>CheckPackageDependencies(p)),"newly enabled owner requires its provider");
  p.PackageIdentities[translator]="RTLC-XUnity_AutoTranslator";CheckPackageDependencies(p);Check(true,"confirmed physical package mapping resolves translated folder");
  p.PackageRoots.Clear();p.PackageRoots[translator]=false;Check(Fails(()=>CheckPackageDependencies(p)),"removing required provider blocked");
  p.PackageRoots.Clear();p.PackageRoots[owner]=false;p.PackageRoots[translator]=false;CheckPackageDependencies(p);Check(true,"owner and provider can disable together");
  string a=Path.Combine(owner,"owner.dll"),b=Path.Combine(translator,"owner.dll"),c=Path.Combine(other,"owner.dll");
  plugins[a]=new(){new(){Path=a,Guid="owner",Dependencies=new(){("translator","")}}};plugins[b]=new(){new(){Path=b,Guid="translator"}};
  plugins[c]=new(){new(){Path=c,Guid="legacy",Dependencies=new(){("missing","")}}};
  p.Files[c]=false;CheckDependencies(p);Check(true,"disabling defective legacy owner allowed");
  p.Files.Clear();p.Files[b]=false;Check(Fails(()=>CheckDependencies(p)),"disabling runtime provider blocked");
  p.Files[a]=false;CheckDependencies(p);Check(true,"runtime owner and provider disable together");
  p.Files.Clear();p.Files[a]=true;CheckDependencies(p);Check(true,"unrelated runtime defect ignored for valid enable");
  plugins[a][0].Dependencies.Add(("missing",""));Check(Fails(()=>CheckDependencies(p)),"new owner missing runtime dependency blocked");
  plugins[a][0].Dependencies.RemoveAt(1);plugins[a][0].Dependencies[0]=("translator","2.0.0");Check(Fails(()=>CheckDependencies(p)),"new owner version requirement enforced");
  p.Files.Clear();CheckDependencies(p);Check(true,"preexisting runtime version mismatch allowed unchanged");
 }
}
static class PluginFileNames{internal static bool IsBackupCompanion(string path)=>false;}
static class Loc{internal static string F(string en,string zh,string tw,params object[] args)=>string.Format(en,args);}
static class ThunderstoreUpdateService{internal static bool ValidPart(string v)=>!string.IsNullOrWhiteSpace(v);internal static bool TryParseSource(string v,out string team,out string name){team="";name="";return false;}}
static class PackageRules{internal static bool IsLoader(string v)=>v.StartsWith("BepInEx-");}
