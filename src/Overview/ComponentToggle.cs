#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>2.3.4: enable/disable a core or patcher component by renaming its assemblies (x.dll ↔ x.dll.d), the same
/// convention as mods. BepInEx loads these at startup, so the change takes effect on the next launch and the files can be
/// renamed while the game runs (Windows allows renaming a loaded file). Uses the installation write lease and its
/// journal, so an interrupted rename is recovered on the next start. Components MCE itself runs on are refused.</summary>
internal static class ComponentToggle
{
    internal static bool RequiredByMce(InfrastructureOverviewModel package)
        => SourceUi.DependencyDiagnostic(package.Key).State == DiagnosticSummary.Status.RequiredByMce;
    /// <summary>Why the package cannot be switched to <paramref name="enabled"/>; empty when it can. Turning an
    /// MCE-required component back on is always allowed.</summary>
    internal static string BlockReason(InfrastructureOverviewModel package, bool enabled = false)
    {
        if (!package.IdentityConfirmed) return Loc.T("Component identity is unconfirmed.", "组件身份未确认。", "組件身分未確認。");
        if (package.RuntimeFiles.Count == 0) return Loc.T("This component has no assembly to rename.", "此组件没有可改名的程序集。", "此組件沒有可改名的程式集。");
        if (!enabled && RequiredByMce(package))
            return Loc.T("MCE runs on this component; it cannot be disabled or uninstalled.", "MCE 运行依赖此组件，不能停用或卸载。", "MCE 執行依賴此組件，不能停用或解除安裝。");
        if (enabled && package.RuntimeFiles.Any(PluginFileNames.IsForeignDisabled))
            return Loc.T("Disabled by another tool. Restore it using that tool.", "由其他工具停用，请使用原工具恢复。", "由其他工具停用，請使用原工具恢復。");
        return "";
    }

    /// <summary>Renames every runtime file of the package to the requested state. Returns the number of files renamed.</summary>
    internal static int Set(InfrastructureOverviewModel package, bool enabled)
    {
        string reason = BlockReason(package, enabled);
        if (reason.Length > 0) throw new InvalidOperationException(reason);
        string bepinex = Path.GetFullPath(BepInEx.Paths.BepInExRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var renames = new List<InstallationWriteLease.Rename>();
        foreach (string file in package.RuntimeFiles)
        {
            string full = Path.GetFullPath(file);
            if (!full.StartsWith(bepinex, StringComparison.OrdinalIgnoreCase)) throw new IOException("Component file is outside BepInEx: " + file);
            if (!File.Exists(full)) throw new IOException(Loc.F("File does not exist: {0}", "文件不存在：{0}", "檔案不存在：{0}", full));
            bool live = full.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            if (live == enabled) continue;
            string target = enabled ? PluginFileNames.OriginalPath(full) : full + PluginFileNames.DisabledSuffix;
            if (File.Exists(target)) throw new IOException(Loc.F("Destination already exists and cannot be overwritten: {0}", "目标文件已存在，不能覆盖：{0}", "目標檔案已存在，不能覆蓋：{0}", target));
            renames.Add(new InstallationWriteLease.Rename { From = full, To = target });
        }
        if (renames.Count == 0) return 0;
        using var lease = InstallationWriteLease.Acquire(BepInEx.Paths.BepInExRootPath);
        lease.Prepare(renames, Array.Empty<InstallationWriteLease.Rewrite>());
        try { lease.Apply(); lease.Commit(); }
        catch { try { lease.Recover(); } catch (Exception) { } throw; }
        Diagnostics.ManagerLog.Info((enabled ? "启用组件 " : "停用组件 ") + package.Key + "：改名 " + renames.Count + " 个文件，下次启动生效");
        return renames.Count;
    }
}
