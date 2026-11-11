import io, re, sys
ROOT = sys.argv[1]

def rep(path, old, new):
    p = ROOT + '/' + path
    s = io.open(p, encoding='utf-8').read()
    assert s.count(old) == 1, (path, old[:50], s.count(old))
    s = s.replace(old, new)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)

for f in ('Desktop/MCE.Desktop.csproj', 'ModConfigEnhance.csproj', 'Updater/MCE.UpdateWorker.csproj'):
    rep(f, '<Version>2.4.0</Version>', '<Version>2.4.1</Version>')
rep('manifest.json', '"version_number": "2.4.0"', '"version_number": "2.4.1"')
rep('src/Plugin.cs', 'PluginVersion = "2.4.0"', 'PluginVersion = "2.4.1"')

rep('Help/zh-cn/mods.md', '在 MCE 自己的设置「版面 › 配色主题」里选；',
    '点概览工具条的月亮图标、或模组设置页「搜索」右侧的月亮图标即可切换，也可在 MCE 自己的设置「版面 › 配色主题」里选；')
rep('Help/zh-tw/mods.md', '在 MCE 自己的設定「版面 › 配色主題」裡選；',
    '點概覽工具列的月亮圖示、或模組設定頁「搜尋」右側的月亮圖示即可切換，也可在 MCE 自己的設定「版面 › 配色主題」裡選；')
rep('Help/en-us/mods.md', "Pick one under MCE's own **Layout › Colour theme**;",
    "Switch with the moon icon in the overview toolbar or to the right of **Search** on the settings page, or pick one under MCE's own **Layout › Colour theme**;")
rep('Help/zh-cn/mods.md', '置顶的行左边有一条青柠色细线；',
    '侧栏表头右侧的「跳转到快捷键设置页」直接打开概览的快捷键设置页签。置顶的行左边有一条青柠色细线；')
rep('Help/zh-tw/mods.md', '置頂的列左邊有一條青檸色細線；',
    '側欄表頭右側的「跳轉到快捷鍵設定頁」直接開啟概覽的快捷鍵設定頁籤。置頂的列左邊有一條青檸色細線；')
rep('Help/en-us/mods.md', 'Pinned rows have a thin lime line on the left;',
    '**Go to keybindings** at the right of the sidebar header opens the overview on its Keybindings tab. Pinned rows have a thin lime line on the left;')

p = ROOT + '/CHANGELOG.md'
s = io.open(p, encoding='utf-8').read()
entry = '''## 2.4.1 工具行图标统一、跳转快捷键、方案箭头、主题切换钮

- **工具行下拉：** 排序 / 语言两个金色下拉的箭头从模板约 30×19 缩到 14 见方、贴右 8，文字区拉到箭头左侧（原先「简体…」只剩两个字）。
- **方钮图标统一：** 刷新 / 分组 / 清空更新三个方钮图标统一占 60% 见方；刷新环加粗放大到与分组图标同一笔画和占地；「清空更新标识」的 ✓ 由字体字符改成同一套粗笔画金色 sprite（原先细、颜色不跟图标）。
- **表头：** 「只显示快捷键选项」复选框改为「跳转到快捷键设置页」（金色文字 + 右箭头），点击打开同一菜单的概览并切到快捷键设置页签（`OverviewController.OpenHotkeys`）。
- **方案列表：** 右侧绿色 ✓ / 蓝色 ▸ 标记原按 `listWidth-46` 放、未扣行的右缩进，压到行外；改为行内右侧 10、垂直居中，名称宽度相应让出。
- **主题切换钮：** 新月图标。概览工具条「偏好」组（语言旁）一个；模组设置页「搜索」标题右侧、搜索框上方一个。点击在蓝 / 紫之间切换（夜里自动紫色时改为固定另一套）；设置页立刻换色，概览在下次打开时换。
- 验证：三项构建 0/0、完整回归；图标与下拉预览 `验证/2.4.1界面细节/icons_preview.png`；未实机。

'''
assert '## 2.4.1' not in s
s = s.replace('## 2.4.0 ', entry + '## 2.4.0 ', 1)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
