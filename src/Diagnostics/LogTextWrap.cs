#nullable enable
using System;
using System.Collections.Generic;

namespace ModConfigEnhance.Diagnostics;

/// <summary>Wrap plain source text using explicit measured width, independently of hidden TMP mesh layout.</summary>
internal static class LogTextWrap
{
    internal static string[] Lines(string text, float width, Func<string, float> measure)
    {
        var result = new List<string>(); width = Math.Max(40, width);
        foreach (string paragraph in text.Replace("\r", "").Split('\n'))
        {
            if (paragraph.Length == 0) { result.Add(""); continue; }
            int start = 0;
            while (start < paragraph.Length)
            {
                int low = 1, high = paragraph.Length - start, fit = 1;
                while (low <= high)
                {
                    int mid = low + (high - low) / 2;
                    if (measure(paragraph.Substring(start, mid)) <= width) { fit = mid; low = mid + 1; }
                    else high = mid - 1;
                }
                if (start + fit < paragraph.Length)
                {
                    // Prefer a nearby word boundary without creating tiny lines for long identifiers.
                    int space = paragraph.LastIndexOf(' ', start + fit - 1, fit);
                    if (space >= start + fit / 2) fit = space - start + 1;
                    if (fit > 1 && char.IsHighSurrogate(paragraph[start + fit - 1]) && char.IsLowSurrogate(paragraph[start + fit])) fit--;
                    else if (fit == 1 && char.IsHighSurrogate(paragraph[start]) && start + 1 < paragraph.Length && char.IsLowSurrogate(paragraph[start + 1])) fit = 2;
                }
                result.Add(paragraph.Substring(start, fit)); start += fit;
            }
        }
        return result.ToArray();
    }
}
