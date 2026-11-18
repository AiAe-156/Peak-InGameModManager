// 0.0.2 导出检查（2026-11-18）：「完整解压即用（自带 BepInEx 加载器）」包的模拟夹具。
// 直接链接生产代码的发布策略：PublicationFilePolicy（Keep / IsShadowCopy / ProjectManifest / IsUpdaterRuntime / IsSelfDemo）、
// PluginFileNames、PackEnvironmentPolicy.ManagedPath、PublicationScrub.Run。
// PackPublisher.EnvironmentPlan / WriteNewcomerZip 依赖游戏运行时（BepInEx.Paths、Chainloader、目录扫描出的 OverviewMod），
// 这里按同样的规则重放：所有 plugins 资料包都被选中（真实发布只带方案引用到的资料包及其依赖，是这里的子集），没有默认方案投影
// （等于作者磁盘现状）。只读取作者游戏目录，所有输出都在暂存目录。
// 用法：publish-sim <游戏根目录> <暂存目录>
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ModConfigEnhance.Overview;
using ModConfigEnhance.Updating;

int checks = 0, failures = 0;
var findings = new List<string>();
void Check(bool ok, string name) { checks++; if (!ok) { failures++; Console.WriteLine("FAIL " + name); } else Console.WriteLine("PASS " + name); }

string game = args.Length > 0 ? args[0] : throw new ArgumentException("game root");
string work = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "igmm-publish-sim");
if (Directory.Exists(work)) Directory.Delete(work, true);
Directory.CreateDirectory(work);

// ── 1. 纯策略：停用副本 / manifest 投影 / 排除规则 ───────────────────────
string mock = Path.Combine(work, "mock"); string pkg = Path.Combine(mock, "plugins", "Grp", "PkgA"), pkg2 = Path.Combine(mock, "plugins", "Grp", "PkgB");
Directory.CreateDirectory(pkg); Directory.CreateDirectory(pkg2);
foreach (string f in new[] { "a.dll", "a.dll.d", "a.dll.old", "manifest.json", "manifest.json.old" }) File.WriteAllText(Path.Combine(pkg, f), f);
foreach (string f in new[] { "b.dll.d", "manifest.json.old", "c.dll_off" }) File.WriteAllText(Path.Combine(pkg2, f), f);
Check(!PublicationFilePolicy.IsShadowCopy(Path.Combine(pkg, "a.dll")), "active dll is not a shadow copy");
Check(PublicationFilePolicy.IsShadowCopy(Path.Combine(pkg, "a.dll.d")) && PublicationFilePolicy.IsShadowCopy(Path.Combine(pkg, "a.dll.old")), ".dll.d / .dll.old beside the active dll are shadow copies");
Check(PublicationFilePolicy.IsShadowCopy(Path.Combine(pkg, "manifest.json.old")), "manifest.json.old beside manifest.json is a shadow copy");
Check(!PublicationFilePolicy.IsShadowCopy(Path.Combine(pkg2, "b.dll.d")) && !PublicationFilePolicy.IsShadowCopy(Path.Combine(pkg2, "manifest.json.old")) && !PublicationFilePolicy.IsShadowCopy(Path.Combine(pkg2, "c.dll_off")), "a disabled package alone keeps its disabled files");
Check(PublicationFilePolicy.Keep("BepInEx/plugins/Grp/PkgB/manifest.json.old"), "disabled manifest is publishable");
Check(!PublicationFilePolicy.Keep("BepInEx/plugins/Grp/PkgB/notes.json.old") && PublicationFilePolicy.Keep("BepInEx/plugins/Grp/PkgB/b.dll.old"), "other .old files still dropped, .dll.old kept");
Check(PublicationFilePolicy.ProjectManifest("plugins/Grp/PkgB/manifest.json.old", true) == "plugins/Grp/PkgB/manifest.json"
    && PublicationFilePolicy.ProjectManifest("plugins/Grp/PkgA/manifest.json", false) == "plugins/Grp/PkgA/manifest.json.old"
    && PublicationFilePolicy.ProjectManifest("plugins/Grp/PkgA/readme.md", false) == "plugins/Grp/PkgA/readme.md", "manifest projected to the default-profile state");
Check(PluginFileNames.OriginalPath("plugins/x/b.dll.d") + PluginFileNames.DisabledSuffix == "plugins/x/b.dll.d", "dll projection suffix");
Check(!PublicationFilePolicy.Keep("BepInEx/IGMM/shop/x.json") && !PublicationFilePolicy.Keep("BepInEx/plugins/X/备份/a.dll") && !PublicationFilePolicy.Keep("BepInEx/plugins/X/cache/a.bin")
    && !PublicationFilePolicy.Keep("BepInEx/LogOutput.log") && !PublicationFilePolicy.Keep("BepInEx/plugins/X/IGMMPack/profiles.json"), "runtime / backup / cache / log / pack data excluded");
