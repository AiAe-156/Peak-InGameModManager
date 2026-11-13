using ModConfigEnhance.Overview;
using Newtonsoft.Json;
int n=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
var repo=new OverviewRepository();repo.Seed();
var result=repo.SaveConfiguration(Array.Empty<OverviewMod>(),ConfigurationSaveScope.Hotkeys);
Check(result.Success,"unrelated external parameter conflict must not block hotkey save");
Check(repo.AppliedTargets!.values.Count==1&&repo.AppliedTargets.values[0].key=="Key","only hotkeys applied");
Check(repo.Draft.values.Single(v=>v.key=="Number").serializedValue=="2","unrelated unsaved parameter retained");
Check(repo.Saved.config.values.Single(v=>v.key=="Number").serializedValue=="1","unrelated parameter not persisted");
Check(repo.Saved.config.values.Single(v=>v.key=="Key").serializedValue=="F2","hotkey persisted");
result=repo.SaveConfiguration(Array.Empty<OverviewMod>(),ConfigurationSaveScope.Parameters);
Check(!result.Success&&result.Message.StartsWith("MCE_EXTERNAL_CONFLICT:"),"actual overlapping external edit is reported");
result=repo.SaveConfiguration(Array.Empty<OverviewMod>(),ConfigurationSaveScope.Parameters,overwriteExternal:true);
Check(result.Success,"explicit external overwrite saves selected range");
repo.Seed();repo.Fail=true;result=repo.SaveConfiguration(Array.Empty<OverviewMod>(),ConfigurationSaveScope.Hotkeys);
Check(!result.Success&&repo.Saved.config.values.Single(v=>v.key=="Key").serializedValue=="F1","failed scoped save keeps old baseline");
Check(repo.Draft.values.Single(v=>v.key=="Key").serializedValue=="F2","failed scoped save keeps edited draft");
repo.Seed();repo.Draft.values.Single(v=>v.key=="Number").serializedValue="1";result=repo.SaveConfiguration(Array.Empty<OverviewMod>());
Check(result.Success&&repo.Draft.values.Single(v=>v.key=="Number").serializedValue=="3","unmodified field adopts external edit instead of silently reverting it");
repo.Seed();repo.Fail=false;repo._draftMods.Add(new(){guid="mod",enabled=false});repo._savedDraftProfile.mods.Add(new(){guid="mod",enabled=true});
result=repo.SaveConfiguration(new[]{new OverviewMod{Guid="mod",TargetEnabled=true}},overwriteExternal:true,skipEnablement:true);
Check(result.Success&&repo.Saved.mods.Single().enabled,"skip enablement keeps committed baseline at installed target");
Check(!repo._draftMods.Single().enabled,"skip enablement retains unsaved requested target");
string home=Path.Combine(AppContext.BaseDirectory,"scope-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(home);
repo=new OverviewRepository();repo.Seed();repo.Profiles.RootDirectory=home;repo.AppliedSnapshotPath=Path.Combine(home,"applied.json");repo.RecoveryDraftPath=Path.Combine(home,"draft.json");
File.WriteAllText(PackWorkspaceInformation.PathFor(home),"previous pack");File.WriteAllText(repo.AppliedSnapshotPath,"previous applied");File.WriteAllText(repo.RecoveryDraftPath,"previous draft");
repo._savedDraftProfile.state.pack.name="old";repo.State.pack.name="new";repo._appliedSnapshot=new(){state=new(){pack=new(){name="old"}}};repo.FailRecovery=true;
bool rejected=false;try{repo.SavePackInformation(ConfigurationSaveScope.Information);}catch(IOException){rejected=true;}
Check(rejected,"recovery persistence fault rejects local information save");
Check(File.ReadAllText(PackWorkspaceInformation.PathFor(home))=="previous pack"&&File.ReadAllText(repo.AppliedSnapshotPath)=="previous applied"&&File.ReadAllText(repo.RecoveryDraftPath)=="previous draft","local metadata fault restores all files using production checkpoint");
Check(repo.Saved.state.pack.name=="old"&&repo._appliedSnapshot.state.pack.name=="old"&&repo.State.pack.name=="new","failed local save restores baselines without consuming edited information");
repo.FailRecovery=false;repo.SavePackInformation(ConfigurationSaveScope.Information);Check(repo.Saved.state.pack.name=="new"&&repo.Draft.values.Single(v=>v.key=="Number").serializedValue=="2","local information save retains unrelated parameter draft");
repo.State.pack.name="unrelated information draft";repo.State.pack.introDocument="new-intro.json";repo.SavePackInformation(ConfigurationSaveScope.Introduction);Check(repo.Saved.state.pack.name=="new"&&repo.Saved.state.pack.introDocument=="new-intro.json","introduction save commits only introduction paths and flags, not information draft");Check(repo.State.pack.name=="unrelated information draft","introduction save preserves unsaved information edit");
repo.State.mods.AddRange(new[]{new OverviewModRule{id="a",alias="A draft"},new OverviewModRule{id="b",alias="B draft"}});repo.SavePackInformation(ConfigurationSaveScope.ModInformation,"a");Check(repo.Saved.state.mods.Single(r=>r.id=="a").alias=="A draft"&&!repo.Saved.state.mods.Any(r=>r.id=="b"),"save mod information isolates selected mod from other staged mod edits");Check(repo.Saved.state.pack.name=="new"&&repo.State.mods.Single(r=>r.id=="b").alias=="B draft","save mod information retains pack and other mod drafts");
Console.WriteLine($"PASS {n} production range-save checks");
namespace ModConfigEnhance.Overview{
internal sealed class OverviewMod{internal string Guid="";internal List<string> PluginGuids=new();internal bool TargetEnabled;}
internal sealed class OverviewState{public OverviewPack pack=new();public List<OverviewGroup> groups=new();public List<OverviewModRule> mods=new();}
internal sealed class OverviewPack{public string name="",introDocument="",introFile="";public bool introEnabled;}internal sealed class OverviewGroup{}
internal sealed class OverviewModRule{public string id="",groupId="",versionLabel="",alias="",description="";public int order;public bool updateAllowed=true,updateOverridden,descriptionOverridden;}
internal readonly record struct OverviewToggleResult(bool Success,string Message);
internal static class OverviewHotkeyAdapter{internal static object? Create(object e)=>null;}
internal static class PackWorkspaceInformation{internal static string PathFor(string root)=>Path.Combine(root,"pack-information.json");internal static void Write(string a,OverviewState b,string c){File.WriteAllText(PathFor(a),JsonConvert.SerializeObject(b));}}
internal static class OverviewPageCache{internal static void ConfigurationSaved(){}}
internal sealed class Store{internal string RootDirectory="";}
internal sealed class ConfigService{internal string? Reconcile(OverviewConfigSnapshot baseline,OverviewConfigSnapshot draft,Func<OverviewConfigValue,bool> match,bool overwrite){foreach(var target in draft.values.Where(match)){var start=baseline.values.FirstOrDefault(v=>v.Identity==target.Identity);var now=Current.values.FirstOrDefault(v=>v.Identity==target.Identity);if(start==null||now==null)continue;if(target.serializedValue==start.serializedValue)target.serializedValue=now.serializedValue;else if(!overwrite&&now.serializedValue!=start.serializedValue&&now.serializedValue!=target.serializedValue)return "MCE_EXTERNAL_CONFLICT: "+target.Identity;}return null;}internal OverviewConfigSnapshot Current=new();internal OverviewConfigSnapshot Capture(OverviewConfigSnapshot x)=>Current;internal object? FindLoadedEntry(OverviewConfigValue v)=>null;}
internal sealed partial class OverviewRepository{
internal OverviewState State=new();internal OverviewConfigSnapshot _draftConfig=new(),_editingRuntimeBaseline=new();internal List<OverviewProfileMod> _draftMods=new();internal OverviewProfile _savedDraftProfile=new();internal string _savedDraftSignature="",ViewedProfileId="p",AppliedSnapshotPath="",PackDirectory="",RecoveryDraftPath="";internal int _loadedRevision;internal static int _sharedRevision;internal bool IsMainMenuScene=true,Fail,FailRecovery;internal ConfigService Configs=new();internal Store Profiles=new();internal OverviewProfile? _appliedSnapshot;internal OverviewConfigSnapshot? AppliedTargets;internal OverviewConfigSnapshot Draft=>_draftConfig;internal OverviewProfile Saved=>_savedDraftProfile;
static T Clone<T>(T x)=>JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(x))!;
static string DraftSignature(OverviewState s,OverviewConfigSnapshot c,IEnumerable<OverviewProfileMod> m)=>JsonConvert.SerializeObject(c);
void EnsureLocalPackCopy(){}void MergeEditedRules(){}void PersistRecoveryDraft(){if(FailRecovery)throw new IOException("Injected recovery persistence fault");}void InvalidatePendingView(){}void FlushRecoveryDraft(){}static void AtomicWrite(string a,string b){File.WriteAllText(a,b);}
internal void Seed(){Fail=false;var number=new OverviewConfigValue{pluginGuid="m",configFile="m.cfg",section="S",key="Number",type="System.Int32",serializedValue="1"};var key=new OverviewConfigValue{pluginGuid="m",configFile="m.cfg",section="S",key="Key",type="UnityEngine.KeyCode",serializedValue="F1"};_savedDraftProfile=new(){config=new(){values=new(){number,key}}};_draftConfig=Clone(_savedDraftProfile.config);_draftConfig.values[0].serializedValue="2";_draftConfig.values[1].serializedValue="F2";_editingRuntimeBaseline=Clone(_savedDraftProfile.config);Configs.Current=Clone(_editingRuntimeBaseline);Configs.Current.values[0].serializedValue="3";}
OverviewToggleResult ApplyProfileTransaction(IReadOnlyList<OverviewMod> m,bool menu,bool skipEnablement=false,OverviewConfigSnapshot? parameterTargets=null,bool ignoreDependencyWarnings=false){if(Fail)return new(false,"failure");AppliedTargets=Clone(parameterTargets??_draftConfig);_savedDraftProfile=new(){state=Clone(State),config=Clone(_draftConfig),mods=Clone(_draftMods)};if(skipEnablement)foreach(var target in _savedDraftProfile.mods){var installed=m.FirstOrDefault(i=>i.Guid==target.guid);if(installed!=null)target.enabled=installed.TargetEnabled;}return new(true,"saved");}
}
}
namespace ModConfigEnhance{
// 1.1.4 stub: the real one copies the profile's controller presets into this PC's combos.
internal static class GamepadCombos{internal static void ApplyPresets(ModConfigEnhance.Overview.OverviewState s){}}
}
