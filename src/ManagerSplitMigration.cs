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
/// 在 <see cref="MceDirectoryMigration"/> 之前、BindConfig 之后运行。三件事，各自独立，单项失败只记日志：
///
/// 1. **个人数据复制**：<c>config\ModConfigEnhance\</c> → <c>config\InGameModManager\</c>（新目录还不存在时才做）。
///    旧目录**原样保留给简化版 MCE**（用户选「复制过去，旧的留给 MCE」）。<c>output\packs</c>（旧版导出的整合包 ZIP，
///    可能几百 MB）不复制，只在日志里写出位置；超长路径文件同样留在原处。复制完把 JSON 里保存的旧绝对路径改到新目录。
/// 2. **设置复制**：<c>com.aiae.modconfigenhance.cfg</c> 原样复制成 <c>com.aiae.ingamemodmanager.cfg</c>（新 cfg 还不存在时；
///    由 <see cref="Plugin"/> 在绑定前调用，BepInEx 读到的就是复制过来的值）。签名身份 <c>%LOCALAPPDATA%\ModConfigEnhance\signing</c> 同样复制。
/// 3. **运行目录改名**：<c>BepInEx\MCE\</c>（队列、整合包库、商店记录、工具缓存）简化版 MCE 从不使用，整体改名为 <c>BepInEx\IGMM\</c>，
///    不复制（整合包库可能好几 GB）；然后改写里面 JSON 保存的旧绝对路径。条件：新目录不存在、更新助手不在运行、没有正在提交的更新。
///    改名失败（被占用）就继续用旧目录的内容下次再试——此前不在新目录写任何东西（<see cref="ManagerIdentity.Runtime(string)"/> 指向新位置，
///    所以本步放在所有读写运行目录的代码之前）。
/// </summary>
internal static class ManagerSplitMigration
{
    private const int MaxPath = 259;
    internal const string MarkerName = "split-from-ModConfigEnhance.txt";

    internal sealed class Report
    {
        internal int Copied, Rewritten;
        internal bool Config, Runtime, Signing, Data;
        internal readonly List<string> Skipped = new List<string>();
        internal string Note = "";
    }

    /// <summary>cfg 复制要在 BepInEx 绑定 Config 之前做：BaseUnityPlugin 的 Config 是懒建的，Plugin.Awake 第一行调用即可。</summary>
    internal static bool CopyConfig(string configRoot, Report report)
    {
        string target = Path.Combine(configRoot, ManagerIdentity.ConfigFileName), source = Path.Combine(configRoot, ManagerIdentity.LegacyConfigFileName);
        if (File.Exists(target) || !File.Exists(source)) return false;
        try { File.Copy(source, target, false); report.Config = true; return true; }
        catch (Exception ex) { report.Skipped.Add(source + ": " + ex.Message); return false; }
    }

    /// <summary>个人数据、签名、运行目录。<paramref name="busy"/> 为 true（助手在跑或更新在提交）时只做不涉及运行目录的部分。</summary>
    internal static Report Run(string bepInExRoot, string configRoot, string localAppData, bool busy)
    {
        var report = new Report();
        CopyData(configRoot, report);
        CopySigning(localAppData, report);
        if (busy) report.Note = "runtime folder rename deferred: the update worker is running or an update is being committed";
        else RenameRuntime(bepInExRoot, configRoot, report);
        return report;
    }

    private static void CopyData(string configRoot, Report report)
    {
        string source = Path.Combine(configRoot, ManagerIdentity.LegacyConfigDirectory), target = Path.Combine(configRoot, ManagerIdentity.ConfigDirectory);
        if (Directory.Exists(target) || !Directory.Exists(source)) return;
        string staging = target + ".copying";
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            CopyTree(source, staging, source, report);
            Directory.Move(staging, target);
            report.Data = true;
        }
        catch (Exception ex)
        {
            report.Skipped.Add(source + ": " + ex.Message);
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            return;
        }

        foreach (string json in SafeJson(target))
        {
            try { report.Rewritten += Rewrite(json, Path.GetFullPath(source), Path.GetFullPath(target)); }
            catch (Exception ex) { report.Skipped.Add(json + ": " + ex.Message); }
        }

