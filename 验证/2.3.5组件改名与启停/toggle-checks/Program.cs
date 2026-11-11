// 2.3.5: real ComponentToggle + real InstallationWriteLease/PluginFileNames on a temporary BepInEx tree. Stubs only for
// the overview model, the dependency diagnostic, Loc and the log.
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;

int passed = 0;
void Check(bool c, string n) { if (!c) throw new Exception("FAIL " + n); passed++; Console.WriteLine("ok  " + n); }
string temp = Path.Combine(Path.GetTempPath(), "mce-235-" + Guid.NewGuid().ToString("N"));
string bep = Path.Combine(temp, "BepInEx"); Directory.CreateDirectory(Path.Combine(bep, "core", "Pkg")); Directory.CreateDirectory(Path.Combine(bep, "patchers"));
Directory.CreateDirectory(Path.Combine(bep, "plugins"));
BepInEx.Paths.BepInExRootPath = bep;
string a = Path.Combine(bep, "core", "Pkg", "A.dll"), b = Path.Combine(bep, "patchers", "0.B.dll");
File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
var pkg = new InfrastructureOverviewModel("Team-Pkg", true, new[] { a, b });
try
{
    Check(pkg.Enabled, "starts enabled");
    Check(ComponentToggle.BlockReason(pkg, false) == "", "may be disabled");
    Check(ComponentToggle.Set(pkg, false) == 2, "disable renames both files");
    Check(File.Exists(a + ".d") && File.Exists(b + ".d") && !File.Exists(a) && !File.Exists(b), "files are now .dll.d");
    Check(!File.Exists(Path.Combine(bep, "MCE", "enablement-recovery.bin")), "journal committed and removed");
    var off = new InfrastructureOverviewModel("Team-Pkg", true, new[] { a + ".d", b + ".d" });
    Check(!off.Enabled, "rescan reads disabled");
    Check(ComponentToggle.Set(off, false) == 0, "disabling again renames nothing");
    Check(ComponentToggle.Set(off, true) == 2 && File.Exists(a) && File.Exists(b), "enable renames back to .dll");

    // MCE-required: cannot be disabled, can always be enabled.
    SourceUi.Required = true;
    Check(ComponentToggle.BlockReason(pkg, false).Length > 0, "MCE-required component cannot be disabled");
    Check(ComponentToggle.BlockReason(pkg, true) == "", "MCE-required component may be enabled");
    try { ComponentToggle.Set(pkg, false); Check(false, "Set refuses MCE-required"); } catch (InvalidOperationException) { Check(true, "Set refuses MCE-required"); }
    Check(File.Exists(a), "refused disable leaves files untouched");
    SourceUi.Required = false;

    // Unconfirmed / no files / foreign disabled.
    Check(ComponentToggle.BlockReason(new InfrastructureOverviewModel("x", false, new[] { a }), false).Length > 0, "unconfirmed identity refused");
    Check(ComponentToggle.BlockReason(new InfrastructureOverviewModel("Team-Empty", true, Array.Empty<string>()), false).Length > 0, "no assembly refused");
    string foreign = Path.Combine(bep, "core", "Pkg", "C.dll.disabled-by-r2modman"); File.WriteAllText(foreign, "c");
    var other = new InfrastructureOverviewModel("Team-Other", true, new[] { foreign });
    Check(ComponentToggle.BlockReason(other, true).Length > 0, "foreign-disabled cannot be enabled here");

    // Collision: a target already exists → nothing renamed.
    File.WriteAllText(a + ".d", "stale");
    try { ComponentToggle.Set(pkg, false); Check(false, "collision refused"); } catch (IOException) { Check(true, "collision refused"); }
    Check(File.Exists(a) && File.Exists(b) && !File.Exists(b + ".d"), "collision renames nothing (all-or-nothing preflight)");
    File.Delete(a + ".d");

    // Outside BepInEx is refused.
    string outside = Path.Combine(temp, "Outside.dll"); File.WriteAllText(outside, "o");
    try { ComponentToggle.Set(new InfrastructureOverviewModel("Team-Out", true, new[] { outside }), false); Check(false, "outside BepInEx refused"); }
    catch (IOException) { Check(true, "outside BepInEx refused"); }
    Check(File.Exists(outside), "outside file untouched");
}
finally { try { Directory.Delete(temp, true); } catch { } }
Console.WriteLine($"component toggle checks passed: {passed}");

namespace BepInEx { internal static class Paths { internal static string BepInExRootPath = ""; } }
namespace ModConfigEnhance
{
    internal static class Loc { internal static string T(string en, string zh, string tw) => en; internal static string F(string en, string zh, string tw, params object[] a) => string.Format(en, a); }
}
namespace ModConfigEnhance.Diagnostics { internal static class ManagerLog { internal static void Info(string m) => Console.WriteLine("[log] " + m); } }
namespace ModConfigEnhance.Overview
{
    internal sealed class InfrastructureOverviewModel
    {
        internal InfrastructureOverviewModel(string key, bool confirmed, string[] files) { Key = key; IdentityConfirmed = confirmed; RuntimeFiles = files; }
        internal string Key { get; }
        internal bool IdentityConfirmed { get; }
        internal IReadOnlyList<string> RuntimeFiles { get; }
        internal bool Enabled => RuntimeFiles.Count == 0 || RuntimeFiles.Any(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    }
    internal sealed class DiagnosticSummary { internal enum Status { Ready, RequiredByMce } internal Status State; }
    internal static class SourceUi
    {
        internal static bool Required;
        internal static DiagnosticSummary DependencyDiagnostic(string key) => new() { State = Required ? DiagnosticSummary.Status.RequiredByMce : DiagnosticSummary.Status.Ready };
    }
}
