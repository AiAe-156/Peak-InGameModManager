#nullable enable
using System.Text;

namespace ModConfigEnhance;

/// <summary>语言文件一行「原文=译文」的转义（读写、导出、导入补翻共用）。</summary>
internal static partial class Loc
{
    internal static int FindUnescapedEquals(string line)
    {
        // 2.5.4：按转义状态扫描——反斜杠转义下一个字符，所以「反斜杠反斜杠=」里的 = 是分隔符
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == '=') return i;
        }

        return -1;
    }

    /// <summary>
    /// 2.5.4（二次审查）：反斜杠只在会引起歧义时写成两个——后面紧跟 =、n、反斜杠，或在末尾。
    /// 原先不转义反斜杠，原文以反斜杠结尾时写出「xxx\=译文」，整行找不到分隔符，整个语言组加载失败。
    /// 普通路径（config\ModConfigEnhance）照旧一个反斜杠，导出文件好读。
    /// </summary>
    internal static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r') continue;
            if (c == '\n') { sb.Append("\\n"); continue; }
            if (c == '=') { sb.Append("\\="); continue; }
            if (c == '\\')
            {
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                sb.Append(next == '\0' || next == '=' || next == 'n' || next == '\\' || next == '\n' ? "\\\\" : "\\");
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>与 <see cref="Escape"/> 对应：\= → =，\n → 换行，两个反斜杠 → 一个；其他反斜杠组合原样保留（旧文件里的路径不受影响）。</summary>
    internal static string Unescape(string text)
    {
        if (text.IndexOf('\\') < 0) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\\' || i + 1 >= text.Length) { sb.Append(c); continue; }
            char n = text[i + 1];
            if (n == '=' || n == '\\') { sb.Append(n); i++; }
            else if (n == 'n') { sb.Append('\n'); i++; }
            else sb.Append(c);
        }

        return sb.ToString();
    }
}
