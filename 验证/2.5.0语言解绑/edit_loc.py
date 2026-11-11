# -*- coding: utf-8 -*-
"""2.5.0 一次性改码脚本：Loc.cs 去 XUnity 同步、加正则、换导出目录。用法 python -I edit_loc.py <src 目录>"""
import io
import os
import sys

SRC = sys.argv[1]
p = os.path.join(SRC, "Loc.cs")
raw = io.open(p, "rb").read()
bom = raw[:3] == b"\xef\xbb\xbf"
s = raw.decode("utf-8-sig")
crlf = "\r\n" in s
if crlf:
    s = s.replace("\r\n", "\n")


def rep(a, b):
    global s
    n = s.count(a)
    assert n == 1, (a[:90], n)
    s = s.replace(a, b)


def cut(start_marker, end_marker):
    global s
    i = s.index(start_marker)
    j = s.index(end_marker, i)
    s = s[:i] + s[j:]


# 1. XUnity 同步整段
cut("    // ── 让 XUnity 跟着切语言", "    private static bool IsTermKey(string key)")
i = s.index("    internal static bool SameLanguage(string fileStem, string xunityLanguage)")
j = s.index("\n    }\n", i) + len("\n    }\n")
s = s[:i] + s[j:]
rep('    internal const string MirrorPrefix = "ModConfigEnhance_";\n', "")
rep('    internal static string ExportDirectory => Path.Combine(RootDirectory, "export");',
    '    /// <summary>2.5.0：导出的待翻文本（原 export\\，与整合包导出的 exports\\ 分开）。</summary>\n'
    '    internal static string ExportDirectory => Path.Combine(RootDirectory, "translation-export");\n'
    '    internal static string LegacyExportDirectory => Path.Combine(RootDirectory, "export");')

# 头注释
i = s.index("/// 语言文件：<c>BepInEx")
j = s.index("///\n/// 两种模式：", i)
s = s[:i] + (
    "/// 语言文件：<c>BepInEx\\config\\ModConfigEnhance\\language\\*.txt</c>，格式与 XUnity 词典相同（<c>键=译文</c>，// 注释，\\n \\= 转义，<c>r:\"正则\"=译文</c>）。\n"
    "/// 三类行：<c>MCE_*</c> 键行是本模组界面词条（只在想改内置文字时写）；<c>r:</c> 行是正则规则；其余是各模组文字的原文 → 译文。\n"
    "/// 2.5.0 起全部由本模组自己显示（侧栏、概览、说明面板、ModConfig 设置页），不再交给 XUnity。\n"
    "/// 文件名不以 <c>_</c> 开头的 .txt 才算语言文件；首行可写 <c>// display: 显示名</c>。说明在根目录 <c>readme.md</c>。\n"
) + s[j:]

# 2. Apply 去镜像
i = s.index("            result.Mirror = SyncXUnity(selection, preparedGroup, out int mirrored, out string? mirrorWarning);")
j = s.index("            _inlineSnapshot = preparedGroup?.Entries ?? new Dictionary<string, string>(StringComparer.Ordinal);\n            if (preparedGroup != null && preparedGroup.Paths.Count > 0)", i)
s = s[:i] + (
    "            WriteReadme();\n"
    "            if (preparedGroup != null && (preparedGroup.Regexes.Count > 0 || preparedGroup.RegexErrors.Count > 0))\n"
    "            {\n"
    "                parts.Add((F(\"  |  Regex rules: {0}\", \"  |  正则规则 {0} 条\", \"  |  正則規則 {0} 條\", preparedGroup.Regexes.Count), ModConfigDescriptionPanel.NoticeRole.Body));\n"
    "                foreach (string error in preparedGroup.RegexErrors.Take(5))\n"
    "                {\n"
    "                    result.Warning = true;\n"
    "                    parts.Add((\"\\n\" + F(\"Regex rule skipped: {0}\", \"正则规则已跳过：{0}\", \"正則規則已略過：{0}\", error), ModConfigDescriptionPanel.NoticeRole.Warning));\n"
    "                }\n"
    "            }\n\n"
) + s[j:]
rep("    /// 需要时刷新全部 LocalizedText 组件并镜像给 XUnity。",
    "    /// 需要时刷新全部 LocalizedText 组件，本模组负责显示的模组文字（侧栏、概览、说明面板、设置页）随版本号重画。")
rep('{(result.Mirror.Length > 0 ? " | " + result.Mirror : "")}', "")
rep('        internal string Mirror = "";\n', "")

