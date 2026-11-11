#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace ModConfigEnhance.Overview;

// Stable document IDs are separate from the lifetime of Unity font objects.
internal static class IntroductionFontRegistry
{
    internal const string DefaultId = "default";
    private const string AliasPrefix = "MCEIntroFont_";
    private static readonly Dictionary<string, TMP_FontAsset> GameFonts = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, TMP_FontAsset> OwnedFonts = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> OwnedFontSources = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, TMP_FontAsset> Aliases = new();
    private static readonly Dictionary<string, string> AliasIds = new(StringComparer.Ordinal);
    private static readonly List<TMP_FontAsset> Fallbacks = new();
    private static int _users;
    /// <summary>1.2.4–1.2.5 global folder; still read so earlier imports keep working.</summary>
    internal static string FontsDirectory => Path.Combine(BepInEx.Paths.ConfigPath, ModConfigEnhance.Updating.ManagerIdentity.ConfigDirectory, "fonts");
    /// <summary>The viewed scheme's own <c>fonts/</c> folder.</summary>
    private static string _packFontsDirectory = "";
    internal static string PackFontsDirectory
    {
        get => _packFontsDirectory;
        set
        {
            string directory = string.IsNullOrEmpty(value) ? "" : Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(_packFontsDirectory, directory, StringComparison.OrdinalIgnoreCase)) return;
            // Two schemes can contain distinct fonts with the same persistent file:<name> ID.
            // Release the earlier scheme's runtime objects before the new document is refreshed.
            ClearOwned(); _packFontsDirectory = directory;
        }
    }
    private static IEnumerable<string> Directories() => new[] { PackFontsDirectory, FontsDirectory }.Where(d => d.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase);
    internal static string? FindFontFile(string filename) => FindFontFile(filename, PackFontsDirectory);
    internal static string? FindFontFile(string filename, string profileFontsDirectory)
    {
        if (filename.Length == 0 || filename != Path.GetFileName(filename) || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        foreach (string directory in new[] { profileFontsDirectory, FontsDirectory }.Where(d => d.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.GetFullPath(Path.Combine(directory, filename));
            if (File.Exists(path) && Supported(path) && SafeFile(path, directory)) return path;
        }
        return null;
    }
    /// <summary>Copies a font found only in the legacy folder into the pack, so exports include it.</summary>
    internal static void AdoptIntoPack(string id)
    {
        if (!id.StartsWith("file:", StringComparison.Ordinal) || PackFontsDirectory.Length == 0) return;
        string name = id.Substring(5); string? source = FindFontFile(name); if (source == null) return;
        string target = Path.Combine(PackFontsDirectory, name);
        if (!SafeFile(target, PackFontsDirectory)) throw new IOException(Loc.T("Invalid profile font destination path.", "方案字体目标路径无效。", "方案字體目標路徑無效。"));
        if (File.Exists(target)) return;
        Directory.CreateDirectory(PackFontsDirectory); File.Copy(source, target, false);
    }

    internal static void Acquire() => _users++;
    internal static void Release() { if (_users > 0) _users--; if (_users == 0) ClearOwned(); }
    internal static void Shutdown() { if (_users == 0) ClearOwned(); }

    internal static void ObserveGameFont(TMP_FontAsset? font)
    {
        if (font == null || OwnedFonts.Values.Contains(font)) return;
        if (!Fallbacks.Contains(font)) Fallbacks.Add(font);
        GameFonts[GameId(font)] = font;
        foreach (var owned in OwnedFonts.Values) AddFallbacks(owned);
    }

    internal static IReadOnlyList<(string Label, string Id)> Options()
    {
        foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font == null || OwnedFonts.Values.Contains(font) || font.name.StartsWith(AliasPrefix, StringComparison.Ordinal)) continue;
            GameFonts[GameId(font)] = font;
            // Only inspect existing glyph tables; scanning must not populate native atlases.
            if (font.HasCharacter('中', false, false) && !Fallbacks.Contains(font)) Fallbacks.Add(font);
        }
        foreach (var owned in OwnedFonts.Values) AddFallbacks(owned);
        var options = new List<(string Label, string Id)> { (Loc.T("Default font (Regular Sans)", "默认字体（常规无衬线）", "默認字體（常規無襯線）"), DefaultId) };
        options.AddRange(GameFonts.Where(p => p.Value != null).OrderBy(p => p.Value.name, StringComparer.OrdinalIgnoreCase)
            .Select(p => (Loc.T("Game font: ", "游戏字体：", "游戲字體：") + p.Value.name, p.Key)));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in Directories())
            try
            {
                if (Directory.Exists(directory) && SafeDirectory(directory))
                    foreach (var path in Directory.EnumerateFiles(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                        if (Supported(path) && SafeFile(path, directory) && seen.Add(Path.GetFileName(path))) options.Add((Loc.T("Imported: ", "导入：", "導入：") + Path.GetFileName(path), "file:" + Path.GetFileName(path)));
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[介绍字体] 无法扫描字体目录：" + ex.Message); }
        return options;
    }

    internal static string ShortLabel(string id)
    {
        if (string.IsNullOrEmpty(id) || id == DefaultId) return Loc.T("Default font", "默认字体", "預設字型");
        if (id.StartsWith("game:", StringComparison.Ordinal)) { string name = id.Substring(5); int bar = name.IndexOf('|'); return (bar > 0 ? name.Substring(0, bar) : name).Replace(" SDF", ""); }
        if (id.StartsWith("file:", StringComparison.Ordinal)) return Path.GetFileNameWithoutExtension(id.Substring(5));
        return id;
    }

    internal static void EnsureDirectory()
    {
        string directory = PackFontsDirectory.Length != 0 ? PackFontsDirectory : FontsDirectory;
        if (!SafeDirectory(directory)) throw new IOException(Loc.T("Font directories cannot use directory links or junctions.", "字体目录不能使用目录链接或联接点。", "字體目錄不能使用目錄鏈接或聯接點。"));
        Directory.CreateDirectory(directory);
    }
    internal static string WritableDirectory => PackFontsDirectory.Length != 0 ? PackFontsDirectory : FontsDirectory;

    internal static bool TryResolve(string id, out TMP_FontAsset? font, out string error)
    {
        font = null; error = "";
        id = string.IsNullOrEmpty(id) ? DefaultId : id;
        try
        {
            string? filePath = null;
            if (id.StartsWith("file:", StringComparison.Ordinal))
            {
                string filename = id.Substring(5);
                if (filename.Length == 0 || filename != Path.GetFileName(filename) || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new IOException(Loc.T("Invalid font filename.", "字体文件名无效。", "字體文件名無效。"));
                if (!Supported(filename)) throw new IOException(Loc.T("Only TTF, OTF, and TTC fonts are supported.", "仅支持 TTF、OTF 和 TTC 字体。", "僅支持 TTF、OTF 和 TTC 字體。"));
                filePath = FindFontFile(filename) ?? throw new FileNotFoundException(Loc.T("The font is missing from the profile fonts directory. Restore it there.", "字体文件不在方案 fonts 目录内，请将它放回。", "字體文件不在方案 fonts 目錄內，請將它放回。"), filename);
                if (OwnedFontSources.TryGetValue(id, out var previousSource) && !string.Equals(previousSource, filePath, StringComparison.OrdinalIgnoreCase)) ClearOwned();
            }
            if (OwnedFonts.TryGetValue(id, out font) && font != null) { Register(id, font); return true; }
            if (id == DefaultId)
            {
                font = MceRegularFont.Get(LocalizedBase());
                if (font != null) ObserveGameFont(font);
                if (font == null) font = Fallbacks.FirstOrDefault(f => f != null) ?? GameFonts.Values.FirstOrDefault(f => f != null);
            }
            else if (id.StartsWith("game:", StringComparison.Ordinal))
            {
                if (!GameFonts.TryGetValue(id, out font) || font == null) { Options(); GameFonts.TryGetValue(id, out font); }
            }
            else if (id.StartsWith("file:", StringComparison.Ordinal))
            {
                font = TMP_FontAsset.CreateFontAsset(filePath!, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
                if (font != null) Own(id, font, filePath!);
            }
            if (font == null) { error = Loc.T("The font is unavailable or damaged. The document will use the default font.", "字体不可用或文件损坏；正文将继续使用默认字体。", "字體不可用或文件損壞；正文將繼續使用默認字體。"); return false; }
            if (id != DefaultId && !id.StartsWith("file:", StringComparison.Ordinal)) font = Face(font);
            Register(id, font); return true;
        }
        catch (Exception ex) { font = null; error = Loc.T("Could not load font: ", "无法加载字体：", "無法加載字體：") + ex.Message; return false; }
    }

    private static readonly Dictionary<TMP_FontAsset, TMP_FontAsset> StaticFaces = new();
    private static readonly Dictionary<TMP_FontAsset, TMP_FontAsset> DynamicFaces = new();

    private static TMP_FontAsset? LocalizedBase()
    {
        var swapper = FontFallbackSwapper.instance;
        if (swapper != null && swapper.mainBaseFont != null) return swapper.mainBaseFont;
        return Fallbacks.FirstOrDefault(f => f != null && !f.name.StartsWith(AliasPrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// A game face (e.g. Pangolin) keeps its own fallback chain, which does not reach the Chinese font of
    /// the game's localized UI; Chinese text then rendered as boxes. The introduction uses a private face
    /// whose first fallback is the localized base font. The shared game asset is never modified.
    /// </summary>
    private static TMP_FontAsset Face(TMP_FontAsset game)
    {
        var localized = LocalizedBase();
        if (localized == null || game == localized || game.name.StartsWith(AliasPrefix, StringComparison.Ordinal)) return game;
        if (DynamicFaces.TryGetValue(game, out var face) && face != null) return face;
        if (StaticFaces.TryGetValue(game, out face) && face != null) return face;
        try
        {
            if (game.atlasPopulationMode == AtlasPopulationMode.Dynamic && game.sourceFontFile != null)
            {
                // A dynamic face needs its own atlas: a copy sharing the game's texture would overwrite its glyphs.
                face = TMP_FontAsset.CreateFontAsset(game.sourceFontFile, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (face != null) { face.name = AliasPrefix + "face_" + game.name; face.normalStyle = 0; face.boldStyle = .16f; DynamicFaces[game] = face; }
            }
            if (face == null)
            {
                // Static copy: shares the game's atlas and material read-only, so it is never destroyed
                // (TMP_FontAsset.OnDestroy would destroy those shared resources).
                face = UnityEngine.Object.Instantiate(game);
                face.name = AliasPrefix + "face_" + game.name; face.m_HashCode = 0; face.m_InstanceID = 0;
                face.atlasPopulationMode = AtlasPopulationMode.Static;
                UnityEngine.Object.DontDestroyOnLoad(face);
                StaticFaces[game] = face;
            }
            var chain = new List<TMP_FontAsset> { localized };
            if (game.fallbackFontAssetTable != null) foreach (var f in game.fallbackFontAssetTable) if (f != null && !chain.Contains(f)) chain.Add(f);
            face.normalStyle = 0; face.boldStyle = .16f;
            // This is a private face; the original game's font asset and material remain untouched.
            face.fallbackFontAssetTable = chain;
            face.ReadFontAssetDefinition();
            Plugin.Log.LogInfo("[介绍字体] " + game.name + "（" + game.atlasPopulationMode + "）回退链：" + string.Join(" → ", chain.Select(f => f.name)));
            return face;
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[介绍字体] 无法为 " + game.name + " 建立中文回退：" + ex.Message); return game; }
    }

    // IDs persist in Markdown; compact deterministic names fit TMP's 128-character tag buffer.
    internal static string RegistryName(string id)
    {
        id = string.IsNullOrEmpty(id) ? DefaultId : id;
        string name = NameFor(id); AliasIds[name] = id;
        if (!TryResolve(id, out var font, out _) && TryResolve(DefaultId, out font, out _) && font != null) Register(id, font);
        return name;
    }
    internal static string StableId(string name)
    {
        name = name.Trim('"');
        return AliasIds.TryGetValue(name, out var id) ? id : DefaultId;
    }
    internal static void EnsureRichFonts(string rich)
    {
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(rich, @"<font=""?([^"">]+)""?>"))
        {
            if (!AliasIds.TryGetValue(match.Groups[1].Value, out var id)) continue;
            RegistryName(id);
        }
    }
    private static string NameFor(string id)
    {
        // Persistent IDs remain portable; private TMP names belong to the viewed scheme.
        string key = id.StartsWith("file:", StringComparison.Ordinal) ? WritableDirectory.ToUpperInvariant() + "\n" + id : id;
        using var hash = SHA256.Create();
        return AliasPrefix + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").Substring(0, 32);
    }
    private static string GameId(TMP_FontAsset font) => "game:" + font.name + "|" + font.faceInfo.familyName + "|" + font.faceInfo.styleName;
    internal static bool Supported(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".ttc", StringComparison.OrdinalIgnoreCase);
    }
    private static bool SafeDirectory(string directory)
    {
        // Check every existing ancestor: links can otherwise redirect an innocent-looking filename.
        for (var dir = new DirectoryInfo(Path.GetFullPath(directory)); dir != null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
    private static bool SafeFile(string path, string directory)
    {
        string full = Path.GetFullPath(path), root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetDirectoryName(full), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            && SafeDirectory(directory) && (!File.Exists(full) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0);
    }
    private static void Own(string id, TMP_FontAsset font, string source)
    {
        font.name = NameFor(id);
        font.normalStyle = 0; font.boldStyle = .16f; font.isMultiAtlasTexturesEnabled = true;
        font.ReadFontAssetDefinition(); OwnedFonts[id] = font; OwnedFontSources[id] = source; AddFallbacks(font);
    }
    private static void AddFallbacks(TMP_FontAsset font)
    {
        font.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
        // TMP performs glyph-level lookup; imported Latin glyphs stay in the imported face.
        foreach (var fallback in Fallbacks.Where(f => f != null && f != font))
            if (!font.fallbackFontAssetTable.Contains(fallback)) font.fallbackFontAssetTable.Add(fallback);
        var settings = TMP_Settings.fallbackFontAssets;
        if (settings != null) foreach (var fallback in settings.Where(f => f != null && f != font))
            if (!font.fallbackFontAssetTable.Contains(fallback)) font.fallbackFontAssetTable.Add(fallback);
    }
    private static void Register(string id, TMP_FontAsset font)
    {
        string name = NameFor(id); AliasIds[name] = id;
        int hash = TMP_TextUtilities.GetHashCode(name);
        // The game's own name/hash/material are left intact. The publicized runtime exposes
        // the native TMP lookup so aliases can also be removed when their owners are freed.
        MaterialReferenceManager.instance.m_FontAssetReferenceLookup[hash] = font;
        Aliases[hash] = font;
    }
    private static void ClearOwned()
    {
        var manager = MaterialReferenceManager.instance;
        foreach (var alias in Aliases)
            if (manager.m_FontAssetReferenceLookup.TryGetValue(alias.Key, out var current) && current == alias.Value) manager.m_FontAssetReferenceLookup.Remove(alias.Key);
        Aliases.Clear();
        // 只释放 fonts 目录里用户自带的字体。DynamicFaces / StaticFaces 是从游戏字体派生的，
        // 和方案字体目录毫无关系，一起销毁会让每次切换方案都重建一份 1024×1024 动态图集。
        foreach (var font in OwnedFonts.Values.Where(f => f != null).Distinct())
        {
            if (TMP_ResourceManager.s_FontAssetNameReferenceLookup.TryGetValue(font.hashCode, out var named) && named == font) TMP_ResourceManager.s_FontAssetNameReferenceLookup.Remove(font.hashCode);
            long familyKey = ((long)font.styleNameHashCode << 32) | (uint)font.familyNameHashCode;
            if (TMP_ResourceManager.s_FontAssetFamilyNameAndStyleReferenceLookup.TryGetValue(familyKey, out var family) && family == font) TMP_ResourceManager.s_FontAssetFamilyNameAndStyleReferenceLookup.Remove(familyKey);
            TMP_ResourceManager.s_FontAssetReferences.Remove(font.instanceID);
            // Native TMP OnDestroy releases this font's atlas textures and material together.
            UnityEngine.Object.Destroy(font);
        }
        OwnedFonts.Clear();
        OwnedFontSources.Clear();
        Fallbacks.RemoveAll(f => f == null);
    }
}
