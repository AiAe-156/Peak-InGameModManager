# 游戏内模组管理器

不用退出游戏、不用开管理器，在 PEAK 的设置菜单里就能看到装了哪些模组、各自是什么版本、谁依赖谁，改配置、改快捷键、更新、启停、卸载，都在同一个界面里完成。

![模组概览](https://raw.githubusercontent.com/AiAe-156/Peak-ModConfigEnhance/master/%E5%9B%BE%E7%89%87/%E7%99%BD%E5%A4%A91.png)

**[查看完整介绍](文档/介绍页/index.html)**

---

### 模组管理

一页看全所有模组，哪个有更新、哪个没生效、谁依赖谁，一眼就知道。

- **按分组排列**：分组可以嵌套三层，拖动就能调整；首次进入会按你现有的文件夹自动分好组。
- **状态角标**：有更新、改了没保存、等重启、资源包……每种状态一个颜色，位置固定。
- **一键启停**：勾选即可，不用手动改文件名；同一个模组装了两份时，选一份用，另一份自动停用。
- **更新、安装、卸载**：直接从雷霆商店检查和下载。要替换正在运行的文件时，等你退出游戏后由小助手完成，改动前全部备份，出问题可以回退。
- **依赖与主页**：悬停看依赖关系，点主页按钮跳到模组的商店页或项目页。
- **目录整理**：按分组把 plugins 文件夹整理好，只移动、不改名，执行前先看预览。

### 模组配置

所有模组的设置集中在一棵树里，找得到、改得明白。

- **树形侧栏**：按分组和模组排列，可以搜索、可以置顶常用的模组。
- **说明面板**：选中一个设置项，上方显示它的说明、默认值和可选范围。
- **快捷键集中管理**：所有模组的快捷键放在同一页，重复的按键会标出来，点一下直接录新按键。
- **先改后存**：改动先放进草稿，保存前能在「变更检查目录」核对改了什么、什么时候生效（立即、重新开局还是要重启）。

### 多配置切换

同一套模组可以有好几套配置，比如「日常」和「整活」，随时切换。

- **配置方案**：每套方案记下所有参数、快捷键和模组的开关。
- **切换只是查看**：切过去先看，确认要用再保存，不会误改。
- **作者预设与个人修改分开**：整合包作者给的预设保持原样，你自己的修改另存一份，随时能恢复作者的设置。

### 整合包

把一整套模组环境存起来，换着玩，也能打包发给朋友。

- **整合包库**：可以存好几套完整的模组环境，每套保留自己的模组版本和配置。
- **导入先预览**：导入时先看里面有什么、缺什么依赖，存进库里不会动你现在的游戏文件。
- **切换有备份**：切换时会先保存当前环境，退出游戏后自动完成替换，随时能切回来。
- **写介绍**：给整合包写图文介绍，支持图片、动图和视频，玩家打开就能看到。
- **发布**：导出成本地完整包，或者雷霆商店包（商店里有的模组自动写成依赖）；可以给作者资料签名，防止被改。

### 多语言翻译

界面和模组的名字、说明都能翻译，补翻也很方便。

- **内置三种语言**：英文、简体中文、繁体中文，不装语言文件也能用。
- **模组名称和说明也能翻**：在列表里双击名称或简介就能改成你习惯的译名。
- **一键导出待翻文本**：导出全部或只导出还没翻的，填好点「导入补翻」直接合进语言文件。
- **跟着整合包走**：整合包作者补好的译文会随包一起发给玩家。
- **不需要 XUnity**：模组设置页的选项名、分区和下拉选项也由管理器自己翻译；只差数字的句子可以写正则规则。装了 XUnity 并开着在线机翻时会提醒，可以一键关掉。

---

### 安装

用 r2modman 或雷霆商店管理器一键安装，依赖会自动装好。进游戏后，在主菜单或暂停菜单的「设置」里打开「模组概览」。

**不推荐手动安装**：依赖比较多，有的还要放进 BepInEx 的 core 文件夹。请用模组管理器安装；自动部署脚本以后会放在 GitHub 页面上。

---

**[完整介绍、截图和使用说明](文档/介绍页/index.html)**　·　[更新日志](CHANGELOG.md)　·　[问题反馈](https://github.com/AiAe-156/Peak-ModConfigEnhance/issues)

---

## Release notes / 版本说明

## 2.5.4 修复：文件夹整理更稳、作者整合包不再覆盖个人配置

- 整理配置文件夹时，没搬完的文件夹继续用原来的位置；个别文件搬不动（被占用、路径太长）就留在原处，其余照搬；每次启动都会把旧文件夹里剩下的并进来。
- 没在用整合包库时，装作者整合包不会再把你的配置换成作者的默认方案。
- 「导入补翻」跳过没翻的行；正则规则的结果与 XUnity 一致；原文以反斜杠结尾的条目不会再让整个语言文件读不出来。
- 从设置页跳快捷键页时中途离开，不会之后突然跳过去。

## 2.5.3 说明跟上新目录

游戏内使用说明、配置目录里的 readme、悬停提示和介绍页里的文件位置都改成了新的四个文件夹；使用说明的「翻译模块」新增「文件放在哪」一节。「打开文件夹」菜单多了「导出的整合包」。

## 2.5.2 配置文件夹整理成四类

`BepInEx/config/ModConfigEnhance/` 顶层只剩 `language`（语言文件）、`profiles`（配置方案和整合包介绍）、`output`（导出的翻译和整合包）、`data`（运行数据、自动备份、说明覆盖、旧版数据）和 `readme.md`。第一次启动时自动搬过去，搬之前先备份到 `BepInEx/MCE/layout-backups/日期/`。

## 2.5.1 「跳转到快捷键设置页」能点了

从模组设置页点它会打开概览并切到快捷键设置页；按钮靠右，换成米黄纸面。旧语言文件里过时的 XUnity 提示不再盖住新文字。

## 2.5.0 翻译全部自己完成，不再依赖 XUnity

**不再依赖 XUnity AutoTranslator。** 商店依赖里去掉了它。模组设置页里各模组的选项名、分区和下拉选项现在由管理器自己按语言文件翻译（以前只能靠 XUnity）。不再往 XUnity 目录写词典、不再改它的语言、不再常驻拦截它的机翻。

**XUnity 开着在线机翻时提醒。** 每次启动第一次打开概览提醒一次，可以选「关闭它的在线机翻」（先备份 `AutoTranslatorConfig.ini` 再清空 Endpoint，下次启动生效）、「知道了」或「不再提醒」。

**正则规则。** 语言文件支持 `r:"正则"=译文`，`$1` 引用括号里的内容，格式和 XUnity 词典一样，可以直接搬。整句译文优先，多条正则命中时写在后面的优先；写错的规则跳过并提示行号。

**工具行。** 「显示原文 / 显示译文」「重载翻译」改成自己的功能，补漏翻不再需要 XUnity。新增「导入补翻」：选一个填好的导出文件，有译文的行合进当前语言文件；「打开目录」改成方形图标钮。

**语言目录。** 一种语言一个文件；不再自动写界面词条快照；概览里改的译名直接写进该语言文件。首次启动时把同一语言的多个文件合并成一个（显示结果不变），原文件移进 `language/_迁移前备份_日期/`；以前写进 XUnity 目录的镜像词典也移进备份。（导出目录的位置见 2.5.2。）

## 2.3.5 基础组件可以改译名和启停

**改译名。** 在概览里点基础组件的名称，详情窗里多了「改译名」：填一个显示名称，列表里显示「译名（包名）」，留空恢复包名。只改显示，不动文件。

**启停。** 同一个窗口里有「停用 / 启用」：把这个组件的程序集改名为 `.dll.d`（或改回 `.dll`），从下次启动起生效。启用状态在最左列显示为 ✓ / ○。MCE 自己运行所依赖的组件不能停用（可以启用）；被其他工具停用的组件请用原工具恢复。

基础组件照旧固定在「基础组件」分组，不能改分组，也不参与目录整理。

## 2.3.4 识别更全：资源包、任意后缀停用的 DLL、散放 DLL 都会显示

**只有 manifest、没有 DLL 的包（资源包）现在会显示。** 比如 Aeralis 的雾效和光追资源包，里面只有 .bundle 和贴图，以前概览里根本看不到；现在作为普通一行显示，名称旁有绿色小方块角标「资源包」，可以启停（改名它的 manifest）、分组、整理。

**DLL 后面接任何字符都认作已停用。** 以前只认 `xxx.dll.<后缀>`，像 `ModConfigEnhance.dll1`、`xxx.dllx` 这种认不出来，整个模组就消失了；现在都显示为已停用，可以直接在概览里启用（改回 `.dll`）。`.dll.config`、`.dll.mdb`、`.dll.pdb`、`.dll.xml` 这类附属文件不算。MCE 自己停用时仍写 `.d`。

**直接放在 plugins 根下的单个 DLL** 有紫色小方块角标「散放」，悬停说明目录整理会把它收进单独的文件夹。

**开发侧：** 本分区新增部署前路径检查 `MODs/_tools/check_deploy_dir.py`，部署目标与现装位置不一致就停下；MCE 部署脚本已内置。整理后失效的 11 个工程部署路径已改到现装位置。

## 2.3.3 退出游戏后的整理/更新会弹出完成窗口；基础组件找回 MonoDetour；小窗口不再铺满屏幕；子分组与新建分组同一个编辑器

**退出游戏后有结果了。** 等游戏退出再执行的目录整理、更新、回退，原先在游戏还没完全关掉时就悄悄做完，没有任何提示。现在只要这次真的改了文件，结束后会弹出结果窗口：全部完成写「已完成，下次启动生效」，20 秒后自动关闭；有失败写「已完成，有问题」，停留 10 分钟方便查看。

**基础组件少了两个，找回来了。** MonoDetour 与 MonoDetour_BepInEx_5 的 DLL 直接放在 `core`/`patchers` 根下，说明文件放在各自的文件夹里；概览只在文件夹里找 DLL，于是这两项一直没列出来（文件本身完好、照常加载）。现在按这两个包自己的文件名认出来，基础组件恢复为 5 项。

**窗口按内容大小。** 新建分组/改名/输入地址这类只有一两个输入框的窗口、确认框、选择列表、颜色输入、删除确认原先都按屏幕比例铺开（最大的占屏幕 66%×80%），现在按内容定高、居中显示；标题较长的确认框会自动换行。

**新建子分组与新建分组一样。** 分组标题右键「新建子分组」和概览里的「新建分组」现在打开同一个分组编辑器，带颜色、说明和直接勾选模组，上级分组已预先选好。

**空分组也显示。** 刚建好的子分组、或成员都移进了子分组的分组，原先在非编辑模式下整个看不见，像没建成一样；现在都会显示（计数为 0）。搜索、标签筛选、相关筛选时仍只显示有匹配的分组。

## 2.3.2 主菜单里的自动更新检查不再卡顿；修复更新助手的几处隐患

**主菜单不再每分钟卡一下。** 自动更新检查原先整段在主线程跑，每次都会让画面停 0.3～0.5 秒；现在放到后台。另外，已弃用或商店里找不到的模组以前会被当成「检查失败」每 60 秒重试一次，整轮检查跟着重跑；现在这类确定答复按正常间隔（30 分钟）处理，只有断网、超时这类临时失败才会 1 分钟后重试。

**更新助手更稳。** 同时安装几个模组时，偶尔会因为内部记录被同时修改而让某个任务莫名失败，已修复。助手如果一启动就出错，现在最多只弹一个错误框，不会在游戏后面叠出一串卡住的进程。点更新等操作时不再重复校验助手文件（每次少算约 12 MB 的哈希）。

**使用 r2modman / 雷霆商店档案的：** 写 `mods.yml` 改为先写临时文件再替换，中途崩溃不会留下残缺文件；在档案模式下启停模组只改对应条目的 `enabled:` 一行，不再把整个文件重新排版。

**Clash TUN（fake-ip）用户：** 不走代理时，商店图片与说明页不再因为域名被解析到 198.18.x.x 而全部加载失败（更新助手早就允许这种情况）。真实的内网地址仍然拒绝。

## 2.3.1 目录整理窗口加大，空文件夹打勾随整理一起放进回收站；修复嵌套分组的几处数据问题

**目录整理窗口变大了。** 窗口按屏幕放大，标题、状态说明、数量标签和筛选按钮压成两行，下面的文件夹列表能显示的内容多了一倍多。

**空文件夹不用再一个个点删除。** 「空文件夹」一栏有一个总复选框「整理时一并放进回收站」，每个文件夹前面也有自己的复选框，默认都勾上，不想删的取消即可。点「确认整理」后，等游戏退出、所有移动都成功了，助手才把勾上的文件夹放进回收站；放之前逐个再查一遍，已经不空的会跳过并在记录里提示。整理失败回退时一个都不删。之前点「删除」会弹出一个写着「保存」的大空窗，点完还跳回管理中心，这个窗口已经去掉。

**修复：** 方案里有子分组时，应用一次后重启会打不开概览；新版导入不了自己发布的含子分组的整合包；首次「按文件夹分组」后模组可能没有归进分组；停用过的模组在目录整理和智能分组里不被认作模组；外层模组停用时，内层若是不能启停的基础库会让整个保存失败，只记了外层停用的方案落盘时内层不会跟着停用；回退整理会删掉你原本就有的空分组文件夹；概览里删除/挂接/新建分组保存失败时界面不会恢复原样；删除分组后上移的子分组位置不对；两个色系有一个颜色重复。

## 2.2.11 目录整理预览不再卡在「正在准备文件」，更新行显示下载速度与用时

**目录整理不会再卡住了。** 预览做完后会弹出核对窗口；之前如果点「返回」或关掉窗口，这份预览就一直留在助手那里，队列里显示成「正在准备文件」，既不会结束也没有取消按钮——而且它被当成一项还没做完的工作，助手因此在游戏运行时每 10 秒被叫起来一次。现在：①核对窗口点「返回」即关闭这份预览，不改动任何目录；②队列里显示为「预览完成 · 等待确认」，带一个「关闭预览」按钮；③发起预览的那次游戏已经退出的，助手下次启动时自动关闭它；④关闭预览不必等正在进行的更新。

**更新行加了下载速度和用时。** 版本号与百分比之间多一行灰色小字，例如「1.2 MB/s | 3.4 秒」，按本次实际下载的字节计算（复用缓存或续传前已有的部分不计）。

## 2.2.10 更新助手不再每 10 秒被拉起一次

**游戏开着时，更新助手不会再每 10 秒启动、退出一轮了。** 原因是队列里有几条很早以前的「已安装，等待重启核对」记录，它们装下的文件后来被目录整理挪走、改成停用名，或者被新版本替换，任何一次启动都不可能再确认它们；而游戏侧只要看到还有「等待重启」的记录，就每 10 秒叫一次助手去核对。现在：①助手发现这类记录装下的文件已经不在原处或内容已变，就把它标为「已被后续操作接替」并结束等待（回退备份照旧保留）；以停用名（`.dll.*`）装下、本次启动确实没加载的，视为已完成；②游戏侧对同一批「等待重启」记录每次启动只核对一次，记录变了才会再问。

## 2.2.9 卸载不再误报「依赖信息不完整」

**2.2.8 之后仍有一批模组卸载是灰的，提示「依赖信息不完整，暂不能卸载」，这次修好了。** 读取模组的依赖声明时需要 BepInEx 本体来解读其中的一个标志位，而读取器没被告知去哪里找 BepInEx —— 于是凡是写了「可选依赖」一类标志的模组（这次反馈的天阶，以及 PEAKLib.Items、时间主题、景观台、So_Fly 等）都读到一半失败，被当成「信息不完整」挡掉了卸载。现在读取器会到 BepInEx 的 core 目录与游戏自带程序集里查找，这类误报消失；卸载照旧先进安全预览、核对文件归属与依赖。

## 2.2.8 卸载保护改为固定清单，名称列角标统一

**该能卸载的现在都能卸载了。** 「此组件是MCE必需依赖，仅可更新」这条判断原先靠遍历依赖图得出，而收集器在遇到解析不出的程序集引用时，会给 MCE 额外连一条指向所有「读不出来的程序集」的硬边 —— 于是任何读取失败的模组都被算成 MCE 的依赖，卸载被直接禁用。现在保护改钉一份固定的短清单（MCE 唯一硬依赖的 ModConfig 宿主，以及它引用的两个 PEAKLib 程序集），其余绝大多数模组都能正常卸载；确实被 MCE 依赖的那几个仍会提示，但改成**先确认再继续**，不再一刀切禁掉。

**名称列右侧那几个小角标统一了。** 未读变动、有新版本、待保存、待重启原本是四套画法：一个正圆一个圆角方形、8 像素与 7 像素混用、位置一套按右上角算一套按左边算 —— 所以「有新版本」旁边出现未读变动时还会跳位。现在它们共用同一个几何：8×8 圆角方形，同一行右对齐、间距 11，从名称列右上角起算，标题预留宽度也按本行实际角标数计算。

**版本提示不再重复一句「有新版本」。** 悬浮/状态里原本会写两遍：一次带版本号，一次是光秃秃的检查消息。

## 2.2.7 支持管理器档案，整理预览改为结构化树

**在 r2modman / 雷霆商店的档案里也能用了。** 这类管理器把模组装在 `%APPDATA%` 下的档案目录里（`...\PEAK\profiles\<档案>\BepInEx\plugins`），BepInEx 的父目录是档案目录、不是游戏目录。以前凡是把「BepInEx 的父目录」当游戏根的地方都会指错 —— worker 找不到 `PEAK_Data\Managed` 会误报缺少程序集，整合包核心文件校验会找不到 `winhttp.dll`/`doorstop_config.ini` 而阻断切换，导出时「输出目录不能在游戏安装目录内」这条也失效了。现在游戏侧把真实游戏目录明确传给 worker（`--game-root`），只在真正需要游戏根的地方使用；用于判定路径越界的边界仍旧是 BepInEx 的父目录，因为档案本来就在游戏目录之外。

**管理器档案不再被整体拒用。** 原来的守卫一见到档案根有 `mods.yml` 就拒绝，等于把更新、卸载、本地导入、整合包切换和恢复全部关掉。现在改为识别该档案，并在更新或卸载提交后把 `mods.yml` 里的包版本同步过去：worker 只记下「哪个包、改成哪个版本」（或卸载时删除条目），由插件在下次启动时改写 —— 管理器（r2modman/Thunderstore）正在运行时不动它的文件，留到下次启动；改写前会备份到 `MCE/manager-sync-backups/`。

**整理预览改成结构化的目录树。** 确认窗口以前是一串扁平的文件路径，现在按 `plugins/` 画成树，每个包标注它是原地未动还是从哪移来，以及来源是「本地」「待定」还是「商店已确认」；「分组并使用译名」也不再给包目录加 `--哈希` 后缀，优先用可读的名称。

## 2.2.6 切页进入也分帧

**打开概览的四段各自分帧。** 2.2.5 把建壳、绑定、切页分开后，实机单帧从 381ms 降到 283ms，但定位显示剩下那 278ms 依旧整块落在切页那一帧里。现在把首次进入的重活也做成独立一段：纸面样式解析、扫页面树套样式、页面准备（含仓库与列表重载）、配置观察者，全部在页面仍未激活时分帧跑完；切页时只做需要已激活页面的事（页头重排、打开列表、状态行）。后续再次进入概览的路径与行为完全不变。

## 2.2.5 首次打开概览不再单帧卡顿

**打开概览的三段各自分帧。** 打开概览要做三件事：建好页面外壳、把模组仓库绑定到页面、切页进入。建壳本来就分帧了，但外壳一跑完，绑定和切页会挤在**同一帧**里跑完 —— 实测那一帧 381ms。现在三段各自成为独立的一段，共用同一条「每帧约 4ms、最多 8 步」的预算；绑定本身也按建造顺序拆成八步（仓库接线 / 页头文本 / 内容区与状态行 / 工具条 / 模组列表 / 快捷键视图与回调 / 悬浮提示 / 页头重排）。页面在切页前一直是未激活的，所以行为与外观不变，只是不再一帧付清。

## 2.2.4 启动日志不再重播历史，自动检查与目录整理预览大幅减负

**运行日志不再每次重播历史任务。** 队列记录的播报去重表是进程内的，每次启动都是空的，于是启动时会把队列里所有任务挨个重播一遍 —— 包括上一轮早已结束的「已回退」「已移除记录」「已生效」。现在本次会话首次看到就已经是终态、且属于纯信息性的记录（完成 / 已移除 / 已回退 / 已被接替）不再播报；回滚真正发生的那一次照旧会报，失败与需要处理的状态也照旧会报。启动日志从三十多行降到只剩真正待办与真错误。

**自动检查更新不再卡主线程。** 每次检查原本会在主线程上 `new OverviewRepository()` —— 而仓库构造内部本身就会扫一遍全部已装程序集、重建方案存储并加载草稿 —— 之后又调一次 `Scan` 把整表扫第二遍。检查用的是「已安装」状态，现在改用已应用视图（顺带跳过草稿与恢复文件的写入），直接复用重载已完成的那次扫描结果，不再重复扫描；被切出安全场景打断的重试也不再从头重做一次完整准备，而是复用上一次的准备结果，正常完成后才丢弃。

**更新助手的磁盘清理节流修好了。** 十分钟的节流原本记在进程内存里，而助手是按需启动的独立进程（有待办任务时游戏每十秒就会拉起一个），于是每个新进程都认为「该清理了」，每次启动都全量扫一遍存储。现在改用时间戳文件，跨进程生效。

**按分组整理预览不再做无用功，并且会报进度。** 预览原先对全部已装文件算 SHA256 —— 包括那些位置已符合、根本不会移动的包（实测 50 个包里 48 个属于这种）；现在预览只读取路径，哈希推迟到确认落地时再算，且只算本次真的要移动的文件。每个包目录原先要走两遍（文件清单与占位记录各一遍），现在只走一遍。另外预览分阶段写进度到任务文件，界面会实时显示「已读取 N 个模组目录」「正在计算目标位置」，不再是点下去就长时间只显示一句"正在准备目录预览"。

**日志窗口的定时重排不再制造大量垃圾。** 判定"日志内容是否变化"原先要为每条记录拼接一个字符串（外加一个 join 出来的分组键），在活跃日志上每次刷新都会造出成千上万个短命字符串；现在改为逐字段折叠计算，不建中间字符串。筛选重建时对全部分组的排序也从三次减为一次。

## 2.2.3 界面刷新摊帧，待应用状态改按需重算

概览界面的维护刷新以前每 0.2 秒把**全部**已注册控件标脏、并在同一帧内把这一整轮跑完，另每 2 秒对事件类控件做同样的事（条件里的计时本身构成定时器，与主题/语言是否真变化无关）。控件数量随已装模组数增长（整表常驻，50+ 行 × 每行约十来个绑定），实测单帧 120~210 ms，是界面持续卡顿、掉帧到个位数的主因。现在改为**固定游标 + 共享每帧约 2ms 预算**的一轮：一轮没跑完不开启下一轮，显式变更排在周期性巡检之前，任何控件都不会被跳过，只是不再挤在同一帧；无可见控件时依旧直接返回。

待应用状态的投影此前带 0.2 秒缓存，与巡检完全同周期，于是每一轮都正好撞上过期、重算一次整份投影（逐参数/逐模组新建对象、每个分组做一次 JSON 序列化）并触发 GC。现在改为**失效驱动**：草稿改动（启停、参数、清参数、批量、快捷键）、方案保存/应用、外部设置变化都会立即失效，缓存只保留 2 秒兜底窗口；介绍文档的改动照旧按原有键立即生效。列表侧的待应用标记缓存同样改为跟随失效版本，不再按 0.19 秒计时重算。刷新语义不变——所有控件最终仍会被刷新，只是不再同一帧付清。

## 2.2.2 重复副本互斥启停，目录整理识别现有分类目录

同一模组装了多份（GUID 相同、目录不同）时，启停改为按副本独立：勾选一份会把其它副本计划停用，取消当前选中的那份即停用该模组，两份都在用时取消其中一份会自动把另一份记为选中；未做选择前各副本保持盘上状态，打开概览不会凭空出现待应用改动。方案里只多记一个「选中哪份」的目录键，作者预设导出时剥离；重复副本的悬浮说明注明互斥。目录整理：`plugins/` 下以分组名命名、只装包不装插件的目录被识别为该分组的现有目录，里面的包视为已就位，散包搬进去而不是新建「分组名--哈希」目录；未分组的包原位不动；分组没有现成目录时新建目录直接用分组名，名称非法或已被占用才退回哈希形式。此前在双层分类目录布局上会把整个分类目录当成一个模组搬进新建的哈希目录。

## 2.2.1 更新换 GUID 时归档被取代的旧副本

更新把模组从一个分支换到另一个分支（例如 Foothold-Forked → 上游 Foothold）时两份 DLL 的 GUID 不同，旧版只按 GUID 归档，旧副本被当成"本地额外文件"留在原处，BepInEx 把两份都加载：AssetBundle 报"已被加载"、新版每帧抛异常。现在安装计划会把**插件类型全名与显示名都相同、但 GUID 不在新包里**的本地 DLL 一并归档（进备份、可回退；汉化伴随插件名称不同，不受影响），提示里写明"旧插件与新版是同一个插件，已归档避免重复加载"；被归档的 DLL 已被游戏加载时，这类更新等退出后再提交。概览的检查结果、"问题"与"重复"筛选新增一类提示：同一文件夹内名称相同但插件标识不同的多份插件，两份都会被加载，建议只保留一份。

## 2.2.0 更新核心重做与前台窗口

更新助手的安装核心整体重写。不再下载旧版本 zip 做对账，不再要求新旧插件 GUID 集合完全相同，不再用"官方原件逐字节一致"拦 Core 包更新；以本地现有文件为基线，要覆盖的先备份，同目录里不在新版包内的文件（汉化、补丁、运行时缓存）原样保留，插件集合变化只在队列里显示为提示。依赖按 manifest 的名称与版本识别（不看目录名），缺失或版本不够时自动加入队列先装依赖。插件元数据读取统一带程序集解析目录，声明了 DependencyFlags 枚举软依赖的模组不再报 "Failed to resolve assembly"。新模组安装不等游戏退出；更新只在要替换已加载的 DLL 时等退出并在退出后自动提交；启停仍等退出。写锁忙、准备中、整合包切换中收到的命令改为延后自动继续，不再拒绝。失败项"重试"丢弃旧事务从解析重新开始。队列文件不再携带整个游戏进程的程序集列表，进度写入每秒最多一次；完成超过 7 天的记录归档到 history.json，装完即删 stage，孤儿任务与备份两周后清理。前台"MCE 模组操作"窗口改为 WPF 深色界面：每个任务一张卡，显示译文状态、进度、真实失败原因（堆栈折叠）与提示，提供重试/移出/打开目录，保留 60 秒无进度横幅，语言跟随游戏。

窗口只在真有工作（下载、安装、等待退出）时才出现，心跳类启动静默完成后自退；只剩等待退出时停在任务栏、从不抢焦点。队列新增"清空"：隐藏所有已结束项、删除失败的商店操作、关闭残留旧批次；失败的商店操作可"移出队列"；同一模组的新任务自动让旧失败记录让位。助手写日志 `BepInEx/MCE/logs/worker-日期.log`（保留 14 天），记录每条命令、每次状态流转和完整异常。卸载、本地导入确认、商店窗口的安装/卸载确认全部走同一条安装管线：卸载把整个目录移入备份、被依赖只提示，可回退；本地导入预览只读分析，确认后作为本地源任务安装，"目标已存在"降级为提示并备份。不同包的文件复制并行，只有账本/文件表登记持共享锁。旧的保存批次执行器、旧版事务提交、账本门禁、本机部署策略等 ~700 行死代码删除，旧事务的回退统一由新执行器处理；`resource-batch` 旧命令兼容转为任务。游戏侧同步清理：删除保存批次评审窗、队列里的批次/待办行、管理页的旧批次按钮、`resource-*` 命令处理与相关状态文案，卸载对话框不再轮询旧操作目录；草稿是否被接受只看命令回执。目录整理改为队列任务：确认后按分组/译名把模组目录整体改名（同卷移动、逐文件校验、可回退），任何能被识别的插件文件夹都可整理，只有账本里已有的条目会被更新。整合包切换保持原流程。

实机反馈修正：`manifest.json` 直接放在 `plugins/` 根目录的包（以及 `core/`、`patchers/`）现在可正常更新；接受阶段的失败也会写入日志。独立进度窗口不再自动缩到后台、整体更紧凑。状态文案统一为带绿勾的形式（"下载成功 ✓ · 等待退出游戏后部署"、"下载成功 ✓ · 已部署 ✓ · 等待重启生效"、"已生效 ✓"、"已回退 ✓ · 原状已恢复"）。已部署待重启的项目留在"更新队列"，重启确认安装完成后才进入"更新记录"。游戏内队列面板加高加宽、行高压缩（一页约六行），右上角小窗改为显示统计"本次可更新 · 已下载 · 等待退出 · 错误"。日志分两层：普通层（更新提交与状态流转、模组启停、配置保存、整理/导入/卸载提交）始终开启，写入 BepInEx 日志并在游戏内"运行日志"新增置顶、独立配色的"模组管理器"来源；技术层在设置 `[9. Diagnostics] Debug logging` 打开后记录 worker 的解析、下载、解压、依赖判定、安装计划与写锁等待等技术细节。

## 2.1.24 独立文件操作与并发准备

安装/卸载/导入独立排队，不保存配置；卸载实际依赖警告在原窗口确认。每包独立任务，最多4项并行下载准备，一项失败不阻止其他包；单包文件复制并行、共享安装清单提交仍互斥。取消Core完整整合包迁移前提，保留原始包核验、备份及退出后替换。缓存更新走原缓存包路径，不自动装回plugins。

## 2.1.23 更新独立于配置保存

点击更新/更新选中/全部更新直接提交下载队列，不触发配置保存、作者预设选择或版本设置；基础组件同批更新只提醒一次风险。商店现装包更新同样独立提交。旧版待办更新可在队列“开始更新”单独启动，不夹带安装/卸载/导入草稿；配置保存及放弃配置不再消费更新待办。助手持久化更新请求并串行接续现有资源事务，保留备份、退出后替换和失败重试。

## 2.1.22 更新回执、批次控制与详情

统一草稿回执同步，已接收操作不重显/不阻塞新操作；提交确认中独立状态，不再显示待保存或提供无效移除。批次重试/取消/跳过/依赖确认绑定精确批次编号，旧窗口不会控制新批次。取消商店任务显示已停止；草稿与批次详情可查看完整操作清单。队列与管理页共用含单项卸载的依赖警告读取，复合错误按英文原文词条分段翻译。

## 2.1.21 更新队列可见性与组件筛选

更新窗口列出未保存操作、提供保存配置入口，保存后展示资源批次准备及失败重试/取消/依赖影响入口；批量结果明确暂存而非已下载，并显示完整清单。基础组件参与状态筛选和数量，更新目标与绿点共用版本判断。修正助手启动仍探测旧写锁、重复助手误报写锁繁忙、控制命令遇写锁被拒绝和旧错误不清理的问题。新增提示使用英文原文及现有中英繁翻译表。

## 2.1.20 行加载音效与统一更新入口

行首次完整加载独立播放原生设置音效，不恢复淡入，滚动/重进/重绑不重复。来源与更新明确提供“所有小角标标记为已读”，保留可更新和未保存状态。全部更新取消基础组件逐项商店前置选择，汇总风险提醒后组件一同加入资源草稿，复用既有组件事务/退出后替换与备份核验。

## 2.1.19 全量来源统计与批次快照

批量来源识别统计全部插件安装行：成功总数、本轮新增、需手动核对、个人本地分别显示，待定优先列出；全已确认仍可查看结果，不再只统计剩余目标。一次目录快照贯通识别与结果，阅读窗口不重新匹配，手动处理显式更新结果。弃用包保留来源身份、单列更新不可用；旧发布线索不压独立证据。回执查询无副作用，商店包本地按钮保护，导出独立投影及同GUID冲突保护，重置识别可随窗口取消。

## 2.1.18 来源识别刷新稳定性

修复近似匹配在下次Apply被精确名规则降回待定的问题；缓存结果用相同置信规则重核。个人手动商店选择按环境/包目录保留，原地DLL更新不失效；异步操作仍使用内容指纹核验防止迟到写入。移除批量结果页已无用途的全目录候选排序。

## 2.1.17 个人本地选择与识别原因

个人runtime/sources.json中的旧Local选择继续生效，发布包Local仍不是个人决定；本地策略按环境和包目录而非DLL内容指纹保留，原地更新不丢标。修正描述性英文目录被当成冲突包声明的问题；批量结果显示无同名包、多发布者、身份冲突或旧线索等未匹配原因，不以候选数代替结论。包含2.1.16导出前来源重核修复。

## 2.1.16 导出来源重新核验

导出sources.json前重新Apply每项来源，避免沿用旧UI的Local分类；输出不携带个人explicitLocal、note或安装指纹。随包附带使用distribution=embedded独立表示，不将附带文件等同本地来源；声明商店依赖仍写精确团队/包/版本。

## 2.1.15 本地标记证据与商店安装来源

修正2.1.12将旧发布资料kind=Local误当用户明确选择的问题：仅本机explicitLocal和安装指纹匹配才跳过识别，旧记录仅作线索，不改原件。内置商店安装成功清单的精确下载身份+实际payload路径直接确立Store，离线无目录也不降级，覆盖陈旧Local；外部复用/未验证/已删除文件/错误路径不冒认下载来源。

## 2.1.14 事件式机翻保护与高置信来源识别

核对XAuto反编译Initialize/Settings.Configure/ReloadTranslations后，移除两秒Update核验，保留机翻创建/队列/执行/回调强制拦截及初始化/重载事件保护。主页按钮待定与本地改暗灰且仍可点击。来源自动识别接受无冲突的唯一精确原始包名，或作者一致且90%以上相似、领先8个百分点的近似名，歧义交人工；不以译名匹配，不以本地版本高于商店拒绝。菜单“重置来源并立即自动识别”明确执行重置并立即查找，未命中打开手动候选。包含2.1.13三语说明校订。

## 2.1.13 内置介绍与说明校订

三语功能速览按上手、查找管理、配置保存、翻译工具、整合包制作和后台说明重新组织；采用标题层级、引用、列表、分隔线及章节锚点展示。同步校正分章的快捷键录制、分组单次保存、工作区资源归属、来源待定、未读标记和失效链接，补2.1更新摘要。生产Markdown解析器对30篇337项检查通过；不改业务逻辑，不覆盖个人说明。

## 2.1.12 来源呈现、功能介绍与后台休眠

检查结果优先译名，灰字原名/自身版本，路径仅重复项；兼容发布来源的旧格式明确本地记录，不再误列来源待定。主页悬停显示已确认商店链接及额外GitHub，菜单页面分类提供GitHub入口；GitHub本身不确认商店来源。三语内置功能速览按现有草稿/保存/搜索筛选/资源/来源和后台行为重写。隐藏列表Tick及无可见控件的维护提前休眠；日志事件记录与XUnity保护保留。后台审计详见验证记录。

## 2.1.11 来源警告与相关依赖跳转

来源未识别独立分类，说明仅影响自动更新；本地有实际payload且启用/版本满足时不列依赖故障，刻意标本地项不催来源。检查页加入一键重新识别/手动识别入口，复用安全来源匹配和批次结果，失败项可逐个手动核对，不隐式保存配置。查看相关模组包含实际依赖提供者，基础组件也按相关包展示，不再只筛出消费者。

## 2.1.10 概览即时搜索

概览搜索按钮改为设置页同款输入框，停输250ms后自动筛选，中文输入法组词期间等待；原名/译名/别名/插件GUID/包ID/作者/插件和包版本/原始及翻译描述均可检索，空格分词跨字段取交集，不区分英文大小写。标签/状态/来源条件保留；相关模组临时视图在搜索时解除。筛选不重建搜索框、不抢走输入焦点，清空恢复列表。基础组件保留包/作者/版本/描述/区域/依赖搜索。

## 2.1.9 完整包清单与依赖状态

修复检查页只查普通插件、遗漏patchers和非插件库的依赖误报。接入现有只读ShopInstalled清单，覆盖plugins/core/patchers与停用缓存，按环境/库存/来源代次复用；草稿启停仍优先。无作者manifest但有实际DLL时显示“已安装，来源待确认”，不混同“缺失”；未知版本、低版本、未启用/缓存区、读取不完整分别提示。分类改名“依赖检查”。不自动安装或修改依赖，不按同名猜作者。

## 2.1.8 检查页横向阅读与来源识别

检查页路径从BepInEx开始完整显示，取消截头和换行，按最长内容扩宽；顶部横向滚动条、内容拖拽、Shift+滚轮横移，普通滚轮继续纵向。修复无作者manifest的依赖已识别来源仍误报缺失：包检查与相关视图接受已确认的精确团队/包来源，保留版本检查，不混用不同作者同名包。发行依赖继续RTLC-XUnity_AutoTranslator-5.6.11，不自动修改现装。

## 2.1.7 重复插件识别与对比修正

修正将同包本体/汉化误判重复的问题：插件副本证据与整包启停归属分开，仅相同GUID且不同DLL路径的副本参与对比；各项显示自身插件名称、插件版本及单条路径。检查报告、相关模组和重复状态筛选共用规则，同包合并行仍保留真实副本。重复组序号独占一行，“与”恢复正常正文颜色，版本/路径保留灰色小字，组间留空行。

## 2.1.6 状态筛选与相关模组视图

检查结果“查看相关模组”直接回概览临时筛出相关项，不保存草稿。标签页优先可更新/重复模组/已启用/未启用/异常模组/有变更，官方标签默认收起、按需展开，保留包含/排除组合及数量；快捷键标签共用状态判断。更改标签或搜索可清除临时相关视图。

## 2.1.5 检查结果层级与去重

检查窗口扩大到1120×780上限；分类精简为重复模组、依赖缺失等，标题后的灰色括号集中给建议。相同文件组按集合去重，模组逐行用“与”连接，版本和路径为灰色小字，长路径保留尾部；依赖列明确缺失/未启用/版本过低。主操作改“忽略以上并保存”。富文本仅用于已转义的检查结果，其余错误保持纯文本。保留启动泛型挂钩去重。FrameDoctor原生分配崩溃由其1.1.5安全关闭入口处理，MCE不新增诊断。

## 2.1.4 问题清单与有效项保存

保存前按真实问题汇总兼容建议、多版本、依赖可用性、需要核对的文件操作，逐项列模组/原因/处理建议，一次确认；提供查看模组参数入口，不自动修复。重复GUID本身不挡保存，不同包明确路径可分别操作，同一路径候选及归属风险继续保护。整批启停预检失败时保留无法执行目标为草稿，保存其余有效目标和参数；结果单独列未保存项，仍有草稿不自动离开。保留2.1.3配置与资源修复。

## 2.1.3 保存一致性与资源事务修复

修复已加载配置的磁盘/运行双基线冲突、跳过项误入正式预设及新对局延迟通知。资源队列使用真实命令编号回执与精确操作关联；网络准备不持安装写锁，暂停/停止可处理，提交仍互斥重验。联合依赖警告按批次及环境确认，硬安全边界保留。增加资源能力门禁，须完整更新主插件及Worker。保留2.1.2保存窗口。

延迟项保存后ConfigEntry.Value会更新，事件与ModConfig回调在对应边界交付；直接轮询该值的第三方可能提前读到。实机交互待复核。完整21步骤回归及新增专项检查通过；旧跳过记录按独立证据保守恢复，较新预设不覆盖。结果与实际部署状态见 `验证/六项审查修复/` 下结果报告及 deployment 记录。

## 2.1.2 保存窗口统一重设计

按紧凑管理器风格重做整套保存交互：深色不透明表面、青蓝标题细线、标题/简述/详情分层、可滚动内容卡、固定底部右对齐操作。未保存确认明确三种选择的后果，主提交按钮居右，默认焦点继续编辑；作者预设首次保存共用同一窗口。依赖/外部冲突/失败详情与变更目录统一外壳，原有事务与草稿语义不变。长消息纯文本显示，窄窗口动作缩放不越界，正文与footer独立。隔离回归与布局契约通过；游戏视觉待下次启动。

## 2.1.1 实机保存与快捷键修复

快捷键当前列直接录制草稿，不再跳转并弹离开确认；底部状态改为加高换行与字号自适应。普通依赖缺口不再挡概览保存，MCE必需链和精确文件安全检查保留。助手单例锁与文件写入锁拆分，空闲助手不再阻止游戏内启停保存；写入阶段仍互斥。隔离全量21步骤通过，游戏内视觉待复测。

## 2.1.0 统一保存与资源草稿

参数、快捷键、启停和资源操作先进入草稿；概览保存统一提交，局部保存严格限定范围。引入按字段配置文档事务、外部编辑三方合并、恢复草稿与旧数据迁移；作者预设保留身份和原版基线，配置预设与整合包共用资料分离。商店/本地导入/更新/卸载改为资源待办，依赖警告绑定精确清单；本地批次按硬依赖排序并预检重复GUID及最低版本。

移除递归目录监控和全部MCE内置性能采样，诊断交给FrameDoctor。简中/繁中/英文配置帮助已同步。隔离回归、发布与部署记录见 `验证/统一保存重构/当前验收清单.md`。真实游戏布局、手柄与第三方回调由下一次启动验收。

## 2.0.13 依赖层级配色

名称悬浮框与依赖详情树共用逐层偏灰配色：一级浅象牙色、二级灰米色、更深逐级弱化且保留可读亮度；缩进/省略号/展开箭头同层一致，缺失等警告色保留。底部文案改为“双击名称查看依赖详情”。480总高、两级默认、固定加载进度和依赖方向不变。验证与部署见`验证/依赖层级/结果.md`。

## 2.0.12 固定加载进度与分支省略

滚动仅改变视口，整表加载顺序/已完成部分保持；取消手柄焦点目标插队，未完成目标等待固定队列。依赖/被依赖悬浮摘要最多两级（被依赖保留精简数量上限），每条截断分支下显示缩进省略号；移除多段灰色提示和共享引用附注，底部固定仅留“更新依赖双击模组查看详情”。总高480及可滚动、详情默认两级、方向修正保留。

验证与实际部署见`验证/固定进度与省略/结果.md`。

## 2.0.11 两级依赖与方向修正

悬浮依赖最多两级，第三级及更深省略；直接依赖仍不受8项限制，被依赖方向保留精简摘要。详情面板初始仅展开两级，深层可手动展开；切方向/新图重置为两级。悬浮卡高度上限改480。

修复多个包内置同名Harmony/Cecil等库时，把库引用显示为依赖所有携带该库模组的问题：多提供方程序集引用显示独立DLL节点，保留真实插件/包声明方向。显示投影不改后端安全检查。保留标题可读性、行居中、200ms悬停及原生动画。验证与部署见`验证/两级与方向/结果.md`。

## 2.0.10 依赖完整展示与行居中

依赖悬浮窗口默认展开完整依赖链，不再按8行截断；直接依赖优先于重复子项保留。被依赖项保留最多8个直接项摘要，省略有说明。最大高度由380提高到680，受屏幕可用范围限制；长卡在跟随和固定模式均可移入滚动，不再裁掉后半段文字。循环/共享分支仍停止递归，保留32层/2048关系安全上限。

紧凑小菜单标题改浅象牙色；普通模组和组件行的按钮、来源标签及复选框按实际背景高度居中。保留200ms提示、常驻分层加载及原生动画音效。验证/部署见`验证/依赖展示与居中/结果.md`。

## 2.0.9 应用问题列表、依赖对齐与导航

应用失败复用分组列表列原因和处理办法；可明确跳过本次全部启停，保留文件原状态，先应用参数与资料。跳过目标仍留在草稿待应用，不冒充已写入或待重启。安全事务与回退保留。已有且与本次无关的包/插件依赖缺口不阻止其他启停；新启用及移除被依赖项继续校验。

包检查使用已确认来源的实际目录映射；依赖展示合并实际manifest归属、包版本与插件GUID，修复XUnity包存在却误报缺失/未关联插件。管理页及详情选中态统一青蓝，普通操作保留语义色；下拉已选项同色并有勾号。普通模组双击名称/版本/简介，在鼠标处复用紧凑菜单，提供更改信息与查看依赖项。保留2.0.8加载收敛、200ms悬停及原生动效。

验证与部署记录：`验证/应用与导航/结果.md`；游戏内布局与交互待下次启动验收。

## 2.0.8 加载收敛与按钮即时可用

按用户要求删除新增按钮/工具栏淡入及透明期交互锁；含按钮的父行也不再用透明动画，原生悬停/点击动画保留。整表常驻、屏外继续建立；改为单一约2ms软预算（最多16小步），优先补齐当前行再建立下一行，取消双队列积压及每批全表颜色遍历。单个Unity步骤仍不可抢占。

修复自绘勾选图案的批量同步和失败恢复，切内部页签真正暂停建行，回来按游标续跑；纳入方案加号不重建整表。显示依赖环境改用目录通知驱动快照，监测失效时两秒兜底，显式打开强制核验；安装/卸载等安全路径继续实时读取。循环动画不强行冻结，只降低无效休眠检查频率。

专项验证见 `验证/加载收敛/结果.md`，部署实际状态以该目录 `deployment_*/result.json` 为准。下面旧版本关于按钮淡入、双队列的描述为历史记录，已由本节取代。



## 2.0.5 200ms、固定首屏与悬停定位（授权部署）

固定工具栏不再串行透明出现；搜索、列标题、分隔线与滚动区域进页即初始化，不等待组件扫描、不参与行动画。数据到达只建立模组行，列宽保持一致；只有行与行内控件逐步加载。表头缓存比较不在每帧拼接字符串，启用列宽按语言一次测量。

诊断候选新增 `[悬停计量]`：设置页/概览页分别按still/moving分组，记录raycast、enterExit、transition、uiRebuild、hoverSound的次数/总耗时/最大耗时/当前线程分配量，每5秒汇总。只计量不改输入、动画或声音；阶段包含子调用，不相加，uiRebuild不含原生Canvas.BuildBatch。相关测试和范围见 `验证/固定首屏与原生悬停/最终结果.md`，实际部署状态见同目录 `部署结果.md` 与deployment结果。

普通/关系悬停等待统一200ms；工具栏提示factory在停满后才执行，快速掠过不比较整份方案，手柄焦点仍即时。页面骨架按4ms/最多8步软预算推进，减少固定空等；组件元数据先提交列表，完整依赖图晚到只更新锁标与提示，不整表重建。未知图仍锁定。原行动画/音效/加载速度及固定中部加载顺序保留。

原生设置页仅SettingsTABSButton.Update内唯一SetActive调用改为状态变化才执行，文字Lerp和其他IL不变。自动队列observe文件准备/校验/发布移至后台单飞，安全校验保留；错误来源分离。DependencyContext加入聚合耗时，不缓存安全路径。FrameDoctor调查及实际验证：`验证/200ms与首屏热点/最终结果.md`。实际游戏帧时间待部署后对照。

## 2.0.4 单次刷新与渐进加载（用户授权部署）

本轮已授权完整备份后更新既有安装；实际部署状态、备份和发行包路径见 `验证/刷新与入口收敛/deployment_*/result.json` 与 `部署结果.md`。以下功能与验证记录不变。

刷新期间保留原列表，普通模组与基础组件扫描齐备后只提交一轮；尺寸/筛选/语言变化在扫描中合并，失败解除等待并显示错误。普通模组与基础组件行先创建背景、名称、版本等内容，原有逐行出现动画、音效及Appearance speed配置保持；复选框、齿轮、主页、目录等另走跨帧队列，补齐的子控件单独透明度淡入，不重播整行动画/音效。行骨架和控件队列各有每帧最多两步/约2ms软预算，单个Unity创建步骤不可中断。两队列独立推进且含屏外，完成条目常驻，滚动和完成后重入不重建。建行/补控件/行过渡期间暂停卡片预热；等待控件完成后恢复手柄候选焦点，用户已切焦点则不抢回。

列表每轮按首次可见行固定顺序：顶部向下，中部向上下两侧展开；滚轮不重排或打断。切页暂停保留行队列/透明度/声音进度，回来继续；已完成不重播。工具栏按钮及说明独立按从左上到右下顺序显示，工具栏步距受Appearance speed影响，不参与列表中部扩散；页面骨架创建也在按钮间让帧。

设置入口缓存原按钮，仅基础几何/字体配置/语言变化时写布局，悬停缩放不触发同步；新行导航仅绑定一次，不再每批重扫已建控件。原动画音效、50ms悬停及任意.dll.*规则保留。诊断候选新增listCommits/entryLayouts/rowBatchMax聚合计数。验证与实机步骤见 `验证/刷新与入口收敛/结果.md`。

## 2.0.3 resident list and arbitrary suffix correction (not deployed)

按用户要求撤销视口回收/滚动重建：整表按顺序继续建立，屏幕外也加载，建成后常驻，滚动不重绑、不回收、不重播；再次进入已完成列表不重播加载。方案/过滤/库存真实变化仍按需要刷新。50ms悬停、冲突完整路径、方案状态与刷新中心修复保留。

任意非空.dll.*都为停用候选；同目录同名.dll存在时，全部后缀副本视为备份而非仅.old/.bak/.backup。无同名.dll且多个后缀候选时明确报冲突，不擅自选取/删除。MCE staging候选仍保留更新事务专用排除。记录 `验证/常驻列表与任意备份/结果.md`。

## 2.0.2 first-open regression, file conflicts and UI follow-up (not deployed)

Mouse dwell is now 50ms. Opening no longer creates a font diagnostic mesh or forces global Canvas updates twice; visible control trees are bound in small batches, card prewarm advances one measurement step at a time after settling, and bulk glyph prefill is no longer scheduled. Existing button animation/sound stays intact. Refresh feedback rotates/scales around its centered pivot while preserving icon offsets and the hit target.

修复.dll旁.old/.bak/.backup备份误阻断启停：明确备份保留不动，新停用写.disabled-by-mce；真正多候选仍拒绝并列模组和完整路径。单独.old仍兼容历史停用。方案界面明确“正在编辑/游戏当前使用”，保存不切应用，失败说明保留。日志确认旧版首开1168ms及随后1717ms长帧；本轮针对同步预热/建行/字体诊断和全局Canvas刷新修正，实机效果待验。详见 `验证/首开回归与50ms/结果.md`。

## 2.0.1 performance continuation (75ms dwell; not deployed)

用户最新要求：普通/关系鼠标提示统一75ms等待，保留前轮缓存/画布优化。续修新增隐藏纯UI骨架分帧预建、点击后才绑定仓储事务、不可变物理扫描证据复用、冷卡片分帧排版及旧任务取消、关系别名索引、按钮原生状态过渡前唤醒与保守Normal状态休眠。按钮模板/动画/音效保持；诊断候选仍开启计量。详细实施与验收见 `验证/悬停与首开性能续修/结果.md`。

## 历史：2.0.1 first hover-performance source candidate (2026-10-30; not deployed)

No artificial hover dwell; isolated inherited-sort tooltip canvases; bounded generation-aware content/metric caches, logarithmic exact TMP prefix measurements, allocation-free mask-list reuse and constant-time display numbering. Existing button animation/appearance/sound is unchanged. Idle visible-row glyph/content preparation, read-only background metadata prefetch and lazy introduction-editor creation reduce cold work. Font/layout maintenance uses events plus a two-second fallback; pending-state/button-permission checks retain their existing cadence.

本轮候选未部署。诊断配置 `9. Diagnostics / UI performance probe` 默认 false；本次按用户要求以 `-p:UiPerfSession=true` 编译，候选进程内开启计量，不修改真实 cfg。每五秒输出 `[UI计量]`；Canvas 事件区间仅作诊断参考，不是 GPU 或完整 Canvas.BuildBatch 时间。已通过离线算法/策略及构建；Unity 中快速划过、固定卡片滚动、语言/缩放/焦点和三轮帧时间待实测。实施状态和分阶段未完成项见 `验证/悬停与首开性能20261030/实施结果.md`。

## 2.0.7 checkbox state-only updates

勾选启用/禁用只修改方案目标和现有行状态，不再Render/Reload整表；行对象、滚动位置和分层加载进度保留。多选、边框、颜色、待保存标记、动态悬停与冲突提示原位同步。应用/切方案/过滤等真正结构变化仍正常刷新。验证和部署见 `验证/勾选局部更新/结果.md`。

## 2.0.6 XUnity machine-translation guard

“Disable XUnity machine translation” remains enabled by default. It now blocks job creation, queue dispatch, endpoint execution and late machine results, including explicit endpoint selection/API calls. File dictionaries for mods remain available; game-native LocalizedText is kept at its original value. Initialization/reload checks clear both endpoints, sweep generated caches with one-time backups, and refresh in-memory dictionaries. Failures are reported instead of claiming success. Turning the option off allows native XUnity behavior again; previously cleared endpoint preferences must be selected again if needed.

默认启用时彻底拦截XUnity机翻路径，保留模组词典并保护游戏原生中文；旧自动缓存备份后清空，重读内存，重载/切语言仍生效。真实原生游戏UI/翻译服务的联动验收见 `验证/XUnity完全禁用机翻/结果.md`。今后本项目验证通过且游戏/助手均退出时，按用户长期授权自动备份部署。

**English** · [中文说明](#中文说明)

An in-game mod and modpack management center for **PEAK**. Find settings, organize mods, switch configuration profiles, manage updates, and build a modpack presentation without leaving the game.

**2.0.0 is our largest update since the public 1.0.2 release.** The original settings-page improvements now sit alongside a complete overview, profile workflow, introduction editor, and publishing workspace.

## Update feedback follow-up (2026-10-30, 2.0.0; authorized delivery)

Checks keep the existing group counts in parentheses after the overall n/N. The bottom status stays on one line with ellipsis and a full hover/focus hint. The refresh glyph has its own short rotation/scale/highlight response, then stops animating when stable.

“全部更新”使用已查到的最新版，与“恢复整合包指定版本”分开；基础组件单列数量和逐项商店预览入口，安全门控保持。旧助手错误保留为历史，不再仅凭文件时间冒充当前红字。工具校验允许所选游戏边界上方Steam路径别名，边界自身及内部链接仍拒绝。MonoDetour的明确清单身份可区分同仓库多组件，0.7.16→0.7.1701两项已用真实清单/目录缓存沙盒验证。

完整20步骤重新publish/build及回归通过：`验证/主菜单与概览优化/integration_20261030_203918/summary.json`；最新专项UI29/队列27（真实junction）/MonoDetour25/集成20及接线24+12通过：`验证/UpdateFeedback20261030/final_20261030_205551/`。真实游戏点击、动效与联网安装待下一次启动。实际部署/备份/ZIP以 `验证/UpdateFeedback20261030/deployment_*/result.json` 为准，本段打包前准备；未执行真实模组更新或修改库存/配置/队列。

## Menu/overview and source-check optimization (2026-10-30, 2.0.0; authorized delivery)

The current work follows `验证/主菜单与概览优化/最终优化方案.md`: menu-idle lifecycle fixes for the three maintained gameplay overlays, overview hover/layout/queue work, and a unified discover → verify Thunderstore identity → compare versions pipeline. Approximate-name scores rank candidates only; local evidence and official identity verification decide confirmation. Embedded distribution must preserve source identity. Player progress counts mods, not deduplicated HTTP requests.

本轮按最终方案实施主菜单空转、概览性能及来源/更新链路修复。近似名称仅推荐候选，不凭高分认领；GitHub是项目线索，确认对象为雷霆商店身份。本地内置导出保留来源。结果统一按模组条目显示检查进度及可更新数量，具体对象仍看绿点。

已完成提示150ms防抖及测量缓存、关系代次索引、静态维护合并、队列后台变化快照；模组/组件/分组标题按视口租用，完整控件树采用稳定身份LRU（闲置上限24/12/12），同身份缓存命中不重绑监听，结构代次变化安全重建。助手重复状态不再发布完整快照。更新检查先自动查源、核验商店，再按完整清单比较版本；保留显式本地，候选展示理由，导出内置保留来源，分组进度及总结按模组计数。

独立审查后追加修复了滚动签名的逐帧分配与组件焦点刷新饥饿；生产签名读取9600次0字节分配，16项窄回归和2953+2056策略检查通过。来源最终补审问题已修复：检查轮次固定分组快照、中断不残留旧提示、基础组件遵守全局许可、个人自动记录不遮蔽作者显式本地策略。最新串行离线回归已通过，20个执行步骤及分项日志见 `验证/主菜单与概览优化/integration_20261030_201102/summary.json`；包括来源77、离线往返6、性能2953、分组12及现有安装/商店/库存回归。实际部署成功/阻断/回滚、备份和发行ZIP以同目录 `deployment_*/result.json` 为准；本段打包前准备。部署仅在游戏和助手退出后，完整备份并按清单交付，不更改真实配置、库存或队列。真实游戏的多语言、手柄/拖动、机场/关卡功能和三轮性能对照待下次启动验收。当前现装基线为 `DropdownSuffixChecks/deployment_20261030_170457/result.json` 记录的37文件，已含Framework助手与WPF窗口；后文“23文件／助手未部署”为各历史检查点状态。

## Disabled filenames and compact menus (2026-10-30, authorized delivery)

用户已明确要求部署本检查点。完整37文件更新、原安装备份及新发行ZIP由 `验证/DropdownSuffixChecks/Deploy.ps1` 执行；实际完成状态、目标、备份与包路径以该目录 `deployment_*/result.json` 为准。本段在打包前写入，不预写部署成功。只更新MCE既有目录，不执行真实安装／整理／切包，不改core或用户配置。此前“source only / 未部署”为实施时历史状态。

Recognizes arbitrary non-empty `*.dll.*` disabled filenames through static metadata, preserving updater-artifact and duplicate-copy safeguards. Small selectors/actions share the Sources and updates compact grouped menu, including eligible text-only ModConfig settings. The refresh glyph now has its own 32×32 click target, with tighter caption spacing. Complex search/multiselect/editing panels keep their structure. Not deployed; implementation and actual offline checks: [results](验证/DropdownSuffixChecks/结果.md).

本轮源码支持任意非空 `.dll.*` 停用后缀；简单下拉统一来源与更新风格，含符合条件的模组设置下拉。刷新图标独立32×32热区、文字间距收紧。未部署；实机点击、焦点、DPI与视觉待核对。

## Import / folder-layout source checkpoint (2026-10-30, 2.0.0; not deployed)

The source now includes a grouped import toolbar entry, metadata-only local DLL/ZIP/folder preparation and exit-time transactional installation, explicit one-time group/translated-folder organization for verified package directories, and a lightweight .NET Framework 4.8 worker plus WPF progress viewer. Local imports keep local provenance and never claim verified store identity. Existing folders/configuration are preserved; unknown/shared/mixed startup packages are retained. Saved revisions are unchanged.

This is a partial implementation checkpoint. Continuous automatic organization, immediate unloaded-plugin operations, store-extracted DLL matching/completion and modpack import/export layout projection remain to be implemented. No game files were deployed or organized. Exact implementations, test results and remaining work: [implementation status](验证/InstallationFlowChecks/实施状态.md).

本轮源码已接入分组导入图标、本地 DLL/ZIP/目录静态分析及退出后事务安装、归属明确模组的一次性分组／译名整理，以及 Framework 4.8 轻量助手与 WPF 进度窗。当前属于分阶段源码检查点，持续自动整理、未加载即时处理、商店拆分 DLL 完整来源流程和整合包布局联动仍需继续实施。没有部署或整理真实游戏目录。

## Compact update indicators and source dropdown (2026-10-30, 2.0.0)

Available updates use matching small green dots beside mod names and current versions, including Core/Patcher components. Hover or focus a version/dot to see the upgrade target; prepared versions remain in the hint too. Blue unread-change dots, yellow pending-state dots, compatibility advice and protection markers keep their separate meanings.

Sources and updates opens a compact, anchored dropdown with colored sections for source tools, update checks and change records. Manual checking and its automatic-check switch now live in that dropdown, alongside the independent global update permission. A circular refresh icon sits inside the toolbar button with reserved label space. The profile button is 20% shorter; only its profile-name text is 20% smaller. The local deployment target follows the user's current installed folder, `0核心&基础模组/ModManager_InGame_2.0.0_20261029_215224/`.

可升级模组在名称和当前版本旁显示同款小绿点，普通模组及基础组件共用；悬停或聚焦显示升级目标，已准备版本也收进提示。蓝色未读变动、黄色待处理、兼容建议及保护标记保留各自含义。

“来源与更新”改为靠按钮展开的紧凑下拉，按来源、更新、变更记录分组配色；手动检查及自动检查勾选框移入，独立列明全局更新许可。按钮内部右端增加循环刷新图标并给文字留足空间。方案按钮缩短20%，其中方案名字体单独缩小20%。本地部署目标跟随用户目前实装目录。

Delivery: Release built with zero warnings/errors; all 2,548 translation entries passed validation. All 23 release files were backed up and deployed to the user's current installation on 2026-10-30. Restart PEAK to use the new interface. 本轮0警告0错误，2548条翻译词条检查通过，完整23文件已备份并部署；重启游戏生效。

## Unified management interface (2026-10-29, 2.0.0)

The visible name is now **Mod Manager (In-Game)** / **模组管理器（游戏内）**. The existing plugin identity, DLL, package source and configuration paths remain compatible. Overview and settings use the same manager highlight; ordinary mods and Core/Patcher components share their row buttons, disabled colors and three-card source menu. Folder choices open beside the clicked icon and include the manager's existing configuration, translation, workspace, resource and maintenance folders.

Name hovers and full dependency pages use one relationship snapshot. Hovers keep two levels; the full tree opens expanded with compact name/version/state rows. BepInEx is treated as an installed runtime prerequisite. Update checks share the store/source catalogue, report package and mod counts separately, and refresh available-update markers after manual or automatic checks. Automatic checks run every 30 minutes in the main menu or Airport; installing an update still uses the update queue. Unrelated dependency metadata gaps no longer disable every uninstall preview; manager dependencies, file ownership and reverse-dependency checks remain enforced.

界面名称改为**模组管理器（游戏内）**，原插件身份、DLL、商店包来源和配置路径保持兼容。概览与设置页复用醒目色；普通模组和基础组件复用齿轮、主页、目录按钮及三分区来源卡片。多目录菜单靠近所点图标显示，管理器另提供已有的配置、翻译、工作区、方案资源及维护目录快捷入口。

悬浮摘要与完整依赖页使用同一份关系数据；摘要最多两层，完整树默认展开并紧凑显示。BepInEx作为运行环境处理。商店、来源查找和更新检查共用目录缓存；检查结果区分模组数与去重后的资源包数，手动或自动检查后同步绿色更新标记。自动检查每30分钟在主菜单或机场候机厅运行，安装仍通过更新队列操作。无关的依赖资料缺口不再阻止所有模组进入卸载预览；管理器必需依赖、文件归属及反向依赖仍受检查。

Build status: Release and the refreshed update helper passed; 99 targeted checks and 2,546 catalogue entries passed validation. All 23 release files were backed up and deployed to the existing installation on 2026-10-29. The user explicitly requested this deployment while PEAK was running; the current session retains its loaded version and the new version takes effect after restart. 本轮完整23文件已备份并部署；用户明确要求在游戏运行时直接替换，重启游戏后生效。

## Current source checkpoint: independent environments (2026-10-28, 2.0.0)

Complete environments now store their own core/plugins/patchers, BepInEx-side host/tools and runtime configuration per revision (schema2/protocol2), with immutable payload/ownership-state pairs. Implemented and sandbox-tested, **not deployed or migrated on this machine**: installed remains `installed-status`. Game-root Doorstop and `PEAK_Data/` remain platform prerequisites, not replaced pack content; MCE management data/local preferences are excluded.

See the [delivery checklist](验证/IndependentEnvironment20261028/交付检查清单.md) for implementation, exact tests and remaining real-game checks. The real legacy lightweight pack is blocked by missing `Murka124.PlantholderOverhaul`; synthetic complete fixtures do not make that pack pass. Defaults/paired complete-fixture audits passed 33/37 in ReleaseFinal; these are not successful real legacy pack round-trips. Earlier fixed-host/delivery records below are historical, not the complete-environment contract.

## Component overview and relationships (2026-10-29, 2.0.0; deployed)

Core/Patcher lists each component package once and counts its installation locations separately. A missing manifest no longer silently hides a component: identical runtime files may associate a location with one verified package; unresolved folders remain visible with an unconfirmed identity. The current local installation contains five packages in seven locations.

Name hovers show at most two relationship levels and eight entries per direction. Click a component name for a bounded details page with centered action buttons, all installation locations, and compact single-line Dependencies / Installed dependants trees, fully expanded by default. Repeated relationship evidence and package-internal references are combined for display. Tree rows combine the shortened display name, gray original/version and load state on one line. Width fitting reserves space for the state; measured font heights keep fallback text visible. Ordinary-mod and component hints share the same numbered row format with an arrow at every level, including direct children. Combined component types retain the (Core/Patcher) suffix. Overview rows, the hotkey page and both sides of the settings page use smaller spacing and text. Mod gears are enabled only for loaded plugins with currently displayable ModConfig settings. Compact dropdowns retain a visible selected-value caption without opening the list. The compact layout was deployed on 2026-10-29 17:38 (23 files); repeated settings refreshes do not shrink the controls again. Uninstall continues through the existing live safety checks.

English is the source language. New labels and previously uncatalogued messages are registered in the existing English / Simplified Chinese / Traditional Chinese catalogue. Configuration identities remain compatible with existing files.

Deployed to the existing MCE plugin directory on 2026-10-29 at 16:33 (23 files). The previous installation is backed up at 备份/部署前_组件界面与翻译修复_20261029_163300/installed/. In-game layout and interaction await the next game session.

## For players

### Start here

1. Install with Thunderstore Mod Manager/r2modman, or follow the manual instructions below.
2. Open **Mod overview** from the main-menu settings page, or press **ESC** and use its overview entry.
3. Choose a mod, use its gear to open settings, and hover over an option to see its description and default value.
4. Open the in-game **User guide** for the full controls and explanations.

### Your setup in one place

| Feature | What you can do |
|---|---|
| Mod overview | Browse colored, collapsible groups; see names, versions, authors, sources and dependencies; jump to settings, homepages and folders. |
| Settings sidebar | Search mods and settings, expand sections, pin entries and filter by setting type. The description panel explains the selected option. |
| Profiles | Keep different settings, hotkeys, enabled-mod targets, groups and introductions for the same installed setup. Create, copy and rename personal profiles. |
| Compatibility advice | See pack-wide advice even in personal profiles; inspect pale-yellow badges and choose whether to apply or ignore reminders locally. |
| Hotkeys | Compare current bindings, mod defaults and profile presets. Search actions and review duplicate or unbound keys. |
| Updates | Check confirmed Thunderstore sources, choose an update or rollback, and follow progress and results in an independent queue window. Downloads come directly from Thunderstore. |
| Languages | Switch between English, Simplified Chinese and Traditional Chinese, or add language files. Reload translations in the game. |
| Input and appearance | Use mouse/keyboard or gamepad navigation, the game's paper-style interface and optional TimeTheme day/night colors. |

### Keep several modpacks

The Modpacks page uses a library on the left and a selected-pack detail pane on the right. Each library record keeps its name, active badge, type/version and file count together in a plain rectangular row; selected and active are separate states. Opening the page draws basic information first; assembly details load off the game thread, with a per-revision memory cache and persistent summary cache. Unchanged library/task files are not repeatedly deserialized. Select any stored pack to browse its introduction, mods, dependencies and file tree directly from the stored revision, without the original ZIP or downloads. Import runs in its own review pane; **Preview switch** is a separate operation that reviews replacement of the active environment.

Add a modpack and MCE **checks it first**. The banner gives the verdict; the tabs **Must fix**, **What it needs**, **Mods** and **All files** (a folder tree) explain the rest in plain words. Saving only stores the pack — your game is not changed until you switch.

Open **Management center → Modpacks** to import a ZIP or extracted directory. MCE detects local mods, fixed-version store dependencies, author material and configuration automatically. Publications with an identity create or update that library entry; a standalone profile without a pack identity is appended to the current pack as a personal profile. Missing files, incompatible host versions and conflicting dependencies remain visible and prevent activation.

Before the first switch, choose **Save current setup as a temporary modpack**, name it and review the complete environment. Confirmation waits for PEAK to exit before capturing core, plugins, patchers, runtime settings and the component ledger into a new paired revision, without replacing live files. The library remains under `BepInEx/MCE/packs/`; one pack occupies the active BepInEx locations at a time. Switching waits for exit, removes old-only managed files and applies the reviewed target; failures retain backups and restore the previous environment.

Complete packs include their own core and MCE host/dependencies/tools. Targets validate without borrowing live libraries. Upgrading the current legacy pack requires explicit preview/confirmation and exit-time capture into a new revision, preserving the old one. Non-current legacy packs need verifiable original content or an explicit source, not a copy of today's live core. Reading never migrates; a schema2 index can still contain legacy records. Incomplete or cross-mode switches are blocked.

**Save current setup and switch** creates a new saved pair; **Switch directly** retains a separate unsaved pair. Returning restores unsaved core/files/ledger and keeps its marker until saved. Runtime updates do not overwrite saved pairs. Multiple mod packages inside one modpack may own identical path/hash content; different modpacks keep independent revisions. Game-root Doorstop and `PEAK_Data/` stay external. `BepInEx/MCE/`, `config/ModConfigEnhance/`, `config/MCE/` and `config/com.aiae.modconfigenhance.cfg` are excluded management/local-preference data; language choice remains local. Not every option in that global cfg is claimed to be pack-isolated. Wait for the helper before restarting.

Publication includes the union of all selected profiles' mod targets, including disabled targets, their hard dependencies and explicitly selected attachments. Referenced cached mods and package-owned resource/patcher directories retain their complete package structure. The preview and ZIP use the same selection. Author root dependencies pin exact versions; third-party requirements reuse the selected same-package version when it satisfies the stated minimum. Missing disabled targets remain visible, while missing enabled targets or required dependencies block activation.

Stored pack file names open the shared content viewer. Markdown is rendered by default, with source and formatting edit modes; images/GIF/video and supported text/config files can be inspected. File edits are staged, then saved as a separate personal copy without replacing the signed original or current game files. Stored details include a language selector and Apply action.

Pack detail previews resolve translated mod names from the selected pack language group only, independently of the running game dictionary. Missing entries retain the original text. A language file is not automatic machine translation: an introduction without a translated entry remains the author original, and filenames/media paths are not translated.

Storage management opens a background-loaded folder tree for downloads, detail caches, completed switch backups and trashed packs. Safe cleanup units have individual deletion buttons, with exact item scope revalidated before execution; children remain browseable without arbitrary deletion. Pack details and file previews provide folder/open/reveal shortcuts. Only allowlisted text documents open externally, using the fixed Windows Notepad executable rather than file associations; all other file types are revealed. Images/video remain available in the internal viewer. Rejected maintenance requests preserve an active switch/recovery task, and storage deletion controls are disabled while the task is busy. Personal copies retain profiles, dictionaries and applied-profile records with rebased paths, without pending operations. Referenced media/fonts in a document’s media/fonts folders can be saved with it; modified author material is verified as a personal unsigned copy while the original remains intact. Closing the file window through its button or unified return route asks whether to save a personal copy, discard, or continue editing, including staged drafts.

### Removal, storage and confirmations

Stored non-active packs can be reviewed before removal to a recoverable trash area. Removing mod files creates a new stored revision and checks hard dependencies; switch away from the active pack first. Storage management separates downloaded ZIPs, regenerated detail caches, completed switch backups and permanent trash removal. Every operation previews its file/space scope and revalidates it at confirmation; active packs, pending operations and referenced content remain protected. Project-source backups are never included. Restoring a trashed pack currently requires manual recovery; no automatic backup retention policy is enabled.

Import previews resolve store metadata but do not download ZIPs until **Approve download and complete** is confirmed for that exact preview. Temporary snapshots retain language dictionaries in their own workspace. Text editing supports Ctrl+Z, Ctrl+Shift+Y, Ctrl+Y and Ctrl+Shift+Z. Switching opens an independent Windows progress window which survives game exit.

### Save, apply, restart

| Action or status | Meaning |
|---|---|
| **Save only** | Save the profile targets and collect current settings that have not yet been recorded. Existing edited targets are retained. |
| **Apply configuration** | Write the chosen targets to the game settings and plugin files. Ordinary settings and hotkeys can be applied during a game. |
| **Enable** | The checkbox records whether the profile wants a mod enabled. |
| **Loaded / Not loaded** | Whether the mod is loaded in this game session. |
| **Pending apply** | Use Apply configuration to write the target. |
| **Pending restart** | The files are prepared; restart PEAK to use the new plugin state. |

Save results and apply confirmation group changes by category, group and mod, showing previous values, targets and effect notices.

Selecting a profile previews it. Author presets stay read-only; editing one creates a personal copy. Language selection is global and stays separate from profiles.

Plugin enablement and settings marked as requiring a restart take effect on the next launch. Disabling a plugin renames its file to `.dll.old`; MCE can still read its name, version and dependencies, and restore `.dll` when you enable it again. Enablement checks protect required foundations and report dependency or file problems.

### Downloads and updates

Choose **system proxy**, **direct connection**, or a **custom HTTP(S) proxy** in Management center → Network. Test the store connection and download connection separately. Custom proxy changes have their own save action.

Updates are installed after you choose them. The queue distinguishes download progress, waiting for exit, waiting for restart, success and failure, and keeps rollback history. Updates to MCE, shared foundations and startup patchers wait for the game to exit. The source-candidate download/update helper and WPF viewer use Windows **.NET Framework 4.8**, without a bundled runtime. Existing deployed releases described below predate this source-only migration.

For manager-controlled installations, configure the actual manager profile folder in MCE settings if you want enablement changes synchronized with that manager. Close the manager before applying those changes.

## For modpack authors

### Build the experience in the game

The management tabs are **Information · Modpacks · Groups · Profiles · Updates · Network · Tools · Publish · Preview**. Publish and Preview stay at the right end; Updates opens the independent queue window.

- **Information:** edit the pack title, author, version and introduction, and manage each mod's displayed name, description, version label and source. The real plugin/package version remains available.
- **Groups:** assign names, colors, descriptions and order; search and select mods in a grouped list. Grouping organizes the interface without moving installed folders. Shop-install grouping is persisted to the pack/profile bound at confirmation, not whichever profile is later selected; failed saves remain pending and do not save unrelated drafts.
- **Profiles:** prepare recommended settings and hotkeys, select enabled-mod targets, and provide several presets with a chosen package default. Players keep their own personal copies.
- **Introduction:** compose a page with headings, lists, links, colors, images, GIFs, video and imported fonts. Resize or arrange media in the page, read it in the same layout, and use Markdown import/export when needed.
- **Translations:** export source text or missing entries, maintain translations of mod names, descriptions and settings, and package selected languages. Personal overrides remain separate from author material.
- **Conflict presets:** in author mode, open Publish → Modpack conflict presets. Describe combinations of two or more mods, using any-pair or all-enabled triggers. The advice applies to every player profile and always allows continuing.
- **Update policy:** choose global and per-mod update permissions and confirm store identities. Display labels and translated names are independent of the identities used for updates.

### Choose what to export

| Format | Intended use |
|---|---|
| **Author data only** | Share profiles, groups, introductions, translations and referenced media/fonts with players who already have the mods. |
| **Complete local modpack** | Embed the complete BepInEx environment (core, host/tools) and exact ownership state in one ZIP (`embedded`). |
| **Thunderstore package** | Complete mode supports embedded components or fixed store dependencies (`resolved`); legacy dependency-only mode remains non-complete. Review platform policy and redistribution rights. |

Complete previews/ZIPs share the MCEEnvironment.json allocation: embedded carries final bytes; resolved pins Key/Version, previews metadata without ZIP downloads, downloads only after approval and seals final files/ledger before confirmation. No live borrowing; complete stored revisions switch offline. Publication captures live payload/ledger together, not saved content mixed with newer workspace state; inconsistent ownership blocks publication.

Both local and Thunderstore installation ZIPs include the selected default configuration under `BepInEx/config/`, ready for plugins to read on first startup; the main-menu stage continues profile registration and supplementary application. Back up personal config before upgrading a pack. The profile selected as the **package default** supplies the exported configuration and initial enablement targets. **Default configuration** is the separate personal mode. Store the intended parameters first; extra config files and attachments are explicit selections. Referenced introduction resources travel with their profiles.

**Preview** groups the contents by type and shows a collapsible directory tree. Select a file to inspect its full relative path, text, Markdown or media. Supported text and config files can be edited for the export; JSON/config edits are checked before storage.

**Store publication changes** saves changes for that publication session. Generate the ZIP before closing the management center to retain them; reopening does not restore the draft. Editing the current profile's introduction uses the introduction editor and saves back to that profile.

Conflict presets are saved independently of profiles and exported as `MCEPack/conflicts.json` in all three formats. They are covered by author-data signing. Player ignores stay on that PC; a changed rule prompts again, while the pale-yellow badge retains ignored details.

Author profiles are distributed as read-only material. Optional signing verifies the integrity of author data and resources. Back up the publication signing identity separately if you use signing. Generated packages are saved locally for you to distribute or upload.

### Suggested workflow

1. Organize mod information, sources and groups.
2. Record the intended settings/hotkeys, edit enablement targets and prepare the introduction.
3. Apply the profile and restart when plugin enablement changes.
4. Open Publish, choose the format, profiles, package default, languages and extra files.
5. Review Preview, generate the ZIP, and use it for your distribution channel.

## Install or upgrade

**Required:** BepInEx 5 for PEAK, PEAKLib.Core, PEAKLib.UI, and PEAKLib.ModConfig **1.8.2**. The package manager installs the declared dependencies. TimeTheme is optional. XUnity AutoTranslator is not needed; if it is installed with online machine translation on, the manager reminds you and can turn that off.

**Manual install:** install the dependencies, then copy this release's `BepInEx` folder into the PEAK game folder. Keep `Tools`, `Demo` and the bundled libraries beside `ModConfigEnhance.dll`. Use one installed copy of MCE; when upgrading an existing custom folder, replace its contents with `BepInEx/plugins/ModConfigEnhance/` from the ZIP.

**Upgrade from 1.0.2:** back up your configuration, exit PEAK, then update MCE. Existing translations and overview data are migrated to the profile layout. Keep `BepInEx/config/ModConfigEnhance/` to retain personal profiles, languages and resources. Review Viewing, Currently applied and pending-action notices before applying a profile.

### XUnity AutoTranslator

Translation is done by the manager's own language files. It no longer writes XUnity dictionaries, switches its language or blocks its machine translation. If XUnity runs online machine translation (Endpoint in `AutoTranslatorConfig.ini` is not empty), the manager reminds you once per launch and offers to clear the endpoint (the ini is backed up first).

### Your files

| Location under `BepInEx/` | Contents |
|---|---|
| `config/com.aiae.modconfigenhance.cfg` | MCE's global options. |
| `config/ModConfigEnhance/language/` | Personal language files and overrides. |
| `config/ModConfigEnhance/profiles/` | Existing personal profiles before adopting the installation into the pack library. |
| `config/ModConfigEnhance/output/` | What you export: `translation/` texts to translate, `packs/` published packages (an export can use a different folder), `logs/` log exports. |
| `config/ModConfigEnhance/data/` | The manager's own data: `runtime/` state and caches, `backups/` configuration and migration backups, `help/` guide edits, `legacy/` pre-October-2026 data. |
| `MCE/` | Download/update queue, working data and update backups. |
| `MCE/packs/<id>/` | Adopted/imported modpacks, recoverable content revisions and each pack's personal profiles and application state. |

## Maintenance and delivery authorization / 本项目维护与交付授权

用户于 2026-10-28 明确给予 **ModConfigEnhance 本项目**长期部署授权：以后修复验证通过后，除非游戏在运行，否则直接部署，不再另问。此授权不扩大到其他项目或其他模组。执行时仍必须确认 **PEAK 与 MCE.UpdateWorker 均已退出**；任一运行即停止部署，报告具体进程及构建／发行包／备份／现装状态，不杀进程，不删除或修改任务来强行部署。

保持版本及既定实际安装位置；核对 plugins 启用目录与明确临时卸载缓存候选无冲突，不擅自换路径。部署前完整备份现装，复制前再次确认两进程为零，按完整 release-files 清单复制并逐项检查目标存在及大小，复制失败恢复完整旧安装。每轮保留独立脚本、日志与 JSON；交付说明打包前更新，保证源码、包内及现装文档一致。不改用户配置、库存、队列，不触 core、PEAK_Data 或其他模组，不执行商店操作，不启动游戏。

**当前交付边界（2026-10-30）：**紧凑更新角标及来源下拉的完整 **23文件**已更新到用户自行安装后的 `BepInEx/plugins/0核心&基础模组/ModManager_InGame_2.0.0_20261029_215224/`，构建部署目标同步跟随此位置。部署前备份位于 `备份/部署前_更新圆点与来源下拉_20261030_105847/installed/`。沿用本轮对话中直接替换安装文件的指示，保留当前游戏会话加载内容，重启后生效。本轮不含实际core写入、真实库存迁移或切包；未改用户配置、库存、队列及其他模组。用户未来在游戏中显式预览、确认并应用计划，是另一次产品操作确认。

## Historical local acceptance and release files (legacy contract)

The fixed-host/core-preinstallation statements below record the old lightweight format only. The latest real legacy publication round-trip is blocked (0/2); defaults/paired complete-fixture audits now pass 33/37 (ReleaseFinal). Neither old passes nor synthetic complete fixtures establish current real-pack success.

The production host discovery code is exercised by `验证/RealPackRoundTrip`, alongside the real publisher, importer and switch transaction. The fixture uses the existing lightweight profile (66 distinct mod targets, 513 parameters), preserves the selected CN-1.1.8 voice implementation and leaves the source installation untouched. Local import runs with network access disabled; the store variant declares PEAKLib.Items 1.6.2 as a downloaded dependency. Both variants check configuration isolation and fixed-host preservation through A → imported pack → A.

For this lightweight pack, the receiving installation needs the new MCE 2.0.0, its loaded hard dependencies (ModConfig 1.8.2, PEAKLib.Core/UI), BepInEx and MonoDetour 0.7.16 core libraries already installed. Install dependencies through their normal installation process before importing; pack switching does not install core files. ClearComms, HowManyTimesWeClimbedTogether and PEAKER remain disabled historical targets and are shown as missing when absent.

Historical project check entry points: `验证/PackLibraryChecks`, `验证/LocalDeploymentChecks`, and `验证/ConflictPublicationChecks`. Current audit evidence is listed below; no combined acceptance total is claimed. `验证/RealPackRoundTrip/resume-final.log` records the real-package acceptance run. If a recorded source mod has since been removed from its live path, the fixture explicitly logs reuse of the previous project-local acceptance copy; the final lightweight fixture retains PlantholderOverhaul and its translation this way. Updated MCE release archives are saved separately under `发布/`; tested lightweight archives remain under the corresponding `验证/RealPackRoundTrip/交付_<timestamp>/`. Existing archives are retained. These checks run in isolated project directories; game UI and real-session switching are the remaining in-game acceptance steps.

## 2026-10-28 audit fix status (2.0.0)

The reported 14 issues have source fixes covering update/switch exclusion, recoverable ownership and enablement, core write boundaries, final-environment dependency checks, atomic profile/config pairs, durable grouping, uninstall dismissal, log timestamps, proxy routing and bounded/validated resources. See [the itemized implementation and acceptance checklist](验证/CommercialFix20261028/修复清单.md).

The earlier source-only checkpoint is superseded by the authorized commercial-fixes delivery below; the previous installed release was `inline-uninstall` (2026-10-27). The recorded plugin Release build passed with **0 warnings / 0 errors** (`更新恢复补全/plugin-build-release-fixed.log`). Recovery follow-up passed **49**, not the superseded 22, in `更新恢复补全/production-checks.log`. These paths are under `验证/CommercialFix20261028/`. Integration and profile fault-test logs are linked in the checklist; profile integration has finished; the final serial rerun (`最终验证/最终验证结果.md`) passed toggle 49, update-recovery 39, profile-integration 16, with exit 0 and 0 compile warnings, and no combined total is claimed. Isolated checks are not full commercial acceptance; actual game UI, manager synchronization and real-session recovery remain to be verified.

## Support and source

Read **User guide** in the overview for detailed help on settings, profiles, groups, authoring, exporting and updates. Report an issue with your MCE version, the action you took and the relevant game log.

[Source and issues](https://github.com/AiAe-156/Peak-ModConfigEnhance) · [Thunderstore](https://thunderstore.io/c/peak/p/AiAeT/ModConfigEnhance/) · [Changelog](CHANGELOG.md)

## 中文说明

# ModConfig Enhance 2.0

为 **PEAK** 提供游戏内模组与整合包管理中心：查找设置、整理模组、切换配置方案、管理更新，并直接制作整合包介绍与发布文件。

**2.0.0 是自商店公开版 1.0.2 以来最大的一次更新。** 原有设置页增强继续保留，新增完整的模组概览、配置方案、介绍编辑器和发布工作区。

## 当前源码检查点：完整独立环境（2026-10-28，保持2.0.0）

完整包独立能力已实现并在项目沙盒验证，**本轮授权交付的实际结果见文末ReleaseFinal记录；部署前现装为 `installed-status`，真实core／库存未迁移**。实现、分项测试及残余实机事项见[交付检查清单](验证/IndependentEnvironment20261028/交付检查清单.md)。以下带日期的旧交付／固定宿主说明作为历史保留，不覆盖当前完整环境契约；无真实游戏视觉验收，不宣称全面商业保证。

## 面向普通用户

### 从这里开始

1. 通过 Thunderstore Mod Manager／r2modman 安装，或按下方说明手动安装。
2. 在主菜单设置页打开“模组概览”；游戏中按 **ESC**，使用左上方的概览入口。
3. 找到模组后点击齿轮进入设置，悬停配置项即可阅读说明和默认值。
4. 需要详细操作说明时，打开概览里的“使用手册”。

### 在一处管理当前环境

| 功能 | 可以做什么 |
|---|---|
| 模组概览 | 按带颜色的可折叠分组浏览；查看名称、版本、作者、来源和依赖，快捷打开设置、主页和目录。 |
| 设置侧栏 | 搜索模组和配置项、展开分区、置顶条目、按设置类型筛选；说明面板解释当前选项。 |
| 配置方案 | 为同一套已安装模组保存不同参数、快捷键、启停目标、分组和介绍，创建、复制及改名个人方案。 |
| 冲突建议 | 整合包建议对个人方案同样生效；浅黄色角标可查看详情，仍可继续应用，也可在本机忽略主动提醒。 |
| 快捷键 | 对照当前按键、模组默认和方案预设；搜索操作，检查重复绑定及未绑定项。 |
| 模组更新 | 检查已确认的雷霆商店来源，主动选择更新或回退，在独立队列小窗中查看进度和结果，直接从商店下载。 |
| 多语言 | 内置英文、简体中文和繁体中文；支持附加语言文件，游戏内可切换、重载译文。 |
| 操作与外观 | 支持键鼠和手柄导航、游戏纸面风格界面，以及可选的 TimeTheme 昼夜配色。 |

### 保存多个整合包

整合包页采用左侧库、右侧详情。左侧每个矩形条目将名称、启用标记、类型／版本、文件数放在同一底板内，选中态与启用态分开。进入页面先显示基础信息，程序集详情在后台加载；各内容版本单独缓存，旧包摘要可持久缓存，未变化的索引和任务文件不再重复解析。选中已保存的包可直接查看介绍、模组、依赖和文件树，不需要原 ZIP，也不会重新下载。导入在独立面板中核对；“预览切换影响”单独检查当前环境和目标包的切换操作。

添加整合包后，MCE 会**先检查**。顶部横幅直接给出结论；“需要处理”“需要什么”“模组”“全部文件”（文件夹树）几页用大白话说明其余内容。存入只是保存，你切换之前不会改动游戏。

在 **管理中心 → 整合包** 导入 ZIP 或已解压目录，程序自动识别本地模组、固定版本商店依赖、作者资料和配置。带明确身份的发布包会建立或更新对应整合包库；没有包身份的单方案会作为个人方案追加到当前包。缺失文件、宿主版本不兼容和依赖冲突会列出，补齐后才能启用。

首次切换前，点击**保存当前配置为临时整合包**，命名并预览完整环境；确认后等待PEAK退出，捕获core、plugins、patchers、运行配置和组件ledger，存为新配对revision，不替换live文件。库仍位于 `BepInEx/MCE/packs/`，临时包不自动删除。切换等待退出，移出旧包独有受管文件并应用预览目标；失败保留备份并恢复原环境。

完整环境的core/plugins/patchers、BepInEx侧宿主及运行配置按包revision独立保存（schema2／protocol2），MCE及必要依赖／助手不再是跨整合包固定宿主。目标须自带完整依赖及管理协议，不能借live core／宿主补缺。旧当前包须明确预览升级、确认并退出后捕获新revision，保留旧版本；旧非当前包须提供可验证原内容或明确来源，不自动复制当前core伪造历史。读取不自动迁移；schema2索引可混存legacy，完整与legacy混切阻断并要求补全。

游戏根Doorstop和 `PEAK_Data/` 仍是平台前置，不随包替换。`BepInEx/MCE/` 管理树、`config/ModConfigEnhance/`、`config/MCE/` 及 `config/com.aiae.modconfigenhance.cfg` 本机偏好排除；语言选择仍是本机设置，不宣称全局cfg每项功能已绝对按包隔离。等待助手完成后再启动。

发布按所有选中方案的目标并集收录（包括停用项）、传递硬依赖及明确附带内容，保留引用缓存包、资源和启动补丁的完整结构。预览和 ZIP 使用同一清单。作者根清单固定版本，第三方依赖优先复用满足要求的已选同包版本。缺失的停用目标保持可见；启用目标或必需依赖缺失阻止启用。

点击库存文件名可打开共享文件查看器。Markdown 默认渲染，编辑时可切换源码与排版；支持图片／GIF／视频和文本／配置查看。文件修改先存入草稿，再显式保存为个人副本，不覆盖签名原包或现装文件。已存包详情提供语言文件选择及应用按钮。

库内预览仅使用所查看整合包的所选语言组翻译模组名称，不再串用当前游戏词典。缺失词条保留原文。语言文件不是自动机翻：介绍没有对应译文时显示作者原文，文件名和媒体路径不翻译。

空间管理按下载缓存、详情缓存、切换恢复备份和整合包回收区显示后台加载的文件树。可清理项目分别带删除按钮，执行前按选中项目重新核对；子文件可查看，不提供越过安全范围的任意删除。整合包详情和文件预览提供打开目录／打开文件／定位文件快捷操作，只有允许清单内的文本使用固定 Windows 记事本打开，其余类型统一定位，图片／视频仍可用内部查看器。任务忙碌时禁用删除，后台拒绝操作也保留原切换／恢复任务。个人副本保留方案、词典与已应用记录，调整副本路径和身份，不继承待执行操作；文档引用的 media／fonts 素材可一并保存，修改的作者资料在副本内解除原签名并校验，原件保留。文件关闭按钮和统一返回入口均检查未提交草稿，提供保存个人副本、放弃和继续编辑。

### 删除、空间管理与操作确认

非当前启用的库内整合包可先预览范围，再移入回收区。包内模组移除会生成新的库存版本，并检查硬依赖；当前启用包需先切换出去。空间管理分别列出下载 ZIP、可重建的详情缓存、已完成切换的恢复备份和回收区永久清除。每次执行前显示文件及空间范围，确认时重新核对；保护当前启用包、未完成操作和被引用内容，不包含项目源码备份。回收区恢复目前需手动处理，不自动清理历史备份。

导入预览只读取商店元数据，点击“同意下载并补齐”后才下载该预览明确列出的 ZIP。临时快照将语言词典保存到自身工作区。文本编辑支持 Ctrl+Z 撤销，Ctrl+Shift+Y／Ctrl+Y／Ctrl+Shift+Z 重做。切换时会打开独立 Windows 进度窗口，退出游戏后仍显示当前阶段和完成结果。

### 保存配置与生效时机

| 按钮或状态 | 含义 |
|---|---|
| **保存配置** | 提交工作区草稿及资源待办；安装、更新和卸载先确认清单与授权。 |
| **局部保存** | 保存快捷键、参数、资料、分组或介绍，只提交对应范围。 |
| **变更检查目录** | 主动查看分类、模组、原值、新值及生效时机，不隐式保存。 |
| **未保存橙标** | 编辑尚未提交，跳过的无效项和启停目标继续保留。 |
| **延迟生效** | 按项声明为重新开局、重启游戏或启用后使用；未知时机由模组决定。 |
| **已加载／未加载** | 本次会话实际加载状态，保存启停后当前插件继续运行。 |

选择预设只预览。预设仅含参数、快捷键及启停目标，整合包共用资料、介绍、分组和媒体。作者预设首次保存选择原地或另存；原版基线保留，偏离原版的星号在保存后继续显示。纯配置导出为 `.mceconfig.json`，仅导出已保存值。

停用使用 `.dll.d`，既有 `.old` 等备份保留；早年版本写的 `.dll.disabled-by-mce` 仍被识别为本管理器的停用状态。普通依赖缺口可明确忽略；MCE及运行必需依赖树受保护，精确归属、路径边界、共享文件与备份回滚检查保留。当前进展见 `验证/统一保存重构/当前验收清单.md`。

### 下载和更新

在“管理中心 → 网络”选择**系统代理、直连或自定义 HTTP(S) 代理**，分别测试商店连接和下载连接。自定义代理需要使用其保存操作。

选择更新后才会安装。队列分别显示下载进度、等待退出、等待重启、成功和失败，并保留回退历史。MCE 自身、共享基础组件及启动补丁的更新会等待退出游戏。随包更新助手面向 **Windows x64**，已包含所需运行环境。

如果使用模组管理器管理安装，可在 MCE 设置中填写实际管理器档案目录，让启停改动同步到该档案；应用这类改动前先关闭管理器。

## 面向整合包作者

### 在游戏里制作整合包体验

管理中心按 **资料、整合包、分组、方案、更新、网络、工具、发布、预览** 排列。“发布”和“预览”位于最右侧；“更新”打开独立队列小窗。

- **资料：**编辑整合包标题、作者、版本和介绍，管理单个模组的显示名称、简介、显示版本及来源，仍可查看真实插件／安装包版本。
- **分组：**设置名称、颜色、说明和顺序，在分组列表中搜索、选择模组。分组用于整理界面，磁盘上的安装目录保持原位置。商店安装分组持久保存到确认时绑定的包／方案，不随之后浏览目标变化；失败保留待办，不保存其他未提交草稿。
- **方案：**准备推荐参数、快捷键和模组启停目标，提供多套预设并选择发布包默认方案，玩家可保留自己的个人副本。
- **介绍：**像编辑文档一样使用标题、列表、链接、颜色、图片、GIF、视频和导入字体；在页面中调整媒体大小和排列，阅读时保持同样布局，也可导入／导出 Markdown。
- **翻译：**导出原文或缺失词条，维护模组名称、简介和设置项译文，随包附带所选语言；玩家个人覆盖与作者资料分别保存。
- **冲突预设：**作者模式下进入“发布 → 整合包冲突预设”，指定两个或多个模组，并选择“任意两项同时启用”或“全部同时启用”。建议适用于所有方案，玩家始终可以继续。
- **更新策略：**设置全局和单个模组的更新许可，确认商店身份。显示标签和翻译名称与实际用于更新的身份分开。

### 选择导出用途

| 格式 | 用途 |
|---|---|
| **仅作者资料** | 为已经装好模组的玩家提供方案、分组、介绍、翻译及引用的媒体和字体。 |
| **完整本地整合包** | embedded内置完整BepInEx环境（core、宿主及工具）与精确归属，输出一个ZIP。 |
| **雷霆商店包** | 完整模式可选组件内置或固定商店依赖（resolved）；旧依赖式legacy通道明确非完整。上传前核对平台政策和再分发许可。 |

完整发布预览与ZIP共用MCEEnvironment.json分配：embedded含最终全部字节；resolved固定Key/Version，预览只读元数据，同意后才下载核验ZIP并封存最终files／ledger，确认时重验source／seal。不从live补缺，完整入库后离线切换。发布捕获同一live payload／ledger时点，不拼接旧saved内容和新workspace状态；owned证据不一致会阻断。

本地与商店安装 ZIP 都在 `BepInEx/config/` 附带所选默认配置，供模组首次启动读取；主菜单阶段继续登记方案及补充应用。升级整合包前请备份个人配置。**发布包默认方案**决定导出的配置和首次安装时的启停目标；“默认配置”是另外的个人模式。先存储希望发布的参数，其余配置文件和附件需要明确选择；介绍引用的资源随对应方案一起收录。

**预览页**按内容类型分类，用可折叠目录树显示文件。选中文件可以查看完整相对路径、文本、Markdown 或媒体；支持的文本和配置可直接为本次导出编辑，JSON／配置修改在存储前会进行格式检查。

**存储本次发布修改**只保存当前发布会话的改动。需要留存时，请在关闭管理中心前生成 ZIP；重新打开不会恢复这份草稿。编辑当前方案的介绍使用现有介绍编辑器，会保存回该方案。

作者方案以只读资料分发，可选签名用于核对作者资料和资源的完整性。使用签名时，请另外备份发布签名身份。生成的包保存在本地，由你分发或上传。

### 推荐制作顺序

1. 整理模组资料、来源和分组。
2. 记录推荐参数与快捷键，编辑启停目标，制作介绍页。
3. 应用方案；涉及模组启停时重启游戏。
4. 打开“发布”，选择格式、方案、发布包默认方案、语言和附加文件。
5. 检查“预览”，生成 ZIP，再用于对应分发渠道。

冲突预设独立于配置方案保存，三种发布方式均附带 `MCEPack/conflicts.json`，并纳入作者资料签名。玩家的忽略记录只留本机；规则内容变化会重新提醒，已忽略的详情仍保留在浅黄色角标中。

## 安装与升级

**基础依赖：**PEAK 用 BepInEx 5、PEAKLib.Core、PEAKLib.UI 和 PEAKLib.ModConfig **1.8.2**。模组管理器会安装声明的依赖。TimeTheme 为可选配色联动。不需要 XUnity AutoTranslator；装了它并开着在线机翻时，管理器会提醒并可一键关掉。

**手动安装：**先装依赖，再将发布包中的 `BepInEx` 文件夹复制进 PEAK 游戏目录。让 `Tools`、`Demo` 及随包运行库与 `ModConfigEnhance.dll` 保持原有相对位置。MCE 保留一份安装；沿用自定义目录升级时，用 ZIP 中 `BepInEx/plugins/ModConfigEnhance/` 的内容替换该目录。

**从商店版 1.0.2 升级：**先备份配置并退出游戏，再更新 MCE。原有翻译和概览数据会迁移到配置方案结构；保留 `BepInEx/config/ModConfigEnhance/` 可继续使用个人方案、语言和资源。应用前留意“正在查看”“当前应用”和待处理状态。

### XUnity AutoTranslator

翻译由管理器自己的语言文件完成，不再写入 XUnity 的词典、不再切换它的语言，也不再拦截它的机翻。XUnity 开着在线机翻（`AutoTranslatorConfig.ini` 的 Endpoint 不为空）时，管理器每次启动提醒一次，可以一键清空它的端点（先备份 ini）。

### 个人文件位置

| `BepInEx/` 下的位置 | 内容 |
|---|---|
| `config/com.aiae.modconfigenhance.cfg` | MCE 全局设置。 |
| `config/ModConfigEnhance/language/` | 个人语言文件和覆盖。 |
| `config/ModConfigEnhance/profiles/` | 首次保存临时整合包前使用的个人方案、参数、介绍及引用资源。 |
| `config/ModConfigEnhance/output/` | 你导出的东西：`translation/` 待翻文本，`packs/` 导出包（也可在发布时另选目录），`logs/` 日志导出。 |
| `config/ModConfigEnhance/data/` | 管理器自己的数据：`runtime/` 运行状态和缓存，`backups/` 配置及迁移备份，`help/` 改过的说明，`legacy/` 2026 年 10 月以前的旧数据。 |
| `MCE/` | 下载更新队列、工作文件和更新备份。 |
| `MCE/packs/<id>/` | 整合包库、可恢复内容版本，以及各包独立的个人方案和应用状态。 |

## 切换失败与恢复

产品在完整验证工具并发布缓存后生成 `BepInEx/MCE/MCE-Recovery.cmd`／`.ps1` 独立入口，从已验证的 `MCE/tools/p2-*` 缓存运行，不依赖当前包core／插件。仅查看／回滚当前任务引用的既有未完成update／pack／shop事务，不消费新commands、不联网、不新建切换或迁移、不杀进程。未知／损坏日志拒绝并保留；孤立历史journal及 `enablement-recovery.bin` 不属于此入口自动恢复范围。本机真实入口尚未生成或操作，窗口视觉待验。工具清单是本地一致性校验，不是发行者安全签名。

切换采用备份及事务日志。失败后先回滚原环境；回滚完成才允许“重新检查并重试”，重试生成新事务并保留原备份。恢复未完成时仅允许“重试恢复原环境”，禁止新切换。独立进度窗展示阶段、源／目标路径和系统错误，支持复制详情、打开事务目录及重试。日志与 MCE 界面均显示恢复结果，不把恢复未完成误写成环境未变。

## 当前环境与切换保存（2.0.0）

完整当前包的core、plugins、patchers、宿主及运行配置变化归其运行环境；新增文件不自动成为商店组件owned删除证据。已保存payload与immutable revision-state一一绑定，运行更新只改live／workspace／ActiveFiles，不覆盖已保存pair。同一整合包内部多个mod package可共享同path／同hash内容并保留各owner；不同整合包revision独立，不共享当前core。管理树及本机偏好仍排除。

切换预览提供“保存当前配置并切换”和“直接切换”。前者在退出后收取最终环境并更新已保存版本；后者保留已保存版本，另存完整未保存运行环境。切回时自动恢复未保存环境并继续标记未保存，再次切换仍可选择是否保存。未保存版本受库存引用保护，不列入缓存清理。事务失败恢复原索引及原环境。

整合包管理采用“整合包库／空间管理”独立子分页，后台详情更新保留左侧列表，删除／切换预览保留选中态、滚动与焦点。

## 游戏内模组商店（2.0.0）

概览工具栏“模组商店”打开平面卡片浏览：搜索、标签、已安装、有更新、收藏、分页和排序。详情为介绍／依赖项／更新日志／版本四页，介绍和更新日志使用只读 Markdown 阅读器。商店以作者原名和包ID识别，不假设存在译名。网络／代理入口与现有下载设置共用。

新安装先只解析依赖，展示清单并等待同意下载；下载到缓存并检查后，再确认退出游戏后安装。商店精确记录所写文件归属；已登记包和可精确认领的独立单 manifest 现装包可预览安全卸载，检查最终插件环境的硬依赖、保留原配置，不按目录猜删。legacy普通模组沿用现有更新器；完整当前包可经明确预览确认的商店批次处理core／patcher安装、升级、降级、重装及受保护卸载，绑定当前包、index／ledger修订及精确文件。旧core须经批准下载原版ZIP逐文件核对，不整目录认领。普通更新器仍禁止写core，legacy core操作仍阻断；MCE走既有自更新，必要BepInEx／MCE管理组件不可单独卸载。游戏根加载器不在批次范围。共享文件、用户改动、重复插件和目录链接受保护。

安装计划支持自动分组、已有分组或新建分组；主动安装模组的分组写入确认时绑定的包／方案并持久保存，依赖不强制改组。只有保存成功才消费完成标记，切包或只读作者方案等阻碍会保留待办并显示原因。商店运行记录位于对应workspace；完整快照／个人副本另存绑定revision的状态，不以可变workspace替代immutable state。安装、卸载、更新、切包共用助手互斥及失败回滚。

真实商店API在本机测试返回403，联网目录／文档／图标和游戏内布局待实机验证。卸载后对商店自动补齐且无其他包声明依赖的项目提示“可能不再使用的依赖”，可逐项预览清理；不自动删除、不提供连带卸载。清理仍重新检查反向依赖、文件归属和用户改动。手工安装仅在独立单 manifest 包可精确认领时开放卸载预览；散装、混合或归属不明仍提示人工处理。概览入口原地确认，提交后关闭窗口不撤销已提交队列。

## 运行日志与诊断导出（2.0.0）

概览右上角“日志”位于“使用说明”左侧，打开 BepInEx、游戏、自定义日志分页。自定义按钮直接展开来源列表，不再另设来源按钮；打开日志文件夹靠近来源，打开导出文件夹靠近导出且成功前禁用。界面只保留一个全文搜索框，日志等级与模组筛选使用下拉列表，跳到顶部／底部只改变阅读位置。

重复错误按来源、等级、对象、正文与调用栈合并，累计次数、首次及最近时间；重复项直接覆盖固定在日志区顶部，多个按首次出现顺序排列，超出固定区域可单独滚动，不重新刷入下方日志。复制整条包含计数；原始导出保持完整。正文按可用宽度自动换行，保留文字选择与调用栈折叠。

时间与模组／来源列收窄，超长模组名保留头尾、中间省略，悬停显示全名；正文换行后不显示横向滚动条。默认 BepInEx 文本通常无原时间，日志语言切换不翻译异常原文。

时间与模组／来源独立固定列；有原始时间优先显示原值，运行时捕获记录保留各自发生时间与序号；文件中无原时间的记录显示 `—`，不借用同文记录时间或轮询／接收时间。横纵滑块按内容长度自动伸缩，新增速率和连续重复数用来识别刷屏。支持后台增量读取、有限可见行渲染、搜索／来源／级别筛选、暂停刷新、跟随最新、较早记录、调用栈折叠及跨行选择复制。日志正文只读，搜索框支持粘贴；原文中的富文本标签按普通文字处理。

导出先选择“两种日志／单种日志”和默认勾选的“附带模组与配置信息”，再用 Windows 保存对话框选 ZIP 位置。默认生成两份标准日志和 `运行信息.json` 三个文件；取消勾选后不附元信息。导出完整可用来源，不受页面筛选或查看缓存限制；文件读取失败或检测到轮换时报告失败，不生成最终 ZIP。原日志正文保留原文，元信息遮盖已识别的敏感字段。仅用于 Debug 和故障排查。

配置差异以当前已应用快照为基准，单列已保存未应用方案和待重启项目；正在浏览的方案不替代基准。自定义日志在自身分页独立导出。公开 API 支持文件源、快速内存快照、主动提交通道，接入说明和无编译依赖示例见 `Demo/日志接入_API.md`。

空间管理四类均提供整类操作：前三类“清理本类”，回收区“清空回收区”。后台生成安全候选并预览实际数量／空间／保留原因，单项删除仍精确匹配项目；未完成任务受保护。文件夹与删除采用紧邻右对齐操作组。备份、详情缓存显示包名和日期，旧日期采用文件时间时明确标注；磁盘事务编号不迁移。

## 历史本机验收与交付位置（legacy契约）

以下固定宿主／预装核心要求为旧轻量格式历史记录。最新真实轻量包因PlantholderOverhaul_ZH默认方案缺启用依赖 `Murka124.PlantholderOverhaul`，本地／商店发布往返均阻断（0/2），522个源文件未变化。defaults／paired原夹具缺core/BepInEx.dll的失败保留；补全完整夹具后ReleaseFinal实跑33／37通过，不以合成完整夹具代替真实旧轻量包往返通过。

`验证/RealPackRoundTrip` 直接调用正式宿主识别代码，以及实际发布、导入和切换事务。验收使用现有轻量方案的 66 个不同模组目标、513 项参数，保留已选择的 CN-1.1.8 语音修复，源安装保持原状。本地包禁止联网导入；商店版以 PEAKLib.Items 1.6.2 测试依赖下载；两种格式都检查 A → 导入包 → A 的配置隔离和固定宿主保留。

此轻量包的接收端需先装新版 MCE 2.0.0、其已加载硬依赖（ModConfig 1.8.2、PEAKLib.Core/UI）、BepInEx 及 MonoDetour 0.7.16 核心库。依赖按各自正常安装流程预装，切包流程只检查核心兼容条件。ClearComms、HowManyTimesWeClimbedTogether 和 PEAKER 是停用的历史目标，缺失时保留提示。

历史项目回归入口：`验证/PackLibraryChecks`、`验证/LocalDeploymentChecks`、`验证/ConflictPublicationChecks`；本轮入口与实际日志见下文，不汇总为全面验收总数。实际轻量包验收日志为 `验证/RealPackRoundTrip/resume-final.log`。记录中的源模组后来从现装路径移走时，验收会明确记录使用上次项目内副本；本次通过这种方式保留迷你景观台及其汉化。新版 MCE 发行包另存于 `发布/`，受测轻量包保留于对应的 `验证/RealPackRoundTrip/交付_<时间>/`，旧包保留。验收使用项目内隔离目录；游戏界面及真实游戏会话切换留待实机验收。

## 本轮审查修复状态（2026-10-28，保持 2.0.0）

本轮报告的14项问题已有源码修复，逐项实现文件、验收入口、实际日志和实机待验见[修复清单](验证/CommercialFix20261028/修复清单.md)。此前“未打包、未部署”的源码检查点由下方授权交付记录接续；部署前现装为2026-10-27的 `inline-uninstall`。未执行真实商店安装／卸载。

主插件 Release 构建0警告0错误，依据 `验证/CommercialFix20261028/更新恢复补全/plugin-build-release-fixed.log`；补全回归以同目录 `production-checks.log` 的 **49项通过**为准，不再引用旧22项。整合记录保留早期编译失败及旧结果，后续补全日志已替代这些状态。方案集成已结束，最终串行复跑（`最终验证/最终验证结果.md`）启停49、更新恢复39、方案集成16均通过，exit 0、编译警告0；不声明合并总数或商业化全面验收。游戏界面、真实管理器同步、联网及实际会话恢复仍待实机验证。

## 字体字重规则

MCE自有正文、按钮、说明、侧栏、介绍和编辑器共用局部材质规则，控制普通及粗体增厚、描边、阴影；内联／回退字体子网格也随文字生成更新。导入字体与私有字体副本降低额外粗体幅度，保留文档中的加粗语义。原游戏共享字体和用户字体文件不改动。默认英文使用常规无衬线字形（Arial优先），中文回退到游戏当前语言字体；介绍明确选用的游戏字体或导入字体保持原选择。若明确所选字体本身为Thick/Black，可在字体选择中换用常规字形。

## 支持与源码

概览中的“使用手册”提供设置、方案、分组、介绍编辑、导出和更新的详细说明。反馈问题时，请附上 MCE 版本、操作步骤和相关游戏日志。

[源码与问题反馈](https://github.com/AiAe-156/Peak-ModConfigEnhance) · [雷霆商店](https://thunderstore.io/c/peak/p/AiAeT/ModConfigEnhance/) · [更新日志](CHANGELOG.md)

## Commercial-fixes authorized delivery / 商业审查修复授权交付

- Prepared / 交付准备时间：2026-10-28T02:43:57.425208+08:00，版本保持 **2.0.0**，不改功能逻辑。
- Release / 发行包：`发布/ModConfigEnhance_2.0.0_20261028_024357_commercial-fixes.zip`；按重新收集的 release-files 全量交付（22 文件，实际数量及完成时间以部署 JSON 为准）。
- Full pre-deployment backup / 完整现装备份：`备份/部署前_商业审查修复_20261028_024357/installed/`。
- Deployment result / 部署实际成功、停止或回滚状态：`验证/CommercialFix20261028/部署/deployment_20261028_024357.json` 及同名日志；本段在打包前写入，不代替实际部署结果。
- Final serial checks: 19 run entries + 3 builds exited 0, no compile warnings; see `验证/CommercialFix20261028/最终验证/最终验证结果.md`. 游戏 UI、管理器同步、联网和真实会话恢复仍待实机验收；不启动游戏，不改用户配置、库存或队列。

## 2026-10-28 network/UI integration follow-up / 网络界面收尾（2.0.0）

Source follow-up (now prepared for authorized delivery below): IME caret composition is counted once; network saves observe their processed command and a fresh matching target, ignore uncorrelated queue errors, retain late confirmation after delay, and safely replace/release callbacks on resubmit/close. Release build: 0 warnings/errors; UiChecks 10023, production state 23, adapter 6, resource regression 66, network policy 15 passed. Real network retry succeeded through the .NET production chain (2581 listings / 9715 versions); Unity visuals and live-session behavior remain unverified.

此前源码修复检查点未部署；本次授权交付见下文，仍不改真实配置/库存/任务。修复IME重复位移、旧全局错误误判及延迟后丢失生效回调；支持重新提交与安全关闭。网络真实成功来自.NET生产链路，不代表Unity视觉验收。首次TLS EOF及后续成功日志均保留，详见[收尾修复结果](验证/NetworkUiFix20261028/收尾修复/收尾修复结果.md)。


## Network/UI authorized delivery / 网络与界面修复授权交付

- Prepared / 准备时间：2026-10-28T17:38:58.381245+08:00；版本固定 **2.0.0**，保留既有改动，仅完整构建、打包和部署，不改功能。
- Package / 发行包：`发布/ModConfigEnhance_2.0.0_20261028_173858_network-ui-fix.zip`；按 release-files 全量交付。
- Full backup / 完整现装备份：`备份/部署前_网络与界面修复_20261028_173858/installed/`。
- Actual result / 实际成功、阻断或回滚结果、文件数与完成时间：`验证/NetworkUiFix20261028/部署/deployment_20261028_173858.json` 及同名日志；本段打包前写入，不预先声明成功。
- Assistant publish precedes plugin Release Build;CollectReleaseFiles, both with deployment disabled. ZIP CRC and per-file target existence/size are checked; no routine hashes. 两次部署流程进程检查，复制前确认 PEAK/助手均未运行；不启动/关闭游戏，不改配置、库存、队列、其他模组、core 或 PEAK_Data，不执行真实商店操作。
- Prior verification: `验证/NetworkUiFix20261028/收尾修复/收尾修复结果.md`；Unity视觉、IME实机与真实会话交互仍待验，不声明全面验收。


## 2026-10-28 shop installed-state follow-up / 商店已安装状态修复（2.0.0，历史源码检查点）

历史源码检查点（2026-10-28；下述只读 core 权限说明不代表 2026-10-29 的现行安全预览契约）。当时只更新源码和隔离验证，**未打包、未部署**。商店卡片使用独立紧凑绿色“已安装”文字徽标，本地／商店版本分开，有更新单独显示；禁用文件和缓存停用保留区别与实际路径。卡片、已安装／有更新筛选、详情和核心列表共用只读聚合快照，覆盖当前包有效 ledger、已确认来源插件及 core/patchers manifest，后台刷新并隔离切包旧结果。状态显示不授予卸载权限，core 保持只读、需人工处理，patcher 沿用原策略。

两个指定本机 manifest 只读核对均为 **0.7.16**（商店截图为 0.7.17）。PeakStatsEx 的确认来源为 `LengSword-PeakStatsEx`，不把旧商店缓存当本地版本。Release `--no-restore -p:EnableDeployment=false` 构建0警告0错误；ShopChecks 29、ShopSearchChecks 9、新生产识别隔离检查29通过。证据和待验边界见 [本轮验证结果](验证/ShopInstalledChecks20261028/验证结果.md)。Unity视觉尚待授权部署后验收。

## 2026-10-28 installed-card tester follow-up / 已安装卡片收尾（2.0.0，历史源码检查点）

安装快照以排序后的内容签名比较，同内容新实例或证据顺序不同不重绘。实际变化时保留当前详情、分页与列表滚动；详情仅原地更新安装状态及按钮，不为徽标重建正文阅读器。首次／切包明确显示“正在读取安装状态”，读取未就绪或失败均禁安装／更新／卸载；失败显示原因，可点击“重试安装状态”，同时保留后台重试。就绪后仍按原归属策略开放操作，core始终只读；过期generation/context结果丢弃。

本轮Release构建关闭部署，0警告0错误；新生产生命周期／签名与适配器检查25、ShopChecks29、ShopSearchChecks9、原识别检查29通过。没有把随机几何测试当视觉验收，没有启动游戏、打包或部署，也未改真实配置／库存／队列。详见 [收尾结果](验证/ShopInstalledChecks20261028/收尾结果.md)。上述未部署状态为历史源码检查点，本次授权交付接续如下。

## 2026-10-28 ComponentActions follow-up / 组件操作与概览收尾（2.0.0，历史源码检查点）

历史说明：以下记录保留当时独立只读基础组与 core 人工处理策略；现行固定顶部普通行、反向树及非保护组件安全预览以 2026-10-29 检查点为准，不将历史禁用策略作为现行能力说明。当时只修过时测试并完成已有 ComponentActions 的验证与文档收尾，**未改生产安全逻辑、未打包、未部署**。既有实现按所选精确版本区分安装／升级／降级／重装；普通模组之外独立展示 core／patchers 的身份、本地版本、依赖与商店入口，不加入普通模组统计、启停、方案或导出目标。core 仍只读；最低需求不足时阻止操作并提示人工处理，不自动下载或写入。

普通 plugins／patchers 无 ledger 时，`CanUninstall=true` **只允许进入安全预览核查，不表示可直接删除**。实际执行仍须通过独立单 manifest 目录的精确认领、文件清单重验、最终环境反向依赖检查与事务保护；共享、歧义、目录链接及用户改动不因 UI 入口而放行，卸载保留配置。版本歧义、缺失／未知版本、缓存停用、core、读取中／失败均阻止操作，owned 也不能绕过。

旧生命周期测试刻意把同包 1.0／2.0 两证据用于顺序签名，却又期望可安装；现保留该样例并断言歧义阻断，另用单版本样例测试操作入口，没有删除失败场景或放宽生产保护。本轮实际通过：生命周期／签名／适配器 **46**、Actions **62**、独立概览 **28**、ShopChecks **29**、CommercialAudit **18 passed / 0 failed**；五个检查工程及主插件 Release 构建均 exit 0、0 警告／0 错误，全部关闭部署。命令、日志、旧失败定位和边界见 [整合验证收尾结果](验证/ComponentActions20261028/整合验证_收尾/整合验证结果.md)。游戏 UI 与真实会话未验收。

**历史源码检查点：当时“整合包core完全独立”仍设计中、未实现；现由本轮完整独立环境实现接续，见交付检查清单。** 本段仅记录先前ComponentActions范围，不代表当前实现状态。本轮没有写真实配置、库存、队列、core 或游戏文件，原现装仍为上一轮 `installed-status` 交付，未更新为本轮 ComponentActions 源码。

## Installed-status authorized delivery / 已安装卡片与 core 识别修复授权交付（上一轮历史交付，非本轮）

- 准备时间：2026-10-28T19:11:08.627664+08:00；版本固定 **2.0.0**。保留既有源码修复，本轮只更新维护／交付说明并完整构建、打包和部署。
- 发行包：`发布/ModConfigEnhance_2.0.0_20261028_191108_installed-status.zip`；按完整 release-files 交付，含 win-x64 自包含助手与许可证，不覆盖旧包。
- 完整现装备份：`备份/部署前_商店已安装状态_20261028_191108/installed/`。
- 实际成功、运行阻断或回滚状态、进程检查、文件数及完成时间：`验证/ShopInstalledChecks20261028/部署/deployment_20261028_191108.json` 及同名日志；本段在打包前写入，不预先声明成功。独立脚本沿用 NetworkUiFix20261028 可靠流程。
- 先助手 publish，再主插件 Release Build;CollectReleaseFiles，均 --no-restore 且 EnableDeployment=false；核对清单、产物时间、ZIP CRC、目标存在及大小。开始和复制前均检查 PEAK/MCE.UpdateWorker，运行即停止，不杀进程、不改任务。仅更新既定现装目录，不改配置／库存／队列／core／PEAK_Data／其他模组，不执行商店操作或启动游戏。
- 已通过的独立验证及边界见上述收尾结果；Unity视觉、阅读位置和真实切包交互仍待实机验收，不声明全面验收。


## ReleaseFinal independent-environment delivery / 独立整合包封版（2.0.0）

- Prepared / 准备时间：2026-10-28T22:55:17.444927+08:00。未改生产逻辑或测试断言；Worker共享obj资产覆盖已通过restore前传入每项目唯一中间目录修复，旧obj及失败日志保留。
- 最终证据：`验证/IndependentEnvironment20261028/ReleaseFinal/summary-final.json`；Foundation118、Transactions51、Shop82、ShopUi7引用同轮最终源码原日志；Worker26＋真实CLI、Integration29、Publication51、重新publish CrossFlow44、ShopChecks33、Actions82、PackLibrary235、ShopLifecycle50、LocalDeployment46、CommercialAudit18、defaults33／paired37／profile16／legacy-negative3、RecoveryLauncher32实run均exit0。
- defaults／paired为完整夹具33／37通过；真实旧轻量包仍缺Murka124.PlantholderOverhaul，negative3证明拒绝与522源文件不变，实际发布往返0/2，不能称成功。CrossFlow保持原saved pair不变，不声称整个B目录零变化。Unity视觉／真实游戏会话未验收。
- 新发行包：`发布/ModConfigEnhance_2.0.0_20261028_225517_independent-environment.zip`；完整现装备份：`备份/部署前_独立整合包_20261028_225517/installed/`。完整release清单23文件，protocol manifest覆盖完整助手；产物时间核对、check-only与CRC检查，非例行部署hash。
- 实际部署成功／进程阻断／回滚、大小／文件数／完成时间见 `验证/IndependentEnvironment20261028/ReleaseFinal/attempt_delivery_20261028_225517/deployment_20261028_225517.json` 及同名日志。本段打包前准备，不预写部署成功。
- 仅更新MCE既定启用目录，开始和复制前检查PEAK与MCE.UpdateWorker均0，任一运行即停且不杀。完整备份后全量复制并逐项存在／大小核对，失败恢复。未操作真实core／PEAK_Data／配置／库存／队列，不执行商店操作／迁移／切包，不启动游戏。Integration29再次证明worker启动不自动迁移；新功能只在用户未来显式预览确认后运行。

## 2026-10-29 组件列表与依赖页面（2.0.0；已部署）

基础组件按资源包合并，分别统计组件数和安装位置数。当前现装目录为5个组件、7处位置；缺少清单但有唯一、相同运行文件证据的位置会归入对应组件，其余未识别目录也会保留显示。

名称悬停最多两层、每个方向最多8项。点击组件名称打开详情页：正常宽度、文字居中的操作按钮，以及可展开和折叠的“依赖的模组／依赖它的已安装模组”树。多个安装位置可分别打开；依赖树默认全部展开，名称、灰色原名与版本、加载状态放在同一行，名称限宽并省略；文字框仍按实际字体行高测量。两种悬停框使用同一编号格式，首层也显示↳，混合类型保留(Core/Patcher)。概览、快捷键页及设置页左右两侧字号、间距一并缩小。只有已加载且实际提供可显示设置项的模组才启用齿轮；下拉框收起时保留当前值文字。重复依赖依据和包内引用只在展示时合并，卸载仍按现有流程重新核查。

默认文本使用英文，补齐421条内置词条，统一173组历史重复译文冲突，拆分6种仅靠空格区分的前缀。2509条词条的英文默认、译文占位符与重复键检查通过。保留旧配置身份以兼容原配置文件。本轮23个文件已于2026-10-29 16:33部署到现装MCE目录；旧文件完整备份在 `备份/部署前_组件界面与翻译修复_20261029_163300/installed/`。实际游戏布局与交互在下一次启动后检查。

## 历史：2026-10-29 dependency-card-fix 授权交付（2.0.0）

- 本轮删去名称列内自造树，复用普通模组名称 DependencyCard，固定42行、名称无 AddHint；巨大英文路径提示改为短原因，完整原始证据从来源菜单“查看检查详情”单独查看。
- 锁标与菜单统一 SourceUi.DependencyDiagnostic / DiagnosticSummary；仅 !CanUninstall 绘制⊘，MCE保护路径优先于 unknown，局部 unknown 且允许安全预览时不绘禁标。cycle/shared/truncated 标记三语；固定模式 resize 保持原屏幕定位锚点。未改 Graph/Shop 后端权限。
- 实跑 Layout20、Diagnostics19、UI36、概览28、新 Wiring12 全通过；主 Release 构建0警告0错误，EnableDeployment=false；助手完整 win-x64 自包含 publish、protocol manifest 验证通过。新测试初次缺 using 编译失败已修复并留日志，无真实断言失败。Graph/Shop 等沿用 Final/结论.md 已通过记录，非本轮重跑。
- 准备时间：2026-10-29T13:58:29+08:00。新包：`发布/ModConfigEnhance_2.0.0_20261029_135829_dependency-card-fix.zip`；完整备份：`备份/部署前_依赖提示布局修复_20261029_135829/installed/`。完整release清单预计23文件，不覆盖旧包；实际时间、文件数、包大小、成功/阻断/恢复状态见 `验证/InfrastructureTooltipFix20261029/ReleaseFinal/attempt_delivery_20261029_135829/deployment_20261029_135829.json`。
- 本段打包前写入，源码/包内/现装文档需一致；独立新脚本沿用上轮失败恢复机制。两次检查 PEAK/MCE.UpdateWorker，任一运行即停止、不杀；核对既定 MCE 启用路径及明确缓存无冲突，完整备份、复制前复检、全量复制后存在/大小核验，失败恢复。
- 只写既定MCE安装目录，不写真实core/PEAK_Data/配置/库存/队列，不执行卸载/迁移/切包、不启动游戏。**Unity实机视觉仍待验**；源码接线、纯诊断与几何测试不是视觉验收。

## 2026-10-29 基础组件历史检查点（2.0.0；由上方 dependency-card-fix 接续）

- 固定顶组位于普通可编辑分组之前，三语精确为：`基础组件(Core/Patcher)`／`基礎組件(Core/Patcher)`／`Core/Patcher`。类型为`核心组件(Core)`／`核心組件(Core)`／`Core`和`启动补丁(Patcher)`／`啟動補丁(Patcher)`。
- 使用普通行共享列与42基础行高，版本20／名称19／作者18／简介16字号；齿轮灰置、来源／目录入口同普通行，启用列“—”且无启停，不混入方案Mods、分组ID或导出目标。默认紧凑单行，类型／路径／保护原因有tooltip补充，不采用旧独立卡片布局。
- 当前已删除名称列内自造树及扩展行高，固定42行；悬停组件名称复用普通模组名称的同一 DependencyCard 展示反向依赖，不给名称叠加 AddHint。卡片有界测量、遮罩、固定模式滚动，cycle/shared/truncated 标记三语。
- 以实际GUID `com.aiae.modconfigenhance`为唯一种子，MCE自身及跨plugins/core/patchers的完整传递硬依赖包图永久禁止独立卸载；禁用DLL及同包资源仍受保护，不以owned状态绕过，不把soft依赖误作硬闭包。灰色⊘、tooltip及“为什么不能卸载”说明完整原因链。**更新入口保持可用**，但仍须原最终依赖／版本／写权限检查；证据缺失／采集失败时卸载拒绝，不猜测可删。
- 非保护且证据完整的patcher进入既有卸载弹窗；core进入原商店流程，核对真实安装快照、原包及实际现装版本，遵守完整环境／归属门禁、官方旧ZIP同意→下载批准→文件预览→用户确认；无自动确认或直接删除。worker在预览claim／下载前、批准／确认边界及commit写入前重采当前文件和workspace ledger，UI或旧图不能绕过。
- 当前环境采集在后台一次发布，行渲染只读图；revision／generation／workspace内容变化拒绝旧完成结果，编辑／滚动／键盘选择中延后重画。旧独立只读基础组说明仅为历史；产品中的用户预览能力不等于代理获准操作真实core。

实施文件、已有日志逐项结果、回归入口、上一部署机制和实机待验见[本轮交付清单](验证/InfrastructureTree20261029/交付清单.md)。本次只读日志与文档收尾，不改生产／测试，不构建（tester独占），不publish／打包／部署，不操作真实配置／库存／队列／core／PEAK_Data，不启停游戏。Unity视觉、滚动／手柄焦点和真实商店分阶段交互尚未验收，不引用旧测试总数冒充本轮重跑。

只读进程预检时间 `2026-10-29T02:13:55.9905084+08:00`：PEAK与MCE.UpdateWorker均0，无实际PID；不代表未来部署可跳过复检。**本轮实际新发行包路径待最终deploy负责人填写**；上一轮包和部署结果不代表本轮源码已交付。最终部署由主代理另行安排。


## 2026-10-29 基础组件依赖树 ReleaseFinal 授权交付（2.0.0）

- 准备时间：2026-10-29T02:52:07.545680+08:00；保留所有既有修改，本轮不增加功能、不改生产代码或测试断言。最终 ScopedFix 生产修复限 `src/Updating/ComponentDependencyCollector.cs`、`src/Updating/ComponentDependencyGraph.cs`、`Updater/ShopComponentProtection.cs`；其余既有 Graph/UI 实现保留。
- 本轮独占串行实际复跑：Graph21、PlatformFix30、ScopedFix12、Final WorkerGraph26、UI36、概览28、ShopUi7、Shop82、ShopChecks33、Actions82、Integration29、CrossFlow44 全部通过。证据：`验证/InfrastructureTree20261029/ReleaseFinal/verify_20261029_025014/summary.json` 及逐项日志。旧 Final 失败与旧日志均保留，由 ScopedFix 及本次复跑接续，不修改旧断言以迁就失败。
- 最新 worker 先 publish 到 `Updater/bin/bundle`，完整自包含协议 manifest 校验通过；主插件 `Release --no-restore -p:EnableDeployment=false '-t:Build;CollectReleaseFiles'` 构建0警告0错误，所有验证构建关闭部署。真实CLI使用本次最新bundle；测试仅项目沙盒，不是生产环境操作。
- 固定顶组简中 `基础组件(Core/Patcher)`、繁中 `基礎組件(Core/Patcher)`、英文 `Core/Patcher`；共享普通42行高及既有列坐标。类型标签 `核心组件(Core)`／`核心組件(Core)`／英文 `Core`，`启动补丁(Patcher)`／`啟動補丁(Patcher)`／英文 `Patcher`。此处原行内树已由本轮共享名称 DependencyCard 替代；灰齿轮、无伪启停保留。⊘仅按不可卸载绘制，短原因与菜单共用 DiagnosticSummary，详情单独查看。
- MCE自身及完整传递硬依赖永久禁止独立卸载，保持更新入口及原检查。ScopedFix按node/path保留失败：无关scoped损坏不再污染已知闭包；无种子、无scope失败、MCE闭包失败或完全未解硬依赖仍全局阻断；同path/hash合法共享只扩散文件保护，不串联另一个包私有依赖。未知owner的CanUninstall仅允许进入核查，不授权删除。其他组件安全预览和真实执行仍依赖完整环境、所有权、反向依赖、文件及最终确认校验；不自动确认、不直接删除。
- 交付准备路径：`发布/ModConfigEnhance_2.0.0_20261029_025207_infrastructure-tree.zip`；完整现装备份：`备份/部署前_基础组件依赖树_20261029_025207/installed/`。按当次完整release-files逐项核对（预计23，实际数以部署JSON为准），ZIP CRC与协议完整性检查，不做例行部署hash。
- 实际成功／阻断／回滚、包大小、文件数、两次进程检查及完成时间以 `验证/InfrastructureTree20261029/ReleaseFinal/attempt_delivery_20261029_025207/deployment_20261029_025207.json` 和同名日志为准。本段打包前准备，不预写部署成功；源码／包内／现装README和CHANGELOG须一致。
- 长期授权只更新 `BepInEx/plugins/0核心&基础模组/[界面]ModConfig增强-ModConfigEnhance/`，核对明确缓存无副本冲突。PEAK或MCE.UpdateWorker任一运行即停止，不杀进程、不改任务；完整备份后复制前再确认两者0，失败完整回滚。
- 不写真实core、PEAK_Data、配置、库存、队列或其他模组，不执行真实卸载／更新／迁移／切包，不启动游戏。Unity视觉、滚动／手柄及真实商店交互仍未验收。前文“未部署／待最终负责人”均为历史检查点，由本段及部署JSON接续。
