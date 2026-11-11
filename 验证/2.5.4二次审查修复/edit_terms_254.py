# -*- coding: utf-8 -*-
"""2.5.4：改了含义的 7 个界面词条换新键名（_V2），不再列为退役；DropRetired 只删精确列出的旧原文；导入补翻跳过占位行。
用法 python -I edit_terms_254.py <src>"""
import io
import os
import sys

SRC = sys.argv[1]
KEYS = ["NOTICE_SHOWING_ORIGINAL", "NOTICE_RELOADED", "TIP_UNTRANSLATED", "TIP_TOGGLE_ORIG", "TIP_RELOAD", "TIP_OPEN_DIR", "TIP_EXPORT"]


def edit(rel, fn):
    p = os.path.join(SRC, rel)
    raw = io.open(p, "rb").read()
    bom = raw[:3] == b"\xef\xbb\xbf"
    s = raw.decode("utf-8-sig")
    crlf = "\r\n" in s
    s = s.replace("\r\n", "\n")
    s = fn(s)
    if crlf:
        s = s.replace("\n", "\r\n")
    io.open(p, "w", encoding="utf-8-sig" if bom else "utf-8", newline="").write(s)
    print("ok", rel)


def rep(s, a, b, n=1):
    assert s.count(a) == n, (a[:80], s.count(a))
    return s.replace(a, b)


def loc(s):
    for k in KEYS:
        s = rep(s, '        new("' + k + '",', '        new("' + k + '_V2",')
    # 退役名单：只留真正删掉的 5 个 + 两个旧 cfg 开关
    a = ('        "NOTICE_NO_XUNITY_CHECK", "NOTICE_NO_XUNITY", "NOTICE_XUNITY_SWITCHED", "NOTICE_XUNITY_SYNCED", "NOTICE_XUNITY_NO_SWITCH",\n'
         '        "NOTICE_SHOWING_ORIGINAL", "NOTICE_RELOADED", "TIP_UNTRANSLATED", "TIP_TOGGLE_ORIG", "TIP_RELOAD", "TIP_OPEN_DIR", "TIP_EXPORT",\n')
    b = ('        "NOTICE_NO_XUNITY_CHECK", "NOTICE_NO_XUNITY", "NOTICE_XUNITY_SWITCHED", "NOTICE_XUNITY_SYNCED", "NOTICE_XUNITY_NO_SWITCH",\n')
    s = rep(s, a, b)
    s = rep(s, "    /// 2.5.1：2.5.0 改了含义（或已删除）的界面词条与 cfg 原文。",
            "    /// 2.5.1：2.5.0 已删除的界面词条与旧 cfg 开关名（2.5.4 起只放真正删掉的；改了含义的词条换新键名 _V2，旧键自然不再被用到，\n"
            "    /// 日文等其他语言和整合包作者给新键写的译文照常生效）。")
    return s


def export(s):
    for k in KEYS:
        s = s.replace('"' + k + '"', '"' + k + '_V2"')
    return s


edit("Loc.cs", loc)
edit("ModConfigTranslationExport.cs", export)

# DropRetired：只删 MCE_ 退役键 + 精确的旧 cfg 原文（2.4.x MCE 自己的旧开关与旧说明）
OLD_CFG = [
    "Switch XUnity language with the language file",
    "Disable XUnity machine translation",
]


