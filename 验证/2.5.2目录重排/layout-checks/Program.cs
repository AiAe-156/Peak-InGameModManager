// 2.5.4 目录重排夹具：链接真实 MceDirectories.cs + MceDirectoryMigration.cs。
// 场景 A：复制现场 config\ModConfigEnhance 跑搬家（字节一致、无旧路径、二次运行不变、备份在 MCE\layout-backups）。
// 场景 B：搬家中途有文件被占用 → 该文件留在原处、其余照搬、该目录路径仍指向旧目录；解除占用后再跑一次全部到位。
// 场景 C：新位置路径超过 259 → 留在原处，其余照搬。
// 场景 D：旧版 MCE（整合包自带）又写出旧 runtime\ → 下次启动并入，较新的文件留在新位置、另一份改名保留。
// 场景 E：助手在运行 → 推迟，什么都不动。
// 场景 F：JSON 里三种写法的旧路径（\\、/、根目录用 /）都改到新位置，runtime-xxx 不误改。
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

(string home, string bep, string root) Fresh()
{
    string home = Path.Combine(Path.GetTempPath(), "mce-layout-" + Guid.NewGuid().ToString("N").Substring(0, 8));
    string bep = Path.Combine(home, "BepInEx"), root = Path.Combine(bep, "config", "ModConfigEnhance");
    Directory.CreateDirectory(root);
    Stubs.Root = root; Stubs.BepInEx = bep; Stubs.Lines.Clear();
    return (home, bep, root);
}
void Write(string path, string text, DateTime? time = null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, text);
    if (time != null) File.SetLastWriteTimeUtc(path, time.Value);
}

