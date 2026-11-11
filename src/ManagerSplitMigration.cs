#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance;

/// <summary>
/// 1.0.0 拆分（用户 2026-11-11 拍板）：完整版从「ModConfig Enhance」独立成「游戏内模组管理器」后，第一次启动把旧数据接过来。
/// 在 <see cref="MceDirectoryMigration"/> 之前、BindConfig 之后运行。各步独立，单项失败只记日志，**没做完的下次启动接着做**：
///
/// 1. **设置复制**：<c>com.aiae.modconfigenhance.cfg</c> 原样复制成 <c>com.aiae.ingamemodmanager.cfg</c>（新 cfg 还不存在时；
///    由 <see cref="Plugin"/> 在绑定前调用）。
/// 2. **个人数据复制**：<c>config\ModConfigEnhance\</c> → <c>config\InGameModManager\</c>，旧目录原样留给简化版 MCE。
///    完成与否看新目录里的标记文件 <see cref="MarkerName"/>，不看目录在不在（启动后别的代码会先建出空目录）。
///    逐个文件复制：目标已有的不覆盖、单个文件失败只记录不中断，失败的下次启动补；全部成功才写标记。
///    <c>output\packs</c>、旧布局 <c>exports</c>（旧版导出的整合包，可能几百 MB）不复制，只在日志里写出位置。
/// 3. **签名身份复制**：<c>%LOCALAPPDATA%\ModConfigEnhance\signing</c> → <c>…\InGameModManager\signing</c>，同样逐个、不覆盖、可续。
/// 4. **运行目录改名**：<c>BepInEx\MCE\</c>（队列、整合包库、商店记录、工具缓存；简化版 MCE 从不使用）整体改名为 <c>BepInEx\IGMM\</c>，
///    不复制（整合包库可能好几 GB）。改名前 <see cref="ManagerIdentity.Runtime(string)"/> 一直回退到旧目录，所以推迟或失败时
///    启动后的代码继续用旧目录里的真实数据。两边都已存在（例如旧版又写出了 <c>BepInEx\MCE</c>）时逐个文件并入，新目录已有的不覆盖。
/// 5. **改写保存的旧绝对路径**：复制 / 改名后把两棵树（个人数据、运行目录）JSON 里的
///    <c>config\ModConfigEnhance</c>、<c>BepInEx\MCE</c> 改成新位置（包括 junction 另一侧的同一物理路径、JSON 的 <c>\uXXXX</c> 转义写法）。
///    改写待办写在 <see cref="PendingRewriteName"/>，做完才删，中途被杀下次重跑（改写本身幂等）。
/// </summary>
internal static class ManagerSplitMigration
{
    private const int MaxPath = 259;
    internal const string MarkerName = "split-from-ModConfigEnhance.txt";
    internal const string PendingRewriteName = "split-rewrite-pending.txt";
    private const long MaxJsonBytes = 64L * 1024 * 1024;

    internal sealed class Report
    {
        internal int Copied, Rewritten, Merged;
        internal bool Config, Runtime, Signing, Data;
        internal readonly List<string> Skipped = new List<string>();
        internal string Note = "";
    }

    /// <summary>cfg 复制要在任何 Config.Bind 之前做（Plugin.Awake 第一行），之后 Config.Reload() 读入复制来的值。</summary>
    internal static bool CopyConfig(string configRoot, Report report)
    {
        string target = Path.Combine(configRoot, ManagerIdentity.ConfigFileName), source = Path.Combine(configRoot, ManagerIdentity.LegacyConfigFileName);
        if (File.Exists(target) || !File.Exists(source)) return false;
        try { File.Copy(source, target, false); report.Config = true; return true; }
        catch (Exception ex) { report.Skipped.Add(source + ": " + ex.Message); return false; }
    }

