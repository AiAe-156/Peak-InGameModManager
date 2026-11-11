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
    Write(Path.Combine(config, "ModConfigEnhance", "profiles", "p.json"), "{}");
    var r = ManagerSplitMigration.Run(bep, config, "", busy: false);
    // 复查 #2：新目录已在（别的代码先建了它）也要补齐；已有的文件绝不覆盖
    Check(r.Data && File.ReadAllText(Path.Combine(config, "InGameModManager", "language", "zh-cn.txt")) == "NEW=新" && File.Exists(Path.Combine(config, "InGameModManager", "profiles", "p.json")), "[B] existing new folder: missing files filled in, existing files never overwritten");
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

// ── F：复查 #1 运行目录推迟后，同次启动的代码不会建出空的新目录；下次照样改名 ──
{
    string home = Home(), bep = Path.Combine(home, "BepInEx"), config = Path.Combine(bep, "config");
    Write(Path.Combine(bep, "MCE", "queue.json"), "{\"q\":1}");
    ManagerSplitMigration.Run(bep, config, "", busy: true);
    Check(ManagerIdentity.Runtime(bep).EndsWith("MCE") && ManagerIdentity.Runtime(bep, "worker.lock").StartsWith(Path.Combine(bep, "MCE")), "[F] while the rename is deferred Runtime() falls back to BepInEx/MCE");
    Directory.CreateDirectory(ManagerIdentity.Runtime(bep));   // 模拟写锁、队列等在 Runtime() 下建目录
    Check(!Directory.Exists(Path.Combine(bep, "IGMM")), "[F] no empty BepInEx/IGMM is created before the rename");
    var r = ManagerSplitMigration.Run(bep, config, "", busy: false);
    Check(r.Runtime && File.ReadAllText(Path.Combine(bep, "IGMM", "queue.json")) == "{\"q\":1}" && !Directory.Exists(Path.Combine(bep, "MCE")), "[F] next launch renames and keeps the data");
    try { Directory.Delete(home, true); } catch { }
}

// ── G：两边都在（旧版又写出 BepInEx\MCE）→ 逐个并入，新目录已有的不覆盖 ──
{
    string home = Home(), bep = Path.Combine(home, "BepInEx"), config = Path.Combine(bep, "config");
    Write(Path.Combine(bep, "IGMM", "queue.json"), "new");
    Write(Path.Combine(bep, "MCE", "queue.json"), "old");
    Write(Path.Combine(bep, "MCE", "packs", "index.json"), "{}");
    ManagerSplitMigration.Run(bep, config, "", busy: false);
    Check(File.ReadAllText(Path.Combine(bep, "IGMM", "queue.json")) == "new" && File.Exists(Path.Combine(bep, "IGMM", "packs", "index.json")) && File.ReadAllText(Path.Combine(bep, "MCE", "queue.json")) == "old", "[G] both folders: missing items merged, existing new files kept, conflicting old copy left in place");
    try { Directory.Delete(home, true); } catch { }
}

// ── H：复查 #2 单个文件复制失败 → 不写完成标记，其余照复制；下次补上 ──
{
    string home = Home(), bep = Path.Combine(home, "BepInEx"), config = Path.Combine(bep, "config");
    Write(Path.Combine(config, "ModConfigEnhance", "language", "zh-cn.txt"), "A=甲");
    string locked = Path.Combine(config, "ModConfigEnhance", "profiles", "locked.json");
    Write(locked, "{}");
    using (var hold = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var r = ManagerSplitMigration.Run(bep, config, "", busy: false);
        Check(!r.Data && File.Exists(Path.Combine(config, "InGameModManager", "language", "zh-cn.txt")) && !File.Exists(Path.Combine(config, "InGameModManager", "data", ManagerSplitMigration.MarkerName)), "[H] a locked file: others copied, no completion marker");
    }
    var r2 = ManagerSplitMigration.Run(bep, config, "", busy: false);
    Check(r2.Data && File.Exists(Path.Combine(config, "InGameModManager", "profiles", "locked.json")) && File.Exists(Path.Combine(config, "InGameModManager", "data", ManagerSplitMigration.MarkerName)), "[H] next launch copies the missing file and marks done");
    try { Directory.Delete(home, true); } catch { }
}

