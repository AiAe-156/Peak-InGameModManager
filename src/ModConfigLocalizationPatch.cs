#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using PEAKLib.ModConfig.Components;
using PEAKLib.ModConfig.SettingOptions.SettingUI;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;

namespace ModConfigEnhance;

/// <summary>
/// 本地化 ModConfig 1.8.x 自己的界面文字（词条与语言文件见 <see cref="Loc"/>）。ModConfig 只给「MOD SETTINGS / BACK」做了多语言，
/// 其余全是写死的英文：搜索标签与占位、MODS / SECTIONS、每个选项的 DEFAULT / CLEAR、
/// 按键捕获时的 SELECT A KEY、按键重复警告、筛选下拉的五个类型名；
/// 多选下拉框自带的 Nothing / Everything / Mixed... 则是游戏魔改版 TMP_Dropdown 的静态文本。
///
/// 做法：把词条注册进游戏自己的语言表（LocalizedText.mainTable，与 PEAKLib 的 MenuAPI 同一条路），
/// 能挂 LocalizedText 组件的文字就挂组件（切语言时游戏会统一刷新），挂不了的在语言切换回调里重设。
/// 跟随游戏语言时简体 / 繁体 / 英文内建，其他语言回落英文；选了语言文件则全语言用该文件。
///
/// 模组名、分区名、选项名、下拉选项（各模组 cfg 里的原文）2.5.0 起由 <see cref="MceConfigLabels"/> 按语言文件显示，不再交给 XUnity。
/// </summary>
internal static class ModConfigLocalizationPatch
{
    private const string KeyPrefix = Loc.KeyPrefix;
    private const string BoundToEnglish = "This key is bound to: ";

    /// <summary>ModConfig 给筛选下拉框的五个选项，顺序即位掩码 1/2/4/8/16。</summary>
    private static readonly string[] FilterKeys =
    {
        "FILTER_BOOLS", "FILTER_STRINGS", "FILTER_NUMBERS", "FILTER_ENUMS", "FILTER_CONTROLS",
    };

    /// <summary>游戏魔改版 TMP_Dropdown 里多选模式的三个静态 OptionData，改了全游戏生效（用户拍板）。</summary>
    private static readonly (string Field, string Key)[] DropdownStatics =
    {
        ("k_NothingOption", "DD_NOTHING"),
        ("k_EverythingOption", "DD_EVERYTHING"),
        ("k_MixedOption", "DD_MIXED"),
    };

    private static bool _staticFieldWarned;

    internal static bool Attached { get; private set; }

    internal static string State { get; private set; } = "未挂载";

    // ── 挂载 ────────────────────────────────────────────────────────

    /// <summary>
    /// Awake 阶段只解析目标、挂钩子、订阅语言切换；词条注册与 TMP 静态文本推迟到第一次构建页面
    /// （主菜单一进就会构建），避免在插件加载期就去碰游戏语言表的资源加载。
    /// 任何一步失败都整项放弃并记日志，不让异常逃到 Awake 里连累其他分区。
    /// </summary>
    internal static void TryApply(Harmony harmony)
    {
        try
        {
            Apply(harmony);
        }
        catch (Exception ex)
        {
            State = "挂载失败";
            Plugin.Log.LogError($"[界面汉化] 挂载失败，本项跳过: {ex}");
        }
    }

