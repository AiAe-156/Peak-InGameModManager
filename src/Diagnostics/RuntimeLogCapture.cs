#nullable enable
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;

namespace ModConfigEnhance.Diagnostics;

/// <summary>Capture original emission times from now on, without changing BepInEx log formatting.</summary>
internal sealed class RuntimeLogCapture : ILogListener
{
    private static RuntimeLogCapture? _instance;
    private static readonly object Gate = new();
    private static MceLogChannel? _language;
    private readonly MceLogChannel _runtime = MceLogApi.CreateChannel(Plugin.PluginGuid, "runtime-timestamped", "Runtime logs (timestamped)", 10000);
    [ThreadStatic] private static bool _writing;
    internal static void Start()
    {
        if (_instance != null) return;
        _instance = new RuntimeLogCapture(); BepInEx.Logging.Logger.Listeners.Add(_instance);
        Application.logMessageReceivedThreaded += _instance.UnityLog;
        LogHistory.Start(); // 同步自 MCE 1.1.0（实测修正）：把本次的 BepInEx／游戏日志持续存进 runtime/log-history，日志窗口看「上次」等用
    }
    internal static void Stop()
    {
        if (_instance == null) return;
        Application.logMessageReceivedThreaded -= _instance.UnityLog;
        BepInEx.Logging.Logger.Listeners.Remove(_instance); _instance.Dispose(); _instance = null;
    }
    internal static void LanguageDetail(string message)
    {
        lock (Gate) { _language ??= MceLogApi.CreateChannel(Plugin.PluginGuid, "language-detail", "Language changes / 语言变更详情", 10000); _language.Write(new MceLogRecord { Level = MceLogLevel.Debug, Category = "Language", Message = message }); }
    }
    // File records have no emission identity. Text cannot distinguish history from
    // a later identical event. Original times live only in this Runtime channel.
    public void LogEvent(object sender, LogEventArgs e)
    {
        var level = (e.Level.ToString() ?? "").ToLowerInvariant() switch { "fatal" => MceLogLevel.Fatal, "error" => MceLogLevel.Error, "warning" => MceLogLevel.Warning, "debug" => MceLogLevel.Debug, _ => MceLogLevel.Info };
        Record(e.Source.SourceName, e.Data?.ToString() ?? "", "", level);
    }
    private void UnityLog(string message, string stack, LogType type)
        => Record("Unity", message, stack, type is LogType.Error or LogType.Exception or LogType.Assert ? MceLogLevel.Error : type == LogType.Warning ? MceLogLevel.Warning : MceLogLevel.Info);
    private void Record(string source, string message, string stack, MceLogLevel level)
    {
        if (_writing) return;
        try
        {
            _writing = true; var now = DateTimeOffset.Now;
            _runtime.Write(new MceLogRecord { Time = now, Level = level, Category = source, Message = message, StackTrace = stack });
        }
        finally { _writing = false; }
    }
    public void Dispose() { _runtime.Dispose(); lock (Gate) { _language?.Dispose(); _language = null; } }
}
