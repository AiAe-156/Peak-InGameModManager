#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.1.0（实测修正，用户 2026-11-16）：分组相关的纯逻辑（不碰 Unity），由 GroupTree、模组列表的分组拖动、分组编辑窗口调用；
/// 夹具 <c>验证/快捷键页_20261115/combo-checks/</c> 连同 OverviewPadSteps.cs 一起链接本文件做单元检查。改规则时两边一起跑。
/// </summary>
internal static class GroupSteps
{
    /// <summary>把分组标题拖到另一个分组标题上时的落点：上缘 = 插到它前面（同层），下缘 = 插到它后面（同层），中间 = 成为它的子分组。</summary>
    internal enum DropZone { Before, Inside, After }

    /// <summary>上缘、下缘各占标题高度的这么多（1/4）。</summary>
    internal const float EdgeFraction = .25f;

    /// <summary><paramref name="fromTop"/>：指针在标题里的位置，0 = 上边，1 = 下边（超出范围按边算）。</summary>
    internal static DropZone Zone(float fromTop)
    {
        if (float.IsNaN(fromTop)) return DropZone.Inside;
        if (fromTop < EdgeFraction) return DropZone.Before;
        if (fromTop > 1 - EdgeFraction) return DropZone.After;
        return DropZone.Inside;
    }

    /// <summary>
    /// 新分组的预填名称：<paramref name="prefix"/> + 最小的、还没被占用的正整数（「新分组1」「新分组2」……）。
    /// 已有名称比较时忽略前后空白与大小写（英文 New group 1 / new group 1 视为同名）。
    /// </summary>
    internal static string NumberedName(string prefix, IEnumerable<string?> existing)
    {
        var taken = new HashSet<string>(existing.Where(n => n != null).Select(n => n!.Trim()), StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            string name = prefix + i;
            if (!taken.Contains(name)) return name;
        }
    }

    /// <summary>
    /// 成员行的排序键：分组展开后子分组排在本组直属模组的前面。<paramref name="subtreeEnd"/> = 本组子树最后一个分组在树序里的位置，
    /// 同一个 subtreeEnd 时层级越深越靠前（子分组的成员先于父分组的直属成员）。
    /// </summary>
    internal static int MemberRank(int subtreeEnd, int depth, int maxDepth)
    {
        if (subtreeEnd < 0) return int.MaxValue;
        long rank = (long)subtreeEnd * (maxDepth + 1) + Math.Max(0, maxDepth - depth);
        return rank >= int.MaxValue ? int.MaxValue - 1 : (int)rank;
    }
}
