using System.Collections;
using System.Reflection;
using ModConfigEnhance.Updating;
using Mono.Cecil;

int count = 0;
void Check(bool value, string why) { if (!value) throw new Exception(why); count++; Console.WriteLine("PASS " + why); }
string home = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(home);
foreach (var prop in typeof(BepInEx.Paths).GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
    if (prop.PropertyType == typeof(string) && prop.SetMethod != null) prop.SetValue(null, home);
var plugin = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "ModConfigEnhance.dll"));
var toggle = plugin.GetType("ModConfigEnhance.Overview.ModToggleService", true)!;
object? Call(string name, params object[] args)
{
    // Loc touches Unity/game settings. Test the exact extracted lookup methods with
    // a formatting-only Loc stub; metadata/move/package methods use the built DLL.
    var type = name is "ExistingFile" or "ExistingFileForMod" ? typeof(LookupHarness) : toggle;
    return type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
}
var moveType = toggle.GetNestedType("Move", BindingFlags.NonPublic)!;
IList Moves() => (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(moveType))!;
string Field(object value, string name) => (string)moveType.GetField(name)!.GetValue(value)!;
string Make(string path, string guid = "fixture.example", string version = "1.2.3")
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Example", new Version(1, 0)), "Example", ModuleKind.Dll);
    var m = a.MainModule;
    var t = new TypeDefinition("Fixture", "Example", Mono.Cecil.TypeAttributes.Public, m.TypeSystem.Object); m.Types.Add(t);
    var scope = new AssemblyNameReference("BepInEx", new Version(5, 4, 23, 0)); m.AssemblyReferences.Add(scope);
    var ctor = new MethodReference(".ctor", m.TypeSystem.Void, new TypeReference("BepInEx", "BepInPlugin", m, scope)) { HasThis = true };
    for (int i = 0; i < 3; i++) ctor.Parameters.Add(new ParameterDefinition(m.TypeSystem.String));
    var attr = new CustomAttribute(ctor);
    foreach (string value in new[] { guid, "Example", version }) attr.ConstructorArguments.Add(new CustomAttributeArgument(m.TypeSystem.String, value));
    t.CustomAttributes.Add(attr); a.Write(path); return path;
}
string Original() => Path.Combine(home, Guid.NewGuid().ToString("N"), "Example.dll");
string Conflict(Action run)
{
    try { run(); } catch (TargetInvocationException error) when (error.InnerException is IOException or InvalidOperationException) { return error.InnerException.Message; }
    throw new Exception("expected protection");
}
try
{
    foreach (string suffix in new[] { ".old", ".bak", ".backup" })
    {
        string original = Original(); Make(original); string backup = Make(original + suffix, "fixture.OLD", "0.1.0");
        byte[] before = File.ReadAllBytes(backup);
        Check(PluginFileNames.ExistingCopies(original).Count == 2, "complete evidence retained " + suffix);
        Check(PluginFileNames.ActionableCopies(original).SequenceEqual(new[] { original }), "active DLL excludes backup " + suffix);
        Check(PluginFileNames.IsBackupCompanion(backup), "backup identified " + suffix);
        Check((string)Call("ExistingFile", original)! == original, "production selects active " + suffix);
        Check(((IList)Call("ReadPlugins", backup)!).Count == 0, "production ignores old identity/dependencies " + suffix);
        var moves = Moves(); Call("AddMove", original, false, moves);
        Check(moves.Count == 1 && Field(moves[0]!, "To") == original + PluginFileNames.DisabledSuffix, "disable uses the short MCE suffix " + suffix);
        Check(PluginFileNames.IsOursDisabled(original + PluginFileNames.LegacyDisabledSuffix) && !PluginFileNames.IsForeignDisabled(original + PluginFileNames.LegacyDisabledSuffix), "legacy MCE suffix still recognised as ours");
        File.Move(Field(moves[0]!, "From"), Field(moves[0]!, "To"));
        Check(!PluginFileNames.IsBackupCompanion(backup), "no active DLL means suffixed files are disabled candidates " + suffix);
        Check(Conflict(() => Call("ExistingFile", original)).Contains(backup), "multiple disabled candidates report paths " + suffix);
        File.Move(original + PluginFileNames.DisabledSuffix,original);
        Check(File.ReadAllBytes(backup).SequenceEqual(before) && File.Exists(original), "roundtrip preserves backup bytes " + suffix);
        var identity = (IList)Call("PackagePluginIdentity", home, Path.GetDirectoryName(original)!)!;
        Check(identity.Count == 1 && identity[0]!.ToString()!.Contains("fixture.example"), "package identity excludes backup " + suffix);
        var planType = toggle.GetNestedType("Plan", BindingFlags.NonPublic)!;
        var plan = Activator.CreateInstance(planType)!;
        Call("AddPackageMoves", plan, home, Path.GetDirectoryName(original)!, false, false);
        var planned = (IList)planType.GetField("Moves")!.GetValue(plan)!;
        Check(planned.Count == 1 && Field(planned[0]!, "From") == original, "manager package moves skip backup " + suffix);
    }
    foreach (string suffix in new[] { ".old", ".bak", ".off", ".custom.with.dots", ".disabled-by-mce" })
    {
        string original = Original(); string disabled = Make(original + suffix);
        Check(!PluginFileNames.IsBackupCompanion(disabled), "lone suffix still disabled " + suffix);
        Check((string)Call("ExistingFile", original)! == disabled, "unique historical lookup " + suffix);
        var moves = Moves(); Call("AddMove", original, true, moves);
        Check(moves.Count == 1 && Field(moves[0]!, "To") == original, "unique suffix restores " + suffix);
    }
    foreach (var suffixes in new[] {
        new[]{".old", ".off"}, new[]{".old", ".bak"},
        new[]{".disabled-by-mce", ".disabled-by-other", ".old"} })
    {
        string original = Original(); var files = suffixes.Select(s => Make(original + s)).ToArray();
        Check(PluginFileNames.ActionableCopies(original).Count > 1, "ambiguous group never preferred " + string.Join(",", suffixes));
        string error = Conflict(() => Call("ExistingFileForMod", original, "Example display name"));
        Check(error.Contains("Example display name") && files.All(error.Contains), "conflict names mod and all complete paths " + string.Join(",", suffixes));
        Check(Conflict(() => Call("AddMove", original, false, Moves())).Length > 0, "ambiguous mutation blocked");
    }
    foreach(string suffix in new[]{".off",".anything.with.dots",".disabled-by-other",".disabled-by-mce"})
    {
        string original=Original();Make(original);string copy=Make(original+suffix);
        Check(PluginFileNames.IsBackupCompanion(copy),"any suffix beside active DLL is backup "+suffix);
        Check(PluginFileNames.ActionableCopies(original).SequenceEqual(new[]{original}),"active DLL is unique target "+suffix);
    }
    string foreign = Original(); Make(foreign + ".disabled-by-other");
    Check(Conflict(() => Call("AddMove", foreign, true, Moves())).Contains(foreign + ".disabled-by-other"), "foreign tool marker never restored");
    Check(PluginFileNames.IsAssembly("name.dll.anything") && !PluginFileNames.IsAssembly("name.dll.mce-candidate"), "arbitrary disabled suffix and staging exclusion retained");
    Console.WriteLine($"RESULT {count} filesystem / real production-rule checks passed");
}
finally { Directory.Delete(home, true); }
