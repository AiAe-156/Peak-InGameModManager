using System;
namespace ModConfigEnhance.Overview;

/// <summary>Shared readable depth palette. Depth zero denotes a direct relationship.</summary>
internal static class DependencyDepthStyle
{
    internal static string Ink(int depth)
    {
        int level = Math.Max(0, Math.Min(32, depth));
        double blend = level <= 2 ? level / (level + 2.0) : .5 + (level - 2) * .014;
        int Mix(int value, int floor) => (int)Math.Round(value + (floor - value) * blend);
        return "#" + Mix(238, 132).ToString("X2") + Mix(232, 140).ToString("X2") + Mix(206, 140).ToString("X2");
    }
    internal static string Wrap(string text, int depth)
        => "<color=" + Ink(depth) + ">" + text + "</color>";
}
