#nullable enable
using System;
namespace ModConfigEnhance.Overview;

// Event-driven, finite transition: a settled hover never needs a permanent Update.
internal sealed class RefreshIconFeedbackState
{
    internal bool Pointer, Focus;
    internal bool Available = true;
    internal float Amount { get; private set; }
    internal bool Hot => Available && (Pointer || Focus);
    internal bool NeedsFrame => Amount != (Hot ? 1f : 0f);
    internal void Step(float seconds)
    {
        float target = Hot ? 1f : 0f, distance = Math.Max(0, seconds) / .16f;
        Amount = target > Amount ? Math.Min(target, Amount + distance) : Math.Max(target, Amount - distance);
    }
    internal void Reset() { Pointer = Focus = false; Amount = 0; }
}
