using ModConfigEnhance;
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;
int count = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine($"PASS {++count} {label}"); }
string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../"));
string row = File.ReadAllText(Path.Combine(root, "src/Overview/OverviewListView.Infrastructure.cs"));
string menu = File.ReadAllText(Path.Combine(root, "src/Overview/SourceUi.cs"));
Check(row.Contains("var diagnostic = SourceUi.DependencyDiagnostic(package.Key);") && row.Contains("string protectionHint = diagnostic.Summary;") && menu.Contains("DependencyDiagnostic(key).BlockReason"), "lock tooltip and menu share authoritative diagnostic summary");
Check(row.Contains("bool lockedState = !diagnostic.CanUninstall;") && !row.Contains("capability.Unknown") && !row.Contains("ProtectionHint(capability)"), "lock is permission-only, no second reason policy");
foreach (int language in new[] { 0, 1, 2 }) {
 Loc.Language = language;
 var protectedUnknown = DiagnosticSummary.FromCapability(new() { Unknown = true, CanUninstall = false, ProtectionPath = new[] { "MCE", "Core" }, Reason = "F:/very-long-path/manifest.json" });
 Check(protectedUnknown.State == DiagnosticSummary.Status.RequiredByMce && protectedUnknown.BlockReason == protectedUnknown.Summary && !protectedUnknown.CanUninstall && protectedUnknown.CanUpdate && !protectedUnknown.Summary.Contains("F:"), "MCE path wins over unknown; lock/menu short reason language " + language);
 var localUnknown = DiagnosticSummary.FromCapability(new() { Unknown = true, CanUninstall = true });
 bool lockedState = !localUnknown.CanUninstall;
 Check(!lockedState && localUnknown.BlockReason == "" && localUnknown.CanUninstall, "local unknown permits safety preview without forbidden icon language " + language);
}
var missing = DiagnosticSummary.FromCapability(null);
Check(!missing.CanUninstall && missing.BlockReason == missing.Summary, "unread locks consistently");
string components = File.ReadAllText(Path.Combine(root, "src/Overview/OverviewDependencyHints.Components.cs"));
Check(components.Contains("Loc.T(\" [cycle]\", \" [循环]\", \" [循環]\")") && components.Contains("Loc.T(\" [shared]\", \" [共享]\", \" [共用]\")") && components.Contains("Loc.T(\" [truncated]\", \" [已截断]\", \" [已截斷]\")"), "cycle shared truncation localized");
string hints = File.ReadAllText(Path.Combine(root, "src/Overview/OverviewDependencyHints.cs"));
Check(hints.Contains("_positionAnchor = screen;") && hints.Contains("Position(_fixed ? _positionAnchor :"), "fixed resize reuses original screen anchor");
Check(row.Contains("const float height = RowHeight;") && row.Contains("BindDependencyCard(name.gameObject, package)") && !row.Contains("AddHint(name.gameObject"), "keep shared card and fixed row, no name tooltip");
Console.WriteLine($"{count} checks passed; source wiring and pure diagnostic behavior, NOT Unity visual acceptance");
namespace ModConfigEnhance { internal static class Loc { internal static int Language; internal static string T(string en, string zh, string tw) => Language == 1 ? zh : Language == 2 ? tw : en; } }
