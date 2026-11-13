#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewRepository
{
    private static readonly string RuntimeSession = Guid.NewGuid().ToString("N");
    private OverviewProfile? _appliedSnapshot;
    internal string LastSaveUncommittedDetails { get; private set; } = "";
    private readonly Dictionary<string, OverviewModRule> _temporaryRules = new(StringComparer.OrdinalIgnoreCase);
    private string AppliedSnapshotPath => Path.Combine(ProfileRuntimeDirectory, "applied-profile.json");
    public string AppliedProfileName => _appliedSnapshot?.name ?? Profiles.Applied.name;
    public bool HasUnappliedProfileChanges => GetPendingState().UnappliedCount > 0;

    private static string DraftSignature(OverviewState state, OverviewConfigSnapshot config, IEnumerable<OverviewProfileMod> mods)
        => JsonConvert.SerializeObject(new {
            state.pack,
            groups = state.groups.OrderBy(g => g.id, StringComparer.Ordinal),
            rules = state.mods.Where(MeaningfulRule).OrderBy(r => r.id, StringComparer.Ordinal).Select(r => new { r.id, r.groupId, r.order, r.versionLabel, r.updateAllowed, r.updateOverridden, r.alias, r.description, r.descriptionOverridden }),
            parameters = config.values.OrderBy(v => v.Identity, StringComparer.Ordinal).Select(v => new { v.Identity, v.type, v.serializedValue }),
            enabled = mods.OrderBy(m => m.guid, StringComparer.Ordinal).Select(m => new { m.guid, m.enabled, m.copy })
        });

    // Shared source discovery is not profile content. Legacy source fields remain readable,
    // while dirty detection uses the same boundary as the pending-item counters.
    private static bool MeaningfulRule(OverviewModRule r) => r.groupId.Length > 0 || r.order != 0 || r.versionLabel.Length > 0 ||
        !r.updateAllowed || r.updateOverridden || r.alias.Length > 0 || r.descriptionOverridden;

    private void MergeEditedRules()
    {
        foreach (var rule in _temporaryRules.Values.Where(MeaningfulRule))
            if (!State.mods.Any(r => r.id.Equals(rule.id, StringComparison.OrdinalIgnoreCase))) State.mods.Add(rule);
    }

    private void LoadAppliedSnapshot()
    {
        if (File.Exists(AppliedSnapshotPath))
        {
            var snapshot = OverviewProfileStore.DeserializeJson<OverviewProfile>(File.ReadAllText(AppliedSnapshotPath));
            if (!GroupTree.SupportedSchema(snapshot.schemaVersion) || snapshot.id != Profiles.AppliedId || !snapshot.IsValid)
                throw new InvalidDataException(Loc.T("The applied-profile record does not match the index. Restore a backup before trying again.", "已应用方案记录与方案索引不一致，请恢复备份后再试。", "已套用方案記錄與方案索引不一致，請還原備份後再試。"));
            snapshot.state ??= new(); snapshot.state.pack ??= new(); _appliedSnapshot = snapshot;
        }
        else
        {
            _appliedSnapshot = Clone(Profiles.Applied);
            AtomicWrite(AppliedSnapshotPath, JsonConvert.SerializeObject(_appliedSnapshot, Formatting.Indented));
        }
        SyncAppliedProfileName(Profiles.AppliedId, Profiles.Applied.name);
        ChangeLedger.AppliedProfile = _appliedSnapshot;
        LoadPendingState();
    }

    private void SyncAppliedProfileName(string id, string name)
    {
        if (_appliedSnapshot == null || _appliedSnapshot.id != id || _appliedSnapshot.name == name) return;
        var renamed = Clone(_appliedSnapshot); renamed.name = name; renamed.state.packNameOverride = "";
        AtomicWrite(AppliedSnapshotPath, JsonConvert.SerializeObject(renamed, Formatting.Indented));
        _appliedSnapshot = renamed; ChangeLedger.AppliedProfile = renamed;
    }

    private sealed class PendingConfiguration
    {
        public int schemaVersion = 1;
        public string profileId = "", session = "";
        public bool pending;
        public OverviewConfigSnapshot config = new();
        // Optional 1.5.7 fields. Older pending records remain valid.
        public List<ProfileRestartParameter> restartParameters = new();
        public List<ProfileRestartMod> restartMods = new();
        public string appliedIntroductionHash = "";
    }

    private OverviewToggleResult ApplyProfileTransaction(IReadOnlyList<OverviewMod> mods, bool mainMenu, bool skipEnablement = false, OverviewConfigSnapshot? parameterTargets = null, bool ignoreDependencyWarnings = false)
    {
        LastSaveUncommittedDetails = "";
        try { PackLibrary.EnsureAvailable(); } catch (Exception error) { return new(false, error.Message); }
        if (!ViewedProfile.IsValid) return new(false, Loc.T("The profile is unreadable; the current configuration is kept: ", "方案损坏，保留当前有效配置：", "方案損壞，保留目前有效設定：") + ViewedProfile.loadError);
        ModToggleService.ModToggleTransaction? toggle = null;
        OverviewConfigService.OverviewConfigTransaction? parameters = null;
        FlushRecoveryDraft(); // 1.1.4: the checkpoint below snapshots the recovery file
        string pendingPath = Path.Combine(ProfileRuntimeDirectory, "pending-profile.json");
        string oldApplied = Profiles.AppliedId;
        bool marked = false;
        var metadata = new ProfileMetadataCheckpoint(new[] { Profiles.IndexPath, Profiles.IndexPath + ".lastvalid", AppliedSnapshotPath, pendingPath,
            Path.Combine(ViewedProfile.directory, "profile.json"), Path.Combine(ViewedProfile.directory, "profile.json.lastvalid"),
            Path.Combine(ViewedProfile.directory, "config-values.json"),
            PackWorkspaceInformation.PathFor(Profiles.RootDirectory), RecoveryDraftPath });
        bool persistenceStarted = false, metadataCaptured = false;
        bool previousRestoreBaseline = _restoreSavedBaseline;
        var previousSavedBaseline = Clone(_savedDraftProfile);
        string previousSavedSignature = _savedDraftSignature;
        string previousRecovery = IsProfileDirty ? JsonConvert.SerializeObject(new RecoverableDraft { profileId = ViewedProfileId, state = State, config = _draftConfig, mods = _draftMods, runtimeBaseline = _editingRuntimeBaseline, savedBaseline = _savedDraftProfile, restoreSavedBaseline = _restoreSavedBaseline }, Formatting.Indented) : "";
        try
        {
            parameters = Configs.PrepareApply(parameterTargets ?? _draftConfig);
            // 2.3.1: a mod nested in one planned off is applied off; record that in the draft so the saved entry agrees.
            if (!skipEnablement) OverviewEnablementDraft.CarryOuterState(_draftMods, mods);
            var targets = mods.Where(m => PlannedEnabled(m) != m.TargetEnabled).Select(m => (m, PlannedEnabled(m))).ToList();
            var skippedTargets = skipEnablement ? targets.ToList() : targets.Take(0).ToList();
            var skipReasons = new List<string>();
            if (targets.Count > 0 && !skipEnablement)
            {
                var service = new ModToggleService(this, OverviewOptions.ManagerProfileDirectory.Value, knownMods: mods) { IgnoreDependencyWarnings = ignoreDependencyWarnings };
                ValidSaveSelection.Select(targets, candidate => {
                    var check = service.PrepareTargets(candidate, out var candidateTransaction);
                    if (check.Success) { toggle = candidateTransaction; return null; }
                    return check.Message;
                }, (target, reason) => {
                    skippedTargets.Add(target); skipReasons.Add(target.m.DisplayName + "：" + ConfigurationIssueReport.Explain(reason));
                });
            }
            parameters.Validate();
            if (toggle != null)
            {
                var valid = toggle.Validate();
                if (!valid.Success)
                {
                    skippedTargets = targets.ToList(); skipReasons.Add(ConfigurationIssueReport.Explain(valid.Message)); toggle = null;
                }
            }
            foreach (string file in new[] { AppliedSnapshotPath, pendingPath, Profiles.IndexPath })
                if (File.Exists(file)) {
                    if ((File.GetAttributes(file) & FileAttributes.ReadOnly) != 0) throw new IOException(Loc.T("The profile state file is read-only: ", "方案状态文件为只读：", "方案狀態檔案為唯讀：") + file);
                    using var writable = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                }
            foreach (var value in _draftConfig.values)
            {
                var deferred = parameters.DeferredSnapshot.values.FirstOrDefault(v => v.Identity == value.Identity);
                value.missing = deferred != null; value.missingReason = deferred?.missingReason ?? "";
            }
            parameters.Apply();
            if (toggle != null)
            {
                var result = toggle.Apply();
                if (!result.Success)
                {
                    var rollback = toggle.Rollback();
                    if (!rollback.Success) throw new InvalidOperationException(result.Message + "\n" + rollback.Message);
                    skippedTargets = targets.ToList(); skipReasons.Add(ConfigurationIssueReport.Explain(result.Message)); toggle = null;
                }
            }
            // Persist only after live/config preparation succeeded. A rejected callback or
            // failed rename must not silently consume the user's unsaved draft.
            var skipped = new HashSet<string>(skippedTargets.SelectMany(t => t.m.PluginGuids.Concat(new[] { t.m.Guid })), StringComparer.OrdinalIgnoreCase);
            // Commit only accepted targets. Keep skipped targets solely in the recovery draft.
            var workingConfig = _draftConfig; var workingMods = _draftMods;
            var committedConfig = Clone(_draftConfig); var committedMods = Clone(_draftMods);
            foreach (var deferred in parameters.DeferredSnapshot.values)
            {
                committedConfig.values.RemoveAll(v => v.Identity == deferred.Identity);
                var original = previousSavedBaseline.config.values.FirstOrDefault(v => v.Identity == deferred.Identity);
                if (original != null) committedConfig.values.Add(Clone(original));
            }
            foreach (var target in committedMods.Where(m => skipped.Contains(m.guid)))
            {
                target.enabled = mods.First(m => m.Guid.Equals(target.guid, StringComparison.OrdinalIgnoreCase) || m.PluginGuids.Contains(target.guid)).TargetEnabled;
            }
            persistenceStarted = true;
            try
            {
                _draftConfig = committedConfig; _draftMods = committedMods;
                Save();
            }
            finally { _draftConfig = workingConfig; _draftMods = workingMods; metadata.CaptureWritten(); metadataCaptured = true; }
            var snapshot = Clone(ViewedProfile);
            snapshot.state = Clone(State); snapshot.config = Clone(committedConfig); snapshot.mods = Clone(committedMods);
            if (parameterTargets != null && _appliedSnapshot != null)
            {
                var scopeIds = new HashSet<string>(parameterTargets.values.Select(v => v.Identity), StringComparer.Ordinal);
                snapshot = Clone(_appliedSnapshot);
                snapshot.config.values.RemoveAll(v => scopeIds.Contains(v.Identity));
                snapshot.config.values.AddRange(Clone(committedConfig.values.Where(v => scopeIds.Contains(v.Identity)).ToList()));
                snapshot.id = ViewedProfileId; snapshot.name = ViewedProfile.name;
            }
            foreach (var deferred in parameters.DeferredSnapshot.values)
            {
                snapshot.config.values.RemoveAll(v => v.Identity == deferred.Identity);
                var previous = _appliedSnapshot?.config.values.FirstOrDefault(v => v.Identity == deferred.Identity);
                if (previous != null) snapshot.config.values.Add(Clone(previous));
            }
            foreach (var target in snapshot.mods.Where(m => skipped.Contains(m.guid)))
            {
                var previous = _appliedSnapshot?.mods.FirstOrDefault(m => m.guid.Equals(target.guid, StringComparison.OrdinalIgnoreCase));
                if (previous != null) target.enabled = previous.enabled;
            }
            metadataCaptured = false;
            AtomicWrite(AppliedSnapshotPath, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
            var pending = AcceptedPendingState(parameters, mods, parameterTargets);
            if (skipped.Count > 0)
            {
                pending.restartMods.RemoveAll(m => skipped.Contains(m.guid));
                pending.restartMods.AddRange(_pendingConfiguration.restartMods.Where(m => skipped.Contains(m.guid)));
            }
            AtomicWrite(pendingPath, JsonConvert.SerializeObject(pending, Formatting.Indented));
            marked = true; Profiles.MarkApplied(ViewedProfileId);
            metadata.CaptureWritten(); metadataCaptured = true;
            PersistRecoveryDraft();
            metadata.CaptureWritten();
            if (toggle != null) { var committed = toggle.Commit(); if (!committed.Success) throw new IOException(committed.Message); }
            parameters.Commit();
            _pendingConfiguration = pending; _appliedIntroductionHash = pending.appliedIntroductionHash;
            _appliedSnapshot = snapshot; ChangeLedger.AppliedProfile = snapshot; _loadedRevision = ++_sharedRevision;
            InvalidatePendingView();
            int nextRound = pending.restartParameters.Count(p => p.effectTiming == ConfigurationEffectTiming.NextRound);
            int restart = pending.restartParameters.Count - nextRound + pending.restartMods.Count;
            string notice = Loc.F("Configuration saved: {0}.", "配置已保存：{0}。", "設定已儲存：{0}。", ViewedProfile.name);
            if (nextRound > 0) notice += " " + Loc.F("{0} changes take effect next round.", "{0} 项重新开局生效。", "{0} 項重新開局生效。", nextRound);
            if (restart > 0) notice += " " + Loc.F("{0} changes will take effect after restarting the game.", "{0} 项改动将在重启游戏后生效。", "{0} 項變更將在重新啟動遊戲後生效。", restart);
            if (parameters.HasDeferredChanges) notice += " " + Loc.F("{0} items were skipped and remain unsaved; see parameter details.", "{0} 项已跳过并保留未保存标记，原因见参数详情。", "{0} 項已略過並保留未儲存標記，原因見參數詳情。", parameters.DeferredSnapshot.values.Count);
            if (skipEnablement && targets.Count > 0) notice += " " + Loc.F("Skipped {0} enablement changes; original files retained, targets still pending.", "已跳过 {0} 项启停改动；文件保留原状态，目标仍未保存。", "已略過 {0} 項啟停變更；檔案保留原狀態，目標仍未儲存。", targets.Count);
            var pendingDetails = new System.Text.StringBuilder();
            if (skipReasons.Count > 0)
            {
                pendingDetails.AppendLine(Loc.T("Enablement not saved:", "未保存的模组启停：", "未儲存的模組啟停："));
                for (int i = 0; i < skipReasons.Count; i++) pendingDetails.AppendLine((i + 1) + ". " + skipReasons[i] + "\n   " + Loc.T("Suggestion: review the mod's paths and retain the target as a draft until corrected.", "处理建议：核对该模组文件位置，处理后再次保存；本次目标仍在草稿中。", "處理建議：核對該模組檔案位置，處理後再次儲存；目標仍在草稿中。"));
            }
            if (parameters.HasDeferredChanges)
            {
                pendingDetails.AppendLine(Loc.T("Settings not saved:", "未保存的配置项：", "未儲存的設定項："));
                int itemIndex = 0;
                foreach (var deferred in parameters.DeferredSnapshot.values)
                {
                    string name = mods.FirstOrDefault(m => m.Guid.Equals(deferred.pluginGuid, StringComparison.OrdinalIgnoreCase) || m.PluginGuids.Contains(deferred.pluginGuid))?.Name ?? deferred.pluginGuid;
                    pendingDetails.AppendLine(++itemIndex + ". " + name + " / [" + deferred.section + "] " + deferred.key);
                    pendingDetails.AppendLine("   " + Loc.LocalizeMessage(deferred.missingReason));
                    pendingDetails.AppendLine("   " + Loc.T("Suggestion: review the setting type/value; update the draft or restore its default, then save again.", "处理建议：查看该配置项，核对类型及允许的值；调整草稿或恢复默认后再次保存。", "處理建議：查看該設定項，核對類型與允許值；調整草稿或恢復預設後再次儲存。"));
                }
            }
            LastSaveUncommittedDetails = pendingDetails.ToString();
            if (skipReasons.Count > 0) notice += " " + Loc.F("{0} enablement targets remain unsaved; see the result list.", "{0} 项启停仍未保存，详见结果清单。", "{0} 項啟停仍未儲存，詳見結果清單。", skippedTargets.Count);
            return new(true, notice);
        }
        catch (Exception ex)
        {
            _restoreSavedBaseline = previousRestoreBaseline;
            _savedDraftProfile = previousSavedBaseline; _savedDraftSignature = previousSavedSignature; InvalidatePendingView();
            var errors = new List<string>();
            if (persistenceStarted) try { if (!metadataCaptured) metadata.CaptureWritten(); metadata.Restore(); Profiles.RefreshCommittedCache(); } catch (Exception metadataError) { errors.Add(metadataError.Message); }
            if (previousRecovery.Length > 0) try { AtomicWrite(RecoveryDraftPath, previousRecovery); } catch (Exception recoveryError) { errors.Add(recoveryError.Message); }
            try { if (toggle != null) { var rollback = toggle.Rollback(); if (!rollback.Success) errors.Add(rollback.Message); } } catch (Exception e) { errors.Add(e.Message); }
            try { parameters?.Rollback(); } catch (Exception e) { errors.Add(e.Message); }
            try { if (marked) Profiles.MarkApplied(oldApplied); } catch (Exception e) { errors.Add(e.Message); }
            return new(false, Loc.T("Apply failed; the previously applied profile is kept: ", "应用失败，保留原方案：", "套用失敗，保留原方案：") + ex.Message +
                (errors.Count == 0 ? Loc.T("; the file and parameter changes were rolled back.", "；已回退本次文件和参数改动。", "；已回復本次檔案及參數變更。") :
                    Loc.T("; check rollback: ", "；回退需检查：", "；回復需檢查：") + string.Join("; ", errors)));
        }
    }

}
