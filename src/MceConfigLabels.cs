#nullable enable
using System.Collections.Generic;
using PEAKLib.ModConfig;
using PEAKLib.ModConfig.Components;
using PEAKLib.ModConfig.SettingOptions.SettingUI;
using TMPro;
using UnityEngine;
using Zorro.Settings;

namespace ModConfigEnhance;

/// <summary>
/// ModConfig 设置页上各模组文字的显示翻译：选项名（单元格标题）、下拉选项、分区页签。
/// 2.5.0 起覆盖所有模组（原先只管本模组，其他模组靠 XUnity），统一走 <see cref="Loc.TranslateExternal"/>：
/// 整句精确 → 正则 → 原文；「显示原文」开着时一律原文。只改显示，cfg 里的节名与键名不动。
/// 每次都从原文重算：单元格标题的原文是 <c>GetDisplayName()</c>，分区页签的原文是 <c>category</c>，
/// 下拉选项的原文第一次见到时记在 <see cref="OriginalOptions"/> 上，所以切回英文 / 显示原文都能还原。
/// 调用点：ShowSettings、UpdateSectionTabs 之后，换语言、重载翻译、显示原文切换时（<see cref="ModConfigLocalizationPatch.RefreshStaticTexts"/>）。
/// </summary>
internal static class MceConfigLabels
{
    /// <summary>挂在下拉框上，记住 ModConfig 填进去的原文选项。</summary>
    private sealed class OriginalOptions : MonoBehaviour
    {
        internal readonly List<string> Texts = new List<string>();
    }

    internal static void Refresh(ModSettingsMenu? menu)
    {
        if (menu == null) return;
        foreach (SettingsUICell cell in menu.m_spawnedCells)
        {
            if (cell == null || cell.m_settingsContentParent == null) continue;
            var input = cell.m_settingsContentParent.GetComponentInChildren<SettingInputUICell>(true);
            if (input?._listeningSetting is not IBepInExProperty property) continue;
            string name = property.GetDisplayName() ?? "";
            if (cell.m_text != null) cell.m_text.text = Loc.TranslateExternal(name);
            foreach (TMP_Dropdown dropdown in cell.GetComponentsInChildren<TMP_Dropdown>(true))
                TranslateOptions(dropdown);
        }

        if (menu.SectionTabController == null) return;
        foreach (GameObject row in menu.SectionTabController.Tabs)
        {
            if (row == null) continue;
            var button = row.GetComponent<ModdedTABSButton>();
            var text = row.GetComponentInChildren<TextMeshProUGUI>(true);
            if (button != null && text != null && !string.IsNullOrEmpty(button.category)) text.text = Loc.TranslateExternal(button.category);
        }
    }

    private static void TranslateOptions(TMP_Dropdown dropdown)
    {
        OriginalOptions originals = dropdown.GetComponent<OriginalOptions>() ?? dropdown.gameObject.AddComponent<OriginalOptions>();
        // 选项数变了（ModConfig 重新 Setup 过）就重新记原文
        if (originals.Texts.Count != dropdown.options.Count)
        {
            originals.Texts.Clear();
            foreach (TMP_Dropdown.OptionData option in dropdown.options) originals.Texts.Add(option.text ?? "");
        }

        bool changed = false;
        for (int i = 0; i < dropdown.options.Count; i++)
        {
            string shown = Loc.TranslateExternal(originals.Texts[i]);
            if (dropdown.options[i].text != shown) { dropdown.options[i].text = shown; changed = true; }
        }

        if (changed) dropdown.RefreshShownValue();
    }
}
