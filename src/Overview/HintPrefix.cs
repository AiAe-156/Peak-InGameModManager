#nullable enable
using System;
namespace ModConfigEnhance.Overview;
/// <summary>Largest fitting logical prefix; preserve at least the heading even in tiny windows.</summary>
internal static class HintPrefix
{
    internal static int Fit(int count, float limit, Func<int,float> measure)
    {
        if(count<=0)return 0;
        int low=1,high=count;
        while(low<high)
        {
            int mid=(low+high+1)/2;
            if(measure(mid)<=limit)low=mid;else high=mid-1;
        }
        return low;
    }
}
