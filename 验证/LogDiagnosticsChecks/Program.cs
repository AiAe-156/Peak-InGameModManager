using System.IO.Compression;
using System.Text;
using ModConfigEnhance.Diagnostics;
using Newtonsoft.Json.Linq;
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
void Reject(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
string root = Path.Combine(Path.GetTempPath(), "IGMM-LogDiagnostics", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")); Directory.CreateDirectory(root);
var parsed = LogParser.Parse("[Error  :Voice] failure", "e", 1);
Check(parsed.Level == MceLogLevel.Error && parsed.Category == "Voice" && parsed.Message == "failure", "BepInEx header");
Check(LogParser.Parse("NullReferenceException: test", "e", 2).Level == MceLogLevel.Error, "Unity exception");
Check(LogParser.Parse("[12:34:56] [Warning:Test] warning", "e", 3).Time != default, "time parse");
Check(LogParser.Parse("<color=red>plain</color>", "e", 4).Message == "<color=red>plain</color>", "raw markup preserved");
string log = Path.Combine(root, "Player.log"); File.WriteAllText(log, "[Error:Test] broken\n  at Test.Call()\nplain\n", new UTF8Encoding(false));
using (var reader = new LogFileReader(log))
{
    var first = reader.Read(); Check(first.Length == 2 && first[0].StackTrace.Contains("Test.Call"), "stack joins exception");
    File.AppendAllText(log, "[Info:Test] 中文\n"); var second = reader.Read(); Check(second.Length == 3 && second[2].Message == "中文", "incremental unicode");
    Check(first[0].Time == default, "historical unknown time not fabricated");
    Check(second[2].Time == default && !second[2].Fields.ContainsKey("timestampKind"), "untimed file lines remain unknown instead of borrowing poll time");
    File.WriteAllText(log, "new\n"); Check(reader.Read().Single().Message == "new", "truncate reset");
    File.WriteAllText(log, "other same prefix replacement\n"); Check(reader.Read().Single().Message == "other same prefix replacement", "replacement reset");
}
using (var channel = MceLogApi.CreateChannel("test.guid", "memory", "Memory", 2))
{
    channel.Write(new() { Message = "one" }); channel.Write(new() { Message = "two" }); channel.Write(new() { Message = "three" });
    var source = MceLogApi.List().Single(); var snapshot = MceLogApi.Read(source); Check(snapshot.Length == 2 && snapshot[0].Message == "two", "bounded channel");
    string epoch = snapshot[0].Epoch; snapshot[0].Message = "changed"; Check(MceLogApi.Read(source)[0].Message == "two", "snapshot isolation");
    channel.Clear(); channel.Write(new() { Message = "reset" }); var reset = MceLogApi.Read(source).Single(); Check(reset.Epoch != epoch && reset.Sequence == 1, "clear epoch");
    Reject(() => MceLogApi.CreateChannel("test.guid", "memory", "Duplicate"), "unique source");
}
Check(MceLogApi.List().Length == 0, "unregister");
using (var bridge = MceLogApi.RegisterTextSnapshot("voice", "diagnostic", "Voice", () => new Dictionary<long, string> { [8] = "[12:34:56] [Info:Local] Player: hi" }, () => "epoch"))
{
    var entry = MceLogApi.Read(MceLogApi.List().Single()).Single(); Check(entry.Sequence == 8 && entry.Epoch == "epoch" && entry.Category == "Local", "reflection bridge shape");
}
var scrub = LogExport.Scrub(JObject.Parse("{ 'key':'ApiKey','previous':'old-secret','current':'new-secret','fields':{'token':'abc'},'normal':'hello' }"));
Check(scrub["previous"]!.ToString() == "[已遮盖]" && scrub["current"]!.ToString() == "[已遮盖]", "both diff values redacted");
Check(scrub["fields"]!["token"]!.ToString() == "[已遮盖]" && scrub["normal"]!.ToString() == "hello", "custom fields redaction");
string zip = Path.Combine(root, "export.zip"); string other = Path.Combine(root, "LogOutput.log"); File.WriteAllText(other, "raw <tag> 中文\n");
var sources = new[] { new LogExportSource { Name = "BepInEx_LogOutput.log", Path = other }, new LogExportSource { Name = "PEAK_Player.log", Path = log } };
LogExport.Save(zip, sources, new JObject { ["schemaVersion"] = 1 });
using (var archive = ZipFile.OpenRead(zip))
{
    Check(archive.Entries.Count == 3, "default exactly three files");
    using var raw = new StreamReader(archive.GetEntry("BepInEx_LogOutput.log")!.Open()); Check(raw.ReadToEnd() == File.ReadAllText(other), "raw export unchanged");
    using var info = new StreamReader(archive.GetEntry("运行信息.json")!.Open()); var metadata = JObject.Parse(info.ReadToEnd()); Check(metadata["collection"]!["sources"]!.Count() == 2, "capture scope metadata");
}
LogExport.Save(zip, new[] { sources[0] }, null); using (var archive = ZipFile.OpenRead(zip)) Check(archive.Entries.Count == 1, "metadata optional");
string failedZip = Path.Combine(root, "missing.zip"); Reject(() => LogExport.Save(failedZip, new[] { new LogExportSource { Name = "missing.log", Path = Path.Combine(root, "missing.log") } }, new()), "missing source fails");
Check(!File.Exists(failedZip) && Directory.GetFiles(root, "*.partial").Length == 0, "no partial success artifact");
LogExport.Save(Path.Combine(root, "custom.zip"), new[] { new LogExportSource { Name = "custom.log", Records = new[] { new MceLogRecord { Message = "custom" } } } }, null);
Check(File.Exists(Path.Combine(root, "custom.zip")), "custom snapshot export");
var pendingSecret = LogExport.Scrub(JObject.Parse("{ 'target':{'key':'Password','serializedValue':'new'}, 'previous':'old' }"));
Check(pendingSecret["previous"]!.ToString() == "[已遮盖]" && pendingSecret["target"]!.ToString() == "[已遮盖]", "pending previous and target redacted together");
// 0.0.2 导出检查（2026-11-18）：个人路径遮盖不分大小写、正反斜杠都认，不误伤同前缀的别的用户名
Check(LogExport.RedactPath(@"C:\Users\Alice\AppData\x.cfg", @"C:\Users\Alice") == @"%USERPROFILE%\AppData\x.cfg", "redact backslash path");
Check(LogExport.RedactPath("c:/users/alice/Documents/PEAK", @"C:\Users\Alice") == "%USERPROFILE%/Documents/PEAK", "redact lower-case forward-slash path");
Check(LogExport.RedactPath(@"C:\Users\Alice", @"C:\Users\Alice\") == "%USERPROFILE%", "redact bare profile with trailing separator");
Check(LogExport.RedactPath(@"C:\Users\Alice2\x", @"C:\Users\Alice") == @"C:\Users\Alice2\x", "longer user name untouched");
Check(LogExport.RedactPath(@"D:\Games\PEAK", "") == @"D:\Games\PEAK", "empty profile leaves path");
string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
Check(LogExport.Scrub(new JValue(home.ToUpperInvariant().Replace('\\', '/') + "/x")).ToString() == "%USERPROFILE%/x", "scrub redacts unity-style profile path");
// 中文、空格、方括号、# 与 & 的目录照常导出
string odd = Path.Combine(root, "导出 测试 [x] #1 & 'q'"); Directory.CreateDirectory(odd);
string oddZip = Path.Combine(odd, "PEAK日志_测试.zip"); LogExport.Save(oddZip, sources, new JObject { ["schemaVersion"] = 1 });
using (var archive = ZipFile.OpenRead(oddZip)) Check(archive.Entries.Count == 3 && archive.GetEntry("运行信息.json") != null, "unicode / special path export");
// 来源正被游戏写着（共享写）照样能导出
using (var live = new FileStream(other, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { LogExport.Save(Path.Combine(root, "live.zip"), new[] { sources[0] }, null); }
Check(File.Exists(Path.Combine(root, "live.zip")), "source held open by writer still exports");
// 目标文件被别的程序独占：导出失败、原文件不变、不留 .partial
string lockedZip = Path.Combine(root, "locked.zip"); File.WriteAllText(lockedZip, "previous");
using (var held = new FileStream(lockedZip, FileMode.Open, FileAccess.Read, FileShare.None)) Reject(() => LogExport.Save(lockedZip, new[] { sources[0] }, null), "locked destination fails");
Check(File.ReadAllText(lockedZip) == "previous" && Directory.GetFiles(root, "*.partial").Length == 0, "locked destination left intact, no partial");
// 目标文件夹被删掉：报错，不静默
Reject(() => LogExport.Save(Path.Combine(root, "gone", "x.zip"), new[] { sources[0] }, null), "missing destination folder fails");
string large = Path.Combine(root, "large.log"); using (var writer = new StreamWriter(large)) for (int i = 0; i < 80000; i++) writer.WriteLine($"[Info:Large] {i:D6} " + new string('x', 60));
using (var reader = new LogFileReader(large)) { var recent = reader.Read(); var earlier = reader.Read(true); Check(recent.Length > 0 && earlier.Length > 0 && string.CompareOrdinal(earlier[^1].Message, recent[^1].Message) < 0, "earlier view loads previous bounded block"); }
var repeats = new LogRepeatIndex();
MceLogRecord Error(long seq, string msg, string stack = "at A", string source = "ModA") => new() { Epoch = "repeat-session", Sequence = seq, Level = MceLogLevel.Error, Category = source, Message = msg, StackTrace = stack, Time = DateTimeOffset.Now.AddSeconds(seq) };
var e1 = Error(1, "first" ); var e2 = Error(2, "second"); var e3 = Error(3, "first");
repeats.Update(new[] { e1, e2, e3 });
Check(repeats.Groups.Count == 2 && repeats.Groups[0].Count == 2, "same error merges and counts");
repeats.Update(new[] { e1, e2, e3 }); Check(repeats.Groups[0].Count == 2, "refresh never recounts snapshot");
repeats.Update(new[] { e2, e3, Error(4, "second"), Error(5, "first") });
Check(repeats.Groups[0].First.Sequence == 1 && repeats.Groups[0].Count == 3 && repeats.Groups[1].Count == 2, "pinned errors keep first appearance order rather than latest order");
Check(repeats.Groups[0].Latest.Sequence == 5 && repeats.Groups[0].CopyText.Contains("重复 x3"), "copy includes count and latest record");
Check(repeats.Groups.OrderBy(g => g.LatestOrder).Last().Latest.Sequence == 5, "normal stacked entries move to latest position when repeated");
repeats.Update(new[] { Error(6, "first", "at B"), Error(7, "first", "at A", "ModB") });
Check(repeats.Groups.Count == 4, "stack and source differences never merge");
var freshError = Error(1, "first"); freshError.Epoch = "new-session"; repeats.Update(new[] { freshError });
Check(repeats.Groups.Count == 1 && repeats.Groups[0].Count == 1, "file reset clears repeat counters");
var wrappedText = "PEAKLib.Core.Loading failed because a long identifier is present";
var wrappedLines = LogTextWrap.Lines(wrappedText, 160, s => s.Length * 8);
Check(wrappedLines.All(s => s.Length > 1 && s.Length <= 20), "wrap fills available width instead of splitting one character per line");
Check(string.Concat(wrappedLines) == wrappedText, "wrapping preserves exact source text");
Check(LogTextWrap.Lines("中文换行测试", 32, s => s.Length * 16).Length == 3, "CJK width-based wrapping");
Check(LogTextWrap.Lines("a\n\nb", 100, s => s.Length * 8).SequenceEqual(new[] { "a", "", "b" }), "explicit newlines and empty lines retained");
Check(LogTextWrap.Lines("short", 500, s => s.Length * 8).Single() == "short", "short line stays intact");
var audit = new LogRepeatIndex();
var middle = new[] { Error(1,"A"), Error(2,"A"), Error(3,"C") };
audit.Update(middle); string beforeStamp=LogRepeatIndex.SnapshotStamp(middle);
middle[0]=Error(1,"A","revised"); audit.Update(middle);
Check(audit.Groups.Single(g=>g.Key==LogRepeatIndex.Key(middle[1])).First.Sequence==2, "revision recomputes first member metadata");
Check(beforeStamp!=LogRepeatIndex.SnapshotStamp(middle), "middle-only change invalidates snapshot stamp");
audit.Update(Array.Empty<MceLogRecord>()); Check(audit.Groups.Count==0, "empty snapshot clears old stacked groups");
Check(LogViewRules.Collapse(new[]{"a","b","c","d","e"},false).SequenceEqual(new[]{"a","b","···+3"}), "long entries show two lines and remaining count");
Check(LogViewRules.Collapse(new[]{"a","b","c","d"},true).Length==4, "expanded entry preserves all visual lines");
Check(LogViewRules.CycleLevel(0,-1,7)==6 && LogViewRules.CycleLevel(6,1,7)==0, "mouse wheel level cycles wrap at boundaries");

// Serialize the actual diagnostic metadata with controlled Unity/configuration boundaries.
BepInEx.Paths.BepInExRootPath = root;
string componentA = Path.Combine(root, "core", "MonoDetour", "runtime.dll");
string componentB = Path.Combine(root, "patchers", "MonoDetour", "patcher.dll");
Directory.CreateDirectory(Path.GetDirectoryName(componentA)!); Directory.CreateDirectory(Path.GetDirectoryName(componentB)!);
File.WriteAllText(componentA, "runtime"); File.WriteAllText(componentB, "patcher");
ModConfigEnhance.Overview.InfrastructurePackages.Local.Add(new() {
    Name = "MonoDetour", Key = "MonoDetour-MonoDetour", Version = "0.7.1701",
    RuntimeFiles = new() { componentA }, Areas = new() { "core" },
    Locations = new() { new() { Area = "core", Path = Path.GetDirectoryName(componentA)! } }
});
ModConfigEnhance.Overview.InfrastructurePackages.Local.Add(new() {
    Name = "MonoDetour_BepInEx_5", Key = "MonoDetour-MonoDetour_BepInEx_5", Version = "0.7.1701",
    RuntimeFiles = new() { componentA, componentB }, Areas = new() { "core", "patchers" },
    Locations = new() { new() { Area = "core", Path = Path.GetDirectoryName(componentA)! }, new() { Area = "patchers", Path = Path.GetDirectoryName(componentB)! } }
});
string resource = Path.Combine(root, "manifest.json"); File.WriteAllText(resource, "{}");
var repository = new ModConfigEnhance.Overview.OverviewRepository();
var modRows = new[] { new ModConfigEnhance.Overview.OverviewMod { RawName = "Textures", Guid = "resource:Textures", PluginVersion = "1.0.0", Location = resource, ResourcePack = true } };
var diagnostic = repository.CaptureDiagnosticMetadata(modRows);
Check((string?)diagnostic["igmmVersion"] == "0.0.1" && (string?)diagnostic["mceVersion"] == "0.0.1", "declared plugin version instead of test assembly version");
Check((string?)diagnostic["gameVersion"] == "3.1.0" && (string?)diagnostic["gameVersionRaw"] == "3.1.0--logFile", "clean game version and preserve raw build string");
Check(diagnostic["components"]!.Count() == 2 && diagnostic["components"]!.Sum(c => c["locations"]!.Count()) == 3, "all components and every core/patcher location exported");
Check(diagnostic["components"]!.All(c => (string?)c["version"] == "0.7.1701" && (bool)c["installed"]!), "component version and existence captured");
Check(diagnostic["components"]![1]!["runtimeFiles"]!.Count() == 2, "runtime files retained across loader areas");
Check(!(bool)diagnostic["components"]![0]!["locations"]![0]!["manifestVerified"]!, "cross-directory manifest evidence not mislabeled local");
Check((bool)diagnostic["mods"]![0]!["enabledNextStart"]!, "enabled resource manifest is not treated as disabled DLL");
Check(!diagnostic["components"]!.Any(c => c["loaded"] != null), "component load state is not invented from Chainloader plugin inventory");
UnityEngine.Application.version = "3.1.0-beta.2";
Check((string?)repository.CaptureDiagnosticMetadata(modRows)["gameVersion"] == "3.1.0-beta.2", "legitimate prerelease suffix preserved");
UnityEngine.Application.version = "3.1.0 --logFile path";
Check((string?)repository.CaptureDiagnosticMetadata(modRows)["gameVersion"] == "3.1.0", "spaced command suffix removed");
UnityEngine.Application.version = "3.1.0";
Check((string?)repository.CaptureDiagnosticMetadata(modRows)["gameVersion"] == "3.1.0", "normal release version unchanged");
LogExport.Save(Path.Combine(root, "metadata.zip"), sources, diagnostic);
using (var archive = ZipFile.OpenRead(Path.Combine(root, "metadata.zip")))
using (var reader = new StreamReader(archive.GetEntry("运行信息.json")!.Open()))
{
    var roundtrip = JObject.Parse(reader.ReadToEnd());
    Check(roundtrip["components"]!.Count() == 2 && (string?)roundtrip["igmmVersion"] == "0.0.1", "fixed metadata survives the actual ZIP export path");
}

Console.WriteLine($"PASS {checks} diagnostic checks; fixtures: {root}");
