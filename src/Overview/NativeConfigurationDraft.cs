#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using PEAKLib.ModConfig;
using Zorro.Settings;

namespace ModConfigEnhance.Overview;

/// <summary>Native ModConfig controls edit Setting.Value, never live ConfigEntry values until save.</summary>
internal static class NativeConfigurationDraft
{
    internal static OverviewRepository? Repository;
    [ThreadStatic] private static int _committing;
    private static bool Editing(object instance)
    {
        if (_committing != 0 || instance is not IBepInExProperty property || property.Pluginfo.Metadata.GUID == Plugin.PluginGuid) return false;
        if (Repository == null)
        {
            using var initialization = new CommitScope();
            Repository = new OverviewRepository();
        }
        return true;
    }

    internal sealed class CommitScope : IDisposable
    {
        internal CommitScope() { _committing++; }
        public void Dispose() { _committing--; }
    }

    internal static void Install(Harmony harmony)
    {
        var targets = typeof(IBepInExProperty).Assembly.GetTypes()
            .Where(t => !t.ContainsGenericParameters && typeof(IBepInExProperty).IsAssignableFrom(t) && typeof(Setting).IsAssignableFrom(t)).ToArray();
        var patched = new HashSet<MethodBase>();
        foreach (var ancestor in targets.SelectMany(type => Ancestors(type)).Where(t => !t.ContainsGenericParameters && typeof(IBepInExProperty).IsAssignableFrom(t)))
            foreach (var method in ancestor.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.Name is not ("Save" or "ApplyValue" or "SetDefaultValue" or "RefreshValueFromConfig") || method.IsAbstract || !patched.Add(method)) continue;
                if (method.Name == "RefreshValueFromConfig") harmony.Patch(method, postfix: new HarmonyMethod(typeof(NativeConfigurationDraft), nameof(Refresh)));
                else harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeConfigurationDraft), method.Name == "Save" ? nameof(Save) : method.Name == "SetDefaultValue" ? nameof(Default) : nameof(Apply)));
            }
        foreach (var type in typeof(IBepInExProperty).Assembly.GetTypes().Where(t => t.Namespace == "PEAKLib.ModConfig.SettingOptions.SettingUI"))
        {
            var setup = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .FirstOrDefault(m => m.Name == "Setup" && !m.IsAbstract && !m.ContainsGenericParameters);
            if (setup?.DeclaringType == type && patched.Add(setup)) harmony.Patch(setup, postfix: new HarmonyMethod(typeof(NativeConfigurationDraft), nameof(SetupControl)));
        }
        // Ancestors above already cover closed generic declaring types. Never patch
        // the same inherited implementation again through a reflected derived type:
        // Mono/Harmony can otherwise produce two detours for one shared method body.
    }

    private static IEnumerable<Type> Ancestors(Type type)
    {
        for (Type? current = type; current != null; current = current.BaseType) yield return current;
    }

    private static void SetupControl(object __instance, object[] __args)
    {
        if (__instance is not UnityEngine.Component component) return;
        var setting = __args.OfType<Setting>().FirstOrDefault();
        if (setting is not IBepInExProperty property || !Editing(setting)) return;
        var host = component.transform as UnityEngine.RectTransform; if (host == null) return;
        var previous = host.Find("MCE_SaveSetting");
        if (previous != null) { previous.gameObject.SetActive(false); UnityEngine.Object.Destroy(previous.gameObject); }
        var save = OverviewUi.Button(host, Loc.T("Save", "保存", "儲存"), () => {
            var repo = Repository!;
            var mods = new OverviewCatalogue().Scan(repo);
            ConfigurationSaveRequest.Run(repo, mods, ConfigurationSaveScope.ModParameters, property.Pluginfo.Metadata.GUID, result => {
                if (!result.Success) OverviewUi.ShowText(Loc.T("Save settings", "保存设置", "儲存設定"), result.Message);
            });
        }, 70, 28, UiActionRole.Commit);
        save.name = "MCE_SaveSetting";
        var rect = (UnityEngine.RectTransform)save.transform;
        // 2.3.11 (user): side by side with ModConfig's Default button instead of stacked above it. ModConfig puts
        // Default at x=+186 from the cell centre (90 wide); Save sits just left of it on the same row.
        rect.anchorMin = rect.anchorMax = rect.pivot = new UnityEngine.Vector2(.5f, .5f);
        rect.anchoredPosition = new UnityEngine.Vector2(186f - 45f - 6f - 35f, 0f);
        SaveSlotMarker.Mark(rect);
        ProfilePendingMarkers.Attach(rect, () => Repository?.GetPendingState().Parameter(property.Pluginfo.Metadata.GUID, property.ConfigBase),
            text => { }, () => { }, reserveText: false);
    }

    private static PropertyInfo? ValueProperty(object setting) => AccessTools.Property(setting.GetType(), "Value");
    private static object ConvertValue(object value, Type target)
    {
        if (target == typeof(bool) && value.GetType().IsEnum) return value.ToString() == "ON";
        if (target.IsEnum && value is string text) return Enum.Parse(target, text);
        if (target.IsInstanceOfType(value)) return value;
        return Convert.ChangeType(value, target);
    }
    private static bool Save(object __instance)
    {
        if (!Editing(__instance)) return true;
        var property = (IBepInExProperty)__instance;
        var value = ValueProperty(__instance)?.GetValue(__instance) ?? throw new InvalidOperationException("Native setting has no editable Value.");
        string serialized = TomlTypeConverter.ConvertToString(ConvertValue(value, property.ConfigBase.SettingType), property.ConfigBase.SettingType);
        if (!Repository!.SetDraftConfig(property.Pluginfo.Metadata.GUID, property.ConfigBase, serialized, out string error))
            throw new InvalidOperationException(error);
        return false;
    }
    private static bool Apply(object __instance) => !Editing(__instance);
    private static bool Default(object __instance)
    {
        if (!Editing(__instance)) return true;
        var property = (IBepInExProperty)__instance;
        if (!Repository!.SetDraftConfig(property.Pluginfo.Metadata.GUID, property.ConfigBase,
            TomlTypeConverter.ConvertToString(property.ConfigBase.DefaultValue, property.ConfigBase.SettingType), out string error)) throw new InvalidOperationException(error);
        Refresh(__instance); return false;
    }
    private static void Refresh(object __instance)
    {
        if (!Editing(__instance)) return;
        var property = (IBepInExProperty)__instance;
        var target = Repository!.FindDraftConfig(property.Pluginfo.Metadata.GUID, property.ConfigBase);
        var destination = ValueProperty(__instance);
        if (target == null || destination?.GetSetMethod(true) == null) return;
        object value = TomlTypeConverter.ConvertToValue(target.serializedValue, property.ConfigBase.SettingType);
        if (property.ConfigBase.SettingType == typeof(bool) && destination.PropertyType.IsEnum)
            value = Enum.Parse(destination.PropertyType, (bool)value ? "ON" : "OFF");
        else if (destination.PropertyType == typeof(string) && value.GetType().IsEnum) value = value.ToString()!;
        destination.SetValue(__instance, value);
    }
}
