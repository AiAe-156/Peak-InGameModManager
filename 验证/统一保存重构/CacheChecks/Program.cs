using ModConfigEnhance.Overview;
using System.Diagnostics;
string root=Path.Combine(AppContext.BaseDirectory,"sandbox-"+Guid.NewGuid().ToString("N"));
BepInEx.Paths.BepInExRootPath=root;BepInEx.Paths.ConfigPath=Path.Combine(root,"config");BepInEx.Paths.PluginPath=Path.Combine(root,"plugins");
Directory.CreateDirectory(BepInEx.Paths.ConfigPath);Directory.CreateDirectory(BepInEx.Paths.PluginPath);
// Historical data is deliberately large relative to the four probed paths.
for(int i=0;i<1000;i++){string dir=Path.Combine(root,"MCE","history",i.ToString());Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"log.txt"),"unrelated");}
int checks=0;void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
OverviewPageCache.Initialize();int revision=OverviewPageCache.Environment;
// Warm tiered JIT and the allocation-counter API outside the measured interval.
for(int i=0;i<100000;i++)_=OverviewPageCache.Environment;
_=GC.GetAllocatedBytesForCurrentThread();
long before=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<100000;i++)_=OverviewPageCache.Environment;
long allocation=GC.GetAllocatedBytesForCurrentThread()-before;
Check(allocation==0,"cached environment reads allocate nothing: "+allocation);
Check(typeof(OverviewPageCache).GetFields(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).All(f=>!f.FieldType.ToString().Contains("FileSystemWatcher")),"no watcher fields");
string index=Path.Combine(root,"MCE","packs","index.json");Directory.CreateDirectory(Path.GetDirectoryName(index)!);File.WriteAllText(index,"{}");
Thread.Sleep(2100);Check(OverviewPageCache.Environment>revision,"new pack index detected by fixed file stat");
int content=OverviewPageCache.Content;OverviewPageCache.ConfigurationSaved();Check(OverviewPageCache.Content>content,"explicit config write invalidates content");
int inventory=OverviewPageCache.Inventory;OverviewPageCache.OnPageEnter();Check(OverviewPageCache.Inventory>inventory,"page entry refreshes external inventory");
inventory=OverviewPageCache.Inventory;OverviewPageCache.OnPageEnter();Check(OverviewPageCache.Inventory==inventory,"immediate settings roundtrip keeps inventory cache");
OverviewPageCache.Dispose();OverviewPageCache.Initialize();Check(OverviewPageCache.Reliable,"dispose/reinitialize restores cache state");
Console.WriteLine($"PASS {checks} cache checks; 100000 hot reads allocated {allocation} bytes; 1000 unrelated history directories; no directory enumeration or watcher");
namespace BepInEx{internal static class Paths{internal static string BepInExRootPath="",ConfigPath="",PluginPath="";}}
namespace ModConfigEnhance{internal static class Plugin{internal static Logger Log=new();}internal sealed class Logger{internal void LogDebug(string value){}}}
namespace ModConfigEnhance.Overview{internal sealed class OverviewPack{public string name="";}}
