#nullable enable
using System;
using System.Globalization;
using HarmonyLib;
using PEAKLib.ModConfig;
using Zorro.Settings;
using Zorro.Settings.UI;

namespace ModConfigEnhance;

/// <summary>
/// PEAK 3.1.0 changed <see cref="FloatSettingUI"/> to show every slider value through a new private
/// <c>GetFormattedString</c>, which for the default display type returns <c>value.ToString("0")</c>. That was meant for
/// the game's own sliders, but PEAKLib.ModConfig's float/double settings reuse the same cell, so every mod parameter
/// lost its decimals (0.35 → "0", 1.25 → "1"). Worse, the cell's <c>OnChanged</c> re-parses the field on every
/// keystroke and writes the rounded text back, so typing "1.25" ends up storing a wrong value.
///
/// This patch only takes over for settings that belong to a mod (<see cref="IBepInExProperty"/>): their values are
/// shown with up to three decimals ("0.###"), and while the user is typing in the field the text is left as typed so
/// the value is parsed from what they entered. The game's own settings are untouched and keep the new look.
/// </summary>
internal static class ModFloatDisplayPatch
{
    internal static string State { get; private set; } = "not installed";

    internal static void Apply(Harmony harmony)
    {
        try
        {
            var target = AccessTools.Method(typeof(FloatSettingUI), "GetFormattedString", new[] { typeof(float) });
            if (target == null) { State = "GetFormattedString not found (game older than 3.1.0); nothing to fix"; return; }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(ModFloatDisplayPatch), nameof(Prefix)));
            State = "installed";
        }
        catch (Exception ex) { State = "failed: " + ex.Message; Plugin.Log.LogWarning("[小数显示] 补丁挂载失败：" + ex.Message); }
    }

    private static bool Prefix(FloatSettingUI __instance, float value, ref string __result)
    {
        if (__instance == null || __instance._listeningSetting is not IBepInExProperty) return true;
        // While the field has focus the user is typing: hand back their own text so the next keystroke parses what
        // they typed, instead of the cell overwriting "1.2" with a re-formatted value mid-edit.
        var field = __instance.inputField;
        if (field != null && field.isFocused && float.TryParse(field.text, NumberStyles.Float, CultureInfo.CurrentCulture, out float typed)
            && Math.Abs(typed - value) < 1e-6f)
        {
            __result = field.text;
            return false;
        }
        __result = Format(value);
        return false;
    }

    /// <summary>Up to three decimals, no trailing zeros; integers stay plain ("2", "0.35", "1.25", "0.001").</summary>
    internal static string Format(float value) => value.ToString("0.###", CultureInfo.CurrentCulture);
}
