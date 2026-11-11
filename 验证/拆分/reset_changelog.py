# -*- coding: utf-8 -*-
"""拆分 1.0.0：归档开发期更新日志，写首发说明。
· CHANGELOG.md 整份 → 备份/CHANGELOG_开发期_ModConfigEnhance-2.x_20261111.md，新 CHANGELOG 只有 1.0.0
· README.md 的「## Release notes / 版本说明」之后整段 → 备份/README_版本说明_开发期_20261111.md，README 留首发说明
· Help/<语言>/changelog.md 整份 → 备份/Help_changelog_开发期_<语言>_20261111.md，换成 1.0.0（锚点 #top 保留）
用法：python -I reset_changelog.py <工程根> [--apply]"""
import io
import os
import sys

root = sys.argv[1]
apply = "--apply" in sys.argv
backup = os.path.join(root, "备份")
stamp = "20261111"


def read(p):
    return io.open(p, encoding="utf-8-sig").read()


def write(p, t):
    io.open(p, "w", encoding="utf-8", newline="\n").write(t)


CHANGELOG = """# Changelog

## 1.0.0 — 2026-11-11

First release of **In-Game Mod Manager** (游戏内模组管理器): manage PEAK mods, settings, hotkeys, translation, store updates and modpacks without leaving the game.

- **Mod overview:** every mod in coloured, nestable groups with version, author, source, dependencies and status chips; enable and disable with a tick; update, install and uninstall from the Thunderstore store; organize the plugins folder by group (it only moves folders, never renames them).
- **Settings:** all mod settings in one tree sidebar with search, pinning and a description panel; every hotkey on one page with duplicates marked; changes go into a draft and are saved together.
- **Profiles and modpacks:** save complete setups as profiles, keep several modpacks in a library and switch between them with a backup; write a modpack introduction with images and video; export a local package or a Thunderstore package.
- **Translation:** English, Simplified and Traditional Chinese built in; export untranslated texts, fill them in and import them back; regex rules for sentences that differ only by numbers. XUnity AutoTranslator is not needed.
- **Works with PEAK 3.1:** mod decimal settings display and accept decimals again.

### Coming from ModConfig Enhance

In-Game Mod Manager grew out of ModConfig Enhance and now ships as its own mod. ModConfig Enhance stays available as the lightweight settings-page version.

- On the first launch your settings, language files, profiles and introductions are **copied** from `BepInEx/config/ModConfigEnhance/` to `BepInEx/config/InGameModManager/`. The original folder is left untouched for ModConfig Enhance. Old modpack exports are not copied; the log says where they are.
- The working folder `BepInEx/MCE/` (update queue, modpack library, store records) is renamed to `BepInEx/IGMM/`.
- Modpacks, author data (`MCEPack` folders), language files with `MCE_` keys and environments published by ModConfig Enhance are still read.
- Running both at once changes the settings page twice. Both show a reminder; disable one of them.
"""

README_NOTES = """## Release notes / 版本说明

## 1.0.0 首发

游戏内模组管理器（In-Game Mod Manager）第一个正式版本：不退出游戏就能管理 PEAK 的模组、设置、快捷键、翻译、商店更新和整合包。

**从 ModConfig Enhance 过来的用户：**

- 第一次启动会把 `BepInEx/config/ModConfigEnhance/` 里的设置、语言文件、配置方案和介绍**复制**到 `BepInEx/config/InGameModManager/`，原文件夹保持不动，留给 ModConfig Enhance。旧的整合包导出文件不复制，日志里会写出它们的位置。
- 工作目录 `BepInEx/MCE/`（更新队列、整合包库、商店记录）改名为 `BepInEx/IGMM/`。
- ModConfig Enhance 发布的整合包、作者资料（`MCEPack` 文件夹）、带 `MCE_` 键的语言文件仍然可以读取。
- 两个一起开会把设置页改两遍，两边都会提醒，停用其中一个即可。

更早的开发版说明已归档。
"""

