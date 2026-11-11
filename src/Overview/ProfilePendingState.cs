#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

/// <summary>A single comparison of the viewed draft, its saved baseline and the running game.</summary>
internal sealed class ProfilePendingState
{
    internal int UnsavedCount, UnappliedCount, RestartCount, RoundRestartCount;
    internal readonly Dictionary<string, ProfileItemPending> Parameters = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, ProfileItemPending> Mods = new(StringComparer.OrdinalIgnoreCase);
    // These dictionaries include unchanged loaded entries so a row lookup never scans configs.
    internal readonly Dictionary<ConfigEntryBase, string> EntryIdentities = new();

    internal ProfileItemPending? Parameter(string guid, ConfigEntryBase entry)
        => EntryIdentities.TryGetValue(entry, out string? id) && Parameters.TryGetValue(id, out var value) &&
           id.StartsWith(guid.ToUpperInvariant() + "\n", StringComparison.Ordinal) ? value : null;
    internal ProfileItemPending? Mod(string guid) => Mods.TryGetValue(guid, out var value) ? value : null;
    internal void Count(ProfileItemPending item)
    {
        if (item.NeedsSave) UnsavedCount++;
        if (item.NeedsApply) UnappliedCount++;
        if (item.NeedsRestart) { if (item.EffectTiming == ConfigurationEffectTiming.NextRound) RoundRestartCount++; else RestartCount++; }
    }