        try
        {
            File.WriteAllText(Path.Combine(target, "data", MarkerName), "Copied from " + source + " on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ".\nThe original folder is left for ModConfig Enhance.\n", new UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>旧版导出的整合包（output\packs、旧布局 exports）不复制：体积大，留在原处，路径写进日志。</summary>
    private static bool SkipDirectory(string relative)
    {
        string p = relative.Replace('\\', '/').Trim('/');
        return p.Equals("output/packs", StringComparison.OrdinalIgnoreCase) || p.Equals("exports", StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyTree(string source, string target, string top, Report report)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
        {
            string to = Path.Combine(target, Path.GetFileName(file));
            if (to.Length - ".copying".Length > MaxPath) { report.Skipped.Add(file + ": path too long, left in place"); continue; }
            File.Copy(file, to, false);
            report.Copied++;
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            string relative = dir.Substring(top.Length).TrimStart('\\', '/');
            if (SkipDirectory(relative)) { report.Skipped.Add(dir + ": old pack exports are not copied; move them yourself if needed"); continue; }
            if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0) { report.Skipped.Add(dir + ": link, not copied"); continue; }
            CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)), top, report);
        }
    }

    private static void CopySigning(string localAppData, Report report)
    {
        if (string.IsNullOrEmpty(localAppData)) return;
        string source = Path.Combine(localAppData, ManagerIdentity.LegacyConfigDirectory, "signing"), target = Path.Combine(localAppData, ManagerIdentity.ConfigDirectory, "signing");
        if (Directory.Exists(target) || !Directory.Exists(source)) return;
        try
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
            report.Signing = true;
        }
        catch (Exception ex) { report.Skipped.Add(source + ": " + ex.Message); }
    }

    private static void RenameRuntime(string bepInExRoot, string configRoot, Report report)
    {
        string source = ManagerIdentity.LegacyRuntime(bepInExRoot), target = ManagerIdentity.Runtime(bepInExRoot);
        if (Directory.Exists(target) || !Directory.Exists(source)) return;
        try { Directory.Move(source, target); report.Runtime = true; }
        catch (Exception ex) { report.Skipped.Add(source + " → " + target + ": " + ex.Message + " (will retry next launch)"); return; }

        string oldFull = Path.GetFullPath(source), newFull = Path.GetFullPath(target);
        foreach (string json in SafeJson(target, skip: new[] { "backups", "downloads", "tools", "layout-backups" }))
        {
            try { report.Rewritten += Rewrite(json, oldFull, newFull); }
            catch (Exception ex) { report.Skipped.Add(json + ": " + ex.Message); }
        }

        // 恢复入口是旧版写的（指向 BepInEx\MCE、MCE.UpdateWorker.exe），已失效；新版在工具缓存就绪后重写自己的入口
        foreach (string stale in new[] { "MCE-Recovery.cmd", "MCE-Recovery.ps1" })
        {
            try { string path = Path.Combine(target, stale); if (File.Exists(path)) File.Move(path, path + ".old"); } catch { }
        }
    }

    /// <summary>可能存着旧绝对路径的 JSON；跳过体积大、不存路径的子目录。</summary>
    private static IEnumerable<string> SafeJson(string root, string[]? skip = null)
    {
        var stack = new Stack<string>(); stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            string[] files, dirs;
            try { files = Directory.GetFiles(dir, "*.json"); dirs = Directory.GetDirectories(dir); } catch { continue; }
            foreach (string file in files) yield return file;
            foreach (string child in dirs)
            {
                if (dir == root && skip != null && skip.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(child).StartsWith(".", StringComparison.Ordinal) && dir != root) continue; // .view-cache、.trash 等缓存
                stack.Push(child);
            }
        }
    }

    /// <summary>
    /// 把 JSON 字符串里的旧绝对路径换成新的，其余字节不动。路径在 JSON 里可能写成 <c>\\</c>（转义反斜杠）或 <c>/</c>；
    /// 旧路径后面必须是 <c>"</c>、<c>\</c> 或 <c>/</c>，避免 <c>…\MCE</c> 误改 <c>…\MCEPack</c>。
    /// </summary>
    internal static int Rewrite(string file, string oldRoot, string newRoot)
    {
        string text;
        try { if (new FileInfo(file).Length > 64 * 1024 * 1024) return 0; text = File.ReadAllText(file, Encoding.UTF8); } catch { return 0; }
        int count = 0;
        string original = text;
        foreach (var (from, to) in new[]
                 {
                     (oldRoot.TrimEnd('\\', '/').Replace("\\", "\\\\"), newRoot.TrimEnd('\\', '/').Replace("\\", "\\\\")),
                     (oldRoot.TrimEnd('\\', '/').Replace('\\', '/'), newRoot.TrimEnd('\\', '/').Replace('\\', '/')),
                 })
        {
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

        if (count == 0 || text == original) return 0;
        string temporary = file + ".split.tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        File.Copy(temporary, file, true);
        File.Delete(temporary);
        return count;
    }
}
