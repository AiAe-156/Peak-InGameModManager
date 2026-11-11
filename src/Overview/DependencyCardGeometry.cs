#nullable enable
using System;
using System.Text;

namespace ModConfigEnhance.Overview;

/// <summary>Local canvas units, never screen pixels. Shared by both dependency-card providers.</summary>
internal static class DependencyCardGeometry
{
    internal readonly struct Box
    {
        internal readonly float X, Y, Width, Height;
        internal Box(float x, float y, float width, float height) { X=x; Y=y; Width=width; Height=height; }
    }
    private static float Clamp(float v, float min, float max) => Math.Max(min, Math.Min(max, v));
    internal static Box Position(float left, float top, float width, float height, float pointerX, float pointerY, float wantedWidth, float wantedHeight)
    {
        float pad = Math.Min(8, Math.Max(0, Math.Min(width, height) / 4));
        float w = Clamp(wantedWidth, 0, Math.Max(0, width - 2 * pad));
        float h = Clamp(wantedHeight, 0, Math.Max(0, height - 2 * pad));
        float x = pointerX + 18;
        if (x + w > left + width - pad) x = pointerX - w - 12;
        return new Box(Clamp(x, left + pad, left + width - pad - w),
            Clamp(pointerY + 18, top + pad, top + height - pad - h), w, h);
    }
    // TMP normally wraps words; explicit break opportunities also cover path/GUID tokens.
    // Never insert characters into rich-text tags or entities.
    internal static string WrapTokens(string text)
    {
        var result = new StringBuilder(text.Length); int run = 0; bool tag = false, entity = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<') { tag = true; run = 0; }
            if (!tag && c == '&') entity = true;
            result.Append(c);
            if (tag) { if (c == '>') tag = false; continue; }
            if (entity) { if (c == ';') entity = false; continue; }
            if (char.IsWhiteSpace(c)) run = 0;
            else if (++run >= 16 && !char.IsHighSurrogate(c)) { result.Append('\u200B'); run = 0; }
        }
        return result.ToString();
    }
}
