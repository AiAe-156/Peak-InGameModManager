# -*- coding: utf-8 -*-
"""2.5.5：玩家看得见的 MCE / ModConfig Enhance 旧名 → 管理器 / In-Game Mod Manager。
只换完整短语（调用处与词条目录里的同一句一起换，键名、GUID、路径、日志前缀、内部匹配串不动）。
用法：python -I rename_pairs.py check <工程根>   列出 验证/ 里引用旧短语的文件（防止夹具断言失配）
      python -I rename_pairs.py apply <工程根>   写入 src / Desktop / Updater
"""
import io
import os
import sys

PAIRS = [
    # 导出 / 目录提示
    ("including MCE (`ModConfigEnhance`) itself", "including this manager itself"),
    ("包括 MCE（`ModConfigEnhance`）模组本身", "包括本管理器自身"),
    ("包括 MCE（`ModConfigEnhance`）模組本身", "包括本管理器自身"),
    # 配色主题说明（cfg 说明原文即词典键，常量与目录一起换）
    ("Colour theme of every MCE screen.", "Colour theme of every manager screen."),
    ("MCE 全部界面的配色主题", "管理器全部界面的配色主题"),
    ("MCE 全部介面的配色主題", "管理器全部介面的配色主題"),
    # 语言通知
    ("  |  MCE dictionary: ", "  |  Manager dictionary: "),
    ("已加载 MCE 词典 ", "已加载管理器词典 "),
    ("已載入 MCE 詞典 ", "已載入管理器詞典 "),
    # 文档版本
    ("Update ModConfig Enhance.", "Update In-Game Mod Manager."),
    ("请更新 ModConfig Enhance。", "请更新游戏内模组管理器。"),
    ("請更新 ModConfig Enhance。", "請更新遊戲內模組管理器。"),
    ("Not an MCE document", "Not a manager document"),
    ("不是 MCE 文档", "不是管理器文档"),
    ("不是 MCE 文檔", "不是管理器文檔"),
    # 说明页版本行：标题已写全名，这里只留版本
    ("\"MCE {0} · {1}\"", "\"v{0} · {1}\""),
    # 组件保护
    ("Required by MCE; uninstall asks for confirmation", "Required by the manager; uninstall asks for confirmation"),
    ("此组件是MCE运行所依赖的组件", "此组件是管理器运行所依赖的组件"),
    ("此組件是MCE執行所依賴的組件", "此組件是管理器執行所依賴的組件"),
    ("MCE runs on this component;", "The manager runs on this component;"),
    ("MCE runs on this component", "The manager runs on this component"),
    ("MCE 运行依赖此组件", "管理器运行依赖此组件"),
    ("MCE 執行依賴此組件", "管理器執行依賴此組件"),
    ("MCE and the libraries it runs on cannot", "The manager and the libraries it runs on cannot"),
    ("MCE 及其运行所需的基础库", "管理器及其运行所需的基础库"),
    ("MCE 及其執行所需的基礎庫", "管理器及其執行所需的基礎庫"),
    ("MCE runs on this library;", "The manager runs on this library;"),
    ("MCE 运行依赖此基础库", "管理器运行依赖此基础库"),
    ("MCE 執行依賴此基礎庫", "管理器執行依賴此基礎庫"),
    ("the loader; MCE does not change them.", "the loader; the manager does not change them."),
    ("加载器本身，MCE 不改动它们", "加载器本身，管理器不改动它们"),
    ("載入器本身，MCE 不變更它們", "載入器本身，管理器不變更它們"),
    ("Open MCE data folder", "Open manager data folder"),
    ("Keep backups separately; MCE will not delete them.", "Keep backups separately; the manager will not delete them."),
    ("Reinstall the complete MCE package.", "Reinstall the complete In-Game Mod Manager package."),
    ("请重新安装完整 MCE 包。", "请重新安装完整的游戏内模组管理器包。"),
    ("請重新安裝完整 MCE 套件。", "請重新安裝完整的遊戲內模組管理器套件。"),
    # 整合包切换
    ("; MCE will restore the previous setup.", "; the manager will restore the previous setup."),
    ("，MCE 会恢复原有配置", "，管理器会恢复原有配置"),
    ("，MCE 會恢復原有配置", "，管理器會恢復原有配置"),
    ("Nothing changes until PEAK exits. MCE then finishes the switch", "Nothing changes until PEAK exits. The manager then finishes the switch"),
    ("退出后由 MCE 完成切换", "退出后由管理器完成切换"),
    ("退出后由 MCE 完成切換", "退出后由管理器完成切換"),
    ("MCE can turn that layout into groups.", "The manager can turn that layout into groups."),
    ("(\"MCE 整合包切换\", \"MCE Modpack Switch\", \"MCE 整合包切換\")", "(\"游戏内模组管理器 · 整合包切换\", \"In-Game Mod Manager · Modpack switch\", \"遊戲內模組管理器 · 整合包切換\")"),
    ("请返回 MCE 查看", "请返回管理器查看"),
    ("請返回 MCE 查看", "請返回管理器查看"),
    ("請返回 MCE 檢視", "請返回管理器檢視"),
    ("Return to MCE for details.", "Return to the manager for details."),
    ("Return to MCE; no action", "Return to the manager; no action"),
    ("check MCE/worker status", "check the manager/worker status"),
    ("检查 MCE／后台状态", "检查管理器／后台状态"),
    ("檢查 MCE／背景狀態", "檢查管理器／背景狀態"),
    ("\"MCE · Modpack switch\", \"MCE · 整合包切换\", \"MCE · 整合包切換\"", "\"In-Game Mod Manager · Modpack switch\", \"游戏内模组管理器 · 整合包切换\", \"遊戲內模組管理器 · 整合包切換\""),
    ("\"MCE progress\", MessageBoxButton", "\"In-Game Mod Manager\", MessageBoxButton"),
    ("T(\"MCE operations\", \"MCE 模组操作\", \"MCE 模組操作\")", "T(\"In-Game Mod Manager · operations\", \"游戏内模组管理器 · 模组操作\", \"遊戲內模組管理器 · 模組操作\")"),
    ("error.Message, \"MCE operations\"", "error.Message, \"In-Game Mod Manager\""),
    # 整合包检查报告（中文报告文字）
    ("MCE 不会修改核心文件。", "管理器不会修改核心文件。"),
    ("MCE 或其依赖的版本过低", "管理器或其依赖的版本过低"),
    ("先更新 MCE（导入整合包不会升级它）", "先更新管理器（导入整合包不会升级它）"),
    ("包内含有 MCE 运行数据：", "包内含有管理器运行数据："),
    ("重新打包时排除 MCE 运行目录。", "重新打包时排除管理器运行目录（BepInEx/MCE）。"),
    # 自身设置项说明
    ("Pause completed non-looping MCE button Normal animations.", "Pause the manager's button animations once a non-looping Normal clip has finished."),
    # 日志来源名
    ("\"Mod manager / 模组管理器 (MCE)\"", "\"In-Game Mod Manager / 游戏内模组管理器\""),
    # 导出文件分块标题
    ("// ==== ModConfig Enhance UI (MCE_ keys", "// ==== In-Game Mod Manager UI (MCE_ keys"),
    ("// ==== ModConfig Enhance 界面（MCE_ 键", "// ==== 游戏内模组管理器界面（MCE_ 键"),
    ("// ==== ModConfig Enhance 介面（MCE_ 鍵", "// ==== 遊戲內模組管理器介面（MCE_ 鍵"),
]

