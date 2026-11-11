#nullable enable
using System;
using System.IO;
using BepInEx;

namespace ModConfigEnhance.Overview;

internal static class InstallationPaths
{
    // The game folder is not the parent of BepInEx: a manager profile (r2modman/TSMM) keeps its own
    // BepInEx outside the installation, so only BepInEx's own GameRootPath answers this question.
    internal static bool AtGameRoot(string path)
    {
        if (string.IsNullOrEmpty(Paths.GameRootPath)) return false;
        string game = Path.GetFullPath(Paths.GameRootPath);
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(game.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }
}
