using System.Collections.Generic;
namespace ModConfigEnhance.Overview;
internal sealed class OverviewProfileMod { public string id = "", guid = "", name = "", version = "", copy = ""; public bool enabled = true; public List<string> nestedEnabled = new(); }
internal sealed class OverviewMod
{
    public string Guid = "", Name = "", DisplayVersion = "", CopyKey = "";
    public bool TargetEnabled = true, CanToggle = true;
    public List<string> PluginGuids = new();
    internal List<OverviewMod> CopyRows = new();
    internal bool DuplicateCopy => CopyRows.Count > 1;
    internal OverviewMod? Outer;
    internal List<OverviewMod> Inner = new();
}
internal static class Loc { internal static string F(string en, string zh, string tw, params object[] args) => string.Format(zh, args); }
