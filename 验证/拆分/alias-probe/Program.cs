// 只读核对复查 #5：把现场 BepInEx\MCE 的 history.json 与 transaction.json 复制到临时目录，
// 用真实 PhysicalAliases(现场 BepInEx) 生成的改写对跑 Rewrite，统计改写后还残留多少旧运行目录写法。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModConfigEnhance;

string live = args[0];
string oldRuntime = Path.Combine(live, "MCE"), newRuntime = Path.Combine(live, "IGMM");
var pairs = ManagerSplitMigration.PhysicalAliases(oldRuntime).Select(a => (a, Path.GetFullPath(newRuntime))).ToList();
Console.WriteLine("pairs: " + string.Join(" ; ", pairs.Select(p => p.a)));
string tmp = Path.Combine(Path.GetTempPath(), "igmm-alias-" + Guid.NewGuid().ToString("N").Substring(0, 6));
Directory.CreateDirectory(tmp);
var files = new List<string> { Path.Combine(oldRuntime, "history.json") };
files.AddRange(Directory.EnumerateFiles(Path.Combine(oldRuntime, "jobs"), "transaction.json", SearchOption.AllDirectories));
int total = 0, residualSteam = 0, residualLibrary = 0;
foreach (string f in files.Where(File.Exists))
{
    string copy = Path.Combine(tmp, Guid.NewGuid().ToString("N") + ".json");
    File.Copy(f, copy);
    total += ManagerSplitMigration.Rewrite(copy, pairs, null);
    string t = File.ReadAllText(copy);
    if (t.IndexOf(@"Games\\Steam\\steamapps\\common\\PEAK\\BepInEx\\MCE\\", StringComparison.OrdinalIgnoreCase) >= 0) residualSteam++;
    if (t.IndexOf(@"Games\\SteamLibrary\\steamapps\\common\\PEAK\\BepInEx\\MCE\\", StringComparison.OrdinalIgnoreCase) >= 0) residualLibrary++;
}
Console.WriteLine($"files {files.Count}, rewritten {total}, residual Steam-form {residualSteam}, residual SteamLibrary-form {residualLibrary}");
Directory.Delete(tmp, true);
return residualSteam + residualLibrary == 0 ? 0 : 1;
