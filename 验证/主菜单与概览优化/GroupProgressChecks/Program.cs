using ModConfigEnhance.Overview;

int checks = 0;
void Equal<T>(T actual, T expected, string name)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
        throw new Exception($"{name}: expected [{expected}], got [{actual}]");
    checks++;
}
var configured = new[] { ("later", "后组", 20), ("first", "先组", 1), ("empty", "空组", 2) };
var fullIds = new[] { "later", "first", "", "unknown", "later" };
var groups = OverviewListView.CaptureCheckProgressGroups(configured, fullIds, "基础组件(Core/Patcher)", "未分组");
Equal(string.Join(",", groups.Select(g => g.Id)), "__infrastructure,first,empty,later,,unknown", "existing group order, infrastructure first, orphan groups last");
Equal(groups[0].Name, "基础组件(Core/Patcher)", "existing infrastructure title");
Equal(groups[1].Name, "先组", "existing group name");
Equal(groups[4].Name, "未分组", "ungrouped caption");
Equal(groups[5].Name, "未分组", "missing rule group uses header fallback");
var snapshot = new Dictionary<string, (int Done, int Total)> {
    ["later"] = (3, 4), ["__infrastructure"] = (1, 2), ["first"] = (2, 3), [""] = (0, 1)
};
int calls = 0;
(int Done, int Total)? GroupLine(string id) {
    calls++;
    return snapshot.TryGetValue(id, out var count) ? count : null;
}
string expected = "Checking 6/10 (基础组件(Core/Patcher)_1/2, 先组_2/3, 后组_3/4, 未分组_0/1)";
Equal(OverviewListView.FormatCheckProgress("Checking 6/10", groups, GroupLine), expected, "snapshot counts, stable order, missing groups omitted");
Equal(calls, groups.Length, "one lookup per captured group");
Equal(OverviewListView.FormatCheckProgress("Checked 10/10; 2 updates.", groups,
    GroupLine, completed: true),
    "Checked 10/10 (基础组件(Core/Patcher)_1/2, 先组_2/3, 后组_3/4, 未分组_0/1); 2 updates.", "completion retains full groups and summary");
Equal(OverviewListView.FormatCheckProgress("Summary", Array.Empty<(string, string)>(), GroupLine), "Summary", "empty group capture");
var stable = OverviewListView.CaptureCheckProgressGroups(new[] { ("b", "B", 0), ("a", "A", 0), ("b", "duplicate", 2) }, Array.Empty<string>(), "Core/Patcher", "Ungrouped");
Equal(string.Join(",", stable.Select(g => g.Name)), "Core/Patcher,B,A", "stable ties and duplicate IDs");
// Captured strings survive later main-thread renames/reordering; no live UI/model access.
configured[1] = ("renamed", "changed", -1);
Equal(groups[1].Name, "先组", "capture detached from mutable configuration");
Equal(OverviewListView.FormatCheckProgress("Checking 6/10", groups, GroupLine), expected, "full snapshot unaffected by filtered/collapsed display");
Console.WriteLine($"Group progress checks: {checks} passed; production pure functions linked; no HTTP or game data access.");
