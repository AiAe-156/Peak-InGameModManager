// 2.3.2 专项检查：链接真实 ShopNetwork.cs（fake-ip 判定）与 ManagerProfileSync.cs（启停行级改写 + 原子写入），
// 其余只补最小桩。夹具全部建在 %TEMP%，只读现场 mods.yml 的副本。
using System.Net;
using System.Text;
using ModConfigEnhance.Overview;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); passed++; Console.WriteLine("ok  " + name); }

// ── 8. fake-ip ────────────────────────────────────────────────
Check(ShopUrlPolicy.IsFakeIp(IPAddress.Parse("198.18.0.1")) && ShopUrlPolicy.IsFakeIp(IPAddress.Parse("198.19.255.254")), "198.18/15 is recognised as fake-ip");
Check(ShopUrlPolicy.IsFakeIp(IPAddress.Parse("::ffff:198.18.3.4")), "IPv4-mapped fake-ip recognised");
Check(!ShopUrlPolicy.IsFakeIp(IPAddress.Parse("198.20.0.1")) && !ShopUrlPolicy.IsFakeIp(IPAddress.Parse("10.0.0.1")), "neighbouring and private ranges are not fake-ip");
// IsPublicAddress keeps its meaning (the existing PolicyChecks still assert 198.18 is not public); acceptance is a separate rule.
Check(!ShopUrlPolicy.IsPublicAddress(IPAddress.Parse("198.18.0.1")), "fake-ip still is not a public address");
foreach (string s in new[] { "127.0.0.1", "10.1.2.3", "192.168.1.1", "172.16.0.1", "169.254.1.1", "100.64.0.1" })
    Check(!ShopUrlPolicy.IsPublicAddress(IPAddress.Parse(s)) && !ShopUrlPolicy.IsFakeIp(IPAddress.Parse(s)), "private stays refused: " + s);
Check(ShopUrlPolicy.IsPublicAddress(IPAddress.Parse("8.8.8.8")), "public IPv4 accepted");
// The acceptance rule in ValidateDestination: whole answer fake-ip → accept; any real private or mixed → refuse.
bool Accept(params string[] answer)
{
    var a = answer.Select(IPAddress.Parse).ToArray();
    bool fake = a.Length > 0 && a.All(ShopUrlPolicy.IsFakeIp);
    return a.Length > 0 && (fake || a.All(ShopUrlPolicy.IsPublicAddress));
}
Check(Accept("198.18.0.7"), "all-fake-ip answer accepted");
Check(!Accept("198.18.0.7", "10.0.0.1"), "fake-ip mixed with private refused");
Check(Accept("104.16.1.1", "104.16.2.2"), "public answer accepted");
Check(!Accept("104.16.1.1", "127.0.0.1"), "public mixed with loopback refused");
string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../../src/Overview/ShopNetwork.cs"));
Check(source.Contains("addresses.All(IsFakeIp)") && source.Contains("!fakeIp && addresses.Any(a => !IsPublicAddress(a))"), "ValidateDestination applies exactly that rule");

