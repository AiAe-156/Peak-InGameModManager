#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;

internal enum ConfigurationSaveScope { All, Hotkeys, ModParameters, Parameters, Information, Groups, Introduction, ModInformation, HotkeyEntry }

internal sealed partial class OverviewRepository
{
    internal void SavePackInformation(ConfigurationSaveScope scope, string modId = "")
    {
        EnsureLocalPackCopy();
        // A rule handed out by GetRule lives in _temporaryRules until something merges it. Without this a group
        // membership set through GetRule (smart grouping, the group editor) was saved without the member.
        MergeEditedRules();
        var committed = Clone(_savedDraftProfile.state);
        if (scope == ConfigurationSaveScope.Groups)
        {
            committed.groups = Clone(State.groups);
            foreach (var rule in State.mods)
            {
                var target = committed.mods.FirstOrDefault(r => r.id == rule.id);
                if (target == null) { target = new OverviewModRule { id = rule.id }; committed.mods.Add(target); }
                target.groupId = rule.groupId; target.order = rule.order;
            }
        }
        else if (scope == ConfigurationSaveScope.Introduction)
        {
            committed.pack.introDocument = State.pack.introDocument;
            committed.pack.introFile = State.pack.introFile;
            committed.pack.introEnabled = State.pack.introEnabled;
        }
        else
        {
            if (scope != ConfigurationSaveScope.ModInformation) committed.pack = Clone(State.pack);
            foreach (var rule in State.mods.Where(r => scope != ConfigurationSaveScope.ModInformation || r.id.Equals(modId, StringComparison.OrdinalIgnoreCase)))
            {
                var target = committed.mods.FirstOrDefault(r => r.id == rule.id);
                if (target == null) { target = new OverviewModRule { id = rule.id }; committed.mods.Add(target); }
                target.versionLabel = rule.versionLabel; target.updateAllowed = rule.updateAllowed; target.updateOverridden = rule.updateOverridden;
                target.alias = rule.alias; target.description = rule.description; target.descriptionOverridden = rule.descriptionOverridden;
            }
        }
        FlushRecoveryDraft(); // 1.1.4: the checkpoint below snapshots the recovery file
        var previousSaved = Clone(_savedDraftProfile);
        string previousSignature = _savedDraftSignature;
        var previousApplied = _appliedSnapshot;
        var checkpoint = new ProfileMetadataCheckpoint(new[] {
            PackWorkspaceInformation.PathFor(Profiles.RootDirectory), AppliedSnapshotPath, RecoveryDraftPath
        });
        try
        {
            PackWorkspaceInformation.Write(Profiles.RootDirectory, committed, PackDirectory);
            _savedDraftProfile.state = Clone(committed);
            _savedDraftSignature = DraftSignature(_savedDraftProfile.state, _savedDraftProfile.config, _savedDraftProfile.mods);
            if (_appliedSnapshot != null)
            {
                var applied = Clone(_appliedSnapshot); applied.state = Clone(committed);
                AtomicWrite(AppliedSnapshotPath, Newtonsoft.Json.JsonConvert.SerializeObject(applied, Newtonsoft.Json.Formatting.Indented));
                _appliedSnapshot = applied;
            }
            PersistRecoveryDraft();
        }
        catch
        {
            _savedDraftProfile = previousSaved; _savedDraftSignature = previousSignature;
            _appliedSnapshot = previousApplied;
            checkpoint.CaptureWritten(); checkpoint.Restore();
            throw;
        }
        _loadedRevision = ++_sharedRevision;
        OverviewPageCache.ConfigurationSaved();
    }

    internal OverviewToggleResult SaveConfiguration(IReadOnlyList<OverviewMod> mods, ConfigurationSaveScope scope = ConfigurationSaveScope.All, string modGuid = "", bool ignoreDependencyWarnings = false, bool overwriteExternal = false, bool skipEnablement = false)
    {
        bool Match(OverviewConfigValue value)
        {
            if (scope is ConfigurationSaveScope.All or ConfigurationSaveScope.Parameters) return true;
            if (scope == ConfigurationSaveScope.ModParameters) return value.pluginGuid.Equals(modGuid, StringComparison.OrdinalIgnoreCase);
            if (scope == ConfigurationSaveScope.HotkeyEntry) return value.Identity.Equals(modGuid, StringComparison.Ordinal); // modGuid carries the identity
            if (scope != ConfigurationSaveScope.Hotkeys) return false;
            var entry = Configs.FindLoadedEntry(value);
            return entry != null ? OverviewHotkeyAdapter.Create(entry) != null :
                value.type == "BepInEx.Configuration.KeyboardShortcut" || value.type == "UnityEngine.KeyCode"
                || value.type == "UnityEngine.InputSystem.Key" || value.type == "UnityEngine.InputSystem.LowLevel.GamepadButton";
        }
        string? conflict = Configs.Reconcile(_editingRuntimeBaseline, _draftConfig, Match, overwriteExternal);
        if (conflict != null) return new OverviewToggleResult(false, conflict);
        if (scope == ConfigurationSaveScope.All)
        {
            var all = ApplyProfileTransaction(mods, IsMainMenuScene, skipEnablement: skipEnablement, ignoreDependencyWarnings: ignoreDependencyWarnings);
            if (all.Success)
            {
                GamepadCombos.ApplyPresets(State); // 1.1.4: the profile's controller keys become this PC's combos
                _editingRuntimeBaseline = Configs.Capture(_draftConfig);
            }
            return all;
        }
        var draftState = State; var draftConfig = _draftConfig; var draftMods = _draftMods;
        var saved = _savedDraftProfile; string signature = _savedDraftSignature;
        try
        {
            State = Clone(saved.state); _draftConfig = Clone(saved.config); _draftMods = Clone(saved.mods);
            _draftConfig.values.RemoveAll(v => Match(v));
            _draftConfig.values.AddRange(draftConfig.values.Where(Match).Select(Clone));
            if (scope == ConfigurationSaveScope.Information) State.pack = Clone(draftState.pack);
            if (scope == ConfigurationSaveScope.Groups)
            {
                State.groups = Clone(draftState.groups);
                foreach (var rule in draftState.mods)
                {
                    var target = State.mods.FirstOrDefault(r => r.id.Equals(rule.id, StringComparison.OrdinalIgnoreCase));
                    if (target == null) { target = new OverviewModRule { id = rule.id }; State.mods.Add(target); }
                    target.groupId = rule.groupId; target.order = rule.order;
                }
            }
            var scopedTargets = Clone(_draftConfig); scopedTargets.values = scopedTargets.values.Where(Match).ToList();
            var result = ApplyProfileTransaction(mods, IsMainMenuScene, skipEnablement: true, parameterTargets: scopedTargets);
            if (!result.Success) { _savedDraftProfile = saved; _savedDraftSignature = signature; }
            else
            {
                var updated = Configs.Capture(_draftConfig);
                _editingRuntimeBaseline.values.RemoveAll(Match);
                _editingRuntimeBaseline.values.AddRange(updated.values.Where(Match).Select(Clone));
            }
            return result;
        }
        finally
        {
            State = draftState; _draftConfig = draftConfig; _draftMods = draftMods;
            PersistRecoveryDraft();
            // ApplyProfileTransaction persisted only the selected scope. The unsaved values
            // remain in this working draft and compare against that new committed baseline.
            _loadedRevision = ++_sharedRevision;
            InvalidatePendingView();
        }
    }
}
