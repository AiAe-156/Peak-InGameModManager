using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ModConfigEnhance.Updating;

namespace MCE.UpdateWorker;

internal static class Files
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    internal static readonly StringComparer Compare = StringComparer.OrdinalIgnoreCase;
    /// <summary>Real game installation root (holds winhttp.dll, doorstop_config.ini and PEAK_Data), passed as
    /// --game-root. In a manager-profile layout the BepInEx root lives outside the game folder, so the parent of
    /// --root is the profile directory and must never stand in for the game root.</summary>
    internal static string GameRoot = "";
    internal static string GameRootFor(string bepinExRoot) => GameRoot.Length > 0 ? GameRoot : Directory.GetParent(bepinExRoot)!.FullName;
    internal static T Read<T>(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<T>(input, Json) ?? throw new IOException("文件内容为空：" + path);
    }
    internal static void Save<T>(string path, T value)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value, Json); stream.Flush(true); }
            // Progress readers / AV scanners can briefly deny rename-overwrite. Keep the old journal intact and retry the atomic commit.
            for (int attempt = 0; ; attempt++)
            {
                try { if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); break; }
                catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < 7)
                { System.Threading.Thread.Sleep(Math.Min(400, 25 * (attempt + 1))); }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new IOException("Transaction record write failed / 写入事务记录失败：" + path + "\nTemporary / 临时文件：" + temp + "\n" + e.Message + " (0x" + e.HResult.ToString("X8") + ")", e); }
    }
    internal static string Hash(string path)
    {
        try { using var stream = File.OpenRead(path); return FrameworkCompat.Hex(FrameworkCompat.HashData(stream)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new IOException("File read failed / 读取文件失败：" + path + "\n" + e.Message + " (0x" + e.HResult.ToString("X8") + ")", e); }
    }
    internal static bool Within(string parent, string child) => Path.GetFullPath(child).StartsWith(Path.GetFullPath(parent).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static string PhysicalPath(string path)
    {
        string result = Path.GetFullPath(path);
        // Comparison only: installation aliases above the game root remain trusted,
        // while write paths still pass RejectLinks within the installation boundary.
        for (int pass = 0; pass < 8; pass++)
        {
            bool resolved = false;
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(result)!); directory != null; directory = directory.Parent)
            {
                if (!directory.Exists || (directory.Attributes & FileAttributes.ReparsePoint) == 0) continue;
                var target = FrameworkCompat.ResolveDirectory(directory);
                if (target == null) continue;
                result = Path.GetFullPath(Path.Combine(target.FullName, Path.GetRelativePath(directory.FullName, result))); resolved = true; break;
            }
            if (!resolved) break;
        }
        return result;
    }
    internal static string PhysicalDirectory(string directory) => Path.GetDirectoryName(PhysicalPath(Path.Combine(directory, ".mce-boundary")))!;
    internal static string Child(string root, string relative)
    {
        relative = relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Split(Path.DirectorySeparatorChar).Any(s => s is "" or "." or ".." || s.EndsWith(' ') || s.EndsWith('.')))
            throw new IOException("包内路径不安全：" + relative);
        string result = Path.GetFullPath(Path.Combine(root, relative));
        if (!Within(root, result)) throw new IOException("文件路径越界。");
        return result;
    }
    internal static void RejectLinks(string root, string path)
    {
        if (!Compare.Equals(Path.GetFullPath(root), Path.GetFullPath(path)) && !Within(root, path)) throw new IOException("路径超出当前游戏目录。");
        for (string? current = path; current != null && (Compare.Equals(current, root) || Within(root, current)); current = Path.GetDirectoryName(current))
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("当前游戏目录内部包含链接：" + current);
    }
    internal static Dictionary<string, string> Inventory(string root)
    {
        var result = new Dictionary<string, string>(Compare);
        if (!Directory.Exists(root)) return result;
        var directories = new Stack<string>(); directories.Push(root);
        while (directories.TryPop(out var directory))
        {
            RejectLinks(root, directory);
            foreach (string file in Directory.GetFiles(directory)) { RejectLinks(root, file); result.Add(Path.GetRelativePath(root, file), Hash(file)); }
            foreach (string sub in Directory.GetDirectories(directory)) directories.Push(sub);
        }
        return result;
    }
    internal static void MoveAside(string file, string destination)
    {
        if (!File.Exists(file)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination)) destination += "." + Guid.NewGuid().ToString("N");
            File.Move(file, destination);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new IOException("移动文件失败：\n源：" + file + "\n目标：" + destination + "\n" + e.Message + " (0x" + e.HResult.ToString("X8") + ")", e); }
    }
    internal static void CopyAtomic(string source, string target)
    {
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".mce-part";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, temp, false);
            // File.Copy inherits readonly; only the newly created temporary copy needs writable access for Flush.
            File.SetAttributes(temp, File.GetAttributes(temp) & ~FileAttributes.ReadOnly);
            using (var stream = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) stream.Flush(true);
            File.Move(temp, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temp)) { File.SetAttributes(temp, FileAttributes.Normal); File.Delete(temp); } } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw new IOException("复制文件失败：\n源：" + source + "\n目标：" + target + "\n" + e.Message + " (0x" + e.HResult.ToString("X8") + ")", e);
        }
    }
    internal static int CompareVersions(string a, string b)
    {
        if (!Version.TryParse(a, out var av) || !Version.TryParse(b, out var bv)) throw new IOException("版本号格式不支持自动更新：" + a + " / " + b);
        return av.CompareTo(bv);
    }
    internal static bool ProcessAlive(int pid)
    {
        if (pid <= 0) return false;
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; } catch (ArgumentException) { return false; }
    }
    internal static bool GameRunning(UpdateJob job)
    {
        if (RunningProbe != null) return RunningProbe(job);
        if (ProcessAlive(job.SourceProcessId)) return true;
        return PeakRunning();
    }
    /// <summary>True when a PEAK process exists at all, regardless of which launch recorded the job.</summary>
    internal static bool PeakRunning()
    {
        var processes = Process.GetProcessesByName("PEAK");
        try { return processes.Length != 0; } finally { foreach (var process in processes) process.Dispose(); }
    }
    // Only used by the isolated checks; production has no CLI bypass for process guards.
    internal static Func<UpdateJob, bool>? RunningProbe = null;
    /// <summary>The manager profile root governing this installation, or empty for a plain local one. A manager
    /// keeps mods.yml beside BepInEx, so only those two places are inspected and no unrelated ancestor counts.</summary>
    internal static string ManagerProfileRoot(string root)
    {
        string full = Path.GetFullPath(root).TrimEnd('\\', '/');
        var parent = Directory.GetParent(full);
        if (parent != null && File.Exists(Path.Combine(parent.FullName, "mods.yml"))) return parent.FullName;
        return File.Exists(Path.Combine(full, "mods.yml")) ? full : "";
    }
    internal static void ValidateRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(Path.Combine(root, "plugins"))) throw new IOException("--root 必须是包含 plugins 的 BepInEx 绝对路径。");
        string boundary = Directory.GetParent(root)!.FullName;
        RejectLinks(boundary, root);
    }
    internal static void ValidateJob(string root, UpdateJob job)
    {
        ValidateRoot(root);
        if (!UpdateWritePolicy.PackageRootAllowed(root, job.PackageRoot)) throw new IOException("模组必须位于独立插件目录或 BepInEx 合法临时卸载目录。");
        RejectLinks(Directory.GetParent(root)!.FullName, job.PackageRoot);
    }
}
