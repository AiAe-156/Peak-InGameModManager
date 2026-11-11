#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using PEAKLib.UI;
using PEAKLib.UI.Elements;
using TMPro;
using UnityEngine;

namespace ModConfigEnhance;

/// <summary>
/// 模组设置页左栏工具行上的两个按钮（2.16.0 起从「搜索」标签右侧挪到列表上方）：
///   · 导出翻译：把 ModConfig 会显示出来的全部文本 —— 模组名、分区名、选项名、选项说明、下拉框选项 ——
///     按模组分块写成 XUnity AutoTranslator 词典格式（<c>原文=</c>，等号右边留给译文）；
///   · 补漏翻：按当前语言组判断（整句译文或 r: 正则命中都算已翻），只导出还没有译文的原文。2.5.0 起不再需要 XUnity。
/// 工具行第三格「仅导出已选」勾上时，只导出左栏导出列勾选的模组（默认全勾，去勾的记在 cfg 里）。
/// 导出文件放在 BepInEx\config\ModConfigEnhance\translation-export\，填好后用语言行「导入补翻」合进当前语言文件。
///
/// 枚举规则照抄 ModConfig 1.8.1 的 ProcessModEntries / GetModConfigEntries：同样跳过带 Hidden 标签的项、
/// 同样只收它支持的类型、模组名同样过一遍它的 FixNaming，保证导出的原文与屏幕上显示的逐字一致。
/// KeyCode 型选项的「可选」不导出：那是一百多个键名，屏幕上显示的是按键图标，不是文字。
/// </summary>
internal static class ModConfigTranslationExport
{
    private static readonly Color ExportColor = new Color(0.185f, 0.394f, 0.6226f);
    private static readonly Color CheckColor = new Color(0.4006706f, 0.6039216f, 0.1411764f);
    private static readonly Color ToolColor = new Color(0.3918986f, 0.1843137f, 0.6235294f);
    private static readonly Color DisabledColor = new Color(0.3f, 0.3f, 0.3f);
    private static Color FolderColor => ModConfigLook.Surface(new Color(0.55f, 0.36f, 0.16f));

    /// <summary>工具行文字统一 13 号（与表头「导出 / 置顶」同号）。</summary>
    internal const float ToolbarFontSize = 13f;

    /// <summary>选项清单超过这个条数就不导出（按键名清单之类）。</summary>
    private const int MaxChoices = 40;

    internal static string ExportDirectory => Loc.ExportDirectory;

    /// <summary>
    /// 工具行里的一格按钮（左上锚、左上轴）。按钮模板高 67 左右，压到工具行高度后两条虚线要按比例缩回去。
    /// </summary>
    internal static PeakMenuButton MakeToolbarButton(RectTransform page, string name, bool untranslatedOnly, float left, float top, float width, float height)
    {
        Color color = untranslatedOnly ? CheckColor : ExportColor;
        PeakMenuButton button = MakeButton(page, name, untranslatedOnly ? "BTN_UNTRANSLATED" : "BTN_EXPORT", color, left, top, width, height, () => Run(untranslatedOnly), UiActionRole.Commit);
        button.gameObject.AddComponent<ModConfigSidebarWidgets.TipTarget>().Key = untranslatedOnly ? "TIP_UNTRANSLATED_V2" : "TIP_EXPORT_V2";
        return button;
    }

