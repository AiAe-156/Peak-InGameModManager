# -*- coding: utf-8 -*-
"""2.5.0：ModConfigTranslationExport.cs 去 XUnity。用法 python -I edit_export.py <src 目录>"""
import io
import os
import sys

p = os.path.join(sys.argv[1], "ModConfigTranslationExport.cs")
raw = io.open(p, "rb").read()
bom = raw[:3] == b"\xef\xbb\xbf"
s = raw.decode("utf-8-sig")
crlf = "\r\n" in s
s = s.replace("\r\n", "\n")


def rep(a, b):
    global s
    n = s.count(a)
    assert n == 1, (a[:90], n)
    s = s.replace(a, b)


def between(start, end, new):
    global s
    i = s.index(start)
    j = s.index(end, i)
    s = s[:i] + new + s[j:]


rep("""///   · 查漏翻：只在 XUnity 在场时可用，读 BepInEx\\config\\Translation 下所有词典，只导出还没有译文的原文。""",
    """///   · 补漏翻：按当前语言组判断（整句译文或 r: 正则命中都算已翻），只导出还没有译文的原文。2.5.0 起不再需要 XUnity。""")
rep("""/// 导出文件放在 BepInEx\\config\\ModConfigEnhance\\export\\，翻好后改名扔进 Translation\\<语言>\\Text\\ 即生效。""",
    """/// 导出文件放在 BepInEx\\config\\ModConfigEnhance\\translation-export\\，填好后用语言行「导入补翻」合进当前语言文件。""")
rep("""    private static readonly Color XUnityColor = new Color(0.3918986f, 0.1843137f, 0.6235294f);\n""",
    """    private static readonly Color ToolColor = new Color(0.3918986f, 0.1843137f, 0.6235294f);\n""")
rep("""    internal static bool XUnityInstalled => XUnityBridge.Installed;\n\n""", "")
rep("""    internal static string TranslationDirectory => Path.Combine(Paths.ConfigPath, "Translation");\n\n""", "")
rep("""    /// 没装 XUnity 时查漏翻按钮灰，点了在说明面板提示。\n""", "")
rep("""        Color color = untranslatedOnly && !XUnityInstalled ? DisabledColor : (untranslatedOnly ? CheckColor : ExportColor);""",
    """        Color color = untranslatedOnly ? CheckColor : ExportColor;""")

# 第二行三个按钮：显示原文 / 重载翻译 / 打开目录
between("    /// <summary>\n    /// 第二行：XUnity 的两个运行时开关。",
        "    /// <summary>打开 config\\ModConfigEnhance\\",
        """    /// <summary>
    /// 第二行（2.5.0 起全是本模组自己的功能）：「显示原文 / 显示译文」切换本模组负责显示的模组文字；
    /// 「重载翻译」重读语言文件；「打开目录」。
    /// </summary>
    internal static void MakeTranslationTools(RectTransform page, float left, float top, float width, float height, float gap)
    {
        float third = (width - gap * 2f) / 3f;
        PeakMenuButton toggle = MakeButton(page, "MCE_OriginalToggle", ToggleKey(), ToolColor, left, top, third, height, null, UiActionRole.Edit);
        toggle.OnClick(() =>
        {
            Loc.ToggleOriginal();
            toggle.SetLocalizationIndex(Loc.Index(ToggleKey()));
            Notify(Loc.Get(Loc.ShowingOriginal ? "NOTICE_SHOWING_ORIGINAL" : "NOTICE_SHOWING_TRANSLATED"));
        });
        toggle.gameObject.AddComponent<ToggleLabel>().Button = toggle;
        toggle.gameObject.AddComponent<ModConfigSidebarWidgets.TipTarget>().Key = "TIP_TOGGLE_ORIG";

        // 重载 = 把磁盘上的语言文件重新读一遍：本模组界面词条重新应用，侧栏、概览、说明面板与设置页随版本号重画。
        PeakMenuButton reload = MakeButton(page, "MCE_ReloadTranslations", "BTN_RELOAD", ToolColor, left + third + gap, top, third, height, () =>
        {
            Loc.ApplyResult result = Loc.Apply(Plugin.LanguageFile.Value, notify: false, refresh: true);
            string message = result.Success ? Loc.Get("NOTICE_RELOADED") + "\\n" + result.Message : result.Message;
            Notify(message, result.Warning);
        });
        reload.gameObject.AddComponent<ModConfigSidebarWidgets.TipTarget>().Key = "TIP_RELOAD";

        PeakMenuButton openDir = MakeButton(page, "MCE_OpenFolder", "BTN_OPEN_FOLDER", FolderColor, left + (third + gap) * 2f, top, third, height, OpenRootFolder);
        openDir.gameObject.AddComponent<ModConfigSidebarWidgets.TipTarget>().Key = "TIP_OPEN_DIR";
    }

""")
between("    private static string ToggleKey()", "    /// <summary><paramref name=\"termKey\"/>",
        """    private static string ToggleKey()
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

""")

