using Mono.Cecil;
string project=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
using var module=ModuleDefinition.ReadModule(Path.Combine(project,"bin/Release/netstandard2.1/ModConfigEnhance.dll"));
int checks=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
foreach(string name in new[]{"NativeConfigurationDraft","ConfigurationNotificationGate","ConfigurationRoundBoundary","ConfigurationPresetFile","PackWorkspaceInformation"})
 Check(module.Types.Any(t=>t.Name==name),"Missing production type: "+name);
var notification=module.Types.Single(t=>t.Name=="ConfigurationNotificationGate");
Check(notification.Methods.Any(m=>m.Name=="BeforeChanged"),"notification guard exists");
var native=module.Types.Single(t=>t.Name=="NativeConfigurationDraft");
foreach(string name in new[]{"Save","Default","Refresh","Apply","SetupControl"})Check(native.Methods.Any(m=>m.Name==name),"native hook missing: "+name);
string config=File.ReadAllText(Path.Combine(project,"src/Overview/OverviewConfigService.cs"));
Check(config.Contains("ConfigurationNotificationGate.Scope"),"save transaction uses notification guard");
string source=File.ReadAllText(Path.Combine(project,"src/Overview/NativeConfigurationDraft.cs"));
Check(source.Contains("SetDraftConfig")&&!source.Contains(".BoxedValue ="),"native editing uses draft rather than live config assignment");
foreach(string removed in new[]{"UiPerfProbe","UiHoverProbe","HoverPerfWindow","OverviewTiming","OverviewFontReport"})
 Check(!module.Types.Any(t=>t.Name==removed),"removed performance monitor is absent from compiled DLL: "+removed);
Check(!module.Types.SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="System.IO.FileSystemWatcher"&&r.Name==".ctor"),"compiled plugin has no filesystem watcher construction");
Console.WriteLine($"PASS {checks} compiled production wiring checks (metadata inspection, not Unity runtime)");
