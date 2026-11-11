import io
d = r'F:/Games/SteamLibrary/steamapps/common/PEAK/MODs/'
io.open(d + 'ModConfigEnhance/验证/2.4.0主题/结果.md', 'w', encoding='utf-8', newline='').write('''# 2.4.0 统一蓝色主题、新增紫色主题、置顶线与分组色条（2026-11-10）

## 用户要求与拍板

- 移除对 TimeTheme 的动态识别，改为可手动切换的主题色；现有页面统一蓝色，再做夜间紫色（以早先紫色主题为模板），只改颜色，不改交互逻辑与参数。
- 问答拍板：只要蓝 / 紫两个（撤掉 2.3.12 的经典棕色）；保留「夜里自动紫色」（只读 TimeTheme 昼夜状态）；置顶竖线改细、用青柠黄绿 #C8F05A；子分组色条每级变细变短、最短 1/2、底端对齐从顶部截短。

## 改动

- `src/ModConfigLook.cs`：主题唯一来源。`ThemeMode { Blue, Purple, PurpleAtNight }`，配置 `1. Layout / Colour theme`。`IsDark` 每帧缓存，变化时 `Epoch++`。设置页 `ApplyMain`（底色、米白、旧棕）/`ApplyOptions`（白字↔亮紫、输入框/下拉 ColorBlock）。概览 `Surface/Tone`：棕金灰转主题色相，蓝色主题保留冷色，紫色主题冷色也转紫；亮的橙金、绿、红等内容色不动。比色容差 0.004。
- `src/TimeThemeBridge.cs`：重写为只读 `IsDarkTheme`。
- `ModConfigTreeLayoutPatch.cs`：全部 TimeTheme 调用换成 `ModConfigLook`；页签 Update 前缀总是挂；`TreeThemeFollower` 按 `ModConfigLook.IsDark` 重刷；筛选下拉跟随 `Accent`。
- `ModConfigSidebarWidgets*.cs`：`Cream` 改属性；工具钮 `Tint(CreamOf(false), BoxOf(true))`；「默认」钮紫色主题深紫；置顶线 2 宽青柠（`PinAccentWidth`），分组条贴其右。
- `Overview/GroupRailShape.cs`（新）：宽每级 -1（最细一半）、高 1→3/4→1/2，底端对齐；侧栏、概览列表、快捷键页、智能分组预览共用。
- 概览：`wrap_surfaces.py` 把 Overview 下 135 处中性颜色字面量包成 `ModConfigLook.Surface(...)`（清单 `wrap.log`）；`OverviewUi.Brown/Gold/Ivory/Muted`、对话框、整理预览、文档工具条、行操作禁用色改属性；`UiActionStyle` 中性/编辑/概览角色与选中色随主题，提交/危险只在紫色主题压暗。`OverviewEntry.EnsureShell` 发现 `Epoch` 变化且页面未打开时销毁重建。
- 文档：三语帮助 mods 页与更新日志、`LanguageReadme.cs`、CHANGELOG。`hardcoded_chinese.py` 把 `ModConfigLook.cs` 列入 LOG_ONLY（State 只进日志）。

## 验证

- 三项构建 0/0；完整回归 27/27（`regression.log`）；help_checks 0。
- 映射预览 `palette_preview.png`（与 C# 同一套 Tone 规则的 Python 复刻 + 色条形状）。
- 部署 `deployment_20261110_155918`，50 文件，备份 `备份/部署前_2.4.0主题_20261110_155918/`。

## 未验证 / 已知

- 全部未实机：两套主题实际观感、即时切换、概览重建、色条形状、置顶线。
- 概览已打开时切换主题不会当场变色，下次打开才重建（设计如此，避免打断操作）。
- 文档编辑器的正文配色（`DocCanvas`、`OverviewDocumentView` 纸面）是内容，未改。
- TimeTheme 本身夜里仍会给 ModConfig 的选项单元格换色（它自己的行为，MCE 不再干预）。
''')

p = d + 'ModConfigEnhance/AGENTS.md'
s = io.open(p, encoding='utf-8').read()
anchor = '## 卸载保护与名称列角标（2.2.8 起）'
add = '''## 配色主题（2.4.0 起，用户 2026-11-10 拍板）
- **唯一来源 `ModConfigLook`**：两套主题蓝 / 紫，配置 `1. Layout / Colour theme`（Blue / Purple / PurpleAtNight）。新界面颜色一律从这里取：设置页用 `Box`/`Cream`/`Prominent`/`TextDefault`/`TextSelected`/`SelectedFill`/`Accent`；概览骨架色（面板、行底、滚动条、分隔线、中性按钮）用 `ModConfigLook.Surface(new Color(...))`。内容色（状态角标、分组色、警告橙、提交绿、危险红）不包。
- **不再读 TimeTheme 的配色**：`TimeThemeBridge` 只剩 `IsDark`，只给 PurpleAtNight 用。不得再调它的配色表、`RefreshGraphicLists` 或按它的表换色。
- 设置页换主题走 `ModConfigLook.ApplyMain/ApplyOptions`（按登记颜色互换，0.004 容差）；不在登记表里的颜色用 `ModConfigSidebarWidgets.Tint(graphic, 蓝主题色, 紫主题色)`。概览不做即时换色，按 `ModConfigLook.Epoch` 在下次打开时重建。
- 置顶竖线固定青柠黄绿 `ModConfigLook.PinAccent`（#C8F05A，2 宽）；分组色条形状只走 `GroupRailShape`（每级变细变短、最短 1/2、底端对齐）。

'''
assert anchor in s and '## 配色主题（2.4.0' not in s
s = s.replace(anchor, add + anchor, 1)
io.open(p, 'w', encoding='utf-8', newline='').write(s)

p = d + 'AGENTS.md'
s = io.open(p, encoding='utf-8').read()
anchor = '### ModConfigEnhance 2.3.12 模组设置页配色跟随'
add = '''### ModConfigEnhance 2.4.0 统一蓝色主题、新增紫色主题、置顶线与分组色条已部署（2026-11-10）

用户拍板：移除 TimeTheme 动态识别，改为手动主题（蓝 / 紫，另有「夜里自动紫色」只读 TimeTheme 昼夜）；撤掉 2.3.12 经典棕色；置顶线改细改青柠 #C8F05A；子分组色条逐级变细变短（最短 1/2、底端对齐）。`ModConfigLook` 成为全部界面的配色来源，概览 135 处骨架色经 `Surface` 跟随主题，`TimeThemeBridge` 只剩读昼夜。构建 0/0、回归 27/27；`deployment_20261110_155918` 50 文件。记录 `ModConfigEnhance/验证/2.4.0主题/`，规则写入项目 AGENTS.md「配色主题」。**未验证**：全部实机观感。

'''
assert anchor in s and '2.4.0 统一蓝色主题' not in s
s = s.replace(anchor, add + anchor, 1)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
