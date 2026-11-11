# 独立审查性能问题修复结果（未部署）

## 范围与备份

只修性能独占文件；没有改来源线、README、CHANGELOG、真实配置/库存/队列，没有启停游戏或部署。
修改前快照：`备份/20261030_MCE性能审查修复_194506/`，含三个生产文件和原有源码接线脚本。保留所有原有验证。

## 修复

1. `VirtualRow` 持有稳定 `OverviewMod` 引用，不再 `_mods.Find` 或为候选拼 `mod:`。每行按 `_rowDataRevision` 缓存 stamp，数据发布及 Render 边界才递增 revision；滚动同 revision 直接返回缓存字符串。组件结果在发布边界建字典，分组为空 stamp，不再逐帧扫描。更新失效覆盖活动、停泊和未构建行，停泊行醒来前比较新内容；内容相同时保留完整控件树。拖动钉住也改用模型引用，避免循环拼 key。
2. `FlushInfrastructureRedraw` 删除永久阻塞的 selected 守卫；保留编辑、拖动、滚动及构建守卫，继续由 Render 捕获/恢复行焦点。
3. Render finally 清 `_building` 后立即 `RefreshVirtualRows()`，再恢复焦点，避免等下一帧才补行。
4. Bind catch 在释放失败行前设置 `_virtualTop=float.NaN`，同滚动坐标下一 tick 可重试。
5. 删除 `buildingRow` 局部变量及永不成立的回滚分支。

## 实际验证

日志均在 `PerformanceRepairChecks/`。

| 验证 | 结果 | 日志 |
|---|---|---|
| `python -X utf8 PerformanceRepairChecks/run.py` | **16 passed** | `results.log` |
| `python -X utf8 PerformanceChecks/source_contracts.py` | **35 passed**（保留原 25 项，拖动钉住契约适配引用语义，新增 10 项） | `source-contracts.log` |
| `dotnet run --project PerformanceChecks/PerformanceChecks.csproj -c Release` | **2953 passed** | `performance-2953.log` |
| `dotnet run --project PerformanceFinalReview/PerformanceFinalReview.csproj -c Release` | **2056 passed** | `final-review-2056.log` |
| `dotnet build ModConfigEnhance.csproj -c Release -p:EnableDeployment=false` | **0 警告、0 错误，未部署** | `release-build.log` |

`run.py` 从当前生产源直接抽取 `RefreshRowData`、`RowUpdateStamp`、`RefreshVirtualRows`、`FlushInfrastructureRedraw` 的方法体，生成 `ProductionMethods.g.cs` 后编译；没有复制另写算法冒充生产验证。Unity 控件/场景是最小桩，因此不能替代实机交互测试。

直接生产方法测量：300 模组、600 帧 × 16 行 = 9600 次同 revision stamp 读取，**0 字节分配**；分组/组件缓存读取同为零分配。数据发生变化时允许生成新 stamp，不声称更新发布或整个 Unity 滚动路径零分配。

新增运行覆盖：隐藏行数据 revision 失效、组件结果失效、停泊后唤醒重建陈旧控件、未变唤醒保留身份/监听、可见行更新、revision 变而内容不变保留控件、Bind 故障释放并以同坐标重试、选中目标不再阻止组件发布、编辑期延迟及结束后刷新。

原 2056 项中的旧 `_mods.Find` 分配量对照仍保留（52,739.1 KiB / 600 帧），只是历史问题模型；修复生产逻辑的零分配证据以新增直接方法测试为准。

## 改动文件清单

生产（仅三处）：
- `src/Overview/OverviewListView.VirtualRows.cs`
- `src/Overview/OverviewListView.Infrastructure.cs`
- `src/Overview/OverviewListView.cs`

验证：
- `验证/主菜单与概览优化/PerformanceChecks/source_contracts.py`
- `验证/主菜单与概览优化/PerformanceRepairChecks/PerformanceRepairChecks.csproj`
- `验证/主菜单与概览优化/PerformanceRepairChecks/Program.cs`
- `验证/主菜单与概览优化/PerformanceRepairChecks/run.py`
- `验证/主菜单与概览优化/PerformanceRepairChecks/ProductionMethods.g.cs`（每次运行重新从生产源生成）
- 上表五个日志、本报告；另有工程内 bin/obj 常规构建产物及上述备份。

## 交接边界

本轮仅构建主 DLL，没有重新发布助手/桌面程序，没有生成部署包或改动现装 DLL；主代理应完成完整构建/交付。

Render 的立即补行及焦点恢复顺序由源码接线检查验证；焦点是否在实际 Unity EventSystem 中正确回到原行/原列、普通点击后后台组件能及时显示、手柄导航、真实帧时间仍需实机验收，不宣称离线达成性能预算。