    /// <summary>个人数据、签名、运行目录、路径改写。<paramref name="busy"/> 为 true（助手在跑或更新在提交）时运行目录不动，下次再做。</summary>
    internal static Report Run(string bepInExRoot, string configRoot, string localAppData, bool busy)
    {
        var report = new Report();
        string oldData = Path.Combine(configRoot, ManagerIdentity.LegacyConfigDirectory), newData = Path.Combine(configRoot, ManagerIdentity.ConfigDirectory);
        string oldRuntime = ManagerIdentity.LegacyRuntime(bepInExRoot), newRuntime = ManagerIdentity.RuntimeTarget(bepInExRoot);
        CopyData(oldData, newData, report);
        CopySigning(localAppData, report);
        if (busy) report.Note = "runtime folder rename deferred: the update worker is running or an update is being committed";
        else MoveRuntime(oldRuntime, newRuntime, report);
        RewritePending(new[] { (oldData, newData), (oldRuntime, newRuntime) }, new[] { newData, newRuntime }, report);
        return report;
    }

    // ── 个人数据 ────────────────────────────────────────────

    private static void CopyData(string source, string target, Report report)
    {
        if (!Directory.Exists(source) || File.Exists(Path.Combine(target, "data", MarkerName))) return;
        int failures = report.Skipped.Count;
        try
        {
            CopyTree(source, target, source, report, skipExports: true);
        }
        catch (Exception ex) { report.Skipped.Add(source + ": " + ex.Message); return; }
        AddPending(target, source, target);
        // 只算真正的失败；被有意跳过的旧导出与链接不影响「已完成」
        bool failed = report.Skipped.Skip(failures).Any(s => !s.EndsWith(SkippedOnPurpose, StringComparison.Ordinal));
        if (failed) { report.Note = Join(report.Note, "some personal files could not be copied yet; they will be retried next launch"); return; }
        try
        {
            Directory.CreateDirectory(Path.Combine(target, "data"));
            File.WriteAllText(Path.Combine(target, "data", MarkerName), "Copied from " + source + " on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ".\nThe original folder is left for ModConfig Enhance.\n", new UTF8Encoding(false));
            report.Data = true;
        }
        catch (Exception ex) { report.Skipped.Add(target + ": " + ex.Message); }
    }

    private const string SkippedOnPurpose = " (left in place on purpose)";

    /// <summary>旧版导出的整合包（output\packs、旧布局 exports）不复制：体积大，留在原处，路径写进日志。</summary>
    private static bool SkipDirectory(string relative)
    {
        string p = relative.Replace('\\', '/').Trim('/');
        return p.Equals("output/packs", StringComparison.OrdinalIgnoreCase) || p.Equals("exports", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>逐个复制；目标已有的文件不覆盖（先前已复制、或新版已经写过）；单个失败只记录。</summary>
    private static void CopyTree(string source, string target, string top, Report report, bool skipExports)
    {
        if (target.Length > MaxPath) { report.Skipped.Add(source + ": target folder path too long, left in place"); return; }
        Directory.CreateDirectory(target);
        string[] files, dirs;
        try { files = Directory.GetFiles(source); dirs = Directory.GetDirectories(source); }
        catch (Exception ex) { report.Skipped.Add(source + ": " + ex.Message); return; }
        foreach (string file in files)
        {
            string to = Path.Combine(target, Path.GetFileName(file));
            if (File.Exists(to)) continue;
            if (to.Length > MaxPath) { report.Skipped.Add(file + ": path too long, left in place"); continue; }
            try { File.Copy(file, to, false); report.Copied++; }
            catch (Exception ex) { report.Skipped.Add(file + ": " + ex.Message); }
        }

        foreach (string dir in dirs)
        {
            string relative = dir.Substring(top.Length).TrimStart('\\', '/');
            if (skipExports && SkipDirectory(relative)) { report.Skipped.Add(dir + ": old pack exports are not copied; move them yourself if needed" + SkippedOnPurpose); continue; }
            bool link;
            try { link = (new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0; } catch { link = true; }
            if (link) { report.Skipped.Add(dir + ": link, not copied" + SkippedOnPurpose); continue; }
            CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)), top, report, skipExports);
        }
    }

    private static void CopySigning(string localAppData, Report report)
    {
        if (string.IsNullOrEmpty(localAppData)) return;
        string source = Path.Combine(localAppData, ManagerIdentity.LegacyConfigDirectory, "signing"), target = Path.Combine(localAppData, ManagerIdentity.ConfigDirectory, "signing");
        if (!Directory.Exists(source)) return;
        int before = report.Copied, failures = report.Skipped.Count;
        CopyTree(source, target, source, report, skipExports: false);
        if (report.Copied > before && report.Skipped.Count == failures) report.Signing = true;
    }

    // ── 运行目录 ────────────────────────────────────────────

    private static void MoveRuntime(string source, string target, Report report)
    {
        if (!Directory.Exists(source)) return;
        if (!Directory.Exists(target))
        {
            try { Directory.Move(source, target); report.Runtime = true; }
            catch (Exception ex) { report.Skipped.Add(source + " → " + target + ": " + ex.Message + " (will retry next launch; until then the old folder is used)"); return; }
        }
        else
        {
            // 两边都在：旧版又写出了 BepInEx\MCE，或上次改名后又被建出来。逐个并入，新目录已有的不覆盖，并入完才删旧目录。
            int before = report.Skipped.Count;
            MergeTree(source, target, report);
            if (report.Skipped.Count == before) { try { DeleteEmpty(source); } catch { } }
            report.Runtime = true;
        }

        AddPending(target, source, target);
        // 旧版写的恢复入口指向 BepInEx\MCE 与 MCE.UpdateWorker.exe，已失效；新版在工具缓存就绪后写自己的入口
        foreach (string stale in new[] { "MCE-Recovery.cmd", "MCE-Recovery.ps1" })
        {
            try { string path = Path.Combine(target, stale); if (File.Exists(path)) File.Move(path, path + ".old"); } catch { }
        }
    }

    private static void MergeTree(string source, string target, Report report)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
        {
            string to = Path.Combine(target, Path.GetFileName(file));
            try
            {
                if (File.Exists(to)) { report.Skipped.Add(file + ": already present in the new folder, old copy kept in place"); continue; }
                File.Move(file, to); report.Merged++;
            }
            catch (Exception ex) { report.Skipped.Add(file + ": " + ex.Message); }
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            string to = Path.Combine(target, Path.GetFileName(dir));
            try
            {
                if (!Directory.Exists(to)) { Directory.Move(dir, to); report.Merged++; continue; }
                MergeTree(dir, to, report);
            }
            catch (Exception ex) { report.Skipped.Add(dir + ": " + ex.Message); }
        }
    }