// ── I：复查 #4 / #8 / #9：| 结尾、\uXXXX 转义、交叉引用（个人数据里的运行目录路径）、改写中断后重跑 ──
{
    string top = Home(), home = Path.Combine(top, "中文 & 目录"), bep = Path.Combine(home, "BepInEx"), config = Path.Combine(bep, "config");
    string oldData = Path.Combine(config, "ModConfigEnhance"), oldRuntime = Path.Combine(bep, "MCE");
    string newData = Path.Combine(config, "InGameModManager"), newRuntime = Path.Combine(bep, "IGMM");
    string Esc(string s) => s.Replace("\\", "\\\\");
    string U(string s) { var sb = new System.Text.StringBuilder(); foreach (char c in s) sb.Append(c > 127 || c == '&' ? "\\u" + ((int)c).ToString("X4") : c.ToString()); return sb.ToString(); }
    Write(Path.Combine(oldData, "data", "runtime", "sources.json"), "{\"" + Esc(oldData) + "|" + Esc(oldRuntime) + "\\\\packs\\\\x|\":1}");
    Write(Path.Combine(oldRuntime, "queue.json"), "{\"w\":\"" + U(Esc(oldRuntime)) + "\\\\jobs\\\\1\"}");
    ManagerSplitMigration.Run(bep, config, "", busy: false);
    string src = File.ReadAllText(Path.Combine(newData, "data", "runtime", "sources.json"));
    string q = File.ReadAllText(Path.Combine(newRuntime, "queue.json"));
    Check(!src.Contains(Esc(oldData) + "|") && src.Contains(Esc(newData) + "|"), "[I] composite key ending in | rewritten");
    Check(!src.Contains(Esc(oldRuntime) + "\\\\packs") && src.Contains(Esc(newRuntime) + "\\\\packs"), "[I] runtime path stored in personal data rewritten too");
    Check(!q.Contains(U(Esc(oldRuntime))) && q.Contains(U(Esc(newRuntime))), "[I] \\uXXXX-escaped root rewritten");
    Check(!File.Exists(Path.Combine(newRuntime, ManagerSplitMigration.PendingRewriteName)) && !File.Exists(Path.Combine(newData, ManagerSplitMigration.PendingRewriteName)), "[I] rewrite to-do removed when complete");
    // 模拟上次改写中途被杀：待办还在、有一处没改
    File.WriteAllText(Path.Combine(newRuntime, ManagerSplitMigration.PendingRewriteName), Path.GetFullPath(oldRuntime) + "|" + Path.GetFullPath(newRuntime));
    File.WriteAllText(Path.Combine(newRuntime, "late.json"), "{\"w\":\"" + Esc(oldRuntime) + "\\\\shop\"}");
    ManagerSplitMigration.Run(bep, config, "", busy: false);
    Check(File.ReadAllText(Path.Combine(newRuntime, "late.json")).Contains(Esc(newRuntime) + "\\\\shop"), "[I] interrupted rewrite resumes on next launch");
    try { Directory.Delete(top, true); } catch { }
}

// ── J：复查 #7 .transactions 下的切包计划会改写，.view-cache 不动 ──
{
    string home = Home(), bep = Path.Combine(home, "BepInEx"), config = Path.Combine(bep, "config");
    string oldRuntime = Path.Combine(bep, "MCE");
    Write(Path.Combine(oldRuntime, "packs", ".transactions", "t1", "switch.json"), "{\"b\":\"" + oldRuntime.Replace("\\", "\\\\") + "\\\\packs\"}");
    Write(Path.Combine(oldRuntime, "packs", ".view-cache", "v.json"), "{\"b\":\"" + oldRuntime.Replace("\\", "\\\\") + "\"}");
    ManagerSplitMigration.Run(bep, config, "", busy: false);
    Check(File.ReadAllText(Path.Combine(bep, "IGMM", "packs", ".transactions", "t1", "switch.json")).Contains("IGMM"), "[J] pending pack switch plans under .transactions rewritten");
    Check(File.ReadAllText(Path.Combine(bep, "IGMM", "packs", ".view-cache", "v.json")).Contains("\\\\MCE\""), "[J] .view-cache left untouched");
    try { Directory.Delete(home, true); } catch { }
}

// ── K：复查 #5 junction 两种写法（本机 F:\Games\Steam → SteamLibrary 才测得到）──
{
    var aliases = ManagerSplitMigration.PhysicalAliases(liveBep);
    Console.WriteLine("  aliases of live BepInEx: " + string.Join(" | ", aliases));
    Check(aliases.Count >= 2 && aliases.Any(a => a.Contains("SteamLibrary")) && aliases.Any(a => a.Contains("\\Steam\\")), "[K] both Steam and SteamLibrary spellings recognised for the live install");
}

Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
return failed == 0 ? 0 : 1;
