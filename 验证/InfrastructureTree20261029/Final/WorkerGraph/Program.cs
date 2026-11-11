using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Mono.Cecil;
using MCE.UpdateWorker;
using ModConfigEnhance.Updating;
// Final review probes. Sandbox only: fixture BepInEx/PEAK_Data live under this project's bin. No live game/core/config/inventory/queue.
int passed = 0, failed = 0;
void Check(bool ok, string label) { Console.WriteLine((ok ? "PASS " : "FAIL ") + label); if (ok) passed++; else failed++; }
string suite = Path.Combine(AppContext.BaseDirectory, "fixtures-" + Guid.NewGuid().ToString("N"));
const string modConfigGuid = "com.github.PEAKModding.PEAKLib.ModConfig", uiGuid = "com.github.PEAKModding.PEAKLib.UI";
Files.RunningProbe = _ => false;
string Root(out string managed)
{
    string game = Path.Combine(suite, Guid.NewGuid().ToString("N")[..8]);
    string root = Path.Combine(game, "BepInEx"); Directory.CreateDirectory(Path.Combine(root, "plugins"));
    managed = Path.Combine(game, "PEAK_Data", "Managed"); Directory.CreateDirectory(managed);
    File.WriteAllBytes(Path.Combine(managed, "mscorlib.dll"), Build("mscorlib", null, null, null, new Version(4, 0, 0, 0))); return root;
}
string Manifest(string root, string key, string area = "plugins", string[]? deps = null)
{
    string dir = Path.Combine(root, area, key); Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(new { name = key.Split('-')[1], @namespace = key.Split('-')[0], version_number = "1.0.0", dependencies = deps ?? Array.Empty<string>() }));
    return dir;
}
byte[] Build(string name, string? guid, (string, bool)[]? deps, (string Name, Version V)[]? refs, Version? version = null)
{
    using var a = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(name, version ?? new Version(1, 0)), name, ModuleKind.Dll);
    var m = a.MainModule;
    if (name is "mscorlib" or "System") { using var plain = new MemoryStream(); a.Write(plain); return plain.ToArray(); } // platform stub: metadata identity only
    var t = new TypeDefinition("Fixture", "Plugin", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, m.TypeSystem.Object); m.Types.Add(t);
    MethodReference Attr(string type, params TypeReference[] args) { var c = new MethodReference(".ctor", m.TypeSystem.Void, new TypeReference("BepInEx", type, m, m.TypeSystem.CoreLibrary)) { HasThis = true }; foreach (var x in args) c.Parameters.Add(new ParameterDefinition(x)); return c; }
    if (guid != null) { var at = new CustomAttribute(Attr("BepInPlugin", m.TypeSystem.String, m.TypeSystem.String, m.TypeSystem.String)); foreach (string v in new[] { guid, name, "1.0.0" }) at.ConstructorArguments.Add(new CustomAttributeArgument(m.TypeSystem.String, v)); t.CustomAttributes.Add(at); }
    foreach (var (dep, soft) in deps ?? Array.Empty<(string, bool)>()) { var at = new CustomAttribute(Attr("BepInDependency", m.TypeSystem.String, m.TypeSystem.Int32)); at.ConstructorArguments.Add(new CustomAttributeArgument(m.TypeSystem.String, dep)); at.ConstructorArguments.Add(new CustomAttributeArgument(m.TypeSystem.Int32, soft ? 2 : 1)); t.CustomAttributes.Add(at); }
    foreach (var r in m.AssemblyReferences.Where(r => r.Name == "mscorlib")) r.PublicKeyToken = Array.Empty<byte>();
    foreach (var (rn, rv) in refs ?? Array.Empty<(string, Version)>()) m.AssemblyReferences.Add(new AssemblyNameReference(rn, rv) { PublicKeyToken = Array.Empty<byte>() });
    using var ms = new MemoryStream(); a.Write(ms); return ms.ToArray();
}
void Write(string dir, string name, string? guid, (string, bool)[]? deps, string[]? refs, Version? version, bool disabled = false)
{
    Directory.CreateDirectory(dir);
    File.WriteAllBytes(Path.Combine(dir, name + (disabled ? ".dll.old" : ".dll")), Build(name, guid, deps, refs?.Select(r => (r, new Version(1, 0))).ToArray(), version));
}
// Standard closure: MCE -> ModConfig -> UI -> (assembly ref) third-party Core; Ordinary/Leaf are unrelated.
string Standard(out string managed)
{
    string root = Root(out managed);
    Write(Manifest(root, "AiAeT-ModConfigEnhance", deps: new[] { "Team-ModConfig-1.0.0" }), "Mce", ComponentDependencyGraph.MceGuid, new[] { (modConfigGuid, false), ("optional", true) }, null, null);
    Write(Manifest(root, "Team-ModConfig"), "ModConfig", modConfigGuid, new[] { (uiGuid, false) }, null, null);
    Write(Manifest(root, "Team-UI"), "UI", uiGuid, null, new[] { "ThirdPartyCore" }, null);
    Write(Manifest(root, "Team-Core", "core"), "ThirdPartyCore", null, null, null, null);
    Write(Manifest(root, "Team-Ordinary"), "Ordinary", "ordinary", null, null, null);
    Write(Manifest(root, "Team-Leaf"), "Leaf", "leaf", null, null, null);
    return root;
}
ComponentDependencyGraph Graph(string root) => new ShopQueue(root).ReadComponentGraph();
string taskPath(string r) => Path.Combine(r, "MCE/shop/task.json");
void Send(QueueWorker worker, string r, ShopRequest request)
{
    string id = Guid.NewGuid().ToString("N");
    Files.Save(Path.Combine(r, "MCE/commands", id + ".json"), new UpdateCommand { Id = id, Action = "shop", Shop = request });
    worker.ProcessCommands();
}
async Task Pump(QueueWorker worker) => await worker.Run(true);
ShopTask Read(string r) => new ShopQueue(r).Read()!;

