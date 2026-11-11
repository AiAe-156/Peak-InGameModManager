using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace InstallerChecks;

/// <summary>Metadata-only fixtures: a fake BepInEx.dll (attributes + enum), plugin DLLs that reference it, Thunderstore-shaped zips and a fake store.</summary>
internal static class Fixtures
{
    internal static byte[] BepInEx()
    {
        var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("BepInEx", new Version(5, 4, 23, 5)), "BepInEx", new ModuleParameters { Kind = ModuleKind.Dll, Runtime = TargetRuntime.Net_4_0 });
        var module = assembly.MainModule; var ts = module.TypeSystem;
        var attributeBase = new TypeReference("System", "Attribute", module, ts.CoreLibrary);
        var plugin = new TypeDefinition("BepInEx", "BepInPlugin", TypeAttributes.Public | TypeAttributes.Class, attributeBase); module.Types.Add(plugin);
        plugin.Methods.Add(Ctor(module, ts.String, ts.String, ts.String));
        var dependency = new TypeDefinition("BepInEx", "BepInDependency", TypeAttributes.Public | TypeAttributes.Class, attributeBase); module.Types.Add(dependency);
        var flags = new TypeDefinition("", "DependencyFlags", TypeAttributes.NestedPublic | TypeAttributes.Sealed, new TypeReference("System", "Enum", module, ts.CoreLibrary));
        dependency.NestedTypes.Add(flags);
        flags.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, ts.Int32));
        flags.Fields.Add(new FieldDefinition("HardDependency", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, flags) { Constant = 1 });
        flags.Fields.Add(new FieldDefinition("SoftDependency", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, flags) { Constant = 2 });
        dependency.Methods.Add(Ctor(module, ts.String));
        dependency.Methods.Add(Ctor(module, ts.String, ts.String));
        dependency.Methods.Add(Ctor(module, ts.String, flags));
        return Bytes(assembly);
    }

    static MethodDefinition Ctor(ModuleDefinition module, params TypeReference[] parameters)
    {
        var method = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        foreach (var parameter in parameters) method.Parameters.Add(new ParameterDefinition(parameter));
        method.Body.GetILProcessor().Emit(OpCodes.Ret);
        return method;
    }

    /// <summary>A plugin assembly; <paramref name="softDependency"/> uses the enum overload so decoding needs assembly resolution.</summary>
    internal static byte[] Plugin(string coreDirectory, string assemblyName, string guid, string name, string version, string? softDependency = null, string? hardMinimum = null)
    {
        var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(coreDirectory);
        var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(assemblyName, new Version(1, 0)), assemblyName, new ModuleParameters { Kind = ModuleKind.Dll, Runtime = TargetRuntime.Net_4_0, AssemblyResolver = resolver });
        var module = assembly.MainModule; var ts = module.TypeSystem;
        var bepinex = new AssemblyNameReference("BepInEx", new Version(5, 4, 23, 5)); module.AssemblyReferences.Add(bepinex);
        var type = new TypeDefinition("Fixture", "Plugin", TypeAttributes.Public | TypeAttributes.Class, ts.Object); module.Types.Add(type);
        var marker = new FieldDefinition("Marker", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, ts.String) { Constant = guid + "@" + version };
        type.Fields.Add(marker);
        var pluginType = new TypeReference("BepInEx", "BepInPlugin", module, bepinex);
        var pluginCtor = new MethodReference(".ctor", ts.Void, pluginType) { HasThis = true };
        for (int i = 0; i < 3; i++) pluginCtor.Parameters.Add(new ParameterDefinition(ts.String));
        var pluginAttribute = new CustomAttribute(pluginCtor);
        foreach (string value in new[] { guid, name, version }) pluginAttribute.ConstructorArguments.Add(new CustomAttributeArgument(ts.String, value));
        type.CustomAttributes.Add(pluginAttribute);
        var dependencyType = new TypeReference("BepInEx", "BepInDependency", module, bepinex);
        if (softDependency != null)
        {
            var flags = new TypeReference("", "DependencyFlags", module, bepinex) { DeclaringType = dependencyType, IsValueType = true };
            var ctor = new MethodReference(".ctor", ts.Void, dependencyType) { HasThis = true };
            ctor.Parameters.Add(new ParameterDefinition(ts.String)); ctor.Parameters.Add(new ParameterDefinition(flags));
            var attribute = new CustomAttribute(ctor);
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(ts.String, softDependency));
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(flags, 2));
            type.CustomAttributes.Add(attribute);
        }
        if (hardMinimum != null)
        {
            var ctor = new MethodReference(".ctor", ts.Void, dependencyType) { HasThis = true };
            ctor.Parameters.Add(new ParameterDefinition(ts.String)); ctor.Parameters.Add(new ParameterDefinition(ts.String));
            var attribute = new CustomAttribute(ctor);
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(ts.String, hardMinimum.Split('@')[0]));
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(ts.String, hardMinimum.Split('@')[1]));
            type.CustomAttributes.Add(attribute);
        }
        return Bytes(assembly);
    }

    static byte[] Bytes(AssemblyDefinition assembly)
    {
        using var stream = new MemoryStream(); assembly.Write(stream); return stream.ToArray();
    }

    internal static string Manifest(string name, string version, params string[] dependencies)
        => JsonSerializer.Serialize(new { name, version_number = version, website_url = "", description = name, dependencies });

    internal static byte[] Zip(params (string Path, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var entry in entries)
            {
                var item = archive.CreateEntry(entry.Path, CompressionLevel.Fastest);
                using var output = item.Open(); output.Write(entry.Bytes, 0, entry.Bytes.Length);
            }
        return stream.ToArray();
    }
    internal static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);
}

