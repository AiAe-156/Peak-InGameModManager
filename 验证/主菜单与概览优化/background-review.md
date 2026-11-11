# 主菜单后台优化与 UBImGui 条件项 独立复核

- 日期：2026-10-30
- 复核人：独立复核子代理（未修改生产源码、未部署、未启停游戏、未新反编译）
- 复核对象：`最终优化方案.md` 第四节 A/B/C（三项自研后台优化）与第七节（UBImGui → LocalFix 条件项）
- 结论口径：**离线/源码级复核通过；实机与三轮性能对照未执行，不得据此宣称性能达标**。

---

## 一、复核方法与证据来源

| 项 | 内容 |
|---|---|
| 规则 | `MODs/AGENTS.md`（分区规则、部署与回滚、反编译复用判据） |
| 方案 | `ModConfigEnhance/验证/主菜单与概览优化/最终优化方案.md`（第四节、第七节、第九节门槛） |
| 生产源码 | 三个工程当前工作树（`汉化/BetterReviveTeleport`、`Better_MicIndicator`、`显示玩家距离/更好的显示玩家距离2`） |
| 备份基线 | 各工程 `备份/20261030_181443_idle_before`（实施前快照） |
| 现装 | `BepInEx/plugins/` 三个独立目录（见第三节） |
| 离线回归 | `显示玩家距离/更好的显示玩家距离2/验证/主菜单休眠/offline_regression.py`（本轮重跑，仅项目沙盒） |
| 构建 | 本轮独立重跑三次 Release，`EnableDeployment=false` / `DeployPlugin=false`，日志见 `background-review-logs/build-*.log` |
| UBImGui 证据 | `UBImGuiPerf/验证/结果.md`、`BepInEx/FrameDoctor-Logs/2026-10-30_10-25-08 / _15-44-21 / _17-00-37 / _17-06-27` 的 `framedoc_report.md` + `framedoc_events.jsonl`、`反编译/UBImGui_1.0.0_PEAK3.0.1_2026-10-29_周期卡顿/`（既有归档，未新增反编译） |

本轮未做的事：未启动/结束游戏，未写真实配置，未改 `BepInEx/plugins`、`core`、`PEAK_Data`，未新增反编译归档，未改任何生产源码。

---

## 二、逐项源码 diff 复核（生产 vs 实施前备份）

diff 均以 `备份/20261030_181443_idle_before` 为基线，忽略 BOM/CRLF 差异。

### A. BetterReviveTeleport 1.4.5

改动文件：`peaklg/reviveKey.cs`（83 行变更）、`peaklg/Plugin.cs`（版本号 1.4.4→1.4.5）、`CHANGELOG.md`、`README.md`、`manifest.json`、`PeakLateJoinButItActuallyWorks.csproj`。
`peaklg/PlayerSwitchUI.cs` 与备份**逐字节相同**（未改动）。

实际改动内容：

1. 删除 `Update()` 开头的无条件 `if (_log == null) _log = FindObjectOfType<PlayerConnectionLog>();`——这是主菜单每帧全场搜索的根因。
2. 新增 `PlayFeedback(bool joined)`：`_log` 为空且 `Time.unscaledTime >= _nextSoundProbe` 时才查找，并把下次探测推到 `+0.5f`（负结果也有限期，符合方案第三节第 4 条）。
3. 新增 `OnSceneLoaded`：`_log = null; _nextSoundProbe = 0f;`，并在 `Start` 订阅、`OnDestroy` 取消订阅 `SceneManager.sceneLoaded`。
4. 原 18 处 `if (_log != null) _log.sfxJoin/sfxLeave.Play(default);` 全部改为 `PlayFeedback(true/false)`；`_log == null` 时直接返回，文字反馈与玩法逻辑不受影响（符合方案 A「提示音对象尚未就绪时，复活／传送的原有逻辑和文字反馈照常工作」）。
5. `DeathTimerSync.Tick()` 仍在 `Update()` 第一行、仍在 `rawChars.Count == 0` 守卫之前（顺序未变，符合方案 A「保留 DeathTimerSync.Tick 原有轻量守卫和通信时序」）。

