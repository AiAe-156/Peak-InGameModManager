import io
d = r'F:/Games/SteamLibrary/steamapps/common/PEAK/MODs/'
io.open(d + 'ModConfigEnhance/验证/2.3.12配色跟随/结果.md', 'w', encoding='utf-8', newline='').write('''# 2.3.12 模组设置页配色跟随游戏 3.1、保留经典棕色开关（2026-11-10）

用户拍板：跟随蓝色，留棕色开关。

## 改动

- `src/ModConfigLook.cs`（新）：反射读 `Peak.UI.SettingsMenu.SettingsColors` 三个静态字段；读不到或开关开 → 棕色（`ClassicBox`）、白字 / 选中白底黑字。
- `ModConfigTreeLayoutPatch.TabBackground` 由写死棕色改为 `ModConfigLook.Box`（8 处引用跟随）。
- `StyleRow`：ModConfig 页签行底色改当前配色（保留透明度），选中底图改 `SelectedFill`（跟随时米色）。
- `TabButtonUpdatePrefix`：跟随时不装 TimeTheme 也挂，白天照原版渐变到 `TextSelected/TextDefault`；夜间分支不变。`SettleSelection` 同步。
- `TimeThemeBridge.TintSelectedGraphic`：白天目标改 `SelectedFill`；跟随模式下 TimeTheme 不在场也执行。
- 配置项 `1. Layout / Classic brown colours`（默认 false，重启生效），三语；`Loc.Release23Terms.cs` 重新生成（419 条）。
- 三语帮助 mods 页与更新日志各补一句。

## 验证

- 三项构建 0/0；完整回归 27/27（`regression.log`）；help_checks 0 问题。
- 部署 `deployment_20261110_135859`，50 文件，备份 `备份/部署前_2.3.12配色跟随_20261110_135859/`。

## 已知 / 未验证

- TimeTheme 0.4.3 配色表没有新蓝：跟随模式下夜里蓝色行不会换夜间色。是否自建夜间对应色看实机后定。
- 全部未实机：左栏/页签/说明面板/下拉框颜色、开关切换后重启效果。
''')

p = d + 'AGENTS.md'
s = io.open(p, encoding='utf-8').read()
anchor = '### ModConfigEnhance 2.3.11 适配 PEAK 3.1'
add = '''### ModConfigEnhance 2.3.12 模组设置页配色跟随游戏 3.1、保留经典棕色开关已部署（2026-11-10）

用户拍板：跟随蓝色、留棕色开关。新增 `ModConfigLook` 反射读 `SettingsColors`（旧版游戏回退棕色），`TabBackground` 及页签行底色/选中底图/文字色全部跟随；新配置 `1. Layout / Classic brown colours`（默认关，重启生效）。构建 0/0、回归 27/27；`deployment_20261110_135859` 50 文件。记录 `ModConfigEnhance/验证/2.3.12配色跟随/`。**已知**：TimeTheme 表无新蓝，跟随模式夜里蓝色行不换色。**未验证**：实机。

'''
assert anchor in s and '2.3.12 模组设置页配色' not in s
s = s.replace(anchor, add + anchor, 1)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
