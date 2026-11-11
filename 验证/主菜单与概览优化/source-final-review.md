# MCE 来源链独立最终审查（补做）

- 日期：2026-10-30
- 范围：仅 `src/Overview/StoreIdentityIndex.cs`、`ModSources.cs`、`ModSources.Discovery.cs`、`ThunderstoreUpdateService.cs`、`验证/SourceUpdateChecks/Program.cs`
- 边界：未改生产代码、未部署、未构建主项目、未写真实配置/库存/队列、未启停游戏。探针工程建在 `反编译/_tmp_tools/source-review-probe/`，用完已删除。
- 说明：此前同一任务因 `Tool call belongs to another run` 中断，**没有**最终结论，本文件不继承其任何"通过"判断。

## 结论

**55 项沙盒检查全部通过，但审查发现 3 个具体缺陷（2 个本轮引入，1 个本轮修复不完整）。不建议把来源链标记为"最终验收通过"。**

| # | 缺陷 | 位置 | 性质 |
|---|---|---|---|
| 1 | 检查进行中修改分组 → `KeyNotFoundException`，整轮检查中断并弹出英文异常 | `ThunderstoreUpdateService.cs:90` | 本轮引入 |
| 2 | 取消/失败后 `CheckMessage` 残留"有新版本"，与已清除的绿点矛盾 | `ThunderstoreUpdateService.cs:98` | 本轮修复不完整 |
| 3 | 全局更新许可关闭后，核心/启动补丁包仍被检查并计入更新数 | `ThunderstoreUpdateService.cs:72` | 本轮行为变更 |

---

## 缺陷 1：分组在检查期间变化导致整轮中断

`CheckAsync` 在轮次开始时按当时的 `groupId` 建表：

```csharp
Groups = entries.GroupBy(m => m.Rule.groupId).ToDictionary(g => g.Key, g => new[] { 0, g.Count() })
```

而 `Complete` 用**实时**的 `mod.Rule.groupId` 直接下标取值：

```csharp
_progress.Groups[mod.Rule.groupId][0]++;
```

`mod.Rule` 是 `repository.GetRule(guid)` 返回的 `State.mods` 活对象；分组编辑器保存时会直接改写它（`OverviewGroupEditor.cs:283/293`），而 `ManageGroups()` / `EditGroup()` **没有** `_checking` 守卫（对比 `CheckSingleUpdate` 有）。检查跑在 `Task.Run` 后台线程，主线程此时可正常改分组。

实测（生产方法 + 生产 `Complete` 路径）：

```
PROBE group-change-mid-check threw=True KeyNotFoundException: The given key 'brand-new-group' was not present in the dictionary. completed=2/2
```

后果：异常从 `Complete` 抛出 → 被 `catch (Exception ex) { error = ex.Message; throw; }` 捕获 → `RunChecks` 的 `catch (Exception)` 把**原始英文异常文本**直接显示给用户（`LocalizeMessage` 翻不了它），且该轮剩余条目不再完成，而 `Completed` 已经计数到 2/2，进度与结果自相矛盾。

建议：`Complete` 用 `TryGetValue` 兜底（取不到就跳过分组计数），或在轮次开始时把 `groupId` 快照进条目，或给分组编辑加 `_checking` 守卫。

## 缺陷 2：取消/失败后 `CheckMessage` 残留

轮次开始的复位循环清了 `HasUpdate` / `LatestVersion` / `LastCheckedUtc` / `UpdateNotes`，**唯独没清 `CheckMessage`**：

```csharp
foreach (var mod in entries) { mod.HasUpdate = false; mod.LatestVersion = ""; mod.LastCheckedUtc = null; mod.UpdateNotes = ""; }
```

若该轮在 `Apply` 之前就失败或取消，`CheckMessage` 保留上一轮的"有新版本"。实测：

```
PROBE after-success  HasUpdate=True  CheckMessage='Update available' Latest='2.0.0'
PROBE after-failure  HasUpdate=False CheckMessage='Update available' Latest=''      reportError='offline'
PROBE d-after-cancel HasUpdate=False CheckMessage='Update available' cancelled=True
```

