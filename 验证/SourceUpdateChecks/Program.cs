using ModConfigEnhance.Overview;
using Newtonsoft.Json;
int checks=0;
void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
Loc.RootDirectory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../runs/"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")));Directory.CreateDirectory(Loc.RootDirectory);
StorePackage P(string author,string name,string home="")=>new(){Author=author,Name=name,Homepage=home,Version="2.0.0",Versions=new(){"1.0.0","2.0.0"}};
OverviewMod M(string guid="mod",string name="BetterPing")=>new(){Id=guid,Guid=guid,RawName=name,PackageName=name,PackageVersion="1.0.0",PackageRoot="packages/"+guid,Author="Alice",AuthorSource="manifest"};
var a=P("Alice","BetterPing","https://github.com/Alice/BetterPing");var b=P("Bob","BetterPing","https://github.com/Bob/BetterPing");var fork=P("Alice","BetterPingFork");var cn=P("Alice","BetterPingCN");var fix=P("Alice","BetterPingFix");
var index=new StoreIdentityIndex(new[]{a,b,fork,cn,fix});var mod=M();
Check(index.Resolve(mod)==a,"exact name plus trusted author");mod.Author="";Check(index.Resolve(mod)==null,"same name authors ambiguous");Check(new StoreIdentityIndex(new[]{a}).Resolve(mod)==null,"unique name alone never confirms");
mod.HomepageUrl="https://github.com/Alice/BetterPing/issues";Check(index.Resolve(mod)==a,"complete repository supports strict name");mod.HomepageUrl="https://github.com/Alice";Check(index.Resolve(mod)==null,"owner-only homepage insufficient");
mod.Author="Bob";mod.HomepageUrl=a.Homepage;Check(index.Resolve(mod)==null,"conflicting author and repository remain unresolved");
mod=M(name:"BeterPing");Check(index.Resolve(mod)==null,"edit distance never auto-confirms");Check(index.Candidates("BeterPing",mod).Any(p=>p==a),"edit-distance candidate recommended");
foreach(var variant in new[]{"BetterPingFork","BetterPingCN","BetterPingFix"}){mod=M(name:variant);Check(index.Resolve(mod)?.Name==variant,"preserve variant "+variant);}
mod=M();mod.SourceNamespace="Nobody";mod.SourceName="BetterPing";Check(index.Resolve(mod)==null,"nonexistent explicit identity not replaced");mod.SourceNamespace="Alice";Check(index.Resolve(mod)==a,"official exact identity verified");
var hinted=M();hinted.SourceNamespace="Bob";hinted.SourceName="BetterPing";Check(index.Resolve(hinted)==null,"existing store identity cannot override conflicting local author");
hinted=M();hinted.SourceNamespace="Alice";hinted.SourceName="BetterPing";hinted.Author="";hinted.AuthorSource="";Check(index.Resolve(hinted)==null,"folder-format identity alone is not sufficient evidence");mod.HomepageUrl="https://thunderstore.io/c/peak/p/Bob/BetterPing/";Check(index.Resolve(mod)==null,"conflicting store URL rejected");
Check(new StoreIdentityIndex(new[]{new StorePackage{Author="Alice",Name="BetterPing",Deprecated=true}}).Identity("Alice","BetterPing")==null,"deprecated identity rejected");
ShopCatalog.Items=new(){new(){Team="Alice",Name="BetterPing",Homepage=a.Homepage,Version="2.0.0",CategoriesKnown=true,Versions=new(){new(){Version="1.0.0"},new(){Version="2.0.0"}}}};
var authorRoot=Path.Combine(Loc.RootDirectory,"author");Directory.CreateDirectory(authorRoot);var legacy=M("legacy");File.WriteAllText(Path.Combine(authorRoot,"sources.json"),JsonConvert.SerializeObject(new Dictionary<string,ModSourceRecord>{{legacy.Guid,new(){kind=ModSourceKind.Local,ns="Alice",name="BetterPing",homepage=a.Homepage,packageVersion="1.0.0"}}}));var original=File.ReadAllText(Path.Combine(authorRoot,"sources.json"));PublishedPack.Packs.Add(new(){Root=authorRoot});ModSources.InvalidateRecords();
await ModSources.Discover(new[]{legacy},CancellationToken.None);Check(legacy.SourceConfirmed,"legacy published Local restored");Check(File.ReadAllText(Path.Combine(authorRoot,"sources.json"))==original,"author original unchanged");Check(!File.Exists(Path.Combine(Loc.RootDirectory,"runtime/sources.json")),"automatic discovery does not write personal source choices");
ModSources.Set(new[]{legacy},new(){kind=ModSourceKind.Local});await ModSources.Discover(new[]{legacy},CancellationToken.None);Check(!legacy.SourceConfirmed&&legacy.SourceKind==ModSourceKind.Local,"explicit local preserved");
var moved=M("legacy");moved.PackageRoot="another installation";await ModSources.Discover(new[]{moved},CancellationToken.None);Check(moved.SourceConfirmed,"choice isolated to original installation");
var one=M("one");await ModSources.Discover(new[]{one},CancellationToken.None);var export=Path.Combine(Loc.RootDirectory,"export");ModSources.Export(export,new[]{one});var record=JsonConvert.DeserializeObject<Dictionary<string,ModSourceRecord>>(File.ReadAllText(Path.Combine(export,"sources.json")))![one.Guid];Check(record.kind==ModSourceKind.Store&&record.packageVersion=="1.0.0","production export retains store identity and version");
var two=M("two");var local=M("local");ModSources.Set(new[]{local},new(){kind=ModSourceKind.Local});var disabled=M("disabled");disabled.Rule.updateAllowed=false;var unknown=M("unknown","NoMatch");var missing=M("missing");missing.PackageVersion="";
using var service=new ThunderstoreUpdateService(new(),TimeSpan.Zero);await service.CheckAsync(new[]{one,two,local,disabled,unknown,missing},CancellationToken.None);var report=service.LastReport;Check(report.ModCount==6&&report.Completed==6,"all entries counted including disabled local unresolved");Check(report.PackageCount==1,"shared identity queried once");Check(report.UpdateCount==2&&new[]{one,two,local,disabled,unknown,missing}.Count(m=>m.HasUpdate)==2,"K matches distinct green-dot entries");Check(!missing.HasUpdate&&missing.CheckMessage.Contains("unknown"),"unknown local version not DLL comparison");Check(ShopNetwork.Requests==0,"PEAK catalog metadata only no extra network");Check(report.Summary=="Checked 6/6 mods; 2 mods have updates.","concise summary no package categories");
using(var cts=new CancellationTokenSource()){ShopCatalog.Loading=()=>cts.Cancel();try{await service.CheckAsync(new[]{one,two},cts.Token);}catch(OperationCanceledException){}finally{ShopCatalog.Loading=null;}Check(service.LastReport.Cancelled&&service.LastReport.Completed==0&&service.LastReport.ModCount==2,"cancel preserves actual incomplete progress");}
ShopCatalog.Error=new IOException("offline");try{await service.CheckAsync(new[]{one,two},CancellationToken.None);}catch(IOException){}finally{ShopCatalog.Error=null;}Check(service.LastReport.Error=="offline"&&service.LastReport.Completed==0,"global failure not fake completion");Check(!one.HasUpdate&&!two.HasUpdate,"old green results cleared on failed new round");
InfrastructurePackages.Items=new(){new(){Team="Alice",Name="BetterPing",Version="1.0.0"}};await service.CheckAsync(new[]{one},CancellationToken.None);Check(service.LastReport.ModCount==2&&service.LastReport.Completed==2&&service.LastReport.UpdateCount==2,"existing component group participates in same entry unit");

