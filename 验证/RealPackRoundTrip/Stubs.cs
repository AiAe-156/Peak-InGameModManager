using System.Collections;

namespace BepInEx
{
    internal static class Paths { internal static string PluginPath = "", BepInExRootPath = "", ConfigPath = "", ManagedPath=""; }
    internal sealed class BepInDependency { internal enum DependencyFlags { HardDependency=1, SoftDependency=2 } internal string DependencyGUID=""; internal Version? MinimumVersion; internal DependencyFlags Flags; }
}
namespace BepInEx.Bootstrap
{
    internal static class Chainloader { internal static readonly Dictionary<string, Info> PluginInfos = new(); }
    internal sealed class Info { internal Metadata Metadata = new(); internal Instance? Instance; internal string Location = ""; internal List<BepInEx.BepInDependency> Dependencies=new(); }
    internal sealed class Instance { internal BepInEx.Configuration.ConfigFile Config = new(); }
    internal sealed class Metadata { internal string GUID = "",Name=""; internal Version Version=new(2,0,0); }
}
namespace BepInEx.Configuration
{
    internal sealed class ConfigFile : IEnumerable<KeyValuePair<ConfigDefinition, ConfigEntryBase>>
    {
        public IEnumerator<KeyValuePair<ConfigDefinition, ConfigEntryBase>> GetEnumerator() => Enumerable.Empty<KeyValuePair<ConfigDefinition, ConfigEntryBase>>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    internal sealed class ConfigDefinition { internal string Section = "", Key = ""; }
    internal sealed class ConfigEntryBase { internal string GetSerializedValue() => ""; internal void SetSerializedValue(string value) { } }
}
namespace ModConfigEnhance
{
    internal static class Loc
    {
        internal static string RootDirectory = "";
        internal static Dictionary<string,string> SnapshotLanguages(IEnumerable<string> languages) => Directory.Exists(Path.Combine(RootDirectory,"language")) ? Directory.GetFiles(Path.Combine(RootDirectory,"language"),"*.txt",SearchOption.AllDirectories).ToDictionary(p=>Path.GetRelativePath(Path.Combine(RootDirectory,"language"),p), File.ReadAllText) : new();
        internal static void ExportLanguages(string target,IEnumerable<string>? languages) { foreach(var item in SnapshotLanguages(languages ?? Array.Empty<string>())) Overview.OverviewRepository.AtomicWrite(Path.Combine(target,item.Key),item.Value); }
        internal static string FollowStem() => "zh-cn";
        internal static string T(string en, string cn, string tw) => en;
        internal static string F(string en, string cn, string tw, params object[] values) => string.Format(en, values);
    }
    internal static class Plugin { internal const string PluginGuid="com.aiae.modconfigenhance", PluginVersion="2.0.0"; internal static LogSink Log = new(); internal static StringSetting? LanguageFile = new(); internal static string FixNaming(string s)=>s; }
    internal sealed class StringSetting { internal string Value = ""; }
    internal sealed class LogSink { internal List<string> Warnings = new(); internal void LogWarning(string value) { Warnings.Add(value); Console.WriteLine(value); } internal void LogDebug(string value) => Console.WriteLine(value); }
}
namespace ModConfigEnhance.Overview
{
    internal sealed class OverviewTagSelection { }
    internal sealed class OverviewHotkeyPreset { }
    internal sealed class OverviewIgnoredHotkeyConflict { }
    // The publication writer and RSA signature code are production sources. Unity/configuration edges stay outside this test.
    internal sealed class OverviewRepository
    {
        internal OverviewState State = new();
        internal string ConfigRootDirectory => ModConfigEnhance.Loc.RootDirectory;
        internal ProfileAdapter Profiles = new();
        internal ConfigAdapter Configs = new();
        internal OverviewProfile ViewedProfile => Profiles.Items[0];
        internal string ViewedProfileId => ViewedProfile.id;
        internal OverviewModRule GetRule(string guid)=>State.mods.FirstOrDefault(m=>m.id==guid)??new OverviewModRule {id=guid};
        internal List<OverviewProfileExport.ExportItem> PrepareExport(IEnumerable<string> ids) => OverviewProfileExport.PrepareItems(Profiles.ListProfiles(), ids);
        internal static string SafePath(string root,string relative) => OverviewProfileAssets.CheckedPath(root,relative);
        internal bool PlannedEnabled(OverviewMod mod) => mod.TargetEnabled;
        internal void SaveUiState() { }
        internal static IEnumerable<string> SafeFiles(string directory) => Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories) : Enumerable.Empty<string>();
        internal static void AtomicWrite(string file, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, text);
            if (File.Exists(file)) File.Replace(temporary, file, file + ".bak");
            else File.Move(temporary, file);
        }
    }
    internal static class OverviewProfileAssets
    {
        internal const long FileLimitBytes=1073741824;
        internal static bool SupportedMedia(string path) => new[]{".png",".jpg",".jpeg",".gif",".webp",".mp4",".webm",".ogg",".wav",".mp3"}.Contains(Path.GetExtension(path).ToLowerInvariant());
        internal static string CheckedPath(string root, string relative)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Outside the payload root.");
            return full;
        }
    }
    internal sealed class ProfileAdapter { internal List<OverviewProfile> Items=new(); internal List<OverviewProfile> ListProfiles()=>Items; }
    internal sealed class ConfigAdapter { internal ConfigAdapter PrepareApply(OverviewConfigSnapshot snapshot)=>this; internal void Validate(){} }
    internal static class OverviewUpdateQueue { internal static void EnsureWorker(){} internal static void EnsurePublicationSafe(IEnumerable<string> roots){} internal static string PreparedVersion(string guid,string root)=>""; }
    internal static class XUnityBridge { internal static bool TryGetExactTranslation(string text,out string translated) { translated=text; return false; } }
    internal static class IntroductionFontRegistry { internal static string? FindFontFile(string name,string directory)=>File.Exists(Path.Combine(directory,name))?Path.Combine(directory,name):null; }
    internal static class InstallationPaths { internal static bool AtGameRoot(string path)=>Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).Equals(PackPublisher.GameRoot.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase); }
    internal static class ThunderstoreUpdateService { internal static bool ValidPart(string p)=>System.Text.RegularExpressions.Regex.IsMatch(p,@"^[A-Za-z0-9_]+$"); internal static string HomeUrl(string ns,string name)=>"https://thunderstore.io/c/peak/p/"+ns+"/"+name+"/"; internal static bool TryParseSource(string source,out string ns,out string name) { var p=source.Split('-'); ns=p.ElementAtOrDefault(0)??""; name=p.ElementAtOrDefault(1)??""; return p.Length>=2&&ValidPart(ns)&&ValidPart(name); } }
}