    /// <summary>
    /// 第二行（2.5.0 起全是本模组自己的功能）：「显示原文 / 显示译文」切换本模组负责显示的模组文字；
    /// 「重载翻译」重读语言文件；「导入补翻」把填好的导出文件合进当前语言文件；「打开目录」（方形图标钮）。
    /// </summary>
    internal static void MakeTranslationTools(RectTransform page, float left, float top, float width, float height, float gap)
    {
        float third = Mathf.Floor((width - height - gap * 3f) / 3f);
        PeakMenuButton toggle = MakeButton(page, "MCE_OriginalToggle", ToggleKey(), ToolColor, left, top, third, height, null, UiActionRole.Edit);
        toggle.OnClick(() =>
        {
            Loc.ToggleOriginal();
            toggle.SetLocalizationIndex(Loc.Index(ToggleKey()));
            Notify(Loc.Get(Loc.ShowingOriginal ? "NOTICE_SHOWING_ORIGINAL_V2" : "NOTICE_SHOWING_TRANSLATED"));
        });
        toggle.gameObject.AddComponent<ToggleLabel>().Button = toggle;
        toggle.gameObject.AddComponent<ModConfigSidebarWidgets.TipTarget>().Key = "TIP_TOGGLE_ORIG_V2";

        // 重载 = 把磁盘上的语言文件重新读一遍：本模组界面词条重新应用，侧栏、概览、说明面板与设置页随版本号重画。
        PeakMenuButton reload = MakeButton(page, "MCE_ReloadTranslations", "BTN_RELOAD", ToolColor, left + third + gap, top, third, height, () =>
        {
            Loc.ApplyResult result = Loc.Apply(Plugin.LanguageFile.Value, notify: false, refresh: true);
            string message = result.Success ? Loc.Get("NOTICE_RELOADED_V2") + "\n" + result.Message : result.Message;
            Notify(message, result.Warning);
        });
        reload.gameObject.AddComponent<ModConfigSidebarWidgets.TipTarget>().Key = "TIP_RELOAD_V2";

        PeakMenuButton import = MakeButton(page, "MCE_ImportAdditions", "BTN_IMPORT_ADDITIONS", CheckColor, left + (third + gap) * 2f, top, third, height, ImportAdditions, UiActionRole.Commit);
        import.gameObject.AddComponent<ModConfigSidebarWidgets.TipTarget>().Key = "TIP_IMPORT_ADDITIONS";

        PeakMenuButton openDir = MakeButton(page, "MCE_OpenFolder", "BTN_OPEN_FOLDER", FolderColor, left + (third + gap) * 3f, top, height, height, OpenRootFolder);
        ModConfigSidebarWidgets.StyleFolderButton(openDir, height);
        openDir.gameObject.AddComponent<ModConfigSidebarWidgets.TipTarget>().Key = "TIP_OPEN_DIR_V3";
    }

