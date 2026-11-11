#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>The one place the name-column status markers are shaped and spaced. Every marker in the lane is an 8x8
/// rounded chip on one right-aligned row, measured from the host's top-right corner.
/// Each kind has its own fixed slot, so a given marker is always in the same place on every row (user decision
/// 2026-11-09: keep positions fixed, only tighten the spacing; 11 -> 10).</summary>
internal static class StatusChip
{
    internal const float Size = 8, Step = 10, RightInset = 2, TopInset = 3, Hit = 16, Breathing = 4;
    /// <summary>Fixed slots, right to left.</summary>
    internal const int UnreadSlot = 0, UpdateSlot = 1, FirstPendingSlot = 2, RestartSlot = 3, NestedSlot = 4, ShapeSlot = 5;
    internal static readonly Color Unread = new(.43f, .68f, .92f, 1f);
    /// <summary>2.3.0: this mod sits inside another mod's folder (amber, like other advice).</summary>
    internal static readonly Color Nested = new(.88f, .63f, .33f, 1f);
    /// <summary>2.3.4: a package with no assembly (resource pack) and a DLL lying loose in plugins/. 2.3.6: teal and
    /// pink, so neither can be mistaken for the green "update available" chip or the violet "pending restart" one.</summary>
    internal static readonly Color ResourcePack = new(.38f, .78f, .76f, 1f), LooseFile = new(.88f, .55f, .74f, 1f);
    /// <summary>2.3.6: pending restart had the same blue as "unread change".</summary>
    internal static readonly Color Restart = new(.68f, .58f, .95f, 1f);

    /// <summary>Width the lane needs when it holds this many chips: to the left edge of the leftmost chip, plus a
    /// little breathing room so the title never touches it.</summary>
    internal static float Reserve(int chips) => chips <= 0 ? 0 : RightInset + (chips - 1) * Step + Size + Breathing;

    /// <summary>A chip in the shared lane. The returned box is the hit target; the chip is drawn centred in it.
    /// <paramref name="raycast"/> is for markers whose pointer handler lives on the box: the whole 16x16 box then gets
    /// a transparent hit image. 2.3.6: only the 8x8 glyph used to be hit-testable, and the title text is sized to its
    /// own words, so pointing near a chip hit the bare row and showed no hint at all.</summary>
    internal static RectTransform Create(Transform parent, int slot, Color color, bool raycast = false)
    {
        var box = OverviewUi.Rect("StatusChip", parent);
        box.anchorMin = box.anchorMax = box.pivot = Vector2.one;
        box.anchoredPosition = Position(slot);
        box.sizeDelta = new Vector2(Hit, Hit);
        if (raycast) { var hit = box.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true; }
        var glyph = Glyph(box, color, false);
        glyph.anchoredPosition = Vector2.zero;
        return box;
    }

    /// <summary>The same chip for markers that sit inside their own cell (the version column) rather than the lane.</summary>
    internal static RectTransform CreateAt(Transform parent, float x, float y, Color color)
    {
        var box = OverviewUi.Rect("StatusChip", parent);
        OverviewUi.Place(box, x, y, Hit, Hit);
        var glyph = Glyph(box, color, false);
        glyph.anchoredPosition = Vector2.zero;
        return box;
    }

    internal static Vector2 Position(int index) => new(-RightInset - index * Step + (Hit - Size) / 2f, -TopInset + (Hit - Size) / 2f);

    private static RectTransform Glyph(RectTransform parent, Color color, bool raycast)
    {
        var glyph = OverviewUi.Rect("ChipGlyph", parent);
        glyph.anchorMin = glyph.anchorMax = glyph.pivot = new Vector2(.5f, .5f);
        glyph.sizeDelta = new Vector2(Size, Size);
        var chip = glyph.gameObject.AddComponent<RoundedChip>();
        chip.color = color;
        chip.raycastTarget = raycast;
        return glyph;
    }
}
