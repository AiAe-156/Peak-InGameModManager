#nullable enable
using System;

namespace ModConfigEnhance.Overview;

/// <summary>All window exit routes use the same draft/commit decision; UI owns the confirmation layer.</summary>
internal static class PackFileCloseGuard
{
    internal static void Request(bool dirty, bool staged, Func<bool> saveDraft, Action commit, Action close,
        Action<Action, Action, Action> confirm)
    {
        if (!dirty && !staged) { close(); return; }
        confirm(() => { if (saveDraft()) commit(); }, close, () => { });
    }
}
