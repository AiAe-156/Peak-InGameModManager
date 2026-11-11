using System.Text.Json;
using Mono.Cecil;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

/// <summary>Target-only completeness checks. Never borrows current core or HostPlugins.</summary>
internal static class PackCompleteEnvironment
{
    // E installs the production protocol/restore-entry validator. Null deliberately fails closed.
    internal static Action<PackStorage, PackRecord>? ManagementProtocolCheck;
    internal static void RequireComplete(PackStorage storage, PackRecord pack)
    {
        if (!PackEnvironmentPolicy.IsComplete(pack)) throw new IOException("目标是旧环境，请先明确补全并升级完整环境，不能继承当前 core。为了避免混用，完整环境与旧环境不能直接切换。");
        storage.Validate(pack);
        var issues = CheckDependencies(storage, pack);
        if (issues.Count > 0) throw new IOException(string.Join("\n", issues));
        if (ManagementProtocolCheck == null) throw new IOException("完整环境管理协议检查尚未接入，禁止切换；需要 protocol2 MCE 及游戏外恢复入口。");
        ManagementProtocolCheck(storage, pack);
    }

    internal static List<string> CheckDependencies(PackStorage storage, PackRecord pack)
    {
        var issues = new List<string>();
        var assemblies = new List<(string Path, string Name, string Token, Version Version, List<AssemblyNameReference> References)>();
        var plugins = new List<(string Guid, string Version)>();
        var dependencies = new List<(string Path, string Guid, string? Minimum)>();
        string content = storage.Content(pack);
        foreach (var file in pack.Files.Where(f => f.Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !f.Path.Replace('\\', '/').Contains("/Tools/Updater/", StringComparison.OrdinalIgnoreCase)))
        {
            string path = Files.Child(content, file.Path); Files.RejectLinks(storage.Home, path);
            try
            {
                using var assembly = AssemblyDefinition.ReadAssembly(path);
                assemblies.Add((file.Path, assembly.Name.Name, Token(assembly.Name), assembly.Name.Version, assembly.MainModule.AssemblyReferences.ToList()));
                foreach (var type in Types(assembly.MainModule.Types))
                {
                    foreach (var identity in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInPlugin" && a.ConstructorArguments.Count == 3))
                        plugins.Add((identity.ConstructorArguments[0].Value.ToString()!, identity.ConstructorArguments[2].Value.ToString()!));
                    foreach (var dependency in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInDependency" && a.ConstructorArguments.Count > 0))
                    {
                        object? value = dependency.ConstructorArguments.Count > 1 ? dependency.ConstructorArguments[1].Value : null;
                        if (value != null && value is not string && (Convert.ToInt32(value) & 2) != 0) continue;
                        dependencies.Add((file.Path, dependency.ConstructorArguments[0].Value.ToString()!, value as string));
                    }
                }
            }
            catch (BadImageFormatException) { } // native DLL, not a plugin provider
        }
        foreach (var group in assemblies.GroupBy(a => a.Name, Files.Compare).Where(g => g.Count() > 1))
            issues.Add("目标程序集同名冲突：" + group.Key + " [" + string.Join(", ", group.Select(a => a.Path + " token=" + a.Token + " version=" + a.Version)) + "]");
        foreach (string name in new[] { "BepInEx", "BepInEx.Preloader", "0Harmony" })
            if (!assemblies.Any(a => a.Name == name && a.Path.StartsWith("core/", StringComparison.OrdinalIgnoreCase)))
                issues.Add("目标缺少必要核心程序集：" + name);
        if (!assemblies.Any(a => a.Name == "ModConfigEnhance" && a.Path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase))
            || !plugins.Any(p => p.Guid == "com.aiae.modconfigenhance")) issues.Add("目标缺少启用的 MCE 管理宿主。");
        foreach (var group in plugins.GroupBy(p => p.Guid, Files.Compare).Where(g => g.Count() > 1)) issues.Add("重复插件身份：" + group.Key);
        foreach (var dependency in dependencies)
            if (!plugins.Any(p => Files.Compare.Equals(p.Guid, dependency.Guid) && (dependency.Minimum == null || Version.TryParse(dependency.Minimum, out var minimum) && Version.TryParse(p.Version, out var actual) && actual >= minimum)))
                issues.Add(dependency.Path + " 缺少目标内部硬依赖：" + dependency.Guid + " " + dependency.Minimum);
        foreach (var required in pack.Requirements)
            if (!plugins.Any(p => Files.Compare.Equals(p.Guid, required.Guid) && Version.TryParse(p.Version, out var actual) && Version.TryParse(required.Version, out var minimum) && actual >= minimum))
                issues.Add("目标缺少插件：" + required.Guid + " >= " + required.Version);
        // Game/platform assemblies are read-only platform prerequisites. Never search live BepInEx/core/plugins.
        string managed = Path.Combine(Files.GameRootFor(storage.Root), "PEAK_Data", "Managed");
        foreach (var owner in assemblies)
            foreach (var reference in owner.References)
            {
                if (assemblies.Any(a => Files.Compare.Equals(a.Name, reference.Name) && a.Token == Token(reference) && a.Version >= reference.Version)) continue;
                if (reference.Name.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase) || reference.Name is "0Harmony" or "ModConfigEnhance")
                { issues.Add(owner.Path + " 缺少目标管理程序集：" + reference.FullName); continue; }
                string platform = Files.Child(managed, reference.Name + ".dll");
                if (File.Exists(platform))
                {
                    Files.RejectLinks(managed, platform);
                    using var provided = AssemblyDefinition.ReadAssembly(platform);
                    if (provided.Name.Name == reference.Name && Token(provided.Name) == Token(reference) && provided.Name.Version >= reference.Version) continue;
                }
                issues.Add(owner.Path + " 缺少目标内部程序集：" + reference.Name + " >= " + reference.Version);
            }
        foreach (var requirement in pack.CoreRequirements)
        {
            string relative = requirement.Path.Replace('\\', '/');
            if (relative.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) relative = relative[8..];
            if (PackStorage.ManagedPath(relative, 2))
            {
                if (!pack.Files.Any(f => Files.Compare.Equals(f.Path, relative) && Files.Compare.Equals(f.Hash, requirement.Hash))) issues.Add("目标缺少核心文件：" + relative);
            }
            else issues.Add("完整环境含未支持的平台条件，请重新明确平台前置条件：" + requirement.Path);
        }
        return issues;
    }
    static string Token(AssemblyNameReference name) => FrameworkCompat.Hex(name.PublicKeyToken ?? Array.Empty<byte>());
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    { foreach (var type in types) { yield return type; foreach (var nested in Types(type.NestedTypes)) yield return nested; } }

