#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ModConfigEnhance;

/// <summary>
/// 2.5.0：语言目录收成「一种语言一个文件」。
/// · <see cref="ImportAdditions"/>：把填好的导出文件里有译文的行合进当前语言文件 <c>language\&lt;组名&gt;.txt</c>
///   （同一句原文以导入的为准，留空的行跳过），合并前先备份。取代以前「另存成 zh-cn_补翻_日期.txt」的分包做法。
/// · <see cref="LanguageMigration.Run"/>：首次启动 2.5.0 时，把每个语言组的多个分包按现在的叠加规则合成一个文件，
///   原文件整体移进 <c>language\_迁移前备份_日期\</c>；旧 <c>export\</c> 改名 <c>translation-export\</c>；
///   本模组以前写进 XUnity 词典目录的镜像 <c>ModConfigEnhance_*.txt</c> 移进同一个备份。
/// </summary>
internal static partial class Loc
{
    internal sealed class ImportResult
    {
        internal int Added, Replaced, Empty, Unchanged;
        internal string Target = "";
        internal int Total => Added + Replaced;
    }

    /// <summary>当前语言组在个人目录里的文件：选了语言用组名，跟随游戏用游戏语言码。</summary>
    internal static string PersonalLanguageFile()
    {
        string selection = Plugin.LanguageFile.Value ?? "";
        string stem = string.IsNullOrEmpty(selection) ? FollowStem() : GroupStemFrom(NormalizeSelection(selection));
        if (stem.Length == 0 || stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) stem = FollowStem();
        return Path.Combine(LanguageDirectory, stem + ".txt");
    }

    /// <summary>读一个导出 / 语言文件里的「原文=译文」行（含 r: 规则）；留空的行计数后跳过。保持文件里的先后。</summary>
    internal static List<(string Key, string Value)> ReadPairs(string path, out int empty)
    {
        empty = 0;
        var result = new List<(string, string)>();
        foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) continue;
            int eq = FindUnescapedEquals(line);
            if (eq <= 0) continue;
            string key = Unescape(line.Substring(0, eq)).Trim();
            string value = Unescape(line.Substring(eq + 1));
            if (key.Length == 0) continue;
            if (string.IsNullOrWhiteSpace(value)) { empty++; continue; }
            result.Add((key, value));
        }

        return result;
    }

    internal static ImportResult ImportAdditions(string source)
    {
        var pairs = ReadPairs(source, out int empty);
        var result = new ImportResult { Empty = empty, Target = PersonalLanguageFile() };
        if (pairs.Count == 0) return result;
        Directory.CreateDirectory(LanguageDirectory);
        string target = result.Target;
        var lines = File.Exists(target) ? File.ReadAllLines(target, Encoding.UTF8).ToList() : new List<string> { "// display: " + DisplayFor(Path.GetFileNameWithoutExtension(target)) };
        // 原文 → 所在行号（同一原文多行时取最后一行，与读取时「后写的优先」一致）
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) continue;
            int eq = FindUnescapedEquals(line);
            if (eq > 0) index[Unescape(line.Substring(0, eq)).Trim()] = i;
        }

        var appended = new List<string>();
        foreach (var (key, value) in pairs)
        {
            // 2.5.4（二次审查）：完整导出时等号右边预填了英文，用户没翻的行原样留着——这类「译文等于原文 / 等于内置英文」的行不导入，
            // 否则会把内置中文盖成英文。
            if (IsPlaceholder(key, value)) { result.Unchanged++; continue; }
            string text = Escape(key) + "=" + Escape(value);
            if (index.TryGetValue(key, out int at))
            {
                if (lines[at] != text) { lines[at] = text; result.Replaced++; }
                continue;
            }

            index[key] = -1;
            appended.Add(text);
            result.Added++;
        }

        if (result.Total == 0) return result;
        if (appended.Count > 0)
        {
            lines.Add("");
            lines.Add("// ---- " + F("Imported from {0} on {1}", "导入自 {0}，{1}", "匯入自 {0}，{1}", Path.GetFileName(source), DateTime.Now.ToString("yyyy-MM-dd HH:mm")) + " ----");
            lines.AddRange(appended);
        }

        if (File.Exists(target)) File.Copy(target, target + ".mce-" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak", true);
        string temporary = target + ".mce.tmp";
        File.WriteAllText(temporary, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        File.Copy(temporary, target, true);
        File.Delete(temporary);
        InvalidateEditingTranslations();
        return result;
    }

    /// <summary>导入补翻里「没翻」的行：译文与原文相同；或 MCE_ 键的译文与内置英文相同。</summary>
    private static bool IsPlaceholder(string key, string value)
    {
        if (string.Equals(key.Trim(), value.Trim(), StringComparison.Ordinal)) return true;
        if (!TryStripTermPrefix(key, out string id)) return false;
        foreach (Term term in Terms)
            if (term.Key == id) return string.Equals(term.English, value, StringComparison.Ordinal);
        return false;
    }

    internal static string DisplayFor(string stem)
    {
        string code = LanguageCodeFromGroup(stem);
        return code == "zh-cn" ? "简体中文" : code == "zh-tw" ? "繁體中文" : code == "en-us" ? "English" : stem;
    }
}

