# 游戏内模组管理器（In-Game Mod Manager，IGMM）项目规则

> 2026-11-11 起工程在 `MODs/InGameModManager/`（本文件夹，git 仓库 `main`）。旧文件夹 `MODs/ModConfigEnhance/` 已还原成简化版 **ModConfig Enhance（MCE）** 的独立工程（1.0.2 源码起，现 1.0.3），两边互不相干；旧完整版的历史资料留档在本文件夹 `备份/旧ModConfigEnhance工程留档_20261111/`。
> 新的部署备份写到本文件夹的 `备份/`（不进 git），发布 ZIP 写到 `发布/`（不进 git）。

## 拆分后的身份（1.0.0 起，用户 2026-11-11 拍板「连内部名称一起换」）
- **唯一来源 `src/Updating/ManagerIdentity.cs`**（插件、更新助手、进度窗共用）。GUID `com.aiae.ingamemodmanager`、DLL `InGameModManager.dll`、包名 `InGameModManager`（键 `AiAeT-InGameModManager`）、cfg `com.aiae.ingamemodmanager.cfg`、个人数据 `config/InGameModManager/`、运行目录 `BepInEx/IGMM/`、作者资料夹 `IGMMPack`、环境文件 `IGMMEnvironment.json`、助手 `IGMM.UpdateWorker.exe` / `IGMM.Desktop.exe`、恢复入口 `IGMM-Recovery.*`、界面词条前缀 `IGMM_`、快捷键描述前缀 `IGMM.HotkeyDescription.`。
- **写一律新名，认（读取、判断）新旧都要认**：旧整合包、旧作者资料（`MCEPack`）、旧语言文件（`MCE_` 键）、旧完整环境（`MCEEnvironment.json`）、旧宿主 GUID 都还在用户手里。新代码判断「是不是管理器 / 管理目录」一律调 `ManagerIdentity.IsManager*` / `IsPackFolder` / `IsRuntimeDirectory`，**不要**再写字面量。
- **简化版 MCE 用旧 GUID 与 `config/ModConfigEnhance/`**，两者可同装：IGMM 进概览提醒一次（`OverviewPage.LegacyMceNotice.cs`），MCE 1.0.3 也提醒；只提示不拦截。**旧 GUID 是普通模组**（2026-11-12 用户「确保新装的 MCE 不会出问题」）：`IsManagerGuid` / `IsManagerDll` / `IsManagerPackage` 只认新身份，MCE 可停用、卸载、导入；旧 GUID 只在接替旧整合包宿主要求时认（`IsManagerSuccession`、`IsManagerDependency`）。
- **首次启动迁移 `src/ManagerSplitMigration.cs`**：cfg 复制（BindConfig 之前）、`config/ModConfigEnhance/` 复制到 `config/InGameModManager/`（旧目录原样留给 MCE；`output/packs`、`exports` 不复制）、签名身份复制、`BepInEx/MCE` 整体改名为 `BepInEx/IGMM`（助手在跑或有更新在提交时推迟）、改写 JSON 旧绝对路径。夹具 `验证/拆分/split-checks`（现场副本 27 项）。
- 旧整合包固定的宿主 `com.aiae.modconfigenhance >= 2.x` 由新管理器接替：`PackTransactions.CheckRequirements` 与 `PackImports.RemoveHostCopies` 对管理器 GUID 不比版本（版本号从 1.0.0 重新起算）。
- C# 命名空间 `ModConfigEnhance.*` 与三个 csproj 文件名**有意保留**（纯内部、改动面数百文件），不对外显示。GitHub 仓库 `https://github.com/AiAe-156/Peak-InGameModManager`（公开，`main`，2026-11-11 建）；旧仓库 `Peak-ModConfigEnhance` 归简化版 MCE。推送需 `git -c credential.helper=manager push`（本机未配置 credential.helper，登录在 Git Credential Manager 里），推送前跑 `验证/拆分/secret_scan.py`。
- 版本 1.0.0 起重新计；开发期 CHANGELOG / README 版本说明 / 内置更新日志归档在 `备份/*_开发期_*_20261111.md`。

