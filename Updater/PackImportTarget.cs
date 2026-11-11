using Mono.Cecil;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal static class PackImportTarget
{
    // Check staged target directly: storage.Content(pack) must NOT be used before commit.
    internal static List<string> Check(PackStorage storage, string content)
    {
        var issues = new List<string>();
        try { WorkerTargetProtocol.ValidateTargetContent(content); }
        catch (Exception error) { issues.Add("独立环境管理协议/工具不完整：" + error.Message); }
        var assemblies = new List<(string Path, string Name, string Token, Version Version, List<AssemblyNameReference> References)>();
        var plugins = new List<Packages.Plugin>();
        foreach (var file in Files.Inventory(content).Keys.Where(Packages.IsAssembly))
        {
            string full = Files.Child(content, file);
            try
            {
                using var assembly = AssemblyDefinition.ReadAssembly(full);
                // Disabled mods are preserved, but not providers for enabled dependencies.
                if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                assemblies.Add((file, assembly.Name.Name, Token(assembly.Name), assembly.Name.Version, assembly.MainModule.AssemblyReferences.ToList()));
                plugins.AddRange(Packages.ReadPlugins(full));
            }
            catch (BadImageFormatException) { }
        }
        foreach (var group in assemblies.GroupBy(a => a.Name, Files.Compare).Where(g => g.Count() > 1)) issues.Add("目标程序集同名冲突：" + group.Key);
        foreach (string name in new[] { "BepInEx", "BepInEx.Preloader", "0Harmony" })
            if (!assemblies.Any(a => a.Name == name && a.Path.Replace('\\', '/').StartsWith("core/", StringComparison.OrdinalIgnoreCase))) issues.Add("独立环境缺少 core 加载器/基础库：" + name);
        if (!plugins.Any(p => ManagerIdentity.IsManagerGuid(p.Guid))) issues.Add("独立环境缺少启用的 MCE 宿主。");
        string managed = Path.Combine(Files.GameRootFor(storage.Root), "PEAK_Data", "Managed");
        foreach (var owner in assemblies)
        foreach (var reference in owner.References)
        {
            if (assemblies.Any(a => Files.Compare.Equals(a.Name, reference.Name) && a.Token == Token(reference) && a.Version >= reference.Version)) continue;
            if (reference.Name is "mscorlib" or "netstandard") continue;
            // Loader / mod libraries are never platform substitutes, even if copied into Managed.
            string platform = Files.Child(managed, reference.Name + ".dll");
            if (!reference.Name.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase) && File.Exists(platform))
            {
                Files.RejectLinks(managed, platform);
                using var provided = AssemblyDefinition.ReadAssembly(platform);
                if (provided.Name.Name == reference.Name && Token(provided.Name) == Token(reference) && provided.Name.Version >= reference.Version) continue;
            }
            issues.Add(owner.Path + " 缺少目标程序集：" + reference.Name + " >= " + reference.Version);
        }
        return issues;
    }
    static string Token(AssemblyNameReference name) => FrameworkCompat.Hex(name.PublicKeyToken ?? Array.Empty<byte>());
}
