#nullable enable
using System;
using HarmonyLib;

namespace ModConfigEnhance.Overview;

/// <summary>Airport departure RPC is a new expedition, not a biome/scene transition.</summary>
internal static class ConfigurationRoundBoundary
{
    internal static int Generation { get; private set; }
    internal static void Install(Harmony harmony)
    {
        var method = AccessTools.Method(typeof(AirportCheckInKiosk), "BeginIslandLoadRPC");
        if (method == null) throw new InvalidOperationException("New-round configuration boundary unavailable.");
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(ConfigurationRoundBoundary), nameof(BeforeRound)));
    }
    private static void BeforeRound()
    {
        Generation++;
        try { NativeConfigurationDraft.Repository?.AcknowledgeNewRound(); }
        catch (Exception error) { Plugin.Log.LogWarning("New-round configuration refresh failed; pending record retained: " + error.Message); }
    }
}
