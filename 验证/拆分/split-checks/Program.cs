// 1.0.0 拆分夹具：链接真实 ManagerIdentity.cs + ManagerSplitMigration.cs，全部在 %TEMP% 下跑，不碰现场。
// 场景 A：现场副本（config\ModConfigEnhance、com.aiae.modconfigenhance.cfg、BepInEx\MCE 的 json/txt 与目录骨架）跑一次：
//   旧目录逐字节保留；新目录内容与旧目录一致（除 output\packs / exports 与超长路径）；cfg 复制；BepInEx\MCE 改名为 IGMM；
//   新位置的 JSON 里不再有旧绝对路径；二次运行什么都不做。
// 场景 B：新目录已存在 → 不复制、不覆盖。
// 场景 C：busy（助手在跑）→ 个人数据照复制，运行目录不改名。
// 场景 D：Rewrite 边界：…\MCE 后接 Pack 不改（MCEPack），…\MCE" / \\ / / 都改；JSON 双反斜杠与正斜杠写法都认。
// 场景 E：新 cfg 已存在 → 不覆盖。
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ModConfigEnhance;
using ModConfigEnhance.Updating;

int failed = 0;
void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
string Hash(string path) { using var sha = SHA256.Create(); using var s = File.OpenRead(path); return Convert.ToHexString(sha.ComputeHash(s)); }
Dictionary<string, string> Snapshot(string dir) => Directory.Exists(dir)
    ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(p => p.Substring(dir.Length + 1).Replace('\\', '/'), Hash, StringComparer.OrdinalIgnoreCase)
    : new Dictionary<string, string>();
