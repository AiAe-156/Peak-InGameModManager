#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ModConfigEnhance;

/// <summary>
/// 2.5.0：语言文件支持 XUnity 同款正则行 <c>r:"正则"=译文</c>（可用 <c>$1</c>、<c>${name}</c> 引用捕获组），
/// 方便直接从 XUnity 词典搬规则。解析与 XUnity 5.6.1 <c>RegexTranslation</c> 一致：去掉 <c>r:</c>，
/// 两侧引号内为正文（没有引号则整段）；译文也可以写成 <c>r:"…"</c>。匹配顺序：整句精确优先，
/// 都没命中再按文件里的先后试正则，**后写的优先**（与 XUnity 一致，补翻文件里新加的规则能盖住旧的）。
/// 正则只在显示时用：导出、补漏翻按原文逐句判断，命中正则的句子也算已翻。
/// </summary>
internal static partial class Loc
{
    internal const string RegexPrefix = "r:";

    internal sealed class RegexRule
    {
        internal Regex Pattern = null!;
        internal string Replacement = "";
        internal string Source = "";
        internal int Line;
    }

    internal static bool IsRegexKey(string key) => key.StartsWith(RegexPrefix, StringComparison.Ordinal);

    /// <summary>与 XUnity 相同：去 <c>r:</c> 前缀，有引号取首尾引号之间。引号不成对时报错。</summary>
    internal static string StripRegexQuotes(string text, string what)
    {
        if (text.StartsWith(RegexPrefix, StringComparison.Ordinal)) text = text.Substring(RegexPrefix.Length);
        int first = text.IndexOf('"');
        if (first < 0) return text;
        int last = text.LastIndexOf('"');
        if (last == first) throw new FormatException(F("The {0} starts with \" but does not end with \".", "{0}以 \" 开头却没有以 \" 结尾。", "{0}以 \" 開頭卻沒有以 \" 結尾。", what));
        return text.Substring(first + 1, last - first - 1);
    }

    /// <summary>编译一条正则行；格式或正则写错返回 null 并给出原因（读文件时记成坏行，提示行号）。</summary>
    internal static RegexRule? TryCompileRegex(string key, string value, string source, int line, out string error)
    {
        error = "";
        try
        {
            string pattern = StripRegexQuotes(key, T("pattern", "正则", "正則"));
            string replacement = StripRegexQuotes(value, T("translation", "译文", "譯文"));
            if (pattern.Length == 0) { error = T("The pattern is empty.", "正则为空。", "正則為空。"); return null; }
            // 不加 Compiled：Mono 下编译正则慢且不省多少；超时防止写坏的规则卡住界面。
            return new RegexRule { Pattern = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(50)), Replacement = replacement, Source = source, Line = line };
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>按当前语言组的正则表翻一句；没命中返回 false。规则后写的优先。</summary>
    internal static bool TryRegex(string text, out string translated)
    {
        translated = text;
        LanguageFile? group = SelectedLanguageGroup;
        if (group == null || group.Regexes.Count == 0 || string.IsNullOrEmpty(text)) return false;
        return TryRegex(group.Regexes, text, out translated);
    }

    internal static bool TryRegex(List<RegexRule> rules, string text, out string translated)
    {
        translated = text;
        for (int i = rules.Count - 1; i >= 0; i--)
        {
            RegexRule rule = rules[i];
            try
            {
                // 2.5.4（二次审查）：与 XUnity 5.6.1 TextTranslationCache 一致：取第一个匹配，用 Match.Result(译文) 作整句结果，命中即停。
                Match match = rule.Pattern.Match(text);
                if (!match.Success) continue;
                translated = match.Result(rule.Replacement);
                return true;
            }
            catch (RegexMatchTimeoutException)
            {
                Plugin.Log.LogWarning($"[语言] 正则匹配超时，本次跳过：{rule.Source} 第 {rule.Line} 行");
            }
        }

        return false;
    }

    /// <summary>
    /// XUnity 词典的转义（TextHelper.ReadTranslationLineAndDecode）：反斜杠后跟 = 或反斜杠 → 该字符本身；n、r → 换行、回车；
    /// uXXXX → 对应字符；其他反斜杠组合原样保留（正则里的 \d、\s 照常）；%3D → =。只用于 r: 行，普通行仍走 Unescape。
    /// </summary>
    internal static string UnescapeXUnity(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '%' && i + 2 < text.Length && text[i + 1] == '3' && (text[i + 2] == 'D' || text[i + 2] == 'd')) { sb.Append('='); i += 2; continue; }
            if (c != '\\' || i + 1 >= text.Length) { sb.Append(c); continue; }
            char n = text[++i];
            if (n == '=' || n == '\\') sb.Append(n);
            else if (n == 'n') sb.Append('\n');
            else if (n == 'r') sb.Append('\r');
            else if (n == 'u' && i + 4 < text.Length && int.TryParse(text.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out int code)) { sb.Append((char)code); i += 4; }
            else sb.Append('\\').Append(n);
        }

        return sb.ToString();
    }
}