    /// <summary>2.5.0：选一个填好的导出文件，有译文的行合进当前语言文件，然后重读。</summary>
    private static void ImportAdditions()
    {
        try
        {
            Directory.CreateDirectory(ExportDirectory);
            if (!Overview.IntroductionFilePicker.TrySelect("translation", out string? path, ExportDirectory))
            {
                Notify(Loc.Format("NOTICE_IMPORT_FAILED", Loc.T("the file dialog is unavailable", "文件选择窗口不可用", "檔案選擇視窗不可用")), warning: true);
                return;
            }

            if (string.IsNullOrEmpty(path)) return; // 取消
            Loc.ImportResult result = Loc.ImportAdditions(path!);
            if (result.Total == 0) { Notify(Loc.Get("NOTICE_IMPORT_NOTHING"), warning: true); return; }
            Loc.ApplyResult applied = Loc.Apply(Plugin.LanguageFile.Value, notify: false, refresh: true);
            string summary = Loc.Format("NOTICE_IMPORTED_V2", result.Total, Path.GetFileName(result.Target), result.Added, result.Replaced, result.Empty, result.Unchanged);
            Notify(summary + (applied.Success ? "" : "\n" + applied.Message), !applied.Success);
            Plugin.Log.LogInfo($"[语言] 导入补翻 {path} → {result.Target}：新增 {result.Added}，替换 {result.Replaced}，跳过空行 {result.Empty}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError("[语言] 导入补翻失败：" + ex);
            Notify(Loc.Format("NOTICE_IMPORT_FAILED", ex.Message), warning: true);
        }
    }

    /// <summary>打开 config\ModConfigEnhance\（language 与 export 都在里面）。</summary>
    private static void OpenRootFolder()
    {
        try
        {
            // 2.5.5（用户要求）：直接打开翻译导出目录（导出翻译、补漏翻写在这里，导入补翻也从这里选）
            Directory.CreateDirectory(Loc.ExportDirectory);
            Application.OpenURL("file:///" + Loc.ExportDirectory.Replace('\\', '/'));
        }
        catch (Exception ex)
        {
            Notify(Loc.Format("NOTICE_OPEN_FOLDER_FAILED", ex.Message), warning: true);
        }
    }

    private static string ToggleKey()
    {
        return Loc.ShowingOriginal ? "BTN_SHOW_TRANSLATED" : "BTN_SHOW_ORIGINAL";
    }

    /// <summary>页面重新打开时按当前状态刷一次按钮文字。</summary>
    private sealed class ToggleLabel : MonoBehaviour
    {
        internal PeakMenuButton? Button;

        private void OnEnable()
        {
            if (Button != null)
            {
                Button.SetLocalizationIndex(Loc.Index(ToggleKey()));
            }
        }
    }

    /// <summary><paramref name="termKey"/> 是 Loc 词条键：按钮文字挂 LocalizedText，切语言即时跟着变。</summary>
    internal static PeakMenuButton MakeButton(RectTransform page, string name, string termKey, Color color, float left, float top, float width, float height, Action? onClick, UiActionRole role = UiActionRole.Neutral)
    {
        Loc.EnsureRegistered();
        PeakMenuButton button = MenuAPI.CreateMenuButton(name).ParentTo(page);
        button.SetAnchorMin(new Vector2(0f, 1f))
            .SetAnchorMax(new Vector2(0f, 1f))
            .SetPivot(new Vector2(0f, 1f))
            .SetSize(new Vector2(width, height))
            .SetPosition(new Vector2(left, -top))
            .SetLocalizationIndex(Loc.Index(termKey))
            .SetColor(color); // 虚线色由 PEAKLib 按底色自动取深一档，与「返回」按钮同一套规则
        if (onClick != null)
        {
            button.OnClick(() => onClick());
        }

        ModConfigSidebarWidgets.FitBorders(button);
        button.gameObject.AddComponent<ModConfigSidebarWidgets.ThemeAsOption>();
        button.gameObject.AddComponent<GamepadSupport.FocusHighlight>(); // 手柄焦点框
        button.Text.rectTransform.offsetMin = new Vector2(4f, 4f);
        button.Text.rectTransform.offsetMax = new Vector2(-4f, -4f);
        button.Text.enableAutoSizing = false;
        button.Text.fontSize = ToolbarFontSize;
        if(button.Text is TextMeshProUGUI caption) { Overview.MceRegularFont.ApplyDefault(caption); Overview.MceTextWeight.Apply(caption); }
        button.Text.textWrappingMode = TextWrappingModes.NoWrap;
        button.Text.overflowMode = TextOverflowModes.Overflow;
        ModConfigSidebarWidgets.FitCaption(button.Text); // 2.4.6：放不下时缩字，不出框
        UiActionStyle.Set(button.Button, role, color == DisabledColor);
        return button;
    }

    // ── 执行 ────────────────────────────────────────────────────────

    private static void Run(bool untranslatedOnly)
    {
        try
        {
            List<ModTexts> mods = Collect();
            if (ModConfigSidebarWidgets.ExportSelectedOnly)
            {
                // 本模组自己的选项永远在（导出文件同时是本模组的语言文件）
                mods.RemoveAll(mod => mod.Guid != Plugin.PluginGuid && !ModConfigSidebarWidgets.ShouldExport(mod.Name));
            }

            // 2.5.0：只查当前语言组（整句或 r: 正则命中都算已翻），不再需要 XUnity。
            Func<string, bool> isTranslated = untranslatedOnly ? Loc.HasTranslation : _ => false;
            string file = Write(mods, untranslatedOnly, isTranslated, out int written, out int skipped, out int noNeed);
            string summary = untranslatedOnly
                ? Loc.Format("NOTICE_UNTRANSLATED", written, skipped, noNeed)
                : Loc.Format("NOTICE_EXPORTED", written);
            if (ModConfigSidebarWidgets.ExportSelectedOnly)
            {
                summary += Loc.Format("NOTICE_SELECTED_MODS", mods.Count);
            }

            Notify(summary + "\n" + file);
            Plugin.Log.LogInfo($"[翻译导出] {summary.Replace("\n", " ")} → {file}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[翻译导出] 失败: {ex}");
            Notify(Loc.Get("NOTICE_EXPORT_FAILED"), warning: true);
        }
    }

    private static void Notify(string text, bool warning = false)
    {
        ModConfigDescriptionPanel.Current?.ShowNotice(text, warning);
    }

    // ── 收集 ────────────────────────────────────────────────────────

    private sealed class ModTexts
    {
        internal string Name = "";
        internal string Guid = "";
        internal string Version = "";
        internal string Description = "";
        internal List<(string Section, string Key, string DescriptionKey)> Hotkeys = new();
        /// <summary>分区 → 该分区的原文（选项名、说明、下拉选项），顺序保留。</summary>
        internal List<(string Section, List<string> Texts)> Sections = new List<(string, List<string>)>();
    }

    private static List<ModTexts> Collect()
    {
        List<ModTexts> result = new List<ModTexts>();
        List<PluginInfo> plugins = new List<PluginInfo>(Chainloader.PluginInfos.Values);
        plugins.Sort((a, b) => string.CompareOrdinal(a.Metadata.Name, b.Metadata.Name));

        foreach (PluginInfo plugin in plugins)
        {
            if (plugin.Instance == null)
            {
                continue;
            }

            ModTexts mod = new ModTexts
            {
                Name = FixNaming(plugin.Metadata.Name),
                Guid = plugin.Metadata.GUID,
                Version = plugin.Metadata.Version.ToString(),
            };

            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in plugin.Instance.Config)
            {
                ConfigEntryBase entry = pair.Value;
                object[]? tags = entry.Description?.Tags;
                if (tags != null && Array.IndexOf(tags, "Hidden") >= 0)
                {
                    continue;
                }

                if (Overview.OverviewHotkeyAdapter.Create(entry) != null)
                    mod.Hotkeys.Add((entry.Definition.Section, entry.Definition.Key, Loc.HotkeyDescriptionKey(mod.Guid, entry.Definition.Section, entry.Definition.Key)));

                if (!IsSupported(entry))
                {
                    continue;
                }

                List<string> texts = SectionTexts(mod, entry.Definition.Section);
                texts.Add(entry.Definition.Key);
                string description = ModConfigDescriptionPanel.FormatDescription(entry);
                if (description.Length > 0)
                {
                    texts.Add(description);
                }

                foreach (string choice in Choices(entry))
                {
                    texts.Add(choice);
                }
            }

            if (mod.Sections.Count > 0 || mod.Hotkeys.Count > 0)
            {
                mod.Description = ModConfigDescriptionPanel.Panel.ReadModDescription(plugin.Location, showFallback: false);
                result.Add(mod);
            }
        }

        return result;
    }

