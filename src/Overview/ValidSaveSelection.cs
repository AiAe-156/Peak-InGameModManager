#nullable enable
using System;
using System.Collections.Generic;

namespace ModConfigEnhance.Overview;

/// <summary>Keep complete-batch validation first; only partition when that batch is rejected.</summary>
internal static class ValidSaveSelection
{
    internal static List<T> Select<T>(IReadOnlyList<T> requested, Func<IReadOnlyList<T>, string?> validate, Action<T, string> rejected)
    {
        if (validate(requested) == null) return new List<T>(requested);
        var accepted = new List<T>();
        foreach (var target in requested)
        {
            var candidate = new List<T>(accepted) { target };
            string? error = validate(candidate);
            if (error == null) accepted.Add(target);
            else rejected(target, error);
        }
        return accepted;
    }
}
