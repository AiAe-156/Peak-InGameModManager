#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ModConfigEnhance;

/// <summary>
/// ModConfig Enhance — in-game mod overview, configuration profiles, direct updates,
/// modpack authoring and publishing, localization, and ModConfig settings enhancements.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(ModConfigSupport.Guid, BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency(TimeThemeBridge.Guid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(XUnityBridge.Guid, BepInDependency.DependencyFlags.SoftDependency)] // 2.5.0：只为加载顺序（启动时读它的 ini 提醒机翻），不再联动
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = Updating.ManagerIdentity.Guid;
    public const string PluginName = "In-Game Mod Manager";
    public const string PluginVersion = "0.0.1";

    internal static ManualLogSource Log { get; private set; } = null!;

    // cfg 说明原文（Loc.ConfigTerms 用同一份字符串做译文的键）
    internal const string DescTreeSidebar = "A vertical mod list on the left; the selected mod's sections expand beneath it, options on the right. Restart to apply.";
    internal const string DescDescriptionPanel = "A panel at the top right shows the hovered option's description, default value and allowed range or choices. Requires the tree sidebar. Restart to apply.";
    internal const string DescUiLocalization = "Localize ModConfig's UI and this mod's buttons and notices. English is the source text; Simplified and Traditional Chinese are built in. Optional language files in config\\InGameModManager\\language\\ override the built-in text. Pick a language above the mod list. Restart to apply this switch.";
    internal const string DescHideLocalizationButtons = "Above the mod list, hide the export and translation-tool button rows; only the language dropdown + Refresh stay, the list moves up and the Export column hides (ticks remembered). Applies at once.";
    internal const string DescWarnXUnity = "When XUnity AutoTranslator is installed with online machine translation on (Endpoint in AutoTranslatorConfig.ini is not empty), remind once per launch the first time the overview opens, with a one-click option to turn its machine translation off. Translation is done by this mod's own language files; XUnity is no longer linked.";

    internal static ConfigEntry<bool> EnableTreeLayout = null!;
    internal static ConfigEntry<bool> EnableDescriptionPanel = null!;
    internal static ConfigEntry<ModConfigLook.ThemeMode> ColourTheme = null!;
    internal const string DescColourTheme = "Colour theme of every manager screen. Blue follows the game's settings menu; Purple is the night style; PurpleAtNight switches to purple at night when TimeTheme is installed (only its day/night state is read). The mod settings page switches at once; an open overview switches the next time it is opened.";
    internal static ConfigEntry<bool> EnableLocalization = null!;
    internal static ConfigEntry<bool> HideLocalizationButtons = null!;
    internal static ConfigEntry<bool> WarnXUnity = null!;
    internal static ConfigEntry<bool> AllowGuideEditing = null!;
    /// <summary>Technical (debug) log layer; also passed to the worker as --debug. Off by default.</summary>
    internal static ConfigEntry<bool> DebugLogging = null!;
    internal const string DescAllowGuideEditing = "Allow editing all user guide chapters and the changelog with the shared document toolbar. Changes are saved locally per language; disabling editing keeps showing saved overrides. Off by default.";
    internal const string DescDebugLogging = "Write the technical log layer as well: every worker command, queue poll, plan file and file-list detail, in this mod's own log source and to the update worker (which then also writes debug lines to its own file). The normal layer (update reports, mod enablement, config saves) is always on. Off by default; turn it on when diagnosing an update problem.";

    /// <summary>界面维护的记忆（Hidden）：选中的语言文件（空 = 跟随游戏）、置顶、不导出、仅导出已选。</summary>
    internal static ConfigEntry<string> LanguageFile = null!;
    internal static ConfigEntry<string> PinnedMods = null!;
    internal static ConfigEntry<string> ExportExcludedMods = null!;
    internal static ConfigEntry<bool> ExportSelectedOnly = null!;
    internal static ConfigEntry<SidebarSortMode> SidebarSort = null!;
    internal static ConfigEntry<float> IntroVideoVolume = null!;
    internal static ConfigEntry<bool> IntroVideoMuted = null!;

    /// <summary>本模组在 ModConfig 列表里的行名 = 它的 FixNaming(插件名)（1.8.1 ModConfigPlugin.cs）。强制置顶与行名上色按它认行，改名时不用同步。</summary>
    internal static readonly string SelfRowName = FixNaming(PluginName);

    private void Awake()
    {
        Log = Logger;
        // 1.0.0 拆分：第一次启动先把旧 cfg 复制成新 cfg（必须在任何 Config.Bind 之前，绑定时读到的就是旧设置）
        var split = new ManagerSplitMigration.Report();
        bool configCopied = ManagerSplitMigration.CopyConfig(BepInEx.Paths.ConfigPath, split);
        if (configCopied) Config.Reload();
        Diagnostics.RuntimeLogCapture.Start();
        Overview.PublishedPack.AuthorRootsProvider = Overview.PackLibrary.AuthorRoots;
        Overview.PublishedPack.RuntimeRootProvider = () => Overview.PackLibrary.RuntimeRoot;
        Overview.PublishedPack.ManagedProvider = () => Overview.PackLibrary.Managed;
        BindConfig();
        Diagnostics.ManagerLog.Initialize(Log, DebugLogging);
        WaitForPendingUpdateWrite();
        RunSplitMigration(split);
        // 2.5.4：等更新写完再整理 config\ModConfigEnhance；助手还在跑就推迟到下次启动（没搬的目录继续用旧位置，见 MceDirectories）。
        // 放在这里仍早于一切读写这些目录的代码（上面只绑定了 MCE 自己的 cfg，它在 config 根目录）。
        MceDirectoryMigration.Run(WorkerRunning);
        
        
        Overview.OverviewOptions.Bind(Config);
        Overview.OverviewDependencySettings.Bind(Config);
        Overview.OverviewPageCache.Initialize();
        Overview.PublishedPack.InitializeDefaults(Config);
        Overview.ManagerProfileSync.Apply(Overview.OverviewOptions.ManagerProfileDirectory.Value);
        try { using var recovery = Updating.InstallationWriteLease.Acquire(BepInEx.Paths.BepInExRootPath); }
        catch (System.Exception error) { Log.LogWarning("Enablement recovery/write ownership unavailable: " + error.Message); }
        Overview.PackLibrary.ResumeWorker();
        gameObject.AddComponent<Overview.PackBootstrap>();
        gameObject.AddComponent<Overview.AutomaticUpdateChecks>();
        Loc.EnsureBuiltInFiles();
        NormalizeSelfPin();

        Harmony harmony = new(PluginGuid);
        SettingsTabActivationPatch.Install(harmony);
        Overview.NativeConfigurationDraft.Install(harmony);
        Overview.OverviewRepository.RecoveryClock = () => UnityEngine.Time.unscaledTimeAsDouble; // 1.1.4: deferred recovery-draft writes
        Overview.ConfigurationRoundBoundary.Install(harmony);
        Overview.ConfigurationNotificationGate.Install(harmony);
        Overview.AnimatorRestGate.Install(harmony, Config);
        Overview.OverviewController.Register(harmony);
        GamepadCombos.Install(harmony); // 1.1.2: LT + controller button presses a keyboard-only mod hotkey
        Overview.ControlsPageJump.Install(harmony); // 1.1.4: controls page "Mod settings" opens the keybindings tab
        // 复查：小数显示是数据正确性（PEAK 3.1 下手打 1.25 会存错），与树形侧栏无关，独立挂载
        ModFloatDisplayPatch.Apply(harmony);
        Log.LogInfo("[小数显示] " + ModFloatDisplayPatch.State);
        if (EnableTreeLayout.Value)
        {
            ModConfigTreeLayoutPatch.TryApply(harmony);
        }

        if (EnableLocalization.Value)
        {
            ModConfigLocalizationPatch.TryApply(harmony);
        }

        Log.LogInfo($"[{PluginName} v{PluginVersion}] 已加载。");
        Diagnostics.ManagerLog.Info($"已加载 {PluginName} v{PluginVersion} · 技术日志(debug)：{(DebugLogging.Value ? "开" : "关")}");
        Log.LogInfo($"  树形侧栏: {(EnableTreeLayout.Value ? ModConfigTreeLayoutPatch.State : "关")}" +
            $" / 界面本地化: {(EnableLocalization.Value ? ModConfigLocalizationPatch.State : "关")}" +
            $" / 说明面板: {(EnableDescriptionPanel.Value ? "开" : "关")}" +
            $" / 工具行: {(HideLocalizationButtons.Value ? "仅语言行" : (XUnityBridge.Installed ? "全开（检测到 XUnity，仅兼容提醒）" : "全开"))}" +
            $" / 语言: {(string.IsNullOrEmpty(LanguageFile.Value) ? "跟随游戏" : LanguageFile.Value)}");
    }

    private void Update() { Overview.OverviewRepository.TickRecovery(); CursorWatch.Tick(); }
    private void OnApplicationQuit() => Overview.OverviewRepository.FlushAllRecovery();
    private void OnDestroy() { GamepadLock.Shutdown(); Overview.OverviewRepository.FlushAllRecovery(); Overview.AnimatorRestGate.Shutdown(); Diagnostics.RuntimeLogCapture.Stop(); Overview.OverviewController.Dispose(); Overview.OverviewPageCache.Dispose(); }

    /// <summary>
    /// If the update worker is mid-write (the game was restarted right after an update was deployed), wait for it
    /// instead of letting the loader open files that are being replaced. Plugins that BepInEx loads after this one
    /// are covered; ones loaded before are not, so the wait is bounded and a timeout is reported.
    /// </summary>
    private void RemoveOrphans(string section, params string[] keys)
    {
        try
        {
            var orphans = HarmonyLib.AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(Config) as System.Collections.Generic.Dictionary<ConfigDefinition, string>;
            bool removed = false;
            foreach (string key in keys)
            {
                var definition = new ConfigDefinition(section, key);
                removed |= Config.Remove(definition);
                if (orphans != null) removed |= orphans.Remove(definition);
            }

            if (removed) Config.Save();
        }
        catch (System.Exception ex) { Log.LogWarning("[配置] 清理旧配置项失败：" + ex.Message); }
    }

    /// <summary>更新助手实例锁被占用 = 助手在运行（与 OverviewUpdateQueue.EnsureWorker 同一个探测法）。</summary>
    private static bool WorkerRunning()
    {
        try
        {
            string path = Updating.ManagerIdentity.Runtime(BepInEx.Paths.BepInExRootPath, "worker-instance.lock");
            if (!System.IO.File.Exists(path)) return false;
            using var probe = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
            return false;
        }
        catch (System.IO.IOException) { return true; }
        catch (System.Exception) { return false; }
    }

    /// <summary>1.0.0 拆分：复制个人数据与签名身份、把 BepInEx\MCE 改名为 BepInEx\IGMM（见 <see cref="ManagerSplitMigration"/>）。</summary>
    private static void RunSplitMigration(ManagerSplitMigration.Report report)
    {
        try
        {
            string bep = BepInEx.Paths.BepInExRootPath.TrimEnd('\\', '/');
            // 复查 #6：改名前 ManagerIdentity.Runtime 回退到旧目录，UpdateWriteMarker.IsActive 查的就是旧的 updating.lock，
            // 并按 pid 与时效清掉崩溃留下的陈旧标记；不再单看文件在不在（陈旧锁会让改名永远推迟）
            bool busy = WorkerRunning() || LegacyWorkerRunning(bep) || Updating.UpdateWriteMarker.IsActive(bep);
            var result = ManagerSplitMigration.Run(bep, BepInEx.Paths.ConfigPath, System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), busy);
            result.Skipped.InsertRange(0, report.Skipped);
            if (report.Config || result.Data || result.Runtime || result.Signing || result.Skipped.Count > 0 || result.Note.Length > 0)
                Log.LogInfo($"[拆分] 从 ModConfig Enhance 接管：设置 {(report.Config ? "已复制" : "无需")} · 个人数据 {(result.Data ? $"已复制 {result.Copied} 个文件" : "无需")} · 运行目录 {(result.Runtime ? "BepInEx\\MCE → BepInEx\\IGMM" : "无需")} · 签名身份 {(result.Signing ? "已复制" : "无需")} · 改写路径 {result.Rewritten} 处{(result.Note.Length > 0 ? " · " + result.Note : "")}");
            foreach (string skipped in System.Linq.Enumerable.Take(result.Skipped, 20)) Log.LogWarning("[拆分] " + skipped);
        }
        catch (System.Exception ex) { Log.LogWarning("[拆分] 接管旧数据未完成，下次启动再试：" + ex.Message); }
    }

    /// <summary>旧版（2.x）助手的实例锁在 BepInEx\MCE 下；还在跑就不动运行目录。</summary>
    private static bool LegacyWorkerRunning(string bep)
    {
        try
        {
            string path = System.IO.Path.Combine(Updating.ManagerIdentity.LegacyRuntime(bep), "worker-instance.lock");
            if (!System.IO.File.Exists(path)) return false;
            using var probe = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);
            return false;
        }
        catch (System.IO.IOException) { return true; }
        catch (System.Exception) { return false; }
    }

    private static void WaitForPendingUpdateWrite()
    {
        string root;
        try { root = BepInEx.Paths.BepInExRootPath.TrimEnd('\\', '/'); }
        catch (System.Exception) { return; }
        if (!Updating.UpdateWriteMarker.IsActive(root)) return;
        string what = Updating.UpdateWriteMarker.Describe(root);
        Diagnostics.ManagerLog.Info("检测到更新正在提交，等待它完成后再继续加载…" + (what.Length > 0 ? "（" + what + "）" : ""));
        System.DateTime deadline = System.DateTime.UtcNow.AddSeconds(15);
        while (System.DateTime.UtcNow < deadline && Updating.UpdateWriteMarker.IsActive(root))
            System.Threading.Thread.Sleep(200);
        if (Updating.UpdateWriteMarker.IsActive(root))
            Diagnostics.ManagerLog.Warn("等待更新提交超过 15 秒，本次启动继续；正在更新的模组可能需要再次重启才生效。");
        else Diagnostics.ManagerLog.Info("更新提交已完成，继续加载。");
    }

    private void BindConfig()
    {
        const string secLayout = "1. Layout";
        EnableTreeLayout = Config.Bind(secLayout, "Tree sidebar", true, DescTreeSidebar);
        EnableDescriptionPanel = Config.Bind(secLayout, "Description panel", true, DescDescriptionPanel);
        ColourTheme = Config.Bind(secLayout, "Colour theme", ModConfigLook.ThemeMode.Blue, DescColourTheme);

        const string secLoc = "2. Localization";
        EnableLocalization = Config.Bind(secLoc, "UI localization", true, DescUiLocalization);
        HideLocalizationButtons = Config.Bind(secLoc, "Hide localization buttons", false, DescHideLocalizationButtons);
        WarnXUnity = Config.Bind(secLoc, "Warn about XUnity machine translation", true, DescWarnXUnity);
        // 2.5.0：两个旧的 XUnity 联动开关不再使用，从 cfg 里清掉（只删这两个键，其余原样）。
        // 2.5.4（二次审查）：ConfigFile.Remove 只删已绑定的项；没绑定的旧键在 BepInEx 的私有 OrphanedEntries 里，存盘时会原样写回，要从那里删。
        RemoveOrphans(secLoc, "Switch XUnity language with the language file", "Disable XUnity machine translation");
        AllowGuideEditing = Config.Bind("5. Authoring", "Allow editing user guide", false, DescAllowGuideEditing);

        const string secDiag = "4. Diagnostics";
        DebugLogging = Config.Bind(secDiag, "Debug logging", false, DescDebugLogging);

        const string secState = "3. State";
        LanguageFile = Config.Bind(secState, "Language file", "",
            new ConfigDescription("Selected language file name without .txt; empty = follow the game language. Maintained by the UI.", null, "Hidden"));
        PinnedMods = Config.Bind(secState, "Pinned mods", "",
            new ConfigDescription("Mods ticked in the Pin column (ModConfig's display names, | separated). Maintained by the UI.", null, "Hidden"));
        ExportExcludedMods = Config.Bind(secState, "Excluded from export", "",
            new ConfigDescription("Mods unticked in the Export column (ModConfig's display names, | separated). Maintained by the UI.", null, "Hidden"));
        SidebarSort = Config.Bind(secState, "Sort mode", SidebarSortMode.Default,
            new ConfigDescription("Sidebar mod-row sort mode, set by the sort dropdown.", null, "Hidden"));
        ExportSelectedOnly = Config.Bind(secState, "Export selected only", false,
            new ConfigDescription("State of the \"Selected only\" toggle. Maintained by the UI.", null, "Hidden"));
        IntroVideoVolume = Config.Bind(secState, "Introduction video volume", 0.8f,
            new ConfigDescription("Volume of videos in the pack introduction, set by the volume slider below each video.", new AcceptableValueRange<float>(0f, 1f), "Hidden"));
        IntroVideoMuted = Config.Bind(secState, "Introduction video muted", false,
            new ConfigDescription("Mute state of videos in the pack introduction, set by the speaker button. Maintained by the UI.", null, "Hidden"));
    }

    /// <summary>
    /// 置顶集合里不再需要自身行名——强制最顶是结构行为，不再吃「置顶」列的勾选。
    /// 把曾经写进去的那条清掉，保持 cfg 干净。
    /// </summary>
    private static void NormalizeSelfPin()
    {
        string current = PinnedMods.Value ?? "";
        if (current.Length == 0)
        {
            return;
        }

        List<string> kept = new List<string>();
        bool removed = false;
        foreach (string part in current.Split('|'))
        {
            string name = part.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            if (string.Equals(name, SelfRowName, StringComparison.Ordinal))
            {
                removed = true;
                continue;
            }

            kept.Add(name);
        }

        if (removed)
        {
            PinnedMods.Value = string.Join("|", kept);
            Log.LogInfo($"[{PluginName}] 已从置顶集合移除自身行名（自身行现在固定最顶）。");
        }
    }

    internal static string FixNaming(string input)
    {
        input = Regex.Replace(input, "([a-z])([A-Z])", "$1 $2");
        input = Regex.Replace(input, "([A-Z])([A-Z][a-z])", "$1 $2");
        input = Regex.Replace(input, "\\s+", " ");
        input = Regex.Replace(input, "([A-Z]\\.)\\s([A-Z]\\.)", "$1$2");
        return input.Trim();
    }

    /// <summary>侧栏模组行排序方式（排序下拉选；置顶行始终在前不受排序影响，本模组行恒第二）。</summary>
    internal enum SidebarSortMode
    {
        /// <summary>ModConfig 原生顺序（≈插件注册/加载顺序）</summary>
        Default,
        /// <summary>加载顺序倒过来</summary>
        LoadReversed,
        /// <summary>按当前显示名正序（zh 排序规则，中文按拼音、英文按字母）</summary>
        Name,
        /// <summary>按当前显示名倒序</summary>
        NameDesc,
    }
}
