#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance;

/// <summary>
/// <c>config\ModConfigEnhance\</c> 从旧的平铺布局搬到四大类（见 <see cref="MceDirectories"/>）。2.5.2 起，2.5.4 按二次审查重写：
/// · **每次启动都检查**（不靠「已搬完」标记）：只要旧目录还在就并入。整合包里自带旧版 MCE、或回退到旧版时会重新写出旧目录，
///   切回来后照样合并，不会成为孤儿。
/// · **没搬完不切换**：<see cref="MceDirectories"/> 对每个目录单独判断，旧在新不在时继续用旧目录；搬一半失败也不会另起一份空数据。
/// · **单个文件失败不中断**：被占用、路径超长（游戏的 Mono 不支持 ≥260 的路径，本机 LongPathsEnabled=0）的文件留在原处并记警告，
///   其余照搬；留下的文件下次启动再试，旧目录随之保留。
/// · **并入时旧数据优先**：目标已有同名文件时，修改时间较新的一份留在目标，另一份改名「名字.old-layout」保留，不覆盖、不丢。
/// · 等更新写入结束、更新助手不在运行时才做（<see cref="Plugin"/> 在 WaitForPendingUpdateWrite 之后调用；助手在跑就推迟到下次启动）。
/// · 动之前把要搬的状态文件（json/txt/md 等，单个 ≤16 MB）复制到 <c>BepInEx\MCE\layout-backups\时间\</c>
///   （不在 MCE\backups 下，更新助手 14 天清理不会删它；目录名短，减少超长路径）。
/// · 改写 JSON 里保存的旧绝对路径（profiles、data\runtime、data\legacy、根目录 pack-information.json）。
/// </summary>
internal static class MceDirectoryMigration
{
    private const int MaxPath = 259;

    internal sealed class Report
    {
        internal int Moved, Renamed, Rewritten;
        internal readonly List<string> Skipped = new List<string>();
        internal string Backup = "";
        internal bool Deferred;
    }

    /// <summary>插件启动时调用。返回报告供日志与夹具使用。</summary>
    internal static Report Run(Func<bool>? workerRunning = null)
    {
        var report = new Report();
        string root = Loc.RootDirectory;
        if (!Directory.Exists(root)) return report;
        var pending = MceDirectories.Moves.Where(m => Exists(Path.Combine(root, m.Old))).ToList();
        if (pending.Count == 0) return report;
        if (workerRunning?.Invoke() == true)
        {
            report.Deferred = true;
            Plugin.Log.LogInfo("[目录] 更新助手正在运行，config\\InGameModManager 的整理推迟到下次启动（期间继续使用原来的文件夹）。");
            return report;
        }

        try
        {
            // 超长等原因留在原处的文件会让旧目录一直在、每次启动都走到这里：沿用第一次的备份目录、已备份的文件不重复复制，备份不会越堆越多
            // 1.0.0 复查 #11：按个人数据目录名分子目录。改名后的运行目录里还有旧 config\ModConfigEnhance 在 2.5.4 时留下的备份，
            // 新根 config\InGameModManager 的整理若沿用它，同名文件会被当成「已备份」跳过
            string backups = ModConfigEnhance.Updating.ManagerIdentity.Runtime(BepInEx.Paths.BepInExRootPath, "layout-backups", Path.GetFileName(root.TrimEnd('\\', '/')));
            string? first = Directory.Exists(backups) ? Directory.GetDirectories(backups).OrderBy(d => d, StringComparer.Ordinal).FirstOrDefault() : null;
            report.Backup = first ?? Path.Combine(backups, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            foreach (var (oldName, _) in pending) Copy(Path.Combine(root, oldName), Path.Combine(report.Backup, oldName), report);
            foreach (string json in RewriteCandidates(root)) Copy(json, Path.Combine(report.Backup, "rewritten", Relative(root, json)), report);
        }
        catch (Exception ex)
        {
            // 备份目录本身建不出来：不搬，继续用旧目录
            Plugin.Log.LogError("[目录] 整理前的备份失败，这次不整理，继续使用原来的文件夹：" + ex.Message);
            return report;
        }

        foreach (var (oldName, newName) in pending)
        {
            try { Move(Path.Combine(root, oldName), MceDirectories.Target(root, newName), report); }
            catch (Exception ex) { report.Skipped.Add(oldName + "：" + ex.Message); }
        }

        foreach (string json in RewriteCandidates(root))
        {
            try { report.Rewritten += RewritePaths(root, json); }
            catch (Exception ex) { report.Skipped.Add(Relative(root, json) + "（改写路径）：" + ex.Message); }
        }

        Plugin.Log.LogInfo($"[目录] 整理 config\\InGameModManager：搬了 {report.Moved} 个文件（同名改留 {report.Renamed} 个），改写 {report.Rewritten} 处保存的路径；备份 {report.Backup}");
        foreach (string skipped in report.Skipped.Take(20)) Plugin.Log.LogWarning("[目录] 没搬动，留在原处，下次启动再试：" + skipped);
        if (report.Skipped.Count > 20) Plugin.Log.LogWarning($"[目录] 另有 {report.Skipped.Count - 20} 项没搬动。");
        return report;
    }

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);