复核判定：**与方案 A 一致，改动最小、无越界**。

### B. Better_MicIndicator 1.4.1

改动文件：`MicUIManager.cs`（68 行）、`Plugin.cs`（版本号 1.4.0→1.4.1 及注释）、`CHANGELOG.md`、`README.md`（新增）、`Better_MicIndicator.csproj`。

实际改动内容：

1. 新增 `hudWait`（唯一协程句柄）、`hudGeneration`（场景代次）、`static readonly WaitForSecondsRealtime HudRetry = 0.35f`（复用等待对象，不每帧 new）。
2. 新增 `NeedsHUD`：`isActiveAndEnabled && Plugin.Instance != null && ConfigEnableIndicator.Value && !isHidden && 场景名 != Pretitle && != Title`。配置在主线程 `Update` 采样，回调不碰 UI（符合方案 B「配置回调只发布状态变化，Unity UI 操作统一回主线程」——本工程无 `SettingChanged` 回调，采样点即主线程 Update）。
3. `CancelHUDWait()`：`hudGeneration++` 后 `StopCoroutine`，句柄置空。
4. `OnDisable` / `OnDestroy` / `OnSceneLoaded` 均调用 `CancelHUDWait()`；`OnSceneLoaded` 额外清 `recorder/micView/pttSetting/micBuffer/displayDb/micRelay` 并注销监听。
5. `Update()` 改为：`!NeedsHUD` → 取消等待 + 隐藏图标后 return；`iconBaseObj == null` → 仅在 `hudWait == null` 时启动一条等待（唯一性）；否则正常同步位置与电平。
6. `WaitForHUDAndCreateIcon(int generation)`：先 `yield return null`（保证即使 HUD 立即就绪也不会留下已完成句柄），循环条件 `generation == hudGeneration && NeedsHUD`，命中后 `CreateUI` + `hudWait = null` + `yield break`；未命中 `yield return HudRetry`（**无硬超时**，符合方案 B「不设置会永久丢失图标的僵硬超时」）；退出时仅当代次相同才清句柄。
7. `CreateUI` 幂等：`iconBaseObj != null && iconBaseObj.transform.parent == hud.transform` 直接复用。

复核判定：**与方案 B 一致**。`ReadLocalLevel()` 与备份逐字节相同（dBFS／快攻慢放／PTT 门控未动）。

### C. 更好的显示玩家距离2 1.1.6

改动文件：`OverlayManager.cs`（28 行）、`DownedManager.cs`（22 行）、`Tracking.cs`（12 行）、`Plugin.cs`（版本号 1.1.5→1.1.6、`Start()` 不再 `TryInitialize`）、`CHANGELOG.md`、`README.md`、`manifest.json`、`更好的显示距离2.csproj`。

实际改动内容：

1. `OverlayManager.LateUpdate()` 开头新增空目标守卫：`_activeLabels.Count == 0` 时，若上一帧有目标则 `ClearFontSizeCache()` + `VanillaNames.Clear()` 并复位 `_hadTargets`，随后 **return**——相机探测、字体维护、标签更新全部跳过（符合方案 C「无目标时跳过显示维护和主相机探测」）。
2. 删除每帧 `_activeLabels.Keys.ToList()`，改为复用 `_labelKeys` 缓冲区（`Clear()` + `Add`）。
3. 字体重试加 `_shouldShowLabels` 门控（只在需要绘制时重试）。
4. `OnSceneLoaded` 增加 `_currentFont = null; CameraCache.Invalidate();`，`_needRescan` 初值改 `true`（保留场景补扫）。
5. `CreateCanvasSystem()` 不再调用 `InitializeFonts()`，改到 `RegisterGenericTarget()` 内按需调用（字体重试在需要绘制时执行）。
6. `Tracking.CameraCache` 新增 `Invalidate()`；`Current` 在缓存相机被禁用时**先置 `_camera = null` 再判重试窗口**，修掉「禁用相机在 0.5 秒重试间隔内仍被返回」的缺陷（这是本轮真实修掉的一个 bug，见第四节）。
7. `DownedManager.LateUpdate()`：先清理已消失角色与失效假人状态（`RemoveState` / `RemoveFakeState`），再判断 `hasPlayerTarget || _states.Count > 0 || _fakeStates.Count > 0`，无需求直接 return——相机、Canvas、标签全部不碰（符合方案 C「以真实角色、调试目标和仍需显示的死亡标记为工作需求」）。清理在请求相机之前，避免退房无主相机时旧状态无限要求探测。

