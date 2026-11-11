import io
d = r'F:/Games/SteamLibrary/steamapps/common/PEAK/MODs/'
io.open(d + 'ModConfigEnhance/验证/2.4.1界面细节/结果.md', 'w', encoding='utf-8', newline='').write('''# 2.4.1 工具行图标统一、跳转快捷键、方案箭头、主题切换钮（2026-11-10）

## 用户反馈（截图 `user_toolbar.png`）

1. 两个金色下拉的下箭头太大，文字显示不出来。
2. 刷新图标与旁边两个尺寸不一样；打勾图标画风不同、√ 太细、颜色不对。
3. 表头「只显示快捷键选项」方框改成「跳转到快捷键设置页」。
4. 方案列表右侧绿色箭头不居中、压在背景外。
5. 做主题切换图标：概览工具条一个，模组设置页「搜索」文字右侧、搜索白框上方一个。

## 改动

- `ModConfigSidebarWidgets.MakeStyledDropdown`：箭头 14×8.7 贴右 8、`preserveAspect`，文字右缘 = 箭头左 4。
- `StyleSquareIconButton`：图标框统一 `height × 0.6`。`RefreshIconSprite` 半径 19→22、半宽 4.5→5；新 `CheckIconSprite`（两段圆头粗线半宽 5）；清空更新钮改用它（原 TMP「✓」删除）。
- 表头：复选框与 `ToggleKeysOnly`/`KeysOnlyBox` 删除；右侧金色「跳转到快捷键设置页」+ 右三角，点击 `OverviewController.OpenHotkeys(Menu.MainPage)` → 同一 `UIPageHandler` 的 `OverviewEntry.OpenOnHotkeys()` → 打开后 `OverviewPage.ShowHotkeysTab()`。
- `OverviewPage.Profiles`：标记改为右锚 `(1, .5)`、`(-10, 0)`、40×30；名称宽度让出 44。
- 主题钮：`OverviewIconGraphic` 新增 `theme`（描边新月）；概览 `_theme` 进工具条「偏好」组（`Tool("theme", …, 32)`），点击 `RequestProfileLeave` 后切换并提示「重新打开概览后此页生效」。设置页 `MakeThemeButton` 放在 x=65+300-36、y=240-36-6（搜索框顶 240），sprite 版实心新月。`ToggleTheme`：当前紫 → Blue，否则 → Purple。
- 新词条：JUMP_HOTKEYS、TIP_JUMP_HOTKEYS、BTN_THEME、TIP_THEME、配色主题与两条状态提示；三语帮助 mods 页、CHANGELOG。

## 验证

- 三项构建 0/0（清理掉 KeysOnlyBox 未赋值警告）；完整回归 27/27；help_checks 0；翻译两项检查通过。
- 预览 `icons_preview.png`：四个图标同框同笔画同色，下拉箭头与文字区比例。
- 部署 `deployment_20261110_183723`，50 文件，备份 `备份/部署前_2.4.1界面细节_20261110_183723/`。

## 未验证

- 全部未实机：下拉文字是否完整显示、主题钮位置是否与「搜索」大字重叠、跳转快捷键在暂停菜单与主菜单两处是否都找得到概览入口、方案标记位置。
''')

p = d + 'AGENTS.md'
s = io.open(p, encoding='utf-8').read()
anchor = '### ModConfigEnhance 2.4.0 统一蓝色主题'
add = '''### ModConfigEnhance 2.4.1 工具行图标统一、跳转快捷键、方案箭头、主题切换钮已部署（2026-11-10）

按用户截图反馈：金色下拉箭头缩到 14 见方；刷新/分组/清空更新三方钮图标统一 60% 见方、同笔画，✓ 改粗笔画金色 sprite；表头「只显示快捷键选项」改「跳转到快捷键设置页」（打开同菜单概览并切快捷键页签）；方案列表标记移回行内、垂直居中；新月主题钮放概览工具条「偏好」组与设置页「搜索」右侧。构建 0/0、回归 27/27；`deployment_20261110_183723` 50 文件。记录 `ModConfigEnhance/验证/2.4.1界面细节/`。**未验证**：实机。

'''
assert anchor in s and '2.4.1 工具行图标统一' not in s
s = s.replace(anchor, add + anchor, 1)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
