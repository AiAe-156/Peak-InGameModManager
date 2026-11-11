using System;
using System.Collections.Generic;
using System.Globalization;

namespace ModConfigEnhance.Overview;

/// <summary>Shared compact name/original/version text for relationship hints and detail rows.</summary>
internal static class DependencyRowText
{
    internal static string Escape(string value) => value.Replace("<", "＜").Replace(">", "＞").Replace('\r', ' ').Replace('\n', ' ');
    internal static string Short(string value, int limit)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ');
        int[] elements = StringInfo.ParseCombiningCharacters(value);
        return elements.Length <= limit ? value : value.Substring(0, elements[limit - 1]) + "…";
    }
    internal static string Name(string display, string original, string version, int nameLimit = 32, int originalLimit = 24)
    {
        var metadata = new List<string>();
        if (original.Length > 0 && !ModNameLayout.Equivalent(display, original)) metadata.Add(Escape(Short(original, originalLimit)));
        if (version.Length > 0) metadata.Add(Escape(Short(version, 24)));
        return Escape(Short(display, nameLimit)) + (metadata.Count == 0 ? "" : " <size=80%><color=#92998F>(" + string.Join(", ", metadata) + ")</color></size>");
    }
    internal static string FitName(string display, string original, string version, float width, Func<string, float> measure)
    {
        int low = 1, high = 32;
        string result = Name(display, original, version, high, 18);
        if (measure(result) <= width) return result;
        if (measure(Name(display, original, version, 1, 18)) > width) original = display;
        result = Name(display, original, version, 1, 18);
        while (low <= high) {
            int middle = (low + high) / 2;
            string candidate = Name(display, original, version, middle, 18);
            if (measure(candidate) <= width) { result = candidate; low = middle + 1; }
            else high = middle - 1;
        }
        return result;
    }
}
