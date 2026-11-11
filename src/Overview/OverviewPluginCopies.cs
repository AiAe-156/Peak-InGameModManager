#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;
// Plugin identity evidence, separate from package-wide toggle ownership.
internal sealed class OverviewPluginCopy
{
    internal string Guid = "", Name = "", Version = "", Path = "";
}
internal static class OverviewPluginCopies
{
    internal static IEnumerable<OverviewPluginCopy> For(OverviewMod mod) => mod.PluginCopies.Count > 0
        ? mod.PluginCopies : new[] { new OverviewPluginCopy { Guid = mod.Guid, Name = mod.Name, Version = mod.PluginVersion, Path = mod.Location } };
    internal static List<List<OverviewPluginCopy>> Duplicates(IEnumerable<OverviewMod> mods) => mods.SelectMany(For)
        .Where(p => p.Guid.Length > 0 && p.Path.Length > 0)
        .GroupBy(p => p.Guid, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.GroupBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Select(p => p.First()).ToList())
        .Where(g => g.Count > 1).ToList();
    internal static bool Contains(OverviewMod mod, IEnumerable<List<OverviewPluginCopy>> groups) =>
        groups.Any(g => For(mod).Any(p => g.Any(c => c.Guid.Equals(p.Guid, StringComparison.OrdinalIgnoreCase)
            && c.Path.Equals(p.Path, StringComparison.OrdinalIgnoreCase))));
    // One package folder, one display name, several plugin identities: an update that changed the GUID (fork → upstream)
    // left the superseded copy loadable beside the new one. Duplicates above never see it because the GUIDs differ.
    internal static List<List<OverviewMod>> SameNameCopies(IEnumerable<OverviewMod> mods) => mods
        .Where(m => m.Guid.Length > 0 && m.PackageRoot.Length > 0 && OriginalName(m).Length > 0)
        .GroupBy(m => m.PackageRoot, StringComparer.OrdinalIgnoreCase)
        .SelectMany(package => package.GroupBy(OriginalName, StringComparer.OrdinalIgnoreCase)
            .Where(name => name.Select(m => m.Guid).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(name => name.ToList()))
        .ToList();
    private static string OriginalName(OverviewMod mod) => string.IsNullOrWhiteSpace(mod.RawName) ? mod.Name : mod.RawName;
}