先读上级 `../AGENTS.md`。保留现有用户改动，不用 git reset 回退整工程。版本流水在 `CHANGELOG.md`；各专项验证记录在 `验证/<专项>/`，部署真源在对应 `deployment_*/result.json`。

## 用户硬规则
- 不做过度防御：不用无关状态拦操作，校验一律降级为提示；真正必要的保护只有备份、回滚、保护名单（BepInEx/doorstop/游戏本体）和路径安全。
- 等退出：新模组安装不等游戏退出；更新只在要替换已加载的 DLL 时等退出；启停等退出。判定看"是否覆盖已加载文件"，不看"游戏是否在跑"。
- 更新助手只在有任务时运行，必须有前台窗口，不许后台静默驻留或卡死。
- 更新不属于配置保存：点更新即入队，不触发预设选择或保存。
- 列表与加载交互（2026-10-31）：不加按钮淡入；滚动/焦点不改加载顺序；依赖卡最多两级；性能诊断归 FrameDoctor，MCE 内不再加采样或监控钩子。
- 巡检与刷新预算（2026-11-04，2.2.3）：`OverviewMaintenance` 的周期性巡检必须走 `RowBuildBudget` 的每帧预算与固定游标，一轮未跑完不得开启下一轮，轮内已不需要刷新的条目不计预算，真实改动走 `Mark` 优先队列。待应用状态一类的重型投影一律由 `InvalidatePendingView()` 事件失效（兜底窗口只作保险），不得改回短周期定时重算。验证夹具在 `验证/每帧刷新摊帧/`（`Checks.csproj` 纯逻辑 + `source_checks.py` 源码断言）。

## 界面文字（2.3.8 起）
- 所有可见文字必须三语：`Loc.T("English", "简体", "繁體")` / `Loc.F(...)`。**不要**再写两参数 `T(英, 中)`（繁中会显示简体），也不要把中文直接传给控件。英文是词条键，也是翻译导出的原文。
- 新增文字后跑 `python -I 验证/发布前审查/collect_terms.py` 重新生成 `src/Loc.Release23Terms.cs`（不要手改）。回归里的 `translation-catalogue` / `translation-hardcoded` 两步会拦下漏登记与硬编码中文；确实只进日志或用作匹配键的中文，加到 `hardcoded_chinese.py` 的 `LOG_ONLY`/`ALLOWED` 并写原因。
- 后台线程（`Task.Run`、更新队列读取）不能调 `Loc`：在后台存简中短语，显示时在主线程翻译（参考 `StorageText.Display`、`DiagnosticUiLanguage.Text`）。
- 待做：更新助手生成的整合包预览报告文字仍是中文，需在 worker 侧按 `UpdateMessageCatalog` 的方式编码。

## 内置使用说明（2.3.10 起）
- 正文在 `Help/<en-us|zh-cn|zh-tw>/*.md`（嵌入 DLL），图片在同级 `media/`（随发行包分发）。渲染器**不支持 Markdown 表格**，用列表代替；图片要单独成段。
- 改功能时同步改三语说明；按钮名以代码里的三语标签为准（`验证/2.3.10帮助重做/labels.py` 可查）。图例图用 `make_figures.py` 重画，颜色取自代码。
- 回归里的 `help-docs` 会检查链接、锚点、图片和三语锚点一致。