    private static void DeleteEmpty(string dir)
    {
        foreach (string child in Directory.GetDirectories(dir)) DeleteEmpty(child);
        if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }

    // ── 路径改写 ────────────────────────────────────────────

    /// <summary>改写待办：每行「旧根|新根」，写在新树里；做完才删。</summary>
    private static void AddPending(string tree, string oldRoot, string newRoot)
    {
        try
        {
            string file = Path.Combine(tree, PendingRewriteName);
            string line = Path.GetFullPath(oldRoot) + "|" + Path.GetFullPath(newRoot);
            var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : new List<string>();
            if (!lines.Contains(line)) { lines.Add(line); File.WriteAllLines(file, lines); }
        }
        catch { }
    }

    /// <summary>
    /// 有待办就把两棵树里所有 JSON 的两对旧根都改写一遍（个人数据里也存着运行目录路径，反之亦然）。
    /// 全部成功才删待办；中途失败或被杀，下次启动重跑。
    /// </summary>
    private static void RewritePending((string Old, string New)[] roots, string[] trees, Report report)
    {
        var pendingFiles = trees.Select(t => Path.Combine(t, PendingRewriteName)).Where(File.Exists).ToList();
        if (pendingFiles.Count == 0) return;
        var pairs = new List<(string Old, string New)>();
        foreach (var (oldRoot, newRoot) in roots)
        {
            foreach (string form in PhysicalAliases(oldRoot)) pairs.Add((form, Path.GetFullPath(newRoot)));
        }

        int failures = report.Skipped.Count;
        foreach (string tree in trees.Where(Directory.Exists))
        {
            foreach (string json in Json(tree))
            {
                try { report.Rewritten += Rewrite(json, pairs, report); }
                catch (Exception ex) { report.Skipped.Add(json + ": " + ex.Message); }
            }
        }

        if (report.Skipped.Count == failures) foreach (string file in pendingFiles) { try { File.Delete(file); } catch { } }
    }

