#nullable enable
using System;

namespace ModConfigEnhance.Overview;

// The profile owns its display name. pack.name is a compatibility mirror;
// identity, publication ID and update-source fields are independent of naming.
internal static class OverviewProfileNames
{
    internal static string DefaultName => Loc.T("Default configuration", "默认配置", "預設設定");
    internal static void Normalize(OverviewProfile profile, bool readLegacyNames = false)
    {
        profile.state ??= new(); profile.state.pack ??= new();
        string name = profile.name?.Trim() ?? "";
        if (profile.kind == OverviewProfileKind.Default) name = DefaultName;
        else if (readLegacyNames)
        {
            if (!string.IsNullOrWhiteSpace(profile.state.packNameOverride)) name = profile.state.packNameOverride.Trim();
            else if (name.Length == 0 && !string.IsNullOrWhiteSpace(profile.state.pack.name)) name = profile.state.pack.name.Trim();
        }
        if (name.Length == 0) name = Loc.T("Current configuration", "现有配置", "現有配置");
        profile.name = name;
        if (profile.state.pack.name.Length == 0) profile.state.pack.name = name;
        profile.state.packNameOverride = "";
    }
}
