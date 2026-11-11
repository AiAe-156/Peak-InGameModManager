import io, sys
ROOT = sys.argv[1]

def append_after(path, anchor, text):
    p = ROOT + '/' + path
    s = io.open(p, encoding='utf-8').read()
    assert s.count(anchor) == 1 and text not in s, (path, s.count(anchor))
    s = s.replace(anchor, anchor + text, 1)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

append_after('Help/zh-cn/mods.md', '在设置页里改的值同样先进草稿，保存后提交。',
    '设置页的配色跟随游戏的设置菜单（PEAK 3.1 起是深蓝）；想要旧版的棕色，在 MCE 自己的设置「版面」里打开「经典棕色配色」，重启生效。')
append_after('Help/zh-tw/mods.md', '在設定頁裡改的值同樣先進草稿，儲存後提交。',
    '設定頁的配色跟隨遊戲的設定選單（PEAK 3.1 起是深藍）；想要舊版的棕色，在 MCE 自己的設定「版面」裡開啟「經典棕色配色」，重新啟動生效。')
append_after('Help/en-us/mods.md', 'Values changed on a settings page also go into the draft and are committed on save.',
    ' The settings page follows the game\'s settings colours (dark blue since PEAK 3.1); for the earlier brown look, turn on **Classic brown colours** under MCE\'s own **Layout** settings and restart.')

append_after('Help/zh-cn/changelog.md', '下拉框和文本框一样高。',
    '设置页配色跟随游戏的深蓝，可在「版面」里切回经典棕色。')
append_after('Help/zh-tw/changelog.md', '下拉框和文字框一樣高。',
    '設定頁配色跟隨遊戲的深藍，可在「版面」裡切回經典棕色。')
append_after('Help/en-us/changelog.md', 'the dropdown is as tall as the text boxes.',
    ' The settings page follows the game\'s dark blue, with a **Classic brown colours** switch under Layout.')

p = ROOT + '/CHANGELOG.md'
s = io.open(p, encoding='utf-8').read()
entry = '''## 2.3.12 模组设置页配色跟随游戏 3.1，保留经典棕色开关

- **用户拍板：** 默认跟随游戏新配色，另给「经典棕色」开关。
- 新增 `ModConfigLook`：反射读游戏 3.1 的 `Peak.UI.SettingsMenu.SettingsColors`（`BoxColor` 半透明深蓝、`TextDefaultColor` 米色、`TextSelectedColor` 深蓝），读不到（旧版游戏）或开关打开时用 3.1 之前的棕色。`ModConfigTreeLayoutPatch.TabBackground` 改为取它，说明面板、复选框、滚动条、分组头、表头、下拉框等 8 处引用自动跟随。
- ModConfig 页签行：`StyleRow` 把 `PeakHorizontalTabs` 写死的棕底改成当前配色、选中底图改米色；`ModdedTABSButton.Update` 前缀在跟随时照原版 `SettingsTABSButton` 渐变到「选中深蓝字 / 未选中米色字」（不装 TimeTheme 也挂）。TimeTheme 夜间逻辑不变。
- 新配置项「1. Layout / Classic brown colours」（默认关，重启生效），三语说明；启动日志记 `[配色]` 当前状态。
- **已知：** TimeTheme 0.4.3 的配色表里没有新蓝，夜里跟随模式下的蓝色行不会被它换成夜间色（经典棕色仍会）；是否自建夜间对应色需看实机效果。
- 验证：三项构建 0/0、完整回归；未实机。

'''
assert '## 2.3.12' not in s
s = s.replace('## 2.3.11 ', entry + '## 2.3.11 ', 1)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