复核判定：**与方案 C 一致**。死亡标记（`DeadUntil` / `WasDead` / 墓碑位置）、假人调试、兼容模式字号恢复、皇冠／语音图标、米换算代码均未改动。

---

## 三、构建与现装一致性

本轮独立重跑（日志 `background-review-logs/build-*.log`）：

| 工程 | 配置 | 结果 | 输出 |
|---|---|---|---|
| BetterReviveTeleport | Release，`EnableDeployment=false -p:DeployPlugin=false` | 43 警告 / **0 错误** | `bin/Release/BetterReviveOrTeleport.dll` |
| Better_MicIndicator | Release，同上 | **0 警告 / 0 错误** | `bin/Release/netstandard2.1/Better_MicIndicator.dll` |
| 更好的显示玩家距离2 | Release，同上 | **0 警告 / 0 错误** | `bin/Release/netstandard2.1/BetterPlayerDistance.dll` |

- 复活工程 43 条警告全部是既有可空性警告（`CS8603/CS8600/CS8618`），分布在 `reviveKey.cs` 与 `PlayerSwitchUI.cs` 的原有代码行；**新增的 `PlayFeedback` / `OnSceneLoaded` / `_nextSoundProbe` / `_log` 相关行零警告**（已逐条 grep 确认）。
- 三个 `TargetPath` 均落在 `MODs\<工程>\bin\Release\...`，未落到 `plugins`（部署门控生效）。

现装与构建产物 md5 对照（复核时点）：

| 模组 | 现装路径 | 现装版本 | 构建 md5 = 现装 md5 |
|---|---|---|---|
| BetterReviveOrTeleport | `plugins/0核心&基础模组/[机制]更好的手动复活&传送/` | 1.4.5 | ✅ `bf542c18…` |
| Better_MicIndicator | `plugins/0核心&基础模组/[功能]更好的麦克风指示器(Better_MicIndicator)/` | 1.4.1 | ✅ `35e007e9…` |
| BetterPlayerDistance | `plugins/1拓展模组/[信息]更好的显示玩家距离-1.1.5/` | 1.1.6 | ✅ `d8e3827c…` |

- 三个 csproj 的部署目录（`OutputPath` / `DeployDir` / `PluginOutDir`）解析后**均指向实际现装独立目录且目录存在**，没有新建第二份安装；距离工程仍沿用 `-1.1.5` 目录名（沿用既有安装，未另建 `-1.1.6`，符合方案「补丁升级不应因此另建重复加载目录」）。
- 部署记录：`background_deployment_20261030_192607/result.json`，`status=completed`，`processes=[]`（复制前后均无 PEAK / MCE 进程），三个模组各自 `备份/部署前_主菜单空转优化_20261030_192607/installed/` 完整旧安装备份存在。
- 文档一致性：三工程 `README.md` / `CHANGELOG.md` 与现装同名文件 `cmp` 相同；`manifest.json` 复活 1.4.5、距离 1.1.6 与现装一致。

### 发现的一处文档/元数据不一致（非功能 bug）

- **Better_MicIndicator 工程内没有 `manifest.json`**，但现装目录里有一份 `version_number: 1.1.0` 的旧 manifest。当前 DLL 是 1.4.1，manifest 仍写 1.1.0。
  - 影响：MCE 概览的「包版本」读 `manifest.json` 的 `version_number`（`OverviewCatalogue.Create`），因此概览里这个模组会显示 1.1.0，而插件自身 `PLUGIN_VERSION` 是 1.4.1。更新检查按包版本比较时也会用 1.1.0。
  - 本轮未修（不改生产、不部署）。建议后续由主代理决定：补一份 `manifest.json`（1.4.1）并纳入部署清单，或在概览侧明确「无 manifest 时回退插件版本」。
  - 注意：`结果.md` 里写的「麦克风原无 manifest，本轮不新增来源身份」是**准确的自我限制**，不是漏报；但现装残留的 1.1.0 manifest 与 DLL 版本不一致这一事实，此前结果文档未点明。

