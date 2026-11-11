using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;
int checks=0;
void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);checks++;}
string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../runs/"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));Directory.CreateDirectory(root);
string ledger=Path.Combine(root,"A.json"),other=Path.Combine(root,"B.json");
string Put(string path,string text="payload"){string p=Path.Combine(root,path);Directory.CreateDirectory(Path.GetDirectoryName(p)!);File.WriteAllText(p,text);return p;}
string Manifest(string folder,string team,string name,string version="1.0.0"){Put(folder+"/manifest.json",JsonConvert.SerializeObject(new{ @namespace=team,name,version_number=version}));return Put(folder+"/library.dll");}
ShopInstalled.Snapshot Read(params ShopInstalled.Evidence[] rows)=>ShopInstalled.Build(root,"A",ledger,rows);
string core=Manifest("core/MonoDetour-MonoDetour","MonoDetour","MonoDetour","0.7.16");
string patch=Manifest("patchers/MonoDetour-MonoDetour_BepInEx_5","MonoDetour","MonoDetour_BepInEx_5","0.7.16");
var snap=Read();Check(snap.Has("MonoDetour-MonoDetour"),"core production recognition");Check(snap.Has("MonoDetour-MonoDetour_BepInEx_5"),"patcher production recognition");Check(snap.Updates("MonoDetour-MonoDetour","0.7.17"),"core update filter");Check(snap.Updates("MonoDetour-MonoDetour_BepInEx_5","0.7.17"),"patcher update filter");
string plugin=Manifest("plugins/custom/translated folder","LengSword","PeakStatsEx","1.5.5");Check(Read().Version("LengSword-PeakStatsEx")=="1.5.5","custom directory explicit manifest");
string plain=Put("plugins/custom2/normal.dll");var confirmed=new ShopInstalled.Evidence{Key="Author-Normal",Version="2.0.0",Path=plain};Check(Read(confirmed).Has(confirmed.Key),"confirmed ordinary plugin no manifest");
File.Move(plain,plain+".disabled-by-mce");confirmed.Path=plain+".disabled-by-mce";Check(Read(confirmed).Packages[confirmed.Key].Single().Disabled,"disabled file remains installed");
string cached=Manifest("temporary mods/custom","Author","Cached");Check(Read().Packages["Author-Cached"].Single().Cached,"cache distinguished actual location");
File.Delete(plugin);Check(!Read().Has("LengSword-PeakStatsEx"),"missing payload not installed");
Put("plugins/bad/manifest.json","{invalid");Put("plugins/bad/a.dll");Check(!Read().Has("bad-package"),"invalid manifest ignored");
Manifest("plugins/Wrong-Ambiguous","Author","Ambiguous");Check(!Read().Has("Author-Ambiguous"),"conflicting folder identity rejected");
Manifest("plugins/missing","Author","Missing");File.Delete(Path.Combine(root,"plugins/missing/library.dll"));Check(!Read().Has("Author-Missing"),"manifest alone not installed");
Manifest("plugins/duplicate1","Author","Duplicate");Manifest("plugins/duplicate2","Author","Duplicate","2.0.0");Check(Read().Packages["Author-Duplicate"].Count==2,"duplicate locations retained");Check(!Read().Updates("Author-Duplicate","1.5.0"),"mixed duplicate versions no false update");
var conflict=new ShopInstalled.Evidence{Key="Other-Package",Version="1.0.0",Path=confirmed.Path};Check(!Read(confirmed,conflict).Has(confirmed.Key),"ambiguous confirmed claims rejected");
string owned=Put("plugins/owned/p.dll");void Ledger(string file,string key)=>File.WriteAllText(file,JsonConvert.SerializeObject(new ShopLedger{Packages=new(){new ShopPackage{Key=key,Version="1.0.0",Files=new(){new PackFile{Path="plugins/owned/p.dll"}}}}}));
Ledger(ledger,"Author-Owned");Check(Read().Has("Author-Owned"),"current ledger real payload");File.Delete(owned);Check(!Read().Has("Author-Owned"),"same-pack removal refresh drops stale ledger");
Put("plugins/owned/p.dll");Ledger(other,"Other-Owned");snap=ShopInstalled.Build(root,"B",other,Array.Empty<ShopInstalled.Evidence>());Check(!snap.Has("Author-Owned")&&snap.Has("Other-Owned")&&snap.Context=="B","cross-pack receipt isolation");
File.WriteAllText(other,"{}");snap=ShopInstalled.Build(root,"B",other,Array.Empty<ShopInstalled.Evidence>());Check(!snap.Has("Other-Owned"),"same pack ledger change refresh");
File.Delete(core);Check(!Read().Has("MonoDetour-MonoDetour"),"core removed payload refresh");
Put("plugins/typed/manifest.json","{\"namespace\":{},\"name\":[],\"version_number\":{}}");Put("plugins/typed/a.dll");Check(ShopInstalled.Manifest(Path.Combine(root,"plugins/typed/manifest.json"),"plugins")==null,"wrong manifest field types rejected");
string nested=Manifest("plugins/parent/child","Author","Child");Put("plugins/parent/manifest.json",JsonConvert.SerializeObject(new{ @namespace="Author",name="Parent",version_number="1.0.0"}));Check(!Read().Has("Author-Parent")&&Read().Has("Author-Child"),"nested package payload not stolen by parent");
string stale=Manifest("plugins/stale","Author","Stale","2.0.0");File.WriteAllText(ledger,JsonConvert.SerializeObject(new ShopLedger{Packages=new(){new ShopPackage{Key="Author-Stale",Version="1.0.0",Files=new(){new PackFile{Path="plugins/stale/library.dll"}}}}}));Check(Read().Version("Author-Stale")=="2.0.0","real manifest version supersedes stale receipt");
var mapped=new ShopInstalled.Evidence{Key="Author-Stale",Version="1.0.0",Path=stale};Check(Read(mapped).Version("Author-Stale")=="2.0.0","manifest version supersedes old confirmed metadata");
File.WriteAllText(ledger,"not json");Check(Read().Has("Author-Stale"),"broken ledger does not hide manifest packages");
string legacy=Put("plugins/translated legacy/legacy.dll");Put("plugins/translated legacy/manifest.json","{\"name\":\"Legacy\",\"version_number\":\"1.2.3\"}");var legacySource=new ShopInstalled.Evidence{Key="Author-Legacy",Path=legacy,Version="1.0.0"};Check(Read(legacySource).Version("Author-Legacy")=="1.2.3","confirmed source resolves authorless manifest in custom directory");Check(!Read().Has("Author-Legacy"),"unconfirmed authorless custom package not guessed");
var wrongLegacy=new ShopInstalled.Evidence{Key="Other-Legacy",Path=legacy,Version="1.0.0"};Check(!Read(legacySource,wrongLegacy).Has("Author-Legacy"),"ambiguous custom source hints rejected");
// Read only: exact two user-authorized local manifests. No inventory of the live game.
string game="F:/Games/SteamLibrary/steamapps/common/PEAK/BepInEx";
var local=new[]{("core/MonoDetour-MonoDetour/manifest.json","core"),("patchers/MonoDetour-MonoDetour_BepInEx_5/manifest.json","patchers")}.Select(x=>new{file=Path.Combine(game,x.Item1),package=ShopInstalled.Manifest(Path.Combine(game,x.Item1),x.Item2)}).ToList();
Check(local.All(x=>x.package!=null),"actual machine manifests parse production function");
File.WriteAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../local-manifests-readonly.json")),JsonConvert.SerializeObject(local.Select(x=>new{x.file,key=x.package!.Key,version=x.package.Version,area=x.package.Area,path=x.package.Path,readUtc=DateTime.UtcNow}),Formatting.Indented));
Console.WriteLine($"PASS {checks} production checks; isolated fixtures {root}");
