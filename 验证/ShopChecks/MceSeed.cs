using System.Text.Json;
using Mono.Cecil;
// Test-only: real MCE GUID evidence is metadata in a fixture (never the live game). Without it uninstall correctly fails closed.
internal static class MceSeed
{
    internal static void Add(string root)
    {
        string game = Path.GetDirectoryName(root)!, managed = Path.Combine(game, "PEAK_Data", "Managed");Directory.CreateDirectory(managed);
        string mscorlib = Path.Combine(managed, "mscorlib.dll");
        if (!File.Exists(mscorlib)) { using var platform = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("mscorlib", new Version(4, 0, 0, 0)), "mscorlib", ModuleKind.Dll); platform.Write(mscorlib); }
        string dir = Path.Combine(root, "plugins", "AiAeT-InGameModManager"); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(new { name = "InGameModManager", @namespace = "AiAeT", version_number = "1.0.0", dependencies = Array.Empty<string>() }));
        using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("MceSeedFixture", new Version(2, 0)), "MceSeedFixture", ModuleKind.Dll);
        var type = new TypeDefinition("Fixture", "Plugin", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, a.MainModule.TypeSystem.Object); a.MainModule.Types.Add(type);
        var ctor = new MethodReference(".ctor", a.MainModule.TypeSystem.Void, new TypeReference("BepInEx", "BepInPlugin", a.MainModule, a.MainModule.TypeSystem.CoreLibrary)) { HasThis = true };
        for (int i = 0; i < 3; i++) ctor.Parameters.Add(new ParameterDefinition(a.MainModule.TypeSystem.String));
        var attribute = new CustomAttribute(ctor); foreach (string value in new[] { "com.aiae.ingamemodmanager", "IGMM", "1.0.0" }) attribute.ConstructorArguments.Add(new CustomAttributeArgument(a.MainModule.TypeSystem.String, value));
        type.CustomAttributes.Add(attribute);
        foreach (var reference in a.MainModule.AssemblyReferences.Where(r => r.Name == "mscorlib")) reference.PublicKeyToken = Array.Empty<byte>();
        a.Write(Path.Combine(dir, "MceSeedFixture.dll"));
    }
}
internal static class NativePeFixture
{
    // Test-only valid native PE32+ with no CLR directory; version marker makes versions differ byte-for-byte.
    internal static byte[] Bytes(string marker)
    {
        var data = new byte[1024]; data[0] = (byte)'M'; data[1] = (byte)'Z'; BitConverter.GetBytes(0x80).CopyTo(data, 0x3c);
        data[0x80] = (byte)'P'; data[0x81] = (byte)'E'; BitConverter.GetBytes((ushort)0x8664).CopyTo(data, 0x84);
        BitConverter.GetBytes((ushort)240).CopyTo(data, 0x80 + 20); BitConverter.GetBytes((ushort)0x20b).CopyTo(data, 0x80 + 24);
        BitConverter.GetBytes(16u).CopyTo(data, 0x80 + 24 + 108);
        System.Text.Encoding.ASCII.GetBytes(marker).CopyTo(data, 0x300); return data;
    }
}