internal static class LanguageMigration
{
    private const string Marker = ".mce-language-v2";

    internal static void Run()
    {
        string root = Loc.RootDirectory, language = Loc.LanguageDirectory;
        if (File.Exists(Path.Combine(language, Marker))) { DropRetired(language); return; }
        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backup = Path.Combine(language, "_" + Loc.T("pre-migration-backup", "迁移前备份", "遷移前備份") + "_" + stamp);
            int merged = 0, moved = 0;
            if (Directory.Exists(language))
            {
                // 只看个人目录里的组；同组多于一个文件，或基础文件是旧版自动写出的界面词条快照，才需要动
                var groups = Directory.GetFiles(language, "*.txt")
                    .Where(p => !Path.GetFileName(p).StartsWith("_", StringComparison.Ordinal))
                    .GroupBy(p => Loc.NormalizeGroupKey(Loc.GroupStemFrom(Path.GetFileNameWithoutExtension(p))), StringComparer.OrdinalIgnoreCase);
                foreach (var group in groups)
                {
                    var files = group.OrderBy(p => Path.GetFileNameWithoutExtension(p).IndexOf('_') < 0 ? 0 : 1).ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
                    string stem = Loc.GroupStemFrom(Path.GetFileNameWithoutExtension(files[0]));
                    bool snapshotOnly = files.Count == 1 && IsBuiltInSnapshot(files[0]);
                    if (files.Count == 1 && !snapshotOnly) continue;
                    Directory.CreateDirectory(backup);
                    var content = Merge(files, stem, out int kept);
                    // 2.5.4（二次审查）：先写好合并结果（临时文件），再把原文件挪进备份，最后换上——写失败时原文件都还在原处
                    string target = Path.Combine(language, stem + ".txt"), staged = target + ".mce-merge.tmp";
                    if (kept > 0) File.WriteAllText(staged, content, new UTF8Encoding(false));
                    foreach (string file in files) { File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), true); }
                    // 复查：先把合并结果换上（目标是同组基础文件，覆盖它），再删其余分包。中途被占用时已删的分包内容都在合并结果里，
                    // 不会只剩在备份目录；下次重跑时合并结果作为基础文件参与，不丢译文。
                    if (kept > 0) { File.Copy(staged, target, true); File.Delete(staged); merged++; }
                    foreach (string file in files.Where(f => !f.Equals(target, StringComparison.OrdinalIgnoreCase) || kept == 0)) { File.Delete(file); moved++; }
                }
            }

            // 旧导出目录 export\ 由 2.5.2 的 MceDirectoryMigration 统一搬到 output\translation\

            // 以前写给 XUnity 的镜像词典（本模组生成的，文件头有标记）
            foreach (string translationRoot in new[] { Path.Combine(BepInEx.Paths.ConfigPath, "Translation"), Path.Combine(BepInEx.Paths.BepInExRootPath, "Translation") })
            {
                if (!Directory.Exists(translationRoot)) continue;
                foreach (string mirror in Directory.GetFiles(translationRoot, "ModConfigEnhance_*.txt", SearchOption.AllDirectories))
                {
                    string first = File.ReadLines(mirror, Encoding.UTF8).FirstOrDefault() ?? "";
                    if (!first.StartsWith("// Generated by ModConfig Enhance", StringComparison.Ordinal)) continue;
                    string into = Path.Combine(backup, "XUnity-Translation", Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(mirror)!)!) ?? "");
                    Directory.CreateDirectory(into);
                    File.Move(mirror, Path.Combine(into, Path.GetFileName(mirror)));
                    moved++;
                }
            }

            Directory.CreateDirectory(language);
            File.WriteAllText(Path.Combine(language, Marker), "2.5.0 " + stamp + "\n", new UTF8Encoding(false));
            if (moved > 0) Plugin.Log.LogInfo($"[语言] 2.5.0 语言目录整理：合并 {merged} 个语言文件，原文件 {moved} 个移进 {backup}");
        }
        catch (Exception ex)
        {
            // 不写标记：下次启动再试；已移动的文件都在备份目录里
            Plugin.Log.LogWarning("[语言] 2.5.0 语言目录整理未完成，下次启动重试：" + ex.Message);
        }
    }

    private const string RetiredMarker = ".mce-language-v3";
    /// <summary>退役清单代次：<see cref="Loc.RetiredTerms"/> 或 <c>RetiredOriginal</c> 增加条目时加一。</summary>
    private const string RetiredGeneration = "g3";

    /// <summary>
    /// 2.5.1：个人语言文件里删掉含义已改的旧界面词条（<see cref="Loc.RetiredTerms"/>）和提到 XUnity 联动的旧 cfg 说明原文——
    /// 读取时本来就跳过，这里把文件也清干净，免得用户以为还在用。改前整份备份进 <c>_迁移前备份_时间\</c>，只做一次。
    /// </summary>
    private static void DropRetired(string language)
    {
        // 复查：标记里记退役清单的代次，清单增加（2.5.4 的 NOTICE_IMPORTED、2.5.5 的 TIP_OPEN_DIR_V2）后已升级的用户再清一次
        string marker = Path.Combine(language, RetiredMarker);
        if (File.Exists(marker) && File.ReadAllText(marker).StartsWith(RetiredGeneration + " ", StringComparison.Ordinal)) return;
        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backup = Path.Combine(language, "_" + Loc.T("pre-migration-backup", "迁移前备份", "遷移前備份") + "_" + stamp);
            int removed = 0;
            foreach (string file in Directory.GetFiles(language, "*.txt").Where(p => !Path.GetFileName(p).StartsWith("_", StringComparison.Ordinal)))
            {
                var lines = File.ReadAllLines(file, Encoding.UTF8).ToList();
                int before = lines.Count;
                lines.RemoveAll(line =>
                {
                    if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal)) return false;
                    int eq = Loc.FindUnescapedEquals(line);
                    if (eq <= 0) return false;
                    string key = Loc.Unescape(line.Substring(0, eq)).Trim();
                    bool termKey = Loc.TryStripTermPrefix(key, out string termId);
                    if (termKey) key = termId;
                    // 2.5.4（二次审查）：只删 MCE 自己的退役词条与旧版 MCE 的 cfg 原文（精确清单），不按「含 XUnity」模糊删，
                    // 免得误删新提醒文字、别的模组提到 XUnity 的选项、或用户给 XUnity 起的译名。
                    return termKey ? Loc.RetiredTerm(key) : RetiredOriginal(key);
                });
                if (lines.Count == before) continue;
                Directory.CreateDirectory(backup);
                File.Copy(file, Path.Combine(backup, Path.GetFileName(file)), true);
                File.WriteAllText(file, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
                removed += before - lines.Count;
            }

            File.WriteAllText(marker, RetiredGeneration + " " + stamp + "\n", new UTF8Encoding(false));
            if (removed > 0) Plugin.Log.LogInfo($"[语言] 2.5.1 清掉个人语言文件里 {removed} 行过时词条（XUnity 旧说法），原文件备份在 {backup}");
        }
        catch (Exception ex) { Plugin.Log.LogWarning("[语言] 清理过时词条未完成，下次启动重试：" + ex.Message); }
    }

    /// <summary>旧版 MCE 自己的 cfg 选项名与说明原文（2.4.x，提到 XUnity 联动），屏幕上已不再出现。</summary>
    private static bool RetiredOriginal(string key) => Loc.RetiredTerms.Contains(key)
        || key.StartsWith("Picking a language file also switches XUnity", StringComparison.Ordinal)
        || key.StartsWith("When a language file is picked (or the game language changes while following it), switch XUnity", StringComparison.Ordinal)
        || key.StartsWith("ON by default. Completely blocks XUnity machine-translation", StringComparison.Ordinal)
        || key.StartsWith("ON by default. While XUnity AutoTranslator is installed, every startup empties", StringComparison.Ordinal)
        || key.StartsWith("OFF by default. When on, every startup empties Endpoint", StringComparison.Ordinal)
        || key.StartsWith("While XUnity is installed, empty Endpoint", StringComparison.Ordinal)
        || key.StartsWith("Above the mod list, hide the export and XUnity button rows", StringComparison.Ordinal)
        || key.StartsWith("Toolbar rows in the left column: Export texts / Untranslated / Selected only", StringComparison.Ordinal);

    /// <summary>旧版首次运行写出的界面词条快照：首两行是「// display:」与「built-in language file」。</summary>
    private static bool IsBuiltInSnapshot(string path)
    {
        var head = File.ReadLines(path, Encoding.UTF8).Take(3).ToList();
        return head.Any(l => l.Contains("built-in language file"));
    }

    /// <summary>
    /// 按读取时的叠加规则合并（后读的文件覆盖同一原文，留空不覆盖）；界面词条与内置相同的行丢掉（以后内置更新就能跟上），改过的保留。
    /// r: 规则按原顺序保留。
    /// </summary>
    private static string Merge(List<string> files, string stem, out int kept)
    {
        string code = Loc.LanguageCodeFromGroup(stem);
        string? display = null;
        var order = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            foreach (string raw in File.ReadAllLines(file, Encoding.UTF8))
            {
                string line = raw.TrimEnd('\r');
                if (display == null && line.StartsWith("// display:", StringComparison.Ordinal)) display = line.Substring("// display:".Length).Trim();
            }

            foreach (var (key, value) in Loc.ReadPairs(file, out _))
            {
                if (!values.ContainsKey(key)) order.Add(key);
                values[key] = value;
            }
        }

        var sb = new StringBuilder();
        sb.Append("// display: ").Append(display ?? Loc.DisplayFor(stem)).Append('\n');
        sb.Append("// ").Append(Loc.F("Merged by In-Game Mod Manager 2.5.0 from: {0}", "游戏内模组管理器 2.5.0 合并自：{0}", "遊戲內模組管理器 2.5.0 合併自：{0}", string.Join(", ", files.Select(Path.GetFileName)))).Append('\n');
        sb.Append("// ").Append(Loc.T("Originals are unchanged; for the same original the later file won, as before.", "原文（= 左边）逐字未变；同一原文按原来的规则取后读文件的译文。", "原文（= 左邊）逐字未變；同一原文按原來的規則取後讀檔案的譯文。")).Append('\n').Append('\n');
        kept = 0;
        foreach (string key in order)
        {
            string value = values[key];
            if (Loc.TryStripTermPrefix(key, out string termId) && Loc.SameAsBuiltIn(termId, value, code)) continue;
            if (!Loc.HasTermPrefix(key) && !Loc.IsRegexKey(key) && Loc.SameAsBuiltInConfig(key, value, code)) continue;
            sb.Append(Loc.Escape(key)).Append('=').Append(Loc.Escape(value)).Append('\n');
            kept++;
        }

        return sb.ToString();
    }
}

internal static partial class Loc
{
    /// <summary>迁移用：这条界面词条和内置的同语言文字一样（丢掉它，以后内置更新就能跟上）。</summary>
    internal static bool SameAsBuiltIn(string key, string value, string code)
    {
        foreach (Term term in Terms)
            if (term.Key == key) return string.Equals(PickTerm(term, code), value, StringComparison.Ordinal);
        return false;
    }

    internal static bool SameAsBuiltInConfig(string original, string value, string code)
    {
        foreach (Term term in AllConfigTerms)
            if (term.Key.Trim() == original.Trim() || term.English.Trim() == original.Trim())
                return string.Equals(PickTerm(term, code), value, StringComparison.Ordinal);
        return false;
    }
}
