#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ModConfigEnhance.Overview;

/// <summary>One original-preset choice for all local and native save buttons.</summary>
internal static class ConfigurationSaveRequest
{
    internal static void Run(OverviewRepository repo, IReadOnlyList<OverviewMod> mods, ConfigurationSaveScope scope, string guid, Action<OverviewToggleResult> completed)
    {
        void Commit()
        {
            var result = repo.SaveConfiguration(mods, scope, guid);
            if (result.Message.StartsWith("MCE_EXTERNAL_CONFLICT:", StringComparison.Ordinal))
                OverviewUi.ConfirmText(Loc.T("Configuration changed externally", "配置被外部修改", "設定被外部修改"), result.Message,
                    () => completed(repo.SaveConfiguration(mods, scope, guid, overwriteExternal: true)), confirmLabel: Loc.T("Keep mine and save", "保留我的并保存", "保留我的並儲存"));
            else completed(result);
        }
        if (!repo.Profiles.NeedsOriginalPresetSaveChoice(repo.ViewedProfileId)) { Commit(); return; }
        OverviewUi.ShowPresetSaveChoice(repo.ViewedProfile.name, () => {
            repo.Profiles.ConfirmOriginalPresetSave(repo.ViewedProfileId); Commit();
        }, () => OverviewUi.Ask(Loc.T("New configuration", "新配置名称", "新設定名稱"), new[] { (Loc.T("Name", "名称", "名稱"), repo.ViewedProfile.name) }, values => {
            repo.SaveDraftAsNewProfile(values[0]); Commit();
        }));
    }
}
