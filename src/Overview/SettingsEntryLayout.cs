#nullable enable
using System;
using UnityEngine;

namespace ModConfigEnhance.Overview;

// Base layout only: animated scale/rotation and resolved TMP auto-size are not layout inputs.
internal readonly struct SettingsEntryLayout : IEquatable<SettingsEntryLayout>
{
    internal readonly Vector2 AnchorMin, AnchorMax, Pivot, Size, Position;
    internal readonly float Height, FontMax;
    internal readonly int Language;
    internal SettingsEntryLayout(RectTransform reference, float fontMax, int language)
    {
        AnchorMin=reference.anchorMin; AnchorMax=reference.anchorMax; Pivot=reference.pivot;
        Size=reference.sizeDelta; Position=reference.anchoredPosition; Height=reference.rect.height;
        FontMax=fontMax; Language=language;
    }
    public bool Equals(SettingsEntryLayout other) => AnchorMin==other.AnchorMin && AnchorMax==other.AnchorMax
        && Pivot==other.Pivot && Size==other.Size && Position==other.Position
        && Height==other.Height && FontMax==other.FontMax && Language==other.Language;
}
