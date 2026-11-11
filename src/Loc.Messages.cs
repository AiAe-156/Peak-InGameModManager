#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ModConfigEnhance.Updating;

namespace ModConfigEnhance;

internal static partial class Loc
{
    // A catalogue is available without creating or requiring any language file.
    internal static IEnumerable<Term> AllInlineTerms => InlineTerms.Concat(AdditionalTerms).Concat(UpdaterTerms).Concat(Release163Terms).Concat(Release165Terms).Concat(Release166Terms).Concat(Release167Terms).Concat(Release168Terms).Concat(Release169Terms).Concat(Release1610Terms).Concat(Release20UiTerms).Concat(ConflictPresetEditorTerms).Concat(ConflictPresetTerms).Concat(Release23Terms)
        .Concat(UpdateMessages.Terms.Select(t => new Term(t.English, t.English, t.SimplifiedChinese, t.TraditionalChinese)));
    internal static IEnumerable<Term> AllConfigTerms => ConfigTerms.Concat(AdditionalConfigTerms);

    internal static string F(string english, string simplifiedChinese, string traditionalChinese, params object[] args)
    {
        try
        {
            string translated = T(english, simplifiedChinese, traditionalChinese);
            if (!FormatArguments(english).SetEquals(FormatArguments(translated))) throw new FormatException("Translation placeholders differ from the original.");
            return string.Format(CultureInfo.CurrentCulture, translated, args);
        }
        catch (FormatException)
        {
            try { return string.Format(CultureInfo.CurrentCulture, english, args); }
            catch (FormatException) { return english; }
        }
    }

    private static readonly Regex FormatArgument = new(@"(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})", RegexOptions.Compiled);
    private static HashSet<string> FormatArguments(string text) => new(FormatArgument.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.Ordinal);

    // Apply aliases to each file before merging layers. An explicit canonical key in the
    // same file wins regardless of line order; a personal legacy key still overrides author data.
    private static void ApplyCompatibilityAliases(LanguageFile file)
    {
        foreach (var alias in CompatibilityAliases)
            if (!file.Entries.ContainsKey(alias.Value) && file.Entries.TryGetValue(alias.Key, out string? text))
                file.Entries[alias.Value] = text;
        foreach (Term term in Terms)
        {
            string english = term.English.Trim();
            if (file.Entries.TryGetValue(term.Key, out string? text)) file.Entries[english] = text;
            else if (file.Entries.TryGetValue(english, out text)) file.Entries[term.Key] = text;
        }
    }

    internal static string EnglishDisplay(string original)
    {
        string normalized = original.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return CompatibilityAliases.TryGetValue(normalized, out string? canonical) ? canonical : normalized;
    }

    // .NET 运行时的 HTTP 异常是英文资源串（worker 与插件侧同此原文）；按状态码换可读短语，保留前后文。
    private static readonly Regex[] HttpStatusErrors =
    {
        new(@"Response status code does not indicate success:\s*(\d+)\s*(\([^)]*\))?\.?", RegexOptions.Compiled),
        new(@"The remote server returned an error:\s*\((\d+)\)[^.]*\.?", RegexOptions.Compiled),
    };

    internal static string LocalizeMessage(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (text.Contains('；')) return string.Join("；", text.Split('；').Select(LocalizeMessage));
        // Only explicitly catalogued updater prefixes may consume a detail suffix.
        var prefix = UpdaterTerms.Where(t => (t.English.EndsWith(": ", StringComparison.Ordinal) || t.English.EndsWith(":\n", StringComparison.Ordinal))
            && text.StartsWith(t.English, StringComparison.Ordinal)).OrderByDescending(t => t.English.Length).FirstOrDefault();
        if (!string.IsNullOrEmpty(prefix.English)) return T(prefix.English, prefix.SimplifiedChinese, prefix.TraditionalChinese) + LocalizeMessage(text.Substring(prefix.English.Length));
        if (text.Contains('\n')) return string.Join("\n", text.Split('\n').Select(LocalizeMessage));
        if (UpdateMessages.TryDecode(UpdateMessages.Encode(text), out var workerTerm, out string argument))
            return T(workerTerm!.English, workerTerm.SimplifiedChinese, workerTerm.TraditionalChinese)
                + (argument.Length == 0 ? "" : LocalizeMessage(argument));
        foreach (Regex pattern in HttpStatusErrors)
        {
            Match match = pattern.Match(text);
            if (!match.Success) continue;
            string code = match.Groups[1].Value;
            string phrase = code == "404"
                ? F("The store could not find this mod or the source is wrong (HTTP {0}).", "商店找不到该模组或来源有误（HTTP {0}）。", "商店找不到此模組或來源有誤（HTTP {0}）。", code)
                : F("The store request failed (HTTP {0}).", "商店请求失败（HTTP {0}）。", "商店請求失敗（HTTP {0}）。", code);
            return pattern.Replace(text, _ => phrase);
        }
        string english = EnglishDisplay(text);
        if (_inlineSnapshot.TryGetValue(english, out string? translated)) return translated;
        foreach (Term term in Terms.Concat(AllConfigTerms).Concat(AllInlineTerms))
            if (term.English.Trim() == english || term.Key.Trim() == english || term.SimplifiedChinese == text || term.TraditionalChinese == text)
                return T(term.English, term.SimplifiedChinese, term.TraditionalChinese);
        return text;
    }

    private static string ActiveLanguageCode => UiLanguage == UiLang.ZhCn ? "zh-cn" : UiLanguage == UiLang.ZhTw ? "zh-tw" : "en-us";

    internal static bool TryConfigText(string original, out string translated)
    {
        string canonical = EnglishDisplay(original);
        foreach (Term term in AllConfigTerms)
            if (term.Key.Trim() == original.Trim() || term.English.Trim() == canonical || term.SimplifiedChinese == original || term.TraditionalChinese == original)
            {
                translated = T(term.English, term.SimplifiedChinese, term.TraditionalChinese);
                return true;
            }
        translated = original;
        return false;
    }
}
