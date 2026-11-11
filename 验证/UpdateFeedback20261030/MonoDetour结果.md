# MonoDetour 来源核验修复（仅源码，未部署）

## 原因与修复

- `RepositoryBranches` 将一个仓库发布多个不同组件统一视为 fork 歧义；MonoDetour core 和 BepInEx5 patcher 都有明确清单 namespace/name，却被一票否决。
- 基础组件转概览检查行时没有携带 manifest 首页及真实身份来源。现在保留原始 manifest 对象、主页、真实声明作者，按清单证据参与校验；文件内容关联的无清单副本不制造显式证据。
- `ModSources` 新增不序列化的弱引用清单证据表。只有扫描器读取的 namespace/author + name 能登记；目录推断、来源缓存、发布记录、手动填充 SourceNamespace/SourceName 均不能冒充。清单 FullName、标准身份目录、作者、包名和仓库矛盾仍拒绝。
- 明确清单身份与官方唯一活跃条目一致时，允许同仓库多组件；无独立清单证据时继续保留原 fork/仓库猜测歧义。
- 缺 PEAK 目录条目时，仅基础组件检查行中的 `MonoDetour-MonoDetour`、`MonoDetour-MonoDetour_BepInEx_5` 可以精确 API 回退，且须可信清单和固定官方仓库一致。API 返回须精确身份、非弃用、活跃最新有效版本、匹配仓库和 active/reviewed PEAK listing；普通 mod 不享有回退。目录已有但失活／冲突的条目不走此缺项通道。
- 回退仍走同一批次 package 分组、并发控制和计数，不预先宣称来源已确认；失败保留真实错误，404 不变成成功或旧版本绿点。全局许可关保持零请求。

## 真实输入及验证

真实游戏数据只读：将 core 与 patchers 下两份 MonoDetour manifest 原样复制进 `MonoDetourChecks/fixtures/`；两者本地版本均为 **0.7.16**。从本机官方商店缓存只提取这两个公开包条目至 `official-mono.json`，不输出完整大缓存。两项官方缓存最新版本均为 **0.7.1701**。

1. `dotnet run --project 验证/UpdateFeedback20261030/MonoDetourChecks/MonoDetourChecks.csproj -c Release -p:EnableDeployment=false`
   - **25 项通过**，日志 `monodetour-checks.log`。
   - 两份清单各自来源确认；两个 `HasUpdate=true`，最新 0.7.1701，K=2，两个包各检查一次，无额外 HTTP。
   - 无独立清单的 ns/name 猜测仍拒绝；伪造目录、FullName、作者、仓库及重复官方身份冲突拒绝。
   - 缺目录基础组件仅两次精确请求，K=2；身份不符、缺社区、失活／未审查 listing、失活／缺活跃字段版本、弃用、无效版本、仓库不符均失败。
   - HTTP 404 缓存保留真实状态，失败不显示旧绿点；普通 mod 不回退；全局关零目录/API 请求。
   - HTTP 使用受控沙盒响应，不宣称完成真实在线端点验收。
2. `dotnet run --project 验证/SourceUpdateChecks/SourceUpdateChecks.csproj -c Release -p:EnableDeployment=false`
   - 原 **77 项全部通过**，日志 `monodetour-source77.log`，保留 fork、授权、分组异步、中断、显式 Local 等回归。
   - 仅适配组件测试桩，使桩显式提供模拟扫描器的清单证据；不改原 77 条断言。
3. `dotnet build ModConfigEnhance.csproj -c Release --no-restore -p:EnableDeployment=false`
   - 最终 **0 警告、0 错误**，日志 `monodetour-build.log`。最初构建遇到其他代理正在编辑的队列/UI语法错误，重试后通过；未修改那些文件。
4. 额外旧审查 `SourceFinalReview` 为 **40/48、8 条失败**，不是全绿；用改前备份源码隔离重跑结果完全相同（`monodetour-finalreview.log`、`monodetour-review-baseline.log`）。该历史审查包含与现行显式 Local 策略相反的 FINDING 断言，以及旧派生映射自动重匹配问题。本次没有扩大范围修这些旧审查项，不把它们报作新增回归或已修复。

## 改动与备份

生产文件：
- `src/Overview/StoreIdentityIndex.cs`
- `src/Overview/ModSources.cs`
- `src/Overview/InfrastructurePackages.cs`
- `src/Overview/ThunderstoreUpdateService.cs`
- `src/Overview/OverviewCatalogue.cs`

测试适配：`验证/SourceUpdateChecks/Stubs.cs`、`验证/SourceFinalReview/Stubs.cs`。
新增专项工程、公开精简输入、日志及本报告位于 `验证/UpdateFeedback20261030/`。
改前源码备份：`备份/MonoDetour来源_20261030/src/Overview/`（同时备份但未改 `ModSources.Discovery.cs`）。

## 边界与未完成

未 publish、打包、部署、运行游戏或修改真实配置／库存／队列／core／manifest；没有触及其他代理的队列、助手协议、进度 UI 文件，也未改总 README/CHANGELOG。保留 2.0.0 版本。

这里验证的是生产来源解析和更新状态／K，未进行 Unity 实机绿点渲染、实际 core 安装更新、完整真实在线 API 请求或真实游戏目录全量扫描。主代理后续整合后仍需执行最终完整部署前验收；本次不部署。
