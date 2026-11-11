using System.IO.Compression;

namespace MCE.UpdateWorker;

/// <summary>Package archive handling for the install core: BepInEx-relative routing plus a short protected list.</summary>
internal static class InstallArchive
{
    static readonly string[] ProtectedNames = { "BepInEx.dll", "BepInEx.Preloader.dll", "winhttp.dll", "version.dll", "doorstop_config.ini", "Assembly-CSharp.dll" };
    static readonly string[] ScriptExtensions = { ".bat", ".cmd", ".ps1", ".hta", ".wsf", ".vbs", ".js", ".lnk", ".url" };
    static readonly string[] Areas = { "plugins", "patchers", "core", "config" };

    internal static bool Protected(string relative)
    {
        string path = relative.Replace('\\', '/');
        string lower = path.ToLowerInvariant();
        string leaf = Path.GetFileName(path);
        if (ProtectedNames.Any(n => leaf.Equals(n, StringComparison.OrdinalIgnoreCase))) return true;
        if (lower.StartsWith("peak_data/") || lower.Contains("/peak_data/")) return true;
        if (lower.StartsWith("mce/") || lower.StartsWith("plugins/mce/")) return true;
        if (ScriptExtensions.Any(lower.EndsWith)) return true;
        if (lower.EndsWith(".exe") && !lower.Contains("/tools/updater/")) return true;
        return false;
    }

    internal static string Area(string relative) => relative.Replace('\\', '/').Split('/')[0].ToLowerInvariant();

    /// <summary>plugins/… patchers/… core/… config/… stay as they are (after an optional BepInEx/ prefix); anything else lives under plugins/.</summary>
    internal static string Normalize(string entry)
    {
        string p = entry.Replace('\\', '/').TrimStart('/');
        if (p.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) p = p.Substring(8);
        string first = p.Split('/')[0];
        return Areas.Any(a => a.Equals(first, StringComparison.OrdinalIgnoreCase)) ? p : "plugins/" + p;
    }

    /// <summary>Extracts into <paramref name="target"/> and returns normalized relative path → SHA-256.</summary>
    internal static Dictionary<string, string> Extract(string zip, string target)
    {
        Directory.CreateDirectory(target);
        var result = new Dictionary<string, string>(Files.Compare);
        using var archive = ZipFile.OpenRead(zip);
        long total = 0;
        if (archive.Entries.Count > 30000) throw new IOException("压缩包文件数超过限额。");
        foreach (var entry in archive.Entries)
        {
            string raw = entry.FullName.Replace('\\', '/');
            if (raw.EndsWith("/") || entry.Name.Length == 0) continue;
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0) throw new IOException("压缩包包含链接：" + raw);
            string relative = Normalize(raw);
            if (Protected(relative)) throw new IOException("压缩包含受保护文件，不能自动安装：" + relative);
            if (result.ContainsKey(relative)) throw new IOException("压缩包目标路径重复：" + relative);
            total += entry.Length;
            if (entry.Length > 512L * 1024 * 1024 || total > 1536L * 1024 * 1024) throw new IOException("压缩包解压大小超过限额。");
            string file = Files.Child(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            using (var input = entry.Open())
            using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            result[relative] = Files.Hash(file);
        }
        WorkerLog.Debug("extracted " + result.Count + " files from " + Path.GetFileName(zip) + " (" + (total / 1024) + " KB)");
        return result;
    }

    /// <summary>A staged BepInEx-relative tree (local import content) as normalized relative path → SHA-256.</summary>
    internal static Dictionary<string, string> InventoryTree(string directory)
    {
        var result = new Dictionary<string, string>(Files.Compare);
        foreach (string file in Walk(directory))
        {
            string relative = Normalize(Path.GetRelativePath(directory, file));
            if (Protected(relative)) throw new IOException("本地包含受保护文件，不能自动安装：" + relative);
            result[relative] = Files.Hash(file);
        }
        return result;
    }

    /// <summary>All files below a directory, never following reparse points and never throwing on unreadable children.</summary>
    internal static IEnumerable<string> Walk(string directory)
    {
        if (!Directory.Exists(directory)) yield break;
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            string[] files, children;
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0 && !Files.Compare.Equals(current, directory)) continue;
                files = Directory.GetFiles(current); children = Directory.GetDirectories(current);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (string file in files) yield return file;
            foreach (string child in children) pending.Push(child);
        }
    }

    internal static void TryDeleteDirectory(string directory)
    {
        try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void TryDeleteFile(string file)
    {
        try { if (File.Exists(file)) { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); } }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
