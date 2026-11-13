<a id="english"></a>

# In-Game Mod Manager

**English** | [中文](#中文说明)

A mod that provides basic in-game mod management without requiring a separate third-party mod store app. View your mod list, mod versions and dependency trees directly in the game. Settings, keybinds, installation, updates, enabling, disabling, uninstalling, and creating or switching modpacks are all brought together in one interface.

ModConfig Enhance focuses on making mod settings easier to find and understand. In-Game Mod Manager builds on that foundation to manage the mods themselves and the entire modded environment.

**0.0.1 · First public test release**

![Mod overview](https://raw.githubusercontent.com/AiAe-156/Peak-InGameModManager/main/%E6%96%87%E6%A1%A3/%E4%BB%8B%E7%BB%8D%E9%A1%B5/assets/overview-blue.jpg)

**[View the full introduction](https://aiae-156.github.io/Peak-InGameModManager/%E6%96%87%E6%A1%A3/%E4%BB%8B%E7%BB%8D%E9%A1%B5/index.html)**

This is a test release. For bug reports or suggestions, join the QQ group: 1104320838.

---

### Mod management

See all your mods in one place: which have updates, which are not active, and which depend on each other.

- **Groups**: Nest groups up to three levels deep, collapse them, reorder them and move their members. You can also generate initial groups from your existing folders.
- **Search and filters**: Find mods by name, author, keyword or tag, hide disabled entries, or focus on items that need attention.
- **Status indicators**: Updates, unread changes, unsaved edits, pending restarts, resource packs and more each have their own color and fixed position. Open an indicator to see the details.
- **Enable and disable**: Set a mod's target state with a checkbox. After saving, the change takes effect the next time you start the game, without manually renaming files.
- **Dependencies and links**: View dependencies, missing items and version requirements, and open store pages, project websites or local folders.
- **Duplicate detection**: When multiple copies of the same plugin are installed, see their versions and paths to help decide which one to keep.
- **Shared components and resource packs**: In addition to regular plugins, view shared libraries, startup patches and mod packages that contain only resources.
- **Folder organization**: Organize the plugins folder by group. Files are moved without being renamed, and you can review a preview before applying the changes.

### Mod settings

All your mod settings are organized into a tree, making them easier to find and understand.

![Mod settings](https://raw.githubusercontent.com/AiAe-156/Peak-InGameModManager/main/%E6%96%87%E6%A1%A3/%E4%BB%8B%E7%BB%8D%E9%A1%B5/assets/settings-tree.jpg)

- **Tree sidebar**: Browse groups, mods and settings sections, search or filter by type, and pin frequently used mods.
- **Description panel**: Select a setting to see its description, default value, allowed range or dropdown choices. Select a mod to read its introduction.
- **Immediate saving**: The settings page writes configuration through ModConfig, saving changes directly to the actual configuration files.
- **Defaults and clearing**: View default values beside each setting and restore defaults or clear the corresponding value.
- **Effect timing**: See whether a setting takes effect immediately, in a new run, after restarting the game, or at a time determined by the mod.

### Centralized keybinds

Manage keybinds from different mods on one page, with separate keyboard and gamepad settings.

- **Keyboard and gamepad columns**: Use a keyboard and mouse to record keyboard bindings, or a controller to record gamepad bindings. Unbound entries show "Not set".
- **Save as you record**: Changes to current bindings are saved immediately. You can also view mod defaults and profile presets.
- **Gamepad combinations**: Assign gamepad buttons or combinations to mods that only support keyboard shortcuts.
- **Configurable modifier**: Choose None, LT, RT, LB, RB, Back, Start, RS or LS. The default is Back. All combinations share one modifier; changing it updates the combinations already recorded.
- **Duplicate-binding indicators**: Check for conflicts between mods and with the game's own controls, with separate indicators in the keyboard and gamepad columns.
- **Button icons**: Bindings use the game's button icons for easy comparison with your controller.
- **Recording lock**: While recording, gamepad input is captured for the binding. Hold B for one second to finish recording.

### Multiple configuration profiles

Keep different configurations for the same set of mods, such as "Everyday" and "Chaos", and switch between them as needed.

- **Configuration profiles**: Each profile records parameters, keybinds and target enablement states. Create, copy, rename, import and export profiles.
- **Preview before applying**: Select another profile to inspect its contents, then save the configuration when you decide to use it.
- **Capture current settings**: Record the settings currently in use in a profile for restoring or sharing later.
- **Change review**: Review edits, old values, new values and effect timing in "Change review".
- **Author presets and personal changes**: Keep the original presets supplied by a modpack author. Save personal changes separately or restore an author preset.
- **Shared modpack information**: Profiles within a modpack share groups, introductions, media and translated mod names.

### Store and updates

Browse Thunderstore, install mods and maintain them from inside the game.

- **Source identification**: Automatically find the store package corresponding to a mod. If the result is unclear, select it manually or mark the mod as local.
- **Installation and version selection**: Read descriptions, dependencies and changelogs, browse versions, and install the version you need.
- **Update checks**: Get notified when new versions are available and choose which mods to update. Update checks can also be controlled per mod.
- **Dependencies included**: Install missing components according to store dependencies during installation or updates.
- **Task queue**: View download progress, speed and history; pause, resume, stop or retry tasks.
- **Replace loaded files after exit**: Downloads and preparation can run while the game is open. When loaded files need replacing, the included helper completes the replacement after you exit.
- **Backups and rollback**: Files are backed up before operations. Roll back an update to the previous files and choose whether to restore the previous settings as well.
- **Network settings**: Use the system proxy, a direct connection or a custom HTTP(S) proxy.

The update helper starts only when there are tasks, provides its own progress window and exits when the work is finished. r2modman and Thunderstore Mod Manager profile layouts are supported.

### Modpacks

Save an entire modded environment, switch between environments, or package one to share with friends.

- **Modpack library**: Store multiple environments, each with its own mod versions and configuration.
- **Import preview**: Inspect mods, files, dependencies, required downloads and any issues before deciding whether to store or apply a package.
- **Store now, activate later**: Add a modpack to the library first, then preview its switching effects when you are ready.
- **Keep your current environment**: Before switching for the first time, save the current environment as a temporary modpack so you can return to it later.
- **Backed-up switching**: Replace the environment after the game exits, retaining the previous state for recovery.
- **Environment revisions**: Save the current state of a modpack and manage saved revisions separately from current changes.
- **Multiple profiles per environment**: Offer different gameplay configurations within one modpack for players to choose from.
- **Storage management**: See the space used by modpacks, download caches, backups and the recycle bin, and clean up as needed.

### Creating and publishing modpacks

Prepare configurations, write introductions and generate release packages from inside the game.

- **Rich introductions**: Write modpack introductions with headings, lists, links, images, GIFs, video and fonts.
- **Visual editing**: Adjust text styles, media sizes and layout, or edit, import and export Markdown.
- **Author presets**: Choose the configuration profiles included in the package and specify the default profile used on first installation.
- **Conflict advice**: Explain why certain mod combinations are not recommended and distribute the advice with the modpack.
- **Export formats**: Export author data only, a complete local package or a Thunderstore release package.
- **Store dependencies and local files**: List mods available on the store as dependencies, and include local mods and other resources according to your selections.
- **Newcomer ZIP**: Complete local packages can also include BepInEx startup files and English/Chinese installation instructions for players who do not use a mod manager.
- **Automatic startup-patch collection**: Complete packages include current startup patches by default. Uncheck items in "Additional files" to exclude them and review the final preview.
- **Private-value filtering**: Clear recognized private configuration values in exported copies, such as keys, tokens, passwords and proxy addresses containing credentials. Original files remain unchanged.
- **Author-data signing**: Sign profiles, translations, introductions and referenced assets so players can check the integrity of the author data.

### Translation

Translate the interface, mod names and descriptions, and easily fill in missing translations.

- **Three built-in languages**: English, Simplified Chinese and Traditional Chinese work without installing language files.
- **Mod text translation**: Translate mod names, summaries, settings sections, option names, descriptions and dropdown choices.
- **Edit translated names directly**: Double-click a mod name, summary or short keybind description in the overview to edit its text in the current language.
- **Export texts for translation**: Export all texts or only untranslated entries, optionally limited to selected mods.
- **Import supplements**: Select a completed translation file to merge it into the current language file. A backup is created before merging.
- **Compare originals and translations**: Switch between original and translated text, and reload language files directly after editing them.
- **Custom languages and regular expressions**: Add more languages, or write regular-expression translations for sentences that differ only in numbers or names.
- **Ship translations with modpacks**: Author translations travel with the package, and personal translations can override them.
- **No XUnity requirement**: The manager handles translation itself. If XUnity is installed with online machine translation enabled, you will be prompted to choose how to handle it.

### Gamepad operation

Use a controller across lists, settings, the store, logs and dialogs. The bottom hint bar shows the controls currently available.

- **Move focus**: Navigate with the D-pad or left stick. Moving up and down a list keeps the same column and skips unavailable cells.
- **Confirm and return**: A confirms, enters or starts editing. B leaves the current operation, then returns one level at a time.
- **Switch tabs**: LB/RB switch tabs in the current window.
- **Additional actions**: Hold RT for actions such as group jumps and paging shown in the hint bar. Y opens the selected mod or group's menu.
- **Read content**: Select a store description, log body or help page, press A to lock reading mode, and press B to unlock it.
- **Scroll**: Use the right stick to scroll. In locked reading mode, D-pad scroll steps are three times the ordinary size; stick scrolling retains its own speed.
- **Text fields**: Press A to start typing. Steam's on-screen keyboard is available in Big Picture mode or on Steam Deck.
- **Restore focus**: After returning from another window or using the mouse, resume with the controller to recover focus.

Authoring tasks such as introduction layout, multiple selection and drag-and-drop organization use the keyboard and mouse.

### Logs and everyday tools

Inspect information in the game when something goes wrong, and share the relevant details with mod authors.

- **Log viewer**: View BepInEx logs, game logs and custom logs. Search and filter by mod, level or message text.
- **Copy and export**: Copy filtered results or export complete logs. Optionally include mod information, versions, configuration differences and pending-restart items. Sensitive configuration values in the attached information are masked.
- **Gamepad export**: When using a controller, save directly to the export folder, with a button to open that folder.
- **Built-in guide**: Read feature instructions in the game. Enable editing to save your own guide versions per language.
- **Blue and purple themes**: Switch themes manually, or use automatic purple night mode when TimeTheme is installed.


## Installation and dependencies

Install with r2modman or Thunderstore Mod Manager to install the required dependencies automatically:

- BepInExPack PEAK
- PEAKLib.UI and its dependencies
- ModConfig 1.8.2 and its dependencies

Open **Settings → Mod overview** from the main menu or pause menu. **Mod settings** opens the settings tree. The overview toolbar's **Management center** provides profiles, modpacks, publishing and other tools.

IGMM and ModConfig Enhance provide overlapping settings interfaces; choose one for your setup. TimeTheme is optional, and the translation tools do not require XUnity AutoTranslator.

## Gamepad controls

| Control | Action |
|---|---|
| Left stick / D-pad | Move focus between controls. Vertical movement in a list keeps the same column and skips unavailable cells. |
| A | Confirm, open a menu or begin editing a field. On a reading pane, lock focus to browse its contents. |
| B | Leave the current edit or reading mode, then close menus and windows one level at a time. |
| LB / RB | Switch overview tabs. |
| RT | Hold for the additional actions shown in the bottom hint bar. |
| Right stick | Scroll content. |
| D-pad in reading mode | Scroll with a step three times the ordinary size; stick scrolling keeps its own speed. |

Use the on-screen hints for actions specific to the selected control.

## Language files and personal data

Personal files are kept under `BepInEx/config/InGameModManager/`:

| Folder | Contents |
|---|---|
| `language/` | Custom language files; one file per language. Maintain them with Export translations, Missing translations and Import translations. |
| `profiles/` | Personal configuration profiles. |
| `output/translation/` | Exported texts for translation. |
| `output/packs/` | Exported modpacks and introductions, unless another output folder was chosen. |
| `output/logs/` | Exported logs when a system save dialog is not used. |
| `data/` | Local state, backups and saved help edits. |

The modpack library, update tasks and helper cache live under `BepInEx/IGMM/`. The manager's own configuration file is `BepInEx/config/com.aiae.ingamemodmanager.cfg`.

Complete modpacks include startup patches by default. Uncheck a patch in "Additional files" to exclude it. Values recognized as private are cleared in exported configuration copies, leaving the original files unchanged. Review the contents in the final file preview before sharing.

[Full introduction and guide](https://aiae-156.github.io/Peak-InGameModManager/%E6%96%87%E6%A1%A3/%E4%BB%8B%E7%BB%8D%E9%A1%B5/index.html) · [Feedback](https://github.com/AiAe-156/Peak-InGameModManager/issues)

---

<a id="中文说明"></a>

# 游戏内模组管理器

[English](#english) | **中文**

一个无需依赖第三方模组商店即可在游戏内对模组进行最基本的管理的模组。 可直接在游戏内预览模组列表、模组版本、模组依赖树等信息。改配置、改快捷键、安装、更新、启停、卸载，以及整合包的制作和切换，都整合在同一套界面里。

ModConfig Enhance 主要让模组设置更好找、更好懂；游戏内模组管理器在它的基础上，进一步管理模组本身和整套模组环境。

**0.0.1 · 首个公开测试版**

![模组概览](https://raw.githubusercontent.com/AiAe-156/Peak-InGameModManager/main/%E6%96%87%E6%A1%A3/%E4%BB%8B%E7%BB%8D%E9%A1%B5/assets/overview-blue.jpg)

**[查看完整介绍](https://aiae-156.github.io/Peak-InGameModManager/%E6%96%87%E6%A1%A3/%E4%BB%8B%E7%BB%8D%E9%A1%B5/index.html)**

测试版，有bug 或者意见可以加qq群：1104320838

---

### 模组管理

一页看全所有模组，哪个有更新、哪个没生效、谁依赖谁，一眼就知道。

- **按分组排列**：分组可以嵌套三层，支持折叠、调整顺序和移动成员，也可以按现有文件夹生成初始分组。
- **搜索与筛选**：按名称、作者、关键词和标签查找模组，隐藏停用项，或只看需要处理的内容。
- **状态角标**：有更新、未读变动、改了没保存、等待重启、资源包……每种状态一个颜色，位置固定，点开可以查看详情。
- **一键启停**：勾选即可设置模组的启停目标，保存后在下次启动游戏时生效，无需手动改文件名。
- **依赖与主页**：查看模组之间的依赖关系、缺失项和版本要求，打开商店页、项目主页或本地文件夹。
- **重复模组识别**：同一个插件装了多份时，显示各自的版本和路径，方便选择保留哪一份。
- **基础组件与资源包**：除了普通插件，也能查看共用运行库、启动补丁和只有资源文件的模组包。
- **目录整理**：按分组整理 plugins 文件夹，只移动、不改名，执行前先看预览。

### 模组配置

所有模组的设置集中在一棵树里，找得到、改得明白。

![模组设置页](https://raw.githubusercontent.com/AiAe-156/Peak-InGameModManager/main/%E6%96%87%E6%A1%A3/%E4%BB%8B%E7%BB%8D%E9%A1%B5/assets/settings-tree.jpg)

- **树形侧栏**：按分组、模组和设置分区排列，可以搜索、筛选类型，也可以置顶常用模组。
- **说明面板**：选中设置项后，显示它的说明、默认值、可选范围或下拉选项；选中模组时查看模组介绍。
- **修改立即写入**：设置页直接使用 ModConfig 写入配置，改完即可保存到实际配置文件。
- **恢复默认与清除**：在设置项旁查看默认值，并恢复默认或清除对应内容。
- **生效时机提示**：区分立即生效、重新开局生效、重启游戏生效，以及由模组自行决定的设置。

### 快捷键集中管理

把各个模组分散的快捷键放在同一页，键盘和手柄分别设置。

- **键盘与手柄两列**：用键鼠录键盘列，用手柄录手柄列；未绑定的位置显示「未设置」。
- **录完立即写入**：直接修改当前按键后立即保存，也可以查看模组默认按键和方案预设。
- **手柄组合键**：可以给只支持键盘快捷键的模组分配手柄按键或组合键。
- **自选修饰键**：支持无、LT、RT、LB、RB、Back、Start、RS、LS，默认 Back。所有组合共用一个修饰键，修改后已录制的组合一起调整。
- **重复键提示**：检查模组之间以及与游戏操作的按键冲突，分别在键盘列和手柄列标出。
- **按键图标**：使用游戏按键图标显示绑定，方便对照实际手柄。
- **录制锁定**：录制期间集中接收手柄按键，按住 B 一秒结束录制。

### 多配置切换

同一套模组可以有好几套配置，比如「日常」和「整活」，按需要切换。

- **配置方案**：每套方案记录参数、快捷键和模组的启停目标，可以新建、复制、改名、导入和导出。
- **切换先查看**：选中另一套方案后先查看内容，确认要使用再保存配置。
- **记录当前配置**：把当前实际使用的设置收录进方案，方便以后恢复或分享。
- **变更检查**：在「变更检查目录」核对方案里的改动、原值、新值，以及什么时候生效。
- **作者预设与个人修改分开**：保留整合包作者提供的原始预设，个人修改可以另存，也可以恢复作者预设。
- **共用整合包资料**：同一整合包的各套方案共用分组、介绍、媒体和模组译名。

### 商店与更新

在游戏里浏览雷霆商店，安装和维护模组。

- **来源识别**：自动查找模组对应的商店包；结果不明确时，可以手动选择，也可以标记为本地模组。
- **安装与版本选择**：查看模组介绍、依赖、更新日志和版本，选择需要的版本安装。
- **更新检查**：发现新版后显示提示，由你选择要更新哪些模组；也支持单独控制各模组的更新检查。
- **依赖一起处理**：安装或更新时，根据商店依赖关系补齐需要的组件。
- **任务队列**：查看下载进度、速度和历史，暂停、继续、停止或重试任务。
- **退出后完成替换**：下载和准备可以在游戏运行时进行；需要替换已加载文件时，退出游戏后由附带助手完成。
- **备份与回退**：操作前备份，更新后可以回退到之前的文件，并选择是否恢复之前的参数。
- **网络设置**：支持系统代理、直连和自定义 HTTP(S) 代理。

更新助手只在有任务时启动，提供独立的进度窗口，任务结束后退出。支持 r2modman 和雷霆管理器的档案布局。

### 整合包

把一整套模组环境存起来，换着玩，也能打包发给朋友。

- **整合包库**：保存多套模组环境，每套分别保留自己的模组版本和配置。
- **导入先预览**：查看包里有哪些模组、文件和依赖，需要下载什么、还有什么问题，再决定存入或应用。
- **存入与启用分开**：可以先把整合包存进库，之后再预览切换影响。
- **保留当前环境**：第一次切换前，可以把当前环境保存为临时整合包，方便以后切回来。
- **切换有备份**：退出游戏后完成环境替换，保留切换前的状态供恢复。
- **环境版本存档**：保存整合包当前状态，分别管理已保存版本和当前修改。
- **一套环境多套方案**：同一个整合包里可以提供多种玩法配置，玩家自行选择。
- **空间管理**：查看整合包、下载缓存、备份和回收区占用的空间，按需要清理。

### 制作与发布整合包

从整理配置、编写介绍到生成发布包，都可以在游戏里完成。

- **图文介绍**：给整合包编写介绍，支持标题、列表、链接、图片、动图、视频和字体。
- **可视化编辑**：调整文字样式、媒体尺寸和排列，也可以编辑、导入或导出 Markdown。
- **作者预设**：选择随包提供的配置方案，指定首次安装时使用的默认方案。
- **冲突建议**：为不建议同时启用的模组组合填写原因，随整合包分发给玩家。
- **多种导出格式**：可以只导出作者资料，也可以生成本地完整包或雷霆商店发布包。
- **商店依赖与本地文件**：商店里有的模组可以写成依赖，本地模组和其他资源按选择随包附带。
- **新手版 ZIP**：本地完整包还可以附带 BepInEx 启动文件和中英文安装说明，供没有使用模组管理器的玩家安装。
- **启动补丁自动收集**：完整包默认包含当前启动补丁，可以在「附加文件」里取消勾选以排除，并在最终预览中核对。
- **导出脱敏**：清空导出副本中识别为私密信息的配置值，例如密钥、令牌、密码和带账号的代理地址，原文件保持原样。
- **作者资料签名**：可以为方案、译文、介绍和引用素材签名，供玩家核对作者资料是否完整。

### 多语言翻译

界面和模组的名字、说明都能翻译，补翻也很方便。

- **内置三种语言**：英文、简体中文、繁体中文，不装语言文件也能使用。
- **模组文字也能翻**：覆盖模组名称、简介、设置分区、选项名、说明和下拉选项。
- **直接修改译名**：在概览中双击模组名称、简介或快捷键短说明，编辑当前语言的对应译文。
- **一键导出待翻文本**：导出全部文本，或只导出还没有翻译的内容，也可以限定为选中的模组。
- **导入补翻**：填好译文后选择文件，合并到当前语言文件，合并前自动备份。
- **原文与译文对照**：切换显示原文或译文，修改语言文件后可以直接重载。
- **自定义语言与正则规则**：添加其他语言，也可以为只差数字、名称的句子编写正则翻译。
- **跟着整合包走**：作者补好的译文随包分发，个人译文可以覆盖作者译文。
- **不需要 XUnity**：翻译由管理器自己完成；装了 XUnity 并启用在线机翻时，会提示你选择如何处理。

### 手柄操作

从列表、设置到商店、日志和弹窗，都可以用手柄操作，底部提示栏显示当前可用按键。

- **移动焦点**：十字键或左摇杆导航，列表上下移动时保留所在列，跳过不可用的格子。
- **确认与返回**：A 确认、进入或开始编辑；B 退出当前操作，再逐层返回。
- **切换页签**：LB／RB 切换当前窗口的页签。
- **附加操作**：按住 RT，使用提示栏里的分组跳转、翻页等功能；Y 打开所在模组或分组的菜单。
- **正文浏览**：选中商店介绍、日志正文或帮助页面后，按 A 锁定浏览，按 B 解除锁定。
- **滚动**：右摇杆滚动内容；锁定正文后，十字键滚动步长为普通步长的三倍，摇杆保持自身速度。
- **输入框**：按 A 开始输入。在 Steam 大屏幕模式或 Steam Deck 上，可以调用 Steam 屏幕键盘。
- **焦点恢复**：从其他窗口返回或使用鼠标后，可以继续用手柄找回焦点。

介绍排版、多选和拖动整理等作者操作使用键鼠。

### 日志与日常工具

遇到问题时，在游戏里查看信息，也方便把需要的内容发给模组作者。

- **日志查看**：查看 BepInEx 日志、游戏日志和自定义日志，按模组、级别或正文搜索筛选。
- **复制与导出**：复制筛选结果，或导出完整日志；可选择附带模组、版本、配置差异和待重启信息，附带的敏感配置值会遮盖。
- **手柄导出**：使用手柄时直接保存到导出文件夹，并提供打开文件夹入口。
- **内置使用说明**：在游戏里查看各功能的说明；开启编辑后，也可以按语言保存自己的说明版本。
- **蓝色与紫色主题**：手动切换主题，也可以在安装 TimeTheme 后选择夜间自动使用紫色。


## 安装与依赖

使用 r2modman 或雷霆商店管理器安装，以下必需依赖会一并安装：

- BepInExPack PEAK
- PEAKLib.UI 及其依赖
- ModConfig 1.8.2 及其依赖

进入主菜单或暂停菜单的「设置 → 模组概览」；「模组设置」打开设置树。概览工具条的「管理中心」提供方案、整合包、发布等工具。

IGMM 与 ModConfig Enhance 的设置界面功能重叠，按需要选装其中一个即可。TimeTheme 为可选项；翻译工具无需安装 XUnity AutoTranslator。

## 手柄操作

| 按键 | 操作 |
|---|---|
| 左摇杆／十字键 | 在控件间移动焦点；列表上下移动时保留所在列，跳过不可用的格子。 |
| A | 确认、打开菜单或开始编辑；选中正文后按 A 锁定浏览。 |
| B | 先退出当前编辑或正文浏览，再逐层关闭菜单和窗口。 |
| LB／RB | 切换概览页签。 |
| RT | 按住后使用底部提示栏显示的附加操作。 |
| 右摇杆 | 滚动内容。 |
| 正文浏览中的十字键 | 滚动步长为普通步长的三倍；摇杆保持自身速度。 |

各控件的专属操作以底部提示为准。

## 语言文件与个人数据

个人文件位于 `BepInEx/config/InGameModManager/`：

| 文件夹 | 内容 |
|---|---|
| `language/` | 自定义语言文件，一种语言一个文件；通过「导出翻译」「漏翻文本」「导入补翻」维护。 |
| `profiles/` | 个人配置方案。 |
| `output/translation/` | 导出的待翻文本。 |
| `output/packs/` | 导出的整合包与介绍，另选输出位置时以所选位置为准。 |
| `output/logs/` | 未使用系统保存对话框时导出的日志。 |
| `data/` | 本地状态、备份和已保存的帮助编辑。 |

整合包库、更新任务和助手缓存位于 `BepInEx/IGMM/`；管理器自身配置为 `BepInEx/config/com.aiae.ingamemodmanager.cfg`。

完整整合包默认包含启动补丁，可在「附加文件」中取消勾选以排除。导出时会清空配置副本中识别为私密信息的值，原文件保持原样；分享前可在最终文件预览中核对内容。

[完整介绍与使用说明](https://aiae-156.github.io/Peak-InGameModManager/%E6%96%87%E6%A1%A3/%E4%BB%8B%E7%BB%8D%E9%A1%B5/index.html) · [问题反馈](https://github.com/AiAe-156/Peak-InGameModManager/issues)