    private static void Apply(Harmony harmony)
    {
        if (!ModConfigSupport.IsInstalled)
        {
            State = "ModConfig 未安装，跳过";
            Plugin.Log.LogInfo($"[界面汉化] {State}。");
            return;
        }

        Type? pluginType = AccessTools.TypeByName(ModConfigSupport.PluginTypeName);
        Type? bindingUiType = AccessTools.TypeByName(ModConfigSupport.BindingUiTypeName);
        Type? menuButtonType = AccessTools.TypeByName(ModConfigSupport.MenuButtonTypeName);

        MethodInfo? builder = pluginType != null
            ? ModConfigSupport.FindLocalFunction(pluginType, "builderDelegate", new[] { typeof(Transform) })
            : null;
        MethodInfo? setText = menuButtonType != null
            ? AccessTools.Method(menuButtonType, "SetText", new[] { typeof(string) })
            : null;
        MethodInfo? capturePrompt = bindingUiType != null
            ? AccessTools.Method(bindingUiType, "ShowCapturePrompt", Type.EmptyTypes)
            : null;
        MethodInfo? duplicates = bindingUiType != null
            ? AccessTools.Method(bindingUiType, "GetDuplicateControls", Type.EmptyTypes)
            : null;

        if (builder == null || setText == null || capturePrompt == null || duplicates == null)
        {
            State = "目标方法缺失，跳过";
            Plugin.Log.LogWarning(
                $"[界面汉化] 目标方法缺失（builder={builder != null}, SetText={setText != null}, " +
                $"ShowCapturePrompt={capturePrompt != null}, GetDuplicateControls={duplicates != null}），" +
                "ModConfig 或 PEAKLib.UI 可能已改结构，本项跳过。");
            return;
        }

        LocalizedText.OnLangugageChanged = (Action)Delegate.Combine(
            LocalizedText.OnLangugageChanged, new Action(OnLanguageChanged));

        harmony.Patch(builder, postfix: new HarmonyMethod(typeof(ModConfigLocalizationPatch), nameof(BuilderPostfix)));
        harmony.Patch(setText, postfix: new HarmonyMethod(typeof(ModConfigLocalizationPatch), nameof(SetTextPostfix)));
        harmony.Patch(capturePrompt, postfix: new HarmonyMethod(typeof(ModConfigLocalizationPatch), nameof(CapturePromptPostfix)));
        harmony.Patch(duplicates, postfix: new HarmonyMethod(typeof(ModConfigLocalizationPatch), nameof(DuplicatesPostfix)));
        harmony.Patch(AccessTools.Method(typeof(ModSettingsMenu), "ShowSettings"), postfix: new HarmonyMethod(typeof(ModConfigLocalizationPatch), nameof(MceSettingsPostfix)));
        harmony.Patch(AccessTools.Method(typeof(ModSettingsMenu), "UpdateSectionTabs"), postfix: new HarmonyMethod(typeof(ModConfigLocalizationPatch), nameof(MceSettingsPostfix)));

        Attached = true;
        State = $"已挂载（ModConfig {ModConfigSupport.InstalledVersion}）";
        ModConfigSupport.LogVersionNote("界面汉化");
    }

    // ── 词条 ────────────────────────────────────────────────────────

    private static void EnsureTerms() => Loc.EnsureRegistered();

    private static string Localize(string key) => Loc.Get(key);

    private static void ApplyDropdownStaticTexts()
    {
        foreach ((string field, string key) in DropdownStatics)
        {
            FieldInfo? info = AccessTools.Field(typeof(TMP_Dropdown), field);
            if (info?.GetValue(null) is TMP_Dropdown.OptionData option)
            {
                option.text = Localize(key);
            }
            else if (!_staticFieldWarned)
            {
                _staticFieldWarned = true;
                Plugin.Log.LogWarning($"[界面汉化] 找不到 TMP_Dropdown.{field}，多选下拉框的 Nothing / Everything / Mixed... 保持英文。");
            }
        }
    }

    private static void ApplyFilterLabels(ModSettingsMenu menu)
    {
        PeakDropdown? dropdown = menu.FilterDropdown;
        TMP_Dropdown? tmp = dropdown != null ? dropdown.Dropdown : null;
        if (tmp == null)
        {
            return;
        }

        int count = Math.Min(tmp.options.Count, FilterKeys.Length);
        for (int i = 0; i < count; i++)
        {
            tmp.options[i].text = Localize(FilterKeys[i]);
        }

        tmp.RefreshShownValue();
    }