后果：行上绿点已消失（正确），但版本提示弹窗仍写"有新版本"，同时"商店最新版：尚未检查"——同一行自相矛盾。55 项里的 `old green results cleared on failed new round` 只断言了 `HasUpdate`，没覆盖 `CheckMessage`，所以没拦住。

建议：复位循环补 `mod.CheckMessage = "";`。

## 缺陷 3：全局更新许可不再约束核心/启动补丁包

改前：

```csharp
var infrastructure = _repository.GetGlobalUpdateAllowed() ? InfrastructurePackages.Scan() : new List<InfrastructurePackages.Package>();
```

改后无条件扫描，且组件行 `Rule = new OverviewModRule { groupId = "__infrastructure" }`（`updateAllowed` 默认 `true`、无 override），因此 `IsUpdateAllowed` 恒真 → 进入 eligible → 参与查询与计数。

实测（`GetGlobalUpdateAllowed()==false`）：

```
PROBE global-permission-off: ModCount=2 Completed=2 PackageCount=1 UpdateCount=2 infraLatest='2.0.0'
```

后果：用户关掉全局更新检查后，只要还有任一单项许可开着，核心/启动补丁包仍会被检查（跨社区 loader 包还会发 HTTP），并被计入"检查出 N 个可更新模组"。这是相对改前的权限范围收缩丢失。

建议：组件行 `Rule.updateAllowed = _repository.GetGlobalUpdateAllowed()`，或在 `eligible` 过滤时对 `__infrastructure` 组单独套全局许可。

---

## 各审查门对照

| 门 | 结论 | 证据 |
|---|---|---|
| 官方存在与本地身份 | 通过 | `exact name plus trusted author`、`same name authors ambiguous`、`unique name alone never confirms`、`owner-only homepage insufficient`、`conflicting author and repository remain unresolved`、`homepage owner is not store-team evidence`、`repository owner differing from store team does not veto full repository evidence`、`deprecated identity rejected`、`invalid official latest version does not confirm source` |
| 手选授权 / 原始证据污染 | 通过 | `author-source user flag alone cannot authorize a mapping`、`selected mapping never forges local author evidence`、`published cache cannot forge user authority or replace conflicting local homepage`、`export keeps identity but never transports installation user authority`、`legacy unbound user record requires revalidation instead of claiming a new installation` |
| DLL 清单 / 环境失效 | 通过 | `DLL stamp change invalidates user mapping`、`manifest content change invalidates user mapping`、`declared author change invalidates user mapping`、`homepage change invalidates user mapping`、`same workspace replacement pack invalidates mapping`、`installation mutation during catalog await rejects stale discovery result` |
| 旧 Local 与显式 Local | **有缺陷** | 见下方"缺陷 4（观察项）"；`legacy published Local restored`、`explicit local preserved`、`choice isolated to original installation` 通过 |
| 取消后旧结果 | **有缺陷** | 缺陷 2；`cancel preserves actual incomplete progress`、`global failure not fake completion` 通过 |
| 条目计数与绿点 | **有缺陷** | 缺陷 1、缺陷 3；`all entries counted including disabled local unresolved`、`K matches distinct green-dot entries`、`existing component group participates in same entry unit` 通过 |

### 缺陷 4（观察项，未计入 3 个缺陷）：个人 auto 记录会遮蔽作者发布的显式 Local

`ExplicitLocal` 先查个人记录，命中即返回，不再看发布包：

```csharp
Records.TryGetValue(mod.Guid, out var record) && record.kind == ModSourceKind.Local && Applies(record, mod)
|| Get(mod.Guid) is { kind: ModSourceKind.Local, explicitLocal: true } published && Applies(published, mod)
```

而 `Get()` 优先返回个人记录。用户点过一次"自动"（`SourceUi.cs:133` → `ModSources.Set(mods, null)`）就会留下 `note="auto"` 的个人记录，此后作者在发布包里标的"本地自用"被永久遮蔽。实测：

```
PROBE A no-personal-record: ExplicitLocal=True  confirmed=False kind=Local
PROBE B after-auto-record:  ExplicitLocal=False confirmed=True  kind=Store (author said LOCAL)
```

代码其他地方都把 `note=="auto"` 当"没有选择"处理（`Apply` 里 `record.note != "auto"`），这里却当成选择，前后不一致。建议 `Get()` 或 `ExplicitLocal` 对 `note=="auto"` 回落到发布包。

