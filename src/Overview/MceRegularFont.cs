#nullable enable
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
namespace ModConfigEnhance.Overview;

/// <summary>Private regular Latin face with the game's localized glyph fallback. Shared game assets are read-only.</summary>
internal static class MceRegularFont
{
    private static TMP_FontAsset? _font;
    private static Font? _source;
    private static bool _failed;
    internal static TMP_FontAsset? Get(TMP_FontAsset? fallback = null)
    {
        if (_font == null && !_failed)
        {
            try
            {
                _source = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Segoe UI", "Noto Sans" }, 32);
                if (_source == null) throw new InvalidOperationException("No regular system font is available.");
                _font = TMP_FontAsset.CreateFontAsset(_source, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (_font == null) throw new InvalidOperationException("Could not create regular text font.");
                _font.name = "MCE Regular Sans"; _font.normalStyle = 0; _font.boldStyle = .16f; _font.isMultiAtlasTexturesEnabled = true;
                _font.fallbackFontAssetTable = new List<TMP_FontAsset>();
                UnityEngine.Object.DontDestroyOnLoad(_source); UnityEngine.Object.DontDestroyOnLoad(_font);
            }
            catch (Exception e) { _failed = true; Plugin.Log.LogWarning("[MCE font] Regular font unavailable: " + e.Message); }
        }
        var localized = FontFallbackSwapper.instance != null ? FontFallbackSwapper.instance.mainBaseFont : fallback;
        if (_font != null && localized != null && localized != _font)
        {
            // Update only our private fallback list when the interface language changes.
            if (_font.fallbackFontAssetTable.Count == 0 || _font.fallbackFontAssetTable[0] != localized)
                _font.fallbackFontAssetTable = new List<TMP_FontAsset> { localized };
        }
        return _font ?? fallback;
    }
    internal static void ApplyDefault(TextMeshProUGUI text)
    {
        var regular = Get(text.font); if (regular == null) return;
        text.font = regular; text.fontSharedMaterial = regular.material;
    }
}
