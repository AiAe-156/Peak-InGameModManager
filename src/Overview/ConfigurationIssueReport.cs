#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ModConfigEnhance.Overview;

internal static class ConfigurationIssueReport
{
    private sealed class Issue
    {
        internal int Category;
        internal string Name = "", Reason = "", Suggestion = "";
    }
    internal static string Explain(string detail)
    {
        string text = Loc.LocalizeMessage(detail);
        if (text.IndexOf("Sharing violation", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("worker.lock", StringComparison.OrdinalIgnoreCase) >= 0)
            return Loc.T("Files are busy; wait for the current write task and retry.", "文件正在被写入任务占用，请等待该任务结束后重试。", "檔案正被寫入工作占用，請等待工作結束後重試。");
        if (text.IndexOf("MCE required dependency", StringComparison.OrdinalIgnoreCase) >= 0)
            return Loc.T("The requested enablement would disable a required manager component.", "本次启停会关闭管理器运行必需的依赖组件。", "本次啟停會關閉管理器執行必需的依賴元件。");
        if (text.IndexOf("Multiple enabled plugin identities", StringComparison.OrdinalIgnoreCase) >= 0)
            return Loc.T("Several installed versions were detected. This alone does not prevent saving.", "检测到多个已安装版本；重复版本本身不阻止保存。", "偵測到多個已安裝版本；重複版本本身不阻止儲存。");
        if (text.IndexOf("Conflicting package identities", StringComparison.OrdinalIgnoreCase) >= 0)
            return Loc.T("A single folder has conflicting package ownership records.", "同一个文件夹存在相互矛盾的安装包归属记录，请核对目录与清单。", "同一個資料夾存在互相矛盾的安裝包歸屬記錄，請核對目錄與清單。");
        if (text.IndexOf("Access", StringComparison.OrdinalIgnoreCase) >= 0 && (text.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0))
            return Loc.T("The installation or configuration file is not writable.", "无法写入安装或配置文件，请检查文件只读属性与目录权限。", "無法寫入安裝或設定檔案，請檢查唯讀屬性與目錄權限。");
        return text.Replace("MCE_EXTERNAL_CONFLICT:", Loc.T("External edit: ", "外部修改：", "外部修改："));
    }

    private static string Safe(string value) => value.Replace("&", "＆").Replace("<", "‹").Replace(">", "›");
    private static string Dim(string value) => "<size=85%><color=#929DA8>" + Safe(value) + "</color></size>";
    internal static string TailPath(string path)
    {
        string normalized = path.Replace('\\', '/');
        int start = normalized.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase) ? 0
            : normalized.IndexOf("/BepInEx/", StringComparison.OrdinalIgnoreCase);
        return start < 0 ? path : path.Substring(start == 0 ? 0 : start + 1);
    }
    private static string Caption(OverviewMod mod, string? version = null, string path = "")
    {
        string primary = OverviewLanguageBridge.GetDisplayName(mod);
        string original = string.IsNullOrWhiteSpace(mod.RawName) ? mod.Name : mod.RawName;
        string detail = (primary.Equals(original, StringComparison.OrdinalIgnoreCase) ? "" : original + "，")
            + (version ?? (mod.PluginVersion.Length > 0 ? mod.PluginVersion : mod.PackageVersion));
        if (path.Length > 0) detail += " · " + TailPath(path);
        return Safe(primary) + (detail.Length > 0 ? " " + Dim("（" + detail + "）") : "");
    }
    internal static bool PackageMatches(OverviewMod mod, string key) =>
        (mod.PackageNamespace + "-" + mod.PackageName).Equals(key, StringComparison.OrdinalIgnoreCase)
        || mod.SourceConfirmed && (mod.SourceNamespace + "-" + mod.SourceName).Equals(key, StringComparison.OrdinalIgnoreCase);
    internal static List<OverviewMod> Unidentified(IReadOnlyList<OverviewMod> mods) =>
        mods.Where(m => !m.SourceConfirmed && m.SourceKind != ModSourceKind.Local && !ModSources.ExplicitLocal(m)).ToList();
    internal static IReadOnlyList<OverviewMod> Related(OverviewRepository repo, IReadOnlyList<OverviewMod> mods)
    {
        var result = new HashSet<OverviewMod>(Unidentified(mods));
        var duplicates = OverviewPluginCopies.Duplicates(mods);
        foreach (var members in OverviewPluginCopies.SameNameCopies(mods)) foreach (var mod in members) result.Add(mod);
        var inventory = ConfigurationPackageInventory.Get(mods);
        foreach (var match in PackConflictService.Evaluate(repo, mods).Where(c => c.Planned && !c.Ignored))
            foreach (var member in match.Members) foreach (var mod in mods.Where(m => m.Name == member.Name)) result.Add(mod);
        foreach (var mod in mods)
        {
            if (OverviewPluginCopies.Contains(mod, duplicates)) result.Add(mod);
            if (repo.PlannedEnabled(mod) && (mod.IdentityConflict.Length > 0 || mod.RuntimeDependencies.Where(d => !d.Optional).Any(d => !mods.Any(p => repo.PlannedEnabled(p)
                && (p.Guid.Equals(d.Id, StringComparison.OrdinalIgnoreCase) || p.PluginGuids.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
                && (d.MinimumVersion.Length == 0 || Version.TryParse(p.PluginVersion, out var have) && Version.TryParse(d.MinimumVersion, out var need) && have >= need))))) result.Add(mod);
            foreach (string dependency in mod.PackageDependencies)
            {
                int split = dependency.LastIndexOf('-'); if (split < 1) continue;
                string key = dependency.Substring(0, split); if (key.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (repo.PlannedEnabled(mod) && Version.TryParse(dependency.Substring(split + 1), out var need)
                    && ConfigurationPackageInventory.Status(repo, mods, inventory, key, need).Length > 0) result.Add(mod);
            }
        }
        // Include actual providers, not only consumers reporting the warning.
        foreach (var consumer in result.ToArray())
        {
            foreach (var dependency in consumer.RuntimeDependencies.Where(d => !d.Optional))
                foreach (var provider in mods.Where(p => p.Guid.Equals(dependency.Id, StringComparison.OrdinalIgnoreCase))) result.Add(provider);
            foreach (string declaration in consumer.PackageDependencies)
            {
                int split = declaration.LastIndexOf('-'); if (split < 1) continue;
                string key = declaration.Substring(0, split); int dash = key.IndexOf('-');
                string name = dash < 0 ? key : key.Substring(dash + 1);
                foreach (var provider in mods.Where(p => PackageMatches(p, key) || p.PackageName.Equals(name, StringComparison.OrdinalIgnoreCase))) result.Add(provider);
            }
        }
        return result.ToList();
    }

    internal static string Build(OverviewRepository repo, IReadOnlyList<OverviewMod> mods, string failure = "", bool formatted = false)
    {
        string T(string en, string cn, string tw) => Loc.T(en, cn, tw);
        var issues = new List<Issue>();
        var inventory = ConfigurationPackageInventory.Get(mods);
        foreach (var match in PackConflictService.Evaluate(repo, mods).Where(c => c.Planned && !c.Ignored))
            issues.Add(new Issue { Category = 0, Name = string.Join(" / ", match.Members.Select(m => m.Name)), Reason = match.Rule.reason,
                Suggestion = T("Keep one of the conflicting features enabled, or save anyway if this combination is intentional.", "建议只启用其中一项冲突功能；确认需要共存时仍可保存。", "建議只啟用其中一項衝突功能；確定需要共存時仍可儲存。") });
        foreach (var copies in OverviewPluginCopies.Duplicates(mods))
            issues.Add(new Issue { Category = 1, Name = string.Join("\n" + T("with", "与", "與") + "\n",
                copies.Select(p => { var owner = mods.FirstOrDefault(m => m.Guid.Equals(p.Guid, StringComparison.OrdinalIgnoreCase) && OverviewPluginCopies.For(m).Any(c => c.Path.Equals(p.Path, StringComparison.OrdinalIgnoreCase)));
                    return owner == null ? Safe(p.Name) + " " + Dim(p.Version + " · " + TailPath(p.Path)) : Caption(owner, p.Version, p.Path); })) });
        foreach (var members in OverviewPluginCopies.SameNameCopies(mods))
            issues.Add(new Issue { Category = 1, Name = string.Join("\n" + T("with", "与", "與") + "\n", members.Select(m => Caption(m, m.PluginVersion, m.Location))),
                Reason = T("Same name but different plugin identities in one folder; both are loaded. Keep one copy.", "同一文件夹内名称相同但插件标识不同，两份都会被加载；建议只保留一份。", "同一資料夾內名稱相同但外掛身分不同，兩份都會被載入；建議只保留一份。") });
        var enabled = mods.Where(repo.PlannedEnabled).ToList();
        foreach (var mod in enabled)
        foreach (var dependency in mod.RuntimeDependencies.Where(d => !d.Optional))
        {
            var installed = mods.Where(m => m.Guid.Equals(dependency.Id, StringComparison.OrdinalIgnoreCase) || m.PluginGuids.Contains(dependency.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            var providers = installed.Where(repo.PlannedEnabled).ToList();
            if (providers.Count > 0 && (dependency.MinimumVersion.Length == 0 || providers.Any(p => Version.TryParse(p.PluginVersion, out var actual) && Version.TryParse(dependency.MinimumVersion, out var minimum) && actual >= minimum))) continue;
            issues.Add(new Issue { Category = 2, Name = Caption(mod) + T(" requires ", " 的依赖模组 ", " 的依賴模組 ") + (installed.Count > 0 ? Caption(installed[0]) : Dim(dependency.Id)) + "  " +
                    (installed.Count == 0 ? T("missing", "缺失", "缺失") : providers.Count == 0 ? T("disabled", "未启用", "未啟用") : T("outdated", "版本过低", "版本過低")),
                Reason = dependency.MinimumVersion.Length > 0 ? Dim(T("Required ≥ ", "要求 ≥ ", "要求 ≥ ") + dependency.MinimumVersion) : "",
                Suggestion = T("Install or enable this dependency, or update it to the listed version. This warning does not block saving other changes.", "建议安装或启用该依赖，版本不足时更新到所列版本；此提醒不阻止保存其他修改。", "建議安裝或啟用該依賴，版本不足時更新至所列版本；此提醒不阻止儲存其他變更。") });
        }
        foreach (var mod in enabled)
        foreach (string declaration in mod.PackageDependencies)
        {
            int split = declaration.LastIndexOf('-');
            if (split < 1 || !Version.TryParse(declaration.Substring(split + 1), out var minimum)) continue;
            string package = declaration.Substring(0, split);
            if (package.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            string status = ConfigurationPackageInventory.Status(repo, mods, inventory, package, minimum);
            if (status.Length == 0) continue;
            string label = status == "unavailable" ? T("Inventory incomplete", "安装清单读取不完整", "安裝清單讀取不完整")
                : status == "unknown-version" ? T("Installed; version unknown", "已安装，版本待确认", "已安裝，版本待確認")
                : status == "missing" ? T("missing", "缺失", "缺失") : status == "disabled" ? T("disabled or cached", "未启用或位于缓存区", "未啟用或位於緩存區") : T("outdated", "版本过低", "版本過低");
            issues.Add(new Issue { Category = 2, Name = Caption(mod) + T(" requires package ", " 的依赖包 ", " 的依賴包 ") + Dim(package) + "  " + label, Reason = Dim("≥ " + minimum),
                Suggestion = T("Check this package's installation, enablement and version. You may retain the current environment and continue saving.", "请核对该安装包是否已安装、启用及版本是否满足要求；可保持当前环境并继续保存。", "請核對該安裝包是否已安裝、啟用及版本是否符合要求；可維持目前環境並繼續儲存。") });
        }
        foreach (var mod in Unidentified(mods))
            issues.Add(new Issue { Category = 5, Name = Caption(mod), Reason = "" });
        foreach (var item in inventory.Unresolved.Where(e => !mods.Any(m => m.PackageName.Equals(e.Name, StringComparison.OrdinalIgnoreCase))))
            issues.Add(new Issue { Category = 5, Name = Safe(item.Name) + " " + Dim(item.Version), Reason = "" });
        foreach (var mod in mods.Where(m => repo.PlannedEnabled(m) != m.TargetEnabled && (!m.CanToggle || m.IdentityConflict.Length > 0)))
            issues.Add(new Issue { Category = 4, Name = mod.Name,
                Reason = Explain(mod.IdentityConflict.Length > 0 ? mod.IdentityConflict : mod.ToggleReason),
                Suggestion = T("Review the selected file paths. This enablement target will remain unsaved; other valid changes can be saved.", "请查看该模组的文件位置并核对本次目标；无法执行的启停留在草稿，其他有效修改继续保存。", "請查看該模組檔案位置並核對目標；無法執行的啟停保留於草稿，其他有效變更繼續儲存。") });
        if (failure.Length > 0)
        {
            bool safety = failure.IndexOf("路径", StringComparison.Ordinal) >= 0 || failure.IndexOf("覆盖", StringComparison.Ordinal) >= 0 || failure.IndexOf("MCE required", StringComparison.OrdinalIgnoreCase) >= 0;
            string reason = Explain(failure);
            if (reason.IndexOf("Multiple enabled plugin identities", StringComparison.OrdinalIgnoreCase) >= 0)
                reason = T("Multiple installed plugin versions were detected. Their presence alone is not a save blocker.", "检测到多个插件版本；仅存在重复版本不应阻止保存。", "偵測到多個外掛版本；僅存在重複版本不應阻止儲存。");
            if (reason.IndexOf("Sharing violation", StringComparison.OrdinalIgnoreCase) >= 0)
                reason = T("The file is busy. Another write operation currently owns the installation lock.", "文件正被占用：另一个写入任务正在处理安装目录。", "檔案正被占用：另一個寫入工作正在處理安裝目錄。");
            issues.Add(new Issue { Category = safety ? 4 : 3, Name = T("This save", "本次保存", "本次儲存"), Reason = reason,
                Suggestion = safety ? T("Review the affected paths or required manager components. Keep unsafe file operations unchanged and save the other settings.", "请核对相关文件路径或管理器必需组件；保留有风险的文件操作，先保存其他设置。", "請核對相關檔案路徑或管理器必需元件；保留有風險的檔案操作，先儲存其他設定。")
                : T("Keep the draft, resolve the listed issue and retry. You may skip enablement and save other settings first.", "草稿已保留。按上述原因处理后重试，也可跳过启停，先保存其他设置。", "草稿已保留。處理上述問題後重試，也可略過啟停，先儲存其他設定。") });
        }
        string[] headings = { T("Compatibility advice", "模组兼容建议", "模組相容建議"), T("Duplicate mods", "重复模组", "重複模組"), T("Dependency issues", "依赖检查", "依賴檢查"), T("Save execution results", "保存执行结果", "儲存執行結果"), T("File operations needing review", "需要核对的文件操作", "需要核對的檔案操作"), T("Unidentified sources", "来源未识别", "來源未識別") };
        var result = new StringBuilder();
        foreach (var group in issues.GroupBy(i => i.Category).OrderBy(g => g.Key))
        {
            if (result.Length > 0) result.AppendLine();
            string hint = group.Key == 5 ? T("Automatic update checks are unavailable until the source is identified manually", "在手动识别之前，该模组将无法自动检测更新", "在手動識別之前，該模組將無法自動檢測更新")
                : group.Key == 1 ? T("Coexistence allowed; tidy old versions only if needed", "可共存，按需整理旧版", "可共存，按需整理舊版")
                : group.Key == 2 ? T("Check installation, enablement and required version", "核对安装、启用状态及要求版本", "核對安裝、啟用狀態與要求版本")
                : group.Key == 0 ? T("Review compatibility; keep your choice if intentional", "核对兼容性，可保持当前选择", "核對相容性，可維持目前選擇")
                : T("Retain unresolved edits as drafts and retry after correction", "未完成项保留草稿，处理后重试", "未完成項保留草稿，處理後重試");
            result.AppendLine("<size=115%><b>" + headings[group.Key] + "</b></size> " + Dim("（" + hint + "）"));
            result.AppendLine(); int number = 0;
            foreach (var issue in group.GroupBy(i => i.Name + "\n" + i.Reason).Select(g => g.First()))
            {
                result.AppendLine(++number + (issue.Category == 1 ? ".\n" : ". ") + (issue.Category == 1 || issue.Category == 2 || issue.Category == 5 ? issue.Name : Safe(issue.Name)));
                if (issue.Reason.Length > 0) result.AppendLine("   " + (issue.Category == 2 ? issue.Reason : Safe(issue.Reason)).Replace("\n", "\n   "));
                result.AppendLine();
            }
        }
        string output = result.ToString();
        return formatted ? output : System.Text.RegularExpressions.Regex.Replace(output, "</?(?:size|color|b)(?:=[^>]*)?>", "");
    }
}