// Adversarial source review: execute production resolver, persistence and export paths.
var ownerOnly=M("owner-only");ownerOnly.Author="Alice";ownerOnly.AuthorSource="homepage";ownerOnly.HomepageUrl="https://github.com/Alice/DifferentRepository";
Check(new StoreIdentityIndex(new[]{a}).Resolve(ownerOnly)==null,"homepage owner is not store-team evidence");
var org=M("organization");org.Author="GitHubOrganization";org.AuthorSource="homepage";org.HomepageUrl=a.Homepage;
Check(new StoreIdentityIndex(new[]{a}).Resolve(org)==a,"repository owner differing from store team does not veto full repository evidence");
var forged=M("forged");forged.Author="Bob";forged.AuthorSource="user";forged.SourceNamespace="Bob";forged.SourceName="BetterPing";
Check(index.Resolve(forged)==null,"author-source user flag alone cannot authorize a mapping");
var branch=P("Alice","BetterPingFork",a.Homepage);var branchIndex=new StoreIdentityIndex(new[]{a,branch});
Check(branchIndex.Resolve(M("branches"))==null,"same repository branches cannot be auto-disambiguated by exact name");
Check(branchIndex.Reason(a,M()).Contains("branches"),"candidate reason exposes repository branch ambiguity");
var implicitMod=M("implicit");implicitMod.Author="Mallory";implicitMod.SourceNamespace="Alice";implicitMod.SourceName="BetterPing";ModSources.Apply(implicitMod);
Check(!implicitMod.SourceConfirmed,"Apply cannot confirm official existence against conflicting local author");
ShopCatalog.Items.Add(new(){Team="Bob",Name="BetterPing",Homepage=b.Homepage,Version="2.0.0",Versions=new(){new(){Version="2.0.0"}}});
// Refresh the official immutable snapshot (not mutate the indexed list in place).
ShopCatalog.Items=ShopCatalog.Items.ToList();
var corrected=M("corrected");corrected.HomepageUrl="https://thunderstore.io/c/peak/p/Alice/BetterPing/";
ModSources.Confirm(corrected,b);ModSources.Apply(corrected);
Check(corrected.SourceConfirmed&&corrected.SourceNamespace=="Bob"&&corrected.HomepageUrl==b.Homepage,"explicit correction A to B replaces stale store URL and remains usable");
Check(corrected.AuthorSource=="manifest"&&corrected.Author=="Alice","selected mapping never forges local author evidence");
ModSources.Apply(corrected);Check(corrected.SourceConfirmed,"repeated Apply preserves valid explicitly selected mapping");
var fixtureRoot=Path.Combine(Loc.RootDirectory,"instance");Directory.CreateDirectory(fixtureRoot);
string dll=Path.Combine(fixtureRoot,"feature.dll"),manifest=Path.Combine(fixtureRoot,"manifest.json");File.WriteAllText(dll,"dll-v1");File.WriteAllText(manifest,"{\"name\":\"BetterPing\"}");
OverviewMod Installed(){var m=M("installed");m.Location=dll;m.PackageRoot=fixtureRoot;return m;}
var installed=Installed();ModSources.Confirm(installed,b);ModSources.Apply(installed);Check(installed.SourceConfirmed&&installed.SourceNamespace=="Bob","manual mapping binds to actual installation");
File.AppendAllText(dll,"-changed");var changed=Installed();ModSources.Apply(changed);Check(!ModSources.UserChoice(changed,"Bob","BetterPing"),"DLL stamp change invalidates user mapping");
ModSources.Confirm(changed,b);File.AppendAllText(manifest," ");var manifestChanged=Installed();ModSources.Apply(manifestChanged);Check(!ModSources.UserChoice(manifestChanged,"Bob","BetterPing"),"manifest content change invalidates user mapping");
ModSources.Confirm(manifestChanged,b);var authorChanged=Installed();authorChanged.Author="Carol";ModSources.Apply(authorChanged);Check(!ModSources.UserChoice(authorChanged,"Bob","BetterPing"),"declared author change invalidates user mapping");
var homeChanged=Installed();homeChanged.HomepageUrl="https://github.com/Carol/Another";ModSources.Apply(homeChanged);Check(!ModSources.UserChoice(homeChanged,"Bob","BetterPing"),"homepage change invalidates user mapping");
PackLibrary.Managed=true;PackLibrary.Workspace="same-workspace";PackLibrary.Active=new(){RevisionStateHash="instance-1"};
var packBound=Installed();ModSources.Confirm(packBound,b);PackLibrary.Active.RevisionStateHash="instance-2";var packReplaced=Installed();ModSources.Apply(packReplaced);Check(!ModSources.UserChoice(packReplaced,"Bob","BetterPing"),"same workspace replacement pack invalidates mapping");
PackLibrary.Managed=false;PackLibrary.Active=null;
var derivedMod=Installed();await ModSources.Discover(new[]{derivedMod},CancellationToken.None,true);Check(derivedMod.SourceConfirmed,"derived fixture initially resolves with local evidence");
File.AppendAllText(manifest," ");var derivedChanged=Installed();derivedChanged.Author="Mallory";ModSources.Apply(derivedChanged);Check(!derivedChanged.SourceConfirmed,"derived cache cannot override changed installation and author");
var correctionExport=Path.Combine(Loc.RootDirectory,"corrected-export");ModSources.Export(correctionExport,new[]{corrected});var exportedChoice=JsonConvert.DeserializeObject<Dictionary<string,ModSourceRecord>>(File.ReadAllText(Path.Combine(correctionExport,"sources.json")))![corrected.Guid];
Check(exportedChoice.ns=="Bob"&&exportedChoice.instance==""&&exportedChoice.note!="user","export keeps identity but never transports installation user authority");

