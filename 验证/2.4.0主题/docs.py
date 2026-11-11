import io, sys
ROOT = sys.argv[1]

def rep(path, old, new):
    p = ROOT + '/' + path
    s = io.open(p, encoding='utf-8').read()
    assert s.count(old) == 1, (path, old[:50], s.count(old))
    s = s.replace(old, new)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

def ins_after(path, anchor, text):
    p = ROOT + '/' + path
    s = io.open(p, encoding='utf-8').read()
    assert s.count(anchor) == 1 and text not in s, (path, anchor[:50])
    s = s.replace(anchor, anchor + text, 1)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

# mods page: replace the 2.3.12 classic-brown sentence
rep('Help/zh-cn/mods.md', '设置页的配色跟随游戏的设置菜单（PEAK 3.1 起是深蓝）；想要旧版的棕色，在 MCE 自己的设置「版面」里打开「经典棕色配色」，重启生效。',
    'MCE 的全部界面有两套配色：蓝色（跟随游戏的设置菜单）和紫色（夜间风格）。在 MCE 自己的设置「版面 › 配色主题」里选；选「夜里自动紫色」则装了 TimeTheme 时夜里自动变紫。设置页立刻切换，概览在下次打开时切换。置顶的行左边有一条青柠色细线；分组色条每深一级变细变短。')
rep('Help/zh-tw/mods.md', '設定頁的配色跟隨遊戲的設定選單（PEAK 3.1 起是深藍）；想要舊版的棕色，在 MCE 自己的設定「版面」裡開啟「經典棕色配色」，重新啟動生效。',
    'MCE 的全部介面有兩套配色：藍色（跟隨遊戲的設定選單）和紫色（夜間風格）。在 MCE 自己的設定「版面 › 配色主題」裡選；選「夜裡自動紫色」則裝了 TimeTheme 時夜裡自動變紫。設定頁立刻切換，概覽在下次開啟時切換。置頂的列左邊有一條青檸色細線；分組色條每深一級變細變短。')
rep('Help/en-us/mods.md', " The settings page follows the game's settings colours (dark blue since PEAK 3.1); for the earlier brown look, turn on **Classic brown colours** under MCE's own **Layout** settings and restart.",
    " Every MCE screen comes in two colour themes: **Blue** (follows the game's settings menu) and **Purple** (the night style). Pick one under MCE's own **Layout › Colour theme**; **PurpleAtNight** turns purple at night when TimeTheme is installed. The settings page switches at once, the overview the next time it opens. Pinned rows have a thin lime line on the left; group rails get thinner and shorter with each level.")

rep('Help/zh-cn/changelog.md', '设置页配色跟随游戏的深蓝，可在「版面」里切回经典棕色。',
    '全部界面统一为蓝色主题，并新增紫色主题，可手动切换或夜里自动切换（不再读取 TimeTheme 的配色）；置顶竖线改细、改用青柠色；子分组色条逐级变细变短。')
rep('Help/zh-tw/changelog.md', '設定頁配色跟隨遊戲的深藍，可在「版面」裡切回經典棕色。',
    '全部介面統一為藍色主題，並新增紫色主題，可手動切換或夜裡自動切換（不再讀取 TimeTheme 的配色）；置頂豎線改細、改用青檸色；子分組色條逐級變細變短。')
rep('Help/en-us/changelog.md', " The settings page follows the game's dark blue, with a **Classic brown colours** switch under Layout.",
    " Every screen now uses a blue theme, with a new purple theme you can pick or have switch on at night (TimeTheme's palette is no longer read); the pin line is thinner and lime; subgroup rails get thinner and shorter per level.")

p = ROOT + '/CHANGELOG.md'
s = io.open(p, encoding='utf-8').read()
entry = '''## 2.4.0 统一蓝色主题、新增紫色主题、置顶线与分组色条

- **用户拍板：** 移除对 TimeTheme 的动态识别，改为可手动切换的主题；只有蓝 / 紫两套（2.3.12 的「经典棕色」开关撤掉）；保留「夜里自动紫色」（只读 TimeTheme 的昼夜状态）；置顶竖线改细、用现有颜色都没有的青柠黄绿 #C8F05A；子分组色条每级变细变短、最短 1/2、底端对齐从顶部截短。只改颜色，不改交互逻辑与参数。
- **主题核心 `ModConfigLook`：** 新配置「1. Layout / Colour theme」（Blue / Purple / PurpleAtNight，默认 Blue）。蓝色主题读游戏 3.1 `SettingsColors`（读不到用同值常量），紫色主题照早先夜间风格（近黑紫底、淡紫字、紫色选中）。模组设置页由 `ApplyMain`/`ApplyOptions` 按登记的颜色互换，主题变化即时重刷；比色改为 0.004 容差（游戏预制体与代码里的同一格蓝差在第三位小数）。
- **TimeTheme：** `TimeThemeBridge` 只剩读昼夜状态；不再读它的配色表、不再让它重新收表、不再替它给页面换色。页签 `ModdedTABSButton.Update` 前缀改为总是挂（按主题渐变）。
- **概览统一：** 概览页 135 处界面骨架颜色（面板、行底、滚动条、分隔线、中性按钮）与 `OverviewUi.Brown/Gold/Ivory/Muted`、对话框底色、文档工具条等改走 `ModConfigLook.Surface`：棕 / 金 / 灰转主题色相，蓝色主题保留原有蓝灰，紫色主题转紫；内容色（状态角标、分组色、警告橙、提交绿、危险红）不动。主题变化后概览在下次打开时重建（`Epoch`）。ModConfig 的筛选下拉跟随主题强调色。
- **竖线：** 置顶竖线 4→2 宽、青柠黄绿，分组色条紧贴其右；新增 `GroupRailShape`（每级宽 -1、高 1→3/4→1/2，底端对齐），树形侧栏、概览列表、快捷键页、智能分组预览共用。
- 验证：三项构建 0/0、完整回归；配色映射预览图 `验证/2.4.0主题/palette_preview.png`；未实机。

'''
assert '## 2.4.0' not in s
s = s.replace('## 2.3.12 ', entry + '## 2.3.12 ', 1)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
