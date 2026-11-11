#nullable enable
using System.Collections.Generic;
namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewMod
{
    internal List<OverviewDependency> RuntimeDependencies = new();
    internal List<string> PackageDependencies = new();
    internal bool DependencyMetadataKnown;
}

internal sealed class OverviewDependency
{
    internal string Id = "";
    internal string MinimumVersion = "";
    internal bool Optional;
}

internal sealed class OverviewDependencyItem
{
    internal string Identity = "";
    internal string Name = "";
    internal string Version = "";
    internal string Required = "";
    internal bool Installed;
    internal bool Unknown;
    internal bool Optional;
    internal bool Framework;
    internal OverviewMod? Mod;
}
