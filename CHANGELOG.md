# Changelog / 更新日志

## 0.0.2

### English

- **Log window:** pick several log levels at once, with each level shown in the same color as the log text; view the logs of the previous three game launches (each copy capped at 10 MB); the mod filter uses the same drop-down menu as the mod list; the filter buttons line up on the right; exported logs mask your user folder in more path spellings, and export errors follow the interface language.
- **Mod Settings tab:** a third tab next to Keybindings opens the mod settings page directly; with a controller the focus lands on "Jump to keybindings" there.
- **Mod list:** a faint hint under the last row explains dragging and editing in edit mode; rows play their loading sound every time the list is rebuilt; the dashed borders of the "Mod overview" entry match the "Mod settings" button.
- **Groups:** subgroups are listed above their parent's own mods (mod list, keybindings page and settings sidebar); when dragging a group heading in edit mode, dropping on its top or bottom edge places it before or after that group, dropping in the middle makes it a subgroup; new groups are prefilled "New group 1" / "New subgroup 1".
- **Controller:** LB / RB jump between the toolbar buttons (inside the management center and the update queue they still switch those pages); RT + LB / RB move the focus onto a tab (including Mod Settings) and A opens it; holding the D-pad or left stick keeps moving; with a controller the focus frame follows the selection, with keyboard and mouse it follows the control under the pointer, and each move to a new control plays a short click (the large tabs and game-style buttons use the game's own hover sound); tabs and every control have a focus frame; on the keybindings page the controller and keyboard columns have separate menus and up / down moves row by row; the settings page header and search box can be reached and left with a controller.
- **Menus:** every drop-down uses one style with a clear title row; the language drop-down shows only the language name so it no longer overlaps the arrow.
- **Export:** before building, the publish wizard lists installed mods that are not recorded in any selected profile (so they would be left out of the package), with their version and whether they are enabled. "Record into profiles and export" saves the ticked ones: the package's default profile keeps each mod's current state and the other selected profiles record it as disabled; the profiles are backed up first. "Export without recording" asks once more, then builds the package without them as before.
- **ModConfig Enhance stays disabled:** this manager already includes everything ModConfig Enhance does, so while it is installed nothing re-enables ModConfig Enhance. Its checkbox in the mod list cannot be ticked (the tooltip says why); profiles, modpacks and imports that record it as enabled are applied, saved and published with it disabled. If it was loaded at startup, the manager disables it (renames its file; takes effect after a restart), writes this to the log and tells you once. With a mod manager profile such as r2modman configured, disable it there instead.
- **Controller combos:** the single shared modifier is gone. While recording, hold any buttons and press the last one (up to 4 buttons, e.g. LT + A or LB + RB + X); only the last button is masked from the game, the held ones still reach it. Combos recorded with the old modifier are converted automatically (the modifier becomes the first held button; the old file is kept as `gamepad-combos.json.before-free-combos`), and profile presets now use the buttons they were recorded with. Combos only act in game, so they never clash with the manager's own controller shortcuts. If you need zero interference, Steam Input action sets or chords are an alternative.

### 中文

- **日志窗口：** 日志等级可多选，等级名与日志正文同色；可查看最近 3 次启动的日志（每份副本上限 10 MB）；「模组」筛选改用与模组列表相同的下拉菜单；筛选按钮靠右排齐；导出日志时个人用户目录的遮盖覆盖更多路径写法，导出出错的提示跟随界面语言。
- **「模组设置」页签：** 「快捷键设置」右边新增第三个页签，按下直接打开模组设置页；用手柄进入时焦点停在「跳转到快捷键设置页」。
- **模组列表：** 最后一行下方有淡色提示，说明编辑模式下的拖动与编辑；每次列表重建时逐行载入都有音效；「模组概览」入口的虚线与「模组设置」按钮一致。
- **分组：** 子分组排在上级分组的直属模组前面（模组列表、快捷键页、设置页侧栏一致）；编辑模式下拖动分组标题，放在另一个标题的上缘或下缘就排到它前面或后面，放在中间就成为它的子分组；新建分组预填「新分组1」「新子分组1」。
- **手柄：** LB / RB 在工具栏按钮间跳（管理中心和更新队列里仍切它们自己的页）；RT + LB / RB 把焦点移到页签上（含「模组设置」），按 A 切换；按住十字键或左摇杆可连续移动；焦点框在手柄下跟随选中、在键鼠下跟随鼠标悬停的控件，每换到一个新控件响一声（大页签和游戏样式按钮用游戏自己的悬停音效），页签和所有控件都有焦点框；快捷键页的手柄列与键盘列菜单分开，上下按行移动；设置页的页头和搜索框可以用手柄进出。
- **菜单：** 所有下拉菜单统一样式、标题行醒目；语言下拉收起时只显示语言名，不再压到箭头。
- **导出：** 生成前，发布向导会列出已安装、但没记录在任何勾选方案里的模组（不处理就不会进包），附版本和启停状态。「存入方案并导出」把勾选的存进去：整合包的默认方案照当前启停，其他勾选的方案记为停用，存入前先备份方案；「只导出、不存入方案」再确认一次后照原样导出，不带它们。
- **ModConfig Enhance 保持停用：** 本管理器已包含 ModConfig Enhance 的全部功能，在场时任何操作都不会重新启用它。模组列表里它的勾选框勾不上（悬停说明原因）；方案、整合包、导入里记为启用的，应用、保存、发布时一律按停用处理。启动时发现它被加载了，管理器会把它停用（改名，重启后生效），写进日志并提醒一次。配置了 r2modman 等管理器档案时，请在那边停用。
- **手柄组合键：** 取消全局唯一的修饰键。录制时先按住任意几个键、最后按一个键（最多 4 个键，例如 LT + A、LB + RB + X）；只屏蔽最后那个键，先按住的键游戏照常读到。用旧修饰键录的组合自动转换（修饰键成为第一个按住的键，旧文件留一份 `gamepad-combos.json.before-free-combos`）；方案预设按它录下的键生效。组合只在局内生效，不会和管理器自己的手柄操作冲突。需要完全不干扰游戏时，可以改用 Steam 输入的动作集或组合键。

## 0.0.1 — First public test release / 首个公开测试版

### English

In-Game Mod Manager expands the settings-focused ModConfig Enhance 1.0.4 into an in-game mod and modpack manager.

- **Mod overview:** versions, sources, enablement, update indicators, dependency information, nested groups and local-folder access in one list.
- **Installation and maintenance:** Thunderstore browsing, installation, updates and uninstalling; queued work, history, backups and recovery through the included helper. Loaded-file replacements finish after the game exits.
- **Settings and keybinds:** the settings tree, search, pinning and descriptions, plus a dedicated keyboard/gamepad binding page, configurable gamepad modifiers and duplicate-binding indicators. Settings-page edits and recorded bindings are written immediately.
- **Configuration profiles:** named sets of parameters, bindings and enablement targets; preview before applying; separate author presets and personal changes, with a change review and effect-timing information.
- **Modpack library:** import previews, separate mod versions and configuration for each environment, saved environment revisions and switching after the game exits.
- **Modpack authoring:** illustrated introductions with images, GIFs and video; export author data, complete local archives or Thunderstore packages. Complete archives collect startup patches automatically and honor explicit exclusions. Newcomer archives include BepInEx startup files and bilingual installation instructions; recognized private configuration values are cleared in export copies.
- **Independent translation tools:** built-in English, Simplified Chinese and Traditional Chinese, custom language files, export all/missing texts, import supplements and regular-expression translations without requiring XUnity AutoTranslator.
- **Gamepad operation:** navigation across settings, overview tabs and dialogs, stable list columns, configurable combinations, Steam on-screen keyboard and A-to-lock/B-to-leave reading panes. D-pad reading steps are three times the ordinary size.
- **Everyday tools:** log browsing/filtering/export, previewed folder organization, and blue/purple themes with optional TimeTheme night switching.
- **Independent identity:** a separate plugin, configuration folder and runtime directory, with support for reading earlier author material and modpacks.

- **Font compatibility:** ordinary interface text reuses the game's localized font instead of creating a font from Windows system fonts.

- **Installation and diagnostics fixes:** recognize split MonoDetour components locally, avoid duplicate resource rows, and export declared manager versions plus complete component locations and files. The all-in-one archive includes the official BepInEx 5.4.23.5 managed core for Unity 6 logging compatibility.

### 中文

游戏内模组管理器以侧重设置的 ModConfig Enhance 1.0.4 为基础，扩展为游戏内的模组与整合包管理工具。

- **模组概览：** 同页查看版本、来源、启停、更新状态和依赖，支持嵌套分组、主页与本地文件夹入口。
- **安装与维护：** 雷霆商店浏览、安装、更新和卸载；附带助手提供任务队列、历史、备份与恢复，替换已加载文件的操作在退出游戏后完成。
- **设置与快捷键：** 保留设置树、搜索、置顶和说明，增加键盘／手柄快捷键专页、自选手柄修饰键和重复键提示；设置页改值与录制完成的快捷键立即写入。
- **配置方案：** 保存多套参数、快捷键和启停目标，应用前先查看；作者预设与个人修改分开，提供变更检查和生效时机提示。
- **整合包库：** 导入前预览，各环境分别保存模组版本与配置，支持环境版本存档和退出游戏后切换。
- **整合包制作：** 支持图片、动图和视频介绍；导出作者资料、本地完整包或雷霆商店包。完整包自动收集启动补丁并执行手动排除；新手版附带 BepInEx 启动文件与中英文安装说明；导出副本会清空识别为私密信息的配置值。
- **独立翻译工具：** 内置英文、简体中文、繁体中文，支持自定义语言、导出全部／漏翻文本、导入补翻和正则翻译，无需 XUnity AutoTranslator。
- **手柄操作：** 覆盖设置、概览页签与弹窗，列表上下移动保留所在列，支持自定义组合键和 Steam 屏幕键盘；正文按 A 锁定浏览、B 退出，十字键浏览步长为普通步长三倍。
- **日常工具：** 日志浏览、筛选与导出，执行前可预览的目录整理，以及蓝色／紫色主题和可选 TimeTheme 夜间切换。
- **独立身份：** 使用独立插件、配置文件夹与运行目录，兼容读取早期作者资料和整合包。
- **字体兼容：** 普通界面文字复用游戏现有本地化字体，修复部分电脑从 Windows 系统字体创建文字资源失败的问题。
- **安装与诊断修复：** 本地识别分散安装的 MonoDetour 组件，消除重复资源包条目；日志导出记录正确的管理器版本与完整组件位置、文件。完整安装包采用官方 BepInEx 5.4.23.5 核心，修复 Unity 6 日志写入器错误。
