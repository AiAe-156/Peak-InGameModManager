import io
import os

PROJECT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")


def prepend(name, anchor, lines):
    path = os.path.join(PROJECT, name)
    s = io.open(path, encoding="utf-8", newline="").read()
    nl = "\r\n" if "\r\n" in s else "\n"
    assert s.count(anchor) == 1, (name, anchor)
    s = s.replace(anchor, nl.join(lines) + nl + nl + anchor, 1)
    io.open(path, "w", encoding="utf-8", newline="").write(s)


prepend("README.md", "## 2.3.4 ", [
    "## 2.3.5 基础组件可以改译名和启停",
    "",
    "**改译名。** 在概览里点基础组件的名称，详情窗里多了「改译名」：填一个显示名称，列表里显示「译名（包名）」，留空恢复包名。只改显示，不动文件。",
    "",
    "**启停。** 同一个窗口里有「停用 / 启用」：把这个组件的程序集改名为 `.dll.d`（或改回 `.dll`），从下次启动起生效。启用状态在最左列显示为 ✓ / ○。MCE 自己运行所依赖的组件不能停用（可以启用）；被其他工具停用的组件请用原工具恢复。",
    "",
    "基础组件照旧固定在「基础组件」分组，不能改分组，也不参与目录整理。",
])
prepend("CHANGELOG.md", "## 2.3.4 ", [
    "## 2.3.5 基础组件改译名与启停（用户拍板：MCE 必需依赖除外可卸载/删除/改译名/启停；不改分组、不参与整理）",
    "",
    "- **改译名：** 组件行与详情标题读 `OverviewModRule.alias`（规则 ID `component:<Team-Name>`，`OverviewListView.ComponentRuleId`），显示为「译名（包名）」；详情窗「改译名」经 `SavePackInformation(ModInformation, id)` 保存，失败回滚。",
    "- **启停：** 新 `ComponentToggle`：对组件全部运行时文件做 `x.dll ↔ x.dll.d` 改名，走 `InstallationWriteLease.Prepare/Apply/Commit` 日志（中断由下次启动恢复），先整体预检（目标已存在、越出 BepInEx、文件缺失都在改名前拒绝）；MCE 必需依赖不可停用、可启用；外部工具停用的不在此恢复；身份未确认或没有程序集的不可操作。`InfrastructurePackages.Package.RuntimeFiles` / `InfrastructureOverviewModel.RuntimeFiles/Enabled`，`$enabled/$disabled` 状态筛选改按实际启用状态。启用列显示 ✓/○。详情窗新增两个操作并在完成后刷新组件列表。",
    "- **分组与整理：** 复核确认分组编辑器候选只来自模组行、整理规划器只采集 `plugins/`，组件不会进入两者。",
    "- 验证：三项构建 0/0；组件启停夹具 19 项（真实 `ComponentToggle` + `InstallationWriteLease` + `PluginFileNames` 在临时 BepInEx 树上实际改名：双向改名、日志提交、MCE 必需拒绝停用、外部停用、碰撞整体不改、越界拒绝）；现装组件探针、完整回归见结果记录。",
])
print("ok")
