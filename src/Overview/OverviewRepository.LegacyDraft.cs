#nullable enable
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace ModConfigEnhance.Overview;

internal sealed partial class OverviewRepository
{
    private void MigrateLegacyUnappliedDraft()
    {
        if (_appliedView) return;
        string marker = Path.Combine(ProfileRuntimeDirectory, "drafts", OverviewProfileExport.PortableId(ViewedProfileId) + ".unified-save.json");
        if (File.Exists(marker)) return;
        // Old Save-only generations are kept on disk. Their differing values become an
        // editable unsaved draft; migration itself never writes live config/plugin files.
        if (ViewedProfileId == Profiles.AppliedId && _appliedSnapshot != null)
        {
            var appliedValues = _appliedSnapshot.config.values.GroupBy(v => v.Identity).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
            bool different = _draftConfig.values.Any(v => !appliedValues.TryGetValue(v.Identity, out var old) || old.serializedValue != v.serializedValue)
                || _draftMods.Any(m => !_appliedSnapshot.mods.Any(old => old.guid.Equals(m.guid, StringComparison.OrdinalIgnoreCase) && old.enabled == m.enabled));
            if (different)
            {
                _restoreSavedBaseline = true;
                _savedDraftProfile.config = Clone(_appliedSnapshot.config);
                _savedDraftProfile.mods = Clone(_appliedSnapshot.mods);
                _savedDraftSignature = DraftSignature(_savedDraftProfile.state, _savedDraftProfile.config, _savedDraftProfile.mods);
                PersistRecoveryDraft();
                ReadWarning = Loc.T("Earlier saved-only changes were restored as an unsaved draft. Save configuration to use them.", "旧版仅保存的修改已恢复为未保存草稿，点击保存配置后使用。", "舊版僅儲存的變更已還原為未儲存草稿，點擊儲存設定後使用。");
            }
        }
        if (ViewedProfileId == Profiles.AppliedId && _pendingConfiguration.pending)
        {
            foreach (var value in _pendingConfiguration.config.values)
            {
                _draftConfig.values.RemoveAll(v => v.Identity == value.Identity);
                _draftConfig.values.Add(Clone(value));
                _savedDraftProfile.config.values.RemoveAll(v => v.Identity == value.Identity);
            }
            _savedDraftSignature = DraftSignature(_savedDraftProfile.state, _savedDraftProfile.config, _savedDraftProfile.mods);
            _pendingConfiguration.pending = false; PersistRecoveryDraft();
            AtomicWrite(Path.Combine(ProfileRuntimeDirectory, "pending-profile.json"), JsonConvert.SerializeObject(_pendingConfiguration, Formatting.Indented));
        }
        AtomicWrite(marker, JsonConvert.SerializeObject(new { schemaVersion = 1, profileId = ViewedProfileId }));
    }
}