# 补漏翻：只查自己的语言组
between("            if (untranslatedOnly && !XUnityInstalled)", "            List<ModTexts> mods = Collect();", "")
between("            Func<string, bool> isTranslated;", "            string file = Write(",
        """            // 2.5.0：只查当前语言组（整句或 r: 正则命中都算已翻），不再需要 XUnity。
            Func<string, bool> isTranslated = untranslatedOnly ? Loc.HasTranslation : _ => false;
""")
rep("""            string file = Write(mods, untranslatedOnly, isTranslated, liveQuery, out int written, out int skipped, out int noNeed);""",
    """            string file = Write(mods, untranslatedOnly, isTranslated, out int written, out int skipped, out int noNeed);""")
rep("""    private static string Write(List<ModTexts> mods, bool untranslatedOnly, Func<string, bool> isTranslated, bool liveQuery, out int written, out int skipped, out int noNeed)""",
    """    private static string Write(List<ModTexts> mods, bool untranslatedOnly, Func<string, bool> isTranslated, out int written, out int skipped, out int noNeed)""")

# 旧词典读取整段删除
between("    // ── 已有词典 ──", "    // ── 写文件 ──", "")

# 文件头
rep("""        string lang = XUnityBridge.TargetLanguage ?? "zh";\n""", "")
rep("""        Line(L("//   How to use: see BepInEx\\\\config\\\\ModConfigEnhance\\\\readme.md (translate > save as code[-variant][_part].txt > put in language\\\\ > Refresh > pick; // display: <name> sets the dropdown label).",
               "//   用法见 BepInEx\\\\config\\\\ModConfigEnhance\\\\readme.md（翻译 → 另存为「语言码[-变体][_分包].txt」→ 放进 language\\\\ → 刷新 → 选中；首行 // display: 名字 改下拉框显示名）。",
               "//   用法見 BepInEx\\\\config\\\\ModConfigEnhance\\\\readme.md（翻譯 → 另存為「語言碼[-變體][_分包].txt」→ 放進 language\\\\ → 重新整理 → 選中；首行 // display: 名字 改下拉框顯示名）。"));
        if (untranslatedOnly)
        {
            Line(L("//   Keep existing files. Save additions as a new file such as zh-cn_20_additions.txt, then reload the same language group.",
                   "//   保留已有文件；补翻请另存为 zh-cn_20_补翻.txt，再重载同一语言组。",
                   "//   保留已有檔案；補翻請另存為 zh-tw_20_補翻.txt，再重載同一語言組。"));
        }""",
    """        Line(L("//   How to use: fill in the right side, save, then click \\"Import additions\\" in the language row of the settings page — the filled lines are merged into the current language file (language\\\\<code>.txt). See BepInEx\\\\config\\\\ModConfigEnhance\\\\readme.md.",
               "//   用法：填好等号右边并保存，在设置页语言行点「导入补翻」，填好的行会合进当前语言文件（language\\\\<语言码>.txt）。详见 BepInEx\\\\config\\\\ModConfigEnhance\\\\readme.md。",
               "//   用法：填好等號右邊並儲存，在設定頁語言列點「匯入補翻」，填好的行會合進目前語言檔案（language\\\\<語言碼>.txt）。詳見 BepInEx\\\\config\\\\ModConfigEnhance\\\\readme.md。"));
        Line(L("//   Regex rules are supported in language files: r:\\"^(\\\\d+) players$\\"=$1 名玩家 (same format as XUnity, $1 refers to the first group).",
               "//   语言文件支持正则规则：r:\\"^(\\\\d+) players$\\"=$1 名玩家（与 XUnity 同格式，$1 引用第一个括号）。",
               "//   語言檔案支援正則規則：r:\\"^(\\\\d+) players$\\"=$1 名玩家（與 XUnity 同格式，$1 引用第一個括號）。"));""")
rep("""        Line(L("//   - Each original appears once globally (the XUnity dictionary is global); repeats in later mods are not listed.",
               "//   · 同一句原文全局只出现一次（XUnity 词典是全局的），后面模组里重复的不再列出。",
               "//   · 同一句原文全域只出現一次（XUnity 詞典是全域的），後面模組裡重複的不再列出。"));""",
    """        Line(L("//   - Each original appears once (one translation serves every place it shows up); repeats in later mods are not listed.",
               "//   · 同一句原文只出现一次（一条译文对所有出现的地方都生效），后面模组里重复的不再列出。",
               "//   · 同一句原文只出現一次（一條譯文對所有出現的地方都生效），後面模組裡重複的不再列出。"));""")
between("                Line(liveQuery\n", "                Line(L(\"//   Originals that already contain CJK characters",
        """                Line(L("//   Untranslated only: checked against the active language group (exact lines and r: regex rules).",
                       "//   只含未翻译条目：按当前语言组判断（整句译文与 r: 正则规则都计入）。",
                       "//   只含未翻譯條目：按目前語言組判斷（整句譯文與 r: 正則規則都計入）。"));
""")

s = s.replace("\n", "\r\n") if crlf else s
io.open(p, "w", encoding="utf-8-sig" if bom else "utf-8", newline="").write(s)
print("export ok")
