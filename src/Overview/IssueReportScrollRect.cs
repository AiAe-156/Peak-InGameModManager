#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace ModConfigEnhance.Overview;
internal sealed class IssueReportScrollRect : ScrollRect
{
    public override void OnScroll(PointerEventData data)
    {
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            StopMovement();
            float range = Mathf.Max(0, content.rect.width - viewport.rect.width);
            if (range > 0) horizontalNormalizedPosition = Mathf.Clamp01(horizontalNormalizedPosition
                - (data.scrollDelta.y + data.scrollDelta.x) * scrollSensitivity / range);
            data.Use(); return;
        }
        base.OnScroll(data);
    }
}
