#nullable enable
using System;
using System.Linq;
namespace ModConfigEnhance.Diagnostics;
internal static class LogViewRules
{
    internal static string[] Collapse(string[] lines, bool expanded)
    {
        if (expanded || lines.Length <= 3) return lines;
        return new[] { lines[0], lines[1], "···+" + (lines.Length - 2) };
    }
    internal static int CycleLevel(int level, int direction, int count) => (level + direction + count) % count;
}