## 个人数据目录（2.5.2 起，用户 2026-11-11 拍板）
- `BepInEx/config/ModConfigEnhance/` 只有四类：`language/`、`profiles/`、`output/`（`translation/`、`packs/`、`logs/`）、`data/`（`runtime/`、`backups/`、`help/`、`legacy/`），外加 `readme.md`。**不得再在顶层新建文件或文件夹。**
- 路径唯一来源 `src/Updating/MceDirectories.cs`（插件与更新助手共用同一份源文件）。新代码要用这些目录，一律从这里或 `Loc.RuntimeDirectory` / `Loc.BackupDirectory` / `Loc.PackOutputDirectory` / `Loc.ExportDirectory` 取，不要再写 `Path.Combine(root, "runtime")` 之类。整合包工作区（`BepInEx/MCE/packs/<id>/workspace/`）是另一套结构（`profiles/` 与 `runtime/` 同级），不要套这里的布局。
- 旧布局由 `MceDirectoryMigration`（`Plugin.Awake` 里 `WaitForPendingUpdateWrite` 之后）搬家并改写 JSON 旧路径。**2.5.4 起没有「搬完」标记**：每次启动只要旧目录还在就并入；`MceDirectories` 对每个目录单独回退（旧在新不在用旧）。以后再挪目录就往 `MceDirectories.Moves` 加一行，同时跑 `验证/2.5.2目录重排/layout-checks`。
- 搬家不得因单个文件失败而中断、不得覆盖任何一份文件（同名按修改时间，另一份 `.old-layout`）；新路径 >259 的文件留在原处（游戏的 Mono 不支持长路径）；备份放 `BepInEx/MCE/layout-backups/`（`MCE/backups/` 会被助手 14 天清理）。
- 改了目录要同步：三语 `Help/*/translation.md#folders`、`export.md`、`authoring.md`、`faq.md`，`LanguageReadme`，`Loc` 里提到路径的提示词条，README「个人文件位置」，介绍站首页「文件放在哪」，`文档/MCE文件脉络.html`。

## 配色主题（2.4.0 起，用户 2026-11-10 拍板）
- **唯一来源 `ModConfigLook`**：两套主题蓝 / 紫，配置 `1. Layout / Colour theme`（Blue / Purple / PurpleAtNight）。新界面颜色一律从这里取：设置页用 `Box`/`Cream`/`Prominent`/`TextDefault`/`TextSelected`/`SelectedFill`/`Accent`；概览骨架色（面板、行底、滚动条、分隔线、中性按钮）用 `ModConfigLook.Surface(new Color(...))`。内容色（状态角标、分组色、警告橙、提交绿、危险红）不包。
- **不再读 TimeTheme 的配色**：`TimeThemeBridge` 只剩 `IsDark`，只给 PurpleAtNight 用。不得再调它的配色表、`RefreshGraphicLists` 或按它的表换色。
- 设置页换主题走 `ModConfigLook.ApplyMain/ApplyOptions`（按登记颜色互换，0.004 容差）；不在登记表里的颜色用 `ModConfigSidebarWidgets.Tint(graphic, 蓝主题色, 紫主题色)`。概览不做即时换色，按 `ModConfigLook.Epoch` 在下次打开时重建。
- **按钮功能色系（2.4.2，用户 2026-11-10 要求恢复）**：`UiActionStyle.ColorFor` 每种角色在蓝 / 紫两套主题下各一个**固定色**，不得再让按钮底色过 `ModConfigLook.Surface`/`Tone`（2.4.0 这样做后三种角色夜里同色）。角色：中性 / 编辑 / 提交 / 危险 / 跳转（Overview）/ 使用说明（Guide）。新按钮按功能选角色，不要新开一套颜色。
- 置顶竖线固定青柠黄绿 `ModConfigLook.PinAccent`（#C8F05A，2 宽）；分组色条形状只走 `GroupRailShape`（每级变细变短、最短 1/2、底端对齐）。