// ---------- A. Real QueueWorker command ingress / pump (not Graph API only) ----------
string root = Standard(out _);
using (var worker = new QueueWorker(root))
{
    foreach (string target in new[] { "UI", "ModConfig", "Core" })
    {
        if (File.Exists(taskPath(root))) File.Delete(taskPath(root));
        Send(worker, root, new ShopRequest { Operation = "preview-uninstall", Namespace = "Team", Package = target, Version = "1.0.0" });
        await Pump(worker);
        var task = Read(root);
        string path = target == "UI" ? "AiAeT-ModConfigEnhance → Team-ModConfig → Team-UI" : target == "ModConfig" ? "AiAeT-ModConfigEnhance → Team-ModConfig" : "AiAeT-ModConfigEnhance → Team-ModConfig → Team-UI → Team-Core";
        Check(task.State == "failed" && task.Error.Contains(path) && task.Originals.Count == 0 && task.Packages.Count == 0, $"worker preview-uninstall of MCE {(target == "ModConfig" ? "direct" : "indirect")} dependency Team-{target} rejected with path; no claim/plan: " + task.State + " " + task.Error.Split('\n')[0]);
    }
    Check(File.Exists(Path.Combine(root, "plugins/Team-UI/UI.dll")) && File.Exists(Path.Combine(root, "core/Team-Core/ThirdPartyCore.dll")), "rejected previews leave every protected file in place");
}
// Legit preview of an unrelated mod, then environment changes so it becomes an indirect MCE dependency before confirm.
root = Standard(out _);
var tokenMethod = typeof(ShopQueue).GetMethod("Token", BindingFlags.Static | BindingFlags.NonPublic)!;
using (var worker = new QueueWorker(root))
{
    Send(worker, root, new ShopRequest { Operation = "preview-uninstall", Namespace = "Team", Package = "Ordinary", Version = "1.0.0" });
    await Pump(worker);
    var preview = Read(root);
    Check(preview.State == "uninstall-preview" && preview.Blockers.Count == 0, "worker previews unrelated ordinary uninstall (unrelated mods not permanently locked): " + preview.State + " " + preview.Error);
    Write(Path.Combine(root, "plugins/Team-UI"), "UI", uiGuid, new[] { ("ordinary", false) }, new[] { "ThirdPartyCore" }, null); // UI now hard-depends on Ordinary
    Send(worker, root, new ShopRequest { Operation = "confirm-uninstall", PreviewId = preview.Id, PlanToken = preview.PlanToken });
    var after = Read(root);
    Check(after.State == "uninstall-preview" && worker.Current.Error.Length > 0 && File.Exists(Path.Combine(root, "plugins/Team-Ordinary/Ordinary.dll")), "worker confirm-uninstall rejects Ordinary after it became MCE-indirect; task not advanced, files remain: " + worker.Current.Error);
    var forged = Read(root); forged.State = "waiting-exit"; forged.Request.Operation = "confirm-uninstall"; forged.PlanToken = (string)tokenMethod.Invoke(null, new object[] { forged })!; Files.Save(taskPath(root), forged);
    await Pump(worker);
    var done = Read(root);
    Check(done.State == "failed" && done.Error.Contains("MCE required component") && done.Error.Contains("Team-Ordinary") && File.Exists(Path.Combine(root, "plugins/Team-Ordinary/Ordinary.dll")), "worker commit boundary rechecks live graph and writes nothing: " + done.State + " " + done.Error.Split('\n')[0]);
}
// Unrelated ordinary uninstall full real worker path: preview -> confirm -> commit.
root = Standard(out _);
using (var worker = new QueueWorker(root))
{
    Send(worker, root, new ShopRequest { Operation = "preview-uninstall", Namespace = "Team", Package = "Leaf", Version = "1.0.0" }); await Pump(worker);
    var p = Read(root);
    Send(worker, root, new ShopRequest { Operation = "confirm-uninstall", PreviewId = p.Id, PlanToken = p.PlanToken }); await Pump(worker);
    var done = Read(root);
    Check(p.State == "uninstall-preview" && done.State == "completed" && !File.Exists(Path.Combine(root, "plugins/Team-Leaf/Leaf.dll")) && File.Exists(Path.Combine(root, "plugins/Team-Ordinary/Ordinary.dll")), "worker allows and commits unrelated ordinary uninstall without touching others: " + done.State + " " + done.Error.Split('\n')[0]);
}
// Update of a protected dependency is not forbidden by the fixed-uninstall gate.
root = Standard(out _);
var store = new UpdateStore { Dll = () => Build("UI", uiGuid, null, new[] { ("ThirdPartyCore", new Version(1, 0)) }, new Version(2, 0)) }; ShopQueue.TestHandler = store;
using (var worker = new QueueWorker(root))
{
    Send(worker, root, new ShopRequest { Operation = "preview-install", Namespace = "Team", Package = "UI", Version = "2.0.0" }); await Pump(worker);
    var p = Read(root);
    Check(p.State == "preview" && p.Error.Length == 0, "worker update preview of MCE-protected Team-UI is not blocked by uninstall protection: " + p.State + " " + p.Error.Split('\n')[0]);
    Send(worker, root, new ShopRequest { Operation = "approve-download", PreviewId = p.Id, PlanToken = p.PlanToken }); await Pump(worker);
    var r = Read(root);
    Check(r.State == "ready" && !r.Error.Contains("MCE required component"), "worker update download stage reaches file preview, not MCE uninstall protection: " + r.State + " " + r.Error.Split('\n')[0] + " " + string.Join(";", r.Blockers));
}
ShopQueue.TestHandler = null;