    private static List<string> SectionTexts(ModTexts mod, string section)
    {
        foreach ((string Section, List<string> Texts) item in mod.Sections)
        {
            if (item.Section == section)
            {
                return item.Texts;
            }
        }

        List<string> texts = new List<string>();
        mod.Sections.Add((section, texts));
        return texts;
    }

    /// <summary>与 ModConfig.ProcessModEntries 的分支一致：其余类型它会记 Missing SettingType 且不显示。</summary>
    private static bool IsSupported(ConfigEntryBase entry)
    {
        Type type = entry.SettingType;
        return type == typeof(bool) || type == typeof(float) || type == typeof(double) || type == typeof(int) ||
               type == typeof(string) || type == typeof(KeyCode) || type == typeof(KeyboardShortcut) || type.IsEnum;
    }

    /// <summary>下拉框里显示的选项文字：枚举名，或字符串型的 AcceptableValueList。KeyCode 走按键控件，不是下拉框。</summary>
    private static IEnumerable<string> Choices(ConfigEntryBase entry)
    {
        if (entry.SettingType == typeof(KeyCode))
        {
            return Array.Empty<string>();
        }

        if (entry.SettingType.IsEnum)
        {
            // InputSystem 的 Key / GamepadButton 之类枚举（peakReconnect 用它存按键）也是一百多个键名，和字符串型按键清单同样跳过。
            string[] names = Enum.GetNames(entry.SettingType);
            return LooksLikeKeyList(names) ? Array.Empty<string>() : names;
        }

        if (entry.SettingType == typeof(string) && entry.Description?.AcceptableValues is AcceptableValueList<string> list)
        {
            return LooksLikeKeyList(list.AcceptableValues) ? Array.Empty<string>() : list.AcceptableValues;
        }

        return Array.Empty<string>();
    }