def imp(s):
    a = ('                    if (key.StartsWith(Loc.KeyPrefix, StringComparison.OrdinalIgnoreCase)) key = key.Substring(Loc.KeyPrefix.Length).ToUpperInvariant();\n'
         '                    return Loc.RetiredTerm(key) || key.IndexOf("XUnity", StringComparison.Ordinal) >= 0 && !key.StartsWith("Warn about XUnity", StringComparison.Ordinal);')
    b = ('                    bool termKey = key.StartsWith(Loc.KeyPrefix, StringComparison.OrdinalIgnoreCase);\n'
         '                    if (termKey) key = key.Substring(Loc.KeyPrefix.Length).ToUpperInvariant();\n'
         '                    // 2.5.4（二次审查）：只删 MCE 自己的退役词条与旧版 MCE 的 cfg 原文（精确清单），不按「含 XUnity」模糊删，\n'
         '                    // 免得误删新提醒文字、别的模组提到 XUnity 的选项、或用户给 XUnity 起的译名。\n'
         '                    return termKey ? Loc.RetiredTerm(key) : RetiredOriginal(key);')
    s = rep(s, a, b)
    a = "    /// <summary>旧版首次运行写出的界面词条快照"
    b = ("    /// <summary>旧版 MCE 自己的 cfg 选项名与说明原文（2.4.x，提到 XUnity 联动），屏幕上已不再出现。</summary>\n"
         "    private static bool RetiredOriginal(string key) => Loc.RetiredTerms.Contains(key)\n"
         "        || key.StartsWith(\"Picking a language file also switches XUnity\", StringComparison.Ordinal)\n"
         "        || key.StartsWith(\"When a language file is picked (or the game language changes while following it), switch XUnity\", StringComparison.Ordinal)\n"
         "        || key.StartsWith(\"ON by default. Completely blocks XUnity machine-translation\", StringComparison.Ordinal)\n"
         "        || key.StartsWith(\"ON by default. While XUnity AutoTranslator is installed, every startup empties\", StringComparison.Ordinal)\n"
         "        || key.StartsWith(\"OFF by default. When on, every startup empties Endpoint\", StringComparison.Ordinal)\n"
         "        || key.StartsWith(\"While XUnity is installed, empty Endpoint\", StringComparison.Ordinal)\n"
         "        || key.StartsWith(\"Above the mod list, hide the export and XUnity button rows\", StringComparison.Ordinal)\n"
         "        || key.StartsWith(\"Toolbar rows in the left column: Export texts / Untranslated / Selected only\", StringComparison.Ordinal);\n\n"
         + a)
    s = rep(s, a, b)
    # 标记升级：v3 已在 2.5.1 写过的现场再按新清单跑一次没有意义（旧规则删的更多），保持 v3
    # 导入补翻：跳过占位行
    a = ("        foreach (var (key, value) in pairs)\n"
         "        {\n"
         "            string text = Escape(key) + \"=\" + Escape(value);")
    b = ("        foreach (var (key, value) in pairs)\n"
         "        {\n"
         "            // 2.5.4（二次审查）：完整导出时等号右边预填了英文，用户没翻的行原样留着——这类「译文等于原文 / 等于内置英文」的行不导入，\n"
         "            // 否则会把内置中文盖成英文。\n"
         "            if (IsPlaceholder(key, value)) { result.Unchanged++; continue; }\n"
         "            string text = Escape(key) + \"=\" + Escape(value);")
    s = rep(s, a, b)
    a = "        internal int Added, Replaced, Empty;"
    b = "        internal int Added, Replaced, Empty, Unchanged;"
    s = rep(s, a, b)
    a = "    internal static string DisplayFor(string stem)"
    b = ("    /// <summary>导入补翻里「没翻」的行：译文与原文相同；或 MCE_ 键的译文与内置英文相同。</summary>\n"
         "    private static bool IsPlaceholder(string key, string value)\n"
         "    {\n"
         "        if (string.Equals(key.Trim(), value.Trim(), StringComparison.Ordinal)) return true;\n"
         "        if (!key.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase)) return false;\n"
         "        string id = key.Substring(KeyPrefix.Length).ToUpperInvariant();\n"
         "        foreach (Term term in Terms)\n"
         "            if (term.Key == id) return string.Equals(term.English, value, StringComparison.Ordinal);\n"
         "        return false;\n"
         "    }\n\n" + a)
    s = rep(s, a, b)
    return s


edit("Loc.Import.cs", imp)
