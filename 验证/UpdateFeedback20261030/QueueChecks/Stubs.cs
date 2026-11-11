using System;
using System.Collections.Generic;
namespace BepInEx { public static class Paths { public static string BepInExRootPath=""; } }
namespace ModConfigEnhance.Overview {
internal static class Plugin { internal static readonly Logger Log=new(); internal class Logger { internal void LogError(object x){} } }
internal class OverviewModRule { }
internal class OverviewMod {
 public string Id="",Guid="test",Name="test",PackageRoot="",PackageVersion="1.0.0",LatestVersion="2.0.0",IdentityConflict="",SourceNamespace="Team",SourceName="Package",HomeUrl="",PluginVersion="";
 public string SourceKey=>SourceNamespace+"-"+SourceName;
 public bool HasUpdate=true,SourceConfirmed=true,Loaded,TargetEnabled=true;
 public ModSourceKind SourceKind=ModSourceKind.Store;
 public List<string> PluginGuids=new(),AssemblyPaths=new();public OverviewModRule Rule=new();
}
internal enum ModSourceKind { Store }
internal static class SourceUi { internal static string DependencyContext=>"sandbox"; }
internal static class Loc {
 internal static string T(string en,string cn,string tw)=>cn;
 internal static string F(string en,string cn,string tw,params object[] values)=>string.Format(cn,values);
 internal static string LocalizeMessage(string s)=>s;
}
internal class ChangedFileSnapshot<T> { internal T? Value; internal string Error="";internal void Refresh(string ctx,Func<IEnumerable<string>> files,Func<T> read){try{Value=read();Error="";}catch(Exception e){Error=e.Message;}} }
internal static class ShopQueueProjection { internal static ModConfigEnhance.Updating.UpdateJob ToJob(ModConfigEnhance.Updating.ShopTask t)=>new(); }
internal static class PackLibrary { internal static void EnsureAvailable(){} }
internal static class OverviewRepositoryHelpers { }
internal class OverviewRepository {
 internal bool Allow=true;internal bool IsUpdateAllowed(OverviewModRule r)=>Allow;
 internal Profiles Profiles=new();internal Profile ViewedProfile=new();internal Configs Configs=new();
 internal static IEnumerable<string> SafeFiles(string r)=>Array.Empty<string>();
}
internal class Profiles { internal List<Profile> ListProfiles()=>new(){new Profile{readOnly=true,id="author",mods=new(){new Recorded{guid="test",packageVersion="1.0.0"}}}}; }
internal class Profile { internal bool readOnly;internal string id="",source="author",directory="";internal List<Recorded> mods=new(); }
internal class Recorded { internal string guid="",packageVersion=""; }
internal class Configs { internal Dictionary<string,Entry> LoadedEntries()=>new(); }
internal class Entry { internal ConfigFile ConfigFile=new(); } internal class ConfigFile { internal string ConfigFilePath=""; }
internal static class PublishedPack { internal static Pack? Active=>null;internal class Pack { internal string Root="",DefaultProfile=""; } }
internal static class OverviewCatalogue { internal static bool IsUpdateCandidate(string x)=>false; }
internal static class OverviewUi { internal static void ConfirmText(string title,string text,Action action)=>action(); }
internal static class OverviewLanguageBridge { internal static string GetDisplayName(OverviewMod m)=>m.Name; }
internal static class ThunderstoreUpdateService { internal static bool ValidPart(string s)=>s.Length>0; }
internal sealed partial class OverviewListView { private OverviewRepository _repo=new();private Action<string> _status=_=>{};private IReadOnlyList<OverviewMod> Selected()=>Array.Empty<OverviewMod>(); }
}
