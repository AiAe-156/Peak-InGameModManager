"""Third batch (2.3.8): remaining main-thread UI text in the plugin.

Covered elsewhere and therefore not edited here:
  * LogWindow.cs — every caption passes DiagnosticUiLanguage.Text at display time (now three languages);
  * StoredPackDetails.cs / PackReportText.cs — built off the main thread from worker data; left for the worker
    localisation pass (see 审查结论.md).
"""
import os, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
def edit(rel, pairs):
    p = os.path.join(ROOT, 'src', rel)
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if s.count(a) != 1: sys.exit(f'{rel}: expected 1 match, got {s.count(a)}: {a[:90]}')
        s = s.replace(a, b)
    open(p, 'w', encoding='utf-8', newline='').write(s)
    print('edited', rel, len(pairs))
def T(en, cn, tw): return f'Loc.T("{en}", "{cn}", "{tw}")'

edit('Overview/OverviewPage.Packs.cs', [
 ('"旧格式环境（legacy）：完整升级须退出游戏后捕获当前 core、宿主与组件记录；原版本保留。"',
  T("Legacy environment: a full upgrade captures the current core, host and component records after the game exits; the original version is kept.",
    "旧格式环境（legacy）：完整升级须退出游戏后捕获当前 core、宿主与组件记录；原版本保留。",
    "舊格式環境（legacy）：完整升級須結束遊戲後擷取目前 core、宿主與組件記錄；原版本保留。")),
 ('"旧格式库存：须从明确原始来源补齐 core 与宿主，不能用当前 core 伪造历史环境。"',
  T("Legacy stored pack: core and host must be completed from an explicit original source; the current core is not used to fake a historical environment.",
    "旧格式库存：须从明确原始来源补齐 core 与宿主，不能用当前 core 伪造历史环境。",
    "舊格式庫存：須從明確原始來源補齊 core 與宿主，不能用目前 core 偽造歷史環境。")),
 ('form.Text("当前目录里的新增和修改均属于当前整合包。保存后切换会更新当前已保存版本；直接切换保留未保存环境，切回时自动恢复。完整环境独立替换 core 与宿主；旧格式不会自动补全。"',
  'form.Text(' + T("New and changed files in the current folder belong to the current modpack. Save and switch updates its saved version; switching without saving keeps the unsaved environment and restores it when you switch back. A complete environment replaces core and host on its own; legacy packs are not completed automatically.",
    "当前目录里的新增和修改均属于当前整合包。保存后切换会更新当前已保存版本；直接切换保留未保存环境，切回时自动恢复。完整环境独立替换 core 与宿主；旧格式不会自动补全。",
    "目前目錄裡的新增和修改均屬於目前整合包。儲存後切換會更新目前已儲存版本；直接切換保留未儲存環境，切回時自動還原。完整環境獨立取代 core 與宿主；舊格式不會自動補全。")),
 ('string body = operation == "confirm-upgrade-environment" ? "退出 PEAK 后捕获当前真实 core、宿主、运行配置与组件状态为独立版本；保留旧版本，不补全其他旧库存。请等待助手完成。"',
  'string body = operation == "confirm-upgrade-environment" ? ' + T("After PEAK exits, the real core, host, runtime settings and component states are captured as an independent version. The old version is kept and other legacy packs are not completed. Wait for the helper to finish.",
    "退出 PEAK 后捕获当前真实 core、宿主、运行配置与组件状态为独立版本；保留旧版本，不补全其他旧库存。请等待助手完成。",
    "結束 PEAK 後擷取目前真實 core、宿主、執行設定與組件狀態為獨立版本；保留舊版本，不補全其他舊庫存。請等待助手完成。")),
 (': saveCurrent ? "PEAK 退出后，将当前环境的最终文件与配置保存为当前包版本，再切换到「" + task.Proposed.Name + "」。请等待完成后再启动。"',
  ': saveCurrent ? Loc.F("After PEAK exits, the current environment\'s final files and settings are saved as the current pack version, then it switches to \\"{0}\\". Wait for it to finish before launching.", "PEAK 退出后，将当前环境的最终文件与配置保存为当前包版本，再切换到「{0}」。请等待完成后再启动。", "PEAK 結束後，將目前環境的最終檔案與設定儲存為目前套件版本，再切換到「{0}」。請等待完成後再啟動。", task.Proposed.Name)'),
 (': "PEAK 退出后直接切换到「" + task.Proposed.Name + "」。当前已保存版本保持不变，运行环境单独保留；切回时自动恢复，并继续标记为未保存。";',
  ': Loc.F("After PEAK exits, it switches straight to \\"{0}\\". The saved version stays unchanged and the running environment is kept separately; switching back restores it and keeps it marked unsaved.", "PEAK 退出后直接切换到「{0}」。当前已保存版本保持不变，运行环境单独保留；切回时自动恢复，并继续标记为未保存。", "PEAK 結束後直接切換到「{0}」。目前已儲存版本保持不變，執行環境單獨保留；切回時自動還原，並繼續標記為未儲存。", task.Proposed.Name);'),
])

