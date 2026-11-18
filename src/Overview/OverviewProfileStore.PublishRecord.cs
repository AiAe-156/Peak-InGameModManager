#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace ModConfigEnhance.Overview;

/// <summary>0.0.2：导出向导「存入方案并导出」用。现有保存流程（SaveFiles）不留备份，这里先整份备份再追加。</summary>
internal sealed partial class OverviewProfileStore
{
    /// <summary>把这些方案的 profile.json / config-values.json 和方案索引复制到运行目录下的 profile-backups（不在 config 里，不会被发布扫到）。返回备份目录。</summary>
    internal string BackupProfiles(IEnumerable<string> ids, string reason)
    {
        lock (_gate)
        {
            string target = ModConfigEnhance.Updating.ManagerIdentity.Runtime(Paths.BepInExRootPath, "profile-backups", DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + reason);
            Directory.CreateDirectory(target);
            if (File.Exists(IndexPath)) File.Copy(IndexPath, Path.Combine(target, "index.json"), false);
            foreach (string id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                OverviewProfile profile = GetRequired(id);
                if (!Directory.Exists(profile.directory)) continue;
                string destination = Path.Combine(target, SafeSegment(profile.id));
                Directory.CreateDirectory(destination);
                foreach (string name in new[] { "profile.json", "config-values.json" })
                {
                    string source = Path.Combine(profile.directory, name);
                    if (File.Exists(source)) File.Copy(source, Path.Combine(destination, name), false);
                }
            }
            return target;
        }
    }

    /// <summary>只追加方案里还没有的模组条目；已有条目、配置、分组都不动，也不改当前查看的方案。作者方案写进它的本地工作副本。</summary>
    internal int AppendMods(string id, IReadOnlyList<OverviewProfileMod> additions)
    {
        lock (_gate)
        {
            OverviewProfile profile = GetRequired(id);
            if (!profile.IsValid) throw new InvalidDataException(profile.loadError);
            if (profile.readOnly || profile.kind == OverviewProfileKind.Author) profile = AuthorWorkingProfile(profile);
            profile = Clone(profile);
            int added = UnrecordedPublication.Merge(profile.mods, additions.Select(a => Clone(a)), m => m.guid);
            if (added == 0) return 0;
            profile.modifiedUtc = DateTime.UtcNow;
            SaveFiles(profile);
            UpsertReference(profile);
            SaveIndex();
            return added;
        }
    }
}