## 包版本以说明文件为准，同包多份取最高（2.4.4 起）
- 同一个包键（Team-Name）在 core/patchers/plugins 或包目录内有多份 manifest.json 时，一律走 `src/Updating/ManifestCopies`（插件与助手共用）：**取最高版本**，其余是旧说明文件残留，只提示不参与比较。新增任何「读包版本」的地方都必须经过它，不要再直接读最近的一份 manifest。
- 助手装包时 `InstallPlanner.ReplaceStaleManifests` 会把同包低版本说明文件备份并换成新版；对账兜底 `ManifestCopies.Contradicts`：同包已有完成记录装到最新版、扫描仍低 → 不提示更新，提示「版本信息不一致」。三处都改到才算完整（扫描、助手、对账），夹具 InstallerChecks S39/M1。

## 卸载保护与名称列角标（2.2.8 起）
- **「MCE 必需依赖」只能来自固定清单**，即 `ComponentDependencyGraph.MceRequiredIdentities`（`com.github.PEAKModding.PEAKLib.ModConfig` / `.Core` / `.UI`，约一只手数得过来，且基本不会变）。**不得**改回用依赖图闭包判定保护：收集器曾为解析不到的引用伪造 `possible provider` 硬边，把 MCE 连向所有读不出来的程序集，导致无关模组被禁卸载。未解析的引用只记 `missing:`。改动这块前先看 `验证/2.2.8保护清单与角标统一/graph/`（它显式造出那条伪造边并断言不受保护）。
- **唯一的保护白名单（用户 2026-11-10 拍板，2.3.9）**：MCE 自身＋`MceRequiredIdentities` 三个库，统一走 `ComponentDependencyGraph.IsProtected(guid)`。白名单内**不能启停、不能卸载**（硬拦截，是「校验降级为提示」的唯一例外）；白名单外**所有模组和基础组件都允许启停与卸载**。
- **提示的分寸**：启停不弹确认（勾选即进草稿）；卸载一律先确认一次；基础组件卸载多一段「共用运行库，其他模组可能无法工作」的提醒。其余校验只做提示。不要再按子串（`peaklib.core` 之类）判定保护，也不要另立第二份名单。
- **名称列的角标几何只有 `StatusChip` 一个来源**：8×8 圆角方形、右对齐同一车道、间距 10、从宿主右上角起算。槽位**固定**（0 未读、1 更新、2 待保存、3 待重启、4 嵌套、5 资源包/散放），同一种角标在每一行都在同一个位置——**不要改成从右紧排**（2.3.6 试过，用户 2026-11-09 否决，2.3.7 撤回）；可悬停的角标传 `raycast: true`（整个 16×16 盒子作热区）。每种角标颜色互不相同（未读蓝、更新绿、待保存橙、待重启紫、嵌套琥珀、资源包青、散放粉），悬停文字先说颜色含义。新增或改动该区域的角标必须走 `StatusChip.Create` / `CreateAt`，标题预留用 `StatusChip.Reserve(n)` 按实际角标数算，不要新写一套形状或坐标。
- **更新队列指纹首次只记基准**（2.3.7）：`OverviewPage.PollUpdates` 只在 `OverviewUpdateQueue.HasRead` 之后比较，`_updateInventoryKey` 为 null 时只记录不 Reload，进页重扫列表后重置。改回「空串当基准」会让每次首次进入都整页重建第二遍。
- **分组折叠走 `RenderFold`**（2.3.6）：只复用已建且 `RowUpdateStamp` 未变的模组行，宽度/语言/作者模式/来源标签变化即全量重建。行内回调不得捕获构建时下标，用 `VisibleIndex(mod)`。
- 版本提示按行拼装（`UpdateVersionHint`），追加检查消息前先判断它是不是与上一行重复的那句（有更新时的 `CheckMessage` 正好是「有新版本」）。

