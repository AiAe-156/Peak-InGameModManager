# -*- coding: utf-8 -*-
"""2.5.2 目录重排：所有说明里的旧路径改成新布局。用法 python -I edit_docs.py <工程根>"""
import io
import os
import sys

ROOT = sys.argv[1]
done = []


def edit(rel, pairs, all_occurrences=False):
    p = os.path.join(ROOT, rel)
    raw = io.open(p, "rb").read()
    bom = raw[:3] == b"\xef\xbb\xbf"
    s = raw.decode("utf-8-sig")
    for a, b in pairs:
        n = s.count(a)
        if n == 0 and b in s:
            continue
        assert n >= 1 if all_occurrences else n == 1, (rel, a[:90], n)
        s = s.replace(a, b)
    io.open(p, "w", encoding="utf-8-sig" if bom else "utf-8", newline="").write(s)
    done.append(rel)


# —— 三语使用说明 ——
edit("Help/en-us/authoring.md", [("`config/ModConfigEnhance/help/<language>/`", "`config/ModConfigEnhance/data/help/<language>/`")])
edit("Help/zh-cn/authoring.md", [("`config/ModConfigEnhance/help/<语言>/`", "`config/ModConfigEnhance/data/help/<语言>/`")])
edit("Help/zh-tw/authoring.md", [("`config/ModConfigEnhance/help/<語言>/`", "`config/ModConfigEnhance/data/help/<語言>/`")])
edit("Help/en-us/export.md", [("`BepInEx/config/ModConfigEnhance/exports/<timestamp>/`", "`BepInEx/config/ModConfigEnhance/output/packs/<timestamp>/`")])
edit("Help/zh-cn/export.md", [("`BepInEx/config/ModConfigEnhance/exports/<时间>/`", "`BepInEx/config/ModConfigEnhance/output/packs/<时间>/`")])
edit("Help/zh-tw/export.md", [("`BepInEx/config/ModConfigEnhance/exports/<时间>/`", "`BepInEx/config/ModConfigEnhance/output/packs/<時間>/`")])
edit("Help/en-us/faq.md", [("`BepInEx\\config\\ModConfigEnhance\\export\\`", "`BepInEx\\config\\ModConfigEnhance\\output\\logs\\`")])
edit("Help/zh-cn/faq.md", [("`BepInEx\\config\\ModConfigEnhance\\export\\`", "`BepInEx\\config\\ModConfigEnhance\\output\\logs\\`")])
edit("Help/zh-tw/faq.md", [("`BepInEx\\config\\ModConfigEnhance\\export\\`", "`BepInEx\\config\\ModConfigEnhance\\output\\logs\\`")])
edit("Help/en-us/translation.md", [("4. Open the file in `translation-export` and", "4. Open the file in `output/translation` and")])
edit("Help/zh-cn/translation.md", [("4. 打开 `translation-export` 里的文本", "4. 打开 `output/translation` 里的文本")])
edit("Help/zh-tw/translation.md", [("4. 打開 `translation-export` 裡的文字", "4. 打開 `output/translation` 裡的文字")])

# 「文件放在哪」一节：translation.md 的词典组织段后加一段目录说明（三语，锚点 #folders）
folders = {
    "en-us": ("## Regex rules {#regex}",
              "## Where the files are {#folders}\n\n"
              "Everything the manager keeps for you is under `BepInEx/config/ModConfigEnhance/`, in four folders:\n\n"
              "- `language/`: your language files.\n"
              "- `profiles/`: configuration profiles, the modpack introduction and its media.\n"
              "- `output/`: things you export. `translation/` holds Export translations and Missing translations, `packs/` holds published modpacks and introduction exports, `logs/` holds log exports when the Save As window is unavailable.\n"
              "- `data/`: the manager's own data. `runtime/` is state and store caches, `backups/` all automatic backups, `help/` your edits to this guide, `legacy/` data from versions before October 2026.\n\n"
              "Installed modpacks and the update helper's queue and backups are in `BepInEx/MCE/`. Versions before 2.5.2 kept these folders side by side; the first launch moves them into place and backs up the state files to `BepInEx/MCE/backups/directory-layout-before-<time>/` first.\n\n"),
    "zh-cn": ("## 正则规则 {#regex}",
              "## 文件放在哪 {#folders}\n\n"
              "管理器替你保存的东西都在 `BepInEx/config/ModConfigEnhance/` 下，分四个文件夹：\n\n"
              "- `language/`：你的语言文件。\n"
              "- `profiles/`：配置方案、整合包介绍和介绍里的素材。\n"
              "- `output/`：你导出的东西。`translation/` 放「导出翻译」「补漏翻」的文件，`packs/` 放发布的整合包和介绍导出，`logs/` 放「另存为」窗口打不开时的日志导出。\n"
              "- `data/`：管理器自己的数据。`runtime/` 是运行状态和商店缓存，`backups/` 是全部自动备份，`help/` 是你改过的使用说明，`legacy/` 是 2026 年 10 月以前版本的旧数据。\n\n"
              "已装的整合包、更新助手的队列和备份在 `BepInEx/MCE/`。2.5.2 以前这些文件夹是平铺的，第一次启动时会自动搬好，搬之前先把状态文件备份到 `BepInEx/MCE/backups/directory-layout-before-<时间>/`。\n\n"),
    "zh-tw": ("## 正則規則 {#regex}",
              "## 檔案放在哪 {#folders}\n\n"
              "管理器替你儲存的東西都在 `BepInEx/config/ModConfigEnhance/` 下，分四個資料夾：\n\n"
              "- `language/`：你的語言檔案。\n"
              "- `profiles/`：設定方案、整合包介紹和介紹裡的素材。\n"
              "- `output/`：你匯出的東西。`translation/` 放「匯出翻譯」「補漏翻」的檔案，`packs/` 放發佈的整合包和介紹匯出，`logs/` 放「另存新檔」視窗打不開時的日誌匯出。\n"
              "- `data/`：管理器自己的資料。`runtime/` 是執行狀態和商店快取，`backups/` 是全部自動備份，`help/` 是你改過的使用說明，`legacy/` 是 2026 年 10 月以前版本的舊資料。\n\n"
              "已裝的整合包、更新助手的佇列和備份在 `BepInEx/MCE/`。2.5.2 以前這些資料夾是平鋪的，第一次啟動時會自動搬好，搬之前先把狀態檔案備份到 `BepInEx/MCE/backups/directory-layout-before-<時間>/`。\n\n"),
}
for lang, (anchor, text) in folders.items():
    edit("Help/" + lang + "/translation.md", [(anchor, text + anchor)])
