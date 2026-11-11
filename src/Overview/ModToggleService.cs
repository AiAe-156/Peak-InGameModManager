#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Mono.Cecil;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YamlDotNet.RepresentationModel;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance.Overview;

/// <summary>Only an explicit UI action calls SetEnabled. Renames affect the next BepInEx startup.</summary>
internal sealed class ModToggleService
{
    private readonly OverviewRepository repository;
    private readonly string profileDirectory;
    private readonly IReadOnlyList<OverviewMod> knownMods;
    private readonly string pluginsRoot, bepinexRoot;
    private readonly object gate = new();
    internal bool IgnoreDependencyWarnings { get; set; }
    private sealed class Move
    {
        public string From = "";
        public string To = "";
    }
    private sealed class PluginRecord
    {
        public string Path = "";
        public string Guid = "";
        public string Version = "";
        public List<(string Guid, string Minimum)> Dependencies = new();
    }
    private sealed class Metadata
    {
        public string Path = "";
        public byte[] Original = Array.Empty<byte>();
        public byte[] Updated = Array.Empty<byte>();
    }
    private sealed class FileState
    {
        public long Length;
        public DateTime WrittenUtc;
    }
    private sealed class Plan
    {
        public string PluginRoot = "";
        public string ProfileRoot = "";
        public readonly List<Move> Moves = new();
        public readonly Dictionary<string, bool> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, bool> Packages = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, bool> PackageRoots = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> PackageIdentities = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, FileState> Observed = new(StringComparer.OrdinalIgnoreCase);
        public Metadata? ManagerMetadata;
    }

    internal ModToggleService(OverviewRepository repository, string managerProfileDirectory = "", string? pluginsDirectory = null, string? bepinexDirectory = null, IReadOnlyList<OverviewMod>? knownMods = null)
    {
        this.repository = repository;
        this.knownMods = knownMods ?? Array.Empty<OverviewMod>();
        profileDirectory = managerProfileDirectory ?? "";
        pluginsRoot = pluginsDirectory ?? Paths.PluginPath;
        bepinexRoot = bepinexDirectory ?? Paths.BepInExRootPath;
    }

    internal OverviewToggleResult SetEnabled(IReadOnlyList<OverviewMod> selected, bool enabled)
        => SetEnabledTargets(selected?.Select(mod => (mod, enabled)).ToList() ?? new List<(OverviewMod, bool)>());

    internal OverviewToggleResult SetEnabledTargets(IReadOnlyList<(OverviewMod Mod, bool Enabled)> targets, bool dryRun = false)
    {
        lock (gate)
        {
            string what = targets == null || targets.Count == 0 ? "" : string.Join("、", targets.Select(t => (t.Mod.Name.Length > 0 ? t.Mod.Name : t.Mod.RawName) + (t.Enabled ? "→启用" : "→停用")));
            if (what.Length > 0) Diagnostics.ManagerLog.Info("模组启停：" + what + (dryRun ? "（预检）" : ""));
            Diagnostics.ManagerLog.Debug($"启停目标 {targets?.Count ?? 0} 个 · 忽略依赖警告={IgnoreDependencyWarnings}");
            var prepared = PrepareTargets(targets!, out var transaction);
            if (!prepared.Success || dryRun) { if (!prepared.Success) Diagnostics.ManagerLog.Error("启停未执行：" + prepared.Message); return prepared; }
            var applied = transaction!.Apply();
            if (!applied.Success) { Diagnostics.ManagerLog.Error("启停写入失败：" + applied.Message); return applied; }
            var committed = transaction.Commit();
            if (committed.Success) { Diagnostics.ManagerLog.Info("启停完成：" + what); return applied; }
            var rollback = transaction.Rollback();
            Diagnostics.ManagerLog.Error("启停提交失败并已回滚：" + committed.Message + "；" + rollback.Message);
            return Fail(committed.Message + "；" + rollback.Message);
        }
    }

    /// <summary>Prepares the final dependency graph without writing files or changing UI state.</summary>
    internal OverviewToggleResult PrepareTargets(IReadOnlyList<(OverviewMod Mod, bool Enabled)> targets, out ModToggleTransaction? transaction)
    {
        lock (gate)
        {
            transaction = null;
            try
            {
                PackLibrary.EnsureAvailable();
                if (targets == null || targets.Count == 0) return Fail(Loc.T("Select a mod first.", "请先选择模组。", "請先選擇模組。"));
                var copy = targets.ToArray();
                transaction = new ModToggleTransaction(this, copy);
                return new OverviewToggleResult(true, Loc.F("Enablement check passed; {0} file(s) need renaming and will take effect after restarting the game.", "启停预检通过；{0} 个文件需要改名，下次启动游戏生效。", "啟停預檢通過；{0} 個文件需要改名，下次啟動游戲生效。", transaction.PlannedMoveCount));
            }
            catch (Exception error) { return Fail(Loc.F("Enablement check failed: {0}", "启停预检未通过：{0}", "啟停預檢未通過：{0}", error.Message)); }
        }
    }

    /// <summary>Apply keeps backups and can be rolled back until the surrounding profile commits.</summary>
    internal sealed class ModToggleTransaction
    {
        private readonly ModToggleService owner;
        private readonly (OverviewMod Mod, bool Enabled)[] targets;
        private readonly Plan plan;
        private readonly Dictionary<OverviewMod, bool> originalTargets;
        private bool applied, committed, finished;
        private string actionDirectory = "";
        private InstallationWriteLease? writeLease;
        internal string BackupDirectory => actionDirectory;
        internal int PlannedMoveCount => plan.Moves.Count;

        internal ModToggleTransaction(ModToggleService owner, (OverviewMod Mod, bool Enabled)[] targets)
        {
            this.owner = owner; this.targets = targets; plan = owner.BuildPlan(targets);
            originalTargets = targets.Select(t => t.Mod).Distinct().ToDictionary(m => m, m => m.TargetEnabled);
        }

        internal OverviewToggleResult Validate()
        {
            lock (owner.gate)
            {
                if (finished || applied) return Fail(Loc.T("The enablement transaction has already run or ended and cannot be checked again.", "启停事务已经执行或结束，不能再次预检。", "啟停事務已經執行或結束，不能再次預檢。"));
                try
                {
                    Plan fresh = owner.BuildPlan(targets);
                    if (!SamePlan(plan, fresh)) throw new IOException(Loc.T("Plugin files or the manager profile changed after validation. Apply the profile again.", "插件文件或管理器档案在预检后发生变化，请重新应用方案。", "插件文件或管理器檔案在預檢后發生變化，請重新應用方案。"));
                    return new OverviewToggleResult(true, Loc.T("Enablement check passed.", "启停预检通过。", "啟停預檢通過。"));
                }
                catch (Exception error) { return Fail(Loc.F("Enablement check failed: {0}", "启停预检未通过：{0}", "啟停預檢未通過：{0}", error.Message)); }
            }
        }

