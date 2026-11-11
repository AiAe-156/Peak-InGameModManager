using System.IO.Compression;
using MCE.UpdateWorker;
using ModConfigEnhance.Updating;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    private static int checks;
    private static string root = "";
    private static void Assert(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    private static string Dir(string name) { string p = Path.Combine(root, name); Directory.CreateDirectory(p); return p; }
    private static ModImportPlan Prepare(string source, Action<ModImportRequest>? configure = null)
    {
        var request = new ModImportRequest { SourcePath = source, StagingParent = Dir("scratch"), PackageFolder = "one-package" };
        configure?.Invoke(request); return ModImportPreview.Prepare(request);
    }
    private static void Good(ModImportPlan plan, string label)
    { Assert(plan.CanProceed, label + ": " + string.Join("; ", plan.Issues.Select(i => i.Code + ": " + i.Message))); }
    private static void Bad(ModImportPlan plan, string label)
    { Assert(!plan.CanProceed && plan.Issues.Any(i => i.Blocking), label); }
    public static void Main()
    {
        root = Dir(Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N")));
        string dll = Path.Combine(Dir("single"), "plugin.dll");
        Plugin(dll, "test.a", nested: true, deps: new[] { ("optional", "", true) });
        var single = Prepare(dll); Good(single, "single nested plugin");
        Assert(single.Source == "local" && single.SourceKind == "dll" && single.OriginalSource == dll, "source retained");
        Assert(single.Plugins.Single().TypeName.Contains("/Nested"), "recursive type scan");
        Assert(single.Plugins.Single().Dependencies.Single().Soft, "soft flag");
        Assert(single.Issues.Any(i => i.Code == "soft-dependency" && !i.Blocking), "optional absent warning");
        Assert(single.Files.Single().TargetRelativePath == "plugins/one-package/plugin.dll", "independent package folder");
        Assert(single.Files.Single().Sha256.Length == 64 && single.Files.Single().ManagedAssembly, "immutable digest");
        Assert(ModImportPreview.Revalidate(single).Count == 0, "unchanged revalidation");
        Assert(!new ModImportStoreCandidate("author-package", "1.0.0", "name match").Verified, "candidate not verification");

        string mixed = Dir("mixed");
        string wrapper = Path.Combine(mixed, "Release", "BepInEx");
        Plugin(Path.Combine(wrapper, "plugins", "A", "a.dll"), "test.a", deps: new[] { ("test.b", "1.0.0", false) });
        Plugin(Path.Combine(wrapper, "plugins", "B", "b.dll"), "test.b");
        Write(Path.Combine(wrapper, "plugins", "A", "images", "icon.png"), new byte[] { 1, 2, 3 });
        Write(Path.Combine(wrapper, "plugins", "A", "native.dll"), Native());
        Library(Path.Combine(wrapper, "core", "support.dll"));
        Patcher(Path.Combine(wrapper, "patchers", "fix", "patch.dll"));
        Write(Path.Combine(wrapper, "config", "author.mod.cfg"), new byte[] { 7 });
        var multi = Prepare(mixed); Good(multi, "multi area package");
        Assert(multi.Plugins.Count == 2 && multi.Files.Count == 7, "one package many dll and resources");
        Assert(multi.Files.Any(f => f.NativeDll), "native follows plugin package");
        Assert(multi.Files.Any(f => f.TargetRelativePath == "config/author.mod.cfg"), "config preserved");
        Assert(multi.Files.Any(f => f.TargetRelativePath == "core/one-package/support.dll"), "core preview");
        Assert(multi.Files.Any(f => f.TargetRelativePath == "patchers/one-package/fix/patch.dll"), "patcher preview");
        Assert(multi.Files.Any(f => f.TargetRelativePath == "plugins/one-package/A/images/icon.png"), "resource structure retained");
        Assert(ModImportPreview.Revalidate(multi).Count == 0, "multi staging unchanged");
        string zip = Path.Combine(root, "mixed.zip"); ZipFile.CreateFromDirectory(mixed, zip);
        Good(Prepare(zip), "ZIP mixed fixture");

        string dependency = Path.Combine(Dir("dependency"), "a.dll");
        Plugin(dependency, "test.dep", deps: new[] { ("external", "2.0.0", false) });
        Bad(Prepare(dependency), "hard dependency missing");
        Bad(Prepare(dependency, r => r.InstalledPlugins = new[] { new ModImportInstalledPlugin { Guid = "external", Version = "1.0.0" } }), "hard dependency too old");
        Good(Prepare(dependency, r => r.InstalledPlugins = new[] { new ModImportInstalledPlugin { Guid = "external", Version = "2.0.0" } }), "installed dependency satisfies");
        string dup = Dir("duplicates"); Plugin(Path.Combine(dup, "a.dll"), "same"); Plugin(Path.Combine(dup, "b.dll"), "same");
        Bad(Prepare(dup), "duplicate GUIDs");
        string bare = Path.Combine(Dir("bare"), "native.dll"); Write(bare, Native()); Bad(Prepare(bare), "bare native blocked");
        string lib = Path.Combine(Dir("lib"), "library.dll"); Library(lib); Bad(Prepare(lib), "bare managed library blocked");
        string patch = Path.Combine(Dir("patch-only"), "patchers", "x.dll"); Patcher(patch); Good(Prepare(Path.GetDirectoryName(Path.GetDirectoryName(patch)!)!), "patcher only supported");
        string typo = Dir("typo"); Plugin(Path.Combine(typo, "pathcer", "x.dll"), "typo");
        var typoPlan = Prepare(typo); Bad(typoPlan, "pathcer blocked");
        Assert(typoPlan.Issues.Any(i => i.Code == "misspelled-patchers" && i.Message.Contains("patchers")), "typo suggestion");
        string protectedDir = Dir("protected"); Plugin(Path.Combine(protectedDir, "x.dll"), "ok"); Library(Path.Combine(protectedDir, "core", "BepInEx.dll"));
        Bad(Prepare(protectedDir), "loader overwrite blocked");
        string game = Dir("game-files"); Plugin(Path.Combine(game, "x.dll"), "ok"); Write(Path.Combine(game, "PEAK_Data", "Managed", "x.txt"), new byte[] { 1 });
        Bad(Prepare(game), "game directory blocked");

        foreach (string name in new[] { "../escape.dll", "/absolute.dll", "C:/drive.dll", "plugins/a.dll:ads", "plugins/CON.dll", "plugins/a./x", "plugins//x" })
            Bad(Prepare(Zip("unsafe-" + checks, (name, new byte[] { 1 }, 0))), "reject " + name);
        Good(Prepare(Zip("late-directory", ("plugins/plugin.dll", File.ReadAllBytes(dll), 0), ("plugins/", Array.Empty<byte>(), 0))), "explicit ZIP directory after payload");
        Bad(Prepare(Zip("case", ("A/x.txt", new byte[] { 1 }, 0), ("a/y.txt", new byte[] { 2 }, 0))), "case conflict directory");
        Bad(Prepare(Zip("dupe", ("x.txt", new byte[] { 1 }, 0), ("x.txt", new byte[] { 2 }, 0))), "duplicate zip entry");
        Bad(Prepare(Zip("prefix", ("x", new byte[] { 1 }, 0), ("x/y", new byte[] { 2 }, 0))), "file/directory conflict");
        Bad(Prepare(Zip("symlink", ("linked", new byte[] { 1 }, unchecked((int)0xA1FF0000)))), "zip symlink");
        Bad(Prepare(Zip("doslink", ("linked", new byte[] { 1 }, (int)FileAttributes.ReparsePoint))), "zip reparse");
        Bad(Prepare(Zip("bomb", ("big", new byte[100000], 0))), "zip ratio limit");
        Bad(Prepare(zip, r => r.Limits.MaximumEntries = 2), "entry limit");
        Bad(Prepare(zip, r => r.Limits.MaximumFileBytes = 50), "file limit");
        Bad(Prepare(zip, r => r.Limits.MaximumTotalBytes = 100), "total limit");
        Bad(Prepare(dll, r => r.PackageFolder = "../outside"), "unsafe package target");
        Bad(Prepare(dll, r => r.StagingParent = Path.GetDirectoryName(dll)!), "staging overlap");

        string stageFile = Path.Combine(single.StagingDirectory, single.Files[0].StagedRelativePath);
        byte[] bytes = File.ReadAllBytes(stageFile); bytes[bytes.Length - 1] ^= 1; File.WriteAllBytes(stageFile, bytes);
        Assert(ModImportPreview.Revalidate(single).Any(i => i.Code == "staging-changed"), "same-length mutation");
        string extra = Path.Combine(multi.StagingDirectory, "raw", "extra.txt"); Write(extra, new byte[] { 2 });
        Assert(ModImportPreview.Revalidate(multi).Count > 0, "additional staging file");
        var missing = Prepare(dll); File.Delete(Path.Combine(missing.StagingDirectory, missing.Files[0].StagedRelativePath));
        Assert(ModImportPreview.Revalidate(missing).Count > 0, "deleted staging file");
#if NET10_0_OR_GREATER
        string linkDir = Dir("source-links");
        Plugin(Path.Combine(linkDir, "x.dll"), "link.test");
        try
        {
            File.CreateSymbolicLink(Path.Combine(linkDir, "linked.dll"), dll);
            Bad(Prepare(linkDir), "filesystem symlink");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine("SKIP source symlink: OS privilege unavailable"); }
        catch (IOException) { Console.WriteLine("SKIP source symlink: OS privilege unavailable"); }
#endif
        Console.WriteLine("PASS " + checks + " assertions; fixtures: " + root);
    }
    private static string Zip(string name, params (string Path, byte[] Bytes, int Attributes)[] entries)
    {
        string path = Path.Combine(root, name + ".zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var value in entries)
            { var entry = zip.CreateEntry(value.Path); entry.ExternalAttributes = value.Attributes; using var output = entry.Open(); output.Write(value.Bytes, 0, value.Bytes.Length); }
        return path;
    }
    private static void Write(string path, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
    private static byte[] Native()
    { byte[] b = new byte[256]; b[0] = 0x4D; b[1] = 0x5A; b[60] = 64; b[64] = 0x50; b[65] = 0x45; b[87] = 0x20; return b; }
    private static void Library(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Fixture", new Version(1, 0)), "Fixture", ModuleKind.Dll);
        assembly.Write(path);
    }
    private static void Patcher(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Patcher", new Version(1, 0)), "Patcher", ModuleKind.Dll);
        var m = assembly.MainModule;
        var type = new TypeDefinition("Fixture", "Patcher", TypeAttributes.Public, m.TypeSystem.Object); m.Types.Add(type);
        var patch = new MethodDefinition("Patch", MethodAttributes.Public | MethodAttributes.Static, m.TypeSystem.Void);
        patch.Parameters.Add(new ParameterDefinition(m.ImportReference(typeof(AssemblyDefinition))));
        patch.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); type.Methods.Add(patch);
        var targetType = m.ImportReference(typeof(IEnumerable<string>));
        var get = new MethodDefinition("get_TargetDLLs", MethodAttributes.Public | MethodAttributes.Static, targetType); get.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull)); get.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); type.Methods.Add(get);
        var prop = new PropertyDefinition("TargetDLLs", PropertyAttributes.None, targetType) { GetMethod = get }; type.Properties.Add(prop);
        assembly.Write(path);
    }
    private static void Plugin(string path, string guid, bool nested = false, (string Guid, string Minimum, bool Soft)[]? deps = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Fixture", new Version(1, 0)), "Fixture", ModuleKind.Dll);
        var m = assembly.MainModule;
        var type = new TypeDefinition("Fixture", "Plugin", TypeAttributes.Public, m.TypeSystem.Object); m.Types.Add(type);
        if (nested) { var child = new TypeDefinition("", "Nested", TypeAttributes.NestedPublic, m.TypeSystem.Object); type.NestedTypes.Add(child); type = child; }
        var scope = new AssemblyNameReference("BepInEx", new Version(5, 4, 23, 0)); m.AssemblyReferences.Add(scope);
        MethodReference Constructor(string name, params TypeReference[] args)
        { var ctor = new MethodReference(".ctor", m.TypeSystem.Void, new TypeReference("BepInEx", name, m, scope)) { HasThis = true }; foreach (var arg in args) ctor.Parameters.Add(new ParameterDefinition(arg)); return ctor; }
        var attr = new CustomAttribute(Constructor("BepInPlugin", m.TypeSystem.String, m.TypeSystem.String, m.TypeSystem.String));
        foreach (string value in new[] { guid, "Test " + guid, "1.0.0" }) attr.ConstructorArguments.Add(new CustomAttributeArgument(m.TypeSystem.String, value)); type.CustomAttributes.Add(attr);
        foreach (var dep in deps ?? Array.Empty<(string, string, bool)>())
        {
            var second = dep.Minimum.Length > 0 ? m.TypeSystem.String : m.TypeSystem.Int32;
            var dependency = new CustomAttribute(Constructor("BepInDependency", m.TypeSystem.String, second));
            dependency.ConstructorArguments.Add(new CustomAttributeArgument(m.TypeSystem.String, dep.Guid));
            dependency.ConstructorArguments.Add(new CustomAttributeArgument(second, dep.Minimum.Length > 0 ? (object)dep.Minimum : dep.Soft ? 2 : 1));
            type.CustomAttributes.Add(dependency);
        }
        assembly.Write(path);
    }
}