    /// <summary>
    /// 同一个物理目录的几种写法（复查 #5）：GetFullPath 本身、它经 junction 解析后的物理路径
    /// （如 <c>F:\Games\Steam\…</c> → <c>F:\Games\SteamLibrary\…</c>），以及同盘其他 junction 指向同一物理位置的写法（从另一侧启动时写下的）。
    /// </summary>
    internal static List<string> PhysicalAliases(string path)
    {
        string full = Path.GetFullPath(path).TrimEnd('\\', '/');
        var result = new List<string> { full };
        string? real = FinalPath(full);
        if (real != null && !result.Contains(real, StringComparer.OrdinalIgnoreCase)) result.Add(real);
        string physical = real ?? full;
        try
        {
            // 逐级向上：同级里有 junction 指向当前这一级（物理路径），就把它当作另一种写法
            string? cursor = physical;
            while (cursor != null)
            {
                string? parent = Path.GetDirectoryName(cursor);
                if (parent == null) break;
                foreach (string sibling in SafeDirectories(parent))
                {
                    if (sibling.Equals(cursor, StringComparison.OrdinalIgnoreCase)) continue;
                    bool link;
                    try { link = (new DirectoryInfo(sibling).Attributes & FileAttributes.ReparsePoint) != 0; } catch { continue; }
                    if (!link) continue;
                    string? target = FinalPath(sibling);
                    if (target == null || !target.Equals(cursor, StringComparison.OrdinalIgnoreCase)) continue;
                    string alias = (sibling + physical.Substring(cursor.Length)).TrimEnd('\\');
                    if (!result.Contains(alias, StringComparer.OrdinalIgnoreCase)) result.Add(alias);
                }
                cursor = parent;
            }
        }
        catch { }
        return result;
    }

    private static string[] SafeDirectories(string dir) { try { return Directory.GetDirectories(dir); } catch { return Array.Empty<string>(); } }

