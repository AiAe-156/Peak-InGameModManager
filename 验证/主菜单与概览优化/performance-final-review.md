# 概览列表性能优化 · 最终集成审查

审查对象：`ModConfigEnhance` 概览页完整行缓存 / 更新队列 / 基础组件路径
审查方式：只读源码 + 独立可执行验证（不构建主工程、不 publish、不部署、不改真实数据、不启停游戏）
生产文件改动：**零**（`src/Overview/*`、`Updater/*` 的 mtime 仍为 2026-10-30T19:08，本轮未写入）

---

## 结论

**设计通过，但有 1 个必须修的性能回归 + 1 个应修的功能缺陷。**

本轮优化的三条主线经实际执行验证成立：

| 主线 | 结论 | 证据 |
| --- | --- | --- |
| 稳定身份完整子树 LRU（`StableRowCache`） | 成立 | 2056 项独立检查中容量 0/12/24、LRU 顺序、重复 Park 幂等、非成员 Remove 全部通过；2953 项既有检查覆盖 60/120/300 行反复滚动时"身份与监听不变、常驻树数受视口约束" |
| `StateChangePublication` 按内容判重 | 成立 | 500 次稳定轮询零写入零 Revision；写异常回滚 Revision 并保持可重试；重试后再次判重 |
| 危险操作保留同步 `PollSafety()` | 成立 | `HasPendingForPackage` 读失败返回 `_unsafeRead=true`（fail-closed）；`EnsurePublicationSafe` 读失败直接抛 |

发现的问题按严重度排列如下。**问题 1 是本轮优化新引入的回归**，其余为既有缺陷或清理项。

---

## 问题 1（必须修，本轮引入）`RowUpdateStamp` 每帧 O(n) 查找 + 字符串分配

**位置**：`src/Overview/OverviewListView.VirtualRows.cs:137-144`（`_mods.Find` 在 `141`）
**调用点**：同文件 `87`（滚动时逐可见行）、`101`（建行时）、`152`（`RefreshUpdateRows` 逐已建行）

```csharp
private string RowUpdateStamp(VirtualRow row)
{
    if(!row.Key.StartsWith("mod:",StringComparison.Ordinal))return string.Join("\n",InfrastructurePackages.Results
        .Where(p=>"component:"+p.Key==row.Key).Select(p=>p.Version+"\t"+p.Latest));
    var mod=_mods.Find(m=>"mod:"+m.Id==row.Key);   // ← 谓词内做字符串拼接
    ...
}
```

**问题**：谓词 `"mod:"+m.Id==row.Key` 对每个候选模组都新建一个字符串。

- 滚动路径（`87`）：每帧对约 16 个可见行各扫一遍 `_mods`，300 模组即 **4800 次字符串分配/帧**。
- 更新结果路径（`152`）：`RefreshUpdateRows` 遍历所有 `row.View!=null` 的行（活动行 + 最多 48 个停泊行），同样是每行一次线性扫描。
- 非模组行（分组标题、组件行）走 `139-140`，对每个可见分组标题都扫一遍 `InfrastructurePackages.Results` 并逐项拼 `"component:"+p.Key`。

这与"降低 GC"的目标直接相悖。该函数是本轮优化线新增的：`备份/20261030_MCE性能收尾` 里 `RowUpdateStamp` 不存在（`VirtualRows.cs:79` 直接 `row.Bind(view); WireRowNavigation(row);`），到 `备份/20261030_MCE完整行缓存` 才出现（`VirtualRows.cs:82`），当前共 4 处引用。

**实测**（`验证/主菜单与概览优化/PerformanceFinalReview`，300 模组 / 16 可见行 / 600 帧，即 10 秒 60fps 滚动）：

```
MEASURE RowUpdateStamp pattern: 52739.1 KiB over 600 frames x 16 rows, 21 ms, hits=9600
MEASURE dictionary lookup:          0.0 KiB,                        7 ms, hits=9600
```

即约 **88 KiB/帧**、60fps 下约 **5.3 MB/s** 纯垃圾；改成字典查找后为 **0 分配**且快 3 倍。

