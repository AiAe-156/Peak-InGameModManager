#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Bootstrap;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 0.0.2（2026-11-19，用户决定）：开机检查。本管理器已加载、而简化版 ModConfig Enhance 这次也被加载了（它的 .dll 是启用的），
/// 就按管理器自己的停用方式把它停掉：.dll 改名成 .dll.d；它的资料包里没有别的 .dll 时，manifest.json 也改成 manifest.json.old
/// （与 <see cref="ModToggleService"/> 非管理器档案模式一致），走安装写入租约与日志，商店账本登记过的文件跟着改路径。下次启动生效，写进日志。
/// 配置了 r2modman 等管理器档案时不自动改：那边下次同步会把它改回来，只提醒玩家在管理器里停用。
/// 结果给 <see cref="OverviewPage"/> 的提醒用（<see cref="Result"/>）。
/// </summary>
internal static class LegacyMceGuard
{
    internal enum Outcome { None, Disabled, Failed }
    internal static Outcome Result { get; private set; }
    internal static string Detail { get; private set; } = "";

    internal static void Run()
    {
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue(ManagerIdentity.LegacyGuid, out var info) || info == null) return;
            string dll = info.Location ?? "";
            if (dll.Length == 0 || !dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(dll))
            { Fail(Loc.T("Its plugin file could not be located.", "找不到它的插件文件。", "找不到它的外掛檔案。")); return; }
            if (!string.IsNullOrWhiteSpace(OverviewOptions.ManagerProfileDirectory?.Value))
            { Fail(Loc.T("A mod manager profile (such as r2modman) is configured; disable it there, otherwise the next sync turns it back on.", "配置了管理器档案（如 r2modman），请在那边停用它，否则下次同步会把它改回启用。", "設定了管理器檔案（如 r2modman），請在那邊停用它，否則下次同步會把它改回啟用。")); return; }
            var moves = Plan(dll, BepInEx.Paths.PluginPath);
            using var lease = InstallationWriteLease.Acquire(BepInEx.Paths.BepInExRootPath);
            var ledger = ModToggleService.ShopLedgerRewrite(BepInEx.Paths.BepInExRootPath, moves);
            lease.Prepare(moves.Select(m => new InstallationWriteLease.Rename { From = m.From, To = m.To }),
                ledger == null ? Array.Empty<InstallationWriteLease.Rewrite>() : new[] { ledger });
            try { lease.Apply(); lease.Commit(); }
            catch { try { lease.Recover(); } catch (Exception) { } throw; }
            Result = Outcome.Disabled; Detail = dll;
            Diagnostics.ManagerLog.Info("开机检查：ModConfig Enhance 本次被加载，本管理器已包含其全部功能，已将其停用（改名 " + moves.Count + " 个文件，下次启动生效）：" + dll);
        }
        catch (Exception error) { Fail(error.Message); }
    }

    private static void Fail(string reason)
    {
        Result = Outcome.Failed; Detail = reason;
        Diagnostics.ManagerLog.Warn("开机检查：ModConfig Enhance 本次被加载，但未能自动停用：" + reason);
    }

    private static List<(string From, string To)> Plan(string dll, string pluginsRoot) => SupersededMce.DisablePlan(dll, pluginsRoot);
}