    /// <summary>
    /// 不走 LocalizedText 组件的那些文字（多选下拉静态项、筛选标签）按当前词条重设。
    /// 游戏切语言时 RefreshAllText 已刷过所有 LocalizedText 组件，这里补没有组件的那几处；语言文件切换后由 Loc 调。
    /// </summary>
    internal static void RefreshStaticTexts()
    {
        try
        {
            ApplyDropdownStaticTexts();
            ModSettingsMenu? menu = ModSettingsMenu.Instance;
            if (menu != null)
            {
                ApplyFilterLabels(menu);
                MceConfigLabels.Refresh(menu);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[界面汉化] 静态文本刷新失败: {ex.Message}");
        }
    }

    private static void OnLanguageChanged()
    {
        try
        {
            if (!Loc.IsRefreshing)
            {
                Loc.ReRegister(); // 语言表若被整表重载（ReloadAll），把词条补回去
                if (string.IsNullOrEmpty(Plugin.LanguageFile.Value))
                {
                    Loc.InvalidateEditingTranslations(); // 跟随游戏语言：游戏换了语言，模组文字也跟着换组重画
                }
            }

            ApplyDropdownStaticTexts();

            ModSettingsMenu? menu = ModSettingsMenu.Instance;
            if (menu != null)
            {
                ApplyFilterLabels(menu);
                MceConfigLabels.Refresh(menu);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[界面汉化] 语言切换后刷新失败: {ex.Message}");
        }
    }

    // ── 页面构建完成：给固定文字挂组件、重设筛选项 ─────────────────

    private static void BuilderPostfix()
    {
        try
        {
            ModSettingsMenu? menu = ModSettingsMenu.Instance;
            if (menu == null || menu.MainPage == null)
            {
                return;
            }

            EnsureTerms();
            ApplyDropdownStaticTexts();
            Transform page = menu.MainPage.transform;

            // 「Search」标签：页面直属的 PeakText 只有它一个（标题在 Header 容器里）
            foreach (Transform child in page)
            {
                PeakText? label = child.GetComponent<PeakText>();
                if (label != null)
                {
                    label.SetLocalizationIndex(KeyPrefix + "SEARCH");
                }
            }

            // 搜索框占位文字：SetPlaceholder 只是赋值，挂个 LocalizedText 让它跟着语言走
            Transform? input = page.Find("SearchInput");
            PeakTextInput? textInput = input != null ? input.GetComponent<PeakTextInput>() : null;
            if (textInput != null && textInput.InputField != null &&
                textInput.InputField.placeholder is TextMeshProUGUI placeholder)
            {
                LocalizedText? localized = placeholder.gameObject.GetComponent<LocalizedText>();
                if (localized == null)
                {
                    localized = placeholder.gameObject.AddComponent<LocalizedText>();
                }

                localized.tmp = placeholder;
                localized.index = KeyPrefix + "SEARCH_HERE";
                localized.RefreshText();
            }

            // 「MODS」「SECTIONS」：内容面板直属的两个 PeakText，按创建顺序对应（树形布局下它们已被隐藏，仍然挂上，关掉布局也有效）
            string[] panelKeys = { "MODS", "SECTIONS" };
            int index = 0;
            foreach (Transform child in menu.transform)
            {
                PeakText? label = child.GetComponent<PeakText>();
                if (label == null)
                {
                    continue;
                }

                if (index < panelKeys.Length)
                {
                    label.SetLocalizationIndex(KeyPrefix + panelKeys[index]);
                }

                index++;
            }

            ApplyFilterLabels(menu);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[界面汉化] 页面文字处理失败: {ex}");
        }
    }

    private static void MceSettingsPostfix(ModSettingsMenu __instance)
    {
        try { MceConfigLabels.Refresh(__instance); }
        catch (Exception ex) { Plugin.Log.LogWarning("[MCE localization] Could not refresh configuration labels: " + ex.Message); }
    }

    // ── 单元格里的按钮与提示 ────────────────────────────────────────

    /// <summary>
    /// ModConfig 的每个选项单元格都用 MenuAPI.CreateMenuButton("DefaultsButton") 再 SetText("DEFAULT")
    /// 生成按钮（清空同理）。CreateMenuButton 把对象命名为 UI_MainMenuButton_&lt;名字&gt;，
    /// 于是「名字 + 文本」两个条件就能精确认出这两种按钮，不会误伤其他模组用同一 API 建的按钮。
    /// </summary>
    private static void SetTextPostfix(object __instance, string text)
    {
        if (__instance is not PeakMenuButton button)
        {
            return;
        }

        string? key = text switch
        {
            "DEFAULT" when button.name == "UI_MainMenuButton_DefaultsButton" => "DEFAULT",
            "CLEAR" when button.name == "UI_MainMenuButton_ClearButton" => "CLEAR",
            _ => null,
        };

        if (key == null)
        {
            return;
        }

        try
        {
            button.SetLocalizationIndex(KeyPrefix + key);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[界面汉化] {key} 按钮挂本地化失败: {ex.Message}");
        }
    }

    private static void CapturePromptPostfix(object __instance)
    {
        // 手柄模式让给 GamepadSupport 的「长按某个键 1 秒换绑」提示（本 postfix 后执行，不让它盖回去）
        if (GamepadSupport.IsGamepad)
        {
            return;
        }

        if (__instance is InputBindingSettingUI ui && ui.KeyText != null)
        {
            ui.KeyText.text = Localize("SELECT_KEY");
        }
    }

    private static void DuplicatesPostfix(object __instance)
    {
        if (__instance is not InputBindingSettingUI ui)
        {
            return;
        }

        TextMeshProUGUI? warning = ui.DuplicateWarningText;
        // 1.1.3: tell the cell's corner badge (it no longer polls the expensive duplicate scan each frame).
        var extras = ui.GetComponentInParent<SettingCellExtras.CellExtras>();
        if (extras != null) extras.OnDuplicatesRecomputed(warning != null && warning.gameObject.activeSelf, warning != null ? warning.text : "");
        if (warning == null || !warning.gameObject.activeSelf)
        {
            return;
        }

        string current = warning.text ?? string.Empty;
        if (current.StartsWith(BoundToEnglish, StringComparison.Ordinal))
        {
            warning.text = Localize("BOUND_TO") + current.Substring(BoundToEnglish.Length);
        }
    }
}
