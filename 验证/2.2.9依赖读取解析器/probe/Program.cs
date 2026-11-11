using System.Text.Json.Nodes;
using ModConfigEnhance.Updating;
string root = args[0];
var claims = args.Skip(2).Select(p => new ComponentDependencyCollector.ConfirmedFile { PackageKey = args[1], Path = p }).ToArray();
var g = ComponentDependencyCollector.Collect(root, file => {
    var d = JsonNode.Parse(File.ReadAllText(file))!;
    string name = d["name"]?.GetValue<string>() ?? "", team = d["namespace"]?.GetValue<string>() ?? d["author"]?.GetValue<string>() ?? "";
    var hint = claims.Select(c => c.PackageKey).FirstOrDefault(k => file.Contains(k, StringComparison.OrdinalIgnoreCase));
    if (team.Length == 0 && hint != null) team = hint.Split('-')[0];
    if (team.Length == 0) throw new IOException("no team " + file);
    return new ComponentDependencyCollector.Manifest { Key = team + "-" + name, Dependencies = (d["dependencies"]?.AsArray() ?? new JsonArray()).Select(x => x!.GetValue<string>()).ToArray() };
}, claims);
Console.WriteLine($"components={g.Components.Count} failures={g.Failures.Count} scopes={g.FailureScopes.Count} globalScopes={g.FailureScopes.Count(f=>f.Node.Length==0)}");
Console.WriteLine("MCE seeds: " + string.Join(", ", g.Components.Where(c => c.PluginGuids.Contains(ComponentDependencyGraph.MceGuid, StringComparer.OrdinalIgnoreCase)).Select(c => c.Id)));
var t = g.Components.Where(c => c.Id.Contains(args[1].Split('-')[1], StringComparison.OrdinalIgnoreCase) || c.PackageKey.Equals(args[1], StringComparison.OrdinalIgnoreCase)).ToList();
foreach (var c in t) Console.WriteLine($"comp {c.Id} pk={c.PackageKey} locs={string.Join(" | ", c.Locations.Where(l=>l.EndsWith(".dll")))} guids={string.Join(",", c.PluginGuids)}");
var cap = g.GetCapability(args[1]);
Console.WriteLine($"CAP CanUninstall={cap.CanUninstall} Unknown={cap.Unknown} Verif={cap.VerificationRequired} Prot={cap.ProtectionPath.Count}\n  Reason={cap.Reason}");
foreach (var f in g.FailureScopes.Where(f => f.Node.Length == 0 || t.Any(c => c.Id == f.Node)).Take(10)) Console.WriteLine($"  scope node='{f.Node}' {f.Reason}");
Console.WriteLine("== failure kinds ==");
foreach (var grp in g.FailureScopes.GroupBy(f => f.Reason.Contains("Failed to resolve assembly") ? "resolve:" + System.Text.RegularExpressions.Regex.Match(f.Reason, "assembly: '([^,]+)").Groups[1].Value : f.Reason.Split(':')[0]).OrderByDescending(x=>x.Count())) Console.WriteLine($"  {grp.Count()}  {grp.Key}");
var pkgs = g.Components.Where(c => c.PackageKey.Length > 0 && c.Id == c.PackageKey).ToList();
int blocked = 0; foreach (var c in pkgs) { var k = g.GetCapability(c.Id); if (!k.CanUninstall && k.ProtectionPath.Count == 0) blocked++; }
Console.WriteLine($"packages={pkgs.Count} blockedIncomplete={blocked}");
foreach (var c in pkgs) { var k = g.GetCapability(c.Id); if (!k.CanUninstall && k.ProtectionPath.Count == 0) Console.WriteLine("  BLOCKED " + c.Id + " :: " + (k.Reason.Length > 160 ? k.Reason[..160] : k.Reason)); }