# Store tags: two-arg T(tag, "中文") with a variable English; give them a Traditional column.
edit('Overview/OverviewTagFilter.cs', [
 ('"AI Generated" => T(tag, "AI 生成"), "Host Only" => T(tag, "仅主机"), "Client Side" => T(tag, "客户端"), "All Clients" => T(tag, "所有玩家"),',
  '"AI Generated" => Loc.T(tag, "AI 生成", "AI 生成"), "Host Only" => Loc.T(tag, "仅主机", "僅主機"), "Client Side" => Loc.T(tag, "客户端", "用戶端"), "All Clients" => Loc.T(tag, "所有玩家", "所有玩家"),'),
 ('"Quality Of Life" => T(tag, "生活质量"), "Optimization" => T(tag, "性能优化"), "Mods" => T(tag, "模组"), "Modpacks" => T(tag, "整合包"),',
  '"Quality Of Life" => Loc.T(tag, "生活质量", "生活品質"), "Optimization" => Loc.T(tag, "性能优化", "效能最佳化"), "Mods" => Loc.T(tag, "模组", "模組"), "Modpacks" => Loc.T(tag, "整合包", "整合包"),'),
 ('"Libraries" => T(tag, "基础库"), "Tools" => T(tag, "工具"), "Translations" => T(tag, "汉化／翻译"), "World" => T(tag, "世界"), "Biomes" => T(tag, "生物群系"),',
  '"Libraries" => Loc.T(tag, "基础库", "基礎函式庫"), "Tools" => Loc.T(tag, "工具", "工具"), "Translations" => Loc.T(tag, "汉化／翻译", "中文化／翻譯"), "World" => Loc.T(tag, "世界", "世界"), "Biomes" => Loc.T(tag, "生物群系", "生物群系"),'),
 ('"Gameplay" => T(tag, "玩法"), "Items" => T(tag, "物品"), "Audio" => T(tag, "音效"), "Cosmetics" => T(tag, "外观"), "Faces" => T(tag, "面部"),',
  '"Gameplay" => Loc.T(tag, "玩法", "玩法"), "Items" => Loc.T(tag, "物品", "物品"), "Audio" => Loc.T(tag, "音效", "音效"), "Cosmetics" => Loc.T(tag, "外观", "外觀"), "Faces" => Loc.T(tag, "面部", "臉部"),'),
 ('"Afflictions" => T(tag, "状态效果"), "Accolades" => T(tag, "成就"), "Asset Replacements" => T(tag, "资源替换"), "Mixtapes" => T(tag, "混音"), "Misc" => T(tag, "其他")',
  '"Afflictions" => Loc.T(tag, "状态效果", "狀態效果"), "Accolades" => Loc.T(tag, "成就", "成就"), "Asset Replacements" => Loc.T(tag, "资源替换", "資源替換"), "Mixtapes" => Loc.T(tag, "混音", "混音"), "Misc" => Loc.T(tag, "其他", "其他")'),
])

# Introduction editor prompts (popover titles / input prompts; button labels already mapped).
edit('Overview/Authoring/OverviewDocumentView.cs', [
 ('Prompt("输入本地"+(type=="video"?"视频":type=="gif"?"GIF":"图片")+"文件完整路径",path=>ImportPath(type,path));',
  'Prompt(type=="video"?Loc.T("Full path of the local video file","输入本地视频文件完整路径","輸入本機影片檔案完整路徑"):type=="gif"?Loc.T("Full path of the local GIF file","输入本地GIF文件完整路径","輸入本機GIF檔案完整路徑"):Loc.T("Full path of the local image file","输入本地图片文件完整路径","輸入本機圖片檔案完整路徑"),path=>ImportPath(type,path));'),
 ('Item("链接",()=>Prompt("链接地址",', 'Item("链接",()=>Prompt(Loc.T("Link address","链接地址","連結網址"),'),
 ('Item("资源预算",()=>Prompt("解码像素百万（1–256；单文件上限1GB）",',
  'Item("资源预算",()=>Prompt(Loc.T("Decoded megapixels (1–256; up to 1 GB per file)","解码像素百万（1–256；单文件上限1GB）","解碼像素百萬（1–256；單檔上限1GB）"),'),
 ('else if(command=="link"&&!_sourceMode)Prompt("链接地址",', 'else if(command=="link"&&!_sourceMode)Prompt(Loc.T("Link address","链接地址","連結網址"),'),
])

# Log save dialog caption (Windows file dialog title).
edit('Overview/LogSavePicker.cs', [
 ('public string Caption = "导出日志 ZIP";', 'public string Caption = Loc.T("Export log ZIP", "导出日志 ZIP", "匯出日誌 ZIP");'),
])
# Pack preview: the "配置文件" count compares a worker group tag, not display text; leave as is.
print('ok')