mode, root = sys.argv[1], sys.argv[2]


def files(top):
    for d, dirs, fs in os.walk(top):
        dirs[:] = [x for x in dirs if x not in ("bin", "obj")]
        for f in fs:
            if f.endswith((".cs", ".py")):
                yield os.path.join(d, f)


if mode == "check":
    hits = {}
    for p in files(os.path.join(root, "验证")):
        if "2.5.5改名与跳转" in p:
            continue
        try:
            t = io.open(p, encoding="utf-8-sig").read()
        except UnicodeDecodeError:
            continue
        for old, _ in PAIRS:
            if old in t:
                hits.setdefault(os.path.relpath(p, root), []).append(old)
    out = io.open(os.path.join(root, "验证", "2.5.5改名与跳转", "fixture_hits.txt"), "w", encoding="utf-8")
    for k, v in sorted(hits.items()):
        out.write(k + "\n")
        for o in v:
            out.write("    " + o + "\n")
    print(len(hits))
else:
    used = {old: 0 for old, _ in PAIRS}
    changed = []
    for sub in ("src", "Desktop", "Updater"):
        for p in files(os.path.join(root, sub)):
            t = io.open(p, encoding="utf-8-sig").read()
            bom = io.open(p, "rb").read(3) == b"\xef\xbb\xbf"
            n = t
            for old, new in PAIRS:
                c = n.count(old)
                if c:
                    used[old] += c
                    n = n.replace(old, new)
            if n != t:
                data = n.encode("utf-8")
                io.open(p, "wb").write((b"\xef\xbb\xbf" if bom else b"") + data)
                changed.append(os.path.relpath(p, root))
    out = io.open(os.path.join(root, "验证", "2.5.5改名与跳转", "apply_report.txt"), "w", encoding="utf-8")
    out.write("changed:\n" + "\n".join(changed) + "\n\nunused pairs:\n")
    out.write("\n".join(o for o, c in used.items() if c == 0) + "\n")
    print(len(changed), sum(1 for c in used.values() if c == 0))
