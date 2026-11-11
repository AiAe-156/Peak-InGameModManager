// Read-only dry run of the folder-organization planner against a real BepInEx root: builds the same request the game
// sends (groups and GUID→group from the applied profile snapshot, GroupOnly mode) and prints the plan. Writes nothing.
using System.Text;
using System.Text.Json;
using MCE.UpdateWorker;
using ModConfigEnhance.Updating;

Console.OutputEncoding = new UTF8Encoding(false);
string root = args.Length > 0 ? args[0] : @"F:\Games\Steam\steamapps\common\PEAK\BepInEx";
string snapshot = args.Length > 2 ? args[2] : Path.Combine(root, "config", "ModConfigEnhance", "runtime", "applied-profile.json");
using var document = JsonDocument.Parse(File.ReadAllText(snapshot));
var state = document.RootElement.GetProperty("state");
var request = new LayoutQueueRequest { Mode = InstallationLayoutMode.GroupOnly, AppliedSnapshotPath = snapshot };
foreach (var group in state.GetProperty("groups").EnumerateArray())
    request.Groups.Add(new LayoutQueueGroup { Id = group.GetProperty("id").GetString() ?? "", Name = group.GetProperty("name").GetString() ?? "" });
foreach (var rule in state.GetProperty("mods").EnumerateArray())
{
    string id = rule.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
    string groupId = rule.TryGetProperty("groupId", out var g) ? g.GetString() ?? "" : "";
    if (id.Length > 0) request.Mods.Add(new LayoutQueueMod { Guid = id, GroupId = groupId.Length > 0 ? groupId : "ungrouped" });
}
var storage = new PackStorage(root);
string active = storage.Read().ActivePackId;
string ledgerPath = active.Length > 0 ? Path.Combine(storage.Workspace(active), "shop-installed.json") : Path.Combine(root, "MCE", "shop", "installed-unmanaged.json");
var ledger = File.Exists(ledgerPath) ? Files.Read<ShopLedger>(ledgerPath) : new ShopLedger();
Console.WriteLine($"root={root} groups={request.Groups.Count} mods={request.Mods.Count} ledger={ledgerPath} packages={ledger.Packages.Count}");
var plan = LayoutPlanner.Build(root, request, ledger);
Console.WriteLine($"moves={plan.Moves.Count} blockers={plan.Blockers.Count}");
foreach (string blocker in plan.Blockers) Console.WriteLine("BLOCKER " + blocker);
foreach (var move in plan.Moves) Console.WriteLine("MOVE " + move.From + " -> " + move.To);
foreach (string detail in plan.Details) Console.WriteLine("  " + detail);
foreach (var node in plan.Tree) Console.WriteLine("TREE " + node.Disposition + (node.Ungrouped ? " ungrouped" : "") + (node.Manager ? " manager" : "") + " | " + node.Group + " | " + node.Folder + " <- " + node.OriginalPath + (node.Reasons.Length > 0 ? " | reasons: " + node.Reasons : "") + (node.Notes.Count > 0 ? " | notes: " + string.Join(",", node.Notes) : ""));
if (args.Length > 1) File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(plan.Tree, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