// ── 5. mods.yml line surgery for enablement ───────────────────
string live = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "r2modmanPlus-local", "PEAK", "profiles", "123123", "mods.yml");
byte[] original = File.Exists(live) ? File.ReadAllBytes(live) : Encoding.UTF8.GetBytes(Sample);
var text = Encoding.UTF8.GetString(original);
var names = text.Split('\n').Where(l => l.StartsWith("  name: ")).Select(l => l.Substring(8).Trim()).ToList();
Console.WriteLine("profile entries: " + names.Count + (File.Exists(live) ? " (live copy)" : " (sample)"));
string first = names[0], second = names[Math.Min(5, names.Count - 1)];
bool firstWas = EnabledOf(text, first), secondWas = EnabledOf(text, second);
byte[] updated = ManagerProfileSync.SetEnabled(original, new Dictionary<string, bool> { [first] = !firstWas, [second] = !secondWas });
string after = Encoding.UTF8.GetString(updated);
Check(EnabledOf(after, first) == !firstWas && EnabledOf(after, second) == !secondWas, "target entries flipped");
var before = text.Split('\n'); var changedLines = after.Split('\n');
Check(before.Length == changedLines.Length, "line count unchanged (" + before.Length + ")");
int differing = before.Zip(changedLines).Count(p => p.First != p.Second);
Check(differing == (first == second ? 1 : 2), "exactly the enabled lines differ (" + differing + ")");
Check(before.Zip(changedLines).Where(p => p.First != p.Second).All(p => p.First.StartsWith("  enabled: ") && p.Second.StartsWith("  enabled: ")), "only enabled: lines changed");
Check(text.Contains("\r\n") == after.Contains("\r\n") && text.EndsWith("\n") == after.EndsWith("\n"), "newline style and trailing newline preserved");
byte[] same = ManagerProfileSync.SetEnabled(original, new Dictionary<string, bool> { [first] = firstWas });
Check(same.SequenceEqual(original.Skip(original.Length >= 3 && original[0] == 0xEF ? 3 : 0)), "no-op target leaves bytes identical");
try { ManagerProfileSync.SetEnabled(original, new Dictionary<string, bool> { ["Nobody-NotInstalled"] = true }); Check(false, "missing entry rejected"); }
catch (InvalidDataException) { Check(true, "missing entry rejected"); }
try { ManagerProfileSync.SetEnabled(Encoding.UTF8.GetBytes("not: [a, sequence"), new Dictionary<string, bool> { [first] = true }); Check(false, "malformed document rejected"); }
catch (InvalidDataException) { Check(true, "malformed document rejected"); }
string odd = "- manifestVersion: 1\n  name: A-B\n  enabled: yes please\n";
try { ManagerProfileSync.SetEnabled(Encoding.UTF8.GetBytes(odd), new Dictionary<string, bool> { ["A-B"] = false }); Check(false, "non-boolean enabled field rejected"); }
catch (InvalidDataException) { Check(true, "non-boolean enabled field rejected"); }

// ── 5b. ApplyCore writes atomically ───────────────────────────
string temp = Path.Combine(Path.GetTempPath(), "mce-232-" + Guid.NewGuid().ToString("N"));
string profile = Path.Combine(temp, "profile"); Directory.CreateDirectory(Path.Combine(profile, "BepInEx", "MCE"));
File.WriteAllBytes(Path.Combine(profile, "mods.yml"), original);
BepInEx.Paths.BepInExRootPath = Path.Combine(profile, "BepInEx");
string key = names[0];
File.WriteAllText(ManagerProfileSync.PendingPath, Newtonsoft.Json.JsonConvert.SerializeObject(new ModConfigEnhance.Updating.ManagerSyncPending { Records = { new() { Key = key, Version = "9.9.9" } } }));
ManagerProfileSync.RunningProbe = () => false;
ManagerProfileSync.Apply(profile);
Check(OverviewRepository.AtomicWrites.Contains(Path.Combine(profile, "mods.yml")), "version sync goes through AtomicWrite");
Check(File.ReadAllText(Path.Combine(profile, "mods.yml")).Contains("    major: 9"), "version sync applied");
Check(!File.Exists(ManagerProfileSync.PendingPath), "pending record consumed");
try { Directory.Delete(temp, true); } catch { }

