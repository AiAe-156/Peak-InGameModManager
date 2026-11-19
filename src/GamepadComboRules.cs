#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModConfigEnhance;

/// <summary>
/// 手柄组合键的判定规则（纯逻辑，不碰 Unity / BepInEx），由 <see cref="GamepadCombos"/> 调用。
/// IGMM 0.0.2（2026-11-18）从 MCE 1.1.0 同名文件照搬，另加 IGMM 旧格式迁移 <see cref="LegacyModifier"/>；
/// 夹具 <c>验证/MCE1.1.0同步_20261117/combo-checks/</c> 直接链接本文件做单元检查。改规则时跑它。
/// </summary>
internal static class GamepadComboRules
{
    internal static readonly string[] Buttons = { "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder",
        "leftTrigger", "rightTrigger", "dpad/up", "dpad/down", "dpad/left", "dpad/right", "leftStickPress", "rightStickPress", "start", "select" };
    /// <summary>一组最多几个键（含最后一个）。</summary>
    internal const int MaxButtons = 4;

    /// <summary>同一组键（先按住的键不分顺序）的比较键：一组键只能给一个快捷键。</summary>
    internal static string SetKey(IEnumerable<string> held, string button) =>
        string.Join("+", held.OrderBy(h => h, StringComparer.Ordinal)) + ">" + button;

    /// <summary>
    /// 读文件时整理「先按住」的键：游戏内模组管理器格式的单个 <paramref name="modifier"/> 并入（放最前），
    /// 去掉未知键、与最后一个键相同的键、重复键。
    /// </summary>
    internal static List<string> NormalizeHeld(List<string>? held, string? modifier, string button)
    {
        var list = held ?? new List<string>();
        if (!string.IsNullOrEmpty(modifier) && !list.Contains(modifier!)) list.Insert(0, modifier!);
        return list.Where(h => Buttons.Contains(h) && h != button).Distinct().ToList();
    }

    /// <summary>IGMM 0.0.2 以前全局唯一修饰键下拉的选项（"" = 无）。只用于读旧文件。</summary>
    internal static readonly string[] LegacyModifiers = { "", "leftTrigger", "rightTrigger", "leftShoulder", "rightShoulder", "select", "start", "rightStickPress", "leftStickPress" };

    /// <summary>
    /// IGMM 0.0.2：读旧版 gamepad-combos.json（没有 version 字段）时，算出这一组实际生效的修饰键，交给 <see cref="NormalizeHeld"/> 并入 held。
    /// 照旧版 Load 的实际效果：条目 modifier 为 null = 1.1.2 写的，一律 LT；空 = 单键；非空的一律换成文件顶层的共用修饰键
    /// （顶层缺失或不认识时旧版默认 Back，顶层为空 = 用户选了「无」，组合全变单键）；条目的修饰键不认识、或共用修饰键与最后一个键相同，
    /// 旧版会丢掉这条，这里返回 null。
    /// </summary>
    internal static string? LegacyModifier(string? itemModifier, string? storeModifier, string button)
    {
        string mod = itemModifier ?? "leftTrigger";
        if (mod.Length == 0) return "";
        if (!LegacyModifiers.Contains(mod)) return null;
        string shared = storeModifier != null && LegacyModifiers.Contains(storeModifier) ? storeModifier : "select";
        if (shared.Length == 0) return "";
        return shared == button ? null : shared;
    }

    /// <summary>整理后的一组是否可用：有 id、最后一个键认识、总键数不超过 <see cref="MaxButtons"/>。</summary>
    internal static bool IsUsable(string? id, string? button, int heldCount) =>
        !string.IsNullOrEmpty(id) && button != null && Buttons.Contains(button) && heldCount + 1 <= MaxButtons;

    /// <summary>
    /// 这一组的「先按住」键全部按着，并且没有别的「先按住」键多按着（多按了说明玩家想要的是另一组，比如
    /// 「LT + A」不该同时触发单键「A」）。<paramref name="allHeld"/> = 所有组的「先按住」键并集。
    /// 不检查最后一个键本身（调用方已确认它按着）。
    /// </summary>
    internal static bool Satisfied(IReadOnlyCollection<string> held, string button, IEnumerable<string> allHeld, Func<string, bool> pressed)
    {
        foreach (string name in held) if (!pressed(name)) return false;
        foreach (string name in allHeld)
        {
            if (name == button || held.Contains(name)) continue;
            if (pressed(name)) return false;
        }
        return true;
    }

    /// <summary>
    /// 已满足的多键组，最后一个键是否要对游戏屏蔽：单键组不屏蔽；最后一个键在某个「先按住」键之前就按下了
    /// （先按 RT 攀爬、再按 LT）也不屏蔽，否则游戏会丢掉 RT。<paramref name="pressedAt"/> 为空（没记到按下帧）时照常屏蔽。
    /// </summary>
    internal static bool MaskLastButton(IReadOnlyCollection<string> held, int? pressedAt, Func<string, int?> heldSince)
    {
        if (held.Count == 0) return false;
        if (pressedAt is int since && held.Any(h => heldSince(h) is int down && since < down)) return false;
        return true;
    }
}
