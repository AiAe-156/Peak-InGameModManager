using Mono.Cecil;
string project=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
using var module=ModuleDefinition.ReadModule(Path.Combine(project,"bin/Release/netstandard2.1/InGameModManager.dll"));
int checks=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
foreach(string name in new[]{"NativeConfigurationDraft","ConfigurationNotificationGate","ConfigurationRoundBoundary","ConfigurationPresetFile","PackWorkspaceInformation"})
 Check(module.Types.Any(t=>t.Name==name),"Missing production type: "+name);
var notification=module.Types.Single(t=>t.Name=="ConfigurationNotificationGate");
Check(notification.Methods.Any(m=>m.Name=="BeforeChanged"),"notification guard exists");
var native=module.Types.Single(t=>t.Name=="NativeConfigurationDraft");
// 1.1.4 (user decision 2026-11-13): the settings page has no draft. Native controls write the live config themselves;
// IGMM only follows the written value (postfix), it never intercepts Save / ApplyValue / SetDefaultValue.
Check(native.Methods.Any(m=>m.Name=="Follow"),"native follow hook present");
foreach(string name in new[]{"Save","Default","Refresh","Apply","SetupControl"})Check(!native.Methods.Any(m=>m.Name==name),"old draft interception removed: "+name);
string config=File.ReadAllText(Path.Combine(project,"src/Overview/OverviewConfigService.cs"));
Check(config.Contains("ConfigurationNotificationGate.Scope"),"save transaction uses notification guard");
string source=File.ReadAllText(Path.Combine(project,"src/Overview/NativeConfigurationDraft.cs"));
Check(!source.Contains("SetDraftConfig")&&source.Contains("postfix:"),"native editing writes live config directly; IGMM only follows (no draft)");
foreach(string removed in new[]{"UiPerfProbe","UiHoverProbe","HoverPerfWindow","OverviewTiming","OverviewFontReport"})
 Check(!module.Types.Any(t=>t.Name==removed),"removed performance monitor is absent from compiled DLL: "+removed);
Check(!module.Types.SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="System.IO.FileSystemWatcher"&&r.Name==".ctor"),"compiled plugin has no filesystem watcher construction");
Console.WriteLine($"PASS {checks} compiled production wiring checks (metadata inspection, not Unity runtime)");
