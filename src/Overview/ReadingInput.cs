namespace ModConfigEnhance.Overview;
internal static class ReadingInput
{
    internal static float Step(bool up, bool dpad) => (up ? -90f : 90f) * (dpad ? 3f : 1f);
}
