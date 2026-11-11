using ModConfigEnhance.Overview;
int checks=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);checks++;}
ShopInstalled.Snapshot Snapshot(string context="A",string version="1.0.0",string path="plugins/a.dll",bool disabled=false,bool cached=false,string area="plugins",bool reverse=false){
    var s=new ShopInstalled.Snapshot{Context=context};
    var a=new ShopInstalled.Evidence{Key="Author-A",Version=version,Path=path,Disabled=disabled,Cached=cached,Area=area};
    var b=new ShopInstalled.Evidence{Key="Author-A",Version="2.0.0",Path="plugins/b.dll",Area="plugins"};
    var c=new ShopInstalled.Evidence{Key="Author-C",Version="1.0.0",Path="core/c.dll",Area="core"};
    if(reverse){s.Packages[c.Key]=new(){c};s.Packages[a.Key]=new(){b,a};}else{s.Packages[a.Key]=new(){a,b};s.Packages[c.Key]=new(){c};}return s;
}
// Keep the deliberately multi-version Snapshot for order-independent signature checks.
// Operations use independent, unambiguous installed evidence.
ShopInstalled.Snapshot Single(string version="1.0.0",string area="plugins",bool cached=false){
    var s=new ShopInstalled.Snapshot{Context="A"};
    s.Packages["Author-A"]=new(){new(){Key="Author-A",Version=version,Path=area+"/a.dll",Area=area,Cached=cached}};
    return s;
}
void Blocked(ShopInstallationState s,string key,string reason){
    Check(!s.CanInstall(key)&&!s.CanUninstall(key,false)&&!s.CanUninstall(key,true),reason+" blocks install and uninstall preview even if owned");
}
var state=new ShopInstallationState();state.Reset("A");int generation=state.Generation;
Check(state.Status==ShopInstallationState.Phase.Reading,"first frame explicitly reading");
Check(!state.CanInstall("Author-New")&&!state.CanInstall("Author-C")&&!state.CanUninstall("Author-A",true),"reading disables install update uninstall including core");
Check(state.Complete(generation,"A",Snapshot()),"first ready changes presentation");
Check(!state.Complete(generation,"A",Snapshot()),"same content new instance no rebuild");
Check(!state.Complete(generation,"A",Snapshot(reverse:true)),"dictionary and evidence order stable no rebuild");
Check(state.Complete(generation,"A",Snapshot(version:"1.1.0")),"local version change updates");
Check(state.Complete(generation,"A",Snapshot(path:"plugins/new.dll")),"path change updates");
Check(state.Complete(generation,"A",Snapshot(disabled:true)),"disabled change updates");
Check(state.Complete(generation,"A",Snapshot(cached:true)),"cache change updates");
Check(state.Complete(generation,"A",Snapshot(area:"patchers")),"area change updates");
Blocked(state,"Author-A","multi-version signature fixture ambiguity");
Check(state.BlockReason("Author-A").Contains("ambiguous"),"multi-version evidence explains ambiguity");
Check(!state.CanInstall("Author-C")&&!state.CanUninstall("Author-C",true),"ready core remains read only");
Check(state.Complete(generation,"A",Single()),"independent single-version operation fixture becomes ready");
Check(state.CanInstall("Author-A")&&state.CanInstall("Author-New"),"single-version ready permits ordinary update and absent-package install");
Check(state.CanUninstall("Author-A",true),"owned single-version plugins may enter safe uninstall preview only");
Check(state.CanUninstall("Author-A",false),"no-ledger single-version plugins may request safe ownership review, not direct deletion");
Check(!state.CanUninstall("Author-New",false)&&!state.CanUninstall("Author-New",true),"absent package cannot enter uninstall preview even if owned");
// CanUninstall is only a UI preview gate. Production claims/dependencies/file revalidation
// remain mandatory and are exercised by Actions62, not bypassed by this state test.
Check(state.Complete(generation,"A",Single(area:"patchers")),"independent single-version patcher fixture becomes ready");
Check(state.CanInstall("Author-A")&&state.CanUninstall("Author-A",false)&&state.CanUninstall("Author-A",true),"patchers with or without ledger may enter safe preview, never directly delete");
foreach(var sample in new[]{("", "missing version"),("unknown", "unknown version")}){
    state.Complete(generation,"A",Single(version:sample.Item1));
    Blocked(state,"Author-A",sample.Item2);
    Check(state.BlockReason("Author-A").Contains("unknown"),sample.Item2+" explains unknown identity");
}
state.Complete(generation,"A",Single(area:"core"));
Blocked(state,"Author-A","single-version core");
// FinalChecks 2026-10-28: legacy/unmanaged keeps core blocked, but the reason is now the explicit migration requirement.
Check(!state.CompleteCurrentPack&&state.BlockReason("Author-A").Contains("Complete the current pack first")&&state.BlockReason("Author-A").Contains("legacy"),"legacy core remains blocked with explicit complete-current-pack requirement");
Check(!state.CanUninstall("Author-A",true),"legacy core uninstall preview stays blocked even if owned");
// Complete current pack: the flag only opens the safe preview gate; the worker RequireComplete/storage.Validate remains the disk authority.
state.CompleteCurrentPack=true;
Check(state.BlockReason("Author-A").Length==0&&state.CanInstall("Author-A")&&state.CanUninstall("Author-A",true),"complete current pack lets single-version core enter safe preview");
state.Complete(generation,"A",Snapshot());
Check(!state.CanInstall("Author-A")&&state.BlockReason("Author-A").Contains("ambiguous"),"complete current pack still blocks multi-version ambiguity");
state.Complete(generation,"A",Single(area:"core"));
state.CompleteCurrentPack=false;
Check(!state.CanInstall("Author-A"),"clearing the complete flag re-blocks core");
state.Complete(generation,"A",Single(area:"cache",cached:true));
Blocked(state,"Author-A","single-version cached package");
Check(state.BlockReason("Author-A").Contains("disabled cache"),"cached reason forbids move or enable");
state.Reset("B");
Check(!state.Complete(generation,"A",Snapshot())&&state.Status==ShopInstallationState.Phase.Reading&&!state.Snapshot.Has("Author-A"),"switch drops old success and clears readiness");
Check(!state.Complete(generation,"A",null,"old error")&&state.Status==ShopInstallationState.Phase.Reading,"switch drops old failure");
Check(!state.Complete(state.Generation,"B",Snapshot("A")),"mismatched snapshot context discarded");
generation=state.Generation;
Check(state.Complete(generation,"B",null,"access denied")&&state.Error=="access denied"&&state.Status==ShopInstallationState.Phase.Failed,"scan exception visible failed state");
Check(!state.CanInstall("Author-New")&&!state.CanInstall("Author-A")&&!state.CanUninstall("Author-A",true),"failed disables all mutations");
Blocked(state,"Author-A","failed scan");
Blocked(state,"Author-New","failed absent package");
state.Retry();
Check(state.Status==ShopInstallationState.Phase.Reading&&state.Error=="","retry returns to reading");
Blocked(state,"Author-A","retry reading");
Blocked(state,"Author-New","reading with absent package");
Check(!state.Complete(generation,"B",Snapshot("B")),"retry drops superseded generation");
generation=state.Generation;
var partial=Snapshot("B");partial.Failure="unreadable directory";
Check(state.Complete(generation,"B",partial)&&state.Status==ShopInstallationState.Phase.Failed&&!state.CanInstall("Author-New"),"partial scan cannot report absent packages as installable");
state.Retry();generation=state.Generation;
Check(state.Complete(generation,"B",Snapshot("B"))&&state.CanInstall("Author-New"),"successful retry restores ordinary operations");
Check(state.Complete(generation,"B",null,"refresh failed")&&!state.CanInstall("Author-A"),"ready refresh failure closes mutation gates");
Blocked(state,"Author-A","failed refresh retaining prior evidence");
state.Retry();
Blocked(state,"Author-A","retry reading retaining prior evidence");
// Adapter regression guards complement runtime production state/signature checks; not Unity visual tests.
string source=File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../src/Overview/ShopWindow.cs")));
Check(source.Contains("if(changed)InstallationChanged();")&&source.Contains("if(_selected!=null){UpdateInstallationDetails();return;}"),"adapter inventory completion guarded and detail reader retained");
Check(source.Contains("scrolls[i].content.anchoredPosition=offsets[i]")&&!source.Contains("_inventory.Result;if(!_showingTask)Draw()"),"adapter list restores scroll without old unconditional draw");
Console.WriteLine($"PASS {checks} production lifecycle/signature and adapter checks; no visual acceptance claimed");