// ── source-level assertions for the changes that need Unity / a live worker ──
string Root(string relative) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../../", relative));
string auto = Root("src/Overview/AutomaticUpdateChecks.cs");
Check(auto.Contains("_task=Task.Run(()=>Run(preparedRepo,preparedMods,token),token);") && !auto.Contains("_task=Run(preparedRepo"), "1. automatic check runs off the main thread");
Check(auto.Contains("report.TransientFailed > 0 || report.Error.Length > 0 ? 60 : 1800") && auto.Contains("report.TransientFailed == 0"), "2. only transient failures retry in a minute");
string service = Root("src/Overview/ThunderstoreUpdateService.cs");
Check(service.Contains("catch (InvalidDataException ex)") && service.Contains("result.Settled = true;") && service.Contains("!entry.Settled"), "2. deprecated/404/identity verdicts are settled");
Check(service.Contains("ex.StatusCode != 408 && ex.StatusCode != 429"), "2. rate limiting and timeouts stay transient");
string worker = Root("Updater/QueueWorker.cs");
Check(worker.Contains("T WithPrivateWarnings<T>(UpdateJob job, Func<T> work)") && worker.Split("WithPrivateWarnings(job,").Length - 1 == 8, "3. planning, commit and rollback write warnings privately (8 call sites)");
Check(worker.Contains("void Warn(UpdateJob job, string text) { lock (gate)") && worker.Contains("InstallDependencies.Parse(plan.Dependencies, parseNotes)"), "3. dependency notes are added under the lock");
string queue = Root("src/Overview/OverviewUpdateQueue.cs");
Check(queue.Contains("string tools = PrepareWorkerTools(root, bepRoot, WorkerSource());") && queue.Contains("EnsureWorker(root, bepRoot, tools);") && !queue.Contains("        PrepareWorkerTools(); // Validate"), "4. Post validates once and launches from that copy");
Check(queue.Contains("internal static UpdateNetworkOptions Network { get { lock (Gate)") && Root("src/Overview/ShopNetwork.cs").Contains("var network = OverviewUpdateQueue.Network;"), "7. background network reads never schedule Poll");
string toggle = Root("src/Overview/ModToggleService.cs");
Check(!toggle.Contains("yaml.Save") && toggle.Contains("ManagerProfileSync.SetEnabled(metadata.Original, managerTargets)"), "5. profile-mode enablement is line-level");
Check(Root("src/Overview/ManagerProfileSync.cs").Contains("OverviewRepository.AtomicWrite(yamlPath, output);"), "5. mods.yml written atomically");
string program = Root("Updater/Program.cs");
Check(program.Contains(@"Local\MCE.UpdateWorker.FatalDialog") && program.Contains("if (owned) System.Windows.Forms.MessageBox.Show"), "6. at most one fatal dialog on screen");
Check(Root("Updater/InstallPlanner.cs").Contains("target += PluginFileNames.DisabledSuffix;") && !Root("Updater/InstallPlanner.cs").Contains("\".disabled-by-mce\""), "9. new disabled installs use the current .d suffix");

Console.WriteLine($"2.3.2 checks passed: {passed}");
return 0;

static bool EnabledOf(string yaml, string name)
{
    var lines = yaml.Replace("\r\n", "\n").Split('\n'); int at = Array.FindIndex(lines, l => l == "  name: " + name);
    for (int i = at + 1; i < lines.Length && !lines[i].StartsWith("- "); i++) if (lines[i].StartsWith("  enabled: ")) return bool.Parse(lines[i].Substring(11).Trim());
    throw new Exception("no enabled for " + name);
}

partial class Program
{
    const string Sample = "- manifestVersion: 1\n  name: A-One\n  versionNumber:\n    major: 1\n    minor: 0\n    patch: 0\n  enabled: true\n- manifestVersion: 1\n  name: B-Two\n  versionNumber:\n    major: 2\n    minor: 0\n    patch: 0\n  enabled: false\n";
}

namespace BepInEx { internal static class Paths { internal static string BepInExRootPath = ""; } }
namespace ModConfigEnhance.Overview
{
    internal static class Diagnostics { internal static class ManagerLog { internal static void Info(string m) => Console.WriteLine("[info] " + m); internal static void Warn(string m) => Console.WriteLine("[warn] " + m); } }
    internal static class OverviewRepository
    {
        internal static readonly List<string> AtomicWrites = new();
        internal static void AtomicWrite(string file, string content)
        {
            AtomicWrites.Add(file); string temporary = file + ".tmp";
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temporary, file, file + ".bak"); else File.Move(temporary, file);
        }
    }
    internal static class ShopDocumentRules { internal static string Prepare(string s) => s; }
    internal static class OverviewUpdateQueue { internal static ModConfigEnhance.Updating.UpdateNetworkOptions Network => new(); }
}
namespace ModConfigEnhance.Updating { public sealed class UpdateNetworkOptions { public string Mode { get; set; } = "direct"; public string ProxyAddress { get; set; } = ""; } }
