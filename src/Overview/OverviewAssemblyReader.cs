#nullable enable
using System.IO;
using BepInEx;
using Mono.Cecil;

namespace ModConfigEnhance.Overview;

internal static class OverviewAssemblyReader
{
    /// <summary>读取依赖特性中的枚举时，Cecil 需要找到 BepInEx；不用当前工作目录猜库路径。</summary>
    internal static DefaultAssemblyResolver ResolverFor(string file)
    {
        var resolver = new DefaultAssemblyResolver();
        string? directory = Path.GetDirectoryName(file);
        if (directory != null) resolver.AddSearchDirectory(directory);
        resolver.AddSearchDirectory(Path.Combine(Paths.BepInExRootPath, "core"));
        resolver.AddSearchDirectory(Paths.ManagedPath);
        return resolver;
    }
}
