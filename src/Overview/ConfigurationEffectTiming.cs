#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ModConfigEnhance.Overview;

internal enum ConfigurationEffectTiming { Unspecified, Immediate, NextRound, GameRestart, WhenEnabled }

/// <summary>Only explicit metadata establishes effect timing; setting names/descriptions are not evidence.</summary>
internal static class ConfigurationEffectPolicy
{
    internal static ConfigurationEffectTiming FromTags(IEnumerable<object?> tags)
    {
        var result = ConfigurationEffectTiming.Unspecified;
        foreach (object? tag in tags)
        {
            if (tag == null) continue;
            if (tag is string text) result = Stronger(result, Named(text));
            else if (tag is ConfigurationEffectTiming declared) result = Stronger(result, declared);
            else
            {
                for (Type? type = tag.GetType(); type != null && type != typeof(object); type = type.BaseType)
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    string name = field.Name;
                    if (name.StartsWith("<", StringComparison.Ordinal) && name.EndsWith(">k__BackingField", StringComparison.Ordinal))
                        name = name.Substring(1, name.IndexOf('>') - 1);
                    // Read stored fields only; third-party property getters may run plugin code.
                    if (field.FieldType == typeof(bool) && Named(name) is var timing && timing != ConfigurationEffectTiming.Unspecified && field.GetValue(tag) is true)
                        result = Stronger(result, timing);
                    if ((name == "EffectTiming" || name == "ApplyTiming") && field.FieldType.IsEnum)
                        result = Stronger(result, Named(field.GetValue(tag)?.ToString() ?? ""));
                }
            }
        }
        return result;
    }

    private static ConfigurationEffectTiming Stronger(ConfigurationEffectTiming a, ConfigurationEffectTiming b)
    {
        if (a == ConfigurationEffectTiming.GameRestart || b == ConfigurationEffectTiming.GameRestart) return ConfigurationEffectTiming.GameRestart;
        if (a == ConfigurationEffectTiming.NextRound || b == ConfigurationEffectTiming.NextRound) return ConfigurationEffectTiming.NextRound;
        if (a == ConfigurationEffectTiming.WhenEnabled || b == ConfigurationEffectTiming.WhenEnabled) return ConfigurationEffectTiming.WhenEnabled;
        return b == ConfigurationEffectTiming.Unspecified ? a : b;
    }
    private static ConfigurationEffectTiming Named(string name) => name.Trim().ToLowerInvariant() switch
    {
        "restartrequired" or "requiresrestart" or "requiresgamerestart" or "gamerestartrequired" or "gamerestart" => ConfigurationEffectTiming.GameRestart,
        "roundrestartrequired" or "requiresroundrestart" or "requiresnewround" or "newroundrequired" or "nextround" or
        "sessionrestartrequired" or "requiressessionrestart" => ConfigurationEffectTiming.NextRound,
        "applyimmediately" or "immediate" or "liveupdate" => ConfigurationEffectTiming.Immediate,
        _ => ConfigurationEffectTiming.Unspecified
    };
}