    internal static ProfilePendingState Compare(OverviewProfile saved, OverviewProfile draft, OverviewProfile applied,
        IReadOnlyDictionary<string, ConfigEntryBase> loaded, IReadOnlyDictionary<string, bool> loadedMods,
        IReadOnlyList<ProfileRestartParameter> restartParameters, IReadOnlyList<ProfileRestartMod> restartMods,
        bool introductionDirty, bool introductionUnapplied, IReadOnlyDictionary<string, string>? offlineValues = null)
    {
        var result = new ProfilePendingState();
        foreach (var pair in loaded) result.EntryIdentities[pair.Value] = pair.Key;
        var savedValues = saved.config.values.GroupBy(v => v.Identity).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var draftValues = draft.config.values.GroupBy(v => v.Identity).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var restartValues = restartParameters.GroupBy(v => v.target.Identity).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        foreach (string id in savedValues.Keys.Union(draftValues.Keys).Union(restartValues.Keys))
        {
            savedValues.TryGetValue(id, out var before); draftValues.TryGetValue(id, out var wanted);
            loaded.TryGetValue(id, out var entry); restartValues.TryGetValue(id, out var restart);
            string actual = entry?.GetSerializedValue() ?? "";
            var item = new ProfileItemPending { Saved = before?.serializedValue ?? "", Draft = wanted?.serializedValue ?? "", Current = actual,
                SavedRecorded = before != null, DraftRecorded = wanted != null, CurrentAvailable = entry != null,
                NeedsSave = !SameTarget(before, wanted) };
            if (restart != null && entry != null && restart.target.type == (entry.SettingType.FullName ?? entry.SettingType.Name) &&
                restart.target.serializedValue == actual && actual != restart.previous)
            { item.NeedsRestart = true; item.RestartPrevious = restart.previous; item.EffectTiming = restart.effectTiming; }
            if (wanted != null)
            {
                if (entry == null && offlineValues != null && offlineValues.TryGetValue(id, out string? diskValue))
                {
                    item.Current = diskValue;
                    item.NeedsApply = wanted.serializedValue != diskValue;
                    result.Parameters[id] = item; result.Count(item); continue;
                }
                if (entry == null)
                    item.UnavailableReason = Loc.T("The configuration file or key has not been written yet; saving will prepare it for the mod.", "配置文件或字段尚未写入，保存后供模组使用。", "設定檔或欄位尚未寫入，儲存後供模組使用。");
                else if (wanted.type != (entry.SettingType.FullName ?? entry.SettingType.Name))
                    item.UnavailableReason = Loc.T("The setting type has changed; its original target is retained.", "配置项类型已改变；已保留原方案目标。", "設定項類型已變更；已保留原方案目標。");
                item.NeedsApply = item.UnavailableReason.Length > 0 || wanted.serializedValue != actual;
            }
            result.Parameters[id] = item; result.Count(item);
        }
        var savedMods = saved.mods.GroupBy(m => m.guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        var draftMods = draft.mods.GroupBy(m => m.guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        var restartModMap = restartMods.GroupBy(m => m.guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        foreach (string guid in savedMods.Keys.Union(draftMods.Keys, StringComparer.OrdinalIgnoreCase).Union(restartModMap.Keys, StringComparer.OrdinalIgnoreCase))
        {
            savedMods.TryGetValue(guid, out var before); draftMods.TryGetValue(guid, out var wanted); restartModMap.TryGetValue(guid, out var restart);
            bool actual = loadedMods.TryGetValue(guid, out bool isLoaded) && isLoaded;
            var item = new ProfileItemPending { Saved = before?.enabled.ToString() ?? "", Draft = wanted?.enabled.ToString() ?? "", Current = actual.ToString(),
                SavedRecorded = before != null, DraftRecorded = wanted != null,
                NeedsSave = before == null ? wanted != null : wanted == null || before.enabled != wanted.enabled };
            item.NeedsRestart = restart != null && restart.enabled != actual;
            if (item.NeedsRestart) item.RestartPrevious = restart!.wasEnabled.ToString();
            // Applying changes the next-launch files; it does not unload the running plugin.
            // Reversing a pending restart therefore needs another apply even when the
            // new profile target happens to match the currently loaded plugin.
            bool nextLaunch = restart?.enabled ?? actual;
            item.NeedsApply = wanted != null && wanted.enabled != nextLaunch;
            result.Mods[guid] = item; result.Count(item);
        }
        // Group properties and its member assignments are a single logical change.
        var savedGroups = GroupSignatures(saved.state); var draftGroups = GroupSignatures(draft.state); var appliedGroups = GroupSignatures(applied.state);
        foreach (string id in savedGroups.Keys.Union(draftGroups.Keys).Union(appliedGroups.Keys))
        {
            savedGroups.TryGetValue(id, out string? before); draftGroups.TryGetValue(id, out string? wanted); appliedGroups.TryGetValue(id, out string? actual);
            if (before != wanted) result.UnsavedCount++;
            if (wanted != actual) result.UnappliedCount++;
        }
        if (JsonConvert.SerializeObject(saved.state.pack) != JsonConvert.SerializeObject(draft.state.pack)) result.UnsavedCount++;
        if (JsonConvert.SerializeObject(applied.state.pack) != JsonConvert.SerializeObject(draft.state.pack)) result.UnappliedCount++;
        if (introductionDirty) result.UnsavedCount++;
        if (introductionDirty || introductionUnapplied) result.UnappliedCount++;
        return result;
    }

    private static bool SameTarget(OverviewConfigValue? a, OverviewConfigValue? b) => a == null ? b == null :
        b != null && a.type == b.type && a.serializedValue == b.serializedValue;
    private static Dictionary<string, string> GroupSignatures(OverviewState state)
    {
        var rules = state.mods.Where(r => r.groupId.Length > 0 || r.order != 0 || r.versionLabel.Length > 0 || !r.updateAllowed || r.updateOverridden || r.alias.Length > 0 || r.descriptionOverridden).ToList();
        return state.groups.Select(g => g.id).Concat(rules.Select(r => r.groupId)).Distinct(StringComparer.Ordinal).ToDictionary(id => id,
            id => JsonConvert.SerializeObject(new { group = state.groups.FirstOrDefault(g => g.id == id),
                members = rules.Where(r => r.groupId == id).OrderBy(r => r.id, StringComparer.Ordinal).Select(r => new { r.id, r.order, r.versionLabel, r.updateAllowed, r.updateOverridden, r.alias, r.description, r.descriptionOverridden }) }), StringComparer.Ordinal);
    }
}

internal sealed class ProfileItemPending
{
    internal bool NeedsSave, NeedsApply, NeedsRestart;
    internal ConfigurationEffectTiming EffectTiming;
    internal bool SavedRecorded, DraftRecorded, CurrentAvailable = true;
    internal string Saved = "", Draft = "", Current = "";
    internal string UnavailableReason = "", RestartPrevious = "";
    internal bool HasStatus => NeedsSave || NeedsApply || NeedsRestart;
}

internal sealed class ProfileRestartParameter
{
    public OverviewConfigValue target = new();
    public string previous = "";
    public ConfigurationEffectTiming effectTiming = ConfigurationEffectTiming.GameRestart;
}

internal sealed class ProfileRestartMod
{
    public string guid = "";
    public bool enabled, wasEnabled;
}