## 嵌套分组与目录整理（2.3.0 起，用户 2026-11-09 拍板，细节见 `验证/2.3.0嵌套分组与整理重做/决定.md`）
- **分组是树**：`OverviewGroup.parentId`，最多 3 层，`order` 只在同级内有效。任何列出分组的地方都必须走 `GroupTree`（树序 `Position`/`Flat`、路径 `Path`、子树计数 `SubtreeCount`、折叠 `HiddenByCollapse`/`HeaderHidden`），**不得**再按 `groups.OrderBy(g => g.order)` 平铺（源码断言会查）。挂载/删除/同级移动用 `GroupTree.Attach/Remove/MoveAmongSiblings`；删除是「子分组与成员上移一层」。新组颜色走 `GroupPalette.ForNewGroup`。
- **导出版本**：有嵌套时 `profile.json` 与 `pack-information.json` 写 schemaVersion 2（`GroupTree.SchemaFor`），无嵌套写 1；读取接受 1/2。配置快照与索引保持只认 1。
- **目录整理只移动不改名**：新规划器 `src/Updating/GroupFolderPlanner.cs`（纯逻辑，夹具 `验证/2.3.0嵌套分组与整理重做/planner/`），worker 侧 `Updater/GroupLayoutPlanner.cs`；请求带 `Rows` 即走新规划器。模组文件夹名不得改（只有重名时后到者加「 版本号」）。旧 `LayoutPlanner`/`InstallationLayoutPlanner` 只为旧请求保留。
- **嵌套模组**：有自己 manifest 的模组文件夹在另一个模组文件夹里（`NestedMods`，打包结构不算）。启停走 `OverviewEnablementDraft.SetWithNested`：外层停连带内层并记下，外层开只恢复记下的；外层关时内层不可开。
- **首次智能分组**：只在方案里没有任何分组时进入概览弹一次（`OverviewPage.SmartGrouping.cs`），只读文件夹，确认即保存；标记文件 `smart-grouping-offered`。
- **部署路径**：用户实际执行整理后，MCE 自己的目录会从 `0核心&基础模组/ModManager_InGame_…` 移到 `0基础模组/…`，届时必须同步改 `ModConfigEnhance.csproj` 的 `DeployDir`，否则会装出第二份。

## 目录整理（2.2.13 起）
- **管理器自身参与整理**（用户 2026-11-08 明确）：它在 plugins 里、移动不改 DLL、助手跑的是 `MCE/tools` 缓存副本，没有理由排除。只做「额外谨慎」：标 `ManagerMovedLast`，在 `plan.Moves` 里排最后，预览单独说明。不得改回 `ProtectedManagerExcluded` 跳过。
- **只拦会伤到文件的情况**：链接/目录联接、越界、路径超长、目标被占、目录内有属于其他包的文件、BepInEx core/patchers。同包多份副本（`.dll`+`.dll.old`）、同插件在别的目录也有一份、内嵌其他分组的依赖库，一律进 `Notes` 只提示、照常移动。
- **分组由包的最浅层插件决定**（`InstallationLayoutPlugin.Depth`）；分类目录名与分组名不一致时由 `LayoutPlan.InferredContainer` 按包的分组过半数推断。
- 移动文件的旧路径一律走 `LayoutPaths.OldPath`（保留包内相对路径），不要再用 `旧根 + Path.GetFileName`。
- 预览界面是 `LayoutReviewModel`（纯逻辑，夹具 `验证/2.2.13整理放宽与预览重做/review-model/`）+ `LayoutReviewView`；新增原因码必须在 `LayoutReviewView.Reason` 补三语说明（源码断言会检查）。`InstallationLayoutChecks` 不在回归集里，改 planner 后要单独跑。

