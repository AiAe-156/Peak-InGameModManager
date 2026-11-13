namespace ModConfigEnhance.Overview;
internal static class ListColumnPolicy
{
    internal const int Settings = -2, Home = -3, Folder = -4;
    internal static int FromPosition(float x, float[] starts)
    {
        if (x < starts[0]) return 0;
        if (x < starts[1]) return 1;
        if (x < starts[2]) return Settings;
        if (x < starts[3]) return Home;
        if (x < starts[4]) return Folder;
        if (x < starts[5]) return 2;
        if (x < starts[6]) return 3;
        if (x < starts[7]) return 4;
        return 5;
    }
    internal static int Find(int preferred, int[] columns, bool[] enabled, bool group)
    {
        for (int i = 0; i < columns.Length; i++) if (columns[i] == preferred && enabled[i]) return i;
        // A group heading stands in for the name lane; it never stands in for an icon lane.
        if (group && preferred == 1) for (int i = 0; i < columns.Length; i++) if (enabled[i]) return i;
        return -1;
    }
}
