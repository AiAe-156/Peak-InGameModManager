#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;

[Serializable]
internal sealed class PackConflictFile
{
    public int schemaVersion = 1;
    public List<PackConflictRule> rules = new();
}

[Serializable]
internal sealed class PackConflictRule
{
    internal const string AnyTwo = "any-two", All = "all";
    public string id = "", title = "", reason = "", mode = AnyTwo;
    public List<PackConflictMember> members = new();
}

[Serializable]
internal sealed class PackConflictMember
{
    public string guid = "", name = "";
}

// Resolved per GUID: several plugins in one package share a toggle unit, but not a loader instance.
internal sealed class PackConflictMemberState
{
    internal PackConflictMember Member = new();
    internal OverviewMod? Mod;
    internal string Guid => Member.guid;
    internal string Name => Mod?.Name ?? (string.IsNullOrWhiteSpace(Member.name) ? Guid : Member.name);
    internal string Unit = "";
    internal bool Planned, Loaded, NextLaunch;
    internal bool Missing => Mod == null;
}

internal sealed class PackConflictMatch
{
    internal string PackId = "", PackName = "", Identity = "";
    internal PackConflictRule Rule = new();
    internal List<PackConflictMemberState> Members = new();
    internal bool Planned, Loaded, NextLaunch, Ignored;
    internal bool Any => Planned || Loaded || NextLaunch;
    internal string Title => string.IsNullOrWhiteSpace(Rule.title) ? string.Join(" / ", Members.Select(m => m.Name)) : Rule.title;
    internal bool Involves(OverviewMod mod) => Members.Any(member => member.Guid.Equals(mod.Guid, StringComparison.OrdinalIgnoreCase)
        || mod.PluginGuids.Contains(member.Guid, StringComparer.OrdinalIgnoreCase));
}
