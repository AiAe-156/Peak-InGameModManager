#nullable enable
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewRepository
{
    private sealed class RecoverableDraft
    {
        public int schemaVersion = 1;
        public string profileId = "";
        public OverviewState state = new();
        public OverviewConfigSnapshot config = new();
        public OverviewConfigSnapshot? runtimeBaseline;
        public OverviewProfile? savedBaseline;
        public bool restoreSavedBaseline;
        public System.Collections.Generic.List<OverviewProfileMod> mods = new();
    }
    private bool _restoreSavedBaseline;
    private string RecoveryDraftPath => Path.Combine(ProfileRuntimeDirectory, "drafts", OverviewProfileExport.PortableId(ViewedProfileId) + ".json");
    internal void PersistRecoveryDraft()
    {
        InvalidatePendingView();
        if (_appliedView) return;
        if (!IsProfileDirty) { ClearRecoveryDraft(); return; }
        AtomicWrite(RecoveryDraftPath, JsonConvert.SerializeObject(new RecoverableDraft { profileId = ViewedProfileId, state = State, config = _draftConfig, mods = _draftMods, runtimeBaseline = _editingRuntimeBaseline, savedBaseline = _savedDraftProfile, restoreSavedBaseline = _restoreSavedBaseline }, Formatting.Indented));
    }
    private void RestoreRecoveryDraft()
    {
        if (_appliedView || !File.Exists(RecoveryDraftPath)) return;
        RecoverableDraft draft;
        try { draft = OverviewProfileStore.DeserializeJson<RecoverableDraft>(File.ReadAllText(RecoveryDraftPath)); }
        catch (Exception error) when (error is IOException || error is Newtonsoft.Json.JsonException)
        {
            ReadWarning = Loc.T("Recovery draft could not be read; the saved configuration is still available: ", "恢复草稿读取失败，仍可使用已保存配置：", "復原草稿讀取失敗，仍可使用已儲存設定：") + error.Message;
            return;
        }
        if (draft == null || draft.schemaVersion != 1 || draft.profileId != ViewedProfileId || draft.config?.values == null || draft.state == null || draft.mods == null)
        {
            ReadWarning = Loc.T("Invalid recovery draft; saved configuration was retained.", "恢复草稿格式无效，已保留已保存配置。", "復原草稿格式無效，已保留已儲存設定。");
            return;
        }
        var historicalRepair = HistoricalSkippedBaseline(draft, ViewedProfile);
        if (historicalRepair != null)
        {
            // Do not change committed files just by opening the page. Existing discard
            // is the explicit choice to recover verified skipped targets' prior values.
            draft.savedBaseline = historicalRepair;
            ReadWarning = Loc.T("A historical save included skipped targets in the preset. They remain an unsaved draft; Discard restores the verified prior preset values. Review before saving/exporting.", "检测到旧版把跳过目标写入正式预设。目标仍作为未保存草稿；选择放弃将恢复有证据的旧正式值。保存或导出前请核对。", "偵測到舊版將略過目標寫入正式預設。目標仍作為未儲存草稿；選擇放棄將還原有證據的舊正式值。儲存或匯出前請核對。");
        }
        else if (!draft.restoreSavedBaseline && draft.savedBaseline != null
            && PresetSignature(ViewedProfile.config, ViewedProfile.mods) != PresetSignature(draft.savedBaseline.config, draft.savedBaseline.mods)
            && PresetSignature(ViewedProfile.config, ViewedProfile.mods) == PresetSignature(draft.config, draft.mods))
            ReadWarning = Loc.T("Historical recovery is ambiguous; the committed preset was kept. Discard keeps that preset; use the retained recovery file/backup to restore older values explicitly if needed.", "历史恢复证据不明确，未改正式预设。放弃将保留当前正式预设；如需旧值，请先保留恢复文件／备份，再明确恢复。", "歷史復原證據不明確，未改正式預設。放棄將保留目前正式預設；如需舊值，請先保留復原檔／備份，再明確還原。");
        _restoreSavedBaseline = draft.restoreSavedBaseline;
        var latestShared = Clone(_savedDraftProfile.state);
        bool sharedConflict = false;
        State = draft.savedBaseline?.state != null
            ? SharedInformationRecovery.Merge(draft.savedBaseline.state, draft.state, latestShared, out sharedConflict)
            : draft.state;
        _draftConfig = draft.config; _draftMods = draft.mods;
        if (sharedConflict)
            ReadWarning = Loc.T("Shared modpack information changed since this draft. Your edits were retained; review before saving.", "整合包共用资料在草稿之后有更新，已保留你的编辑，请核对后保存。", "整合包共用資料在草稿之後有更新，已保留你的編輯，請核對後儲存。");
        if (draft.runtimeBaseline?.values != null) _editingRuntimeBaseline = draft.runtimeBaseline;
        if (draft.savedBaseline?.config?.values != null)
        {
            _savedDraftProfile = draft.savedBaseline;
            _savedDraftProfile.state = latestShared;
            _savedDraftSignature = DraftSignature(_savedDraftProfile.state, _savedDraftProfile.config, _savedDraftProfile.mods);
        }
    }
    private void RestoreSavedDraftBaseline()
    {
        if (!File.Exists(RecoveryDraftPath)) return;
        RecoverableDraft recovery;
        try { recovery = OverviewProfileStore.DeserializeJson<RecoverableDraft>(File.ReadAllText(RecoveryDraftPath)); }
        catch (Newtonsoft.Json.JsonException) { return; }
        if (recovery == null || recovery.schemaVersion != 1 || recovery.profileId != ViewedProfileId
            || recovery.savedBaseline?.config?.values == null || recovery.savedBaseline.mods == null || recovery.savedBaseline.state == null) return;
        var historical = HistoricalSkippedBaseline(recovery, ViewedProfile);
        if (historical != null)
        {
            Profiles.RestoreBaseline(ViewedProfileId, historical);
            return;
        }
        if (!recovery.restoreSavedBaseline)
        {
            // Preserve uncertain records for manual recovery even when the user discards
            // the working draft; never silently consume the only historical evidence.
            if (PresetSignature(ViewedProfile.config, ViewedProfile.mods) != PresetSignature(recovery.savedBaseline.config, recovery.savedBaseline.mods))
                AtomicWrite(RecoveryDraftPath + ".historical-backup", File.ReadAllText(RecoveryDraftPath));
            return;
        }
        // Only a failed/legacy partial persistence needs disk restoration. Ordinary
        // editing drafts never replaced the committed profile generation.
        if (PresetSignature(ViewedProfile.config, ViewedProfile.mods) == PresetSignature(recovery.config, recovery.mods)
            && PresetSignature(ViewedProfile.config, ViewedProfile.mods) != PresetSignature(recovery.savedBaseline.config, recovery.savedBaseline.mods))
            Profiles.RestoreBaseline(ViewedProfileId, recovery.savedBaseline);
    }

    private OverviewProfile? HistoricalSkippedBaseline(RecoverableDraft recovery, OverviewProfile committed)
    {
        if (recovery.restoreSavedBaseline || recovery.savedBaseline?.config?.values == null || recovery.savedBaseline.mods == null)
            return null;
        string pendingPath = Path.Combine(ProfileRuntimeDirectory, "pending-profile.json");
        string appliedPath = Path.Combine(ProfileRuntimeDirectory, "applied-profile.json");
        if (!File.Exists(pendingPath) || !File.Exists(appliedPath)) return null;
        // These independent records were written after the old buggy Save(). A plain
        // editing draft (or an unrelated/newer profile generation) is not enough proof.
        DateTime recoveryWritten = File.GetLastWriteTimeUtc(RecoveryDraftPath);
        if (committed.modifiedUtc.ToUniversalTime() > recoveryWritten
            || committed.directory.Length > 0 && File.GetLastWriteTimeUtc(Path.Combine(committed.directory, "profile.json")) > recoveryWritten)
            return null;
        Newtonsoft.Json.Linq.JObject pending;
        OverviewProfile applied;
        try
        {
            pending = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(pendingPath));
            applied = OverviewProfileStore.DeserializeJson<OverviewProfile>(File.ReadAllText(appliedPath));
        }
        catch (Exception error) when (error is IOException || error is JsonException || error is InvalidDataException) { return null; }
        if (pending.Value<int?>("schemaVersion") != 1 || !GroupTree.SupportedSchema(applied.schemaVersion) || pending.Value<string>("profileId") != ViewedProfileId || applied.id != ViewedProfileId
            || pending.Value<bool?>("pending") == true || applied.config?.values == null || applied.mods == null)
            return null;
        var repaired = Clone(committed); bool changed = false;
        foreach (var target in recovery.config.values.Where(v => v.missing && v.missingReason.Length > 0))
        {
            var old = recovery.savedBaseline.config.values.FirstOrDefault(v => v.Identity == target.Identity);
            var saved = repaired.config.values.FirstOrDefault(v => v.Identity == target.Identity);
            var accepted = applied.config.values.FirstOrDefault(v => v.Identity == target.Identity);
            if (old == null || saved == null || accepted == null || old.serializedValue == target.serializedValue
                || saved.type != target.type || saved.serializedValue != target.serializedValue
                || !saved.missing || saved.missingReason != target.missingReason
                || accepted.serializedValue != target.serializedValue || !accepted.missing) continue;
            // Restore only this proven polluted field; unrelated external fields survive.
            repaired.config.values.Remove(saved); repaired.config.values.Add(Clone(old)); changed = true;
        }
        var restartMods = pending["restartMods"] as Newtonsoft.Json.Linq.JArray;
        foreach (var target in recovery.mods)
        {
            var old = recovery.savedBaseline.mods.FirstOrDefault(m => m.guid.Equals(target.guid, StringComparison.OrdinalIgnoreCase));
            var saved = repaired.mods.FirstOrDefault(m => m.guid.Equals(target.guid, StringComparison.OrdinalIgnoreCase));
            var accepted = applied.mods.FirstOrDefault(m => m.guid.Equals(target.guid, StringComparison.OrdinalIgnoreCase));
            if (old == null || saved == null || accepted == null || old.enabled == target.enabled || saved.enabled != target.enabled
                || accepted.enabled != old.enabled || restartMods == null
                || restartMods.Any(m => string.Equals((string?)m["guid"], target.guid, StringComparison.OrdinalIgnoreCase))) continue;
            saved.enabled = old.enabled; changed = true;
        }
        return changed ? repaired : null;
    }

    private void ClearRecoveryDraft()
    {
        if (File.Exists(RecoveryDraftPath)) File.Delete(RecoveryDraftPath);
    }
}