internal sealed class FakeStore : HttpMessageHandler
{
    public readonly List<string> Requests = new();
    readonly Dictionary<string, (string Version, string[] Dependencies)> latest = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, (byte[] Zip, string[] Dependencies)> versions = new(StringComparer.OrdinalIgnoreCase);
    public void Add(string ns, string name, string version, byte[] zip, params string[] dependencies)
    {
        versions[ns + "/" + name + "/" + version] = (zip, dependencies);
        if (!latest.TryGetValue(ns + "/" + name, out var current) || Version.Parse(version) > Version.Parse(current.Version)) latest[ns + "/" + name] = (version, dependencies);
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.ToString(); Requests.Add(url);
        const string api = "https://thunderstore.io/api/experimental/package/";
        if (url.StartsWith(api, StringComparison.Ordinal))
        {
            string[] parts = url.Substring(api.Length).TrimEnd('/').Split('/');
            if (parts.Length == 2 && latest.TryGetValue(parts[0] + "/" + parts[1], out var head))
                return Json(new { @namespace = parts[0], name = parts[1], latest = new { version_number = head.Version, download_url = Download(parts[0], parts[1], head.Version), dependencies = head.Dependencies } });
            if (parts.Length == 3 && versions.TryGetValue(parts[0] + "/" + parts[1] + "/" + parts[2], out var exact))
                return Json(new { @namespace = parts[0], name = parts[1], version_number = parts[2], download_url = Download(parts[0], parts[1], parts[2]), dependencies = exact.Dependencies });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") });
        }
        const string dl = "https://dl.test/";
        if (url.StartsWith(dl, StringComparison.Ordinal))
        {
            string key = url.Substring(dl.Length); if (key.EndsWith(".zip")) key = key.Substring(0, key.Length - 4);
            if (versions.TryGetValue(key, out var package))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package.Zip) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip") } } });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("missing") });
    }
    static string Download(string ns, string name, string version) => "https://dl.test/" + ns + "/" + name + "/" + version + ".zip";
    static Task<HttpResponseMessage> Json(object value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") });
}
