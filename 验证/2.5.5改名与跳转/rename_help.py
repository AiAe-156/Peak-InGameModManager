# -*- coding: utf-8 -*-
"""2.5.5：三语使用说明里指本模组的 MCE → 管理器 / the manager。
路径（BepInEx/MCE、MCEPack、config/ModConfigEnhance、%LOCALAPPDATA%/ModConfigEnhance）和 `MCE_` 键前缀不动。
用法：python -I rename_help.py <Help目录> [--apply]   不带 --apply 只把替换后会剩下的 MCE 行写进 help_left.txt
"""
import io
import os
import re
import sys

help_dir = sys.argv[1]
apply = "--apply" in sys.argv

# 先替换整句里的固定说法（按语言），再兜底单词
PAIRS = {
    "en-us": [
        ("In-Game Mod Manager, MCE for short, puts", "In-Game Mod Manager (IGMM) puts"),
        ("[Edit the MCE guide]", "[Edit the user guide]"),
        ("## Edit the MCE guide", "## Edit the user guide"),
        ("MCE Markdown includes", "The manager's Markdown includes"),
        ("saved as an MCE document", "saved as a manager document"),
        ("under **5. Authoring** in MCE settings", "under **5. Authoring** in the manager's settings"),
        ("into the MCE project for the next build", "into the manager's project for the next build"),
        ("distributed with the MCE plugin", "distributed with the manager plugin"),
        ("only MCE and the libraries it runs on", "only the manager and the libraries it runs on"),
        ("and MCE personal runtime state", "and the manager's personal runtime state"),
        ("MCE itself and the libraries it runs on", "The manager itself and the libraries it runs on"),
        ("cannot be disabled or uninstalled from MCE", "cannot be disabled or uninstalled from the manager"),
        ("MCE finds the profile location", "the manager finds the profile location"),
        ("The components MCE runs on", "The components the manager runs on"),
        ("MCE itself is organized too", "The manager itself is organized too"),
        ("are in MCE's own settings", "are in the manager's own settings"),
        ("There are two kinds of actions in MCE:", "There are two kinds of actions in the manager:"),
        ("can be turned off in MCE's own settings", "can be turned off in the manager's own settings"),
        ("MCE uses it to tell", "The manager uses it to tell"),
        ("MCE's principle is", "The manager's principle is"),
        ("**The whitelist**: MCE itself", "**The whitelist**: the manager itself"),
        ("- MCE and the libraries it runs on always stay", "- The manager and the libraries it runs on always stay"),
        ("the first time you reach the main menu MCE tries", "the first time you reach the main menu the manager tries"),
        ("switch profiles, MCE asks", "switch profiles, the manager asks"),
        ("When you save, MCE lists", "When you save, the manager lists"),
        ("Once the source is confirmed, MCE can check", "Once the source is confirmed, the manager can check"),
        ("installed through MCE's store", "installed through the manager's store"),
        ("On first run MCE records", "On first run the manager records"),
        ("MCE checks on next launch", "the manager checks on next launch"),
        ("Every MCE screen comes in", "Every manager screen comes in"),
        ("Exit PEAK before replacing MCE.", "Exit PEAK before replacing the old version."),
        ("under MCE's own **Layout", "under the manager's own **Layout"),
    ],
    "zh-cn": [
        ("游戏内模组管理器（In-Game Mod Manager），简称 MCE。", "游戏内模组管理器（In-Game Mod Manager，简称 IGMM）。"),
        ("编辑 MCE 说明", "编辑使用说明"),
        ("编辑 MCE 使用说明", "编辑使用说明"),
        ("MCE Markdown 带有", "管理器的 Markdown 带有"),
        ("保存为 MCE 文档", "保存为管理器文档"),
        ("在 MCE 设置", "在管理器设置"),
        ("放回 MCE 工程", "放回管理器工程"),
        ("随 MCE 插件分发", "随管理器插件分发"),
        ("只有 MCE 和它运行所需", "只有管理器和它运行所需"),
        ("和 MCE 个人运行状态", "和管理器的个人运行状态"),
        ("MCE 自己和它运行所需", "管理器自己和它运行所需"),
        ("不能在 MCE 里停用", "不能在管理器里停用"),
        ("时，MCE 会自动识别", "时，管理器会自动识别"),
        ("- MCE 运行所需的", "- 管理器运行所需的"),
        ("MCE 自己也会被整理", "管理器自己也会被整理"),
        ("在 MCE 自己的设置里", "在管理器自己的设置里"),
        ("MCE 里的操作分两类", "管理器里的操作分两类"),
        ("可以在 MCE 设置里关闭", "可以在管理器设置里关闭"),
        ("MCE 用它判断", "管理器用它判断"),
        ("MCE 的原则是", "管理器的原则是"),
        ("**白名单**：MCE 自己", "**白名单**：管理器自己"),
        ("- MCE 及其运行所需的库", "- 管理器及其运行所需的库"),
        ("时，MCE 会尝试", "时，管理器会尝试"),
        ("时，MCE 会问你", "时，管理器会问你"),
        ("保存时 MCE 会列出", "保存时管理器会列出"),
        ("后，MCE 才能检查", "后，管理器才能检查"),
        ("通过 MCE 商店", "通过管理器商店"),
        ("第一次运行时 MCE 会记下", "第一次运行时管理器会记下"),
        ("下次启动 MCE 会核对", "下次启动时管理器会核对"),
        ("MCE 的全部界面有两套配色", "管理器的全部界面有两套配色"),
        ("也可在 MCE 自己的设置", "也可在管理器自己的设置"),
        ("替换 MCE 前先退出", "替换旧版前先退出"),
    ],
    "zh-tw": [
        ("遊戲內模組管理器（In-Game Mod Manager），簡稱 MCE。", "遊戲內模組管理器（In-Game Mod Manager，簡稱 IGMM）。"),
        ("編輯 MCE 說明", "編輯使用說明"),
        ("編輯 MCE 使用說明", "編輯使用說明"),
        ("MCE Markdown 帶有", "管理器的 Markdown 帶有"),
        ("儲存為 MCE 文檔", "儲存為管理器文件"),
        ("在 MCE 設定", "在管理器設定"),
        ("放回 MCE 專案", "放回管理器專案"),
        ("隨 MCE 插件分發", "隨管理器插件分發"),
        ("只有 MCE 和它執行所需", "只有管理器和它執行所需"),
        ("和 MCE 個人執行狀態", "和管理器的個人執行狀態"),
        ("MCE 自己和它執行所需", "管理器自己和它執行所需"),
        ("不能在 MCE 裡停用", "不能在管理器裡停用"),
        ("時，MCE 會自動識別", "時，管理器會自動識別"),
        ("- MCE 執行所需的", "- 管理器執行所需的"),
        ("MCE 自己也會被整理", "管理器自己也會被整理"),
        ("在 MCE 自己的設定裡", "在管理器自己的設定裡"),
        ("MCE 裡的操作分兩類", "管理器裡的操作分兩類"),
        ("可以在 MCE 設定裡關閉", "可以在管理器設定裡關閉"),
        ("MCE 用它判斷", "管理器用它判斷"),
        ("MCE 的原則是", "管理器的原則是"),
        ("**白名單**：MCE 自己", "**白名單**：管理器自己"),
        ("- MCE 及其執行所需的庫", "- 管理器及其執行所需的庫"),
        ("時，MCE 會嘗試", "時，管理器會嘗試"),
        ("時，MCE 會問你", "時，管理器會問你"),
        ("儲存時 MCE 會列出", "儲存時管理器會列出"),
        ("後，MCE 才能檢查", "後，管理器才能檢查"),
        ("通過 MCE 商店", "通過管理器商店"),
        ("第一次執行時 MCE 會記下", "第一次執行時管理器會記下"),
        ("下次啟動 MCE 會核對", "下次啟動時管理器會核對"),
        ("MCE 的全部介面有兩套配色", "管理器的全部介面有兩套配色"),
        ("也可在 MCE 自己的設定", "也可在管理器自己的設定"),
        ("替換 MCE 前先退出", "替換舊版前先退出"),
    ],
}
KEEP = re.compile(r"MCEPack|BepInEx[\\/]MCE|MCE[\\/]|`MCE_|MCE_ |MCE\.|MCEEnvironment")
left = []
for lang, pairs in PAIRS.items():
    d = os.path.join(help_dir, lang)
    for f in sorted(os.listdir(d)):
        if not f.endswith(".md") or f == "changelog.md" and False:
            continue
        p = os.path.join(d, f)
        raw = io.open(p, "rb").read()
        bom = raw.startswith(b"\xef\xbb\xbf")
        t = raw.decode("utf-8-sig")
        n = t
        for old, new in pairs:
            n = n.replace(old, new)
        for i, line in enumerate(n.split("\n"), 1):
            if re.search(r"\bMCE\b", KEEP.sub("", line)):
                left.append(f"{lang}/{f}:{i}: {line[:200]}")
        if apply and n != t:
            io.open(p, "wb").write((b"\xef\xbb\xbf" if bom else b"") + n.encode("utf-8"))
io.open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "help_left.txt"), "w", encoding="utf-8").write("\n".join(left) + "\n")
print(len(left))