---

## 四、离线回归（仅项目沙盒）

命令：`python offline_regression.py`（脚本位于 `显示玩家距离/更好的显示玩家距离2/验证/主菜单休眠/`，本轮重跑两次，日志 `background-review-logs/offline-regression-rerun.log`）。

结果：**22 条运行时断言 + 17 条源码契约断言全部通过，exit 0**。

脚本机制复核（确认不是「复制算法自证」）：脚本用 `method()` 从**当前生产源码**按签名切出 `PlayFeedback`、`Update`、`CancelHUDWait`、`WaitForHUDAndCreateIcon`、`CameraCache`、`NeedsHUD` 等真实方法体，拼进确定性 Unity 桩后编译到 .NET 10 运行。因此断言对象是生产代码本身。

覆盖到的功能点（与方案验收项对应）：

- Title 场景 3600 次 `Update`：HUD 搜索 0 次、协程启动 0 次（方案 B 验收「主菜单稳态 HUD 搜索为 0」）。
- Pretitle 休眠；Airport 只启动 1 条等待；重试间隔 0.35s；隐藏 / 配置关闭取消；重新开启恢复；旧代次不能建 UI；HUD 就绪后释放句柄；图标复用；HUD 销毁后恢复。
- 提示音：空结果 0.5s 限频、成功缓存复用、场景失效后立即重新探测。
- 相机：缺失时负缓存有界（50 次读取只探测 1 次）、恢复、**禁用相机永不返回**、场景失效后立即探测。
- 源码契约：`DeathTimerSync.Tick` 顺序不变、声音查找只在播放帮助方法内、事件订阅/注销成对、`ReadLocalLevel` 与备份逐字节相同、场景补扫保留、字体按需初始化、复用标签键与空目标守卫、死亡/假人清理在相机请求之前、三个 csproj 排除备份与验证源码、三个部署门控。

**边界说明**：这是 .NET 10 + Unity 桩的离线验证，不是 Unity 运行时验证。它证明「逻辑分支与契约正确」，**不能**证明实机零搜索、也不能证明 ≤0.10ms/帧。

---

## 五、功能无退化核对（源码级）

| 方案要求 | 核对结果 |
|---|---|
| 复活：H／Alt+H／Shift+H、二次确认、落点校验、幂等补发、飞行保护 | 未改动（diff 仅涉提示音与场景订阅），`DoRevive`/`DoTeleport`/`ReviveAndVerify`/`PrepareLocalForWarp` 逐字节相同 |
| 复活：主客机权限、幽灵过滤、在场计时、RPC 与重连记录 | 未改动 |
| 麦克风：M 静音、Alt+M 显隐、四档阈值、dBFS 包络、PTT 门控 | `ReadLocalLevel` / `UpdateMicState` / `HandleInputs` 未改动 |
| 麦克风：图标启用状态与显隐、静音分离 | `NeedsHUD` 同时看 `ConfigEnableIndicator` 与 `isHidden`，两者仍独立（`isHidden` 只由 Alt+M 改） |
| 距离：米换算、近处让位、兼容模式、TimeTheme、皇冠／语音图标 | 未改动 |
| 距离：死亡标记时长（10s）、墓碑位置、假人调试 | `DeadUntil`/`WasDead`/`LastAlivePosition` 逻辑未改动；假人注册/清理路径保留 |
| 距离：角色生成/销毁注册、场景补扫 | `Tracking.CharacterPatches` 未改动；`_needRescan` 保留且初值 `true` |
| 配置键、GUID、数据格式 | 三个 GUID 与全部配置键未变（`peaklatejoinfr.RE` / `com.aiae.better_micindicator` / `com.AiAe.betterplayerdistance`） |

