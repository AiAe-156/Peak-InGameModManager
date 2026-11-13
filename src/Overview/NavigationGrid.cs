#nullable enable
using System;
using System.Collections.Generic;
namespace ModConfigEnhance.Overview;
/// <summary>Layout-only navigation; scroll views are independent regions, never one interleaved list.</summary>
internal static class NavigationGrid
{
    internal readonly struct Cell {
        internal readonly int Area; internal readonly float X, Y, ScopeX, ScopeY, HalfWidth, HalfHeight;
        internal Cell(int area, float x, float y, float scopeX, float scopeY, float halfWidth = 0, float halfHeight = 0)
        { Area = area; X = x; Y = y; ScopeX = scopeX; ScopeY = scopeY; HalfWidth = halfWidth; HalfHeight = halfHeight; }
    }
    internal readonly struct Box {
        internal readonly float Left, Bottom, Right, Top;
        internal Box(float left, float bottom, float right, float top)
        { Left = left; Bottom = bottom; Right = right; Top = top; }
    }
    private const float Row = 14;
    internal static int[][] Build(IReadOnlyList<Cell> cells, IReadOnlyDictionary<int, Box> areas)
    {
        var result = new int[cells.Count][];
        for (int i = 0; i < cells.Count; i++) {
            result[i] = new int[4];
            for (int d = 0; d < 4; d++) result[i][d] = Find(cells, areas, i, d);
        }
        return result;
    }
    internal static int Find(IReadOnlyList<Cell> cells, IReadOnlyDictionary<int, Box> areas, int index, int direction)
    {
        var from = cells[index]; bool horizontal = direction < 2, positive = direction is 1 or 2;
        int best = -1; float bestAlong = float.MaxValue, bestCross = float.MaxValue;
        for (int i = 0; i < cells.Count; i++) {
            var to = cells[i]; if (i == index || to.Area != from.Area) continue;
            float dx = to.X - from.X, dy = to.Y - from.Y;
            float centerAlong = (horizontal ? dx : dy) * (positive ? 1 : -1);
            float along = Math.Max(0, centerAlong - (horizontal ? from.HalfWidth + to.HalfWidth : from.HalfHeight + to.HalfHeight));
            float cross = Math.Max(0, Math.Abs(horizontal ? dy : dx) - (horizontal ? from.HalfHeight + to.HalfHeight : from.HalfWidth + to.HalfWidth));
            if (centerAlong <= (horizontal ? 2 : Row) || horizontal && cross > Row) continue;
            if (best < 0 || along < bestAlong - (horizontal ? 0 : Row)
                || Math.Abs(along - bestAlong) <= (horizontal ? 0 : Row) && cross < bestCross) {
                best = i; bestAlong = along; bestCross = cross;
            }
        }
        if (from.Area != 0 && best >= 0) return best;
        float x = from.ScopeX, y = from.ScopeY;
        if (from.Area != 0) {
            var bounds = areas[from.Area];
            x = Math.Max(bounds.Left, Math.Min(bounds.Right, x)); y = Math.Max(bounds.Bottom, Math.Min(bounds.Top, y));
            if (horizontal) x = positive ? bounds.Right : bounds.Left; else y = positive ? bounds.Top : bounds.Bottom;
        }
        if (from.Area == 0) { if (horizontal) x += positive ? from.HalfWidth : -from.HalfWidth; else y += positive ? from.HalfHeight : -from.HalfHeight; }
        float score = best < 0 ? float.MaxValue : bestAlong + bestCross * 2;
        for (int i = 0; i < cells.Count; i++) {
            var to = cells[i]; if (to.Area != 0 || to.Area == from.Area || i == index) continue;
            float centerAlong = (horizontal ? to.ScopeX - x : to.ScopeY - y) * (positive ? 1 : -1);
            float along = Math.Max(0, centerAlong - (horizontal ? to.HalfWidth : to.HalfHeight));
            float cross = Math.Max(0, Math.Abs(horizontal ? to.ScopeY - y : to.ScopeX - x) - (horizontal ? to.HalfHeight : to.HalfWidth));
            if (centerAlong <= 2 || horizontal && cross > Row) continue;
            float candidate = along + cross * 2;
            if (candidate < score) { score = candidate; best = i; }
        }
        foreach (var pair in areas) {
            if (pair.Key == from.Area) continue;
            var box = pair.Value;
            float along = horizontal ? (positive ? box.Left - x : x - box.Right) : (positive ? box.Bottom - y : y - box.Top);
            if (along < -2) continue;
            float low = horizontal ? box.Bottom : box.Left, high = horizontal ? box.Top : box.Right, axis = horizontal ? y : x;
            float extent = from.Area == 0 ? (horizontal ? from.HalfHeight : from.HalfWidth) : 0;
            float cross = Math.Max(low - axis - extent, Math.Max(0, axis - high - extent)), candidate = Math.Max(0, along) + cross * 2;
            if (candidate >= score) continue;
            int entrance = -1; float edge = positive ? float.MaxValue : float.MinValue, column = float.MaxValue;
            for (int i = 0; i < cells.Count; i++) {
                var to = cells[i]; if (to.Area != pair.Key) continue;
                float row = horizontal ? Math.Abs(to.ScopeY - y) : to.Y;
                float distance = horizontal ? (positive ? to.X : -to.X) : Math.Abs(to.ScopeX - x);
                bool better = horizontal ? entrance < 0 || row < edge - Row || Math.Abs(row - edge) <= Row && distance < column
                    : entrance < 0 || (positive ? row < edge - Row : row > edge + Row) || Math.Abs(row - edge) <= Row && distance < column;
                if (better) { entrance = i; edge = row; column = distance; }
            }
            if (entrance >= 0) { best = entrance; score = candidate; }
        }
        return best;
    }
}
