# 全部更新 / 历史红字 / Steam 祖先 junction（2026-10-30）

状态：**源码完成，本子任务未部署**。版本保持 2.0.0；未改总 README/CHANGELOG；未执行真实更新、未修改真实配置/库存/队列/worker-error.json、未启停游戏。源码备份：`备份/QueueFeedback_20261030/`。

## 实施

- `WorkerProtocol` 增加显式 `RejectLinks(boundary,path)`；只信任边界上方的路径别名，边界本身及边界内链接仍拒绝；路径先规范化并检查包含关系。旧单参数保留保守盘根语义。
- 工具 inventory/manifest/PrepareTools 的源、缓存使用各自显式边界；完整依赖/摘要、残留文件拒绝、不可变缓存发布等安全检查保留。调用者指定游戏根时，也检查游戏根至 Tools/Updater 的中间目录。
- `OverviewUpdateQueue` 使用选定游戏根校验 index、现装助手和缓存；`RecoveryLauncher`、其 PowerShell 模板、`PackSwitchProgressWindow`、worker `WorkerTargetProtocol` 同步边界。它们是全部调用点调查后必须的辅助修改（否则 PrepareTools 后仍被 RecoveryLauncher 上溯盘根阻断）。`PackPublisher.Environment` 的独立包工具校验默认以明确工具目录为边界，其外层内容清单已有安装边界校验。
- Enqueue/EnqueueAll 使用真实 `HasUpdate` + `LatestVersion`，不再读取作者钉住版本；包级去重优先有更新的条目。没有新版、下行版本、许可关闭、已有任务/失败任务、来源不明等均给出原因；反馈保留全部条目而非仅前三条，队列提示可点开阅读全文。加入项显示 `installed → latest`。
- `EnqueueModpackVersion` 为独立显式恢复作者整合包版本的入口，转 `EnqueueTarget` 做确认；批量更新绝不走恢复。
- 普通批量与 `Add` 均保留 Core/受保护安装根/loader 防护，组件仅提示用户走商店 ComponentActions 预览链路，不发布普通 core 写命令。
- worker-error 使用 JSON `UpdatedUtc` + 本会话启动尝试 + 后续 protocol2 queue 成功证据判断当前失败；不以错误文件 mtime 较新就显示当前红字。旧快照错误同样须有本会话启动证据。当前命令错误优先保留，不被旧启动诊断覆盖；本地发布错误保存到成功命令/启动后清除。历史错误保留在内存投影，队列提示点击可查看，绝不删真实错误文件。

## 验证（生产源码执行，非仅源码断言）

命令（全部工程内，禁部署）：

```
dotnet build ModConfigEnhance.csproj -c Release -p:EnableDeployment=false --no-restore
dotnet build Updater/MCE.UpdateWorker.csproj -c Release -p:EnableDeployment=false --no-restore
dotnet run --project 验证/UpdateFeedback20261030/QueueChecks/QueueChecks.csproj -c Release -- <工程内短路径沙盒>
```

结果：Release **0警告/0错误**；net48 worker **0警告/0错误**；**27/27** 执行检查通过。日志：`queue-release.log`、`queue-worker.log`、`queue-tests.log`。

QueueChecks 编译实际 OverviewUpdateQueue、UpdateQueuePolicy、WorkerProtocol、RecoveryLauncher 及 contracts，仅 UI/BepInEx/仓库数据接口用桩。生产 `EnqueueAll` 实际发布沙盒 JSON 命令；以独占沙盒 worker.lock 避免运行任何 worker。检查：作者钉住 installed 仍入 latest；明确无更新与回退跳过；许可、已有待办与失败队列条目；组件保护及原因；历史错误 fresh mtime 不红；本轮启动失败红；成功 protocol2 queue 后旧失败降历史；错误记录保留；当前命令错误不隐藏。

通过 `cmd /c mklink /J` 创建**真实 Windows junction**：祖先别名允许 PrepareTools、验证、复用；边界 junction、内部 junction（含不存在叶子）、缓存根 junction、越界与清单篡改均拒绝。所有 junction 均在 finally 中移除（仅移除链接目录，不删除目标）。实际执行生成的恢复 PowerShell `-Headless` 并调用 `Find-RecoveryTools` 验证祖先别名通过。首次恢复脚本测试被 Windows 执行策略/Framework长路径限制阻断，最终采用与真实启动一致的 `-ExecutionPolicy Bypass` 并用工程内短沙盒；严格检查退出码、stderr为空及成功标记，最终27项日志通过。

## 需主代理集成 / 实机待验

1. **必须修菜单语义**：`src/Overview/OverviewListView.cs:ShowVersion` 当前有 pin 时菜单仍叫“使用整合包指定版本”，回调却是 `Enqueue`。新 `Enqueue` 明确代表“更新到已检查最新版”。请把菜单拆成“更新到已检查最新版”→`Enqueue`，有 pin 时额外“恢复整合包指定版本”→`EnqueueModpackVersion`；`UpdateVersionHint` 的“加入队列时使用此版本”改为“仅显式恢复整合包版本时使用；全部更新使用已检查最新版”。本子任务未触碰该文件以免与 UI 代理冲突。
2. `OverviewPage.Updates` 全部更新现在只传普通 `_list.Mods`；若需要包括本轮检测到的 MonoDetour 等基础组件，应在主代理层把组件转成受保护条目/计数反馈或另外提示“基础组件请逐项预览”，**不得把 core 包转普通更新写入**。本轮未扩展商店组件安装权限；ComponentActions 预览流程保持原样。
3. 主代理统一登记新本地化短语/策略失败理由（UpdateQueuePolicy 目前返回英文，经现有 LocalizeMessage 投影；中文主要路径消息已经三语 Loc.T）。未修改共享 Loc 文件。
4. 本轮不更新现装。PEAK运行中禁止投放；主代理稍后应重新构建/发布完整 worker bundle、生成协议清单、完成集成回归后，待游戏和助手退出再按原完整37文件部署流程执行。当前 `Updater/bin/framework-bundle` 仍是旧已发布缓存，本轮仅 build worker，不能直接把它当新 worker 交付。
5. 实机需验：更新队列紧凑提示和全文点击、所选/全部状态反馈、当前失败到历史的颜色变化、菜单拆分后的作者版本回退确认。真实网络下载与重启生效本任务未执行。

## 本子任务改动文件

- `src/Updating/WorkerProtocol.cs`
- `src/Overview/OverviewUpdateQueue.cs`
- `src/Overview/UpdateQueueView.cs`
- `src/Overview/UpdateQueuePolicy.cs`（新增）
- `src/Overview/RecoveryLauncher.cs`
- `src/Overview/PackSwitchProgressWindow.cs`
- `Updater/WorkerTargetProtocol.cs`
- `验证/UpdateFeedback20261030/QueueChecks/{QueueChecks.csproj,Stubs.cs,Program.cs}`
- 本结果文档及三份 queue-* 验证日志；工程内测试缓存/沙盒（可清，未清理）。
