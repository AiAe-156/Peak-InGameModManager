#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using PEAKLib.ModConfig;
using Zorro.Settings;

namespace ModConfigEnhance.Overview;

/// <summary>Uses only the already registered ModConfig settings; never discovers third-party getters.</summary>
internal static class OverviewRuntimeSettingsBridge
{
    internal static void Synchronize(IEnumerable<ConfigEntryBase> entries, bool newRound = false)
    {
        using var commit = new NativeConfigurationDraft.CommitScope();
        var changed = new HashSet<ConfigEntryBase>(entries);
        if (changed.Count == 0 || !ModConfigSupport.IsInstalled) return;
        var handler = SettingsHandler.Instance;
        if (handler == null)
        {
            if (newRound) throw new InvalidOperationException("ModConfig settings are not ready; next-round delivery remains pending.");
            return;
        }
        foreach (Setting setting in handler.GetAllSettings().ToArray())
        {
            if (setting is not IBepInExProperty property || !changed.Contains(property.ConfigBase)) continue;
            var timing = Timing(property.ConfigBase);
            // Do not refresh a cached Setting or invoke its UI callbacks early for a
            // value explicitly declared to belong to the next round/game process.
            if (timing == ConfigurationEffectTiming.GameRestart || timing == ConfigurationEffectTiming.NextRound && !newRound) continue;
            property.RefreshValueFromConfig();
            // Native setting controls perform ApplyValue then SaveSetting. SaveSetting also
            // invokes registration-time save callbacks used by mods that cache their values.
            if (timing is ConfigurationEffectTiming.Immediate or ConfigurationEffectTiming.Unspecified || newRound && timing == ConfigurationEffectTiming.NextRound)
            {
                setting.ApplyValue();
                handler.SaveSetting(setting);
            }
            setting.OnSettingChangedExternal();
            setting.OnSettingChanged();
        }
    }

    internal static ConfigurationEffectTiming Timing(ConfigEntryBase entry)
        => ConfigurationEffectPolicy.FromTags(entry.Description?.Tags ?? Array.Empty<object>());

    internal static bool RequiresRestart(ConfigEntryBase entry) => Timing(entry) == ConfigurationEffectTiming.GameRestart;

    internal static string TimingLabel(ConfigurationEffectTiming timing) => timing switch
    {
        ConfigurationEffectTiming.Immediate => Loc.T("Immediately", "立即生效", "立即生效"),
        ConfigurationEffectTiming.NextRound => Loc.T("Next round", "重新开局生效", "重新開局生效"),
        ConfigurationEffectTiming.GameRestart => Loc.T("Restart game", "重启游戏生效", "重新啟動遊戲生效"),
        ConfigurationEffectTiming.WhenEnabled => Loc.T("When enabled", "启用模组后使用", "啟用模組後使用"),
        _ => Loc.T("Determined by the mod", "生效时机由模组决定", "生效時機由模組決定")
    };
}