**建议修法**（最小改动，O(1) 且零分配）：在 `AddVirtualRow` 时把模组引用直接挂在行上，`RowUpdateStamp` 不再反查。

```csharp
// VirtualRow 增加字段
internal OverviewMod? Mod;

// Render() 里 AddVirtualRow("mod:"+mod.Id, ...) 处传入 mod
// RowUpdateStamp:
var mod = row.Mod;
```

若不想改 `AddVirtualRow` 签名，退一步在 `Render()` 末尾构建 `Dictionary<string,OverviewMod>`（键 `"mod:"+Id`）供 stamp 查表，同样零分配。

---

## 问题 2（应修，既有缺陷）`FlushInfrastructureRedraw` 被选中项守卫无限期饿死

**位置**：`src/Overview/OverviewListView.Infrastructure.cs:50-57`（守卫在 `55`）

```csharp
private void FlushInfrastructureRedraw()
{
    if (!_infrastructureRedraw || !_opened || _building || IsEditingText || _dragged != null || _scroll.velocity.sqrMagnitude > 1) return;
    var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
    // Do not replace a keyboard/controller target while the user is navigating rows.
    if (selected != null && selected.transform.IsChildOf(_root)) return;   // ← 直接 return，不清 _infrastructureRedraw
    _infrastructureRedraw = false; Render();
}
```

**复现条件**（普通鼠标即可，不需要手柄）：

1. 打开概览页，后台组件扫描尚未完成（首帧显示"正在读取组件……"）。
2. 用左键单击任意模组名称。
   - 模组标题在 `BindDependencyHint` 里被补上 `Selectable`（`OverviewDependencyHints.cs:68`），
   - `LinkRowNavigation()` 又把该 `Selectable` 的 `navigation.mode` 设为 `Explicit`（`VirtualRows.cs:130`），
   - 于是 Unity 的 `Selectable.OnPointerDown` 会把 `EventSystem.currentSelectedGameObject` 设为该标题。
   - `Select()` 只调 `RefreshSelectionColors()`，**不触发 Render**（`OverviewListView.cs:561-573`）。
3. 扫描完成，pump 回调置 `_infrastructureRedraw = true`（`Infrastructure.cs:46`）。
4. `FlushInfrastructureRedraw` 每帧命中守卫并直接 return，`_infrastructureRedraw` 永远为 `true`。

**后果**：组件行不出现，占位文案永久停在"正在读取组件……"（`_infrastructureLoading` 已在回调里置 false，但占位文本是上一次 Render 建的，不会自己更新）。只有用户再做一次会 Render 的动作（勾选复选框、折叠分组、搜索、改宽度）才恢复。

`_root` 覆盖 `_tools`、`_header`、`_viewport` 与全部行，所以列表内任何 Selectable 被选中都会命中该守卫，不只是行。

**建议修法**：删除该守卫。`Render()` 本身已经做了焦点保全——`Render` 开头 `CaptureRowFocus()`（`OverviewListView.cs:288`）、清选中（`297`）、`finally` 里 `RestoreRowFocus(oldFocus)`（`489`），且 `RestoreRowFocus` 按 `row.Key` 找回组件行（`VirtualRows.cs:168-176`）。若仍想保留"别打断键盘导航"的意图，则改为**有界延迟**：记录连续 defer 次数/时间，超过约 2 秒强制刷新一次，避免永久陈旧。

---

## 问题 3（低，视觉）`Render()` 结束后不重建虚拟行，产生一帧空列表

**位置**：`src/Overview/OverviewListView.cs:470-491`（`finally` 只置 `_virtualTop=float.NaN`，不调 `RefreshVirtualRows`）

`Render()` 销毁全部行（`ResetVirtualRows()` + `Clear(_content)`），`finally` 里只把 `_virtualTop` 置 NaN 等下一帧重建。而 `OverviewListPump.Update` 里的顺序是 `RefreshVirtualRows()`（`133`）→ `RefreshInfrastructure()`（`135`）→ `FlushInfrastructureRedraw()`（`136`，可能 Render）→ 宽度/语言变化 Render（`138`）。凡是在 `133` 之后发生的 Render，本帧 `_content` 就是空的，渲染出来是一帧空列表；下一帧才补上。点击驱动的 Render（勾选、折叠、搜索）是否闪一帧取决于 `EventSystem.Update` 与 `OverviewListPump.Update` 的相对顺序。

