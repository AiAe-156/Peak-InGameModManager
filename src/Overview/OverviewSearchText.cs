#nullable enable
using System;
using System.Linq;
namespace ModConfigEnhance.Overview;
internal static class OverviewSearchText
{
    internal static bool Matches(string query, params string[] values) =>
        query.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .All(term => values.Any(value => (value ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
}
