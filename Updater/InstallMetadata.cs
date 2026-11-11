using Mono.Cecil;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal sealed record PluginDependency(string Guid, string? MinimumVersion, bool Soft);
internal sealed record PluginInfo(string Guid, string Name, string Version, string File, List<PluginDependency> Dependencies, string TypeName);

/// <summary>Metadata-only assembly reads that never fail a job: anything unreadable yields no plugins plus a warning.</summary>
internal static class InstallMetadata
{
    internal static List<PluginInfo> Read(string root, string file, List<string>? warnings = null)
    {
        var result = new List<PluginInfo>();
        try
        {
            using var resolver = new DefaultAssemblyResolver();
            foreach (string directory in SearchDirectories(root, file)) resolver.AddSearchDirectory(directory);
            using var assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = false, ReadingMode = ReadingMode.Deferred });
            foreach (var type in Types(assembly.MainModule.Types))
            {
                CustomAttribute? plugin;
                try { plugin = type.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "BepInEx.BepInPlugin" && a.ConstructorArguments.Count == 3); }
                catch (Exception e) { warnings?.Add(Warn(file, e)); continue; }
                if (plugin == null) continue;
                var dependencies = new List<PluginDependency>();
                foreach (var attribute in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInDependency"))
                {
                    try
                    {
                        if (attribute.ConstructorArguments.Count == 0 || attribute.ConstructorArguments[0].Value is not string guid || guid.Length == 0) continue;
                        string? minimum = null; bool soft = false;
                        if (attribute.ConstructorArguments.Count > 1)
                        {
                            object? flags = attribute.ConstructorArguments[1].Value;
                            if (flags is string version) minimum = version;
                            else if (flags != null) soft = (Convert.ToInt32(flags, System.Globalization.CultureInfo.InvariantCulture) & 2) != 0;
                        }
                        dependencies.Add(new PluginDependency(guid, minimum, soft));
                    }
                    catch (Exception e) { warnings?.Add(Warn(file, e)); }
                }
                result.Add(new PluginInfo(Text(plugin, 0), Text(plugin, 1), Text(plugin, 2), file, dependencies, type.FullName ?? ""));
            }
        }
        catch (BadImageFormatException) { }
        catch (Exception e) { warnings?.Add(Warn(file, e)); }
        return result;
    }

    internal static List<PluginInfo> ReadTree(string root, string directory, List<string>? warnings = null)
    {
        var result = new List<PluginInfo>();
        foreach (string file in InstallArchive.Walk(directory))
            if (PluginFileNames.IsAssembly(file)) result.AddRange(Read(root, file, warnings));
        return result;
    }

    internal static IEnumerable<string> SearchDirectories(string root, string file)
    {
        string? own = Path.GetDirectoryName(file);
        if (own != null && Directory.Exists(own)) yield return own;
        string core = Path.Combine(root, "core");
        if (Directory.Exists(core)) yield return core;
        string managed = Path.Combine(Files.GameRootFor(root), "PEAK_Data", "Managed");
        if (Directory.Exists(managed)) yield return managed;
    }

    static string Text(CustomAttribute attribute, int index) => attribute.ConstructorArguments[index].Value?.ToString() ?? "";
    static string Warn(string file, Exception e) => "插件元数据未能完整读取（已忽略）：" + Path.GetFileName(file) + " — " + e.Message;
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types) { yield return type; foreach (var nested in Types(type.NestedTypes)) yield return nested; }
    }
}