**建议修法**：`finally` 中 `_building = false;` 之后补一行 `RefreshVirtualRows();`（该函数自带 `!_opened` 早退，`OnOpened` 路径不受影响）。

---

## 问题 4（低，健壮性）`RefreshVirtualRows` 在 Bind 抛异常时已提交 `_virtualTop`，失败行永久空白

**位置**：`src/Overview/OverviewListView.VirtualRows.cs:72`（提交 `_virtualTop`）与 `101-102`（`catch { ReleaseRow(row); throw; }`）

`_virtualTop = top` 在循环之前就写入。若某行 `Bind` 抛异常，`catch` 释放该行后重新抛出，但 `_virtualTop` 已是当前 `top`，下一帧 `if(Mathf.Abs(top-_virtualTop)<.1f …) return;` 直接早退——该行不会被重建，一直是空白，直到用户滚动改变 `top`。异常还会穿出 `Tick?.Invoke()`（`OverviewListView.cs:778` 无 try），本帧 Tick 后续逻辑全部跳过。

**建议修法**：`catch` 中先 `_virtualTop = float.NaN;` 再 `throw;`，让下一帧重试。

---

## 问题 5（清理）`Render()` 的 `buildingRow` 是死代码

**位置**：`src/Overview/OverviewListView.cs:291`（`RectTransform? buildingRow = null;` 从未赋值）与 `466`（`if (buildingRow != null) { … }` 永不成立）

虚拟化之后行在 `RefreshVirtualRows` 里构建，`Render` 不再逐行建控件，这个局部变量与 catch 分支是旧内联建行逻辑的残留。无害，但会误导后续维护者以为 Render 内建行有回滚保护。建议连同分支一起删除。

---

## 已核实无问题（本轮重点排查后排除）

| 候选 | 结论 |
| --- | --- |
| `CoalescedProgress.Publish` 陈旧 pending（`_pending` 已是 B，新值 A==`_last` 被丢弃） | **不是 bug**。测试证明 `Take` 返回 B 后，下一次 `Publish("A")` 会正常投递（自愈）；且进度文本随 `Completed` 单调递增，实际不可达 |
| `ChangedFileSnapshot` 同长度同 tick 重写盲区 | **确认存在但风险极低**。测试用 `File.SetLastWriteTimeUtc` 强制同 tick 复现了"内容变了但指纹没变"；真实场景需在同一个 FS 时间戳粒度内完成同长度重写，且下一次任意文件变化即恢复 |
| `InfrastructurePackages.Results` 无锁/无 volatile（`InfrastructurePackages.cs:20,112`） | **实际安全**。`SetResults` 在后台线程写，主线程读；但两者之间隔着 `_pump.Enqueue` / `ConcurrentQueue` 的出队，构成 happens-before 屏障。且 `Results` 指向的 `List` 写入后不再修改 |
| `Loc.T` / `Loc.F` 在后台线程调用（`CheckProgress.Summary`/`GroupProgress` 经 `Task.Run` 回调） | **不会崩**。`_inlineSnapshot` 只做字段重赋值（原子）与字典读；`KnownInline` 是 `ConcurrentDictionary`；`InlineKeys` 走 `Lazy`。最坏情况是切语言瞬间出现一条混合语言文本 |
| `OverviewModRule.groupId` 可能为 null 导致 `ToDictionary` / `Groups[...]` 抛异常 | **不会**。`groupId` 默认 `""`（`OverviewModels.cs:83`），`CleanState` 显式置 `""`（`OverviewProfileStore.cs:585`）；`Groups` 的键与 `Complete` 的索引都来自同一个 `entries` 快照（`ThunderstoreUpdateService.cs:97` / `90`） |
| `ParkRow` 不移除 `_dropAreas` 条目 | 正确。`MoveDragFeedback` 用 `activeInHierarchy` 过滤（`OverviewListView.cs:594`），`ReleaseRow` 才移除 |
| `ReleaseRow` 中 `Cache(row).Remove(row)` 与 `view==null` 的先后 | 正确。先移出预算再判空，避免无 view 的行占着缓存槽 |
| `RefreshVirtualRows` 的 `_virtualTop` 早退 | 正确。需要强制刷新的调用点都先置 `float.NaN`，NaN 比较恒 false 可穿透 |
| `QueueWorker.Save` 去重 | 正确。`published` 仅在写成功后更新；写失败回滚 Revision 并抛出，保持可重试（测试覆盖） |
| `OverviewMaintenance` 静态字典泄漏 | 未发现。`OnDisable` 移除、`OnEnable` 重注册；`LateUpdate` 里 `owner==null` 分支清理 `Checks` |
| 回调错误对象 | 正确。`OverviewUi.Run` 包裹全部按钮回调；`Toggle.onValueChanged` 经 `Run`；pump 队列逐项 try/catch。`ReleaseRow` 清空 `Clicked`/`DragStarted`/`Dropped`/`onClick`/`onValueChanged` 并先 `Lease.Release()`，回收壳不会误触旧身份 |