string Home() { string h = Path.Combine(Path.GetTempPath(), "igmm-split-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(h); return h; }
void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }

string liveBep = args.Length > 0 ? args[0] : throw new ArgumentException("需要现场 BepInEx 路径");
string liveBepFull = Path.GetFullPath(liveBep).TrimEnd('\\');

// ── A：现场副本 ──
{
    string home = Home(), bep = Path.Combine(home, "BepInEx"), config = Path.Combine(bep, "config"), local = Path.Combine(home, "LocalAppData");
    long copied = 0;
    // 大文件截成 4 KB（只关心结构与路径），运行目录只带 json/txt/md/cmd/ps1，其余留空文件以保留目录骨架
    void CopyTree(string from, string to, bool runtime)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from))
        {
            string target = Path.Combine(to, Path.GetFileName(f));
            if (target.Length > 259) continue;
            string ext = Path.GetExtension(f).ToLowerInvariant();
            var info = new FileInfo(f);
            if (runtime && ext is not (".json" or ".txt" or ".md" or ".cmd" or ".ps1" or ".lock")) { File.WriteAllBytes(target, Array.Empty<byte>()); copied++; continue; }
            if (info.Length > 4 << 20) { using var s = File.OpenRead(f); var buf = new byte[4096]; int n = s.Read(buf, 0, buf.Length); File.WriteAllBytes(target, buf.Take(n).ToArray()); }
            else File.Copy(f, target);
            copied++;
        }
        foreach (string d in Directory.GetDirectories(from))
        {
            if ((new DirectoryInfo(d).Attributes & FileAttributes.ReparsePoint) != 0) continue;
            CopyTree(d, Path.Combine(to, Path.GetFileName(d)), runtime);
        }
    }
    CopyTree(Path.Combine(liveBep, "config", "ModConfigEnhance"), Path.Combine(config, "ModConfigEnhance"), false);
    File.Copy(Path.Combine(liveBep, "config", "com.aiae.modconfigenhance.cfg"), Path.Combine(config, "com.aiae.modconfigenhance.cfg"));
    CopyTree(Path.Combine(liveBep, "MCE"), Path.Combine(bep, "MCE"), true);
    Write(Path.Combine(local, "ModConfigEnhance", "signing", "author-key.json"), "{\"fixture\":true}");
    // 现场 JSON 里的绝对路径指向真实 BepInEx：改成副本位置，迁移后才能核对
    int retargeted = 0;
    foreach (string json in Directory.EnumerateFiles(bep, "*.json", SearchOption.AllDirectories))
    {
        string text = File.ReadAllText(json);
        string replaced = text.Replace(liveBepFull.Replace("\\", "\\\\"), bep.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase)
            .Replace(liveBepFull.Replace('\\', '/'), bep.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)
            .Replace(@"F:\\Games\\Steam\\steamapps\\common\\PEAK\\BepInEx", bep.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase);
        if (replaced != text) { File.WriteAllText(json, replaced); retargeted++; }
    }
    Console.WriteLine($"[A] copied {copied} files, retargeted {retargeted} json");

    string oldData = Path.Combine(config, "ModConfigEnhance"), newData = Path.Combine(config, "InGameModManager");
    var before = Snapshot(oldData);
    var oldRuntime = Snapshot(Path.Combine(bep, "MCE"));
    var report = new ManagerSplitMigration.Report();
    Check(ManagerSplitMigration.CopyConfig(config, report) && report.Config, "[A] cfg copied");
    Check(File.ReadAllBytes(Path.Combine(config, "com.aiae.ingamemodmanager.cfg")).SequenceEqual(File.ReadAllBytes(Path.Combine(config, "com.aiae.modconfigenhance.cfg"))), "[A] new cfg byte-identical to old cfg");
    var result = ManagerSplitMigration.Run(bep, config, local, busy: false);
    foreach (string s in result.Skipped.Take(8)) Console.WriteLine("  skipped: " + s.Substring(0, Math.Min(200, s.Length)));
    Console.WriteLine($"  copied {result.Copied}, rewritten {result.Rewritten}, skipped {result.Skipped.Count}");
    Check(result.Data && Directory.Exists(newData), "[A] personal data copied to config/InGameModManager");
    var afterOld = Snapshot(oldData);
    Check(before.Count == afterOld.Count && before.All(kv => afterOld.TryGetValue(kv.Key, out var h) && h == kv.Value), "[A] old config/ModConfigEnhance left byte-identical for ModConfig Enhance");
    var afterNew = Snapshot(newData);
    bool Excluded(string rel) => rel.StartsWith("output/packs/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("exports/", StringComparison.OrdinalIgnoreCase);
    var expected = before.Keys.Where(k => !Excluded(k) && Path.Combine(newData, k).Length <= 259).ToList();
    var missing = expected.Where(k => !afterNew.ContainsKey(k)).ToList();
    Check(missing.Count == 0, $"[A] every non-excluded file arrived ({expected.Count}); missing {missing.Count}" + (missing.Count > 0 ? ": " + string.Join(", ", missing.Take(3)) : ""));
    var nonJsonDiff = expected.Where(k => afterNew.ContainsKey(k) && !k.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && afterNew[k] != before[k]).ToList();
    Check(nonJsonDiff.Count == 0, "[A] non-JSON files copied byte-identical" + (nonJsonDiff.Count > 0 ? ": " + nonJsonDiff[0] : ""));
    Check(!afterNew.Keys.Any(Excluded), "[A] old pack exports (output/packs, exports) not copied");
    Check(File.Exists(Path.Combine(newData, "data", ManagerSplitMigration.MarkerName)), "[A] marker written in data/");
    string oldDataFull = Path.GetFullPath(oldData);
    var stale = Directory.EnumerateFiles(newData, "*.json", SearchOption.AllDirectories)
        .Where(f => { string t = File.ReadAllText(f); return t.IndexOf(oldDataFull.Replace("\\", "\\\\") + "\\\\", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf(oldDataFull.Replace("\\", "\\\\") + "\"", StringComparison.OrdinalIgnoreCase) >= 0; }).ToList();
    Check(stale.Count == 0, "[A] no JSON under the new folder still points at config/ModConfigEnhance" + (stale.Count > 0 ? ": " + stale[0] : ""));

    Check(result.Runtime && !Directory.Exists(Path.Combine(bep, "MCE")) && Directory.Exists(Path.Combine(bep, "IGMM")), "[A] BepInEx/MCE renamed to BepInEx/IGMM");
    var newRuntime = Snapshot(Path.Combine(bep, "IGMM"));
    var lost = oldRuntime.Keys.Where(k => !newRuntime.ContainsKey(k) && !(k.EndsWith("MCE-Recovery.cmd") || k.EndsWith("MCE-Recovery.ps1"))).ToList();
    Check(lost.Count == 0, $"[A] every runtime file kept after rename ({oldRuntime.Count})" + (lost.Count > 0 ? ": " + lost[0] : ""));
    string oldRuntimeFull = Path.GetFullPath(Path.Combine(bep, "MCE"));
    var staleRuntime = Directory.EnumerateFiles(Path.Combine(bep, "IGMM"), "*.json", SearchOption.AllDirectories)
        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "backups" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "tools" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "layout-backups" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "downloads" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "."))
        .Where(f => { string t = File.ReadAllText(f); return t.IndexOf(oldRuntimeFull.Replace("\\", "\\\\") + "\\\\", StringComparison.OrdinalIgnoreCase) >= 0; }).ToList();
    Check(staleRuntime.Count == 0, "[A] queue/history JSON no longer point at BepInEx/MCE" + (staleRuntime.Count > 0 ? ": " + staleRuntime[0] : ""));
    string queue = Path.Combine(bep, "IGMM", "queue.json");
    if (File.Exists(queue)) Check(File.ReadAllText(queue).Contains(Path.Combine(bep, "IGMM").Replace("\\", "\\\\")), "[A] queue.json WorkDirectory now under BepInEx/IGMM");
    Check(!File.Exists(Path.Combine(bep, "IGMM", "MCE-Recovery.cmd")), "[A] stale MCE-Recovery entry set aside");
    Check(result.Signing && File.Exists(Path.Combine(local, "InGameModManager", "signing", "author-key.json")) && File.Exists(Path.Combine(local, "ModConfigEnhance", "signing", "author-key.json")), "[A] signing identity copied, original kept");
    string index = Path.Combine(bep, "IGMM", "packs", "index.json");
    if (File.Exists(index)) Check(File.ReadAllText(index).Contains("MCEPack_"), "[A] pack index keeps MCEPack folder names (\\MCE boundary)");

    var again = ManagerSplitMigration.Run(bep, config, local, busy: false);
    var report2 = new ManagerSplitMigration.Report();
    Check(!ManagerSplitMigration.CopyConfig(config, report2) && !again.Data && !again.Runtime && !again.Signing && again.Rewritten == 0, "[A] second run does nothing");
    try { Directory.Delete(home, true); } catch { }
}

