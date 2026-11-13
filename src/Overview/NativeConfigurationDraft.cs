#nullable enable
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using PEAKLib.ModConfig;
using Zorro.Settings;

namespace ModConfigEnhance.Overview;

/// <summary>
/// 1.1.4（用户：「模组设置页面完全移除草稿系统」）：ModConfig 原生控件不再被拦截，改值由 ModConfig 自己写进
/// ConfigEntry（BepInEx 默认立即存盘），当场生效。IGMM 不写草稿、不写恢复文件、不延迟提交。
/// 只做一件事：把当前查看方案的内存记录跟上实际值，概览里不会因此出现「未保存」。
/// 之前的草稿拦截（Save/ApplyValue/SetDefaultValue 前缀、RefreshValueFromConfig 后缀）全部移除。
/// </summary>
internal static class NativeConfigurationDraft
{
    internal static OverviewRepository? Repository;
    [ThreadStatic] private static int _committing;

    /// <summary>Kept for callers that push values into native settings (runtime sync); no longer changes behaviour here.</summary>
    internal sealed class CommitScope : IDisposable
    {
        internal CommitScope() { _committing++; }
        public void Dispose() { _committing--; }
    }

    internal static void Install(Harmony harmony)
    {
        // Follow the live value after ModConfig writes it (Save → its save callback sets ConfigEntry.BoxedValue).
        // Walk each concrete setting type and its ancestors, so closed generic parents (e.g. a Setting<int> that declares
        // Save) are patched once through the closed type; open generic definitions are never patched.
        foreach (var type in typeof(IBepInExProperty).Assembly.GetTypes())
        {
            if (type.ContainsGenericParameters || !typeof(IBepInExProperty).IsAssignableFrom(type) || !typeof(Setting).IsAssignableFrom(type)) continue;
            for (Type? current = type; current != null && current != typeof(Setting) && current != typeof(object); current = current.BaseType)
            {
                if (current.ContainsGenericParameters) continue;
                foreach (var method in current.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (method.Name is not ("Save" or "SetDefaultValue") || method.IsAbstract || !Patched.Add(method)) continue;
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(NativeConfigurationDraft), nameof(Follow)));
                }
            }
        }
    }
    private static readonly HashSet<System.Reflection.MethodBase> Patched = new();

    /// <summary>After a native write: record the live value in the viewed profile (memory only, no file, no draft).</summary>
    private static void Follow(object __instance)
    {
        if (_committing != 0 || __instance is not IBepInExProperty property) return;
        try { Repository?.FollowLiveValue(property.Pluginfo.Metadata.GUID, property.ConfigBase); }
        catch (Exception error) { Plugin.Log.LogDebug("[设置页] 方案记录跟随失败：" + error.Message); }
    }
}