# 3. 正则
rep("        internal readonly List<int> BadLines = new List<int>();\n",
    "        internal readonly List<int> BadLines = new List<int>();\n"
    "        /// <summary>2.5.0：<c>r:</c> 正则规则，按文件内与组内先后排列；匹配时后写的优先。</summary>\n"
    "        internal readonly List<RegexRule> Regexes = new List<RegexRule>();\n"
    "        /// <summary>写坏的正则（「文件名 第 n 行：原因」），跳过不用，在应用结果里提示。</summary>\n"
    "        internal readonly List<string> RegexErrors = new List<string>();\n")
rep("                continue; // 等号右边留空 = 未翻，回落\n            }\n",
    "                continue; // 等号右边留空 = 未翻，回落\n            }\n\n"
    "            if (IsRegexKey(key))\n"
    "            {\n"
    "                RegexRule? rule = TryCompileRegex(key, value, Path.GetFileName(file), lineNo, out string regexError);\n"
    "                if (rule != null) result.Regexes.Add(rule);\n"
    "                else result.RegexErrors.Add(F(\"{0} line {1}: {2}\", \"{0} 第 {1} 行：{2}\", \"{0} 第 {1} 行：{2}\", Path.GetFileName(file), lineNo, regexError));\n"
    "                continue;\n"
    "            }\n")
rep("            merged.CompleteSnapshot |= file.CompleteSnapshot;\n",
    "            merged.CompleteSnapshot |= file.CompleteSnapshot;\n"
    "            merged.Regexes.AddRange(file.Regexes);\n"
    "            merged.RegexErrors.AddRange(file.RegexErrors);\n")
rep("        if (merged.Entries.Count == 0)", "        if (merged.Entries.Count == 0 && merged.Regexes.Count == 0)")

# 4. 内建文件
rep("            Directory.CreateDirectory(ExportDirectory);\n            WriteReadme();",
    "            LanguageMigration.Run();\n            Directory.CreateDirectory(ExportDirectory);\n            WriteReadme();")
cut("    private static void WriteIfMissing(", "    private static string BuildReadme()")
i = s.index("    private static string BuildFile(")
j = s.rindex("\n}")
s = s[:i].rstrip() + "\n" + s[j:]

# 5. 配置项词条
rep('        new("Switch XUnity language with the language file", "Switch XUnity language with the language file", "语言文件同时切换 XUnity 的语言", "語言檔案同時切換 XUnity 的語言"),\n'
    '        new("Disable XUnity machine translation", "Disable XUnity machine translation", "禁用 XUnity 机翻端点", "禁用 XUnity 機翻端點"),\n',
    '        new("Warn about XUnity machine translation", "Warn about XUnity machine translation", "提醒 XUnity 在线机翻", "提醒 XUnity 線上機翻"),\n')
i = s.index("        new(Plugin.DescSwitchXUnity, Plugin.DescSwitchXUnity,")
j = s.index("    };\n", i)
s = s[:i] + (
    "        new(Plugin.DescWarnXUnity, Plugin.DescWarnXUnity,\n"
    "            \"装有 XUnity AutoTranslator 且它开着在线机翻（AutoTranslatorConfig.ini 的 Endpoint 不为空）时，每次启动第一次打开概览提醒一次，并可一键关掉它的在线机翻。翻译由本模组自己的语言文件完成，不再与 XUnity 联动。\",\n"
    "            \"裝有 XUnity AutoTranslator 且它開著線上機翻（AutoTranslatorConfig.ini 的 Endpoint 不為空）時，每次啟動第一次開啟概覽提醒一次，並可一鍵關掉它的線上機翻。翻譯由本模組自己的語言檔案完成，不再與 XUnity 連動。\"),\n"
) + s[j:]
rep("收起模组列表上方的导出与 XUnity 两排按钮", "收起模组列表上方的导出与翻译工具两排按钮")
rep("收起模組列表上方的導出與 XUnity 兩排按鈕", "收起模組列表上方的導出與翻譯工具兩排按鈕")
rep("    /// <summary>本模组 cfg 选项名 / 说明的译文：键是 cfg 里的英文原文，镜像给 XUnity 后由它翻设置列表。</summary>",
    "    /// <summary>本模组 cfg 选项名 / 说明的内置译文：键是 cfg 里的英文原文，设置页与说明面板按它显示。</summary>")

if crlf:
    s = s.replace("\n", "\r\n")
io.open(p, "w", encoding="utf-8-sig" if bom else "utf-8", newline="").write(s)
print("Loc.cs ok")