        internal OverviewToggleResult Apply()
        {
            lock (owner.gate)
            {
                OverviewToggleResult valid;
                try
                {
                    if (finished || applied) return Fail("The enablement transaction has already run or ended.");
                    writeLease = InstallationWriteLease.Acquire(owner.bepinexRoot);
                    valid = Validate();
                    if (!valid.Success) throw new IOException(valid.Message);
                    if (plan.Moves.Count > 0 || plan.ManagerMetadata != null)
                    {
                        string actionsRoot = SafePath(owner.repository.RootDirectory, Path.Combine(owner.repository.RootDirectory, "actions"));
                        actionDirectory = SafePath(actionsRoot, Path.Combine(actionsRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + System.Guid.NewGuid().ToString("N")));
                        Directory.CreateDirectory(actionDirectory);
                        if (plan.ManagerMetadata != null) File.WriteAllBytes(Path.Combine(actionDirectory, "mods.yml.before"), plan.ManagerMetadata.Original);
                        WriteRecord(actionDirectory, "prepared", targets, plan.Moves, "");
                        for (int i = 0; i < plan.Moves.Count; i++)
                            File.Copy(plan.Moves[i].From, Path.Combine(actionDirectory, i.ToString("D4") + ".backup"), false);
                        // Recheck after the backup copy, before the first mutation.
                        valid = Validate();
                        if (!valid.Success) throw new IOException(valid.Message);
                    }
                    if (plan.ProfileRoot.Length != 0) EnsureManagerClosed();
                    var writes = new List<InstallationWriteLease.Rewrite>();
                    Metadata? metadata = plan.ManagerMetadata;
                    if (metadata != null) writes.Add(new InstallationWriteLease.Rewrite { Path = metadata.Path, Before = metadata.Original, After = metadata.Updated });
                    var ledgerWrite = owner.PrepareShopLedger(plan);
                    if (ledgerWrite != null) writes.Add(ledgerWrite);
                    writeLease.Prepare(plan.Moves.Select(m => new InstallationWriteLease.Rename { From = m.From, To = m.To }), writes);
                    writeLease.Apply();
                    if (actionDirectory.Length != 0) WriteRecord(actionDirectory, "applied", targets, plan.Moves, "");
                    foreach (var target in targets) target.Mod.TargetEnabled = target.Enabled;
                    applied = true;
                    return new OverviewToggleResult(true, (plan.Moves.Count == 0 && metadata == null ? Loc.T("Files already match the requested state.", "文件已经处于目标状态。", "文件已經處于目標狀態。") : Loc.T("Enablement targets saved. Restart the game to use them.", "已应用启停目标，下次启动游戏生效。", "已應用啟停目標，下次啟動游戲生效。"))
                        + (plan.ProfileRoot.Length != 0 ? Loc.T("The configured manager profile was synchronized.", "已同步配置的管理器档案。", "已同步配置的管理器檔案。") : Loc.T("Only this game installation was changed. Configure the actual manager profile to preserve these changes on its next synchronization.", "仅修改当前游戏安装；管理器下次同步可能覆盖，请配置实际 profile。", "僅修改當前游戲安裝；管理器下次同步可能覆蓋，請配置實際 profile。"))
                        + (actionDirectory.Length == 0 ? "" : Loc.T("Backup record: ", "备份记录：", "備份記錄：") + actionDirectory));
                }
                catch (Exception error)
                {
                    var rollback = RollbackCore(error.Message);
                    return Fail(Loc.F("Operation incomplete: {0}；{1}", "操作未完成：{0}；{1}", "操作未完成：{0}；{1}", error.Message, rollback.Message));
                }
            }
        }

        internal OverviewToggleResult Rollback()
        {
            lock (owner.gate)
            {
                return RollbackCore("");
            }
        }

        internal OverviewToggleResult Commit()
        {
            lock (owner.gate)
            {
                if (committed) return new OverviewToggleResult(true, Loc.T("The enablement transaction has already been committed.", "启停事务已经提交。", "啟停事務已經提交。"));
                if (!applied || finished) return Fail(Loc.T("The enablement transaction has not completed successfully and cannot be committed.", "启停事务尚未成功执行，不能提交。", "啟停事務尚未成功執行，不能提交。"));
                try
                {
                    if (actionDirectory.Length != 0) WriteRecord(actionDirectory, "completed", targets, plan.Moves, "");
                    writeLease?.Commit();
                    writeLease?.Dispose(); writeLease = null;
                    committed = finished = true;
                    return new OverviewToggleResult(true, Loc.T("Enablement transaction committed.", "启停事务已提交。", "啟停事務已提交。"));
                }
                catch (Exception error) { return Fail(Loc.F("Could not commit the enablement record: {0}", "启停记录提交失败：{0}", "啟停記錄提交失敗：{0}", error.Message)); }
            }
        }

        private OverviewToggleResult RollbackCore(string reason)
        {
            var errors = new List<string>();
            if (committed) return Fail("The enablement transaction has already been committed.");
            try { writeLease?.Recover(); }
            catch (Exception error) { errors.Add(error.Message); }
            finally { writeLease?.Dispose(); writeLease = null; }
            if (errors.Count == 0)
            {
                foreach (var original in originalTargets) original.Key.TargetEnabled = original.Value;
                applied = false; committed = false; finished = true;
            }
            if (actionDirectory.Length != 0)
                try { WriteRecord(actionDirectory, errors.Count == 0 ? "rolled-back" : "rollback-incomplete", targets, plan.Moves, reason + " " + string.Join("；", errors)); }
                catch (Exception error) { errors.Add(Loc.T("Could not write the operation record: ", "操作记录写入失败：", "操作記錄寫入失敗：") + error.Message); }
            return errors.Count == 0 ? new OverviewToggleResult(true, Loc.T("All enablement files and manager state were rolled back.", "已回退全部启停文件和管理器状态。", "已回退全部啟停文件和管理器狀態。"))
                : Fail(Loc.F("Some changes could not be rolled back. Restore them from the backup: {0}；{1}", "部分回退失败，请按备份恢复：{0}；{1}", "部分回退失敗，請按備份恢復：{0}；{1}", actionDirectory, string.Join("；", errors)));
        }
    }