    private static HashSet<string>? _keyNames;

    /// <summary>KeyCode、InputSystem 的 Key / GamepadButton 的全部名字：用来认出「用字符串存按键」的模组（如 peakReconnect）。</summary>
    private static HashSet<string> KeyNames
    {
        get
        {
            if (_keyNames == null)
            {
                _keyNames = new HashSet<string>(Enum.GetNames(typeof(KeyCode)), StringComparer.OrdinalIgnoreCase);
                foreach (string typeName in new[] { "UnityEngine.InputSystem.Key", "UnityEngine.InputSystem.LowLevel.GamepadButton" })
                {
                    Type? type = HarmonyLib.AccessTools.TypeByName(typeName);
                    if (type != null && type.IsEnum)
                    {
                        _keyNames.UnionWith(Enum.GetNames(type));
                    }
                }
            }

            return _keyNames;
        }
    }

    /// <summary>超过 40 项、或八成以上是按键名的选项清单，不是给人翻的（屏幕上也只显示当前值）。</summary>
    private static bool LooksLikeKeyList(string[] values)
    {
        if (values.Length > MaxChoices)
        {
            return true;
        }

        if (values.Length < 4)
        {
            return false;
        }

        int hits = 0;
        foreach (string value in values)
        {
            if (KeyNames.Contains(value))
            {
                hits++;
            }
        }

        return hits * 5 >= values.Length * 4;
    }

    /// <summary>含中日韩统一表意文字：对中文玩家来说已无需翻译（查漏翻模式下按「无需」计，不列出）。</summary>
    private static bool ContainsCjk(string text)
    {
        foreach (char c in text)
        {
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x3000 && c <= 0x303F) || (c >= 0xFF00 && c <= 0xFFEF))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>照抄 ModConfigPlugin.FixNaming：camelCase 拆词、压空白、缩写点号复原。</summary>
    private static string FixNaming(string input)
    {
        input = Regex.Replace(input, "([a-z])([A-Z])", "$1 $2");
        input = Regex.Replace(input, "([A-Z])([A-Z][a-z])", "$1 $2");
        input = Regex.Replace(input, "\\s+", " ");
        input = Regex.Replace(input, "([A-Z]\\.)\\s([A-Z]\\.)", "$1$2");
        return input.Trim();
    }

    // ── 写文件 ──────────────────────────────────────────────────────