Check(!PackEnvironmentPolicy.ManagedPath("config/InGameModManager/profiles/a.json", 2) && PackEnvironmentPolicy.ManagedPath("config/BepInEx.cfg", 2)
    && PackEnvironmentPolicy.ManagedPath("core/BepInEx.dll", 2) && !PackEnvironmentPolicy.ManagedPath("cache/x", 2), "environment v2 boundary");

// ── 2. 用作者真实目录重放完整包 ─────────────────────────────────────────
string bep = Path.Combine(game, "BepInEx");
string package = Path.Combine(work, "package"); string outBep = Path.Combine(package, "BepInEx");
var plan = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var skippedShadow = new List<string>(); var skippedPolicy = new List<string>();
string? self = Directory.EnumerateFiles(Path.Combine(bep, "plugins"), "InGameModManager.dll*", SearchOption.AllDirectories).Select(Path.GetDirectoryName).FirstOrDefault();
string Rel(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');
void Add(string file, string relative)
{
    if (!PackEnvironmentPolicy.ManagedPath(relative, 2)) throw new IOException("not an environment path: " + relative);
    bool keep = PublicationFilePolicy.Keep(relative) && !PublicationFilePolicy.IsUpdaterRuntime(file, bep) && (self == null || !PublicationFilePolicy.IsSelfDemo(file, self));
    if (!keep) { skippedPolicy.Add("BepInEx/" + relative); return; }
    if (plan.TryGetValue(relative, out var previous) && !previous.Equals(file, StringComparison.OrdinalIgnoreCase)) throw new IOException("target conflict: " + relative);
    plan[relative] = file;
}
void Tree(string folder, bool plugin)
{
    string root = Path.Combine(bep, folder); if (!Directory.Exists(root)) return;
    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
        if (plugin && PublicationFilePolicy.IsShadowCopy(file)) { skippedShadow.Add(Rel(game, file)); continue; }
        Add(file, Rel(bep, file));
    }
}
Tree("plugins", true); Tree("core", false); Tree("patchers", false); Tree("monomod", false);
foreach (string file in Directory.EnumerateFiles(Path.Combine(bep, "config"), "*", SearchOption.AllDirectories))
{
    string relative = Rel(bep, file);
    if (PackEnvironmentPolicy.ManagedPath(relative, 2)) Add(file, relative); else skippedPolicy.Add("BepInEx/" + relative);
}
foreach (var entry in plan) { string destination = Path.Combine(outBep, entry.Key); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(entry.Value, destination); }
var scrubbed = PublicationScrub.Run(Path.Combine(outBep, "config"));
// 新手版：游戏根目录的加载器三件（与 PackPublisher.WriteNewcomerZip 同一判定）
string ini = Path.Combine(game, "doorstop_config.ini"); string iniText = File.Exists(ini) ? File.ReadAllText(ini) : "";
bool standard = PublicationFilePolicy.StandardDoorstopIni(iniText);
// 作者 ini 的判定（生产代码）：空的覆盖项后面隔空行不能被误判为有值；真有覆盖路径或绝对加载目标才换成内置模板
string blankLines = "[General]\n\nenabled = true\n\ntarget_assembly=BepInEx\\core\\BepInEx.Preloader.dll\n\nboot_config_override =\n\nignore_disable_switch = false\n\n[UnityMono]\n\ndll_search_path_override =\n\ndebug_enabled = false\n";
Check(PublicationFilePolicy.StandardDoorstopIni(blankLines) && PublicationFilePolicy.StandardDoorstopIni(blankLines.Replace("\n", "\r\n")), "standard ini with blank lines / CRLF kept verbatim");
Check(!PublicationFilePolicy.StandardDoorstopIni(blankLines.Replace("boot_config_override =", @"boot_config_override = C:\Users\x\boot.config")), "boot override path rejected");
Check(!PublicationFilePolicy.StandardDoorstopIni(blankLines.Replace("dll_search_path_override =", @"dll_search_path_override = D:\r2\profile\BepInEx\core")), "dll search override rejected");
Check(!PublicationFilePolicy.StandardDoorstopIni(blankLines.Replace(@"target_assembly=BepInEx\core", @"target_assembly=C:\r2modman\profiles\Default\BepInEx\core")), "absolute target rejected");
Check(!PublicationFilePolicy.StandardDoorstopIni(blankLines.Replace("enabled = true", "enabled = false")), "disabled doorstop rejected");
Check(standard, "author's real doorstop_config.ini is recognised as standard and shipped verbatim");
const string StandardDoorstopIni = "[General]\r\nenabled = true\r\ntarget_assembly = BepInEx\\core\\BepInEx.Preloader.dll\r\nredirect_output_log = false\r\nboot_config_override =\r\nignore_disable_switch = false\r\n\r\n[UnityMono]\r\ndll_search_path_override =\r\ndebug_enabled = false\r\ndebug_address = 127.0.0.1:10000\r\ndebug_suspend = false\r\n";
File.Copy(Path.Combine(game, "winhttp.dll"), Path.Combine(package, "winhttp.dll"));
if (File.Exists(Path.Combine(game, ".doorstop_version"))) File.Copy(Path.Combine(game, ".doorstop_version"), Path.Combine(package, ".doorstop_version"));
File.WriteAllText(Path.Combine(package, "doorstop_config.ini"), standard ? iniText : StandardDoorstopIni, new UTF8Encoding(false));
File.WriteAllText(Path.Combine(package, "安装说明.txt"), "sim", new UTF8Encoding(true));
string zip = Path.Combine(work, "newcomer.zip"); ZipFile.CreateFromDirectory(package, zip, CompressionLevel.Fastest, false);