    /// <summary>
    /// Windows 的最终物理路径（解析全部 junction / 符号链接）。Mono 没有 <c>LinkTarget</c>，所以直接问系统
    /// （<c>GetFinalPathNameByHandleW</c>）。不是 Windows、网络路径或失败时返回 null。
    /// </summary>
    private static string? FinalPath(string path)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || !Directory.Exists(path)) return null;
        // FILE_FLAG_BACKUP_SEMANTICS 才能打开目录；共享读写删，不加锁
        IntPtr handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;
        IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(2048 * 2);
        try
        {
            uint length = GetFinalPathNameByHandleW(handle, buffer, 2048, 0);
            if (length == 0 || length >= 2048) return null;
            string value = System.Runtime.InteropServices.Marshal.PtrToStringUni(buffer, (int)length);
            const string unc = @"\\?\UNC\", local = @"\\?\";
            if (value.StartsWith(unc, StringComparison.OrdinalIgnoreCase)) return null;
            if (value.StartsWith(local, StringComparison.Ordinal)) value = value.Substring(local.Length);
            return value.TrimEnd('\\');
        }
        catch { return null; }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); CloseHandle(handle); }
    }

    // 缓冲用 AllocHGlobal（不用 StringBuilder：Mono 下 StringBuilder 互操作曾导致崩溃，见 LogSavePicker）
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(IntPtr handle, IntPtr path, uint length, uint flags);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>树里的 JSON（含 .transactions 下的切包计划；只跳过纯缓存）。</summary>
    private static IEnumerable<string> Json(string root)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "downloads", "tools", ".view-cache", ".trash", "layout-backups" };
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            string[] files, dirs;
            try { files = Directory.GetFiles(dir, "*.json"); dirs = Directory.GetDirectories(dir); } catch { continue; }
            foreach (string file in files) yield return file;
            foreach (string child in dirs) if (!skip.Contains(Path.GetFileName(child))) stack.Push(child);
        }
    }

    internal static int Rewrite(string file, string oldRoot, string newRoot) => Rewrite(file, new List<(string, string)> { (Path.GetFullPath(oldRoot), Path.GetFullPath(newRoot)) }, null);

    /// <summary>
    /// 把 JSON 字符串里的旧绝对路径换成新的，其余字节不动。每个旧根认三种写法：<c>\\</c>（转义反斜杠）、<c>/</c>、
    /// 以及 System.Text.Json 把非 ASCII 字符转成 <c>\uXXXX</c> 后的写法。旧路径后面必须**不是文件名字符**
    /// （字母数字、<c>.</c>、<c>-</c>、<c>_</c> 等），这样 <c>…\MCE</c> 不会误改 <c>…\MCEPack</c>、<c>…\MCE.bak</c>，
    /// 但 <c>…\MCE"</c>、<c>…\MCE\…</c>、<c>…\MCE|…</c>（来源缓存的组合键）都会改。
    /// </summary>
    internal static int Rewrite(string file, List<(string Old, string New)> pairs, Report? report)
    {
        string text;
        try
        {
            if (new FileInfo(file).Length > MaxJsonBytes) { report?.Skipped.Add(file + ": larger than 64 MB, saved paths not rewritten"); return 0; }
            text = File.ReadAllText(file, Encoding.UTF8);
        }
        catch { return 0; }
        int count = 0;
        string original = text;
        foreach (var (oldRoot, newRoot) in pairs)
        {
            string o = oldRoot.TrimEnd('\\', '/'), n = newRoot.TrimEnd('\\', '/');
            foreach (var (from, to) in new[] { (o.Replace("\\", "\\\\"), n.Replace("\\", "\\\\")), (o.Replace('\\', '/'), n.Replace('\\', '/')), (EscapeNonAscii(o.Replace("\\", "\\\\")), EscapeNonAscii(n.Replace("\\", "\\\\"))) }.Distinct())
            {
                int at = 0;
                while ((at = text.IndexOf(from, at, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    int end = at + from.Length;
                    if (end < text.Length && IsNameChar(text[end])) { at = end; continue; }
                    text = text.Substring(0, at) + to + text.Substring(end);
                    at += to.Length;
                    count++;
                }
            }
        }

        if (count == 0 || text == original) return 0;
        string temporary = file + ".split.tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        File.Copy(temporary, file, true);
        File.Delete(temporary);
        return count;
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or ' ' or '(' or ')' or '[' or ']' or '&' or '+' or ',' or '\'' or '!' or '#' or '$' or '%' or '@' or '~' or '{' or '}';

    /// <summary>System.Text.Json 默认把非 ASCII 与 &amp; &lt; &gt; 等写成 \uXXXX（小写十六进制大小写不敏感比较）。</summary>
    private static string EscapeNonAscii(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(c > 127 || c is '&' or '<' or '>' or '+' or '\'' ? "\\u" + ((int)c).ToString("X4") : c.ToString());
        return sb.ToString();
    }

    private static string Join(string a, string b) => a.Length == 0 ? b : a + "; " + b;
}