// ── B：新目录已存在 ──
{
    string home = Home(), bep = Path.Combine(home, "BepInEx"), config = Path.Combine(bep, "config");
    Write(Path.Combine(config, "ModConfigEnhance", "language", "zh-cn.txt"), "OLD=旧");
    Write(Path.Combine(config, "InGameModManager", "language", "zh-cn.txt"), "NEW=新");
    var r = ManagerSplitMigration.Run(bep, config, "", busy: false);
    Check(!r.Data && File.ReadAllText(Path.Combine(config, "InGameModManager", "language", "zh-cn.txt")) == "NEW=新", "[B] existing new folder is never overwritten");
    try { Directory.Delete(home, true); } catch { }
}

// ── C：busy ──
{
    string home = Home(), bep = Path.Combine(home, "BepInEx"), config = Path.Combine(bep, "config");
    Write(Path.Combine(config, "ModConfigEnhance", "language", "zh-cn.txt"), "A=甲");
    Write(Path.Combine(bep, "MCE", "queue.json"), "{}");
    var r = ManagerSplitMigration.Run(bep, config, "", busy: true);
    Check(r.Data && !r.Runtime && Directory.Exists(Path.Combine(bep, "MCE")) && !Directory.Exists(Path.Combine(bep, "IGMM")) && r.Note.Length > 0, "[C] worker busy: data copied, runtime folder left for next launch");
    var r2 = ManagerSplitMigration.Run(bep, config, "", busy: false);
    Check(r2.Runtime && Directory.Exists(Path.Combine(bep, "IGMM")), "[C] next launch renames the runtime folder");
    try { Directory.Delete(home, true); } catch { }
}

// ── D：Rewrite 边界 ──
{
    string home = Home(), file = Path.Combine(home, "t.json");
    string oldRoot = @"C:\X\BepInEx\MCE", newRoot = @"C:\X\BepInEx\IGMM";
    File.WriteAllText(file, "{\"a\":\"C:\\\\X\\\\BepInEx\\\\MCE\\\\jobs\\\\1\",\"b\":\"C:\\\\X\\\\BepInEx\\\\MCEPack\\\\p\",\"c\":\"C:/X/BepInEx/MCE/shop\",\"d\":\"C:\\\\X\\\\BepInEx\\\\MCE\"}");
    int n = ManagerSplitMigration.Rewrite(file, oldRoot, newRoot);
    string t = File.ReadAllText(file);
    Check(n == 3 && t.Contains("BepInEx\\\\IGMM\\\\jobs") && t.Contains("BepInEx\\\\MCEPack\\\\p") && t.Contains("C:/X/BepInEx/IGMM/shop") && t.Contains("\"d\":\"C:\\\\X\\\\BepInEx\\\\IGMM\""), "[D] rewrites \\\\, / and end-of-string forms; leaves MCEPack alone");
    try { Directory.Delete(home, true); } catch { }
}

// ── E：新 cfg 已在 ──
{
    string home = Home(), config = Path.Combine(home, "config");
    Write(Path.Combine(config, "com.aiae.modconfigenhance.cfg"), "old");
    Write(Path.Combine(config, "com.aiae.ingamemodmanager.cfg"), "mine");
    var r = new ManagerSplitMigration.Report();
    Check(!ManagerSplitMigration.CopyConfig(config, r) && File.ReadAllText(Path.Combine(config, "com.aiae.ingamemodmanager.cfg")) == "mine", "[E] existing new cfg is never overwritten");
    try { Directory.Delete(home, true); } catch { }
}

Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
return failed == 0 ? 0 : 1;
