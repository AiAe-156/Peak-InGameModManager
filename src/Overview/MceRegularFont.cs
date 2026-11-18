#nullable enable
using TMPro;
namespace ModConfigEnhance.Overview;

/// <summary>Reuse the game's localized font. Weight adjustments remain on private materials in MceTextWeight.</summary>
internal static class MceRegularFont
{
    internal static TMP_FontAsset? Get(TMP_FontAsset? fallback = null)
    {
        var swapper = FontFallbackSwapper.instance;
        if (swapper != null && swapper.mainBaseFont != null) return swapper.mainBaseFont;
        return fallback;
    }
    internal static void ApplyDefault(TextMeshProUGUI text)
    {
        var regular = Get(text.font); if (regular == null) return;
        text.font = regular; text.fontSharedMaterial = regular.material;
    }
}
