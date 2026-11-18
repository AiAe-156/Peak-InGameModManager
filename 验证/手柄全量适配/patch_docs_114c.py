# -*- coding: utf-8 -*-
"""1.1.4 docs for the newcomer ZIP and the export scrub."""
import io, os
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
def edit(rel, pairs):
    p = os.path.join(ROOT, rel)
    s = io.open(p, encoding='utf-8').read(); nl = '\r\n' if '\r\n' in s else '\n'
    for a, b in pairs:
        a = a.replace('\n', nl); b = b.replace('\n', nl)
        assert s.count(a) == 1, (rel, a[:80], s.count(a))
        s = s.replace(a, b)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

edit('Help/zh-cn/export.md', [
('作者分享前需自行检查个人连接信息，再点「预览发布内容」核对文件、方案和依赖。',
 '打包时会自动清空看起来像私人信息的配置值（API 密钥、Secret、Token、密码、带账号的代理地址），清了哪些写在包里的 README；你自己的文件不变。分享前仍建议自己再看一眼，然后点「预览发布内容」核对文件、方案和依赖。\n\n   完整本地整合包默认还会同时生成一个「新手版」压缩包：多带 BepInEx 的启动文件（winhttp.dll、doorstop_config.ini、.doorstop_version）和一页中英文安装说明，没装过模组、也不用 r2modman 的人解压到游戏目录就能玩。已经装了 IGMM 的人请用不带「新手版」的那个包导入。'),
])
edit('Help/zh-tw/export.md', [
('作者分享前請自行檢查個人連線資訊，再點「預覽發佈內容」核對檔案、方案和依賴。',
 '打包時會自動清空看起來像私人資訊的設定值（API 金鑰、Secret、Token、密碼、帶帳號的代理位址），清了哪些寫在包裡的 README；你自己的檔案不變。分享前仍建議自己再看一眼，然後點「預覽發佈內容」核對檔案、方案和依賴。\n\n   完整本機整合包預設還會同時產生一個「新手版」壓縮檔：多帶 BepInEx 的啟動檔案（winhttp.dll、doorstop_config.ini、.doorstop_version）和一頁中英文安裝說明，沒裝過模組、也不用 r2modman 的人解壓到遊戲目錄就能玩。已經裝了 IGMM 的人請用不帶「新手版」的那個包匯入。'),
])
edit('Help/en-us/export.md', [
('Authors should review their personal connection details themselves before sharing, then preview files, profiles and dependencies.',
 'Values that look private (API keys, secrets, tokens, passwords, proxy addresses with credentials) are cleared in the packed copy and listed in its README; your own files are not changed. Still take a look before sharing, then preview files, profiles and dependencies.\n\n   A complete local package also produces a "newcomer" ZIP by default: it adds the BepInEx startup files (winhttp.dll, doorstop_config.ini, .doorstop_version) and a short bilingual install note, so someone who has never modded and does not use r2modman can extract it into the game folder and play. IGMM users should import the archive without "newcomer" in its name.'),
])
edit('CHANGELOG.md', [
('''- The first list load no longer piles up every button's appear sound over the row sound.''',
 '''- The first list load no longer piles up every button's appear sound over the row sound.
- **Newcomer modpack ZIP:** a complete local export also writes a newcomer ZIP with the BepInEx startup files (winhttp.dll, a standard doorstop_config.ini, .doorstop_version) and a bilingual install note, so players without BepInEx or r2modman extract it into the PEAK folder and play. The IGMM import ZIP is unchanged.
- **Private values cleared on export:** values that look like API keys, secrets, tokens, passwords or proxy credentials are cleared in the packed configuration (never in your own files) and listed in the package README.'''),
])
edit('README.md', [
('''- 首次加载列表不再叠加每个按钮的出现音效。''',
 '''- 首次加载列表不再叠加每个按钮的出现音效。
- 完整本地整合包同时生成「新手版」压缩包：附带 BepInEx 启动文件（winhttp.dll、标准 doorstop_config.ini、.doorstop_version）和中英文安装说明，没装过 BepInEx、不用 r2modman 的人解压到游戏目录即可。IGMM 导入用的包不变。
- 导出时清空打包配置里疑似私人信息的值（API 密钥、Secret、Token、密码、代理账号），只改包里的副本，清了哪些写在包的 README。'''),
])
for lang, a, b in [
 ('zh-cn', '- 日志窗口、商店、各弹窗都能用手柄操作：焦点不丢、B 逐层退出、右摇杆滚动。', '- 日志窗口、商店、各弹窗都能用手柄操作：焦点不丢、B 逐层退出、右摇杆滚动。\n- 完整本地整合包同时生成「新手版」，带 BepInEx 启动文件和安装说明，解压到游戏目录即可。\n- 导出时自动清空疑似私人信息的配置值（只改包里的副本）。'),
 ('zh-tw', '- 日誌視窗、商店、各彈窗都能用手把操作：焦點不丟、B 逐層退出、右搖桿捲動。', '- 日誌視窗、商店、各彈窗都能用手把操作：焦點不丟、B 逐層退出、右搖桿捲動。\n- 完整本機整合包同時產生「新手版」，帶 BepInEx 啟動檔案和安裝說明，解壓到遊戲目錄即可。\n- 匯出時自動清空疑似私人資訊的設定值（只改包裡的副本）。'),
 ('en-us', '- Log window, store and dialogs work with a controller: focus kept, B backs out one layer, right stick scrolls.', '- Log window, store and dialogs work with a controller: focus kept, B backs out one layer, right stick scrolls.\n- Complete local packages also produce a newcomer ZIP with the BepInEx startup files and an install note.\n- Private-looking config values are cleared in the packed copy on export.'),
]:
    edit(f'Help/{lang}/changelog.md', [(a, b)])
print('ok')