    private Plan BuildPlan(IReadOnlyList<(OverviewMod Mod, bool Enabled)> targets)
    {
        var plan = new Plan { PluginRoot = Path.GetFullPath(pluginsRoot) };
        SafePath(plan.PluginRoot, plan.PluginRoot);
        foreach (var mod in knownMods.Where(m => m.SourceConfirmed && m.IdentityConflict.Length == 0 && ShopInstalled.Key(m.SourceKey)))
        {
            if (plan.PackageIdentities.TryGetValue(mod.PackageRoot, out var prior) && !prior.Equals(mod.SourceKey, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Conflicting package identities: " + mod.PackageRoot);
            plan.PackageIdentities[mod.PackageRoot] = mod.SourceKey;
        }
        foreach (var target in targets)
        {
            var mod = target.Mod;
            if (mod == null) continue;
            if (mod.IdentityConflict.Length > 0) throw new InvalidOperationException(mod.DisplayName + "：\n" + mod.IdentityConflict);
            string packageRoot = SafePath(plan.PluginRoot, mod.PackageRoot);
            IEnumerable<string> files = Directory.Exists(packageRoot) ? SafeFiles(plan.PluginRoot, packageRoot)
                : SafeFiles(plan.PluginRoot, Path.GetDirectoryName(packageRoot)!).Where(p => Same(OverviewCatalogue.CanonicalPluginPath(p), OverviewCatalogue.CanonicalPluginPath(packageRoot)));
            if (OverviewUpdateQueue.HasPendingForPackage(packageRoot) || mod.AssemblyPaths.Any(OverviewCatalogue.IsUpdateCandidate) || files.Any(OverviewCatalogue.IsUpdateCandidate))
                throw new InvalidOperationException(Loc.F("This package is being updated. Complete or undo its update before changing enablement: {0}", "此包正在更新，请完成或撤销后再修改启停：{0}", "此包正在更新，請完成或撤銷後再修改啟停：{0}", mod.DisplayName));
        }
        bool managerMode = !string.IsNullOrWhiteSpace(profileDirectory);
        YamlStream? yaml = null;
        Metadata? metadata = null;
        var nodes = new Dictionary<string, YamlMappingNode>(StringComparer.OrdinalIgnoreCase);
        var managerOriginalStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool managerChanged = false;
        var managerTargets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (managerMode)
        {
            EnsureManagerClosed();
            plan.ProfileRoot = SafePath(Path.GetFullPath(profileDirectory), profileDirectory);
            string yamlPath = SafePath(plan.ProfileRoot, Path.Combine(plan.ProfileRoot, "mods.yml"));
            if (!File.Exists(yamlPath)) throw new InvalidOperationException(Loc.T("The configured manager profile has no mods.yml. Enter the actual profile root directory.", "配置的管理器档案没有 mods.yml；请填写实际 profile 根目录。", "配置的管理器檔案沒有 mods.yml；請填寫實際 profile 根目錄。"));
            metadata = new Metadata { Path = yamlPath, Original = File.ReadAllBytes(yamlPath) };
            Observe(plan, yamlPath);
            yaml = new YamlStream();
            using (var reader = new StringReader(Encoding.UTF8.GetString(metadata.Original).TrimStart('\uFEFF'))) yaml.Load(reader);
            if (yaml.Documents.Count != 1 || !(yaml.Documents[0].RootNode is YamlSequenceNode sequence))
                throw new InvalidOperationException(Loc.T("Unrecognized mods.yml format. No mods were changed.", "mods.yml 格式无法确认，未改动任何模组。", "mods.yml 格式無法確認，未改動任何模組。"));
            foreach (var child in sequence.Children)
            {
                if (!(child is YamlMappingNode node)) throw new InvalidOperationException(Loc.T("Unrecognized package entry format in mods.yml.", "mods.yml 包条目格式无法确认。", "mods.yml 包條目格式無法確認。"));
                string identity = Scalar(node, "name");
                if (string.IsNullOrEmpty(identity) || nodes.ContainsKey(identity)) throw new InvalidOperationException(Loc.T("Package identity in mods.yml is ambiguous and cannot be synchronized.", "mods.yml 包身份不明确，不能安全同步。", "mods.yml 包身份不明確，不能安全同步。"));
                nodes.Add(identity, node);
                if (!bool.TryParse(Scalar(node, "enabled"), out bool active)) throw new InvalidOperationException(Loc.F("{0} has an unrecognized manager enablement value.", "{0} 的管理器启用状态格式无法确认。", "{0} 的管理器啟用狀態格式無法確認。", identity));
                managerOriginalStates.Add(identity, active);
            }
        }
        var guidTargets = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            OverviewMod mod = target.Mod ?? throw new InvalidOperationException(Loc.T("The profile contains an empty mod entry.", "方案包含空模组条目。", "方案包含空模組條目。"));
            if (string.IsNullOrWhiteSpace(mod.Guid) || mod.AssemblyPaths.Count == 0 && !mod.ResourcePack) throw new InvalidOperationException(Loc.F("{0} has no confirmed plugin identity or file.", "{0} 没有确认的插件身份或文件。", "{0} 沒有確認的插件身份或文件。", mod.DisplayName));
            var expected = new HashSet<string>(mod.PluginGuids, StringComparer.OrdinalIgnoreCase) { mod.Guid };
            foreach (string guid in expected) AddTarget(guidTargets, guid, target.Enabled, Loc.T("Conflicting enablement targets for the same plugin: ", "同一插件身份有相反的启停目标：", "同一插件身份有相反的啟停目標："));
            if (!string.IsNullOrWhiteSpace(mod.PackageRoot))
            {
                string packageRoot = SafePath(plan.PluginRoot, mod.PackageRoot);
                AddTarget(plan.PackageRoots, packageRoot, target.Enabled, Loc.T("Conflicting enablement targets for the same package: ", "同一资料包有相反的启停目标：", "同一資料包有相反的啟停目標："));
                if (mod.SourceConfirmed)
                {
                    if (plan.PackageIdentities.TryGetValue(packageRoot, out var identity) && !identity.Equals(mod.SourceKey, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(Loc.F("{0} has conflicting manager identities for the same package.", "{0} 的同一资料包有不同管理器身份。", "{0} 的同一資料包有不同管理器身份。", mod.DisplayName));
                    plan.PackageIdentities[packageRoot] = mod.SourceKey;
                }
                if (Directory.Exists(SafePath(plan.PluginRoot, Path.Combine(packageRoot, "patchers")))
                    || (mod.SourceConfirmed && Directory.Exists(SafePath(bepinexRoot, Path.Combine(bepinexRoot, "patchers", mod.SourceKey)))))
                    throw new InvalidOperationException(Loc.F("{0} contains startup patch files. Enable or disable the package in your manager.", "{0} 包含启动补丁文件；请在管理器中启停该包。", "{0} 包含啟動補丁文件；請在管理器中啟停該包。", mod.DisplayName));
            }
            var actual = new List<PluginRecord>();
            int firstMove = plan.Moves.Count;
            foreach (string assemblyPath in mod.AssemblyPaths)
            {
                if (PluginFileNames.IsBackupCompanion(assemblyPath)) continue;
                string dll = OriginalDll(SafePath(plan.PluginRoot, assemblyPath));
                AddTarget(plan.Files, dll, target.Enabled, Loc.T("Conflicting enablement targets for the same plugin file: ", "同一插件文件有相反的启停目标：", "同一插件文件有相反的啟停目標："));
                string existing = ExistingFileForMod(dll, mod.DisplayName);
                SafePath(plan.PluginRoot, existing);
                Observe(plan, existing); actual.AddRange(ReadPlugins(existing));
                AddMove(dll, target.Enabled, plan.Moves);
            }
            // A resource pack has no assembly to read: its identity is its manifest, toggled below by renaming it.
            if (!mod.ResourcePack && (!expected.SetEquals(actual.Select(p => p.Guid)) || !actual.Any(p => p.Guid.Equals(mod.Guid, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(mod.PluginVersion) || p.Version == mod.PluginVersion))))
                throw new InvalidOperationException(Loc.F("{0} has a changed assembly identity or version. Refresh the overview and try again.", "{0} 的程序集身份或版本发生变化，刷新概览后重试。", "{0} 的程序集身份或版本發生變化，刷新概覽后重試。", mod.DisplayName));
            if (managerMode)
            {
                if (!mod.SourceConfirmed || string.IsNullOrWhiteSpace(mod.SourceNamespace) || string.IsNullOrWhiteSpace(mod.SourceName))
                    throw new InvalidOperationException(Loc.F("{0} has an unconfirmed manager package identity.", "{0} 的管理器包身份尚未确认。", "{0} 的管理器包身份尚未確認。", mod.DisplayName));
                string key = mod.SourceKey;
                AddTarget(plan.Packages, key, target.Enabled, Loc.T("Conflicting enablement targets for the same manager package: ", "同一管理器包有相反的启停目标：", "同一管理器包有相反的啟停目標："));
                if (!nodes.TryGetValue(key, out var node)) throw new InvalidOperationException(Loc.F("The manager profile does not contain the confirmed package {0}。", "管理器档案中找不到已确认包 {0}。", "管理器檔案中找不到已確認包 {0}。", key));
                string expectedRoot = SafePath(plan.PluginRoot, Path.Combine(plan.PluginRoot, key));
                if (!Same(mod.PackageRoot, expectedRoot)) throw new InvalidOperationException(Loc.F("The manager package and game copy paths do not match: {0}", "管理器包的游戏副本路径不匹配：{0}", "管理器包的游戲副本路徑不匹配：{0}", mod.DisplayName));
                if (mod.AssemblyPaths.Any(p => !IsUnder(expectedRoot, p))) throw new InvalidOperationException(Loc.F("{0} has plugin files outside its confirmed manager package.", "{0} 的插件文件不属于已确认管理器包。", "{0} 的插件文件不屬于已確認管理器包。", mod.DisplayName));
                string profilePackage = SafePath(plan.ProfileRoot, Path.Combine(plan.ProfileRoot, "BepInEx", "plugins", key));
                AddPackageMoves(plan, plan.PluginRoot, expectedRoot, target.Enabled, true);
                AddPackageMoves(plan, plan.ProfileRoot, profilePackage, target.Enabled, false);
                if (!PackagePluginIdentity(plan.PluginRoot, expectedRoot).SequenceEqual(PackagePluginIdentity(plan.ProfileRoot, profilePackage), StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Loc.F("{0} has a game copy whose plugin identity, version, or enablement differs from the manager profile. Synchronize it in the manager first.", "{0} 的游戏副本与管理器档案插件身份、版本或启用状态不一致，请先在管理器中同步。", "{0} 的游戲副本與管理器檔案插件身份、版本或啟用狀態不一致，請先在管理器中同步。", key));
                string gameVersion = PackageManifestVersion(plan, plan.PluginRoot, expectedRoot);
                string profileVersion = PackageManifestVersion(plan, plan.ProfileRoot, profilePackage);
                if (gameVersion != profileVersion || (!string.IsNullOrEmpty(mod.PackageVersion) && gameVersion != mod.PackageVersion))
                    throw new InvalidOperationException(Loc.F("{0} has a changed or unsynchronized package version. Refresh and synchronize it in the manager.", "{0} 的安装包版本发生变化或不同步，请刷新并在管理器中同步。", "{0} 的安裝包版本發生變化或不同步，請刷新并在管理器中同步。", key));
                if (gameVersion.Length != 0)
                {
                    if (!TryPackageVersion(gameVersion, out var installed) || !TryManagerVersion(node, out var managed) || installed != managed)
                        throw new InvalidOperationException(Loc.F("{0} has a mods.yml version differing from its plugin copy. Synchronize it in the manager.", "{0} 的 mods.yml 版本与插件副本不一致，请在管理器中同步。", "{0} 的 mods.yml 版本與插件副本不一致，請在管理器中同步。", key));
                }
                if (Directory.Exists(SafePath(plan.ProfileRoot, Path.Combine(plan.ProfileRoot, "BepInEx", "patchers", key)))
                    || Directory.Exists(SafePath(bepinexRoot, Path.Combine(bepinexRoot, "patchers", key))))
                    throw new InvalidOperationException(Loc.F("{0} contains startup patch files. Enable or disable the package in your manager.", "{0} 包含启动补丁文件；请在管理器中启停该包。", "{0} 包含啟動補丁文件；請在管理器中啟停該包。", key));
                bool managerEnabled = managerOriginalStates[key];
                bool gameEnabled = actual.Any(p => File.Exists(p.Path));
                if (managerEnabled != gameEnabled) throw new InvalidOperationException(Loc.F("{0} has different manager and game enablement states. Synchronize them first.", "{0} 的管理器与游戏启用状态不一致，请先同步。", "{0} 的管理器與游戲啟用狀態不一致，請先同步。", key));
                managerChanged |= managerEnabled != target.Enabled;
                managerTargets[key] = target.Enabled;
            }
            if (!managerMode && !string.IsNullOrWhiteSpace(mod.PackageRoot) && Directory.Exists(mod.PackageRoot))
            {
                string manifest = SafePath(plan.PluginRoot, Path.Combine(mod.PackageRoot, "manifest.json"));
                if (File.Exists(manifest) || File.Exists(manifest + ".old"))
                { Observe(plan, File.Exists(manifest) ? manifest : manifest + ".old"); AddMove(manifest, target.Enabled, plan.Moves); }
            }
            if (!mod.CanToggle && plan.Moves.Count != firstMove) throw new InvalidOperationException(mod.DisplayName + "：" + mod.ToggleReason);
        }
        var unique = plan.Moves.GroupBy(m => m.From, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        plan.Moves.Clear(); plan.Moves.AddRange(unique);
        foreach (Move move in plan.Moves)
        {
            if (!File.Exists(move.From)) throw new IOException(Loc.F("Source file changed: {0}", "源文件已变化：{0}", "源文件已變化：{0}", move.From));
            if (File.Exists(move.To) || Directory.Exists(move.To)) throw new IOException(Loc.F("Destination already exists and cannot be overwritten: {0}", "目标文件已存在，不能覆盖：{0}", "目標文件已存在，不能覆蓋：{0}", move.To));
            if (OriginalDllIfPlugin(move.From) != null) CheckProtectedAssembly(move.From);
        }
        PrepareShopLedger(plan); // Read-only preflight; actual enlistment is under the shared lease.
        if (!IgnoreDependencyWarnings)
        {
            CheckDependencies(plan);
            CheckPackageDependencies(plan);
            if (managerMode) CheckManagerDependencies(nodes);
        }
        else
        {
            // MCE required assemblies remain protected above. User consent only relaxes
            // dependency completeness, never path/identity/copy/update ownership checks.
            var finalPlugins = SafeFiles(plan.PluginRoot, plan.PluginRoot)
                .Where(f => OriginalDllIfPlugin(f) != null && !PluginFileNames.IsBackupCompanion(f))
                .Where(f => plan.Files.TryGetValue(OriginalDll(f), out bool enabled) ? enabled : f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .SelectMany(ReadPlugins).ToList();
            // Existing same-GUID versions are resolved by the loader. They do not make
            // an unrelated save unsafe; exact target paths are checked above.
            var finalGuids = new HashSet<string>(finalPlugins.Select(p => p.Guid), StringComparer.OrdinalIgnoreCase);
            foreach (var seed in knownMods.Where(m => m.Guid.Equals(Plugin.PluginGuid, StringComparison.OrdinalIgnoreCase)))
                foreach (var dependency in seed.RuntimeDependencies.Where(d => !d.Optional))
                    if (!dependency.Id.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase) && !finalGuids.Contains(dependency.Id))
                        throw new InvalidOperationException("MCE required dependency cannot be disabled: " + dependency.Id);
        }
        if (yaml != null && metadata != null)
        {
            if (managerChanged)
            {
                // Line-level, like ManagerProfileSync: only each entry's enabled line changes, all other bytes stay as written.
                metadata.Updated = ManagerProfileSync.SetEnabled(metadata.Original, managerTargets); plan.ManagerMetadata = metadata;
            }
        }
        return plan;
    }

    private InstallationWriteLease.Rewrite? PrepareShopLedger(Plan plan)
    {
        if (plan.Moves.Count == 0) return null;
        string ledgerPath = PackLibrary.Active != null ? Path.Combine(PackLibrary.Workspace, "shop-installed.json")
            : Path.Combine(bepinexRoot, "MCE", "shop", "installed-unmanaged.json");
        SafePath(bepinexRoot, ledgerPath);
        if (!File.Exists(ledgerPath)) return null;
        byte[] before = File.ReadAllBytes(ledgerPath);
        var ledger = JsonConvert.DeserializeObject<ShopLedger>(Encoding.UTF8.GetString(before))
            ?? throw new IOException("Shop ownership ledger is empty.");
        if (ledger.SchemaVersion != 1 || ledger.Packages == null) throw new IOException("Shop ownership ledger is unsupported.");
        bool changed = false;
        foreach (var move in plan.Moves.Where(m => IsUnder(bepinexRoot, m.From)))
        {
            string from = Path.GetRelativePath(bepinexRoot, move.From).Replace('\\', '/');
            string to = Path.GetRelativePath(bepinexRoot, move.To).Replace('\\', '/');
            var owned = ledger.Packages.SelectMany(p => p.Files).Where(f => f.Path.Equals(from, StringComparison.OrdinalIgnoreCase)).ToList();
            if (ledger.Packages.SelectMany(p => p.Files).Any(f => f.Path.Equals(to, StringComparison.OrdinalIgnoreCase)) || owned.Count > 1)
                throw new IOException("Ambiguous shop file ownership: " + from);
            if (owned.Count == 0) continue;
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var input = File.OpenRead(move.From);
            string hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            if (!hash.Equals(owned[0].Hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Shop-owned file was modified: " + from);
            owned[0].Path = to; changed = true;
        }
        if (!changed) return null;
        ledger.Revision++;
        return new InstallationWriteLease.Rewrite { Path = ledgerPath, Before = before,
            After = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(ledger, Formatting.Indented)) };
    }

    private static void AddTarget(Dictionary<string, bool> states, string key, bool value, string reason)
    {
        if (states.TryGetValue(key, out bool prior) && prior != value) throw new InvalidOperationException(reason + key);
        states[key] = value;
    }
    private static void Observe(Plan plan, string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists) throw new IOException(Loc.F("The confirmed file does not exist: {0}", "确认的文件不存在：{0}", "確認的文件不存在：{0}", file));
        plan.Observed[file] = new FileState { Length = info.Length, WrittenUtc = info.LastWriteTimeUtc };
    }
    private static bool MatchesObserved(Plan plan, string file)
    {
        var info = new FileInfo(file);
        return info.Exists && plan.Observed.TryGetValue(file, out var expected)
            && info.Length == expected.Length && info.LastWriteTimeUtc == expected.WrittenUtc;
    }
    private static bool SamePlan(Plan first, Plan second) => Same(first.PluginRoot, second.PluginRoot)
        && string.Equals(first.ProfileRoot, second.ProfileRoot, StringComparison.OrdinalIgnoreCase)
        && first.Moves.Count == second.Moves.Count
        && first.Moves.All(m => second.Moves.Any(n => Same(m.From, n.From) && Same(m.To, n.To)))
        && first.Observed.Count == second.Observed.Count
        && first.Observed.All(p => second.Observed.TryGetValue(p.Key, out var file) && file.Length == p.Value.Length && file.WrittenUtc == p.Value.WrittenUtc)
        && (first.ManagerMetadata == null ? second.ManagerMetadata == null : second.ManagerMetadata != null
            && first.ManagerMetadata.Original.SequenceEqual(second.ManagerMetadata.Original));
    private static string? OriginalDllIfPlugin(string path) => PluginFileNames.IsAssembly(path) ? OriginalDll(path) : null;
    private static void ReplaceMetadata(string path, byte[] bytes)
    {
        string staged = path + ".mce-" + System.Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(staged, bytes); File.Replace(staged, path, null);
    }

    private static OverviewToggleResult Fail(string reason) => new(false, reason);
    private static bool Same(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    private static bool IsUnder(string root, string path) => Same(root, path) || Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string SafePath(string root, string path)
    {
        string full = Path.GetFullPath(path);
        string basePath = Path.GetFullPath(root);
        if (!IsUnder(basePath, full)) throw new InvalidOperationException(Loc.F("Destination is outside the confirmed plugin/profile directory: {0}", "目标不在确认的插件/档案目录内：{0}", "目標不在確認的插件/檔案目錄內：{0}", full));
        for (string? part = full; part != null; part = Path.GetDirectoryName(part))
        {
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(Loc.F("The directory contains a link or reparse point and cannot be modified: {0}", "目录包含链接或重解析点，不能安全修改：{0}", "目錄包含鏈接或重解析點，不能安全修改：{0}", part));
            if (InstallationPaths.AtGameRoot(part)) break;
        }
        return full;
    }
    private static string OriginalDll(string path)
    {
        if (!PluginFileNames.IsAssembly(path) || PluginFileNames.IsUpdateCandidate(path))
            throw new InvalidOperationException("Not a restorable plugin file: " + path);
        return PluginFileNames.OriginalPath(path);
    }
    private static string ExistingFile(string original) => ExistingFileForMod(original, Path.GetFileName(original));
    private static string ExistingFileForMod(string original, string modName)
    {
        // Non-plugin package assets keep the historical .old policy.
        var copies = PluginFileNames.IsAssembly(original) ? PluginFileNames.ActionableCopies(original)
            : new[] { original, original + ".old" }.Where(File.Exists).ToList();
        if (copies.Count > 1) throw new IOException(OverviewFileConflicts.Copies(modName, PluginFileNames.IsAssembly(original) ? PluginFileNames.ExistingCopies(original) : copies));
        if (copies.Count == 0) throw new IOException(Loc.F("File does not exist: {0}", "文件不存在：{0}", "文件不存在：{0}", original));
        return copies[0];
    }
    private static void AddMove(string original, bool enabled, List<Move> moves)
    {
        string from = ExistingFile(original);
        if (enabled && PluginFileNames.IsForeignDisabled(from))
            throw new InvalidOperationException("Disabled by another tool. Restore it using that tool: " + from);
        bool currentlyEnabled = from.Equals(original, StringComparison.OrdinalIgnoreCase);
        if (currentlyEnabled == enabled) return;
        string to = enabled ? original : original + (PluginFileNames.IsAssembly(original) ? PluginFileNames.DisabledSuffix : ".old");
        if (moves.Any(m => Same(m.From, from) && Same(m.To, to))) return;
        moves.Add(new Move { From = from, To = to });
    }
    private static IEnumerable<string> SafeFiles(string root, string directory)
    {
        SafePath(root, directory);
        foreach (string file in Directory.GetFiles(directory)) yield return SafePath(root, file);
        foreach (string child in Directory.GetDirectories(directory))
            foreach (string file in SafeFiles(root, child)) yield return file;
    }
    private static void AddPackageMoves(Plan plan, string root, string package, bool enabled, bool gameCopy)
    {
        SafePath(root, package);
        if (!Directory.Exists(package)) throw new IOException(Loc.F("Package directory does not exist: {0}", "包目录不存在：{0}", "包目錄不存在：{0}", package));
        foreach (string file in SafeFiles(root, package))
        {
            if (PluginFileNames.IsBackupCompanion(file)) continue;
            if (PluginFileNames.IsUpdateCandidate(file)) throw new InvalidOperationException("Package has a pending update: " + file);
            if (enabled && PluginFileNames.IsForeignDisabled(file)) throw new InvalidOperationException(Loc.F("The package uses another tool's disabled marker. Restore it using that tool: {0}", "包中存在其他工具的禁用标记，不能代替它恢复：{0}", "包中存在其他工具的禁用標記，不能代替它恢復：{0}", file));
            Observe(plan, file);
            string original = OriginalDllIfPlugin(file) ?? (file.EndsWith(".old", StringComparison.OrdinalIgnoreCase) ? file.Substring(0, file.Length - 4) : file);
            if (gameCopy && original.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                AddTarget(plan.Files, original, enabled, Loc.T("Conflicting enablement targets for the same plugin file: ", "同一插件文件有相反的启停目标：", "同一插件文件有相反的啟停目標："));
            AddMove(original, enabled, plan.Moves);
        }
    }
    private static List<PluginRecord> ReadPlugins(string file)
    {
        var result = new List<PluginRecord>();
        if (PluginFileNames.IsBackupCompanion(file)) return result;
        AssemblyDefinition assembly;
        using var resolver = OverviewAssemblyReader.ResolverFor(file);
        try { assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { ReadingMode = ReadingMode.Deferred, ReadSymbols = false, AssemblyResolver = resolver }); }
        catch (BadImageFormatException) { return result; }
        using var ownedAssembly = assembly;
        foreach (var module in assembly.Modules)
            foreach (var type in Flatten(module.Types))
            {
                var plugin = type.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
                if (plugin == null || plugin.ConstructorArguments.Count < 3) continue;
                var record = new PluginRecord { Path = OriginalDll(file), Guid = (string)plugin.ConstructorArguments[0].Value, Version = (string)plugin.ConstructorArguments[2].Value };
                foreach (var dependency in type.CustomAttributes.Where(a => a.AttributeType.FullName == "BepInEx.BepInDependency"))
                {
                    if (dependency.ConstructorArguments.Count == 0) continue;
                    string minimum = "";
                    bool hard = true;
                    if (dependency.ConstructorArguments.Count > 1)
                    {
                        object value = dependency.ConstructorArguments[1].Value;
                        if (value is string version) minimum = version;
                        else hard = (Convert.ToInt32(value) & 1) != 0;
                    }
                    if (hard) record.Dependencies.Add(((string)dependency.ConstructorArguments[0].Value, minimum));
                }
                result.Add(record);
            }
        return result;
    }
    private static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types) { yield return type; foreach (var child in Flatten(type.NestedTypes)) yield return child; }
    }
    private static List<string> PackagePluginIdentity(string root, string package) => SafeFiles(root, package)
        .Where(p => OriginalDllIfPlugin(p) != null && !PluginFileNames.IsBackupCompanion(p))
        .SelectMany(file => ReadPlugins(file).Select(p => p.Guid + "@" + p.Version + ":" + file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    private static string PackageManifestVersion(Plan plan, string root, string package)
    {
        string path = SafePath(root, Path.Combine(package, "manifest.json"));
        if (File.Exists(path) && File.Exists(path + ".old")) throw new IOException(Loc.F("Both the original package manifest and .old copy exist: {0}", "原安装包说明与 .old 同时存在：{0}", "原安裝包說明與 .old 同時存在：{0}", path));
        if (!File.Exists(path)) path += ".old";
        if (!File.Exists(path)) return "";
        Observe(plan, path);
        return JObject.Parse(File.ReadAllText(path, Encoding.UTF8)).Value<string>("version_number") ?? "";
    }
    private static void CheckProtectedAssembly(string file)
    {
        if (!File.Exists(file)) throw new IOException(Loc.F("The confirmed assembly does not exist: {0}", "确认的程序集不存在：{0}", "確認的程序集不存在：{0}", file));
        foreach (var record in ReadPlugins(file))
            if (ModConfigEnhance.Updating.ComponentDependencyGraph.IsProtected(record.Guid))
                throw new InvalidOperationException(Loc.F("MCE and the libraries it runs on cannot be disabled or uninstalled: {0}", "MCE 及其运行所需的基础库不能停用或卸载：{0}", "MCE 及其執行所需的基礎庫不能停用或解除安裝：{0}", record.Guid));
    }
    private static void CheckDependencies(Plan plan)
    {
        var records = new List<PluginRecord>();
        foreach (string file in SafeFiles(plan.PluginRoot, plan.PluginRoot))
        {
            if (OriginalDllIfPlugin(file) == null || PluginFileNames.IsBackupCompanion(file)) continue;
            string original = OriginalDll(file);
            ExistingFile(original);
            Observe(plan, file);
            var info = ReadPlugins(file);
            bool afterEnabled = plan.Files.TryGetValue(original, out bool target) ? target : file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            if (afterEnabled) records.AddRange(info);
        }
        var byGuid = records.GroupBy(r => r.Guid, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        // Multiple installed versions alone are advisory; dependency satisfaction can
        // use any enabled provider. File-operation ambiguity is validated separately.
        var disabledGuids = plan.Files.Where(p => !p.Value).SelectMany(p => ReadPlugins(ExistingFile(p.Key)))
            .Select(p => p.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
            foreach (var dependency in record.Dependencies)
            {
                // Existing pack defects do not veto unrelated edits. Newly enabled owners and
                // explicitly removed providers still undergo the complete dependency check.
                if (!(plan.Files.TryGetValue(record.Path, out bool enabled) && enabled) && !disabledGuids.Contains(dependency.Guid)) continue;
                if (!byGuid.TryGetValue(dependency.Guid, out var providers)) throw new InvalidOperationException(Loc.F("{0} requires {1}. Select the related mods too, or keep the dependency enabled.", "{0} 必须依赖 {1}；请同时选择有关模组，或保留该依赖启用。", "{0} 必須依賴 {1}；請同時選擇有關模組，或保留該依賴啟用。", record.Guid, dependency.Guid));
                if (!string.IsNullOrEmpty(dependency.Minimum))
                {
                    if (!System.Version.TryParse(dependency.Minimum, out var minimum)) throw new InvalidOperationException(Loc.F("{0} has an unknown plugin dependency version: {1}", "{0} 的插件依赖版本无法确认：{1}", "{0} 的插件依賴版本無法確認：{1}", record.Guid, dependency.Minimum));
                    if (!providers.Any(p => System.Version.TryParse(p.Version, out var actual) && actual >= minimum))
                        throw new InvalidOperationException(Loc.F("{0} requires {1} version at least {2}。", "{0} 需要 {1} 至少 {2}。", "{0} 需要 {1} 至少 {2}。", record.Guid, dependency.Guid, dependency.Minimum));
                }
            }
    }
    private static string Scalar(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar ? scalar.Value ?? "" : "";
    private static void CheckPackageDependencies(Plan plan)
    {
        var manifests = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string manifestPath in SafeFiles(plan.PluginRoot, plan.PluginRoot).Where(f => Path.GetFileName(f).Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(f).Equals("manifest.json.old", StringComparison.OrdinalIgnoreCase)))
        {
            string directory = Path.GetDirectoryName(manifestPath)!;
            string key = Path.GetFileName(directory);
            Observe(plan, manifestPath);
            JObject manifest = JObject.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
            string ns = manifest.Value<string>("namespace") ?? manifest.Value<string>("author") ?? "";
            string name = manifest.Value<string>("name") ?? "";
            if (ThunderstoreUpdateService.ValidPart(ns) && ThunderstoreUpdateService.ValidPart(name)) key = ns + "-" + name;
            else if (ThunderstoreUpdateService.TryParseSource(key, out var folderNs, out var folderName) && folderName.Equals(name, StringComparison.OrdinalIgnoreCase)) key = folderNs + "-" + folderName;
            else if (ThunderstoreUpdateService.TryParseSource(manifest.Value<string>("website_url") ?? "", out var linkNs, out var linkName) && linkName.Equals(name, StringComparison.OrdinalIgnoreCase)) key = linkNs + "-" + linkName;
            if (plan.PackageIdentities.TryGetValue(directory, out var confirmed)) key = confirmed;
            if (manifests.ContainsKey(key)) throw new InvalidOperationException(Loc.F("Multiple copies have the same package identity. Dependencies are ambiguous: {0}", "安装包身份有多个副本，不能确认依赖：{0}", "安裝包身份有多個副本，不能確認依賴：{0}", key));
            manifests.Add(key, manifest);
            if (plan.PackageRoots.ContainsKey(directory)) changed.Add(key);
            var files = SafeFiles(plan.PluginRoot, directory).ToList();
            bool afterEnabled = plan.PackageRoots.TryGetValue(directory, out bool target) ? target
                : files.Any(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    || (!files.Any(f => OriginalDllIfPlugin(f) != null) && !manifestPath.EndsWith(".old", StringComparison.OrdinalIgnoreCase));
            if (afterEnabled) active.Add(key);
        }
        foreach (string key in active)
        {
            if (!manifests.TryGetValue(key, out var manifest)) continue;
            if (!(manifest["dependencies"] is JArray dependencies)) continue;
            foreach (var item in dependencies)
            {
                string identity = item.Value<string>() ?? "";
                int split = identity.LastIndexOf('-');
                if (split <= 0)
                {
                    if (!changed.Contains(key)) continue;
                    throw new InvalidOperationException(Loc.F("{0} has an unrecognized package dependency format.", "{0} 的包依赖格式无法确认。", "{0} 的包依賴格式無法確認。", key));
                }
                string dependency = identity.Substring(0, split);
                if (!changed.Contains(key) && !changed.Contains(dependency)) continue;
                // BepInEx is the loader, never an ordinary controllable package.
                if (PackageRules.IsLoader(dependency)) continue;
                if (!active.Contains(dependency)) throw new InvalidOperationException(Loc.F("{0} depends on package {1}. Select the related packages too, or keep the dependency enabled.", "{0} 的安装包依赖 {1}；请同时选择相关包或保留依赖启用。", "{0} 的安裝包依賴 {1}；請同時選擇相關包或保留依賴啟用。", key, dependency));
                string required = identity.Substring(split + 1);
                if (!TryPackageVersion(required, out var minimum)) throw new InvalidOperationException(Loc.F("{0} has an unknown package dependency version: {1}", "{0} 的包依赖版本无法确认：{1}", "{0} 的包依賴版本無法確認：{1}", key, required));
                if (!manifests.TryGetValue(dependency, out var provider) || !TryPackageVersion(provider.Value<string>("version_number") ?? "", out var actual) || actual < minimum)
                    throw new InvalidOperationException(Loc.F("{0} requires package {1} version at least {2}。", "{0} 的包依赖需要 {1} 至少 {2}。", "{0} 的包依賴需要 {1} 至少 {2}。", key, dependency, required));
            }
        }
    }
    private static void EnsureManagerClosed()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string name;
                try { name = process.ProcessName; } catch { throw new InvalidOperationException(Loc.T("Cannot determine whether the manager is closed. Close it before continuing.", "无法确认管理器是否已关闭；请关闭管理器再操作。", "無法確認管理器是否已關閉；請關閉管理器再操作。")); }
                if (name.IndexOf("r2modman", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Thunderstore", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException(Loc.T("Close r2modman/Thunderstore first. Its running cache may overwrite profile changes.", "请先关闭 r2modman/Thunderstore 管理器，其运行缓存可能覆盖档案修改。", "請先關閉 r2modman/Thunderstore 管理器，其運行緩存可能覆蓋檔案修改。"));
            }
        }
    }
    private static void CheckManagerDependencies(Dictionary<string, YamlMappingNode> nodes)
    {
        foreach (var pair in nodes)
        {
            if (!bool.TryParse(Scalar(pair.Value, "enabled"), out bool active)) throw new InvalidOperationException(Loc.F("{0} has an unrecognized manager enablement value.", "{0} 的管理器启用状态格式无法确认。", "{0} 的管理器啟用狀態格式無法確認。", pair.Key));
            if (!active || !pair.Value.Children.TryGetValue(new YamlScalarNode("dependencies"), out var dependenciesNode)) continue;
            if (!(dependenciesNode is YamlSequenceNode dependencies)) throw new InvalidOperationException(Loc.F("{0} has an unrecognized manager dependency format.", "{0} 的管理器依赖格式无法确认。", "{0} 的管理器依賴格式無法確認。", pair.Key));
            foreach (var entry in dependencies.Children)
            {
                string identity = (entry as YamlScalarNode)?.Value ?? "";
                int split = identity.LastIndexOf('-');
                if (split <= 0) throw new InvalidOperationException(Loc.F("{0} has an unrecognized manager package dependency format.", "{0} 的管理器包依赖格式无法确认。", "{0} 的管理器包依賴格式無法確認。", pair.Key));
                string dependency = identity.Substring(0, split);
                if (PackageRules.IsLoader(dependency)) continue;
                if (!nodes.TryGetValue(dependency, out var provider) || !bool.TryParse(Scalar(provider, "enabled"), out bool providerEnabled) || !providerEnabled)
                    throw new InvalidOperationException(Loc.F("{0} requires manager package {1} to be enabled. Select the related packages too.", "{0} 的管理器依赖 {1} 必须启用；请同时选择相关包。", "{0} 的管理器依賴 {1} 必須啟用；請同時選擇相關包。", pair.Key, dependency));
                string required = identity.Substring(split + 1);
                if (TryPackageVersion(required, out var minimum))
                {
                    if (!TryManagerVersion(provider, out var actual) || actual < minimum)
                        throw new InvalidOperationException(Loc.F("{0} requires manager package {1} version at least {2}。", "{0} 的管理器依赖需要 {1} 至少 {2}。", "{0} 的管理器依賴需要 {1} 至少 {2}。", pair.Key, dependency, required));
                }
                else throw new InvalidOperationException(Loc.F("{0} has an unknown manager dependency version: {1}", "{0} 的管理器依赖版本无法确认：{1}", "{0} 的管理器依賴版本無法確認：{1}", pair.Key, required));
            }
        }
    }
    private static bool TryManagerVersion(YamlMappingNode node, out System.Version version)
    {
        version = new System.Version(0, 0, 0);
        if (!node.Children.TryGetValue(new YamlScalarNode("versionNumber"), out var value) || !(value is YamlMappingNode map)) return false;
        return System.Version.TryParse(Scalar(map, "major") + "." + Scalar(map, "minor") + "." + Scalar(map, "patch"), out version!);
    }
    private static bool TryPackageVersion(string text, out System.Version version)
    {
        int suffix = text.IndexOfAny(new[] { '-', '+' });
        return System.Version.TryParse(suffix < 0 ? text : text.Substring(0, suffix), out version!);
    }
    private static void WriteRecord(string directory, string status, IReadOnlyList<(OverviewMod Mod, bool Enabled)> targets, List<Move> moves, string error)
        => File.WriteAllText(Path.Combine(directory, "transaction.json"), JsonConvert.SerializeObject(new
        { status, timestamp = DateTime.Now, targets = targets.Select(t => new { guid = t.Mod.Guid, enabled = t.Enabled, currentlyLoaded = t.Mod.Loaded }), moves, error }, Formatting.Indented), new UTF8Encoding(false));
}

