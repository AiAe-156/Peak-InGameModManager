#nullable enable
using System.Collections.Generic;

internal static class Stubs
{
    internal static string Root = "", BepInEx = "";
    internal static readonly List<string> Lines = new();
}

namespace BepInEx
{
    internal static class Paths { internal static string BepInExRootPath => Stubs.BepInEx; }
}

namespace ModConfigEnhance
{
    internal static class Loc { internal static string RootDirectory => Stubs.Root; internal static string T(string en, string zhCn, string zhTw) => en; }
    internal static class Plugin
    {
        internal static class Log
        {
            internal static void LogInfo(string text) => Stubs.Lines.Add(text);
            internal static void LogError(string text) => Stubs.Lines.Add("ERROR " + text);
            internal static void LogWarning(string text) => Stubs.Lines.Add("WARN " + text);
        }
    }
}
