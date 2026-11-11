using ModConfigEnhance.Overview;
int checks = 0;
void Equal<T>(T value, T expected, string name) { if (!EqualityComparer<T>.Default.Equals(value, expected)) throw new Exception($"{name}: expected [{expected}] got [{value}]"); checks++; }
var configured = new[] { ("later", "分组2", 20), ("first", "分组1", 1), ("empty", "空组", 2) };
var groups = OverviewListView.CaptureCheckProgressGroups(configured, new[] { "later", "first", "", "unknown", "later" }, "基础组件(Core/Patcher)", "未分组");
Equal(string.Join(",", groups.Select(g => g.Id)), "__infrastructure,first,empty,later,,unknown", "existing stable order, missing rules appended");
var counts = new Dictionary<string, (int, int)> { ["__infrastructure"] = (5, 5), ["first"] = (10, 10), ["later"] = (25, 28), [""] = (0, 1) };
int calls = 0;
(int, int)? Count(string id) { calls++; return counts.TryGetValue(id, out var c) ? c : null; }
string detail = " (基础组件(Core/Patcher)_5/5, 分组1_10/10, 分组2_25/28, 未分组_0/1)";
Equal(OverviewListView.FormatCheckProgress("正在检查更新 40/44", groups, Count), "正在检查更新 40/44" + detail, "checking single-line exact");
Equal(calls, groups.Length, "one lookup per captured group");
foreach (string summary in new[] { "共检查了 69/69 个模组，检查出 3 个可更新模组。", "Checked 69/69 mods; 3 mods have updates.", "共檢查了 69/69 個模組，檢查出 3 個可更新模組。" }) {
    Equal(OverviewListView.FormatCheckProgress(summary, groups, Count, true), summary.Insert(summary.IndexOf("69/69") + 5, detail), "completed keeps localized result and groups");
}
Equal(OverviewListView.FormatCheckProgress("更新检查已取消。共检查了 40/44 个模组，检查出 2 个可更新模组。", groups, Count, true), "更新检查已取消。共检查了 40/44" + detail + " 个模组，检查出 2 个可更新模组。", "cancel preserves actual partial counts and K");
Equal(OverviewListView.FormatCheckProgress("Checked 40/44; 2 updates.\nFailed\r\nnetwork", groups, Count, true), "Checked 40/44" + detail + "; 2 updates. · Failed · network", "failure no line breaks");
Equal(OverviewListView.FormatCheckProgress("Checking", Array.Empty<(string, string)>(), Count), "Checking", "empty capture");
Equal(OverviewListView.FormatCheckProgress("Summary", groups, _ => null, true), "Summary", "no participating groups");
Equal(OverviewListView.FormatCheckProgress("Checking", new[] { ("a", "A\r\nB\u2028C") }, _ => (0, 0)), "Checking (A · B C_0/0)", "zero counts and hostile name remain single-line");
var many = Enumerable.Range(0, 200).Select(i => ($"id{i}", new string('长', 60) + i)).ToArray();
string longText = OverviewListView.FormatCheckProgress("Checked 200/200; 3 updates.", many, _ => (1, 1), true);
Equal(longText.Contains(many[^1].Item2 + "_1/1"), true, "all long groups retained for full hover");
Equal(longText.Length > 12000, true, "no destructive character cutoff");
Equal(longText.Contains('\n'), false, "long text single-line");
configured[1] = ("rename", "changed", -1);
Equal(groups[1].Name, "分组1", "capture independent of mutable model");
var state = new RefreshIconFeedbackState();
Equal(state.NeedsFrame, false, "idle no frames");
state.Pointer = true; Equal(state.NeedsFrame, true, "pointer wakes");
state.Step(.08f); Equal(state.Amount, .5f, "160ms transition half");
state.Step(.08f); Equal(state.Amount, 1f, "hover settled"); Equal(state.NeedsFrame, false, "settled hover no frames");
state.Focus = true; state.Pointer = false; Equal(state.NeedsFrame, false, "focus retains feedback after pointer exit");
state.Focus = false; state.Step(.04f); Equal(state.Amount, .75f, "exit returns");
state.Pointer = true; state.Step(.04f); Equal(state.Amount, 1f, "reenter reverses without drift");
state.Available = false; state.Step(1); Equal(state.Amount, 0f, "disabled returns identity"); Equal(state.NeedsFrame, false, "disabled settles");
state.Available = true; state.Focus = true; state.Step(1); state.Reset();
Equal(state.Amount, 0f, "close resets"); Equal(state.Pointer || state.Focus, false, "close clears stale hover/focus"); Equal(state.NeedsFrame, false, "close no frames");
state.Step(-1); Equal(state.Amount, 0f, "negative delta safe");
Console.WriteLine($"Update feedback: {checks} production-linked checks passed; no HTTP or live data.");
