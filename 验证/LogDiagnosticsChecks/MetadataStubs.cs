// External game/configuration boundaries only; the JSON serializer under test is linked production code.
using Newtonsoft.Json.Linq;
namespace UnityEngine { internal static class Application { internal static string version = "3.1.0--logFile", unityVersion = "6000.3.15f1"; } }
namespace BepInEx
{
    internal class BaseUnityPlugin { }
    internal static class Paths { internal static string BepInExRootPath = ""; }
}
namespace BepInEx.Bootstrap
{
    internal sealed class PluginInfo { internal object? Instance = null; }
    internal static class Chainloader { internal static Dictionary<string, PluginInfo> PluginInfos = new(); }
}
namespace ModConfigEnhance { internal static class Plugin { public const string PluginVersion = "0.0.1"; } }
namespace ModConfigEnhance.Overview
{
    internal sealed class OverviewMod { internal string RawName = "", Guid = "", PluginVersion = "", Location = ""; internal bool ResourcePack; }
    internal enum OverviewConfigDiffKind { Equal, Changed }
    internal sealed class ConfigValue { internal string pluginGuid = "", section = "", key = "", serializedValue = ""; }
    internal sealed class ConfigDiff { internal OverviewConfigDiffKind Kind = OverviewConfigDiffKind.Equal; internal ConfigValue? ProfileValue = null, CurrentValue = null; }
    internal sealed class ConfigService
    {
        internal JObject Capture() => new();
        internal IEnumerable<ConfigDiff> Diff(JObject a, JObject b) => Array.Empty<ConfigDiff>();
    }
    internal sealed class ProfileMod { public string guid = ""; public bool enabled = false; }
    internal sealed class Profile { internal string id = "", name = ""; internal bool IsValid = true; internal JObject config = new(); internal List<ProfileMod> mods = new(); }
    internal sealed class ProfileStore { internal Profile Applied = new(); internal string AppliedId = ""; internal IEnumerable<Profile> ListProfiles() => Array.Empty<Profile>(); }
    internal sealed class Pack { internal string Name = "", name = ""; }
    internal sealed class State { internal Pack pack = new(); }
    internal static class PackLibrary { internal static Pack? Active = null; }
    internal sealed partial class OverviewRepository
    {
        internal Profile? _appliedSnapshot = null;
        internal JObject _pendingConfiguration = new();
        internal ConfigService Configs = new();
        internal ProfileStore Profiles = new();
        internal State State = new();
    }
    internal static class InfrastructurePackages
    {
        internal static List<InfrastructureOverviewModel> Local = new();
        internal static IEnumerable<InfrastructureOverviewModel> Scan() => Local;
    }
    internal sealed class InfrastructureOverviewModel
    {
        internal sealed class Location { internal string Area = "", Path = ""; internal bool IdentityConfirmed = true, ManifestVerified = false; }
        internal string Name = "", Key = "", Version = "";
        internal bool IdentityConfirmed = true, Enabled = true;
        internal List<string> RuntimeFiles = new(), Areas = new();
        internal List<Location> Locations = new();
        internal static IEnumerable<InfrastructureOverviewModel> Create(IEnumerable<InfrastructureOverviewModel> local) => local;
    }
}