---

## 实际 55 项结果

命令（项目根，只沙盒、关闭部署）：

```text
dotnet run --project 验证/SourceUpdateChecks/SourceUpdateChecks.csproj -c Release -p:EnableDeployment=false
```

结果：`TOTAL 55 production checks passed`，沙盒 `验证/SourceUpdateChecks/runs/20261030_195550_007`。逐项：

```text
PASS exact name plus trusted author
PASS same name authors ambiguous
PASS unique name alone never confirms
PASS complete repository supports strict name
PASS owner-only homepage insufficient
PASS conflicting author and repository remain unresolved
PASS edit distance never auto-confirms
PASS edit-distance candidate recommended
PASS preserve variant BetterPingFork
PASS preserve variant BetterPingCN
PASS preserve variant BetterPingFix
PASS nonexistent explicit identity not replaced
PASS official exact identity verified
PASS existing store identity cannot override conflicting local author
PASS folder-format identity alone is not sufficient evidence
PASS conflicting store URL rejected
PASS deprecated identity rejected
PASS legacy published Local restored
PASS author original unchanged
PASS automatic discovery does not write personal source choices
PASS explicit local preserved
PASS choice isolated to original installation
PASS production export retains store identity and version
PASS all entries counted including disabled local unresolved
PASS shared identity queried once
PASS K matches distinct green-dot entries
PASS unknown local version not DLL comparison
PASS PEAK catalog metadata only no extra network
PASS concise summary no package categories
PASS cancel preserves actual incomplete progress
PASS global failure not fake completion
PASS old green results cleared on failed new round
PASS existing component group participates in same entry unit
PASS homepage owner is not store-team evidence
PASS repository owner differing from store team does not veto full repository evidence
PASS author-source user flag alone cannot authorize a mapping
PASS same repository branches cannot be auto-disambiguated by exact name
PASS candidate reason exposes repository branch ambiguity
PASS Apply cannot confirm official existence against conflicting local author
PASS explicit correction A to B replaces stale store URL and remains usable
PASS selected mapping never forges local author evidence
PASS repeated Apply preserves valid explicitly selected mapping
PASS manual mapping binds to actual installation
PASS DLL stamp change invalidates user mapping
PASS manifest content change invalidates user mapping
PASS declared author change invalidates user mapping
PASS homepage change invalidates user mapping
PASS same workspace replacement pack invalidates mapping
PASS derived fixture initially resolves with local evidence
PASS derived cache cannot override changed installation and author
PASS export keeps identity but never transports installation user authority
PASS published cache cannot forge user authority or replace conflicting local homepage
PASS legacy unbound user record requires revalidation instead of claiming a new installation
PASS invalid official latest version does not confirm source
PASS installation mutation during catalog await rejects stale discovery result
```

**这 55 项全绿并不代表来源链无缺陷**：缺陷 1/2/3/4 都不在断言覆盖内（分别缺"检查中改分组""失败后 CheckMessage""全局许可关闭""auto 记录 + 发布包 Local"四个场景）。

## 未实机 / 未覆盖项

- 未在游戏内验证：真实分组编辑与检查并发、真实取消按钮时序、绿点与提示弹窗的视觉一致性。
- 未验证真实网络下核心/启动补丁包的跨社区 loader 包 404 路径。
- 未运行 `OfflineImport`（6 项）、`SharedStoreCatalog20261029`（25 项）、`UpdateFlow20261029`（16 项）——按限时窄范围要求，本轮只跑指定工程。
- 未构建主项目、未部署；现装 DLL 与本轮源码的一致性未复核（本轮无生产改动，无需复核）。

## 建议处置

1. 缺陷 1、2 改动量极小（`TryGetValue` 兜底 / 补一行 `CheckMessage=""`），建议先修再宣称来源链收尾。
2. 缺陷 3 需确认设计意图：若"全局许可关闭仍检查核心包"是有意为之，应在文档写明；否则按建议恢复门控。
3. 缺陷 4 建议一并修，属"旧 Local 与显式 Local"门的实际漏洞。
4. 修完补 4 项针对性断言（上述四个场景），再重跑本 55 项。