---

## 未覆盖 / 需实机确认

1. **Unity 侧监听数与真实帧时间**：本轮只验证了模型层"身份与监听不变"，`AddListener` 的实际注册数量、`GetComponentsInChildren` 的真实开销需在游戏内用 Profiler 观察。
2. **手柄/键盘导航**：`MoveVirtualFocus` 的越界唤醒、`RestoreRowFocus` 的列恢复、以及问题 2 修好后的焦点保持，都需要手柄实机验证。
3. **拖动钉住**：`IsDragging` 钉住与 `_dropAreas` 的离屏过滤只在模型层验证，拖拽手感需实机。
4. **三轮性能对照**：`最终优化方案.md` 里的三轮对照数据未在本轮重测（不启停游戏）。
5. **问题 2 的修复**会改变焦点行为，修完需回归"键盘导航中后台扫描完成"这一场景。

---

## 验证记录

全部在本机实际执行，日志随附：

| 命令 | 结果 | 日志 |
| --- | --- | --- |
| `python 验证/主菜单与概览优化/PerformanceChecks/source_contracts.py` | **25 passed** | — |
| `dotnet run --project 验证/主菜单与概览优化/PerformanceChecks/PerformanceChecks.csproj -c Release` | **2953 passed** | `PerformanceChecks/final-review-rerun.log` |
| `dotnet run --project 验证/主菜单与概览优化/PerformanceFinalReview/PerformanceFinalReview.csproj -c Release` | **2056 passed** | `PerformanceFinalReview/final-review-checks.log` |

新增独立工程 `验证/主菜单与概览优化/PerformanceFinalReview/`（`PerformanceFinalReview.csproj` + `Program.cs`）以只读方式编译生产源文件，覆盖：

- `StableRowCache`：容量 0/12/24 的预算与淘汰边界、LRU 顺序（重 Park 后淘汰次旧项）、重复 Park 幂等、非成员 `Remove` 无操作、负容量抛 `ArgumentOutOfRangeException`。
- `CoalescedProgress`：150ms 门控、重复值不重投、陈旧 pending 的自愈、`Clear` 重置去重基线、2000 次并发后台写入。
- `ChangedFileSnapshot`：500 次稳定轮询不重复反序列化、读失败保留旧值并报错、上下文切换不继承旧环境、多文件竞态读被拒、同 tick 同长度重写盲区（复现并标注为已记录风险）。
- `StateChangePublication`：500 次稳定轮询零写入、业务变化才升 Revision、写异常不改 Revision 且可重试、`QueueWorker.Save` 形状的 Revision 回滚。
- `VirtualRowRange` / `RowIdentityLease`：缓冲边界、负滚动钳制、越界滚动、控制器全模型遍历、回收壳拒绝旧身份监听。
- `RowUpdateStamp` 分配量实测与字典方案对照（见问题 1）。

沙箱目录由测试自建自删，不触碰真实配置、库存、队列或来源数据。