**本轮真实修掉的一个 bug**（不是仅性能优化）：`CameraCache.Current` 旧实现在缓存相机 `isActiveAndEnabled == false` 时，因 `Time.unscaledTime < _nextProbe` 分支直接返回**旧相机引用**，导致观战/过场换相机后最长 0.5 秒仍用失效相机定位名牌。新实现先置 `_camera = null` 再判窗口，禁用相机立即返回 null。离线断言 `disabled camera never returned` 覆盖此项。

---

## 六、UBImGui 条件项判定（第七节门槛）

### 6.1 现有证据（不新反编译，全部取自既有日志与既有归档）

四轮同条件会话（同一 61 个模组、同一 GUID/路径表，模组表 md5 完全一致；PEAK 3.0.1 / Unity 6000.3.15f1 / i5-14600KF / RTX 3060 Ti）：

| 会话 | 帧数 | `ImGuiBehaviour.Update` 调用 | 每调用中位 | p90 | 窗口最大 | ≥0.3ms 窗口 | HeavyCall 次数 | GC/self 中位 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 10-25-08 | 146,794 | 146,251 | **0.130 ms** | 0.133 | 0.166 | 0/102 | 2 | 0.958 |
| 15-44-21 | 102,427 | 101,919 | **0.140 ms** | 0.246 | 0.311 | 1/79 | 37 | 0.962 |
| 17-00-37 | 17,889 | 17,353 | **0.172 ms** | 0.216 | 0.216 | 0/7 | 8 | 0.961 |
| 17-06-27 | 77,627 | 76,853 | **0.131 ms** | 0.278 | 0.354 | 2/55 | 28 | 0.961 |

（每调用耗时 = 相邻两次 `MethodProfile` 的 `selfMs` 增量 ÷ `calls` 增量；这是该库在稳定段的真实 CPU 口径。）

### 6.2 尖峰归因

- 全部 75 次 UBImGui `HeavyCall`（≥30ms）都落在 `MacroSpike.class == "GC-bound"` 的帧上，`gcCollectMs / selfMs` 中位 **0.961**（区间 0.955–0.969）。即：**这些「尖峰」是 GC 暂停恰好发生在 `Update` 执行中**，self 里含暂停时间，不是该库自身的 CPU。
- 三个 ≥0.3ms 的窗口（15:46:42→15:46:57、17:08:28→17:08:43、17:10:28→17:10:43）在窗口内分别有 11 / 10 / 15 次 GC 尖峰、GC 合计 1217 / 1216 / 1270 ms——**窗口被 GC 污染**，不能作为该库 CPU 达标的证据。
- 会话级 `selfAllocKB` 在全部 3705 条 `MethodProfile` 中**均为 null**（游戏 Mono 无 per-frame 分配计数器），因此第七节条件 2（16KiB/帧 或 2MiB/秒）**无法用现有证据判定**。

### 6.3 对照第七节三条判据

| 判据 | 门槛 | 现有证据 | 判定 |
|---|---|---|---|
| 1. 无窗口时持续 CPU | 约 0.3 ms/帧 | 四轮中位 0.130 / 0.140 / 0.172 / 0.131 ms/帧，最高 p90 仅 0.278 | **未达到**（中位约为门槛的 43%–57%） |
| 2. 可消除分配 | 约 16 KiB/帧 或 2 MiB/s | 运行时无分配计数器（`selfAllocKB` 全 null）；仅有静态代码证据（`InputManagerHandler.Update` 每帧 `foreach (KeyCode value in Enum.GetValues(typeof(KeyCode)))`，`RenderInFront` 每轮 `new WaitForEndOfFrame()`） | **无法判定**（缺测量，不捏造数值） |
| 3. 可重复、定位到该库的 ≥5ms 尖峰 | 5 ms 以上且候选补丁能稳定改善 | 所有 ≥30ms 事件均为 GC 主导（gc/self ≈ 0.96），非该库 CPU；无「非 GC 的库自身尖峰」证据 | **未达到** |

补充的独立测量（本轮在 .NET 10 上对 `UnityEngine.KeyCode` 做的分配测量，仅作量级参考，**不等同游戏 Mono**）：`Enum.GetValues` 单次约 1384 B；原版非泛型 `foreach (KeyCode v in Enum.GetValues(...))` 因每个元素装箱，单次约 **9552 B**（339 个成员）。若该调用每帧执行，量级约 9.3 KiB/帧，接近但**低于** 16 KiB/帧门槛；且游戏 Mono 的装箱/数组开销与 .NET 10 不同，不能直接换算成游戏内结论。

