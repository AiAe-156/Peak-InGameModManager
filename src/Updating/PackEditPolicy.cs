#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ModConfigEnhance.Updating;

/// <summary>Shared editor/worker allowlist. Resource additions belong to an existing Markdown document.</summary>
internal static class PackEditPolicy
{
    internal const long TextLimit = 2L * 1024 * 1024;
    internal const long AssetLimit = 1024L * 1024 * 1024;
    internal static bool IsText(string path) => new[] { ".md", ".txt", ".cfg", ".ini", ".json", ".xml", ".yaml", ".yml", ".toml", ".csv" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    internal static bool CanEdit(string path) => IsText(path) && !(Path.GetFileName(path).ToLowerInvariant() is "signature.json" or "release.json" or "manifest.json" or "profiles.json" or "profile.json" or "config-values.json");
    internal static bool IsAsset(string path) => new[] { ".png", ".jpg", ".jpeg", ".gif", ".mp4", ".webm", ".mov", ".avi", ".m4v", ".ogv", ".ttf", ".otf", ".ttc" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    internal static bool AssetForDocument(string asset, string document, string text)
    {
        asset = asset.Replace('\\', '/'); document = document.Replace('\\', '/');
        string directory = document.Substring(0, document.LastIndexOf('/') + 1);
        if (!asset.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) return false;
        string local = asset.Substring(directory.Length);
        bool font = new[] { ".ttf", ".otf", ".ttc" }.Contains(Path.GetExtension(asset).ToLowerInvariant());
        if (!IsAsset(asset) || !local.StartsWith(font ? "fonts/" : "media/", StringComparison.OrdinalIgnoreCase) || local.Contains("../")) return false;
        // Match actual Markdown image destinations / MCE media metadata / font attributes, not arbitrary prose.
        foreach (Match match in Regex.Matches(text, @"!\[[^\]]*\]\(<?([^\s)>]+)>?(?:\s+[^)]*)?\)|""Media""\s*:\s*""([^""]+)""|data-mce-font=""file:([^""]+)""", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
        {
            string reference = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : "fonts/" + match.Groups[3].Value;
            try { reference = Uri.UnescapeDataString(reference).Replace('\\', '/'); } catch (UriFormatException) { continue; }
            if (string.Equals(reference, local, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