var poisonedRoot=Path.Combine(Loc.RootDirectory,"poisoned-author");Directory.CreateDirectory(poisonedRoot);
File.WriteAllText(Path.Combine(poisonedRoot,"sources.json"),JsonConvert.SerializeObject(new Dictionary<string,ModSourceRecord>{{"poisoned",new(){kind=ModSourceKind.Store,ns="Alice",name="BetterPing",author="Alice",homepage=a.Homepage,note="user"}}}));
PublishedPack.Packs.Add(new(){Root=poisonedRoot});ModSources.InvalidateRecords();var poisoned=M("poisoned");poisoned.Author="Mallory";poisoned.HomepageUrl="https://github.com/Mallory/Other";ModSources.Apply(poisoned);
Check(!poisoned.SourceConfirmed&&poisoned.AuthorSource=="manifest"&&poisoned.HomepageUrl.Contains("Mallory"),"published cache cannot forge user authority or replace conflicting local homepage");
var unbound=M("unbound");var choiceFile=Path.Combine(Loc.RootDirectory,"runtime/sources.json");var savedChoices=JsonConvert.DeserializeObject<Dictionary<string,ModSourceRecord>>(File.ReadAllText(choiceFile))!;savedChoices[unbound.Guid]=new(){kind=ModSourceKind.Store,ns="Bob",name="BetterPing",author="Bob",note="user"};File.WriteAllText(choiceFile,JsonConvert.SerializeObject(savedChoices));ModSources.InvalidateRecords();ModSources.Apply(unbound);
Check(!ModSources.UserChoice(unbound,"Bob","BetterPing"),"legacy unbound user record requires revalidation instead of claiming a new installation");

