using System;
namespace ModConfigEnhance.Overview;
internal static class QueueToolbarSizing
{
    internal static float[] Widths(float available, float[] preferred, float gap)
    {
        var result = new float[preferred.Length]; float total = 0;
        foreach (float width in preferred) total += Math.Max(1, width);
        float usable = Math.Max(1, available - Math.Max(0, preferred.Length - 1) * gap);
        float scale = total <= usable ? 1 : usable / total;
        for (int i = 0; i < result.Length; i++) result[i] = Math.Max(1, preferred[i]) * scale;
        return result;
    }
}
