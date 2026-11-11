#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ModConfigEnhance;

/// <summary>Only remove redundant activation in the native settings tab Update. Never patch SetActive globally.</summary>
internal static class SettingsTabActivationPatch
{
    internal static void Install(Harmony harmony)
    {
        try
        {
            var type = AccessTools.TypeByName("SettingsTABSButton");
            var update = type == null ? null : AccessTools.DeclaredMethod(type, "Update", Type.EmptyTypes);
            if (update == null) { Plugin.Log.LogWarning("[设置页] 未找到 SettingsTABSButton.Update；保留原生行为。"); return; }
            harmony.Patch(update, transpiler: new HarmonyMethod(typeof(SettingsTabActivationPatch), nameof(Transpile)));
        }
        catch (Exception error) { Plugin.Log.LogWarning("[设置页] 页签激活优化未应用；保留原生行为：" + error); }
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        var setActive = AccessTools.Method(typeof(GameObject), nameof(GameObject.SetActive), new[] { typeof(bool) });
        var matches = original.Where(i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) && Equals(i.operand, setActive)).ToArray();
        if (matches.Length != 1)
        {
            Plugin.Log.LogWarning("[设置页] 页签 SetActive 匹配数为 " + matches.Length + "（预期1）；保留全部原始IL。");
            return original;
        }
        var replacement = AccessTools.Method(typeof(SettingsTabActivationPatch), nameof(SetActiveIfChanged));
        var result = original.Select(i => new CodeInstruction(i)).ToList();
        int index = original.IndexOf(matches[0]);
        result[index].opcode = OpCodes.Call;
        result[index].operand = replacement;
        Plugin.Log.LogInfo("[设置页] 已限定优化 SettingsTABSButton.Update 的唯一 SetActive；文字渐变及其他IL保持原样。");
        return result;
    }

    internal static void SetActiveIfChanged(GameObject target, bool active)
    {
        if (target.activeSelf != active) target.SetActive(active);
    }
}
