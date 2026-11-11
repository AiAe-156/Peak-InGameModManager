using BepInEx;
using ModConfigEnhance;
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
using Mono.Cecil;

// Only defaults/paired audits use this synthetic, self-contained publication source.
// No live dependency probing, historical reconstruction, downloaded tools or execution.
internal static class CompleteAuditFixture
{
    internal static string Setup(string fixture)
    {
        Paths.ManagedPath = Path.Combine(fixture, "publisher", "PEAK_Data", "Managed");
        Directory.CreateDirectory(Paths.ManagedPath);
        foreach (string name in new[] { "BepInEx", "BepInEx.Preloader", "0Harmony" })
            WriteAssembly(Path.Combine(Paths.BepInExRootPath, "core", name + ".dll"), name);
        string host = Path.Combine(Paths.PluginPath, "AuditHost");
        WriteAssembly(Path.Combine(host, "ModConfigEnhance.dll"), "ModConfigEnhance", true);
        string tools = Path.Combine(host, "Tools", "Updater");
        // A minimal managed no-op entry point. This is manifest-validation data,
        // NOT a shipped worker or an execution/recovery test.
        WriteAssembly(Path.Combine(tools, WorkerProtocol.Executable), "AuditWorker");
        foreach (string name in new[] { "Mono.Cecil.LICENSE.txt", "Runtime-LICENSE.TXT", "Runtime-THIRD-PARTY-NOTICES.TXT" })
            File.WriteAllText(Path.Combine(tools, name), "Synthetic audit fixture only; no third-party runtime bundled.\n");
        WorkerProtocol.WriteManifest(tools);
        OverviewCatalogue.InvalidateCaches();
        return tools;
    }

    private static void WriteAssembly(string file, string name, bool host = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        bool executable = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        using var a = AssemblyDefinition.CreateAssembly(new(name, new Version(2, 0, 0)), name, executable ? ModuleKind.Console : ModuleKind.Dll);
        if (executable)
        {
            var type = new TypeDefinition("Fixture", "Program", TypeAttributes.Public, a.MainModule.TypeSystem.Object);
            var main = new MethodDefinition("Main", MethodAttributes.Public | MethodAttributes.Static, a.MainModule.TypeSystem.Void);
            main.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
            type.Methods.Add(main); a.MainModule.Types.Add(type); a.MainModule.EntryPoint = main;
        }
        if (host)
        {
            a.MainModule.AssemblyReferences.Add(new("BepInEx", new Version(2, 0, 0)));
            var metadataType = new TypeReference("System.Reflection", "AssemblyMetadataAttribute", a.MainModule, a.MainModule.TypeSystem.Object.Scope);
            var metadataCtor = new MethodReference(".ctor", a.MainModule.TypeSystem.Void, metadataType) { HasThis = true };
            for (int i = 0; i < 2; i++) metadataCtor.Parameters.Add(new(a.MainModule.TypeSystem.String));
            var metadata = new CustomAttribute(metadataCtor);
            foreach (string value in new[] { "MCE.WorkerProtocol", "2" }) metadata.ConstructorArguments.Add(new(a.MainModule.TypeSystem.String, value));
            a.CustomAttributes.Add(metadata);
            var attributeType = new TypeReference("BepInEx", "BepInPlugin", a.MainModule, a.MainModule.AssemblyReferences.Single(r => r.Name == "BepInEx"));
            var ctor = new MethodReference(".ctor", a.MainModule.TypeSystem.Void, attributeType) { HasThis = true };
            for (int i = 0; i < 3; i++) ctor.Parameters.Add(new(a.MainModule.TypeSystem.String));
            var identity = new CustomAttribute(ctor);
            foreach (string value in new[] { Plugin.PluginGuid, "Audit MCE", "2.0.0" }) identity.ConstructorArguments.Add(new(a.MainModule.TypeSystem.String, value));
            var type = new TypeDefinition("Fixture", "Plugin", TypeAttributes.Public, a.MainModule.TypeSystem.Object);
            type.CustomAttributes.Add(identity); a.MainModule.Types.Add(type);
        }
        a.Write(file);
    }
}
