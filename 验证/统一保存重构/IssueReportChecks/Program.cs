using ModConfigEnhance.Overview;
BepInEx.Paths.BepInExRootPath=Path.Combine(Path.GetTempPath(),"mce-issue-"+System.Guid.NewGuid());
var repo=new OverviewRepository();var mods=new[]{new OverviewMod{Name="模组甲",Guid="same",PluginVersion="1.0",Location="plugins/a.dll",DisplayVersion="1.0",AssemblyPaths=new(){"plugins/a.dll"},RuntimeDependencies=new(){new(){Id="missing"}}},new OverviewMod{Name="模组乙",Guid="same",PluginVersion="2.0",Location="plugins/b.dll",DisplayVersion="2.0",AssemblyPaths=new(){"plugins/b.dll"}}};
int n=0;void Check(bool ok,string reason){if(!ok)throw new Exception(reason);n++;}
string report=ConfigurationIssueReport.Build(repo,mods,formatted:true);Check(report.Contains("重复模组")&&report.Contains("模组甲")&&report.Contains("模组乙")&&report.Contains("1.0"),"duplicate report names copies and versions");Check(report.Contains("可共存，按需整理旧版"),"duplicate versions are explicitly advisory");Check(report.Contains("依赖检查")&&report.Contains("missing"),"missing dependencies have independent entries");Check(report.Contains("1. ")&&report.Contains("<size=85%><color=#929DA8>"),"numbered entries include actionable suggestions");Check(ConfigurationIssueReport.Build(repo,Array.Empty<OverviewMod>())=="","clean environment has no empty categories");report=ConfigurationIssueReport.Build(repo,mods,"Sharing violation on path worker.lock");Check(!report.Contains("Sharing violation")&&report.Contains("文件正在被写入任务占用"),"known IO error uses localized explanation");var rejected=new List<string>();var accepted=ValidSaveSelection.Select(new[]{"good","bad","good2"},values=>values.Contains("bad")?"unsafe path":null,(target,reason)=>rejected.Add(target));Check(accepted.SequenceEqual(new[]{"good","good2"})&&rejected.SequenceEqual(new[]{"bad"}),"unsafe target excluded while valid targets remain accepted");int calls=0;accepted=ValidSaveSelection.Select(new[]{"owner","provider"},values=>{calls++;return values.Count==2?null:"requires companion";},(target,reason)=>throw new Exception("full valid batch should not partition"));Check(accepted.Count==2&&calls==1,"complete batch validated before partitioning dependent companions");
Check(ConfigurationIssueReport.TailPath("F:/Games/PEAK/BepInEx/plugins/"+new string('a',120)+"/mod.dll")=="BepInEx/plugins/"+new string('a',120)+"/mod.dll","full path starts at BepInEx without truncation");Check(report.Split(new[]{"可共存，按需整理旧版"},StringSplitOptions.None).Length==2,"duplicate advice occurs once per category");
Check(ConfigurationIssueReport.Related(repo,mods).Count==2,"related view includes both duplicate copies");Check(ConfigurationIssueReport.Related(repo,Array.Empty<OverviewMod>()).Count==0,"related view excludes clean unrelated mods");
foreach (string title in new[]{"So Fly","TerrainRandomiser"}) {
 var body=new OverviewMod{Name=title,Guid=title,PluginVersion="2.1.0",Location="plugins/pack/body.dll",PluginGuids=new(){title,title+".zh"},AssemblyPaths=new(){"plugins/pack/body.dll","plugins/pack/zh.dll"}};
 var zh=new OverviewMod{Name=title+" 汉化",Guid=title+".zh",PluginVersion="1.2.0",Location="plugins/pack/zh.dll",PluginGuids=new(){title,title+".zh"},AssemblyPaths=new(){"plugins/pack/body.dll","plugins/pack/zh.dll"}};
 var pair=new[]{body,zh};
 Check(OverviewPluginCopies.Duplicates(pair).Count==0,title+" body and localization are not duplicates despite shared package IDs/paths");
 Check(!ConfigurationIssueReport.Build(repo,pair).Contains("重复模组"),title+" no false duplicate report");
 Check(ConfigurationIssueReport.Related(repo,pair).Count==0,title+" no false related/issue filter members");
 var old=new OverviewMod{Name=title+" old",Guid=title,PluginVersion="1.0.0",Location="plugins/old/body.dll"};
 var all=new[]{body,zh,old};var groups=OverviewPluginCopies.Duplicates(all);
 Check(groups.Count==1&&groups[0].Count==2&&groups[0].All(p=>p.Guid==title),title+" only actual body copies paired");
 string text=ConfigurationIssueReport.Build(repo,all,formatted:true);
 Check(!text.Contains("汉化")&&!text.Contains("zh.dll")&&text.Contains("2.1.0")&&text.Contains("1.0.0"),title+" own metadata/path only");
 Check(text.Contains("1.\n"+title)&&text.Contains("</size>\n与\n"),"standalone number and normally colored connector");
 Check(OverviewPluginCopies.Contains(body,groups)&&OverviewPluginCopies.Contains(old,groups)&&!OverviewPluginCopies.Contains(zh,groups),"filter identifies exact plugin rows");
 Check(OverviewPluginCopies.Duplicates(new[]{body,body}).Count==0,"same physical evidence not counted twice");
 body.PluginCopies=new(){new(){Guid=title,Name=title,Version="2.1.0",Path=body.Location},new(){Guid=title,Name=title,Version="1.9.0",Path="plugins/pack/old.dll.disabled"}};
 Check(OverviewPluginCopies.Duplicates(pair).Single().Count==2,"same-package consolidated copies retain independent metadata");
}
var manager=new OverviewMod{Name="Mod Manager",Guid="manager",PackageDependencies=new(){"RTLC-XUnity_AutoTranslator-5.6.11"}};
var xauto=new OverviewMod{Name="XUnity",Guid="gravydevsupreme.xunity.autotranslator",PackageName="XUnity_AutoTranslator",PackageVersion="5.6.11",SourceConfirmed=true,SourceNamespace="RTLC",SourceName="XUnity_AutoTranslator"};
Check(ConfigurationIssueReport.Build(repo,new[]{manager,xauto})=="","verified RTLC identity satisfies dependency with authorless manifest");
Check(ConfigurationIssueReport.Related(repo,new[]{manager,xauto}).Count==0,"related status agrees with resolved package check");
xauto.SourceNamespace="Hayrizan";
Check(ConfigurationIssueReport.Build(repo,new[]{manager,xauto}).Contains("缺失"),"same package name under different team is not substituted");
xauto.SourceNamespace="RTLC";xauto.SourceConfirmed=false;
Check(ConfigurationIssueReport.Build(repo,new[]{manager,xauto}).Contains("缺失"),"unverified source hint is not package proof");
xauto.SourceConfirmed=true;xauto.PackageVersion="5.6.1";
Check(ConfigurationIssueReport.Build(repo,new[]{manager,xauto}).Contains("版本过低"),"verified installed provider still checks version");
string fixture=BepInEx.Paths.BepInExRootPath;
void Package(string dir,string name,string version,string team="",bool disabled=false){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"manifest.json"),System.Text.Json.JsonSerializer.Serialize(new{name,version_number=version,@namespace=team}));File.WriteAllText(Path.Combine(dir,"payload.dll"+(disabled?".disabled":"")),"fixture");OverviewPageCache.Inventory++;}
var consumer=new OverviewMod{Name="PerspectiveSwitcher",PackageDependencies=new(){"Hamunii-AutoHookGenPatcher-1.0.9"}};
Package(Path.Combine(fixture,"patchers","Hamunii-AutoHookGenPatcher"),"AutoHookGenPatcher","1.1.1","Hamunii");
Check(ConfigurationIssueReport.Build(repo,new[]{consumer})=="","installed patcher satisfies ordinary plugin package dependency");
consumer.PackageDependencies=new(){"Snosz-PhotonCustomPropsUtils-1.1.0"};
Package(Path.Combine(fixture,"plugins","中文工具目录"),"PhotonCustomPropsUtils","1.1.0");
string unknown=ConfigurationIssueReport.Build(repo,new[]{consumer});
Check(unknown.Contains("来源未识别")&&!unknown.Contains("依赖检查")&&!unknown.Contains("缺失"),"authorless available library has independent source warning not dependency warning");
Check(ConfigurationIssueReport.Related(repo,new[]{consumer}).Count==0,"available anonymous library does not mark consumer dependency broken");
Package(Path.Combine(fixture,"plugins","Snosz-PhotonCustomPropsUtils"),"PhotonCustomPropsUtils","1.1.0","Snosz");
Check(!ConfigurationIssueReport.Build(repo,new[]{consumer}).Contains("依赖检查"),"non-plugin library manifest and payload satisfy package dependency");
consumer.PackageDependencies=new(){"Team-Disabled-1.0.0"};Package(Path.Combine(fixture,"plugins","Team-Disabled"),"Disabled","1.0.0","Team",true);
Check(ConfigurationIssueReport.Build(repo,new[]{consumer}).Contains("未启用"),"disabled payload not active dependency");
consumer.PackageDependencies=new(){"Team-Cached-1.0.0"};Package(Path.Combine(fixture,"Team-Cached"),"Cached","1.0.0","Team");
Check(ConfigurationIssueReport.Build(repo,new[]{consumer}).Contains("缓存区"),"cached payload not active dependency");
consumer.PackageDependencies=new(){"Team-ManifestOnly-1.0.0"};string empty=Path.Combine(fixture,"plugins","Team-ManifestOnly");Directory.CreateDirectory(empty);File.WriteAllText(Path.Combine(empty,"manifest.json"),"{\"namespace\":\"Team\",\"name\":\"ManifestOnly\",\"version_number\":\"1.0.0\"}");OverviewPageCache.Inventory++;
Check(ConfigurationIssueReport.Build(repo,new[]{consumer}).Contains("缺失"),"manifest without real runtime payload is not installed");
Directory.Delete(fixture,true);
if(args.Length>0){BepInEx.Paths.BepInExRootPath=args[0];OverviewPageCache.Inventory++;var real=ShopInstalled.Build(args[0],"read-only","",Array.Empty<ShopInstalled.Evidence>());
 Check(real.Packages.TryGetValue("Hamunii-AutoHookGenPatcher",out var patch)&&patch.Any(e=>e.Version=="1.1.1"&&!e.Disabled&&!e.Cached),"actual installed patcher 1.1.1 read-only inventory");
 Check(real.Unresolved.Any(e=>e.Name=="PhotonCustomPropsUtils"&&e.Version=="1.1.0"&&!e.Disabled&&!e.Cached)||real.Has("Snosz-PhotonCustomPropsUtils"),"actual Photon library present read-only inventory");}
