# B 事务组稳定调用接口（2.0.0）

只改 B 分配源码，备份 `备份/IndependentEnvironment20261028_B/`；未部署/未写真实游戏配置、库存、队列、core、PEAK_Data。未新增 PackRequest 字段。

## C / E 接线

```csharp
PackTransactions.Live(PackStorage storage, PackIndex index); // 保持 legacy
PackTransactions.Live(PackStorage storage, PackIndex index, int environmentVersion); // 1 / 2
PackCompleteEnvironment.RequireComplete(PackStorage storage, PackRecord pack);
PackCompleteEnvironment.CheckDependencies(PackStorage storage, PackRecord pack); // List<string> target-only issues
PackCompleteEnvironment.ManagementProtocolCheck; // Action<PackStorage, PackRecord>，E 在启动时安装生产校验器
```

`RequireComplete` 要求显式2/protocol2、Validate(payload + revision-state)、目标内部依赖和管理协议钩子。钩子未安装**默认拒绝**；E 必须读取目标 MCE/工具协议与独立恢复入口并明确校验，不能设无条件通过。B 隔离测试才使用无条件通过钩子，不是生产默认。普通 legacy 不调用该钩子。完整模式不要求 HostRoots 恒定且不排除 HostRoots。

完整管理入口使用现有 PackRequest.Operation：
- `preview-adopt-environment` → `confirm-adopt-environment`：新完整临时快照，走退出等待及 Prepare/Commit，不替换 live，但复制完整 core/plugins/patchers/运行 config + ledger；第一次采用 installed-unmanaged ledger，无文件时明确空 ledger。后续当前完整包 ledger 缺失则拒绝。
- `preview-upgrade-environment` → `confirm-upgrade-environment`：**仅当前启用 legacy**，PackId 必须是当前包，退出后另建完整 revision，不覆盖旧 revision；旧包待更新/未保存状态需先处理。非当前旧包不能借 live。预览不提升真实 index，初始化也不提升。
- `preview-adopt` / `confirm-adopt` 保留原 legacy 快照语义（仍可运行中保存）。`SaveTemporary` 会依据 task.Proposed.EnvironmentVersion 支持完整版本配对，生产完整入口默认仍等待退出。
- `preview-switch` → `confirm-switch`：完整→完整才走完整模式；legacy→完整提示先显式升级，完整→legacy 阻断提示补全，避免继承当前 core。legacy→legacy 原语义保留。

`PackSwitchPlan` 新增 `EnvironmentVersion` 默认1、`Ledgers: List<PackLedgerChange>`（PackId / Before nullable原文本 / After版本状态ledger文本），完整事务SchemaVersion=2。旧schema1字段缺失时按legacy恢复；Before/After index可schema1/2。commit先校验退出后快照、ledger未外部变动，再文件→ledger→index；失败按日志恢复原文件、原ledger存在/不存在和原index。rollback不依赖目标payload/state仍完好（验证备份），避免目标损坏阻断原环境恢复。rollback遇到ledger第三方变化保留并明确阻断，不抹除用户修改。

当前完整runtime新revision使用当前退出时workspace ledger；已有owned证据改变/缺文件会明确拒绝，说明修复/放弃对应归属，不自动清空ledger。手动新增无owned引用文件正常收录。个人副本/delete-mod读取**原revision-state**而非workspace最新；编辑/删除明确移除改变文件的exact ledger引用，空组件条目移除并递增ledger revision。

维护引用列表包含 `revision-state/<revisionId>/`；新schema事务仅完成installed/rolled-back可清理。未引用且准备失败的新payload/state留档诊断，不删除旧对。

## B 核验和边界

独立工程 `验证/IndependentEnvironment20261028/Transactions/Transactions.csproj`；完整读过现有 PackLibraryChecks/Program.cs，未修改任何既有测试期望。

初次全Updater源码链接构建碰到并行C组PackImports.cs字符串换行语法错误，未改C文件。B测试暂以ImportIsolation.cs的**显式抛错导入适配器**排除PackImports（不测导入；误调用立即失败），其余链接生产代码。统一tester须恢复生产导入联测；主工程构建留统一tester。

运行命令：
```
dotnet build 验证/IndependentEnvironment20261028/Transactions/Transactions.csproj -p:EnableDeployment=false
dotnet run --no-build --project 验证/IndependentEnvironment20261028/Transactions/Transactions.csproj -p:EnableDeployment=false
```

检查A旧/B新core与宿主往返、旧独有移出、unsaved core+ledger往返/保存提升、真实index与ledger写锁故障回滚、prepare/文件移出/放入/ledger/index前后中断、重复rollback/rollback中断恢复、显式迁移保留旧版本、缺payload/state激活拒绝、旧schema恢复、个人副本/delete-mod与state保护。全部fixture项目内。最终数量以checks.log为准。

不足：E管理协议实际钩子/工具独立恢复入口与C完整导入发布往返待联测；未宣称整体完成。未做真实游戏启动、部署、用户环境迁移。手工修改owned文件必须明确修复归属，当前无自动接管按钮。平台依赖只读取游戏Managed前置条件，不从live core或HostPlugins补缺；平台根加载器文件不纳入payload。