var invalidVersion=P("Alice","BetterPing");invalidVersion.Version="invalid";Check(new StoreIdentityIndex(new[]{invalidVersion}).Resolve(M())==null,"invalid official latest version does not confirm source");
var raced=Installed();ShopCatalog.Loading=()=>File.AppendAllText(dll,"race");bool rejected=false;try{await ModSources.Discover(new[]{raced},CancellationToken.None,true);}catch(OperationCanceledException){rejected=true;}finally{ShopCatalog.Loading=null;}Check(rejected,"installation mutation during catalog await rejects stale discovery result");
// Final repair regressions: asynchronous catalogue boundaries execute the production round.
InfrastructurePackages.Items.Clear();
var groupOne=M("group-one");var groupTwo=M("group-two");groupOne.Rule.groupId="old-a";groupTwo.Rule.groupId="old-b";
var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var resume=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
int waits=0;
ShopCatalog.Awaiting=async t=>{if(Interlocked.Increment(ref waits)==1){entered.SetResult();await resume.Task.WaitAsync(t);}};
try {
    var round=service.CheckAsync(new[]{groupOne,groupTwo},CancellationToken.None);await entered.Task;
    groupOne.Rule.groupId="brand-new-group";groupTwo.Rule=new(){groupId="another-new-group"};resume.SetResult();await round;
} finally {ShopCatalog.Awaiting=null;}
var grouped=service.LastReport;
Check(grouped.Completed==2&&grouped.Groups["old-a"].SequenceEqual(new[]{1,1})&&grouped.Groups["old-b"].SequenceEqual(new[]{1,1})&&!grouped.Groups.ContainsKey("brand-new-group"),"await group edits retain fixed round groups without exception");
Check(grouped.UpdateCount==2&&new[]{groupOne,groupTwo}.Count(m=>m.HasUpdate)==2,"group mutation keeps K equal to completed green dots");