    private static string Write(List<ModTexts> mods, bool untranslatedOnly, Func<string, bool> isTranslated, out int written, out int skipped, out int noNeed)
    {
        Directory.CreateDirectory(ExportDirectory);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string file = Path.Combine(ExportDirectory,
            (untranslatedOnly
                ? Loc.T("Untranslated_", "未翻译_", "未翻譯_")
                : Loc.T("ToTranslate_", "待翻译_", "待翻譯_")) + stamp + ".txt");

        StringBuilder sb = new StringBuilder();
        string L(string en, string zhCn, string zhTw) => Loc.T(en, zhCn, zhTw);
        void Line(string text) => sb.Append(text).Append('\n');

        Line("// ==============================================================");
        Line("//   " + L("In-Game Mod Manager", "游戏内模组管理器", "遊戲內模組管理器") + " " + Plugin.PluginVersion + " · " + (untranslatedOnly
            ? L("untranslated texts", "未翻译文本", "未翻譯文字")
            : L("texts to translate", "待翻译文本", "待翻譯文字")) + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        Line("// ==============================================================");
        Line("//");
        Line(L("//   ★ Format: one entry per line — \"original=translation\". Left of = is the original text; write your translation on the right.",
               "//   ★ 基本格式：每行一条「原文=译文」—— = 左边是原文，右边写译文。",
               "//   ★ 基本格式：每行一條「原文=譯文」—— = 左邊是原文，右邊寫譯文。"));
        Line(L("//   ★ Never touch the left side of =: it is the key the dictionary matches character-for-character against on-screen text. Change one character (including writing \\= as =) and the entry can never match — the translation is wasted.",
               "//   ★ 永远不要碰 = 左边：它是词典用来逐字匹配屏幕文字的键，改动任何一个字符（包括把 \\= 写成 =），这条译文就再也对不上原文、等于白翻。",
               "//   ★ 永遠不要碰 = 左邊：它是詞典用來逐字比對螢幕文字的鍵，改動任何一個字元（包括把 \\= 寫成 =），這條譯文就再也對不上原文、等於白翻。"));
        Line(L("//   ★ Inside the original, = and newlines are escaped as \\= and \\n, so a line may contain many \\= (e.g. \"Hold\\=Hold to show; Toggle\\=…flag.=\"). The real separator is the first = without a backslash — in a well-formed export, the rightmost one. Use \\= and \\n in your translation too.",
               "//   ★ 原文里的 = 和换行已转义成 \\= 和 \\n，一条里可能有很多个 \\=（如 \"Hold\\=按住显示；Toggle\\=切换…flag.=\"）；真正的分隔符是第一个不带 \\ 的 =，正常导出时就是最右边那个。译文里要用 = 或换行，同样写 \\= 和 \\n。",
               "//   ★ 原文裡的 = 和換行已轉義成 \\= 和 \\n，一條裡可能有很多個 \\=（如 \"Hold\\=按住顯示；Toggle\\=切換…flag.=\"）；真正的分隔符是第一個不帶 \\ 的 =，正常匯出時就是最右邊那個。譯文裡要用 = 或換行，同樣寫 \\= 和 \\n。"));
        Line("//");
        Line(L("//   How to use: fill in the right side, save, then click \"Import additions\" in the language row of the settings page — the filled lines are merged into the current language file (language\\<code>.txt). See BepInEx\\config\\InGameModManager\\readme.md.",
               "//   用法：填好等号右边并保存，在设置页语言行点「导入补翻」，填好的行会合进当前语言文件（language\\<语言码>.txt）。详见 BepInEx\\config\\InGameModManager\\readme.md。",
               "//   用法：填好等號右邊並儲存，在設定頁語言列點「匯入補翻」，填好的行會合進目前語言檔案（language\\<語言碼>.txt）。詳見 BepInEx\\config\\InGameModManager\\readme.md。"));
        Line(L("//   Regex rules are supported in language files: r:\"^(\\d+) players$\"=$1 名玩家 (same format as XUnity, $1 refers to the first group).",
               "//   语言文件支持正则规则：r:\"^(\\d+) players$\"=$1 名玩家（与 XUnity 同格式，$1 引用第一个括号）。",
               "//   語言檔案支援正則規則：r:\"^(\\d+) players$\"=$1 名玩家（與 XUnity 同格式，$1 引用第一個括號）。"));
        Line("//");
        Line(L("// # Rules for translators / AI", "// # 给翻译者 / AI 的规则", "// # 給翻譯者 / AI 的規則"));
        Line("//");
        Line(L("//   - // lines are comments. \"// ==== Mod (GUID version) ====\" and \"// -- [Section]\" are block markers: don't translate or delete them.",
               "//   · // 开头是注释；「// ==== 模组名 (GUID 版本) ====」与「// -- [分区]」只是分块标记，不用翻、不要删。",
               "//   · // 開頭是註解；「// ==== 模組名 (GUID 版本) ====」與「// -- [分區]」只是分塊標記，不用翻、不要刪。"));
        Line(L("//   - \\n is a line break, \\= an equals sign - keep the same notation. Copy numbers, units, key names, mod names, GUIDs and paths as-is.",
               "//   · \\n 是换行、\\= 是等号，译文保持同样写法；数字、单位、按键名、模组名、GUID、路径照抄。",
               "//   · \\n 是換行、\\= 是等號，譯文保持同樣寫法；數字、單位、按鍵名、模組名、GUID、路徑照抄。"));
        Line(L("//   - Single key / enum names (A, F1, auto, eu, Left) are dropdown choices: usually copy or leave empty.",
               "//   · 单个键名 / 枚举值（A、F1、auto、eu、Left）是下拉框里的选项，一般照抄或留空。",
               "//   · 單個鍵名 / 列舉值（A、F1、auto、eu、Left）是下拉框裡的選項，一般照抄或留空。"));
        Line(L("//   - Each original appears once (one translation serves every place it shows up); repeats in later mods are not listed.",
               "//   · 同一句原文只出现一次（一条译文对所有出现的地方都生效），后面模组里重复的不再列出。",
               "//   · 同一句原文只出現一次（一條譯文對所有出現的地方都生效），後面模組裡重複的不再列出。"));
        Line(L("//   - Skipped automatically: KeyCode / key-name lists and choice lists longer than " + MaxChoices + " items.",
               "//   · 已自动跳过：KeyCode / 按键名清单、超过 " + MaxChoices + " 项的选项清单。已是中文或中英双语的行留空即可。",
               "//   · 已自動跳過：KeyCode / 按鍵名清單、超過 " + MaxChoices + " 項的選項清單。已是中文或中英雙語的行留空即可。"));
        if (untranslatedOnly || ModConfigSidebarWidgets.ExportSelectedOnly)
        {
            Line("//");
            Line(L("// # About this export", "// # 本次导出", "// # 本次導出"));
            Line("//");
            if (untranslatedOnly)
            {
                Line(L("//   Untranslated only: checked against the active language group (exact lines and r: regex rules).",
                       "//   只含未翻译条目：按当前语言组判断（整句译文与 r: 正则规则都计入）。",
                       "//   只含未翻譯條目：按目前語言組判斷（整句譯文與 r: 正則規則都計入）。"));
                Line(L("//   Originals that already contain CJK characters count as \"no need\" and are not listed.",
                       "//   已含中文的原文按「无需翻译」计，不列出。",
                       "//   已含中文的原文按「無需翻譯」計，不列出。"));
            }

            if (ModConfigSidebarWidgets.ExportSelectedOnly)
            {
                Line(L("//   Selected only: the " + mods.Count + " mods ticked in the Export column.",
                       "//   仅导出已选：左栏「导出」列勾选的 " + mods.Count + " 个模组。",
                       "//   僅導出已選：左欄「導出」列勾選的 " + mods.Count + " 個模組。"));
            }
        }

        sb.Append('\n');

        // 本模组界面词条：键是稳定 ID，等号右边预填英文，翻完这段就是本模组的语言文件
        Line(L("// ==== In-Game Mod Manager UI (MCE_ keys: translate the right side only, never the key) ====",
               "// ==== 游戏内模组管理器界面（MCE_ 键：只翻等号右边，键不要动）====",
               "// ==== 遊戲內模組管理器介面（MCE_ 鍵：只翻等號右邊，鍵不要動）===="));
        HashSet<string>? translatedMceTerms = untranslatedOnly ? Loc.CurrentTranslatedTermKeys() : null;
        written = 0;
        skipped = 0;
        noNeed = 0;
        foreach (Loc.Term term in Loc.Terms)
        {
            if (translatedMceTerms != null && translatedMceTerms.Contains(term.Key))
            {
                skipped++;
                continue;
            }

            sb.Append(Loc.KeyPrefix).Append(term.Key).Append('=').Append(Escape(term.English)).Append('\n');
            written++;
        }

        sb.Append('\n');

        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in Loc.AllConfigTerms.Concat(Loc.AllInlineTerms))
        {
            string original = term.English.Trim();
            if (!seen.Add(original)) continue;
            if (untranslatedOnly && isTranslated(original)) { skipped++; continue; }
            sb.Append(Escape(original)).Append('=').Append(Escape(term.English)).Append('\n'); written++;
        }
        foreach (ModTexts mod in mods)
        {
            StringBuilder block = new StringBuilder();
            int blockCount = 0;
            foreach (var hotkey in mod.Hotkeys)
            {
                if (untranslatedOnly && Loc.HasHotkeyDescription(mod.Guid, hotkey.Section, hotkey.Key)) { skipped++; continue; }
                block.Append("// Hotkey short description: ").Append(Escape(mod.Name)).Append(" / [").Append(Escape(hotkey.Section)).Append("] / ").Append(Escape(hotkey.Key)).Append('\n');
                block.Append(hotkey.DescriptionKey).Append("=\n"); blockCount++;
            }
            blockCount += Emit(block, mod.Name, seen, isTranslated, untranslatedOnly, ref skipped, ref noNeed);
            blockCount += Emit(block, mod.Description, seen, isTranslated, untranslatedOnly, ref skipped, ref noNeed);
            foreach ((string Section, List<string> Texts) item in mod.Sections)
            {
                StringBuilder sectionBlock = new StringBuilder();
                int sectionCount = Emit(sectionBlock, item.Section, seen, isTranslated, untranslatedOnly, ref skipped, ref noNeed);
                foreach (string text in item.Texts)
                {
                    sectionCount += Emit(sectionBlock, text, seen, isTranslated, untranslatedOnly, ref skipped, ref noNeed);
                }

                if (sectionCount > 0)
                {
                    block.Append("// -- [").Append(item.Section).Append("]\n").Append(sectionBlock);
                    blockCount += sectionCount;
                }
            }

            if (blockCount > 0)
            {
                sb.Append("// ==== ").Append(mod.Name).Append(" (").Append(mod.Guid).Append(' ').Append(mod.Version).Append(") ====\n");
                sb.Append(block).Append('\n');
                written += blockCount;
            }
        }

        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        return file;
    }

    private static int Emit(StringBuilder sb, string text, HashSet<string> seen, Func<string, bool> isTranslated, bool untranslatedOnly, ref int skipped, ref int noNeed)
    {
        if (string.IsNullOrWhiteSpace(text) || !seen.Add(text))
        {
            return 0;
        }

        if (untranslatedOnly)
        {
            if (isTranslated(text))
            {
                skipped++;
                return 0;
            }

            if (ContainsCjk(text))
            {
                noNeed++;
                return 0;
            }
        }

        sb.Append(Escape(text)).Append("=\n");
        return 1;
    }

    // 2.5.4：与语言文件读写用同一套转义（反斜杠结尾的原文也能读回来）
    private static string Escape(string text) => Loc.Escape(text);
}