HELP = {
    "zh-cn": """# 更新日志 {#top}

## 1.0.0 — 首发 {#v1-0-0}

游戏内模组管理器的第一个正式版本。

- **模组概览**：所有模组按可嵌套的彩色分组排列，显示版本、作者、来源、依赖和状态角标；勾选即可启停；直接从雷霆商店更新、安装、卸载；可按分组整理 plugins 文件夹（只移动、不改名）。
- **模组设置**：所有模组的设置集中在一棵树里，可搜索、可置顶，配有说明面板；快捷键集中在一页，重复按键会标出；改动先进草稿，一起保存。
- **配置方案与整合包**：把整套设置存成方案；在整合包库里保存多套环境、带备份地切换；用图片和视频写整合包介绍；导出本地包或雷霆商店包。
- **翻译**：内置英文、简体中文、繁体中文；导出待翻文本，填好再导入；只差数字的句子可以写正则规则。不需要 XUnity AutoTranslator。
- **适配 PEAK 3.1**：模组的小数参数恢复正常显示和输入。

### 从 ModConfig Enhance 过来 {#from-mce}

- 第一次启动会把 `BepInEx/config/ModConfigEnhance/` 里的设置、语言文件、配置方案和介绍**复制**到 `BepInEx/config/InGameModManager/`，原文件夹不动，留给 ModConfig Enhance。旧的整合包导出文件不复制，日志里会写出位置。
- 工作目录 `BepInEx/MCE/` 改名为 `BepInEx/IGMM/`。
- ModConfig Enhance 发布的整合包、作者资料（`MCEPack` 文件夹）和带 `MCE_` 键的语言文件仍然能读。
- 两个一起开会把设置页改两遍，两边都会提醒；停用其中一个即可。
""",
    "zh-tw": """# 更新日誌 {#top}

## 1.0.0 — 首發 {#v1-0-0}

遊戲內模組管理器的第一個正式版本。

- **模組概覽**：所有模組按可巢狀的彩色分組排列，顯示版本、作者、來源、依賴和狀態角標；勾選即可啟停；直接從雷霆商店更新、安裝、解除安裝；可按分組整理 plugins 資料夾（只移動、不改名）。
- **模組設定**：所有模組的設定集中在一棵樹裡，可搜尋、可置頂，配有說明面板；快捷鍵集中在一頁，重複按鍵會標出；改動先進草稿，一起儲存。
- **配置方案與整合包**：把整套設定存成方案；在整合包庫裡儲存多套環境、帶備份地切換；用圖片和影片寫整合包介紹；匯出本機包或雷霆商店包。
- **翻譯**：內建英文、簡體中文、繁體中文；匯出待翻文字，填好再匯入；只差數字的句子可以寫正則規則。不需要 XUnity AutoTranslator。
- **適配 PEAK 3.1**：模組的小數參數恢復正常顯示和輸入。

### 從 ModConfig Enhance 過來 {#from-mce}

- 第一次啟動會把 `BepInEx/config/ModConfigEnhance/` 裡的設定、語言檔案、配置方案和介紹**複製**到 `BepInEx/config/InGameModManager/`，原資料夾不動，留給 ModConfig Enhance。舊的整合包匯出檔案不複製，日誌裡會寫出位置。
- 工作目錄 `BepInEx/MCE/` 改名為 `BepInEx/IGMM/`。
- ModConfig Enhance 發布的整合包、作者資料（`MCEPack` 資料夾）和帶 `MCE_` 鍵的語言檔案仍然能讀。
- 兩個一起開會把設定頁改兩遍，兩邊都會提醒；停用其中一個即可。
""",
    "en-us": """# Changelog {#top}

## 1.0.0 — First release {#v1-0-0}

The first release of In-Game Mod Manager.

- **Mod overview:** every mod in coloured, nestable groups with version, author, source, dependencies and status chips; enable and disable with a tick; update, install and uninstall straight from the Thunderstore store; organize the plugins folder by group (it only moves folders, never renames them).
- **Settings:** all mod settings in one tree with search, pinning and a description panel; every hotkey on one page with duplicates marked; changes go into a draft and are saved together.
- **Profiles and modpacks:** save a whole setup as a profile; keep several environments in the modpack library and switch with a backup; write a modpack introduction with images and video; export a local package or a Thunderstore package.
- **Translation:** English, Simplified and Traditional Chinese built in; export untranslated texts, fill them in and import them back; regex rules for sentences that differ only by numbers. XUnity AutoTranslator is not needed.
- **PEAK 3.1 support:** mod decimal settings display and accept decimals again.

### Coming from ModConfig Enhance {#from-mce}

- On the first launch, settings, language files, profiles and introductions are **copied** from `BepInEx/config/ModConfigEnhance/` to `BepInEx/config/InGameModManager/`. The original folder is left for ModConfig Enhance. Old modpack exports are not copied; the log says where they are.
- The working folder `BepInEx/MCE/` is renamed to `BepInEx/IGMM/`.
- Modpacks, author data (`MCEPack` folders) and language files with `MCE_` keys published by ModConfig Enhance are still read.
- Running both changes the settings page twice. Both show a reminder; disable one of them.
""",
}

plan = []
cl = os.path.join(root, "CHANGELOG.md")
plan.append((cl, os.path.join(backup, f"CHANGELOG_开发期_ModConfigEnhance-2.x_{stamp}.md"), read(cl), CHANGELOG))
rd = os.path.join(root, "README.md")
t = read(rd)
marker = "## Release notes / 版本说明"
assert t.count(marker) == 1
head, tail = t.split(marker, 1)
plan.append((rd, os.path.join(backup, f"README_版本说明_开发期_{stamp}.md"), marker + tail, head + README_NOTES))
for lang, body in HELP.items():
    p = os.path.join(root, "Help", lang, "changelog.md")
    plan.append((p, os.path.join(backup, f"Help_changelog_开发期_{lang}_{stamp}.md"), read(p), body))
for target, archive, old, new in plan:
    print(os.path.relpath(target, root), len(old), "->", len(new), "archive", os.path.relpath(archive, root))
    if apply:
        os.makedirs(backup, exist_ok=True)
        assert not os.path.exists(archive), archive
        write(archive, old)
        write(target, new)
