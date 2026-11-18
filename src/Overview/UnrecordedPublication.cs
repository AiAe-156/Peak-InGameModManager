#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 0.0.2（2026-11-18）：导出前找出「已安装、可发布，但不在任何勾选方案里」的模组。
/// 方案只记录草稿里已有的模组（切换、点「+」、收录当前配置，或默认方案初建时在场的）；之后新装、更新队列装上、手工放进来的模组
/// 不会自动进方案。发布清单只按方案里的 GUID 选模组，所以这些模组原来会被悄悄漏掉。
/// 纯逻辑，不依赖 Unity / BepInEx，publish-sim 夹具直接链接本文件。
/// </summary>
internal static class UnrecordedPublication
{
    internal sealed class Candidate
    {
        public string Guid = "", Name = "", Version = "";
        /// <summary>相对 BepInEx 根目录的包路径（如 <c>plugins/Team-Mod</c>）。</summary>
        public string BepInExRelative = "";
        /// <summary>自己的包目录，加上外层（嵌套）模组的包目录：任何一个被打包，它的文件就跟着进包。</summary>
        public List<string> Roots = new();
        /// <summary>磁盘现状（manifest 不是 .old、DLL 不是 .old）。</summary>
        public bool Enabled;
    }

    /// <summary>可以记进方案的模组：有 GUID、在 BepInEx 会加载的目录里；IGMM 自己、BepInEx 本体（core / BepInEx.* GUID）、
    /// 只有 manifest 的包（<c>package:</c>，方案不记录它们）不算。停放文件夹已由发布清单本身排除。</summary>
    internal static bool Eligible(Candidate mod, string managerGuid)
    {
        if (mod.Guid.Length == 0 || mod.Guid.StartsWith("package:", StringComparison.Ordinal)) return false;
        if (mod.Guid.Equals(managerGuid, StringComparison.OrdinalIgnoreCase) || mod.Guid.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)) return false;
        string first = mod.BepInExRelative.Replace('\\', '/').TrimStart('/').Split('/')[0];
        return LoadedFolders.Contains(first, StringComparer.OrdinalIgnoreCase);
    }
    private static readonly string[] LoadedFolders = { "plugins", "patchers", "monomod" };

    /// <param name="inventory">发布清单（已排除停放文件夹）。</param>
    /// <param name="packedRoots">本次会打进包的包目录（按方案 GUID、附带内容、依赖闭包选出的）。</param>
    /// <param name="recordedGuids">所有勾选方案里记录过的 GUID（含 installed=false 的旧记录）。</param>
    internal static List<Candidate> Find(IEnumerable<Candidate> inventory, IEnumerable<string> packedRoots, IEnumerable<string> recordedGuids, string managerGuid)
    {
        var packed = new HashSet<string>(packedRoots.Select(Normalize), StringComparer.OrdinalIgnoreCase);
        var recorded = new HashSet<string>(recordedGuids.Where(g => g.Length > 0), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Candidate>();
        foreach (var mod in inventory)
        {
            if (!Eligible(mod, managerGuid) || recorded.Contains(mod.Guid) || mod.Roots.Any(r => packed.Contains(Normalize(r)))) continue;
            if (seen.Add(mod.Guid)) result.Add(mod);
        }
        return result.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(m => m.Guid, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 存入方案时的启停：本次发布的默认方案照磁盘现状；其他勾选的方案一律记为停用。
    /// 理由：其他方案是作者在没有这个模组时定下、测过的组合；记成停用，接收方切到那些方案时与作者原来的组合一致，
    /// 不会凭空多出一个没在那套方案里测过的模组（之前它根本不在包里，效果等同停用）。
    /// </summary>
    internal static bool EnabledFor(bool diskEnabled, string profileId, string defaultProfileId)
        => diskEnabled && profileId.Equals(defaultProfileId, StringComparison.OrdinalIgnoreCase);

    /// <summary>把方案里还没有的条目追加进去（按 GUID，不分大小写），已有的一概不动。返回追加数。</summary>
    internal static int Merge<T>(List<T> mods, IEnumerable<T> additions, Func<T, string> guid)
    {
        var present = new HashSet<string>(mods.Select(guid).Where(g => g.Length > 0), StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (T item in additions)
            if (guid(item).Length > 0 && present.Add(guid(item))) { mods.Add(item); added++; }
        return added;
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');
}
