using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;

int passed = 0, failed = 0;
void Check(string name, bool ok, string detail = "") { if (ok) passed++; else failed++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name + (ok || detail.Length == 0 ? "" : " · " + detail)); }
UpdateJob J(string id, string batch, string batchAt, string created, string updated) => new() { Id = id, BatchId = batch, BatchCreatedUtc = batchAt, CreatedUtc = created, UpdatedUtc = updated };

// 1. Synthetic: two real batches, two legacy clicks 2 h apart, one undated record.
var jobs = new List<UpdateJob> {
    J("a1", "B1", "2026-11-08T11:50:00Z", "2026-11-08T11:50:01Z", "2026-11-08T13:36:06Z"),
    J("a2", "B1", "2026-11-08T11:50:00Z", "2026-11-08T11:50:02Z", "2026-11-08T13:36:07Z"),
    J("b1", "B2", "2026-11-04T17:22:34Z", "2026-11-04T17:22:34Z", "2026-11-05T04:41:09Z"),
    J("l1", "", "", "2026-11-02T12:40:19Z", ""),
    J("l2", "", "", "2026-11-02T12:42:00Z", ""),
    J("l3", "", "", "2026-11-02T09:04:05Z", ""),
    J("u1", "", "", "", ""),
};
var groups = UpdateHistoryBatches.Group(jobs);
Check("合成：分出 5 组", groups.Count == 5, string.Join(",", groups.Select(g => g.Key)));
Check("合成：最近批次在前", groups[0].Key == "B1" && groups[1].Key == "B2", string.Join(",", groups.Select(g => g.Key)));
Check("合成：组内最近完成在前", groups[0].Jobs[0].Id == "a2");
Check("合成：旧记录按 5 分钟拆开", groups[2].Jobs.Select(j => j.Id).OrderBy(x => x).SequenceEqual(new[] { "l1", "l2" }) && groups[3].Jobs.Single().Id == "l3",
    string.Join(" | ", groups.Skip(2).Select(g => string.Join(",", g.Jobs.Select(j => j.Id)))));
Check("合成：无时间的排最后且无起始时间", groups[^1].Key == "undated" && groups[^1].StartedUtc == null);
Check("合成：每条恰好出现一次", groups.SelectMany(g => g.Jobs).Select(j => j.Id).OrderBy(x => x).SequenceEqual(jobs.Select(j => j.Id).OrderBy(x => x)));
Check("合成：批次起始时间取 BatchCreatedUtc", groups[0].StartedUtc == new DateTime(2026, 11, 8, 11, 50, 0, DateTimeKind.Utc), groups[0].StartedUtc?.ToString("O") ?? "null");

// 2. The live queue copy: exactly the records the history tab shows (not hidden, recorded states).
string sample = Path.Combine(AppContext.BaseDirectory, "../../../queue.sample.json");
using var doc = JsonDocument.Parse(File.ReadAllText(sample));
string S(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
var recorded = new HashSet<string> { "completed", "rolled-back", "superseded" };
var live = doc.RootElement.GetProperty("Jobs").EnumerateArray()
    .Where(e => !(e.TryGetProperty("Hidden", out var h) && h.GetBoolean()) && recorded.Contains(S(e, "State")))
    .Select(e => J(S(e, "Id"), S(e, "BatchId"), S(e, "BatchCreatedUtc"), S(e, "CreatedUtc"), S(e, "UpdatedUtc"))).ToList();
var liveGroups = UpdateHistoryBatches.Group(live);
Console.WriteLine($"现场副本：{live.Count} 条可见记录 → {liveGroups.Count} 组");
foreach (var g in liveGroups) Console.WriteLine("  " + (g.StartedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "更早") + " · " + g.Jobs.Count + " 项");
Check("现场：可见记录数与标签一致（15）", live.Count == 15, live.Count.ToString());
Check("现场：分组后条数不丢", liveGroups.Sum(g => g.Jobs.Count) == live.Count);
Check("现场：不止一组（旧记录不再并成一个「更早」）", liveGroups.Count > 1, liveGroups.Count.ToString());
Check("现场：组按开始时间倒序", liveGroups.Zip(liveGroups.Skip(1)).All(p => p.First.StartedUtc >= p.Second.StartedUtc));
Check("现场：没有落入无时间组", liveGroups.All(g => g.StartedUtc != null));

Console.WriteLine($"=== {passed} passed, {failed} failed ===");
return failed == 0 ? 0 : 1;

namespace ModConfigEnhance.Updating
{
    /// <summary>Stub with only the fields the grouping reads; names and types match the real contract.</summary>
    public sealed class UpdateJob
    {
        public string Id { get; set; } = "";
        public string BatchId { get; set; } = "";
        public string BatchCreatedUtc { get; set; } = "";
        public string CreatedUtc { get; set; } = "";
        public string UpdatedUtc { get; set; } = "";
    }
}
