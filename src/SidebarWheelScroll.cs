#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigEnhance;

/// <summary>
/// 仅左栏使用的滚轮缓动。Harmony 前缀只认本组件，避免影响右侧选项和说明面板。
/// 滚动条拖拽、手柄自动滚入视野或其他脚本改位置时，会撤销尚未完成的滚轮目标。
/// </summary>
internal sealed class SidebarWheelScroll : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    internal const float StepPixels = 40f / 3f;
    private const float SmoothTime = 0.1f;
    private const float PositionEpsilon = 0.0001f;

    internal ScrollRect? Scroll;

    private float? _target;
    private float _start;
    private float _elapsed;
    private float _lastPosition;

    private void OnEnable()
    {
        Cancel();
        if (Scroll != null)
        {
            _lastPosition = Scroll.verticalNormalizedPosition;
        }
    }

    private void OnDisable() => Cancel();

    /// <summary>ScrollRect.OnScroll 的 Prefix：返回 false 才会截断 Unity 原始滚轮。</summary>
    internal static bool OnScrollPrefix(ScrollRect __instance, PointerEventData __0)
    {
        SidebarWheelScroll? wheel = __instance.GetComponent<SidebarWheelScroll>();
        if (wheel == null || wheel.Scroll != __instance || !wheel.isActiveAndEnabled || !__instance.isActiveAndEnabled)
        {
            return true;
        }

        wheel.Queue(__0.scrollDelta.y);
        return false;
    }

    private void Queue(float delta)
    {
        if (Scroll == null || Scroll.content == null || !Scroll.vertical || Mathf.Approximately(delta, 0f))
        {
            return;
        }

        // 若滚动条、手柄或别的脚本刚改过位置，Update 还未来得及看到，先放弃旧目标。
        // 否则这一次滚轮会从已经过期的目标继续累计，造成明显回拉。
        float current = Scroll.verticalNormalizedPosition;
        if (HasExternalMovement(current))
        {
            Cancel();
        }

        float scrollable = Scroll.content.rect.height - ViewportHeight(Scroll);
        if (scrollable <= 0.5f)
        {
            Cancel();
            return;
        }

        // Unity 原逻辑把 scrollDelta.y 取反后加到 anchoredPosition；换算到
        // verticalNormalizedPosition 后正负号恢复，因此正值始终向列表顶部移动。
        float baseline = _target ?? current;
        _target = Mathf.Clamp01(baseline + delta * StepPixels / scrollable);
        _start = current;
        _elapsed = 0f;
        _lastPosition = current;
    }

    private void Update()
    {
        if (Scroll == null || !Scroll.isActiveAndEnabled || Scroll.content == null || !Scroll.vertical)
        {
            Cancel();
            return;
        }

        float current = Scroll.verticalNormalizedPosition;
        if (HasExternalMovement(current))
        {
            // 滚动条、手柄 FocusScrollIntoView 或其他调用接管了位置，不能再把它拉回旧目标。
            Cancel();
        }

        _lastPosition = current;
        if (!_target.HasValue)
        {
            return;
        }

        _elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(_elapsed / SmoothTime);
        float next = Mathf.Lerp(_start, _target.Value, Mathf.SmoothStep(0f, 1f, t));
        next = Mathf.Clamp01(next);
        Scroll.verticalNormalizedPosition = next;
        // ScrollRect 会依据布局与像素阈值修正写入值，必须记实际值而不是请求值。
        _lastPosition = Scroll.verticalNormalizedPosition;
        if (Mathf.Abs(next - _target.Value) <= PositionEpsilon)
        {
            Scroll.verticalNormalizedPosition = _target.Value;
            _lastPosition = Scroll.verticalNormalizedPosition;
            Cancel();
        }
    }

    public void OnBeginDrag(PointerEventData eventData) => Cancel();
    public void OnDrag(PointerEventData eventData) => Cancel();
    public void OnEndDrag(PointerEventData eventData) => Cancel();

    private void Cancel()
    {
        _target = null;
        _elapsed = 0f;
    }

    private bool HasExternalMovement(float current)
    {
        if (Scroll == null || Scroll.content == null) return false;
        float scrollable = Mathf.Max(1f, Scroll.content.rect.height - ViewportHeight(Scroll));
        // 固定像素容差，不让 normalizedPosition 趋近 0 时的微小舍入误差取消缓动。
        return Mathf.Abs(current - _lastPosition) * scrollable > 0.25f;
    }

    private static float ViewportHeight(ScrollRect scroll)
    {
        RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        return viewport.rect.height;
    }
}