// ── 3. 解压到干净的模拟游戏目录并核对 ─────────────────────────────────────
string clean = Path.Combine(work, "clean-game"); Directory.CreateDirectory(clean);
File.WriteAllText(Path.Combine(clean, "PEAK.exe"), "stub"); Directory.CreateDirectory(Path.Combine(clean, "PEAK_Data", "Managed"));
ZipFile.ExtractToDirectory(zip, clean);
string cb = Path.Combine(clean, "BepInEx");
var files = Directory.EnumerateFiles(clean, "*", SearchOption.AllDirectories).Select(f => Rel(clean, f)).ToList();
Check(File.Exists(Path.Combine(clean, "winhttp.dll")) && File.Exists(Path.Combine(clean, "doorstop_config.ini")) && File.Exists(Path.Combine(clean, ".doorstop_version")), "loader files at game root (winhttp.dll, doorstop_config.ini, .doorstop_version)");
string cleanIni = File.ReadAllText(Path.Combine(clean, "doorstop_config.ini"));
var target = Regex.Match(cleanIni, @"(?im)^\s*target_assembly\s*=\s*(.+?)\s*$").Groups[1].Value;
Check(target.Length > 0 && !Path.IsPathRooted(target) && File.Exists(Path.Combine(clean, target.Replace('\\', Path.DirectorySeparatorChar))), "doorstop target_assembly is relative and present: " + target);
Check(!Regex.IsMatch(cleanIni, @"(?im)^\s*[^#;\r\n]*=\s*(?:[A-Za-z]:[\\/]|\\\\|/)"), "doorstop ini has no absolute path values");
Check(Regex.IsMatch(cleanIni, @"(?im)^\s*enabled\s*=\s*true\s*$"), "doorstop enabled");
foreach (string core in new[] { "BepInEx.dll", "BepInEx.Preloader.dll", "0Harmony.dll", "Mono.Cecil.dll", "MonoMod.RuntimeDetour.dll", "MonoMod.Utils.dll", "BepInEx.Harmony.dll", "HarmonyXInterop.dll" })
    Check(File.Exists(Path.Combine(cb, "core", core)) || !File.Exists(Path.Combine(bep, "core", core)), "core/" + core + (File.Exists(Path.Combine(bep, "core", core)) ? "" : " (not in source either)"));
