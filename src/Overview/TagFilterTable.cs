#nullable enable
using System;
using UnityEngine;
namespace ModConfigEnhance.Overview;
internal sealed class TagFilterTable : MonoBehaviour
{
    internal Action<float>? Reflow;
    private float _width = -1;
    internal void Refresh()
    {
        float width = ((RectTransform)transform).rect.width;
        if (Reflow == null || width <= 0 || Mathf.Abs(width - _width) < .1f) return;
        _width = width; Reflow(width);
    }
    private void LateUpdate() => Refresh();
}
