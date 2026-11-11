// Uninstall protection must follow the fixed list of what MCE actually runs on, not a graph walk: the walk
// used to give MCE a hard edge to every assembly it could not resolve, so an unrelated mod became an "MCE
// requirement" and could not be uninstalled.
using ModConfigEnhance.Updating;

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
    if (!ok) failures++;
}

var graph = new ComponentDependencyGraph();
static ComponentDependencyGraph.Component Add(ComponentDependencyGraph graph, string id, string? guid = null, string? assembly = null, string? location = null)
{
    var component = new ComponentDependencyGraph.Component { Id = id, PackageKey = id };
    if (guid != null) component.PluginGuids.Add(guid);
    if (assembly != null) component.Assemblies.Add(assembly);
    if (location != null) component.Locations.Add(location);
    graph.Components.Add(component);
    return component;
}

var mce = Add(graph, "AiAeT-ModConfigEnhance", ComponentDependencyGraph.MceGuid, "ModConfigEnhance", "plugins/0核心&基础模组/MCE/ModConfigEnhance.dll");
var unrelated = Add(graph, "CptPICHU-TrueFinalAscent", "CptPICHU.TrueFinalAscent", "TrueFinalAscent", "plugins/CptPICHU-TrueFinalAscent/TrueFinalAscent.dll");
var host = Add(graph, "PEAKModding-PEAKLib_ModConfig", "com.github.PEAKModding.PEAKLib.ModConfig", "com.github.PEAKModding.PEAKLib.ModConfig", "plugins/0核心&基础模组/ModConfig/com.github.PEAKModding.PEAKLib.ModConfig.dll");
var core = Add(graph, "PEAKModding-PEAKLib_Core", "com.github.PEAKModding.PEAKLib.Core", "com.github.PEAKModding.PEAKLib.Core", "plugins/0核心&基础模组/PEAKLib_Core/com.github.PEAKModding.PEAKLib.Core.dll");

// The edge the old collector fabricated for an unresolved reference. Protection must ignore it now.
graph.Edges.Add(new ComponentDependencyGraph.Edge
{ From = mce.Id, To = unrelated.Id, Evidence = "possible provider for unresolved PEAKLib.Core at plugins/CptPICHU-TrueFinalAscent/TrueFinalAscent.dll", Hard = true });

var unrelatedCapability = graph.GetCapability("CptPICHU-TrueFinalAscent");
Check(unrelatedCapability.ProtectionPath.Count == 0, "unrelated mod is not claimed as an MCE requirement despite the fabricated edge");
Check(unrelatedCapability.CanUninstall, "unrelated mod keeps CanUninstall");
Check(!unrelatedCapability.Unknown, "unrelated mod is not forced into verification by an unrelated edge");

foreach (string identity in new[] { "com.github.PEAKModding.PEAKLib.ModConfig", "com.github.PEAKModding.PEAKLib.Core" })
{
    var capability = graph.GetCapability(identity == "com.github.PEAKModding.PEAKLib.ModConfig" ? host.Id : core.Id);
    Check(capability.ProtectionPath.SequenceEqual(new[] { identity }), "the real MCE requirement stays pinned: " + identity);
}

Check(mce.Id.Length > 0 && graph.GetCapability(mce.Id).ProtectionPath.Count == 0, "MCE itself is not in its own dependency list (it is protected by the shop's own rule)");
Check(ComponentDependencyGraph.MceRequiredIdentities.Length == 3, "the list is short and fixed: 3 identities");
// 2.3.9: one whitelist for enable/disable and uninstall — MCE + the three libraries, exact GUID match only.
foreach (string guid in new[] { "com.aiae.modconfigenhance", "com.github.PEAKModding.PEAKLib.ModConfig", "com.github.PEAKModding.PEAKLib.Core", "com.github.PEAKModding.PEAKLib.UI" })
    Check(ComponentDependencyGraph.IsProtected(guid), "whitelisted: " + guid);
foreach (string guid in new[] { "com.github.PEAKModding.PEAKLib.Items", "CptPICHU.TrueFinalAscent", "my.peaklib.core.fork", "" })
    Check(!ComponentDependencyGraph.IsProtected(guid), "not whitelisted: " + guid);

Console.WriteLine(failures == 0 ? "PASS 保护清单" : $"FAIL {failures}");
return failures == 0 ? 0 : 1;