var sourceCore = Directory.EnumerateFiles(Path.Combine(bep, "core")).Select(Path.GetFileName).ToList();
var missingCore = sourceCore.Where(n => !File.Exists(Path.Combine(cb, "core", n!))).ToList();
Check(missingCore.All(n => n!.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".log", StringComparison.OrdinalIgnoreCase)), "every core file shipped except docs/symbols (" + string.Join(", ", missingCore) + ")");
Check(Directory.Exists(Path.Combine(cb, "plugins")) && Directory.Exists(Path.Combine(cb, "config")), "plugins and config present");
Check(Directory.Exists(Path.Combine(bep, "patchers")) == Directory.Exists(Path.Combine(cb, "patchers")) || !Directory.EnumerateFiles(Path.Combine(bep, "patchers"), "*", SearchOption.AllDirectories).Any(), "patchers shipped when the source has any");
Check(File.Exists(Path.Combine(cb, "config", "BepInEx.cfg")) == File.Exists(Path.Combine(bep, "config", "BepInEx.cfg")), "BepInEx.cfg shipped");
string[] banned = { "BepInEx/LogOutput.log", "BepInEx/cache/", "BepInEx/logs/", "BepInEx/IGMM/", "BepInEx/MCE/", "BepInEx/config/InGameModManager/", "BepInEx/config/ModConfigEnhance/", "PEAK_Data/Managed/x", "/IGMMPack/" };
Check(!files.Any(f => f.EndsWith(".log", StringComparison.OrdinalIgnoreCase)), "no .log files");
foreach (string b in banned) Check(!files.Any(f => f.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith("/") && f.Contains(b, StringComparison.OrdinalIgnoreCase)), "excluded: " + b);
Check(!files.Any(f => f.Split('/').Any(p => p is "备份" or "验证" or "backup" or "backups" or "cache" or "logs" or ".git")), "no backup / verification / cache folders anywhere");
var parked = Directory.GetDirectories(bep).Select(Path.GetFileName).Where(n => !new[] { "plugins", "core", "config", "patchers", "monomod", "cache", "logs", "IGMM", "MCE" }.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
Check(!files.Any(f => parked.Any(p => f.StartsWith("BepInEx/" + p + "/", StringComparison.OrdinalIgnoreCase))), "folders parked under BepInEx/ are not copied as-is");
Check(!files.Any(f => f.EndsWith("PEAK.exe", StringComparison.OrdinalIgnoreCase) && f != "PEAK.exe") && !files.Any(f => f.StartsWith("PEAK_Data/", StringComparison.OrdinalIgnoreCase) && f != "PEAK_Data/Managed"), "no game files in package");
// 启停状态：每个插件程序集在包里的启停 = 作者磁盘上的启停（无默认方案投影时）
int enabled = 0, disabled = 0; var mismatched = new List<string>();
foreach (string file in Directory.EnumerateFiles(Path.Combine(bep, "plugins"), "*", SearchOption.AllDirectories).Where(PluginFileNames.IsAssembly))
{
    if (PublicationFilePolicy.IsShadowCopy(file)) continue;
    string relative = Rel(bep, file); if (!plan.ContainsKey(relative)) continue;
    bool on = file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase); if (on) enabled++; else disabled++;
    if (!File.Exists(Path.Combine(cb, relative))) mismatched.Add(relative);
}
Check(mismatched.Count == 0, $"plugin enablement identical to author's disk ({enabled} enabled, {disabled} disabled)");
var disabledManifests = Directory.EnumerateFiles(Path.Combine(bep, "plugins"), "manifest.json.old", SearchOption.AllDirectories).Where(f => !PublicationFilePolicy.IsShadowCopy(f)).ToList();
Check(disabledManifests.All(f => File.Exists(Path.Combine(cb, Rel(bep, f)))), $"disabled packages keep their manifest ({disabledManifests.Count})");
// 个人路径：只记发现，不判失败（配置是作者的原样设置，PublicationScrub 只清凭据）
var absolute = new Regex(@"(?i)(?:[A-Z]:[\\/]+(?:Users|Documents and Settings)[\\/]+[^\\/\s""']+|\\\\[0-9.]+\\|[A-Z]:[\\/]+(?:Games|SteamLibrary|Program Files)[^\r\n""']{0,60})");
foreach (string f in files.Where(f => new[] { ".cfg", ".json", ".ini", ".txt", ".md", ".yml", ".yaml", ".xml", ".toml" }.Contains(Path.GetExtension(f).ToLowerInvariant())))
{
    string path = Path.Combine(clean, f); if (new FileInfo(path).Length > 4 * 1024 * 1024) continue;
    foreach (Match m in absolute.Matches(File.ReadAllText(path))) { findings.Add(f + " : " + m.Value.Trim()); break; }
}
string report = Path.Combine(work, "summary.txt");
File.WriteAllLines(report, new[] {
    $"package files: {files.Count}", $"zip bytes: {new FileInfo(zip).Length}", $"plan entries: {plan.Count}",
    $"doorstop ini copied verbatim (standard): {standard}",
    $"skipped shadow copies ({skippedShadow.Count}):" }.Concat(skippedShadow.Select(s => "  " + s))
    .Concat(new[] { $"skipped by policy ({skippedPolicy.Count}):" }).Concat(skippedPolicy.Take(60).Select(s => "  " + s)).Concat(skippedPolicy.Count > 60 ? new[] { "  …" } : Array.Empty<string>())
    .Concat(new[] { $"parked folders under BepInEx/ (not loaded by BepInEx): {string.Join(" | ", parked)}" })
    .Concat(new[] { $"scrubbed config values ({scrubbed.Count}):" }).Concat(scrubbed.Select(s => "  " + s))
    .Concat(new[] { $"absolute / personal paths left in shipped text files ({findings.Count}):" }).Concat(findings.Select(s => "  " + s)), new UTF8Encoding(false));
Console.WriteLine($"summary: {report}");
Console.WriteLine(failures == 0 ? $"PASS {checks} publish-sim checks" : $"FAIL {failures} of {checks} publish-sim checks");
return failures == 0 ? 0 : 1;
