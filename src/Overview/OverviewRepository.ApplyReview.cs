#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace ModConfigEnhance.Overview;
internal sealed partial class OverviewRepository
{
    internal ProfileChangeReview ReviewApplyFailures(IReadOnlyList<OverviewMod> mods, string failure)
    {
        var review = new ProfileChangeReview { ProfileName = ViewedProfile.name, AppliedName = AppliedProfileName };
        void Add(string name, string reason, string fix) => review.Items.Add(new ProfileReviewItem {
            Category = ProfileReviewCategory.Unavailable, GroupName = Loc.T("Application issues", "应用问题", "套用問題"),
            ModName = name, ModId = name, Title = Loc.T("Reason", "原因", "原因"), Before = reason,
            Target = Loc.T("Retain current state", "保留当前状态", "保留目前狀態"), Note = Loc.T("Fix: ", "处理办法：", "處理方法：") + fix });
        string Fix(string reason)
        {
            if (reason.IndexOf("依赖", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("depend", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("requires", StringComparison.OrdinalIgnoreCase) >= 0)
                return Loc.T("Keep the required provider enabled, enable it with this mod, or install the requested version. Skip enablement to leave this installation as it is.", "保留所需依赖启用，或与此模组一起启用；版本不足时更新到提示版本。也可跳过启停，维持现有安装。", "保留所需依賴啟用，或與此模組一起啟用；版本不足時更新至提示版本。也可略過啟停，維持現有安裝。");
            if (reason.IndexOf("副本", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("copies", StringComparison.OrdinalIgnoreCase) >= 0)
                return Loc.T("Compare the listed paths and retain one intended active/disabled candidate, then refresh. Keep backups separately; the manager will not delete them.", "核对列出的路径，保留一个预期启用/停用候选后刷新；备份另行保留，管理器不会删除。", "核對列出的路徑，保留一個預期啟用/停用候選後重新整理；備份另行保留，管理器不會刪除。");
            return Loc.T("Resolve the reported file, permission or manager-profile issue and refresh before retrying; skip enablement to apply other settings first.", "按原因处理文件、权限或管理器档案后刷新重试；可跳过启停先应用其他设置。", "依原因處理檔案、權限或管理器檔案後重新整理重試；可略過啟停先套用其他設定。");
        }
        Add(Loc.T("This application", "本次应用", "本次套用"), failure, Fix(failure));
        // Isolated probes are diagnostic only: the final combined transaction remains authoritative.
        var service = new ModToggleService(this, OverviewOptions.ManagerProfileDirectory.Value, knownMods: mods);
        foreach (var mod in mods.Where(m => PlannedEnabled(m) != m.TargetEnabled))
        {
            var probe = service.PrepareTargets(new[] { (mod, PlannedEnabled(mod)) }, out _);
            if (probe.Success) continue;
            Add(mod.DisplayName, probe.Message, Fix(probe.Message) + " " + Loc.T("Single-item check; batch companion changes are not included.", "此为单项检查，未计入本批配套改动。", "此為單項檢查，未計入本批配套變更。"));
        }
        return review;
    }
}