## 管理器档案与路径（2.2.7 起）
- **游戏根 ≠ BepInEx 的父目录。** r2modman/Thunderstore 的档案布局下 BepInEx 在档案目录里（`%APPDATA%\...\profiles\<档案>\BepInEx`），而游戏根是 `Paths.GameRootPath`。任何需要游戏根的地方（`PEAK_Data\Managed`、`winhttp.dll`/`doorstop_config.ini`/`Assembly-CSharp.dll`、导出校验）都必须用真实游戏根：游戏侧传 `--game-root`，worker 用 `Files.GameRootFor(root)`。
- **包含边界保持 `--root` 的父目录。** `RejectLinks` 一类的越界判定不得换成游戏根——档案就在游戏根之外，换过去会把整个安装判成越界。改动这些点前先看 `验证/2.2.7管理器档案与结构化预览/source_checks.py` 里固定边界的断言。
- **不再用 `mods.yml` 拒绝操作。** 管理器档案里更新、卸载、导入、整合包切换、恢复都要能用；识别档案根走 `Files.ManagerProfileRoot`（只查 BepInEx 与它的父目录）。
- **`mods.yml` 的写入归插件。** worker 没有 YAML 库，也不该替管理器改写它的文件：它只在提交/回滚成功时记账到 `MCE/manager-sync.json`（`ManagerSyncContracts`）。插件启动时用 YamlDotNet **只做形状校验**，实际改写是**行级**的（只动目标条目的 `versionNumber`，或整段删除条目），其余字节保持管理器写下的原样；改写前备份到 `MCE/manager-sync-backups/`（留 20 份）；r2modman/Thunderstore 运行时推迟到下次启动。**不要**改回用 `yaml.Save` 整档回写（实测会把 68 条全部重排）。
- 不要顺手把 `ModToggleService` 改成自动进入档案模式：档案布局下 `Paths.PluginPath` 本就是档案的 `plugins`，启停本来就能工作；档案模式是为「游戏根一份 + 另有一份档案」设计的。

## 更新系统（2.2.0 起）
- 执行核心：`Updater/Install*.cs` + `Installer.cs`；`QueueWorker.Execute` 只走它。`PackagePlan.Prepare`、`Transactions.Commit`、`ShopQueue` 的普通更新分支只为旧事务回退与旧验证工程保留，不再新增调用。
- 前台窗口：`Updater/WorkerWindow.cs`（WPF，`#if NETFRAMEWORK` 隔离），状态模型 `WorkerStatus.cs`，语言 `WorkerLanguage.cs`，游戏侧启动时传 `--language`。
- 测试：`验证/更新核心重做/InstallerChecks`，已并入 `验证/统一保存重构/run-regression.py`。所有验证夹具建在 `%TEMP%`，不进项目根。
- 卸载、本地导入、交互式商店预览、整合包切换仍走原流程；改动它们前先读 `验证/更新核心重做/2.2.0实施记录.md` 的边界说明。

## 构建、回归与部署
- 顺序：`dotnet publish Updater/MCE.UpdateWorker.csproj -c Release -o Updater/bin/framework-bundle -p:EnableDeployment=false` → `dotnet build Desktop/MCE.Desktop.csproj -c Release` → `dotnet build ModConfigEnhance.csproj -c Release`（带 `-p:EnableDeployment=false` 只编译）。
- 回归：`python 验证/统一保存重构/run-regression.py`。
- 只想生成发行清单而不部署：`dotnet build ModConfigEnhance.csproj -c Release -t:CollectReleaseFiles -p:EnableDeployment=false`（写 `bin/Release/netstandard2.1/release-files.txt`；`Deploy` 目标带条件，`-p:EnableDeployment=false` 时不会复制）。自建部署脚本时必须：读该清单、复查 PEAK/MCE.UpdateWorker/MCE.Desktop 进程、整目录备份、逐文件存在性与大小核对、失败回滚、写 `验证/<专项>/deployment_<时间戳>/result.json`。模板见 `验证/2.2.7管理器档案与结构化预览/deploy.py`。
- 长期授权（2026-10-31）：构建与回归通过且 PEAK、MCE.UpdateWorker、MCE.Desktop 都没在跑 → 自动完整备份并部署到 csproj 既定现装目录，不重复询问；任一在跑则停下报告，不强退。部署仍须：实际位置核对、完整原安装备份、复制前复查进程、发行清单与助手协议验证、失败回滚；不改真实 core/游戏本体，不擅自清理配置、库存、队列。
