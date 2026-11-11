#nullable enable
using UnityEngine.UI;
namespace ModConfigEnhance.Overview { internal static partial class OverviewUi {
internal static void SyncCheck(Toggle toggle, bool value) { toggle.SetIsOnWithoutNotify(value); toggle.GetComponent<OverviewToggleVisual>()?.Sync?.Invoke(value); }
} partial class SchedulerHarness {
private void RefreshVirtualRows(VirtualRow? immediate=null)
    {
        if(_disposed || !ListVisible || _building || _infrastructureLoading)return;
        if(!_residentPending && immediate==null && !_layerFocus.HasValue)return;
        CaptureBuildOrder();
        // Scrolling/focus changes only the viewport. Even a requested focus target
        // waits for this generation's fixed build cursor instead of jumping the queue.
        while(_buildCursor<_buildOrder.Count)
        {
            if(!BuildResidentRow(_buildOrder[_buildCursor]))break;
            _buildCursor++;
        }
        _residentPending=_buildCursor<_buildOrder.Count;
    }
} }