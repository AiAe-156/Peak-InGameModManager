using BepInEx;
using BepInEx.Bootstrap;
using MCE.UpdateWorker;
using ModConfigEnhance;
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;

string project=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../.."));
if (args.Contains("defaults-audit")) { await PublicationDefaultsAudit.Run(project); return; }
if (args.Contains("profile-integration")) { ProfileIntegrationAudit.Run(project); return; }
if (args.Contains("paired-audit")) { await PairedProfileAudit.Run(project); return; }
if (args.Contains("cache-check"))
{
    string fixture=Path.Combine(project,"验证","RealPackRoundTrip","cache-"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
    Paths.BepInExRootPath=Path.Combine(fixture,"BepInEx"); Paths.ManagedPath=Path.GetFullPath(Path.Combine(project,"../..","PEAK_Data","Managed"));
    Directory.CreateDirectory(PackLibrary.Home);
    var record=new PackRecord {Id=Guid.NewGuid().ToString("N"),Name="缓存测试",Content="revisions/"+Guid.NewGuid().ToString("N"),ModifiedUtc="first"};
    string root=StoredPackDetails.Content(record);Directory.CreateDirectory(Path.Combine(root,"plugins","Test"));
    string dll=Path.Combine(root,"plugins","Test","ModConfigEnhance.dll");
    File.Copy(Path.Combine(project,"bin","Release","netstandard2.1","ModConfigEnhance.dll"),dll);
    File.WriteAllText(Path.Combine(root,"plugins","Test","manifest.json"),"{\"version_number\":\"2.0.0\"}");
    record.Files=PackStorage.Inventory(root);
    var index=new PackIndex();index.Packs.Add(record);string indexPath=Path.Combine(PackLibrary.Home,"index.json");Files.Save(indexPath,index);
    int passed=0;void Assert(bool good,string label){if(!good)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
    var firstIndex=PackLibrary.Read();Assert(ReferenceEquals(firstIndex,PackLibrary.Read()),"unchanged library index is not deserialized twice");
    var taskFile=new PackTask{Id=Guid.NewGuid().ToString("N"),State="preview"};Files.Save(PackLibrary.TaskPath,taskFile);
    var firstTask=PackLibrary.Task();Assert(ReferenceEquals(firstTask,PackLibrary.Task()),"unchanged operation task reuses its parsed model");
    string indexBefore=File.ReadAllText(indexPath),taskBefore=File.ReadAllText(PackLibrary.TaskPath);
    var clock=System.Diagnostics.Stopwatch.StartNew();var cold=StoredPackDetails.LoadAsync(record);var loaded=await cold;clock.Stop();Console.WriteLine("COLD ms="+clock.ElapsedMilliseconds);
    Assert(loaded.Report.ModCount==1&&loaded.Report.FolderVersions.Count>0,"cold load rebuilds legacy mod and version summary");
    Assert(ReferenceEquals(cold,StoredPackDetails.LoadAsync(record)),"memory cache reuses the same revision task");
    // A new service instance is not needed: remove only its memory entry, not its disk cache.
    StoredPackDetails.ClearMemoryForChecks(record);
    string moved=dll+".test-held";File.Move(dll,moved);
    var disk=await StoredPackDetails.LoadAsync(record);Assert(disk.Report.ModCount==1,"persistent cache supplies the report without reading the temporarily unavailable assembly");
    File.Move(moved,dll);
    Assert(File.ReadAllText(indexPath)==indexBefore&&File.ReadAllText(PackLibrary.TaskPath)==taskBefore,"detail cache never changes library index or operation task");
    index.Revision++;index.Packs[0].Name="修改后的名称";Files.Save(indexPath,index);
    Assert(!ReferenceEquals(firstIndex,PackLibrary.Read())&&PackLibrary.Read().Packs[0].Name=="修改后的名称","changed library file invalidates the parsed index cache");
    Console.WriteLine("PASS "+passed+" real detail-cache checks; "+fixture);return;
}
string live=Path.GetFullPath(Path.Combine(project,"../..","BepInEx"));
string sourceConfig=Path.Combine(live,"config","ModConfigEnhance"), profileId="8646e4d170df4d61bb1a3768f4662e66";
string run=Path.Combine(project,"验证","RealPackRoundTrip","runs",DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
string game=Path.Combine(run,"publisher"), bep=Path.Combine(game,"BepInEx"), output=Path.Combine(run,"output");
Directory.CreateDirectory(output);
var sourceStates=new Dictionary<string,(long Size,DateTime Time)>(StringComparer.OrdinalIgnoreCase);
void CopyFile(string source,string destination,bool replace=false) { if(source.StartsWith(live+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) {var state=new FileInfo(source);sourceStates.TryAdd(source,(state.Length,state.LastWriteTimeUtc));} Directory.CreateDirectory(Path.GetDirectoryName(destination)!); if(!File.Exists(destination)||replace) File.Copy(source,destination,replace); }
void CopyTree(string source,string destination) { if(!Directory.Exists(source)) return; foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)) { if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0) throw new IOException("Link:"+file); CopyFile(file,Path.Combine(destination,Path.GetRelativePath(source,file))); } }
string PackageRoot(string file) { string directory=Path.GetDirectoryName(file)!; for(var d=new DirectoryInfo(directory);d!=null&&d.FullName.StartsWith(live+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);d=d.Parent) if(File.Exists(Path.Combine(d.FullName,"manifest.json"))) return d.FullName; return directory; }
Paths.BepInExRootPath=bep; Paths.PluginPath=Path.Combine(bep,"plugins"); Paths.ConfigPath=Path.Combine(bep,"config"); Paths.ManagedPath=Path.GetFullPath(Path.Combine(live,"..","PEAK_Data","Managed"));
Loc.RootDirectory=Path.Combine(Paths.ConfigPath,"ModConfigEnhance");
CopyTree(Path.Combine(live,"core"),Path.Combine(bep,"core"));
foreach(string file in new[]{"winhttp.dll","doorstop_config.ini",".doorstop_version"}) if(File.Exists(Path.Combine(live,"..",file))) CopyFile(Path.Combine(live,"..",file),Path.Combine(game,file));
foreach(string file in new[]{"publisher.json","sources.json","store-catalog.json"}) if(File.Exists(Path.Combine(sourceConfig,"runtime",file))) CopyFile(Path.Combine(sourceConfig,"runtime",file),Path.Combine(Loc.RootDirectory,"runtime",file));
CopyTree(Path.Combine(sourceConfig,"language"),Path.Combine(Loc.RootDirectory,"language"));
string profileDirectory=Path.Combine(Loc.RootDirectory,"profiles",profileId);
CopyTree(Path.Combine(sourceConfig,"profiles",profileId),profileDirectory);
var locations=JArray.Parse(File.ReadAllText(Path.Combine(project,"验证","RealPackRoundTrip","input","target-locations.json")));
var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foreach(JObject location in locations)
{
    string file=location.Value<string>("File")!;
    if(file.Contains("[优化]更好的语音修复CN-1.1.6",StringComparison.OrdinalIgnoreCase)) continue; // User selected the distinct CN-1.1.8 implementation.
    string root=PackageRoot(file); if(!roots.Add(root)) continue;
    if(!File.Exists(file))
    {
        string previous=Path.Combine(project,"验证","RealPackRoundTrip","runs","20261024_182708_810","publisher","BepInEx");
        string saved=Path.Combine(previous,Path.GetRelativePath(live,file));
        if(!File.Exists(saved)) throw new FileNotFoundException("Source target and previous acceptance fixture are both missing",file);
        var savedRoot=new DirectoryInfo(Path.GetDirectoryName(saved)!);
        while(savedRoot.Parent!=null&&!File.Exists(Path.Combine(savedRoot.FullName,"manifest.json"))) savedRoot=savedRoot.Parent;
        if(!savedRoot.FullName.StartsWith(previous+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new IOException("Previous fixture package root escaped");
        CopyTree(savedRoot.FullName,Path.Combine(bep,Path.GetRelativePath(previous,savedRoot.FullName)));
        Console.WriteLine("FIXTURE fallback for missing live target: "+location.Value<string>("Guid"));
    }
    else CopyTree(root,Path.Combine(bep,Path.GetRelativePath(live,root)));
}
var archive=JArray.Parse(File.ReadAllText(Path.Combine(project,"验证","RealPackRoundTrip","input","archive-locations.json"))).OfType<JObject>().First();
using(var zip=ZipFile.OpenRead(archive.Value<string>("Zip")!))
{
    string entry=archive.Value<string>("Entry")!, prefix=entry[..(entry.LastIndexOf('/')+1)];
    foreach(var item in zip.Entries.Where(e=>e.FullName.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&e.Name.Length>0))
    { string target=Path.Combine(game,item.FullName); Directory.CreateDirectory(Path.GetDirectoryName(target)!); if(!File.Exists(target)) item.ExtractToFile(target); }
}
CopyTree(Path.Combine(live,"patchers"),Path.Combine(bep,"patchers")); // Selection below admits only package-owned dependencies.
CopyTree(Path.Combine(live,"monomod"),Path.Combine(bep,"monomod"));
var copiedDependencies=new HashSet<string>(StringComparer.OrdinalIgnoreCase); bool more;
do
{
    more=false;
    foreach(string manifest in Directory.GetFiles(bep,"manifest.json",SearchOption.AllDirectories))
    foreach(string declaration in JObject.Parse(File.ReadAllText(manifest))["dependencies"]?.Values<string>()??Array.Empty<string>())
    {
        int dash=declaration.LastIndexOf('-'); if(dash<=0) continue; string key=declaration[..dash]; if(!copiedDependencies.Add(key)) continue;
        string cache=Path.Combine(live,key); if(Directory.Exists(cache)) {CopyTree(cache,Path.Combine(bep,key));more=true;}
    }
}while(more);
var repo=new OverviewRepository();
var profile=JsonConvert.DeserializeObject<OverviewProfile>(File.ReadAllText(Path.Combine(profileDirectory,"profile.json")))!;
profile.directory=profileDirectory;
profile.config=JsonConvert.DeserializeObject<OverviewConfigSnapshot>(File.ReadAllText(Path.Combine(profileDirectory,"config-values.json")))!;
repo.Profiles.Items.Add(profile); repo.State=profile.state;
var initial=PackPublisher.Inventory(repo);
var self=initial.Single(m=>m.Guid==Plugin.PluginGuid);
foreach(string line in File.ReadAllLines(Path.Combine(project,"bin","Release","netstandard2.1","release-files.txt"))) { var parts=line.Split('|'); CopyFile(parts[0],Path.Combine(self.PackageRoot,parts[1]),true); }
var loadedHostGuids=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Plugin.PluginGuid};
bool foundHostDependency;
do
{
    foundHostDependency=false;
    foreach(var mod in initial.Where(m=>loadedHostGuids.Contains(m.Guid)).ToList())
        foreach(var dependency in mod.RuntimeDependencies.Where(d=>!d.Optional)) foundHostDependency|=loadedHostGuids.Add(dependency.Id);
}while(foundHostDependency);
foreach(var mod in initial.Where(m=>loadedHostGuids.Contains(m.Guid)))
    Chainloader.PluginInfos[mod.Guid]=new Info { Location=mod.Location, Metadata=new Metadata {GUID=mod.Guid,Name=mod.Name,Version=mod.Guid==Plugin.PluginGuid?new Version(2,0,0):Version.TryParse(mod.PluginVersion,out var version)?version:new Version(0,0,0)}, Dependencies=mod.RuntimeDependencies.Select(d=>new BepInDependency {DependencyGUID=d.Id,MinimumVersion=Version.TryParse(d.MinimumVersion,out var v)?v:null,Flags=d.Optional?BepInDependency.DependencyFlags.SoftDependency:BepInDependency.DependencyFlags.HardDependency}).ToList() };
OverviewCatalogue.InvalidateCaches();
var mods=PackPublisher.Inventory(repo);
Console.WriteLine($"SOURCE rows={profile.mods.Count}, unique={profile.mods.Select(m=>m.guid).Distinct(StringComparer.OrdinalIgnoreCase).Count()}, parameters={profile.config.values.Count}, inventory={mods.Count}, run={run}");
var request=new PublishRequest { Name="MCEPack_6b679869",DisplayName=profile.name,Version=profile.state.pack.version,DefaultProfile=profile.id,Profiles=new(){profile.id},Description="AIAE lightweight modpack",Icon=Path.Combine(project,"icon.png"),OutputDirectory=output,IncludeBootstrap=false,SigningKeyPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ModConfigEnhance","signing","author-key.json"),Languages=new(){"zh-cn"},ExcludedExtras=new(StringComparer.OrdinalIgnoreCase){{"BepInEx/patchers/PEAKERRpcInfoNativeSkip","Dedicated PEAKER/PEAKERRpcInfo patcher; target mods are not supplied/enabled / 未收录目标模组的专用补丁，排除"}} };
foreach(var mod in mods) request.InlineRoots.Add(mod.PackageRoot);
// Inline selection is a publication choice, not an extra-content selection.
request.InlineRoots.Clear();
var selected=PackPublisher.SelectInventory(repo,request,mods);
foreach(var mod in selected) { request.InlineRoots.Add(mod.PackageRoot); Console.WriteLine("SELECT "+mod.Guid+" "+mod.PluginVersion+" "+mod.PackageRoot); }
if(args.Contains("inventory")) return;
// Independent negative entry; original lightweight round-trip below is unchanged.
if(args.Contains("legacy-negative")) { await LegacyNegativeAudit.Run(repo,request,selected,sourceStates); return; }
int failures=0;
foreach(PublishMode mode in new[]{PublishMode.Local,PublishMode.Thunderstore})
{
    request.Mode=mode;
    if(mode==PublishMode.Thunderstore)
    {
        var storePackage=selected.Single(m=>m.Guid=="com.github.PEAKModding.PEAKLib.Items");
        request.InlineRoots.Remove(storePackage.PackageRoot);
        request.Dependencies[storePackage.PackageRoot]="PEAKModding-PEAKLib_Items-1.6.2";
    }
    Console.WriteLine("EXPORT "+mode);
    try
    {
        string zip=args.Length==3&&args[0]=="verify"?args[mode==PublishMode.Local?1:2]:await PackPublisher.PublishAsync(repo,request.Snapshot());
        Console.WriteLine("ZIP "+zip);
        using(var checkZip=ZipFile.OpenRead(zip))
        {
            if(mode==PublishMode.Thunderstore)
            { var manifest=JObject.Parse(new StreamReader(checkZip.GetEntry("manifest.json")!.Open()).ReadToEnd()); if(!System.Text.RegularExpressions.Regex.IsMatch(manifest.Value<string>("name")!,"^[A-Za-z0-9_]+$")||checkZip.GetEntry("icon.png")==null||checkZip.GetEntry("README.md")==null) throw new Exception("Store root format invalid"); Console.WriteLine("PASS store manifest ASCII name, icon.png, README.md"); }
            else if(checkZip.Entries.Any(e=>e.FullName.StartsWith("BepInEx/core/",StringComparison.OrdinalIgnoreCase))) throw new Exception("Local inventory package unexpectedly contains core");
        }
        string receiver=Path.Combine(run,"receiver-"+mode,"BepInEx"); Directory.CreateDirectory(Path.Combine(receiver,"plugins"));
        CopyTree(Path.Combine(bep,"core"),Path.Combine(receiver,"core"));
        var hostRequest=PackLibrary.Request("preview-adopt",repo);
        var hosts=hostRequest.HostPlugins; var hostRoots=hostRequest.HostRoots;
        var expectedHosts=new[]{Plugin.PluginGuid,"com.github.PEAKModding.PEAKLib.ModConfig","com.github.PEAKModding.PEAKLib.UI","com.github.PEAKModding.PEAKLib.Core"};
        if(expectedHosts.Any(g=>hosts.All(h=>!h.Guid.Equals(g,StringComparison.OrdinalIgnoreCase)))) throw new Exception("Production host discovery missed a required foundation");
        if(hosts.Any(h=>h.Guid=="gravydevsupreme.xunity.autotranslator")) throw new Exception("Optional translation integration became a fixed host");
        foreach(string root in hostRoots) CopyTree(Path.Combine(bep,root),Path.Combine(receiver,root));
        Console.WriteLine("PASS production PackLibrary.Request protects MCE and all transitive hard dependencies");
        Console.WriteLine("HOST "+string.Join(", ",hosts.Select(h=>h.Guid+"="+h.Version)));
        var storage=new PackStorage(receiver);
        var coreBefore=Files.Inventory(Path.Combine(receiver,"core"));
        var hostBefore=hostRoots.SelectMany(root=>Files.Inventory(Path.Combine(receiver,root)).Select(p=>root+"/"+p.Key+"|"+p.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported=await PackImports.Preview(storage,new PackRequest {Source=zip,HostPlugins=hosts,HostRoots=hostRoots},new(),mode==PublishMode.Local?new NoNetwork():null);
        Console.WriteLine("IMPORT files="+imported.Proposed.Files.Count+", problems="+imported.Proposed.Problems.Count);
        foreach(string problem in imported.Proposed.Problems) Console.WriteLine("BLOCK "+problem);
        { var rep=imported.Report; Console.WriteLine("REPORT "+rep.Verdict+" | "+rep.Name+" "+rep.Version+" | mods="+rep.ModCount+" enabled="+rep.EnabledCount+" downloads="+rep.DownloadCount+" files="+rep.FileCount); Console.WriteLine("REPORT headline: "+rep.Headline);
          foreach(var l in rep.Lines.Where(l=>l.Section!="mod")) Console.WriteLine("REPORT ["+l.Section+"/"+l.State+"] "+l.Group+" | "+l.Title+" | "+l.Detail+(l.Hint.Length>0?" | 怎么办："+l.Hint:""));
          foreach(var g in rep.Lines.Where(l=>l.Section=="mod").GroupBy(l=>l.Group)) Console.WriteLine("REPORT mods ["+g.Key+"] "+string.Join("; ",g.Select(x=>x.Title)));
          foreach(var g in rep.Files.GroupBy(f=>f.Group)) Console.WriteLine("REPORT files ["+g.Key+"] "+g.Count()); }
        foreach(string detail in imported.Details.Where(d=>d.Contains("停用模组")||d.Contains("兼容")||d.Contains("依赖"))) Console.WriteLine("DETAIL "+detail);
        if(imported.Proposed.Problems.Count==0)
        {
            string content=Path.Combine(imported.StagingDirectory,"content");
            var plugins=Files.Inventory(content).Keys.Where(Packages.IsAssembly).SelectMany(p=>Packages.ReadPlugins(Files.Child(content,p))).ToList();
            var absent=profile.mods.Select(m=>m.guid).Distinct(StringComparer.OrdinalIgnoreCase).Where(g=>plugins.All(p=>!p.Guid.Equals(g,StringComparison.OrdinalIgnoreCase))&&hosts.All(h=>!h.Guid.Equals(g,StringComparison.OrdinalIgnoreCase))).ToList();
            if(absent.Count!=3||profile.mods.Any(m=>absent.Contains(m.guid)&&m.enabled)) throw new Exception("Target mapping differs from three retained disabled missing targets");
            string author=Path.Combine(content,imported.Proposed.AuthorRoot,"profiles",OverviewProfileExport.PortableId(profile.publicationId));
            if(JObject.Parse(File.ReadAllText(Path.Combine(author,"config-values.json")))["values"]!.Count()!=513) throw new Exception("Author parameters changed");
            var frame=plugins.Single(p=>p.Guid=="com.framedoctor.peak"); if(frame.File.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)) throw new Exception("Disabled cached FrameDoctor was enabled");
            if(!Files.Inventory(content).Keys.Any(p=>p.EndsWith("NativeFog.bundle"))||!Files.Inventory(content).Keys.Any(p=>p.EndsWith("unifiedraytracing"))) throw new Exception("PLL resources missing");
            Console.WriteLine("PASS 66 target identities: 63 supplied/host + 3 retained disabled missing; 513 parameters; FrameDoctor disabled; PLL resources");
        }
        PackImports.Commit(storage,imported); storage.Validate(storage.Read().Packs.Single());
        string previousBep=Paths.BepInExRootPath; Paths.BepInExRootPath=receiver;
        try {
            var stored=storage.Read().Packs.Single();
            var direct=StoredPackDetails.Read(stored);
            if(direct.Request.Operation!="view-stored"||direct.Report.FileCount!=stored.Files.Count||direct.Report.ModCount==0) throw new Exception("Stored content view lost its report");
            string libraryBefore=File.ReadAllText(storage.IndexPath), taskBefore=File.ReadAllText(storage.TaskPath);
            stored.Report=null; var legacy=StoredPackDetails.Read(stored);
            if(legacy.Report.ModCount==0||legacy.Report.FolderVersions.Count==0) throw new Exception("Legacy stored pack view cannot reconstruct mods or versions");
            if(File.ReadAllText(storage.IndexPath)!=libraryBefore||File.ReadAllText(storage.TaskPath)!=taskBefore) throw new Exception("Read-only stored view wrote library state");
            Console.WriteLine("PASS direct stored-pack browsing + legacy report reconstruction without source ZIP or state changes");
        } finally { Paths.BepInExRootPath=previousBep; }
        if(imported.Proposed.Problems.Count>0) throw new Exception("Import has blocking problems");
        Files.RunningProbe=_=>false;
        string aConfig=Path.Combine(receiver,"config","acceptance-a.cfg"); Directory.CreateDirectory(Path.GetDirectoryName(aConfig)!); File.WriteAllText(aConfig,"A=original");
        var adopt=PackTransactions.Preview(storage,new PackRequest {Operation="preview-adopt",HostPlugins=hosts,HostRoots=hostRoots}); adopt.Request.Operation="confirm-adopt"; adopt.Request.ConfirmedConfigFiles=adopt.UnknownConfigurations;
        PackTransactions.Commit(storage,PackTransactions.Prepare(storage,adopt)); string a=storage.Read().ActivePackId;
        void Switch(string id) { var task=PackTransactions.Preview(storage,new PackRequest {Operation="preview-switch",PackId=id,HostPlugins=hosts}); task.Request.Operation="confirm-switch"; task.Request.ConfirmedConfigFiles=task.UnknownConfigurations; PackTransactions.Commit(storage,PackTransactions.Prepare(storage,task)); }
        Switch(imported.Proposed.Id); if(File.Exists(aConfig)) throw new Exception("A config leaked into target");
        Switch(a); if(File.ReadAllText(aConfig)!="A=original") throw new Exception("A config not restored");
        if(!coreBefore.OrderBy(p=>p.Key).SequenceEqual(Files.Inventory(Path.Combine(receiver,"core")).OrderBy(p=>p.Key))) throw new Exception("Core changed");
        var hostAfter=hostRoots.SelectMany(root=>Files.Inventory(Path.Combine(receiver,root)).Select(p=>root+"/"+p.Key+"|"+p.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!hostBefore.SetEquals(hostAfter)) throw new Exception("Host changed");
        Console.WriteLine("PASS actual export/import/inventory/A-target-A "+mode);
        string delivery=Path.Combine(project,"验证","RealPackRoundTrip","交付_"+Path.GetFileName(run)); Directory.CreateDirectory(delivery); string delivered=Path.Combine(delivery,mode+"_MCEPack_6b679869-1.1.0.zip"); File.Copy(zip,delivered); Console.WriteLine("DELIVER "+delivered);
    }
    catch(Exception error) { failures++; Console.WriteLine("FAILED "+mode+" "+error); }
}
foreach(var source in sourceStates) {var file=new FileInfo(source.Key);if(file.Length!=source.Value.Size||file.LastWriteTimeUtc!=source.Value.Time) throw new Exception("Live source changed: "+source.Key);}
Console.WriteLine("PASS source files unchanged: "+sourceStates.Count);
if(failures>0) throw new Exception($"{failures} publication round-trip(s) failed");
sealed class NoNetwork : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>throw new InvalidOperationException("OFFLINE CHECK attempted network: "+request.RequestUri);
}
