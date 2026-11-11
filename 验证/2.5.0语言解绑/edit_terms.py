# -*- coding: utf-8 -*-
"""2.5.0：Loc.cs 里描述 XUnity 联动的界面词条改成本模组自己的行为。用法 python -I edit_terms.py <src 目录>"""
import io
import os
import re
import sys

p = os.path.join(sys.argv[1], "Loc.cs")
raw = io.open(p, "rb").read()
bom = raw[:3] == b"\xef\xbb\xbf"
s = raw.decode("utf-8-sig")
crlf = "\r\n" in s
if crlf:
    s = s.replace("\r\n", "\n")


def replace_term(key, new_lines):
    """把 new("KEY", ...) 整条（可能跨多行，直到行尾 "),"）换成 new_lines。"""
    global s
    start = s.index('        new("' + key + '",')
    end = s.index("\"),\n", start) + len("\"),\n")
    s = s[:start] + new_lines + s[end:]


def drop_term(key):
    global s
    start = s.index('        new("' + key + '",')
    end = s.index("\"),\n", start) + len("\"),\n")
    s = s[:start] + s[end:]


def q(text):
    return '"' + text.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n") + '"'


def term(key, en, cn, tw):
    return "        new(" + q(key) + ",\n            " + q(en) + ",\n            " + q(cn) + ",\n            " + q(tw) + "),\n"


drop_term("NOTICE_NO_XUNITY_CHECK")
drop_term("NOTICE_NO_XUNITY")
drop_term("NOTICE_XUNITY_SWITCHED")
drop_term("NOTICE_XUNITY_SYNCED")
drop_term("NOTICE_XUNITY_NO_SWITCH")
replace_term("NOTICE_SHOWING_ORIGINAL", term("NOTICE_SHOWING_ORIGINAL",
    "Showing original texts of mods (click again to restore).",
    "正在显示模组原文（再点一次切回译文）。",
    "正在顯示模組原文（再點一次切回譯文）。"))
replace_term("NOTICE_RELOADED", term("NOTICE_RELOADED",
    "Re-read the language files from disk.",
    "已从磁盘重新读取语言文件。",
    "已從磁碟重新讀取語言檔案。"))
replace_term("TIP_UNTRANSLATED", term("TIP_UNTRANSLATED",
    "Untranslated\nExports only the entries the current language group doesn't cover yet: exact lines and `r:` regex rules both count as translated.\n\nOutput lands in `translation-export\\`; fill it in, then use `Import additions` in the language row.",
    "补漏翻\n只导出当前语言组还没覆盖的原文条目：整句译文和 `r:` 正则规则命中的都算已翻。\n\n产物落在 `translation-export\\`，填好后用语言行的`导入补翻`合进语言文件。",
    "補漏翻\n只導出目前語言組還沒覆蓋的原文條目：整句譯文和 `r:` 正則規則命中的都算已翻。\n\n產物落在 `translation-export\\`，填好後用語言列的`匯入補翻`合進語言檔案。"))
replace_term("TIP_TOGGLE_ORIG", term("TIP_TOGGLE_ORIG",
    "Show original / Show translated\nSwitches every mod text this manager displays (sidebar, overview, description panel, settings page) between translation and original — check the source before exporting, or compare.\n\nThis manager's own interface stays in the chosen language.",
    "显示原文 / 显示译文\n把本管理器负责显示的模组文字（侧栏、概览、说明面板、设置页）在译文和原文之间切换：导出前对照原文，或检查译文效果。\n\n管理器自己的界面文字保持所选语言。",
    "顯示原文 / 顯示譯文\n把本管理器負責顯示的模組文字（側欄、概覽、說明面板、設定頁）在譯文和原文之間切換：匯出前對照原文，或檢查譯文效果。\n\n管理器自己的介面文字保持所選語言。"))
replace_term("TIP_RELOAD", term("TIP_RELOAD",
    "Reload texts\nRe-reads the files in `language\\` — after editing a language file this applies it without re-picking the language.",
    "重载翻译\n重新读取 `language\\` 的语言文件：改完语言文件点它即生效，无需重选语言。",
    "重載翻譯\n重新讀取 `language\\` 的語言檔案：改完語言檔案點它即生效，無需重選語言。"))
for old, new in [("`export\\\\` (exports)", "`translation-export\\\\` (exports)"),
                 ("`export\\\\`（导出产物）", "`translation-export\\\\`（导出产物）"),
                 ("`export\\\\`（導出產物）", "`translation-export\\\\`（導出產物）"),
                 ("ModConfigEnhance\\\\export\\\\*", "ModConfigEnhance\\\\translation-export\\\\*")]:
    assert old in s, old
    s = s.replace(old, new)

# 新词条：放在 TIP_GROUPS 之前
anchor = '        new("TIP_GROUPS",'
extra = (
    term("BTN_IMPORT_ADDITIONS", "Import additions", "导入补翻", "匯入補翻")
    + term("TIP_IMPORT_ADDITIONS",
        "Import additions\nPick a filled-in export file: every line with a translation is merged into the current language file (same original → the imported one wins). Empty lines are skipped. The language file is backed up first.",
        "导入补翻\n选一个填好的导出文件：有译文的行合进当前语言文件（同一句原文以导入的为准），留空的行跳过。合并前先备份语言文件。",
        "匯入補翻\n選一個填好的匯出檔案：有譯文的行合進目前語言檔案（同一句原文以匯入的為準），留空的行略過。合併前先備份語言檔案。")
    + term("NOTICE_IMPORTED", "Merged {0} lines into {1} ({2} new, {3} replaced, {4} empty skipped).",
        "已合并 {0} 行到 {1}（新增 {2}，替换 {3}，跳过空行 {4}）。", "已合併 {0} 行到 {1}（新增 {2}，取代 {3}，略過空行 {4}）。")
    + term("NOTICE_IMPORT_NOTHING", "No translated lines in that file.", "这个文件里没有填好译文的行。", "這個檔案裡沒有填好譯文的行。")
    + term("NOTICE_IMPORT_FAILED", "Import failed: {0}", "导入失败：{0}", "匯入失敗：{0}")
)
s = s.replace(anchor, extra + anchor, 1)

if crlf:
    s = s.replace("\n", "\r\n")
io.open(p, "w", encoding="utf-8-sig" if bom else "utf-8", newline="").write(s)
print("terms ok")
