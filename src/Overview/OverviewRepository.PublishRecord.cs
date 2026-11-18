#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;

internal sealed class PublicationRecordResult
{
    public string BackupDirectory = "";
    public int Added;
    /// <summary>当前查看的方案若是只读作者方案，保存时会转成可编辑副本，id 可能变；向导据此改写勾选的方案和默认方案。</summary>
    public string ViewedBefore = "", ViewedAfter = "";
}

internal sealed partial class OverviewRepository
{
    /// <summary>
    /// 0.0.2：把导出前发现的未记录模组存进勾选的方案。先备份这些方案；当前查看的方案走草稿 + <see cref="Save"/>（与「收录当前配置」同一条路，
    /// 否则下次保存草稿会把直接写进文件的条目标成未安装），其余方案只追加条目。启停见 <see cref="UnrecordedPublication.EnabledFor"/>。
    /// </summary>
    internal PublicationRecordResult RecordForPublication(IReadOnlyList<OverviewMod> mods, IReadOnlyList<string> profileIds, string defaultProfileId)
    {
        var result = new PublicationRecordResult { BackupDirectory = Profiles.BackupProfiles(profileIds, "before-publish-record") };
        string viewed = ViewedProfileId;
        result.ViewedBefore = result.ViewedAfter = viewed;
        foreach (string id in profileIds.Where(id => !id.Equals(viewed, StringComparison.OrdinalIgnoreCase)))
            result.Added += Profiles.AppendMods(id, mods.Select(m => PublicationEntry(m, UnrecordedPublication.EnabledFor(m.TargetEnabled, id, defaultProfileId))).ToList());
        if (profileIds.Contains(viewed, StringComparer.OrdinalIgnoreCase))
        {
            EnsureEditableProfile();
            result.Added += UnrecordedPublication.Merge(_draftMods, mods.Select(m => PublicationEntry(m, UnrecordedPublication.EnabledFor(m.TargetEnabled, viewed, defaultProfileId))), p => p.guid);
            Save();
            result.ViewedAfter = ViewedProfileId;
        }
        return result;
    }

    private static OverviewProfileMod PublicationEntry(OverviewMod m, bool enabled) => new()
    {
        id = m.Id, guid = m.Guid, name = m.Name, version = m.DisplayVersion, pluginVersion = m.PluginVersion, packageVersion = m.PackageVersion,
        packageNamespace = m.PackageNamespace, packageName = m.PackageName, enabled = enabled, installed = true
    };
}
