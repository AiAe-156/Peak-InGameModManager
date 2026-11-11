# -*- coding: utf-8 -*-
"""2.5.5（用户要求纠正个人语言文件里的旧描述）：先整份备份，再删掉本管理器自己的过时条目。
只删精确列出的键；别的模组的译文、快捷键描述（ModConfigEnhance.HotkeyDescription.*）一律不动。
用法：python -I clean_language.py <language目录> <备份目录> [--apply]
"""
import io
import os
import shutil
import sys

lang_dir, backup_dir = sys.argv[1], sys.argv[2]
apply = "--apply" in sys.argv

# 个人覆盖的本管理器界面词条：内容停在旧版（查漏翻 / 个文件 / 导出文本 / 未翻译 …），删掉后用内置
STALE_TERM_KEYS = {
    "MCE_BTN_UNTRANSLATED", "MCE_SORT_LOAD_DESC", "MCE_NOTICE_REFRESHED", "MCE_TIP_SELECTED_ONLY",
    "MCE_TIP_SORT", "MCE_TIP_LANGUAGE", "MCE_TIP_REFRESH", "MCE_NOTICE_GROUPS_UNAVAILABLE",
    "MCE_TIP_GROUPS", "MCE_TIP_GROUP_HEADER",
}
# 本管理器旧版的名称、简介、cfg 选项名与说明原文（现已改名或改写，或由内置译文覆盖）
STALE_ORIGINALS = {
    "Mod Config Enhance", "ModConfig Enhance", "Translation tools",
    "ModConfig settings page enhancements: tree sidebar, description panel, translation tools, language files.",
    "Native mod overview and pack introduction editor, update checks, groups, and ModConfig settings enhancements.",
}
STALE_PREFIXES = (
    "Localize ModConfig's own UI (Search, Default, Clear, filters",
    "Translate ModConfig's own UI texts (Search, Default, Clear, filter names",
    "Turn the mod settings page into a tree: a vertical mod list on the left",
    "A panel at the top right shows the hovered option's description from its cfg",
)


def key_of(line):
    # 第一个不带反斜杠的 =
    i = 0
    while True:
        i = line.find("=", i)
        if i <= 0:
            return None
        if line[i - 1] != "\\":
            return line[:i].strip()
        i += 1


report = []
os.makedirs(backup_dir, exist_ok=True) if apply else None
for name in sorted(os.listdir(lang_dir)):
    if not name.endswith(".txt") or name.startswith("_"):
        continue
    path = os.path.join(lang_dir, name)
    raw = io.open(path, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    lines = text.split("\n")
    keep, removed = [], []
    for line in lines:
        k = key_of(line) if not line.startswith("//") else None
        if k is not None and (k in STALE_TERM_KEYS or k.replace("\\=", "=") in STALE_ORIGINALS or k.startswith(STALE_PREFIXES)):
            removed.append(line)
        else:
            keep.append(line)
    report.append(f"== {name}: {len(removed)} 行")
    report.extend("   - " + r[:160] for r in removed)
    if apply and removed:
        shutil.copy2(path, os.path.join(backup_dir, name))
        io.open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + "\n".join(keep).encode("utf-8"))
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "clean_language_report.txt")
io.open(out, "w", encoding="utf-8").write("\n".join(report) + "\n")
print(sum(1 for r in report if r.startswith("   - ")))
