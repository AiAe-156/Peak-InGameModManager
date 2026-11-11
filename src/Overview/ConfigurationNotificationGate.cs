#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace ModConfigEnhance.Overview;

/// <summary>Persist restart-scoped entries without invoking third-party live update listeners.</summary>
internal static class ConfigurationNotificationGate
{
    [ThreadStatic] private static HashSet<ConfigEntryBase>? _suppressed;
    [ThreadStatic] private static HashSet<ConfigFile>? _saveSuppressed;
    internal static void Install(Harmony harmony)
    {
        var changed = AccessTools.Method(typeof(ConfigFile), "OnSettingChanged");
        if (changed == null) throw new InvalidOperationException("Configuration notification boundary unavailable.");
        harmony.Patch(changed, prefix: new HarmonyMethod(typeof(ConfigurationNotificationGate), nameof(BeforeChanged)));
        harmony.Patch(AccessTools.Method(typeof(ConfigFile), "Save"), prefix: new HarmonyMethod(typeof(ConfigurationNotificationGate), nameof(BeforeSave)));
    }
    private static bool BeforeChanged(object[] __args)
        => !__args.OfType<ConfigEntryBase>().Any(entry => _suppressed?.Contains(entry) == true);
    private static bool BeforeSave(ConfigFile __instance) => _saveSuppressed?.Contains(__instance) != true;
    // BepInEx OnSettingChanged catches callback exceptions. Dispatch its actual event
    // subscriptions explicitly at the deferred boundary so failures remain pending.
    // Successful subscribers are remembered across retries (including metadata I/O failure).
    private static readonly Dictionary<ConfigEntryBase, HashSet<Delegate>> Delivered = new();
    internal static void DeliverNextRound(ConfigEntryBase entry)
    {
        if (!Delivered.TryGetValue(entry, out var delivered)) Delivered[entry] = delivered = new();
        var fileEvent = AccessTools.Field(typeof(ConfigFile), "SettingChanged")
            ?? throw new InvalidOperationException("BepInEx file event unavailable.");
        var entryEvent = AccessTools.Field(entry.GetType(), "SettingChanged")
            ?? throw new InvalidOperationException("BepInEx entry event unavailable.");
        var callbacks = new List<Delegate>();
        var entryCallbacks = (entryEvent.GetValue(entry) as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>();
        if (fileEvent.GetValue(entry.ConfigFile) is Delegate fileCallbacks)
            foreach (var callback in fileCallbacks.GetInvocationList())
            {
                // Expand the forwarding subscription in place, preserving BepInEx order.
                if (ReferenceEquals(callback.Target, entry)) callbacks.AddRange(entryCallbacks);
                else callbacks.Add(callback);
            }
        using var saves = new Scope(new[] { entry });
        foreach (var callback in callbacks)
        {
            if (delivered.Contains(callback)) continue;
            callback.DynamicInvoke(entry, new SettingChangedEventArgs(entry));
            delivered.Add(callback);
        }
    }
    private static readonly HashSet<ConfigEntryBase> BridgeDelivered = new();
    internal static void DeliverBridge(ConfigEntryBase entry, Action deliver)
    {
        if (BridgeDelivered.Contains(entry)) return;
        deliver(); BridgeDelivered.Add(entry);
    }
    internal static void ResetDelivery(ConfigEntryBase entry) { Delivered.Remove(entry); BridgeDelivered.Remove(entry); }

    internal sealed class Scope : IDisposable
    {
        private readonly HashSet<ConfigEntryBase>? _previous;
        private readonly HashSet<ConfigFile>? _previousSaves;
        internal Scope(IEnumerable<ConfigEntryBase> entries)
        {
            _previous = _suppressed; _previousSaves = _saveSuppressed;
            var targets = entries.ToList();
            _saveSuppressed = new HashSet<ConfigFile>(_previousSaves ?? Enumerable.Empty<ConfigFile>());
            foreach (var target in targets) _saveSuppressed.Add(target.ConfigFile);
            _suppressed = new HashSet<ConfigEntryBase>(_previous ?? Enumerable.Empty<ConfigEntryBase>());
            foreach (var entry in targets.Where(e => OverviewRuntimeSettingsBridge.Timing(e) is ConfigurationEffectTiming.GameRestart or ConfigurationEffectTiming.NextRound))
                _suppressed.Add(entry);
        }
        public void Dispose() { _suppressed = _previous; _saveSuppressed = _previousSaves; }
    }
}
