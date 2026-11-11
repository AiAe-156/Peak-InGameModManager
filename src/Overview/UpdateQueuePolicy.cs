#nullable enable
using System;
using System.Globalization;

namespace ModConfigEnhance.Overview;

/// <summary>Update-latest is deliberately separate from explicit modpack restore/version selection.</summary>
internal static class UpdateQueuePolicy
{
    internal static bool LatestTarget(bool hasUpdate, string installed, string latest, out string target, out string reason)
    {
        target = ""; reason = "";
        if (!hasUpdate) { reason = "No confirmed update is available."; return false; }
        if (!Version.TryParse(installed, out var current)) { reason = "The installed package version is unknown."; return false; }
        if (!Version.TryParse(latest, out var next)) { reason = "Latest version unknown; check for updates first."; return false; }
        if (next <= current) { reason = "The checked version is not newer; batch updates never downgrade."; return false; }
        target = latest; return true;
    }
    internal static bool CurrentFailure(string updatedUtc, DateTime sessionStartUtc, DateTime launchAttemptUtc, DateTime successUtc)
    {
        return launchAttemptUtc != DateTime.MinValue
            && DateTime.TryParse(updatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var failed)
            && failed >= sessionStartUtc && failed >= launchAttemptUtc && failed > successUtc;
    }
}