    internal static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Files.Json), Files.Json)!;
    internal static string LedgerPath(PackStorage storage, string id) => id.Length == 0
        ? Path.Combine(storage.Root, "MCE", "shop", "installed-unmanaged.json") : Path.Combine(storage.Workspace(id), "shop-installed.json");
    internal static string? ReadLedgerText(PackStorage storage, string id)
    {
        string path = LedgerPath(storage, id); Files.RejectLinks(storage.Root, path);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
    internal static ShopLedger CaptureLedger(PackStorage storage, string id, bool required)
    {
        string? text = ReadLedgerText(storage, id);
        if (text == null && required) throw new IOException("当前完整环境的 workspace ledger 缺失，不能偷偷使用旧状态或抹除归属。请先恢复商店归属记录。");
        return text == null ? PackRevisionState.EmptyLedger() : JsonSerializer.Deserialize<ShopLedger>(text, Files.Json) ?? throw new IOException("商店归属记录无效。");
    }
    // Personal edits/delete-mod explicitly relinquish only changed/removed exact file references.
    internal static void ReconcileEditedLedger(ShopLedger ledger, PackRecord revision)
    {
        foreach (var package in ledger.Packages)
            package.Files.RemoveAll(f => !revision.Files.Any(p => Files.Compare.Equals(p.Path, f.Path) && Files.Compare.Equals(p.Hash, f.Hash)));
        ledger.Packages.RemoveAll(p => p.Files.Count == 0);
        ledger.Revision++;
    }
}

internal sealed class PackLedgerChange
{
    public string PackId { get; set; } = "";
    public string? Before { get; set; }
    public string After { get; set; } = "";
}
