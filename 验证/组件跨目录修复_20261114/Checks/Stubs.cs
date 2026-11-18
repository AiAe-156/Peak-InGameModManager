namespace BepInEx { internal static class Paths { internal static string BepInExRootPath = ""; } }
namespace ModConfigEnhance { internal static class Loc { internal static string T(string en,string zh,string tw)=>en; } }
namespace ModConfigEnhance.Overview {
    internal static class ThunderstoreUpdateService { internal static bool IsNewer(string a,string b)=>System.Version.TryParse(a,out var x)&&System.Version.TryParse(b,out var y)&&x>y; }
    internal static class OverviewUpdateQueue { internal static string CompletedVersion(string team,string name)=>""; }
    internal static class OverviewTagSelection { internal const string Unknown="?"; }
    internal static class ResourceOperationDraft {
        internal sealed class Change { internal Entry? Shop,Job; }
        internal sealed class Entry { internal string Namespace="",Package=""; }
        internal static Change[] Snapshot()=>System.Array.Empty<Change>();
    }
}
