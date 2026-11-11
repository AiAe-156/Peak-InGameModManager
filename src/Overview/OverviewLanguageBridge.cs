#nullable enable
using System;
using System.Collections.Generic;

namespace ModConfigEnhance.Overview;

internal static class OverviewLanguageBridge
{
    internal static int Revision => Loc.TranslationRevision;
    internal static string Selection => Plugin.LanguageFile.Value ?? "";
    internal static List<Loc.LanguageFile> LanguageOptions => Loc.Scan();
    internal static string CurrentDisplayName => Selection.Length == 0 ? Loc.Get("LANG_FOLLOW") : Loc.SelectedLanguageGroup?.DisplayOrStem ?? Selection;
    internal static bool Select(string selection, out string error)
    {
        string previous = Selection; error = "";
        Plugin.LanguageFile.Value = selection ?? "";
        var result = Loc.Apply(selection ?? "", notify: false, refresh: true);
        if (!result.Success) { Plugin.LanguageFile.Value = previous; Loc.InvalidateEditingTranslations(); error = result.Message; return false; }
        return true;
    }
    internal static string GetDisplayName(OverviewMod mod)
    {
        if (string.Equals(mod.Guid, Plugin.PluginGuid, StringComparison.OrdinalIgnoreCase))
            return Loc.T("In-Game Mod Manager", "游戏内模组管理器", "遊戲內模組管理器");
        string raw = string.IsNullOrEmpty(mod.RawName) ? mod.Name : mod.RawName;
        if (Loc.ShowingOriginal) return raw; // 2.5.0「显示原文」：别名与译名都先收起
        if (!string.IsNullOrWhiteSpace(mod.Rule.alias)) return mod.Rule.alias;
        string settingName = string.IsNullOrEmpty(mod.SettingDisplayName) ? Plugin.FixNaming(raw) : mod.SettingDisplayName;
        return GetName(raw, settingName);
    }
    internal static string DisplayNameWithOriginal(OverviewMod mod)
    {
        string primary = GetDisplayName(mod), original = string.IsNullOrWhiteSpace(mod.RawName) ? mod.Name : mod.RawName;
        string Safe(string text) => text.Replace("<", "＜").Replace(">", "＞");
        return Safe(primary) + (ModNameLayout.Equivalent(primary, original) ? "" : " <size=70%><color=#777E78>（" + Safe(original) + "）</color></size>");
    }
    internal static string GetName(string rawName, string settingName)
    {
        if (Loc.ShowingOriginal) return settingName;
        var entries = Loc.SelectedLanguageGroup?.Entries;
        if (entries != null)
        {
            if (entries.TryGetValue(settingName.Trim(), out string? translated) && !string.IsNullOrWhiteSpace(translated)) return translated;
            if (entries.TryGetValue(rawName.Trim(), out translated) && !string.IsNullOrWhiteSpace(translated)) return translated;
        }
        return Loc.TranslateExternal(settingName);
    }
    internal static string GetDescription(OverviewMod mod) => mod.Rule.descriptionOverridden && !Loc.ShowingOriginal ? mod.Rule.description : GetDescription(mod.Guid, string.IsNullOrEmpty(mod.RawDescription) ? mod.Description : mod.RawDescription);
    private static string DescriptionKey(string guid) => "ModConfigEnhance.Description." + guid;
    internal static string GetDescription(string guid, string original)
    {
        if (!string.IsNullOrWhiteSpace(original)) return Translate(original);
        string key = DescriptionKey(guid);
        if (!string.IsNullOrWhiteSpace(guid) && !Loc.ShowingOriginal && Loc.SelectedLanguageGroup?.Entries.TryGetValue(key, out string? value) == true && !string.IsNullOrEmpty(value) && value != key) return value;
        return "";
    }
    internal static string GetSettingName(string settingName)
    {
        foreach (var plugin in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            if (Plugin.FixNaming(plugin.Metadata.Name) == settingName) return GetName(plugin.Metadata.Name, settingName);
        return Translate(settingName);
    }
    internal static string Translate(string original) => Loc.TranslateExternal(original);
    internal static bool SetName(OverviewMod mod, string value, out string error)
    {
        string raw = string.IsNullOrEmpty(mod.RawName) ? mod.Name : mod.RawName;
        string settingName = string.IsNullOrEmpty(mod.SettingDisplayName) ? Plugin.FixNaming(raw) : mod.SettingDisplayName;
        return Loc.WriteExternalTranslations(new[] { raw, settingName }, value, out error);
    }
    internal static bool SetDescription(OverviewMod mod, string value, out string error)
    {
        string raw = string.IsNullOrEmpty(mod.RawDescription) ? mod.Description : mod.RawDescription;
        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.IsNullOrWhiteSpace(mod.Guid)) { error = Loc.T("The mod identity is unavailable.", "无法确认该模组身份。", "無法確認該模組身分。"); return false; }
            return Loc.WriteExternalTranslations(new[] { DescriptionKey(mod.Guid) }, value, out error);
        }
        return Loc.WriteExternalTranslations(new[] { raw }, value, out error);
    }
}

