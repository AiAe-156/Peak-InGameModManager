using System.Collections.Generic;
namespace ModConfigEnhance.Overview;

// Captured once per list generation. Scrolling never mutates this order.
internal static class ResidentBuildOrder
{
    internal static int[] Create(int count,int first)
    {
        if(count<=0)return System.Array.Empty<int>();
        first=System.Math.Max(0,System.Math.Min(first,count-1));
        var result=new List<int>(count){first};
        for(int distance=1;first+distance<count || first-distance>=0;distance++)
        {
            if(first+distance<count)result.Add(first+distance);
            if(first-distance>=0)result.Add(first-distance);
        }
        return result.ToArray();
    }
}