foreach(bool cancelRound in new[]{true,false}) {
    var staleOne=M("stale-one");var staleTwo=M("stale-two");
    foreach(var m in new[]{staleOne,staleTwo}){m.HasUpdate=true;m.CheckMessage="Update available";m.LatestVersion="9.0.0";m.UpdateNotes="old notes";m.LastCheckedUtc=DateTime.UtcNow;}
    using var cancellation=new CancellationTokenSource();
    entered=new(TaskCreationOptions.RunContinuationsAsynchronously);resume=new(TaskCreationOptions.RunContinuationsAsynchronously);
    ShopCatalog.Awaiting=async t=>{entered.SetResult();await resume.Task.WaitAsync(t);if(!cancelRound)throw new IOException("round offline");};
    try {
        var round=service.CheckAsync(new[]{staleOne,staleTwo},cancellation.Token);await entered.Task;
        Check(new[]{staleOne,staleTwo}.All(m=>!m.HasUpdate&&m.CheckMessage=="Checking updates"&&m.LatestVersion==""&&m.UpdateNotes==""&&m.LastCheckedUtc==null),"old messages cleared while awaiting "+(cancelRound?"cancellation":"failure"));
        if(cancelRound)cancellation.Cancel();else resume.SetResult();
        try{await round;throw new Exception("expected interrupted round");}catch(OperationCanceledException) when(cancelRound){}catch(IOException) when(!cancelRound){}
    } finally {ShopCatalog.Awaiting=null;}
    Check(new[]{staleOne,staleTwo}.All(m=>!m.HasUpdate&&m.CheckMessage==(cancelRound?"Update check cancelled":"Check failed: round offline")&&m.LastCheckedUtc==null),"unfinished messages report actual "+(cancelRound?"cancellation":"failure"));
    Check(service.LastReport.Completed==0&&service.LastReport.UpdateCount==0,"interrupted round has no fake completion or K "+cancelRound);
}
// Interrupt after an actual completed package, not before every result.
foreach(bool cancelRound in new[]{true,false}) {
    var finished=M("finished");var unfinished=M("unfinished");unfinished.SourceNamespace="Bob";unfinished.SourceName="BetterPing";unfinished.Author="Bob";
    unfinished.CheckMessage="Update available";unfinished.HasUpdate=true;
    using var cancellation=new CancellationTokenSource();
    try {
        await service.CheckAsync(new[]{finished,unfinished},cancellation.Token,(done,total)=>{if(done==1){cancellation.Cancel();if(!cancelRound)throw new IOException("progress offline");}});
        throw new Exception("expected partial interruption");
    }catch(OperationCanceledException) when(cancelRound){}catch(IOException) when(!cancelRound){}
    Check(finished.HasUpdate&&finished.CheckMessage=="Update available"&&finished.LatestVersion=="2.0.0"&&finished.LastCheckedUtc!=null,"completed actual result survives partial "+(cancelRound?"cancel":"failure"));
    Check(!unfinished.HasUpdate&&unfinished.LatestVersion==""&&unfinished.CheckMessage==(cancelRound?"Update check cancelled":"Check failed: progress offline"),"only unfinished entry gets interruption status "+cancelRound);
    Check(service.LastReport.Completed==1&&service.LastReport.UpdateCount==1&&new[]{finished,unfinished}.Count(m=>m.HasUpdate)==1,"partial round progress and K remain truthful "+cancelRound);
}
InfrastructurePackages.Items=new(){new(){Team="OtherCommunity",Name="Loader",Version="1.0.0"},new(){Team="Alice",Name="BetterPing",Version="1.0.0"}};
var offRepository=new OverviewRepository{GlobalUpdateAllowed=false};
using(var offService=new ThunderstoreUpdateService(offRepository,TimeSpan.Zero)) {
    int loadsBefore=ShopCatalog.Loads,requestsBefore=ShopNetwork.Requests;
    var offMod=M("off-mod");offMod.Rule.updateAllowed=false;
    await offService.CheckAsync(new[]{offMod},CancellationToken.None);
    Check(ShopCatalog.Loads==loadsBefore&&ShopNetwork.Requests==requestsBefore,"global off components cause zero catalogue and API requests");
    Check(offService.LastReport.ModCount==3&&offService.LastReport.Completed==3&&offService.LastReport.EligibleModCount==0&&offService.LastReport.PackageCount==0&&offService.LastReport.UpdateCount==0&&InfrastructurePackages.Items.All(p=>p.Latest==""),"disabled components remain denominator only without K or latest");
    var allowedMod=M("allowed-mod");await offService.CheckAsync(new[]{allowedMod},CancellationToken.None);
    Check(allowedMod.HasUpdate&&offService.LastReport.UpdateCount==1&&offService.LastReport.EligibleModCount==1&&offService.LastReport.PackageCount==1&&ShopNetwork.Requests==requestsBefore,"global component gate preserves ordinary per-mod permission semantics");
}
InfrastructurePackages.Items.Clear();
var policyRoot=Path.Combine(Loc.RootDirectory,"published-policy");Directory.CreateDirectory(policyRoot);
var policyMod=M("published-policy");
File.WriteAllText(Path.Combine(policyRoot,"sources.json"),JsonConvert.SerializeObject(new Dictionary<string,ModSourceRecord>{{policyMod.Guid,new(){kind=ModSourceKind.Local,explicitLocal=true,ns="Alice",name="BetterPing"}}}));
PublishedPack.Packs.Add(new(){Root=policyRoot});ModSources.InvalidateRecords();
ModSources.Set(new[]{policyMod},null);await ModSources.Discover(new[]{policyMod},CancellationToken.None,true);
Check(ModSources.ExplicitLocal(policyMod)&&!policyMod.SourceConfirmed&&policyMod.SourceKind==ModSourceKind.Local,"personal auto cannot mask published explicit Local");
ModSources.Confirm(policyMod,a);await ModSources.Discover(new[]{policyMod},CancellationToken.None,true);
Check(policyMod.SourceConfirmed&&policyMod.SourceKind==ModSourceKind.Store&&!ModSources.ExplicitLocal(policyMod),"explicit personal Store overrides published explicit Local");
ModSources.Set(new[]{policyMod},new(){kind=ModSourceKind.Local});await ModSources.Discover(new[]{policyMod},CancellationToken.None,true);
Check(ModSources.ExplicitLocal(policyMod)&&!policyMod.SourceConfirmed,"explicit personal Local retains priority");
ModSources.Set(new[]{policyMod},null);ModSources.InvalidateRecords();await ModSources.Discover(new[]{policyMod},CancellationToken.None,true);
Check(ModSources.ExplicitLocal(policyMod)&&!policyMod.SourceConfirmed,"auto fallback to published explicit Local survives reload");
ModSources.Set(new[]{legacy},null);await ModSources.Discover(new[]{legacy},CancellationToken.None,true);
Check(legacy.SourceConfirmed&&!ModSources.ExplicitLocal(legacy),"personal auto permits recovery from legacy published Local without explicitLocal");
Console.WriteLine($"TOTAL {checks} production checks passed; sandbox {Loc.RootDirectory}");
