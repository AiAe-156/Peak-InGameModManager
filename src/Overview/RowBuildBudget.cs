using System.Diagnostics;

namespace ModConfigEnhance.Overview;

// Shared by all build entry points within a frame. One Unity tree is non-preemptible.
internal sealed class RowBuildBudget
{
    private readonly long _frequency;
    private readonly int _maxSteps;
    private int _frame = -1, _count;
    private long _started;
    internal RowBuildBudget(long frequency = 0, int maxSteps = 2)
    { _frequency = frequency > 0 ? frequency : Stopwatch.Frequency; _maxSteps = maxSteps; }
    internal bool TryTake(int frame, long now, bool immediate = false)
    {
        if (_frame != frame) { _frame=frame; _count=0; _started=now; }
        if (!immediate && _count>0 && (_count>=_maxSteps || (now-_started)*1000d/_frequency>=2)) return false;
        _count++;
        return true;
    }
}