var searchFields=new[]{"Original Mod", "模组译名", "Team-Package_Id", "作者 Developer", "2.1.10", "Detailed description keyword"};
foreach(var query in new[]{"original","译名","team-package_id","developer","2.1.10","keyword","  ORIGINAL   译名 developer  "})
 Check(OverviewSearchText.Matches(query,searchFields),"search covers field/case/token: "+query);
Check(!OverviewSearchText.Matches("original absent",searchFields),"all search terms required across fields");
Check(OverviewSearchText.Matches("  ",searchFields),"empty query restores all");
Package(Path.Combine(fixture,"plugins","中文工具目录"),"PhotonCustomPropsUtils","1.1.0");
var unresolvedProvider=new OverviewMod{Name="PhotonCustomPropsUtils",Guid="photon",PackageName="PhotonCustomPropsUtils",PluginVersion="1.1.0",SourceConfirmed=false,SourceKind=ModSourceKind.Pending};
consumer.PackageDependencies=new(){"Snosz-PhotonCustomPropsUtils-1.1.0"};
var sourceReport=ConfigurationIssueReport.Build(repo,new[]{consumer,unresolvedProvider});
Check(sourceReport.Contains("来源未识别")&&sourceReport.Contains("在手动识别之前，该模组将无法自动检测更新")&&!sourceReport.Contains("依赖检查"),"source notice describes only automatic updates");
Check(ConfigurationIssueReport.Related(repo,new[]{consumer,unresolvedProvider}).Contains(unresolvedProvider),"inspect includes unidentified dependency provider itself");
var disabledProvider=new OverviewMod{Name="disabled provider",Guid="runtime-provider",TargetEnabled=false};
var needsRuntime=new OverviewMod{Name="consumer",RuntimeDependencies=new(){new(){Id="runtime-provider"}}};
Check(ConfigurationIssueReport.Related(repo,new[]{needsRuntime,disabledProvider}).Count==2,"inspect includes disabled runtime provider and consumer");
unresolvedProvider.SourceKind=ModSourceKind.Local;
Check(ConfigurationIssueReport.Unidentified(new[]{unresolvedProvider}).Count==0,"intentional local mod excluded from automatic-source notices");
var named=new OverviewMod{Name="Original",RawName="Original",TranslatedName="译名",PluginVersion="1.2.3",SourceConfirmed=false,SourceKind=ModSourceKind.Pending};
string caption=ConfigurationIssueReport.Build(repo,new[]{named},formatted:true);
Check(caption.Contains("译名 <size=85%><color=#929DA8>（Original，1.2.3）"),"translated source caption with gray original and own version");
Check(!caption.Contains("BepInEx"),"nonduplicate source notice has no path");
// Fork → upstream update inside one folder: same display name, different GUIDs, both loadable → advice under 重复模组; the localization companion is not.
var upstream=new OverviewMod{Name="Foothold?",RawName="Foothold?",Guid="Foothold",PluginVersion="1.6.3",Location="plugins/FootholdForked/Foothold.dll",PackageRoot="plugins/FootholdForked"};
var fork=new OverviewMod{Name="Foothold?",RawName="Foothold?",Guid="Foothold-Forked",PluginVersion="1.5.1",Location="plugins/FootholdForked/plugins/Foothold-Forked.dll",PackageRoot="plugins/FootholdForked"};
var companion=new OverviewMod{Name="Foothold? 汉化",RawName="Foothold? 汉化",Guid="Foothold.zh",PluginVersion="1.0.0",Location="plugins/FootholdForked/FootholdZh.dll",PackageRoot="plugins/FootholdForked"};
var sameName=OverviewPluginCopies.SameNameCopies(new[]{upstream,fork,companion});
Check(sameName.Count==1&&sameName[0].Count==2&&!sameName[0].Contains(companion),"same name with different GUIDs in one folder grouped, companion excluded");
Check(OverviewPluginCopies.Duplicates(new[]{upstream,fork}).Count==0,"different GUIDs are still not plain duplicates");
string forkReport=ConfigurationIssueReport.Build(repo,new[]{upstream,fork,companion},formatted:true);
Check(forkReport.Contains("重复模组")&&forkReport.Contains("1.6.3")&&forkReport.Contains("1.5.1")&&forkReport.Contains("Foothold-Forked.dll")&&forkReport.Contains("两份都会被加载"),"fork/upstream pair reported as duplicate advice with versions and paths");
Check(!forkReport.Contains("汉化"),"companion left out of fork/upstream advice");
Check(ConfigurationIssueReport.Related(repo,new[]{upstream,fork,companion}).Count==2,"related view includes both fork/upstream copies only");
Check(OverviewPluginCopies.SameNameCopies(new[]{upstream,new OverviewMod{Name="Foothold?",RawName="Foothold?",Guid="Foothold",PluginVersion="1.6.3",Location="plugins/Other/Foothold.dll",PackageRoot="plugins/Other"}}).Count==0,"same name across different folders is not the same-folder case");
// Two copies of one GUID (the TrueFinalAscent case): toggles act per copy and are mutually exclusive; without a recorded choice each copy keeps its physical state.
var draft=new List<OverviewProfileMod>();
var newer=new OverviewMod{Name="TFA",Guid="tfa",PluginVersion="3.0.2",PluginGuids=new(){"tfa"},TargetEnabled=true,CopyKey="plugins/3玩法/TFA/CptPICHU-TrueFinalAscent"};
var older=new OverviewMod{Name="TFA",Guid="tfa",PluginVersion="3.0.0",PluginGuids=new(){"tfa"},TargetEnabled=true,CopyKey="plugins/CptPICHU-TrueFinalAscent"};
newer.CopyRows=older.CopyRows=new List<OverviewMod>{newer,older};
Check(OverviewEnablementDraft.Planned(draft,newer)&&OverviewEnablementDraft.Planned(draft,older),"no entry: both copies keep their physical state");
OverviewEnablementDraft.Set(draft,older,false);
Check(!OverviewEnablementDraft.Planned(draft,older)&&OverviewEnablementDraft.Planned(draft,newer)&&draft.Single().enabled&&draft.Single().copy==newer.CopyKey,"unchecking one of two live copies keeps the other as the chosen copy");
OverviewEnablementDraft.Set(draft,older,true);
Check(OverviewEnablementDraft.Planned(draft,older)&&!OverviewEnablementDraft.Planned(draft,newer)&&draft.Single().copy==older.CopyKey,"checking a copy moves the choice there and plans the other off");
OverviewEnablementDraft.Set(draft,newer,false);
Check(OverviewEnablementDraft.Planned(draft,older)&&!OverviewEnablementDraft.Planned(draft,newer)&&draft.Single().enabled,"unchecking the copy that is already off changes nothing");
OverviewEnablementDraft.Set(draft,older,false);
Check(!OverviewEnablementDraft.Planned(draft,older)&&!OverviewEnablementDraft.Planned(draft,newer)&&!draft.Single().enabled,"unchecking the chosen copy disables the GUID");
draft.Single().enabled=true;draft.Single().copy="plugins/removed-folder";
Check(OverviewEnablementDraft.Planned(draft,newer)&&OverviewEnablementDraft.Planned(draft,older),"a choice naming a folder that is gone counts as none");
var solo=new OverviewMod{Name="Solo",Guid="solo",PluginGuids=new(){"solo"},TargetEnabled=true,CopyKey="plugins/Solo"};
OverviewEnablementDraft.Set(draft,solo,false);OverviewEnablementDraft.Set(draft,solo,true);
Check(OverviewEnablementDraft.Planned(draft,solo)&&draft.Single(m=>m.guid=="solo").copy=="","a single copy keeps the plain per-GUID state without a copy choice");
Console.WriteLine($"PASS {n} production issue-report/selection checks");
namespace ModConfigEnhance{internal static class Loc{internal static string T(string a,string b,string c)=>b;internal static string F(string a,string b,string c,params object[] args)=>string.Format(b,args);internal static string LocalizeMessage(string s)=>s;}}
namespace ModConfigEnhance.Overview{
internal static class OverviewLanguageBridge{internal static string GetDisplayName(OverviewMod m)=>m.TranslatedName.Length>0?m.TranslatedName:m.Name;}
internal sealed class OverviewRepository{internal bool PlannedEnabled(OverviewMod mod)=>mod.TargetEnabled;}
internal sealed class OverviewMod{internal ModSourceKind SourceKind=ModSourceKind.Store;internal bool SourceConfirmed=true;internal string SourceNamespace="",SourceName="";internal List<OverviewPluginCopy> PluginCopies=new();internal List<OverviewMod> CopyRows=new();internal string CopyKey="";internal bool DuplicateCopy=>CopyRows.Count>1;internal string RawName="",TranslatedName="";internal string Location="",PackageRoot="";internal string Name="",Guid="",DisplayVersion="",PluginVersion="",PackageNamespace="",PackageName="",PackageVersion="",IdentityConflict="",ToggleReason="";internal bool TargetEnabled=true,CanToggle=true;internal List<string> PluginGuids=new(),AssemblyPaths=new(),PackageDependencies=new();internal List<OverviewDependency> RuntimeDependencies=new();internal OverviewMod? Outer;internal List<OverviewMod> Inner=new();}
internal sealed class OverviewProfileMod{public string id="",guid="",name="",version="";public bool enabled=true;public string copy="";public List<string> nestedEnabled=new();}
internal sealed class OverviewDependency{internal string Id="",MinimumVersion="";internal bool Optional;}
internal sealed class Match{internal bool Planned,Ignored;internal List<Member> Members=new();internal Rule Rule=new();}internal sealed class Member{internal string Name="";}internal sealed class Rule{internal string reason="";}
internal static class PackConflictService{internal static IEnumerable<Match> Evaluate(OverviewRepository repo,IReadOnlyList<OverviewMod> mods)=>Array.Empty<Match>();}
}
