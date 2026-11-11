using System;
using System.Linq;
namespace ModConfigEnhance.Overview;
internal static class ModNameLayout
{
    internal static bool Equivalent(string left, string right)
    {
        string Normalize(string value) => new string(value.Where(c => !char.IsWhiteSpace(c) && c != '_' && c != '-' && c != '.').ToArray()).ToUpperInvariant();
        return Normalize(left) == Normalize(right);
    }
    /// <summary>2.3.6: wider (27% of the table, up to 370 before the 1.2 factor, was 23% / 310) so long translated names
    /// fit beside their chips; the description column takes what is left and is the one that gives way.</summary>
    internal static float ColumnWidth(float tableWidth) => Math.Max(150, Math.Min(370, tableWidth * .27f)) * 1.2f;
    internal static bool ShowOriginal(float available, float primaryWidth, float originalWidth) => primaryWidth + 8 + originalWidth <= available;
}
