#nullable enable
using System;
namespace ModConfigEnhance.Overview;
internal static class ModUpdateMarkerPolicy
{
    internal static bool IsUpgrade(ChangeRecord? record) => record != null && record.kind == "changed"
        && Version.TryParse(record.before,out var before) && Version.TryParse(record.after,out var after) && after > before;
    internal static string Scope(string packId) => "installed:" + (string.IsNullOrEmpty(packId) ? "unmanaged" : packId);
}
