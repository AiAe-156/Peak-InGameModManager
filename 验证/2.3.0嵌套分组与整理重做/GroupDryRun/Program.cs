// Read-only dry run of the 2.3.0 group-folder organizer against a real BepInEx root. Builds the request the game sends
// (groups with parents, one row per overview row approximated from manifests and plugin GUIDs → groupId in the applied
// profile), runs GroupLayoutPlanner.Build and prints the plan and tree. Writes nothing except the report file.
using System.Text;
using System.Text.Json;
using MCE.UpdateWorker;
using ModConfigEnhance.Updating;

Console.OutputEncoding = new UTF8Encoding(false);
string root = args[0];
string snapshot = args[1];
string report = args.Length > 2 ? args[2] : "";
using var document = JsonDocument.Parse(File.ReadAllText(snapshot));
var state = document.RootElement.GetProperty("state");
var request = new LayoutQueueRequest { UngroupedFolder = "未分组" };
foreach (var group in state.GetProperty("groups").EnumerateArray())
    request.Groups.Add(new LayoutQueueGroup { Id = group.GetProperty("id").GetString() ?? "", Name = group.GetProperty("name").GetString() ?? "",
        ParentId = group.TryGetProperty("parentId", out var p) ? p.GetString() ?? "" : "" });
var groupOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
foreach (var rule in state.GetProperty("mods").EnumerateArray())
    if (rule.TryGetProperty("id", out var id) && rule.TryGetProperty("groupId", out var g)) groupOf[id.GetString() ?? ""] = g.GetString() ?? "";
// Rows: one per plugin assembly, with its package folder = nearest manifest folder (what the overview uses).
string plugins = Path.Combine(root, "plugins");
foreach (string dll in Directory.EnumerateFiles(plugins, "*", SearchOption.AllDirectories).Where(PluginFileNames.IsAssembly))
{
    if (GroupFolderPlanner.NeverTouch(Path.GetRelativePath(root, dll).Replace('\\', '/'))) continue;
    var infos = InstallMetadata.Read(root, dll);
    if (infos.Count == 0) continue;
    string? dir = Path.GetDirectoryName(dll); string package = dir!;
    for (string? probe = dir; probe != null && probe.Length > plugins.Length; probe = Path.GetDirectoryName(probe))
        if (File.Exists(Path.Combine(probe, "manifest.json"))) { package = probe; break; }
    foreach (var info in infos)
        request.Rows.Add(new LayoutQueueRow { Guids = { info.Guid }, GroupId = groupOf.TryGetValue(info.Guid, out var gid) ? gid : "", PackageRoot = package,
            Version = info.Version, Loaded = dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) });
}
var storage = new PackStorage(root);
string active = storage.Read().ActivePackId;
string ledgerPath = active.Length > 0 ? Path.Combine(storage.Workspace(active), "shop-installed.json") : Path.Combine(root, "MCE", "shop", "installed-unmanaged.json");
var ledger = File.Exists(ledgerPath) ? Files.Read<ShopLedger>(ledgerPath) : new ShopLedger();
var plan = GroupLayoutPlanner.Build(root, request, ledger);
var output = new StringBuilder();
output.AppendLine($"groups={request.Groups.Count} rows={request.Rows.Count} moves={plan.Moves.Count} blockers={plan.Blockers.Count}");
foreach (var move in plan.Moves) output.AppendLine("MOVE " + move.From + " -> " + move.To);
foreach (var node in plan.Tree.OrderBy(n => n.Kind).ThenBy(n => n.FinalPath, StringComparer.OrdinalIgnoreCase))
    output.AppendLine("TREE " + node.Kind + " " + node.Disposition + (node.Ungrouped ? " ungrouped" : "") + (node.Manager ? " manager" : "") + " | " + node.OriginalPath + " => " + node.FinalPath
        + " | mods=" + node.ModCount + (node.Reasons.Length > 0 ? " | reasons: " + node.Reasons : "") + (node.Notes.Count > 0 ? " | notes: " + string.Join(",", node.Notes) : ""));
Console.Write(output);
if (report.Length > 0)
{
    File.WriteAllText(report, output.ToString(), new UTF8Encoding(false));
    File.WriteAllText(Path.ChangeExtension(report, ".json"), JsonSerializer.Serialize(plan.Tree, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
}
