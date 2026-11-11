#nullable enable
using System;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace ModConfigEnhance.Diagnostics;

/// <summary>
/// Two layers of manager logging. Normal lines (update reports, mod enablement, config saves, submissions)
/// always go to the BepInEx log and to our own in-game log source; the technical layer (worker commands,
/// poll detail, plan files) only exists when the Diagnostics config switch is on, which also passes --debug
/// to the update worker. Never throws: logging must not break an operation.
/// </summary>
internal static class ManagerLog
{
    internal const string SourceId = "manager";
    /// <summary>Category shown in the log window's source/name column for our records.</summary>
    internal const string CategoryLabel = "In-Game Mod Manager";
    internal const string SourceName = "In-Game Mod Manager / 游戏内模组管理器";

    private static readonly object Gate = new();
    private static ManualLogSource? _sink;
    private static MceLogChannel? _channel;
    private static ConfigEntry<bool>? _switch;

    internal static bool DebugEnabled => _switch?.Value == true;

    internal static void Initialize(ManualLogSource sink, ConfigEntry<bool> debugSwitch)
    {
        lock (Gate)
        {
            _sink = sink;
            _switch = debugSwitch;
            if (_channel == null)
            {
                try { _channel = MceLogApi.CreateChannel(Plugin.PluginGuid, SourceId, SourceName, 4000); }
                catch (Exception error) { sink.LogWarning("[MCE] log source unavailable: " + error.Message); }
            }
        }
        Info($"日志接入完成 · 普通层：开 · 技术层(debug)：{(DebugEnabled ? "开" : "关")} · 日志来源：{SourceName}");
    }

    internal static void Info(string message) => Write(MceLogLevel.Info, message, "", false);
    internal static void Warn(string message) => Write(MceLogLevel.Warning, message, "", false);
    internal static void Error(string message) => Write(MceLogLevel.Error, message, "", false);
    internal static void Error(string message, Exception error) => Write(MceLogLevel.Error, message + " :: " + error.GetType().Name + ": " + error.Message, error.ToString(), false);
    internal static void Debug(string message) { if (DebugEnabled) Write(MceLogLevel.Debug, message, "", true); }

    /// <summary>Technical detail that must be readable even with the switch off: goes to our source only.</summary>
    internal static void Detail(string message) => Write(MceLogLevel.Debug, message, "", true);

    private static void Write(MceLogLevel level, string message, string stack, bool sourceOnly)
    {
        ManualLogSource? sink; MceLogChannel? channel;
        lock (Gate) { sink = _sink; channel = _channel; }
        if (!sourceOnly && sink != null)
        {
            try
            {
                switch (level)
                {
                    case MceLogLevel.Warning: sink.LogWarning("[MCE] " + message); break;
                    case MceLogLevel.Error:
                    case MceLogLevel.Fatal: sink.LogError("[MCE] " + message); break;
                    default: sink.LogInfo("[MCE] " + message); break;
                }
            }
            catch (Exception) { /* a closed logger must never break the caller */ }
        }
        try
        {
            channel?.Write(new MceLogRecord { Level = level, Category = CategoryLabel, SubjectName = "IGMM", Message = message, StackTrace = stack });
        }
        catch (Exception) { }
    }
}
