#nullable enable
// Loc.Regex.cs 依赖的 Loc 成员桩（真实实现依赖 Unity / BepInEx）。
using System.Collections.Generic;

namespace ModConfigEnhance
{
    internal static partial class Loc
    {
        internal sealed class LanguageFile { internal readonly List<RegexRule> Regexes = new(); }
        internal static LanguageFile? SelectedLanguageGroup => null;
        internal static string T(string en, string zhCn, string zhTw) => en;
        internal static string F(string en, string zhCn, string zhTw, params object[] args) => string.Format(en, args);
    }

    internal static class Plugin
    {
        internal static class Log { internal static void LogWarning(string text) => System.Console.WriteLine("  [warn] " + text); }
    }
}