    private static string Relative(string root, string path) => path.Substring(Path.GetFullPath(root).TrimEnd('\\', '/').Length + 1);

    /// <summary>可能存着 config\ModConfigEnhance 绝对路径的 JSON：方案、运行状态（新旧两处）、旧版数据、根目录的整合包信息。</summary>
    private static IEnumerable<string> RewriteCandidates(string root)
    {
        var files = new List<string>();
        foreach (string dir in new[] { MceDirectories.Profiles(root), MceDirectories.Target(root, "data/runtime"), Path.Combine(root, "runtime"),
                     MceDirectories.Target(root, "data/legacy"), Path.Combine(root, "overview") })
        {
            if (!Directory.Exists(dir)) continue;
            try { files.AddRange(Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)); } catch { }
        }

        string information = Path.Combine(root, "pack-information.json");
        if (File.Exists(information)) files.Add(information);
        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string text;
            try { text = File.ReadAllText(file, Encoding.UTF8); } catch { continue; }
            if (text.IndexOf(Updating.ManagerIdentity.ConfigDirectory, StringComparison.OrdinalIgnoreCase) >= 0) yield return file;
        }
    }

    /// <summary>
    /// 只改 JSON 字符串里的旧路径，其余字节不动。JSON 里根目录与子目录之间可能是 <c>\\</c>（转义的反斜杠）或 <c>/</c>，
    /// 根目录本身也可能用 <c>/</c> 写；三种写法都认，统一写回 <c>\\</c>。子目录名后面必须是 <c>"</c>、<c>\</c> 或 <c>/</c>，
    /// 避免 <c>runtime</c> 误改 <c>runtime-xxx</c>、<c>backup</c> 误改 <c>backups</c>。返回改了几处。
    /// </summary>
    internal static int RewritePaths(string root, string file)
    {
        string text = File.ReadAllText(file, Encoding.UTF8);
        string original = text;
        int count = 0;
        string full = Path.GetFullPath(root).TrimEnd('\\', '/');
        string[] roots = { full.Replace("\\", "\\\\"), full.Replace('\\', '/') };
        string[] separators = { "\\\\", "/" };
        foreach (var (oldName, newName) in MceDirectories.Moves)
        {
            foreach (string rootForm in roots)
            {
                foreach (string separator in separators)
                {
                    string from = rootForm + separator + oldName;
                    string to = roots[0] + "\\\\" + newName.Replace("/", "\\\\");
                    int at = 0;
                    while ((at = text.IndexOf(from, at, StringComparison.OrdinalIgnoreCase)) >= 0)
                    {
                        int end = at + from.Length;
                        bool boundary = end >= text.Length || text[end] == '"' || text[end] == '\\' || text[end] == '/';
                        if (!boundary) { at = end; continue; }
                        text = text.Substring(0, at) + to + text.Substring(end);
                        at += to.Length;
                        count++;
                    }
                }
            }
        }

        if (count == 0 || text == original) return 0;
        string temporary = file + ".mce-layout.tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        File.Copy(temporary, file, true);
        File.Delete(temporary);
        return count;
    }

    /// <summary>
    /// 搬一个文件或目录。目录：目标不在且里面没有超长路径时整体改名，否则逐个文件并入。
    /// 单个文件失败（占用、超长）只记进 <see cref="Report.Skipped"/>，留在原处。
    /// </summary>
    private static void Move(string source, string target, Report report)
    {
        if (File.Exists(source)) { MoveFile(source, target, report); return; }
        if (!Directory.Exists(source)) return;
        if (!Exists(target) && LongestUnder(source, target) <= MaxPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            int files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Count();
            try { Directory.Move(source, target); report.Moved += files; return; }
            catch (IOException) { } // 里面有被占用的文件：改成逐个搬
            catch (UnauthorizedAccessException) { }
        }

        Directory.CreateDirectory(target);
        // 复查：逐个搬期间新目录先挂「未搬完」标记，MceDirectories 看到它继续用旧目录；旧目录清空才摘掉
        string marker = Path.Combine(target, MceDirectories.IncompleteMarker);
        bool top = !File.Exists(marker);
        try { if (top) File.WriteAllText(marker, DateTime.Now.ToString("O")); } catch { }
        foreach (string file in Directory.GetFiles(source))
        {
            try { MoveFile(file, Path.Combine(target, Path.GetFileName(file)), report); }
            catch (Exception ex) { report.Skipped.Add(file + "：" + ex.Message); }
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            try { Move(dir, Path.Combine(target, Path.GetFileName(dir)), report); }
            catch (Exception ex) { report.Skipped.Add(dir + "：" + ex.Message); }
        }

        try { if (!Directory.EnumerateFileSystemEntries(source).Any()) Directory.Delete(source); } catch { }
        try { if (!Directory.Exists(source) && File.Exists(marker)) File.Delete(marker); } catch { }
    }

    private static void MoveFile(string source, string target, Report report)
    {
        if (target.Length > MaxPath)
        {
            report.Skipped.Add(source + "：" + Loc.T("the new path would be too long for the game to open; left in place", "新位置的路径太长，游戏打不开，留在原处", "新位置的路徑太長，遊戲打不開，留在原處"));
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target)) { File.Move(source, target); report.Moved++; return; }
        // 两边都有：较新的一份留在新位置，另一份改名保留（不覆盖任何一份）
        bool sourceNewer = File.GetLastWriteTimeUtc(source) > File.GetLastWriteTimeUtc(target);
        if (sourceNewer)
        {
            // 复查：先把新位置那份挪开、再搬进来；第二步失败就挪回去，新位置不会缺文件
            string aside = Unique(target);
            File.Move(target, aside);
            try { File.Move(source, target); }
            catch { try { File.Move(aside, target); } catch { } throw; }
        }
        else File.Move(source, Unique(target));
        report.Moved++; report.Renamed++;
    }

    private static int LongestUnder(string source, string target)
    {
        int extra = target.Length - source.Length, longest = target.Length;
        try { foreach (string entry in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories)) longest = Math.Max(longest, entry.Length + extra); }
        catch { return int.MaxValue; }
        return longest;
    }

    private static string Unique(string target)
    {
        string candidate = target + ".old-layout";
        for (int i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++) candidate = target + ".old-layout" + i;
        return candidate;
    }

    private static readonly HashSet<string> StateExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".bak", ".lastvalid", ".cfg", ".txt", ".md", ".mcedoc", ".yml", ".yaml" };

    /// <summary>备份状态文件。单个文件失败（超长、占用）只记警告，不中断。</summary>
    private static void Copy(string source, string target, Report report)
    {
        if (File.Exists(source))
        {
            try
            {
                if (!StateExtensions.Contains(Path.GetExtension(source)) || new FileInfo(source).Length > 16 * 1024 * 1024) return;
                if (target.Length > MaxPath || File.Exists(target)) return; // 超长的状态文件只可能在整合包导出里；已备份过的不重复
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, false);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[目录] 备份跳过 " + source + "：" + ex.Message); }
            return;
        }

        if (!Directory.Exists(source)) return;
        string[] files, dirs;
        try { files = Directory.GetFiles(source); dirs = Directory.GetDirectories(source); }
        catch (Exception ex) { Plugin.Log.LogWarning("[目录] 备份跳过 " + source + "：" + ex.Message); return; }
        foreach (string file in files) Copy(file, Path.Combine(target, Path.GetFileName(file)), report);
        foreach (string dir in dirs) Copy(dir, Path.Combine(target, Path.GetFileName(dir)), report);
    }
}
