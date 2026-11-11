# 交给 FrameDoctor：MCE 性能诊断移交摘要

用户决定（本会话）：移除所有 MCE 内置性能监控，诊断交给 FrameDoctor。此文件是交接材料；本轮没有修改/部署 FrameDoctor 项目。

## 1. 严重热点证据

用户提供会话 thr_1793442727297 截图：12秒后台分配795.6MB，约63.2MiB/s；4096条后台分配样本中4093条落 System.IO.DefaultWatcher 递归轮询。Unity Mono 每750ms递归枚举目录/读取时间/生成路径字符串。

MCE旧 OverviewPageCache.Initialize 同时递归监听 BepInEx 全树、plugins、core、patchers、config，范围重叠；MCE历史/缓存/整合包内容也被扫描。源证据由另一会话记录在 FrameDoctorSrc/验证/主菜单卡顿_20261031/后台分配定位结果.md（本会话未跨项目读取）。

### 本轮MCE源码处理

- 已移除所有FileSystemWatcher构造，无后台递归监听。
- 仅最多每2秒按需stat四个固定元数据文件：packs/index.json、runtime/applied-profile.json、overview/state.json、pack-information.json。无目录枚举。
- 进入页面最多每10秒触发现有库存扫描，快速设置返回保留缓存；显式写入主动失效；安全操作实时核验保留。
- CacheChecks：1000个无关历史目录，100000次热Environment读取0字节分配，7项通过。尚未部署，不将.NET隔离测量当Unity Mono结果。

## 2. 已从MCE移除的性能诊断

原件移到MCE/备份/20261031_性能监控移除原件/，不再编译：

- UiPerfProbe：UI逐帧计量；Canvas.preWillRenderCanvases / willRenderCanvases事件计时；5秒汇总；GC0；Animator/Text/Button计数；3秒延后Inspect。
- UiHoverProbe：独立Harmony实例“.hover-timing”，对鼠标静止/移动及settings/overview四组采样，5秒汇总。
- HoverPerfWindow：calls/ticks/max/allocatedBytes/frame均值与最大值聚合。
- OverviewFontReport（含OverviewTiming）：字体资产枚举/强制mesh检查、首开与重入分段计时。
- Plugin不再Bind上述组件；所有生产调用点与UiPerfSession/MSBuild编译开关已移除。
- 更新队列自动observe的prepare/publish/launch耗时采集及调试日志也已移除；网络超时和逐帧工作预算保留为业务控制。

保留正常日志查看/导出 RuntimeLogCapture、MceLogApi；它们是用户功能，不做性能采样。保留RowBuildBudget等加载限时/超时控制，属于功能调度而非监控。

## 3. FrameDoctor建议覆盖点

### 通用CPU/分配

- 主线程帧耗时、GC暂停/分代计数、进程所有线程分配/调用栈采样；不能只盯主线程。
- 单独归因System.IO.DefaultWatcher/DefaultWatcher.DoFiles/目录枚举/FileInfo时间/路径字符串。
- 宏观捕获不把diagnostic本身计量成本归因MCE业务；给出采样开销与覆盖线程。

### UI原生热点（原探针目标）

1. UnityEngine.EventSystems.EventSystem.RaycastAll
2. BaseInputModule.HandlePointerExitAndEnter
3. UnityEngine.UI.Selectable.DoStateTransition
4. CanvasUpdateRegistry.PerformUpdate
5. NicksButtonSFX.OnPointerEnter / OnSelect

原计量是含子调用时间，**不相加**；PerformUpdate不含原生Canvas.BuildBatch。指针位移仅用于区分静止/移动，不修改鼠标交互、按钮动画或音效。

### MCE业务热点（由外部探针观察）

- OverviewEntryAction.Open、OverviewPage.BindRepository/OnPageEnter/PrepareCachedPage
- OverviewListView.Render、BuildVirtualRow、加载单行/控件分层（具体私有方法名以当前程序集为准）
- OverviewDependencyHints内容构造、LayoutSteps、TMP GetPreferredValues、HoverDwell/DeferredHint缓存命中
- SourceUi.DependencyContext、PackLibrary.Read/Active、元数据读取
- OverviewConfigService.BuildCatalog、OverviewRepository.GetPendingState、ProfilePendingState.Compare
- 新增作者基线/草稿签名JSON串行化、恢复草稿落盘的UI热路径；检查是否逐帧重复序列化

## 4. 同条件复测

1. 主菜单不进设置、不动鼠标，连续采样12秒，与795.6MB证据窗口对齐。
2. 原生设置页静止/移动各一轮，概览静止/移动各一轮。
3. 首次打开与连续三次返回重入分开记录；整表加载中/加载完成分开。
4. 保留所有原生动画音效；不靠关闭交互掩盖问题。
5. 检查线程分配栈中DefaultWatcher占比、12秒累计分配、GC暂停和主线程峰值是否下降。
6. 需要实际部署的MCE版本/构建先由本会话交付记录确认；当前包含未完成统一保存重构，未部署。