### 6.4 结论

**未达到纳入 LocalFix 的门槛。** 按第七节「没有达到纳入标准时，本轮记录结果并保留原库」：

- 不新增 LocalFix UBImGui 分区，不改原 UBImGui / cimgui / ABI。
- `UBImGuiPerf` 工程保持为研究参考，不作为本轮安装插件（现装 `plugins` 下确认**没有** `UBImGuiPerf.dll`）。
- 条件 2 的判定需要一次带分配计数的同条件对照（或用 `GC.GetTotalMemory` 做前后差分），且必须至少三轮；本轮**未执行**，因此该条件保持「未判定」而非「已排除」。
- 若后续要推进，最小验证路径是：同一场景、同一模组集，三轮「保留 UBImGui」vs「停用 TC+UBImGui」对照，记录 `Update` 每调用耗时、GC 次数/暂停、以及可得的分配趋势。

---

## 七、实机待验清单（本轮未执行）

以下全部**未验证**，不得在交付说明中写成已完成：

1. **复活传送**：主菜单稳态提示音全场搜索为 0（需实机计数器或日志确认）；机场首次按键、切换玩家、首次提示音、H／Alt+H／Shift+H、主客机、倒地／死亡、自传静音、飞行保护、落点校验；提示音对象延迟出现时不阻断动作与文字。
2. **麦克风**：机场／关卡及慢加载 HUD；连续 Title 往返不累积等待与图标；HUD 销毁重建；`EnableIndicator` 开关、Alt+M 显隐、M 静音、四档音量、PTT 与静音弧线；任意时刻等待协程 ≤1。
3. **玩家距离**：机场角色生成、`sceneLoaded` 前后注册顺序、重连、观战换相机（含禁用相机立即失效）、远近让位、兼容模式字号恢复、昼夜切换、皇冠／语音图标、死亡 10 秒标记、假人调试；纯主菜单无目标时相机搜索与标签更新为 0。
4. **性能门槛**：相同环境 Title／原生设置／机场各 60 秒、至少三轮，记录搜索数、相机探测数、标签维护数、等待数、CPU 与 GC。**当前没有任何实机性能数字**，方案第九节「三项合计 ≤0.10ms/帧」未验证。
5. **UBImGui 条件 2**：需要带分配计数的三轮对照（见 6.4）。

---

## 八、结论汇总

| 项 | 判定 |
|---|---|
| 三项后台优化源码与方案一致性 | **通过**（A/B/C 逐条对应，改动最小，无越界） |
| 功能无退化（源码级） | **通过**（玩法、配置键、GUID、通信格式、死亡标记、假人、兼容模式均未动） |
| 离线回归 | **通过**（22 运行时 + 17 契约，本轮重跑 exit 0） |
| 构建 | **通过**（0 错误；复活 43 条均为既有可空性警告，新代码零警告） |
| 现装一致性 | **通过**（三 DLL md5 与当前源码构建完全一致；版本 1.4.5 / 1.4.1 / 1.1.6） |
| 部署路径 | **通过**（均指向实际现装独立目录，未新建重复安装；部署前后无进程，旧安装已备份） |
| 本轮真实修掉的 bug | `CameraCache` 禁用相机在重试窗口内仍被返回（已修，有断言覆盖） |
| 文档/元数据不一致 | **1 处**：麦克风工程无 `manifest.json`，现装残留 1.1.0 旧 manifest 与 DLL 1.4.1 不符（非功能 bug，未修） |
| UBImGui 纳入 LocalFix | **未达到门槛**（判据 1、3 明确未达；判据 2 缺测量，未判定） |
| 实机与三轮性能对照 | **未执行**（第七节清单） |

**总体**：三项后台优化的代码与离线证据可以支撑「候选可交付、可实机验收」，但不能支撑「性能已达标」。UBImGui 本轮不纳入，保留原库。