// ── A：现场副本 ──
string live = args.Length > 0 ? args[0] : throw new ArgumentException("需要现场 config\\ModConfigEnhance 路径");
{
    var (home, bep, root) = Fresh();
    long copied = 0;
    void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from))
        {
            string target = Path.Combine(to, Path.GetFileName(f));
            var info = new FileInfo(f);
            if (info.Length > 4 << 20) { using var s = File.OpenRead(f); var buf = new byte[4096]; int n = s.Read(buf, 0, buf.Length); File.WriteAllBytes(target, buf.Take(n).ToArray()); }
            else File.Copy(f, target);
            copied++;
        }
        foreach (string d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }
    CopyTree(live, root);
    Console.WriteLine($"[A] copied {copied} files");
    string liveFull = Path.GetFullPath(live).TrimEnd('\\');
    foreach (string json in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
    {
        string text = File.ReadAllText(json);
        string replaced = text.Replace(liveFull.Replace("\\", "\\\\"), root.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase)
            .Replace(@"F:\\Games\\Steam\\steamapps\\common\\PEAK\\BepInEx\\config\\ModConfigEnhance", root.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase);
        if (replaced != text) File.WriteAllText(json, replaced);
    }
    string Rel(string p) => p.Substring(root.Length + 1).Replace('\\', '/');
    string Expected(string rel)
    {
        foreach (var (o, n) in MceDirectories.Moves)
            if (rel.Equals(o, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(o + "/", StringComparison.OrdinalIgnoreCase)) return n + rel.Substring(o.Length);
        return rel;
    }
    var before = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToDictionary(Rel, Hash, StringComparer.OrdinalIgnoreCase);
    // 2.5.5：现场 11-11 17:45 已搬过，副本里没有旧 runtime/ 时这条前提不成立，只在旧目录还在时检查
    if (Directory.Exists(Path.Combine(root, "runtime")))
        Check(MceDirectories.Runtime(root) == Path.Combine(root, "runtime"), "[A] before migration Runtime() resolves to the old folder");
    else Console.WriteLine("SKIP [A] live copy has no old runtime/ (already migrated)");
    var report = MceDirectoryMigration.Run(() => false);
    foreach (var line in Stubs.Lines.Take(6)) Console.WriteLine("  log: " + line.Substring(0, Math.Min(200, line.Length)));
    var after = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(Rel).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var missing = before.Where(p => { string t = Expected(p.Key); string? found = after.Contains(t) ? t : after.FirstOrDefault(a => a.StartsWith(t + ".old-layout", StringComparison.OrdinalIgnoreCase)); return found == null || Hash(Path.Combine(root, found)) != p.Value; }).Select(p => p.Key).ToList();
    // 超长的那几份（若有）应留在原处：在 .NET 10 里不会失败，但代码按 259 上限主动跳过
    var leftInPlace = missing.Where(m => File.Exists(Path.Combine(root, m.Replace('/', '\\')))).ToList();
    Check(missing.Count == leftInPlace.Count, $"[A] all {before.Count} non-JSON files moved with identical bytes, or deliberately left in place ({leftInPlace.Count})");
    Check(leftInPlace.All(m => MceDirectories.Target(root, Expected(m)).Length > 259), "[A] only over-long paths were left in place");
    foreach (var m in leftInPlace.Take(3)) Console.WriteLine("  left in place (too long): " + m.Substring(0, Math.Min(120, m.Length)));
    Check(MceDirectories.Runtime(root) == Path.Combine(root, "data", "runtime"), "[A] after migration Runtime() resolves to data/runtime");
    Check(Directory.Exists(Path.Combine(bep, "MCE", "layout-backups")) && !Directory.Exists(Path.Combine(bep, "MCE", "backups")), "[A] backup in MCE/layout-backups (outside the 14-day cleanup)");
    var snapshot = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(Rel, Hash);
    MceDirectoryMigration.Run(() => false);
    var again = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(Rel, Hash);
    Check(snapshot.Count == again.Count && snapshot.All(p => again.TryGetValue(p.Key, out var h) && h == p.Value), "[A] second run changes nothing");
    Directory.Delete(home, true);
}

// ── B：文件被占用 ──
{
    var (home, bep, root) = Fresh();
    Write(Path.Combine(root, "runtime", "ui.json"), "{\"real\":1}");
    Write(Path.Combine(root, "runtime", "sources.json"), "{\"real\":2}");
    Write(Path.Combine(root, "help", "zh-cn", "faq.md"), "# mine");
    using (var hold = new FileStream(Path.Combine(root, "runtime", "ui.json"), FileMode.Open, FileAccess.Read, FileShare.None))
    {
        var report = MceDirectoryMigration.Run(() => false);
        Check(report.Skipped.Count >= 1, "[B] locked file reported as skipped");
    }
    Check(File.ReadAllText(Path.Combine(root, "runtime", "ui.json")) == "{\"real\":1}", "[B] locked file stays where it was, untouched");
    Check(File.Exists(Path.Combine(root, "data", "runtime", "sources.json")), "[B] the other file was moved");
    Check(File.Exists(Path.Combine(root, "data", "help", "zh-cn", "faq.md")), "[B] other folders moved");
    // 部分搬过：新目录已存在，Runtime() 指向新目录；旧目录里剩下的那份下次并入
    var second = MceDirectoryMigration.Run(() => false);
    Check(!Directory.Exists(Path.Combine(root, "runtime")) && File.ReadAllText(Path.Combine(root, "data", "runtime", "ui.json")) == "{\"real\":1}", "[B] after unlock the next launch merges the rest; real data intact");
    Check(!Directory.EnumerateFiles(root, "*.old-layout*", SearchOption.AllDirectories).Any(), "[B] no file was pushed aside");
    Directory.Delete(home, true);
}

// ── C：新路径超长 ──
{
    var (home, bep, root) = Fresh();
    string deep = Path.Combine(root, "exports", new string('x', 120), new string('y', 80));
    int room = 259 - Path.Combine(root, "output", "packs", new string('x', 120), new string('y', 80)).Length - 1;
    string name = new string('z', Math.Max(1, room + 3)) + ".txt"; // 新路径 = 260+
    Write(Path.Combine(deep, name), "long");
    Write(Path.Combine(root, "exports", "short.txt"), "short");
    var report = MceDirectoryMigration.Run(() => false);
    Check(File.Exists(Path.Combine(deep, name)), "[C] over-long file left in place");
    Check(File.Exists(Path.Combine(root, "output", "packs", "short.txt")), "[C] the rest moved");
    Check(report.Skipped.Any(s => s.Contains(name)), "[C] reported with a reason");
    Directory.Delete(home, true);
}

// ── D：旧版 MCE 又写出旧目录 ──
{
    var (home, bep, root) = Fresh();
    var t0 = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
    Write(Path.Combine(root, "data", "runtime", "ui.json"), "{\"v\":\"new-layout older\"}", t0);
    Write(Path.Combine(root, "data", "runtime", "sources.json"), "{\"v\":\"new-layout newer\"}", t0.AddHours(2));
    Write(Path.Combine(root, "runtime", "ui.json"), "{\"v\":\"old-version newer\"}", t0.AddHours(1));
    Write(Path.Combine(root, "runtime", "sources.json"), "{\"v\":\"old-version older\"}", t0.AddHours(1));
    Write(Path.Combine(root, "runtime", "only-old.json"), "{}");
    MceDirectoryMigration.Run(() => false);
    string rt = Path.Combine(root, "data", "runtime");
    Check(File.ReadAllText(Path.Combine(rt, "ui.json")).Contains("old-version newer"), "[D] newer copy wins (written by the old version)");
    Check(File.ReadAllText(Path.Combine(rt, "sources.json")).Contains("new-layout newer"), "[D] newer copy wins (new layout)");
    Check(File.Exists(Path.Combine(rt, "ui.json.old-layout")) && File.Exists(Path.Combine(rt, "sources.json.old-layout")), "[D] the other copy kept as .old-layout");
    Check(File.Exists(Path.Combine(rt, "only-old.json")) && !Directory.Exists(Path.Combine(root, "runtime")), "[D] old folder merged and removed");
    Directory.Delete(home, true);
}

// ── E：助手在运行 ──
{
    var (home, bep, root) = Fresh();
    Write(Path.Combine(root, "runtime", "ui.json"), "{}");
    var report = MceDirectoryMigration.Run(() => true);
    Check(report.Deferred && File.Exists(Path.Combine(root, "runtime", "ui.json")) && !Directory.Exists(Path.Combine(root, "data")), "[E] worker running: nothing moved, old folder still used");
    Check(MceDirectories.Runtime(root) == Path.Combine(root, "runtime"), "[E] Runtime() keeps pointing at the old folder");
    Directory.Delete(home, true);
}

// ── F：JSON 旧路径三种写法 ──
{
    var (home, bep, root) = Fresh();
    string full = Path.GetFullPath(root);
    string bs = full.Replace("\\", "\\\\"), fs = full.Replace('\\', '/');
    Write(Path.Combine(root, "pack-information.json"),
        "{\"a\":\"" + bs + "\\\\overview\\\\local-pack\",\"b\":\"" + bs + "/runtime/x.json\",\"c\":\"" + fs + "/exports/p\",\"d\":\"" + bs + "\\\\runtime-xxx\",\"e\":\"" + bs + "\\\\profiles\\\\default\"}");
    Write(Path.Combine(root, "overview", "state.json"), "{}");
    Write(Path.Combine(root, "runtime", "ui.json"), "{}");
    Write(Path.Combine(root, "exports", "p", "x.txt"), "x");
    MceDirectoryMigration.Run(() => false);
    string json = File.ReadAllText(Path.Combine(root, "pack-information.json"));
    Check(json.Contains(bs + "\\\\data\\\\legacy\\\\local-pack"), "[F] backslash form rewritten (overview -> data/legacy)");
    Check(json.Contains(bs + "\\\\data\\\\runtime/x.json"), "[F] mixed form rewritten (runtime)");
    Check(json.Contains(bs + "\\\\output\\\\packs/p"), "[F] forward-slash root form rewritten (exports)");
    Check(json.Contains(bs + "\\\\runtime-xxx"), "[F] runtime-xxx untouched");
    Check(json.Contains(bs + "\\\\profiles\\\\default"), "[F] profiles untouched");
    Directory.Delete(home, true);
}

Check(MceDirectories.Remap(@"C:\r", @"C:\r\runtime\x.json") == @"C:\r\data\runtime\x.json", "Remap runtime");
Check(MceDirectories.Remap(@"C:\r", @"C:\r\profiles\default") == @"C:\r\profiles\default", "Remap leaves profiles alone");

Console.WriteLine(failed == 0 ? "ALL PASS" : failed + " FAILED");
return failed == 0 ? 0 : 1;
