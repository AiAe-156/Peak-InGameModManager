#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ModConfigEnhance.Overview;

/// <summary>Imports content-addressed media into one scheme without cleaning up its older files.</summary>
internal static class OverviewProfileAssets
{
    internal const long FileLimitBytes = 1024L * 1024 * 1024;
    internal readonly struct ImportResult
    {
        internal readonly string RelativePath;
        internal readonly bool Reused;
        internal ImportResult(string relativePath, bool reused) { RelativePath = relativePath; Reused = reused; }
    }

    internal static Task<ImportResult> ImportMediaAsync(string source, string profileDirectory, long limitBytes, CancellationToken cancellation) =>
        ImportAsync(source, profileDirectory, "media", SupportedMedia, limitBytes, cancellation);

    internal static Task<ImportResult> ImportFontAsync(string source, string profileDirectory, long limitBytes, CancellationToken cancellation) =>
        ImportAsync(source, profileDirectory, "fonts", IntroductionFontRegistry.Supported, limitBytes, cancellation);

    internal static bool SupportedMedia(string path) => new[] { ".png", ".jpg", ".jpeg", ".gif", ".mp4", ".webm", ".mov", ".avi", ".m4v", ".ogv" }
        .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static async Task<ImportResult> ImportAsync(string source, string profileDirectory, string folder, Func<string, bool> supported, long limitBytes, CancellationToken cancellation)
    {
        if (limitBytes <= 0) throw new ArgumentOutOfRangeException(nameof(limitBytes));
        limitBytes = Math.Min(limitBytes, FileLimitBytes);
        cancellation.ThrowIfCancellationRequested();
        source = source.Trim().Trim('"');
        var file = new FileInfo(source);
        if (!file.Exists) throw new FileNotFoundException(Loc.T("File does not exist.", "文件不存在。", "文件不存在。"), source);
        if (file.Length > limitBytes) throw new IOException(Loc.T("The file exceeds the 1 GB import limit.", "单文件超过1GB导入上限。", "單文件超過1GB導入上限。"));
        string extension = file.Extension.ToLowerInvariant();
        if (!supported(source)) throw new IOException(folder == "fonts" ? Loc.T("Only TTF, OTF, and TTC fonts are supported.", "仅支持 TTF、OTF 和 TTC 字体。", "僅支持 TTF、OTF 和 TTC 字體。") : Loc.T("Supported formats: PNG, JPEG, GIF, MP4, WebM, MOV, AVI, M4V, and OGV.", "仅支持 PNG、JPEG、GIF 及现有视频容器 MP4、WebM、MOV、AVI、M4V、OGV。", "僅支持 PNG、JPEG、GIF 及現有視頻容器 MP4、WebM、MOV、AVI、M4V、OGV。"));
        string resourceDirectory = CheckedPath(profileDirectory, folder);
        Directory.CreateDirectory(resourceDirectory);

        // Hash before creating a copy: repeated imports read the source but never create duplicate media.
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        string hash = await HashAsync(input, limitBytes, cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        string relative = folder + "/" + hash + extension;
        string target = CheckedPath(profileDirectory, relative);
        if (File.Exists(target))
        {
            using var existing = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (!string.Equals(hash, await HashAsync(existing, limitBytes, cancellation).ConfigureAwait(false), StringComparison.Ordinal))
                throw new IOException(Loc.T("An asset with the same name has different content. Keep the original and check the asset directory.", "同名素材文件内容不一致，请保留原文件并检查素材目录。", "同名素材文件內容不一致，請保留原文件并檢查素材目錄。"));
            return new ImportResult(relative, true);
        }

        // Earlier imports and manually supplied fonts can have ordinary names. Compare only
        // same-sized supported files in this scheme; never touch backups or another scheme.
        foreach (string candidate in OverviewRepository.SafeFiles(resourceDirectory))
        {
            cancellation.ThrowIfCancellationRequested();
            // Document font IDs are file:<name>, so a nested legacy file cannot be reused by ID.
            if (folder == "fonts" && !string.Equals(Path.GetDirectoryName(candidate), resourceDirectory, StringComparison.OrdinalIgnoreCase)) continue;
            if (!supported(candidate) || new FileInfo(candidate).Length != input.Length) continue;
            string candidateRelative = candidate.Substring(Path.GetFullPath(profileDirectory).TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
            string existingPath = CheckedPath(profileDirectory, candidateRelative);
            using var existing = new FileStream(existingPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            if (string.Equals(hash, await HashAsync(existing, limitBytes, cancellation).ConfigureAwait(false), StringComparison.Ordinal))
                return new ImportResult(candidateRelative, true);
        }

        string partial = CheckedPath(profileDirectory, relative + "." + Guid.NewGuid().ToString("N") + ".partial");
        input.Position = 0;
        using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
        {
            using var copyHash = SHA256.Create();
            var buffer = new byte[81920]; long total = 0; int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0)
            {
                total += read;
                if (total > limitBytes) throw new IOException(Loc.T("The file exceeds the 1 GB import limit.", "单文件超过1GB导入上限。", "單文件超過1GB導入上限。"));
                copyHash.TransformBlock(buffer, 0, read, buffer, 0);
                await output.WriteAsync(buffer, 0, read, cancellation).ConfigureAwait(false);
            }
            copyHash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            if (!string.Equals(hash, Hex(copyHash.Hash!), StringComparison.Ordinal)) throw new IOException(Loc.T("The source asset changed during import. The incomplete copy was kept.", "导入期间源素材发生变化，未完成的副本已保留。", "導入期間源素材發生變化，未完成的副本已保留。"));
        }
        cancellation.ThrowIfCancellationRequested();
        // No overwrite and no automatic deletion, including an interrupted import's .partial file.
        target = CheckedPath(profileDirectory, relative);
        File.Move(partial, target);
        return new ImportResult(relative, false);
    }

    private static async Task<string> HashAsync(Stream stream, long limitBytes, CancellationToken cancellation)
    {
        using var hash = SHA256.Create();
        var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > limitBytes) throw new IOException(Loc.T("The file exceeds the 1 GB import limit.", "单文件超过1GB导入上限。", "單文件超過1GB導入上限。"));
            hash.TransformBlock(buffer, 0, read, buffer, 0);
        }
        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Hex(hash.Hash!);
    }

    private static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

    internal static string CheckedPath(string root, string relative)
    {
        if (Array.IndexOf(relative.Replace('\\', '/').Split('/'), "..") >= 0) throw new IOException(Loc.F("Profile-relative paths cannot contain parent directories: {0}", "方案内相对路径不能包含上级目录：{0}", "方案內相對路徑不能包含上級目錄：{0}", relative));
        // BepInEx already selected this installation. Steam library aliases above it
        // are valid; check profile/export directories only up to that trusted root.
        for (var directory = new DirectoryInfo(Path.GetFullPath(root)); directory != null; directory = directory.Parent)
        {
            if (InstallationPaths.AtGameRoot(directory.FullName)) break;
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException(Loc.T("Profile or export directories cannot use directory links or junctions.", "方案或导出目录不能使用目录链接或联接点。", "方案或導出目錄不能使用目錄鏈接或聯接點。") + "\n" + directory.FullName);
        }
        string path = OverviewRepository.SafePath(root, relative);
        if (path.Length == 0) throw new IOException(Loc.F("Invalid profile-relative path: {0}", "方案内相对路径无效：{0}", "方案內相對路徑無效：{0}", relative));
        return path;
    }
}
