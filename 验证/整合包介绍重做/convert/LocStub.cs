// Test stub: the converters only use Loc for error messages; return the simplified-Chinese text.
namespace ModConfigEnhance;
internal static class Loc
{
    internal static string T(string en, string zh, string tw) => zh;
    internal static string F(string en, string zh, string tw, params object[] args) => string.Format(zh, args);
}
