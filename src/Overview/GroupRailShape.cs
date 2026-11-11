#nullable enable
using UnityEngine;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 2.4.0：分组色条的分级形状（用户定）：每深一级变细、变短，最短为原高的 1/2；底端对齐、从顶部截短。
/// 树形侧栏、概览列表、快捷键页、智能分组预览共用。level 0 = 顶层分组。
/// </summary>
internal static class GroupRailShape
{
    /// <summary>高度比例：1、3/4、1/2（第三级起保持 1/2）。</summary>
    internal static float Height(int level) => Mathf.Max(.5f, 1f - .25f * Mathf.Max(0, level));

    /// <summary>宽度：每级减 1 像素，最细为基准宽度的一半。</summary>
    internal static float Width(float baseWidth, int level) => Mathf.Max(baseWidth * .5f, baseWidth - Mathf.Max(0, level));

    /// <summary>第 level 根色条的左边 x：前面各级宽度 + 间隙依次累加。</summary>
    internal static float X(float left, float baseWidth, float gap, int level)
    {
        float x = left;
        for (int i = 0; i < level; i++) x += Width(baseWidth, i) + gap;
        return x;
    }

    /// <summary>左上角锚定（概览用 OverviewUi.Place）的色条：返回 (y, height)，底端与满高色条对齐。</summary>
    internal static (float Y, float Height) Vertical(float y, float fullHeight, int level)
    {
        float height = fullHeight * Height(level);
        return (y + fullHeight - height, height);
    }

    /// <summary>纵向拉伸锚定（侧栏）的色条：上下各留 inset，按级截掉顶部。</summary>
    internal static void Stretch(RectTransform rect, float x, float width, float inset, int level)
    {
        float f = Height(level);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, f);
        rect.offsetMin = new Vector2(x, inset);
        rect.offsetMax = new Vector2(x + width, inset - 2f * inset * f);
    }
}
