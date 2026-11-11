# U5 概览分组进度集成结果

## 范围与安全边界
- 仅集成 OverviewListView.RunChecks 进度，新增独立 partial 与离线纯函数验证；未修改 Render、VirtualRows、Updater、来源链或总文档。
- 改前备份：`备份/U5分组进度_20261030_190759/src/Overview/OverviewListView.cs`。
- 未部署，未写真实配置/来源/库存/队列，未启停游戏。
- 同期性能代理在同一 OverviewListView.cs 中修改了分组标题虚拟化和拖动方法；这些不是本任务改动，未覆盖或回退。

## 实现
1. RunChecks 首个 await/Task.Run 前，在主线程一次性捕获分组 ID、名称及顺序。基础组固定使用 `__infrastructure` 与现有 `InfrastructureOverviewModel.GroupTitle`，排在首位；普通组按当前 `State.groups.order` 稳定排序，同序保持原列表顺序。
2. 缺失定义的完整目录 groupId 在末尾补入，并沿用现有分组标题的“未分组”回退。重复 ID 去重；不存在于当前服务快照的空组不生成空行。
3. 每个后台回调只读取一次 `service.LastProgress`，总行 Summary 和所有 GroupProgress 均绑定该快照。按捕获顺序生成整段字符串，交现有 `_checkProgress.Publish`，保留原 150ms 合并机制。
4. 输入仍为完整 `_mods.ToArray()`，不使用 `_visible`、搜索、标签过滤、折叠状态或控件池计数；模组与基础组件计数均沿用服务快照，不在 UI 重算。
5. 正常完成走纯函数 completed 分支，仅返回 `LastReport.Summary`，不遍历分组，不增加可更新名单；保留既有整合包错误提示。

## 窄验证
在工程根目录执行（均禁部署）：

```text
dotnet run --project 验证/主菜单与概览优化/GroupProgressChecks -c Release -p:EnableDeployment=false
# 12项通过，直接链接生产 partial，无 HTTP 或游戏数据访问。

dotnet build ModConfigEnhance.csproj -c Release -p:EnableDeployment=false
# 成功，0警告、0错误。
```

纯函数覆盖：基础组首位及原有标题、分组排序与名称、缺定义组回退、快照缺组跳过、快照计数原样输出、稳定同序排序、重复 ID、捕获后配置变化隔离、空分组列表、完成只总简报且不调用分组函数。
另有 RunChecks 源码接线检查 6 项：后台前捕获、完整目录输入、单次快照、整段合并发布、完成分支、无可见行计数。日志：
- `GroupProgressChecks/checks.log`
- `GroupProgressChecks/wiring.log`
- `GroupProgressChecks/build.log`

## 修改文件
生产：
- `src/Overview/OverviewListView.cs`（仅 RunChecks 进度附近）
- `src/Overview/OverviewListView.CheckProgress.cs`（新增）

验证：
- `验证/主菜单与概览优化/GroupProgressChecks/GroupProgressChecks.csproj`
- `验证/主菜单与概览优化/GroupProgressChecks/Program.cs`
- 上述三份日志及本结果文档。

## 待验
未进行 Unity 实机多语言排版、分组很多时的状态区域可读性、交互/切包并发实测；本结果不宣称完整 U1–U7 或 M1–M4 全部验收完成。新增状态行沿用现有状态控件显示行为，未扩大本任务到布局或性能池实现。
