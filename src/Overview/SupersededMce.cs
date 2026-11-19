#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 0.0.2（2026-11-19，用户决定）：本管理器在场时，简化版 ModConfig Enhance（旧 GUID <see cref="ManagerIdentity.LegacyGuid"/>）一律停用——
/// 本管理器已包含它的全部功能，两者同开会把模组设置页改两遍。任何启停路径都不能把它重新启用：
///   · 草稿 / 方案读写：<see cref="OverviewEnablementDraft"/> 把它算成停用，<see cref="ForceOff"/> 在方案读入、保存、应用前把记录改成停用；
///   · 真正改名的地方：<see cref="ModToggleService"/> 把「启用它」的目标改成停用（兜底，防止漏网的调用方）；
///   · 发布：投影成停用（<c>PackPublisher.ProjectPluginRelative</c>）；
///   · 启动：<c>LegacyMceGuard</c> 发现它这次被加载了，就按管理器的停用方式改名，下次启动生效。
/// 纯逻辑，夹具 验证/统一保存重构/IssueReportChecks 链接它。
/// </summary>
internal static class SupersededMce
{
    internal static bool Is(string? guid) => ManagerIdentity.IsSupersededMce(guid);
    internal static bool Is(OverviewMod mod) => Is(mod.Guid) || mod.PluginGuids.Any(Is);

    /// <summary>把方案记录里的 MCE 改成停用（不删条目：删了以后磁盘上启用的 MCE 会被当成「未记录、保持现状」）。返回改了几条。</summary>
    internal static int ForceOff(IEnumerable<OverviewProfileMod>? mods)
    {
        int changed = 0;
        if (mods == null) return 0;
        foreach (var mod in mods)
        {
            if (mod == null || !Is(mod.guid) || !mod.enabled && string.IsNullOrEmpty(mod.copy)) continue;
            mod.enabled = false; mod.copy = ""; changed++;
        }
        return changed;
    }

    /// <summary>启停目标里「启用 MCE」一律换成停用。<paramref name="coerced"/> = 换了几个。</summary>
    internal static (OverviewMod Mod, bool Enabled)[] Coerce(IEnumerable<(OverviewMod Mod, bool Enabled)> targets, out int coerced)
    {
        int count = 0;
        var result = targets.Select(t =>
        {
            if (!t.Enabled || t.Mod == null || !Is(t.Mod)) return t;
            count++; return (t.Mod, false);
        }).ToArray();
        coerced = count;
        return result;
    }

    /// <summary>开机检查要改名的文件：.dll → .dll.d；所在资料包（plugins 下最近一层带 manifest.json 的目录）没有别的 .dll 时，
    /// manifest.json → manifest.json.old（与 <see cref="ModToggleService"/> 非管理器档案模式一致）。目标已存在就报错、不改。</summary>
    internal static List<(string From, string To)> DisablePlan(string dll, string pluginsRoot)
    {
        string full = Path.GetFullPath(dll);
        string root = Path.GetFullPath(pluginsRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(Loc.F("The plugin file is outside the plugins folder: {0}", "插件文件不在 plugins 目录内：{0}", "外掛檔案不在 plugins 目錄內：{0}", full));
        var moves = new List<(string From, string To)> { (full, full + PluginFileNames.DisabledSuffix) };
        for (string? dir = Path.GetDirectoryName(full); dir != null && dir.Length > root.Length; dir = Path.GetDirectoryName(dir))
        {
            string manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest)) continue;
            bool others = Directory.EnumerateFiles(dir, "*.dll", SearchOption.AllDirectories)
                .Any(f => !Path.GetFullPath(f).Equals(full, StringComparison.OrdinalIgnoreCase));
            if (!others) moves.Add((manifest, manifest + ".old"));
            break;
        }
        foreach (var move in moves)
            if (File.Exists(move.To) || Directory.Exists(move.To))
                throw new IOException(Loc.F("Destination already exists and cannot be overwritten: {0}", "目标文件已存在，不能覆盖：{0}", "目標檔案已存在，不能覆蓋：{0}", move.To));
        return moves;
    }
}
