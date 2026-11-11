#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigEnhance.Overview;

internal static partial class OverviewUi
{
    internal static void ShowSaveIssues(string report, IReadOnlyList<OverviewMod> mods, Action inspect, Action save, Action? retrySources = null, Action? manualSources = null)
    {
        var panel = CreateModalPanel("MCE_SaveIssues", 1120, 780); if (panel == null) return;
        var owner = _modal;
        SaveDialogHeader(panel, Loc.T("Review before saving", "保存前检查结果", "儲存前檢查結果"),
            Loc.T("Warnings do not change your choices. Review once, then save the available changes.", "一般提醒不会改变你的选择；统一核对一次，再保存有效修改。", "一般提醒不會改變你的選擇；統一核對一次，再儲存有效變更。"), compactSummary: true);
        SaveDialogBody(panel, report, attention: true, formatted: true, horizontalReport: true);
        // Navigation is explicit and never invokes the save/discard guard.
        if (retrySources != null && ConfigurationIssueReport.Unidentified(mods).Count > 0)
        {
            var retry = Button(panel, Loc.T("Retry identification", "一键重新识别", "一鍵重新識別"), () => { CloseModal(); retrySources(); }, 148, 28, UiActionRole.Edit);
            Place((RectTransform)retry.transform, 24, 77, 148, 28);
            if (manualSources != null) {
                var manual = Button(panel, Loc.T("Identify manually", "手动识别…", "手動識別…"), () => { CloseModal(); manualSources(); }, 130, 28, UiActionRole.Edit);
                Place((RectTransform)manual.transform, 182, 77, 130, 28);
            }
        }
        SaveDialogAction(panel, Loc.T("Inspect mod…", "查看相关模组…", "查看相關模組…"), () => {
            CloseModal(); inspect();
        }, 348, 148, UiActionRole.Edit);
        SaveDialogAction(panel, Loc.T("Ignore above and save", "忽略以上并保存", "忽略以上並儲存"), () => { save(); if (_modal == owner) CloseModal(); }, 20, 180, UiActionRole.Commit);
        SaveDialogAction(panel, Loc.T("Back", "返回编辑", "返回編輯"), CloseModal, 210, 128, UiActionRole.Neutral);
    }
}
