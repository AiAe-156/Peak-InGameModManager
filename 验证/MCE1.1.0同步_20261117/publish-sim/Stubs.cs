// 只为让 PublicationScrub.cs 单独编译：它用到 Plugin.Log 与 OverviewConfigSnapshot（本夹具不走 ScrubSnapshot）。
using System.Collections.Generic;
namespace ModConfigEnhance
{
    internal sealed class StubLog { internal void LogWarning(string text) => System.Console.WriteLine("WARN " + text); }
    internal static class Plugin { internal static readonly StubLog Log = new(); }
}
namespace ModConfigEnhance.Updating
{
    // PackContracts.cs 里其他记录类型引用到的类型；本夹具只用其中的 PackEnvironmentPolicy。
    public sealed class PackLanguage { }
    public sealed class MaintenanceRequest { }
    public sealed class MaintenancePreview { }
}
namespace ModConfigEnhance.Overview
{
    internal sealed class OverviewConfigValue { public string configFile = "", pluginGuid = "", section = "", key = "", serializedValue = ""; }
    internal sealed class OverviewConfigSnapshot { public List<OverviewConfigValue> values = new(); }
}
