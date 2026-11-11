# 来源链最终审查修复（未部署）

日期：2026-10-30

## 边界与备份

- 已读 `MODs/AGENTS.md`、`source-final-review.md` 与当前生产逻辑。
- 改动前快照：`备份/source-final-repair_20261030_200852/`（三份生产源、SourceUpdateChecks 的 Program/Stubs/csproj）。
- 本轮仅修来源/更新检查及对应测试；未改性能文件、总 README/CHANGELOG、真实配置/库存/队列/来源文件，未启停游戏，未部署，未执行 publish。现装仍为此前 19:49 交付，本次构建并不代表现装已更新。

## 四项修复

1. **检查中修改分组**：轮次开始固定 `OverviewMod -> groupId` 快照，初始分母及 Complete 分组计数统一使用快照。await 期间编辑现有 Rule 的分组或直接换 Rule 均不影响本轮分组统计，不再索引实时 groupId。
2. **旧更新提示与中断状态**：开始时复位旧版本/绿点/说明/时间，并把 CheckMessage 替换为“正在检查更新”。取消或整体故障时，仅对未完成条目清理更新字段、写入真实取消/失败状态；已完成包的真实结果与进度/K 保留。
3. **基础组件全局许可**：组件条目仍扫描并列入本轮总分母，但单独使用轮次开始的 `GetGlobalUpdateAllowed()` 门控 Discover 与 eligible。关闭时标禁用，不产生组件更新 K。无可查询条目时不调用 Discover，从而也不触发目录加载。普通模组仍调用原 `IsUpdateAllowed(rule)`，没有擅自套全局门控。
4. **auto 与发布 Local**：Get 忽略个人 `note=auto` 选择、回落至发布记录；ExplicitLocal 也不把 auto 当显式个人 Local。明确个人 Store/Local 优先级不变；历史发布 Local 未标 explicitLocal 仍允许自动恢复。

## 执行验证

所有命令在项目根执行，退出码均为 0：

```text
dotnet run --project 验证/SourceUpdateChecks/SourceUpdateChecks.csproj -c Release -p:EnableDeployment=false
  TOTAL 77 production checks passed
  沙盒：验证/SourceUpdateChecks/runs/20261030_200949_173

dotnet run --project 验证/UpdateFlow20261029/Checks.csproj -c Release -p:EnableDeployment=false
  Update flow checks passed: 16

dotnet run --project 验证/SharedStoreCatalog20261029/SharedCatalogChecks.csproj -c Release -p:EnableDeployment=false
  25 shared catalog checks passed; isolated HTTP and filesystem only.

dotnet run --project 验证/SourceUpdateChecks/OfflineImport/OfflineImport.csproj -c Release -p:EnableDeployment=false
  TOTAL 6 production offline checks passed

dotnet build ModConfigEnhance.csproj -c Release -p:EnableDeployment=false
  成功；0 警告，0 错误。
```

总计 **124 项运行断言通过（77 + 16 + 25 + 6）**，另主 Release 构建通过。UpdateFlow 有测试桩未赋值字段警告，不影响通过；主构建无警告。

证据日志均在本目录：
- `source-final-repair-source.log`
- `source-final-repair-flow.log`
- `source-final-repair-catalog.log`
- `source-final-repair-offline.log`
- `source-final-repair-release.log`

### 来源测试增量（原 55 项未删减或修改）

与快照逐字比较：原 Program 在最终 TOTAL 输出前的正文完整保留；仅在其后追加 **22 项**断言，并为测试桩增加可控制的全局许可与异步目录边界。

- 两项：真正暂停在目录 await 中，改写 groupId / 替换 Rule 后继续，验证原分组 1/1、本轮完成 2/2、K=绿点数。
- 六项：分别预置旧 Update available 后取消/整体失败；暂停期间验证“正在检查更新”，结束验证真实状态及 0 完成/0 K。
- 六项：分别在一个包已实际完成后取消/整体失败；验证已完成版本/提示/时间保留，未完成写真实状态，Completed=K=绿点数=1。故障注入通过进度回调抛 IOException，并取消后续工作，避免后续包被同步执行完而丢失“未完成”测试前提。
- 三项：全局关闭且普通模组禁用时，跨社区组件和目录内组件都不触发目录/API；组件仅计分母、不计 eligible/package/K；普通模组单项开启时仍正常查询与计 K，组件不混入。
- 五项：auto 不遮蔽发布 explicitLocal；个人明确 Store 优先；个人 Local 优先；auto 回落经磁盘重载仍有效；历史发布 Local 无 explicitLocal 可自动恢复。

首次新增测试中，故障回调抛出后，另一个同步完成的包也会继续执行，因此“未完成条目”前提不成立；测试增加取消后续工作的确定性后，完整重跑通过。没有删减原断言或放宽生产结果标准。

## 改动文件

生产：
- `src/Overview/ThunderstoreUpdateService.cs`
- `src/Overview/ModSources.cs`
- `src/Overview/ModSources.Discovery.cs`

测试：
- `验证/SourceUpdateChecks/Program.cs`
- `验证/SourceUpdateChecks/Stubs.cs`

交付记录：本报告、上述五个日志、改动前备份；运行沙盒及项目 bin/obj 为正常验证产物。csproj 仅备份，未修改。

## 风险与后续

- 尚未游戏内验收分组编辑并发、取消按钮、绿点/提示视觉；未以真实外网验证组件目录/API 路径。当前证据为直接链接生产方法的隔离运行，不宣称已完成实机验收。
- 全局组件许可按轮次开始快照；检查中切换许可对后续轮次生效。普通模组既有许可语义保持原样。
- 本轮按委派要求止于源码与禁部署构建。主代理可接续串行集成/部署，不应把本报告视作部署成功证明。
