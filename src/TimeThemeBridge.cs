#nullable enable
using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace ModConfigEnhance;

/// <summary>
/// 2.4.0：与 TimeTheme（昼夜主题，com.github.MiiMii1205.TimeTheme）的对接只剩一件事 —— 读它此刻是不是夜里，
/// 给主题选项「夜里自动紫色」用。不再读它的配色表、不再让它重新收表、不再替它给页面换色；MCE 的颜色全部来自
/// <see cref="ModConfigLook"/>。TimeTheme 对原版界面的着色与 MCE 无关，照旧由它自己负责。
/// 全部走反射，不引用它的 DLL；不在场时 <see cref="IsDark"/> 恒为 false。
/// </summary>
internal static class TimeThemeBridge
{
    internal const string Guid = "com.github.MiiMii1205.TimeTheme";
    private const string PluginTypeName = "TimeTheme.TimeThemePlugin";

    private static bool _resolved;
    private static PropertyInfo? _isDarkTheme;
    private static int _darkCacheFrame = -1;
    private static bool _darkCache;

    internal static bool Available
    {
        get
        {
            Resolve();
            return _isDarkTheme != null;
        }
    }

    private static void Resolve()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        if (!Chainloader.PluginInfos.ContainsKey(Guid))
        {
            return;
        }

        try
        {
            Type? type = AccessTools.TypeByName(PluginTypeName);
            _isDarkTheme = type != null ? AccessTools.Property(type, "IsDarkTheme") : null;
            if (_isDarkTheme == null)
            {
                Plugin.Log.LogWarning("[配色] TimeTheme 在场但读不到它的昼夜状态，「夜里自动紫色」按白天处理。");
            }
        }
        catch (Exception ex)
        {
            _isDarkTheme = null;
            Plugin.Log.LogWarning("[配色] 对接 TimeTheme 失败，「夜里自动紫色」按白天处理：" + ex.Message);
        }
    }

    /// <summary>
    /// TimeTheme 此刻是否夜间。每帧最多读一次反射。只在游戏场景里算数：它的昼夜标记回到主菜单也不复位，
    /// 而主菜单本身它不着色，夜里退回主菜单不该单独变紫。
    /// </summary>
    internal static bool IsDark
    {
        get
        {
            Resolve();
            if (_isDarkTheme == null || GUIManager.instance == null)
            {
                return false;
            }

            int frame = Time.frameCount;
            if (frame == _darkCacheFrame)
            {
                return _darkCache;
            }

            _darkCacheFrame = frame;
            try
            {
                _darkCache = _isDarkTheme.GetValue(null) is bool dark && dark;
            }
            catch
            {
                _darkCache = false;
            }

            return _darkCache;
        }
    }
}
