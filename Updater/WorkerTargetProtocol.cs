using Mono.Cecil;
using ModConfigEnhance.Updating;
namespace MCE.UpdateWorker;

/// <summary>Read-only target-content inspection; never load/execute an assembly or borrow installed tools.</summary>
internal static class WorkerTargetProtocol
{
    internal static void Initialize()
    {
        PackCompleteEnvironment.ManagementProtocolCheck = (storage, pack) =>
        {
            ValidateTargetContent(storage.Content(pack), pack.MinimumWorkerProtocol);
            RequireCachedExecution(storage.Root);
        };
    }
    internal static string ValidateTargetContent(string content, int requiredProtocol = WorkerProtocol.Current)
    {
        Files.RejectLinks(content, content);
        var candidates = Files.Inventory(content).Keys.Where(p => p.Replace('\\', '/').StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(p).Equals("ModConfigEnhance.dll", StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count != 1) throw new IOException("Target must contain exactly one MCE host with protocol2 metadata and its complete Tools/Updater release; legacy targets cannot be auto-repaired.");
        string host = Files.Child(content, candidates[0]);
        using var module = ModuleDefinition.ReadModule(host, new ReaderParameters { ReadSymbols = false });
        if (!ManagerIdentity.IsManagerAssembly(module.Assembly.Name.Name)) throw new IOException("Target MCE assembly identity mismatch.");
        var declaration = module.Assembly.CustomAttributes.SingleOrDefault(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" && a.ConstructorArguments.Count == 2 && (string?)a.ConstructorArguments[0].Value is "IGMM.WorkerProtocol" or "MCE.WorkerProtocol");
        if (declaration == null || !int.TryParse(declaration.ConstructorArguments[1].Value?.ToString(), out int protocol) || protocol < requiredProtocol || protocol > WorkerProtocol.Current)
            throw new IOException("Target MCE does not declare the required worker protocol; product version 2.0.0 alone is insufficient.");
        return WorkerProtocol.ValidateTools(Path.Combine(Path.GetDirectoryName(host)!, "Tools", "Updater"), requiredProtocol, content);
    }
    internal static void RequireCachedExecution(string root)
    {
        string tools = ManagerIdentity.Runtime(root, "tools");
        string executable = FrameworkCompat.ProcessPath ?? throw new IOException("Cannot locate worker executable.");
        if (!Files.Within(tools, executable) || !Path.GetFileName(executable).Equals(WorkerProtocol.Executable, StringComparison.OrdinalIgnoreCase)) throw new IOException("Environment writes require the complete worker release cached under MCE/tools, outside pack payloads.");
        WorkerProtocol.ValidateTools(Path.GetDirectoryName(executable)!, boundary: Directory.GetParent(root)!.FullName);
    }
}
