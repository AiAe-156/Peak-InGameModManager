using System.IO.Compression;
using System.Text;
using ModConfigEnhance.Diagnostics;
using Newtonsoft.Json.Linq;
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
void Reject(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
string root = Path.Combine(AppContext.BaseDirectory, "runs", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")); Directory.CreateDirectory(root);
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
Console.WriteLine($"PASS {checks} diagnostic checks; fixtures: {root}");