edit("Help/en-us/translation.md", [("[Regex rules](#regex) · [XUnity AutoTranslator](#xunity)", "[Where the files are](#folders) · [Regex rules](#regex) · [XUnity AutoTranslator](#xunity)")])
edit("Help/zh-cn/translation.md", [("[正则规则](#regex) · [XUnity AutoTranslator](#xunity)", "[文件放在哪](#folders) · [正则规则](#regex) · [XUnity AutoTranslator](#xunity)")])
edit("Help/zh-tw/translation.md", [("[正則規則](#regex) · [XUnity AutoTranslator](#xunity)", "[檔案放在哪](#folders) · [正則規則](#regex) · [XUnity AutoTranslator](#xunity)")])

# —— 配置目录 readme（LanguageReadme）——
edit("src/LanguageReadme.cs", [
    ("        - `language\\` — your language files, one per language: `zh-cn.txt`, `zh-tw.txt`, `de-de.txt` …\n"
     "        - `translation-export\\` — files written by **Export texts** and **Untranslated**\n"
     "        - `readme.md` — this file",
     "        - `language\\` — your language files, one per language: `zh-cn.txt`, `zh-tw.txt`, `de-de.txt` …\n"
     "        - `profiles\\` — configuration profiles, the modpack introduction and its media\n"
     "        - `output\\` — things you export: `translation\\` (**Export texts** / **Untranslated**), `packs\\` (published modpacks), `logs\\` (log exports when Save As is unavailable)\n"
     "        - `data\\` — the manager's own data: `runtime\\` state and store caches, `backups\\` automatic backups, `help\\` your guide edits, `legacy\\` data from before October 2026\n"
     "        - `readme.md` — this file"),
    ("        - `language\\`：你的语言文件，一种语言一个：`zh-cn.txt`、`zh-tw.txt`、`de-de.txt`……\n"
     "        - `translation-export\\`：「导出翻译」「补漏翻」写出的文件\n"
     "        - `readme.md`：本说明",
     "        - `language\\`：你的语言文件，一种语言一个：`zh-cn.txt`、`zh-tw.txt`、`de-de.txt`……\n"
     "        - `profiles\\`：配置方案、整合包介绍和介绍里的素材\n"
     "        - `output\\`：你导出的东西：`translation\\`（「导出翻译」「补漏翻」）、`packs\\`（发布的整合包）、`logs\\`（「另存为」打不开时的日志导出）\n"
     "        - `data\\`：管理器自己的数据：`runtime\\` 运行状态和商店缓存、`backups\\` 自动备份、`help\\` 你改过的使用说明、`legacy\\` 2026 年 10 月以前的旧数据\n"
     "        - `readme.md`：本说明"),
    ("        - `language\\`：你的語言檔案，一種語言一個：`zh-cn.txt`、`zh-tw.txt`、`de-de.txt`……\n"
     "        - `translation-export\\`：「匯出翻譯」「補漏翻」寫出的檔案\n"
     "        - `readme.md`：本說明",
     "        - `language\\`：你的語言檔案，一種語言一個：`zh-cn.txt`、`zh-tw.txt`、`de-de.txt`……\n"
     "        - `profiles\\`：設定方案、整合包介紹和介紹裡的素材\n"
     "        - `output\\`：你匯出的東西：`translation\\`（「匯出翻譯」「補漏翻」）、`packs\\`（發佈的整合包）、`logs\\`（「另存新檔」打不開時的日誌匯出）\n"
     "        - `data\\`：管理器自己的資料：`runtime\\` 執行狀態和商店快取、`backups\\` 自動備份、`help\\` 你改過的使用說明、`legacy\\` 2026 年 10 月以前的舊資料\n"
     "        - `readme.md`：本說明"),
])