// ---------- B. Collector evidence review ----------
{
    string r = Standard(out _); var g = Graph(r);
    Check(g.GetCapability("Team-Leaf").CanUninstall && g.GetCapability("Team-Ordinary").CanUninstall && g.Failures.Count == 0, "baseline: unrelated mods deletable, no failures: " + string.Join("|", g.Failures));
    Manifest(r, "Team-Core", "patchers"); Write(Path.Combine(r, "patchers/Team-Core"), "Preloader", null, null, null, null);
    g = Graph(r); var core = g.Components.Single(c => c.Id == "Team-Core");
    Check(core.Locations.Any(l => l.StartsWith("core/")) && core.Locations.Any(l => l.StartsWith("patchers/")) && core.PluginGuids.Count == 0, "same package core+patchers merges to one node; no fabricated plugin GUID");
}
{
    string r = Standard(out _);
    Write(Manifest(r, "Other-Dup"), "ThirdPartyCore", null, null, null, null);
    var g = Graph(r);
    Check(g.GetCapability("Team-Leaf").CanUninstall, "duplicate assembly providers do not make an unrelated mod unknown: " + g.GetCapability("Team-Leaf").Reason);
    Check(!g.GetCapability("Other-Dup").CanUninstall && !g.GetCapability("Team-Core").CanUninstall, "duplicate provider satisfying a protected ref is conservatively protected");
}
{
    string r = Standard(out _);
    Write(Manifest(r, "Team-Unrelated2"), "Unrel2", "unrel2", null, new[] { "System.NoSuchFacade" }, null);
    var g = Graph(r);
    Check(g.GetCapability("Team-Leaf").CanUninstall && g.GetCapability("Team-Ordinary").CanUninstall, "unresolved system ref in an unrelated mod does not make other uninstalls unknown: " + g.GetCapability("Team-Leaf").Reason);
}
{
    string r = Standard(out _);
    Write(Path.Combine(r, "plugins/Team-UI"), "UI", uiGuid, null, new[] { "ThirdPartyCore", "System.NoSuchFacade" }, null);
    var g = Graph(r);
    Check(g.GetCapability("Team-Leaf").Unknown && g.GetCapability("Team-Core").ProtectionPath.Count == 4, "unresolved ref inside MCE closure = explicit unknown for others while the known chain stays protected");
}
{
    string r = Standard(out string managed);
    File.WriteAllBytes(Path.Combine(managed, "System.dll"), Build("System", null, null, null, new Version(4, 0, 0, 0)));
    File.WriteAllBytes(Path.Combine(r, "plugins/Team-UI/UI.dll"), Build("UI", uiGuid, null, new[] { ("ThirdPartyCore", new Version(1, 0)), ("System", new Version(2, 0, 0, 0)) }));
    var g = Graph(r);
    Check(!g.GetCapability("Team-Leaf").Unknown, "MCE-closure mod referencing System 2.0.0.0 vs game Managed System 4.0.0.0 is not unknown: " + string.Join(" | ", g.Edges.Where(e => e.To.StartsWith("missing:")).Select(e => e.Evidence)));
}
{
    string r = Standard(out _);
    File.WriteAllBytes(Path.Combine(r, "plugins/Team-Leaf/broken.dll"), new byte[] { 1, 2, 3 });
    var g = Graph(r);
    Check(g.GetCapability("Team-Ordinary").CanUninstall, "one unrelated corrupt DLL (in Team-Leaf) does not lock uninstall of Team-Ordinary: " + g.GetCapability("Team-Ordinary").Reason);
    Check(g.GetCapability("Team-Leaf").CanUninstall, "corrupt DLL owner Team-Leaf can still be uninstalled (repair path): " + g.GetCapability("Team-Leaf").Reason);
    Check(!g.GetCapability("Team-Core").CanUninstall, "known MCE chain remains protected despite the corrupt DLL");
}
{
    string r = Standard(out _);
    File.WriteAllText(Path.Combine(r, "plugins/Team-Leaf/manifest.json"), "not json");
    var g = Graph(r);
    Check(g.GetCapability("Team-Ordinary").CanUninstall, "one unrelated corrupt manifest does not lock uninstall of Team-Ordinary: " + g.GetCapability("Team-Ordinary").Reason);
}
{
    string r = Standard(out _);
    Write(Manifest(r, "Team-Optional"), "Optional", "optional", null, null, null, disabled: true);
    var g = Graph(r);
    Check(g.GetCapability("Team-Optional").CanUninstall && g.Edges.Any(e => e.To == "Team-Optional" && !e.Hard), "soft dependency evidence retained as soft and not protecting (disabled provider too)");
    File.Move(Path.Combine(r, "plugins/Team-UI/UI.dll"), Path.Combine(r, "plugins/Team-UI/UI.dll.old"));
    Check(!Graph(r).GetCapability("Team-Core").CanUninstall, "disabled hard dependency in chain still protects");
}
{
    string r = Standard(out _); // game-data style nested manifest.json (not a package manifest) inside an unrelated mod
    Directory.CreateDirectory(Path.Combine(r, "plugins/Team-Leaf/assets")); File.WriteAllText(Path.Combine(r, "plugins/Team-Leaf/assets/manifest.json"), "{\"files\":[]}");
    var g = Graph(r);
    Check(g.GetCapability("Team-Ordinary").CanUninstall, "a nested non-package manifest.json inside one unrelated mod does not lock all uninstalls: " + g.GetCapability("Team-Ordinary").Reason);
}
{
    string r = Standard(out _); // managed-looking but non-PE .dll data file in an unrelated mod
    File.WriteAllText(Path.Combine(r, "plugins/Team-Leaf/readme.dll"), "text");
    var g = Graph(r);
    Check(g.GetCapability("Team-Ordinary").CanUninstall, "one non-PE .dll data file in an unrelated mod does not lock all uninstalls: " + g.GetCapability("Team-Ordinary").Reason);
}
{
    string r = Standard(out _); // ordinary Thunderstore-format manifest (no namespace field) in a manually named folder
    string manual = Path.Combine(r, "plugins/ManualMod"); Directory.CreateDirectory(manual);
    File.WriteAllText(Path.Combine(manual, "manifest.json"), JsonSerializer.Serialize(new { name = "ManualMod", version_number = "1.0.0", dependencies = Array.Empty<string>() }));
    Write(manual, "Manual", "manual", null, null, null);
    var g = Graph(r);
    Check(g.GetCapability("Team-Ordinary").CanUninstall, "a manually installed mod folder without Author- prefix (standard manifest has no namespace) does not lock all other uninstalls: " + g.GetCapability("Team-Ordinary").Reason);
}
Console.WriteLine($"{passed} passed, {failed} failed; sandbox fixtures: {suite}");
return failed == 0 ? 0 : 1;

sealed class UpdateStore : HttpMessageHandler
{
    internal Func<byte[]> Dll = () => Array.Empty<byte>();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        string url = request.RequestUri!.AbsoluteUri;
        if (url.Contains("/api/experimental/package/"))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { version_number = "2.0.0", download_url = "https://example.test/UI.zip", dependencies = Array.Empty<string>() })) });
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open())) w.Write(JsonSerializer.Serialize(new { name = "UI", version_number = "2.0.0", dependencies = Array.Empty<string>() }));
            using var s = zip.CreateEntry("UI.dll").Open(); var bytes = Dll(); s.Write(bytes, 0, bytes.Length);
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(ms.ToArray()) });
    }
}
