#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ModConfigEnhance.Updating;
namespace ModConfigEnhance.Overview;
internal static partial class ModSources
{
    private static string _ledgerPath = "";
    private static long _ledgerLength = -1, _ledgerTicks = -1;
    private static ShopLedger? _installedLedger;
    private static ShopLedger? InstalledLedger()
    {
        string path = PackLibrary.Managed ? Path.Combine(PackLibrary.Workspace, "shop-installed.json")
            : Path.Combine(BepInEx.Paths.BepInExRootPath, "MCE", "shop", "installed-unmanaged.json");
        lock (Gate)
        {
            try {
                var file = new FileInfo(path);
                if (!file.Exists) return null;
                if (_ledgerPath == path && _ledgerLength == file.Length && _ledgerTicks == file.LastWriteTimeUtc.Ticks) return _installedLedger;
                _installedLedger = JsonConvert.DeserializeObject<ShopLedger>(File.ReadAllText(path));
                _ledgerPath = path; _ledgerLength = file.Length; _ledgerTicks = file.LastWriteTimeUtc.Ticks;
                return _installedLedger;
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
        }
    }
    private static bool TryInstalledStore(OverviewMod mod)
    {
        var package = ReadInstalledStore(mod); if (package == null) return false;
        int split = package.Key.IndexOf('-');
        mod.SourceNamespace = package.Key.Substring(0, split); mod.SourceName = package.Key.Substring(split + 1);
        mod.PackageVersion = package.Version; mod.SourceConfirmed = true; mod.SourceKind = ModSourceKind.Store;
        return true;
    }
    internal static bool HasInstalledStore(OverviewMod mod) => ReadInstalledStore(mod) != null;
    private static ShopPackage? ReadInstalledStore(OverviewMod mod)
    {
        if (mod.Location.Length == 0 || !File.Exists(mod.Location)) return null;
        var ledger = InstalledLedger(); if (ledger == null) return null;
        string root = Path.GetFullPath(BepInEx.Paths.BepInExRootPath).TrimEnd('\\','/') + Path.DirectorySeparatorChar;
        string own = Path.GetFullPath(mod.Location);
        if (!own.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        string relative = own.Substring(root.Length).Replace('\\','/');
        var matches = (ledger.Packages ?? new List<ShopPackage>()).Where(p => p != null && !p.External && !p.VerificationRequired && !string.IsNullOrEmpty(p.Url)
            && ValidStoreDownload(p.Key, p.Version, p.Url)
            && p.Files != null && p.Files.Any(f => f != null && f.Path != null && f.Path.Replace('\\','/').Equals(relative, StringComparison.OrdinalIgnoreCase))).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    internal static bool ValidStoreDownload(string key, string version, string url)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(version)) return false;
        int split = key.IndexOf('-'); if (split <= 0 || !ThunderstoreUpdateService.ValidPart(key.Substring(0, split))
            || !ThunderstoreUpdateService.ValidPart(key.Substring(split + 1)) || !Version.TryParse(version, out _)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.Host.Equals("thunderstore.io", StringComparison.OrdinalIgnoreCase)) return false;
        string expected = "/package/download/" + key.Substring(0, split) + "/" + key.Substring(split + 1) + "/" + version + "/";
        return uri.AbsolutePath.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }
}
