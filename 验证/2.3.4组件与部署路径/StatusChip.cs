#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

/// <summary>The one place the name-column status markers are shaped and spaced. Four of them share this lane —
/// unread change, available update, pending save/apply, pending restart — and they had drifted into two shapes
/// (circle vs rounded chip), two sizes (8 vs 7) and two coordinate systems (top-right anchored vs absolute x
/// from the left), which is why they never looked like one group. Everything in the lane is now an 8x8 rounded
/// chip on one right-aligned row, 11 apart, measured from the host's top-right corner.</summary>
internal static class StatusChip
{
    internal const float Size = 8, Step = 11, RightInset = 2, TopInset = 3, Hit = 16;
    internal const int UnreadSlot = 0, UpdateSlot = 1, FirstPendingSlot = 2;
    internal static readonly Color Unread = new(.43f, .68f, .92f, 1f);
    /// <summary>2.3.0: this mod sits inside another mod's folder (amber, like other advice).</summary>
    internal static readonly Color Nested = new(.88f, .63f, .33f, 1f);
    /// <summary>2.3.4: a package with no assembly (resource pack) and a DLL lying loose in plugins/.</summary>
    internal static readonly Color ResourcePack = new(.56f, .78f, .58f, 1f), LooseFile = new(.78f, .62f, .86f, 1f);

    /// <summary>Width the lane needs when it holds this many chips: to the left edge of the leftmost chip, plus
    /// one step of breathing room so the title never touches it.</summary>
    internal static float Reserve(int chips) => chips <= 0 ? 0 : RightInset + (chips - 1) * Step + Size + Step;

    /// <summary>A chip in the shared lane. The returned box is the hit target; the chip is drawn centred in it.
    /// <paramref name="raycast"/> is for markers whose pointer handler lives on the box and therefore needs the
    /// chip itself to be hit-testable.</summary>
    internal static RectTransform Create(Transform parent, int slot, Color color, bool raycast = false)
    {
        var box = OverviewUi.Rect("StatusChip", parent);
        box.anchorMin = box.anchorMax = box.pivot = Vector2.one;
        box.anchoredPosition = new Vector2(-RightInset - slot * Step + (Hit - Size) / 2f, -TopInset + (Hit - Size) / 2f);
        box.sizeDelta = new Vector2(Hit, Hit);
        var glyph = Glyph(box, color, raycast);
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