# —— 界面提示词条 ——
edit("src/Loc.cs", [
    ("ModConfigEnhance\\\\translation-export\\\\*", "ModConfigEnhance\\\\output\\\\translation\\\\*"),
    ("`translation-export\\\\`", "`output\\\\translation\\\\`"),
], all_occurrences=True)

# —— README 个人文件表 ——
edit("README.md", [
    ("| `config/ModConfigEnhance/language/` | Personal language files and overrides. |\n"
     "| `config/ModConfigEnhance/profiles/` | Existing personal profiles before adopting the installation into the pack library. |\n"
     "| `config/ModConfigEnhance/exports/` | Your exported packages; an export can use a different output folder. |\n"
     "| `config/ModConfigEnhance/backups/` | Configuration and migration backups. |",
     "| `config/ModConfigEnhance/language/` | Personal language files and overrides. |\n"
     "| `config/ModConfigEnhance/profiles/` | Existing personal profiles before adopting the installation into the pack library. |\n"
     "| `config/ModConfigEnhance/output/` | What you export: `translation/` texts to translate, `packs/` published packages (an export can use a different folder), `logs/` log exports. |\n"
     "| `config/ModConfigEnhance/data/` | The manager's own data: `runtime/` state and caches, `backups/` configuration and migration backups, `help/` guide edits, `legacy/` pre-October-2026 data. |"),
    ("| `config/ModConfigEnhance/language/` | 个人语言文件和覆盖。 |\n"
     "| `config/ModConfigEnhance/profiles/` | 首次保存临时整合包前使用的个人方案、参数、介绍及引用资源。 |\n"
     "| `config/ModConfigEnhance/exports/` | 导出包，也可在发布时另选输出目录。 |\n"
     "| `config/ModConfigEnhance/backups/` | 配置及迁移备份。 |",
     "| `config/ModConfigEnhance/language/` | 个人语言文件和覆盖。 |\n"
     "| `config/ModConfigEnhance/profiles/` | 首次保存临时整合包前使用的个人方案、参数、介绍及引用资源。 |\n"
     "| `config/ModConfigEnhance/output/` | 你导出的东西：`translation/` 待翻文本，`packs/` 导出包（也可在发布时另选目录），`logs/` 日志导出。 |\n"
     "| `config/ModConfigEnhance/data/` | 管理器自己的数据：`runtime/` 运行状态和缓存，`backups/` 配置及迁移备份，`help/` 改过的说明，`legacy/` 2026 年 10 月以前的旧数据。 |"),
    ("**语言目录。** 一种语言一个文件；不再自动写界面词条快照；概览里改的译名直接写进该语言文件。首次启动时把同一语言的多个文件合并成一个（显示结果不变），原文件移进 `language/_迁移前备份_日期/`；导出目录 `export/` 改名 `translation-export/`；以前写进 XUnity 目录的镜像词典也移进备份。",
     "**语言目录。** 一种语言一个文件；不再自动写界面词条快照；概览里改的译名直接写进该语言文件。首次启动时把同一语言的多个文件合并成一个（显示结果不变），原文件移进 `language/_迁移前备份_日期/`；以前写进 XUnity 目录的镜像词典也移进备份。（导出目录的位置见 2.5.2。）"),
])

# —— 文件脉络页 ——
edit("文档/MCE文件脉络.html", [
    ("<ul><li><code>language/</code> 个人词典</li><li><code>profiles/</code> 配置方案</li><li><code>runtime/</code> 已应用和待应用状态</li><li><code>exports/</code> 发布输出，<code>export/</code> 待翻译文本</li><li><code>help/</code> 你改过的说明</li></ul>",
     "<ul><li><code>language/</code> 个人词典</li><li><code>profiles/</code> 配置方案、整合包介绍与素材</li><li><code>output/</code> 导出的东西：<code>translation/</code> 待翻译文本、<code>packs/</code> 发布输出、<code>logs/</code> 日志导出</li><li><code>data/</code> 管理器自己的数据：<code>runtime/</code> 已应用和待应用状态、<code>backups/</code> 自动备份、<code>help/</code> 你改过的说明、<code>legacy/</code> 10 月前旧数据</li></ul>"),
    ("文本写到 <code>config/ModConfigEnhance/export/</code>。</li>", "文本写到 <code>config/ModConfigEnhance/output/translation/</code>。</li>"),
    ("<li>另存为 <code>zh-cn_补翻_日期.txt</code> 放进个人词典目录，点「重载翻译」。</li>", "<li>点「导入补翻」选这个文件，填好的行合进当前语言文件（旧文件先备份）。</li>"),
    ("存到 <code>config/ModConfigEnhance/help/&lt;语言&gt;/</code>", "存到 <code>config/ModConfigEnhance/data/help/&lt;语言&gt;/</code>"),
])
print("edited:", len(done))
