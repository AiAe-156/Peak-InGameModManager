#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ModConfigEnhance.Updating;

/// <summary>Filename recognition only; callers must still read metadata and validate ownership before writing.</summary>
internal static class PluginFileNames
{
    internal static bool IsUpdateArtifact(string path)
    {
        string name = Path.GetFileName(path);
        return Regex.IsMatch(name, @"\.(?:mce-part|mce-candidate)(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool IsAssembly(string path) => !IsUpdateArtifact(path)
        && (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || DisabledOffset(path) > 0);

    /// <summary>Offset of the end of ".dll" in a disabled name. 2.3.4 (user decision): anything after ".dll" counts as a
    /// disabled copy, not only ".dll.&lt;suffix&gt;" — tools and people also write "x.dll1", "x.dllx" or "x.dll_off". The
    /// manager still writes ".d" itself. Metadata decides whether the file is a plugin at all. Known companion files
    /// (x.dll.config / .mdb / .pdb / .xml) are not assemblies.</summary>
    private static readonly string[] Companions = { ".config", ".mdb", ".pdb", ".xml" };
    private static int DisabledOffset(string path)
    {
        string name = Path.GetFileName(path);
        int at = name.LastIndexOf(".dll", StringComparison.OrdinalIgnoreCase);
        if (at <= 0 || at + 4 >= name.Length) return -1;
        string rest = name.Substring(at + 4);
        if (Companions.Any(c => rest.Equals(c, StringComparison.OrdinalIgnoreCase))) return -1;
        return path.Length - name.Length + at + 4;
    }

    internal static string OriginalPath(string path)
    {
        if (!IsAssembly(path) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return path;
        int at = DisabledOffset(path);
        return at > 0 ? path.Substring(0, at) : path;
    }

    internal static bool IsDisabled(string path) => IsAssembly(path) && !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    /// <summary>Suffix this manager writes when it disables a plugin assembly; kept short, BepInEx ignores any *.dll.*.</summary>
    internal const string DisabledSuffix = ".d";
    /// <summary>Suffix earlier releases wrote; still recognised so packages disabled then stay toggleable.</summary>
    internal const string LegacyDisabledSuffix = ".disabled-by-mce";
    internal static bool IsOursDisabled(string path)
    {
        if (!IsDisabled(path)) return false;
        string suffix = path.Substring(OriginalPath(path).Length);
        return suffix.Equals(DisabledSuffix, StringComparison.OrdinalIgnoreCase) || suffix.Equals(LegacyDisabledSuffix, StringComparison.OrdinalIgnoreCase);
    }
    internal static bool IsForeignDisabled(string path)
    {
        if (!IsDisabled(path)) return false;
        string suffix = path.Substring(OriginalPath(path).Length);
        if (suffix.Equals(DisabledSuffix, StringComparison.OrdinalIgnoreCase) || suffix.Equals(LegacyDisabledSuffix, StringComparison.OrdinalIgnoreCase)) return false;
        return suffix.IndexOf(".disabled-by-", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool IsUpdateCandidate(string path) => IsUpdateArtifact(path)
        || Regex.IsMatch(Path.GetFileName(OriginalPath(path)), @"\.__mce_[A-Za-z0-9.+_-]+\.dll$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    internal static string CanonicalPath(string path) => Regex.Replace(OriginalPath(path), @"\.__mce_[A-Za-z0-9.+_-]+\.dll$", ".dll", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Complete physical evidence, including backup companions; never use this alone to select a write target.</summary>
    internal static List<string> ExistingCopies(string original)
    {
        string? directory = Path.GetDirectoryName(original);
        if (string.IsNullOrEmpty(directory)) directory = ".";
        if (!Directory.Exists(directory)) return new List<string>();
        return Directory.GetFiles(directory).Where(p => IsAssembly(p)
            && string.Equals(Path.GetFullPath(OriginalPath(p)), Path.GetFullPath(original), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static bool HasBackupSuffix(string path) => IsDisabled(path);

    /// <summary>Any .dll.* is disabled alone, or a backup when the exact active DLL exists.
    /// Without the active DLL multiple suffixed files remain a genuine conflict.</summary>
    internal static List<string> BackupCompanions(string original)
        => BackupCompanions(original, ExistingCopies(original));

    private static List<string> BackupCompanions(string original, List<string> copies)
    {
        original=Path.GetFullPath(OriginalPath(original));
        string? active=copies.FirstOrDefault(p=>Path.GetFullPath(p).Equals(original,StringComparison.OrdinalIgnoreCase));
        if(active!=null)return copies.Where(p=>!p.Equals(active,StringComparison.OrdinalIgnoreCase)).ToList();
        // With no active DLL all suffixed files are disabled candidates; never pick one silently.
        return new List<string>();
    }

    internal static List<string> ActionableCopies(string original)
    {
        original = Path.GetFullPath(OriginalPath(original));
        var copies = ExistingCopies(original);
        var backups = new HashSet<string>(BackupCompanions(original, copies), StringComparer.OrdinalIgnoreCase);
        return copies.Where(p => !backups.Contains(p)).ToList();
    }

    internal static bool IsBackupCompanion(string path) => IsAssembly(path) && HasBackupSuffix(path)
        && BackupCompanions(Path.GetFullPath(OriginalPath(path))).Any(p =>
            Path.GetFullPath(p).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

}
