#!/usr/bin/env python3
"""源码契约断言（2.2.7）：改动散布在 worker / 插件 / 无 UI 的深层类型里，用源码断言把
「哪条假设被换掉了、哪条边界必须留在原地」固定下来。只读，不写任何文件。"""
import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parents[2]

failures = []


def read(relative: str) -> str:
    path = ROOT / relative
    if not path.is_file():
        failures.append(f"缺少文件 {relative}")
        return ""
    return path.read_text(encoding="utf-8")


def must(haystack: str, pattern: str, name: str, where: str):
    if re.search(pattern, haystack) is None:
        failures.append(f"{name}：在 {where} 中找不到 /{pattern}/")


def must_not(haystack: str, pattern: str, name: str, where: str):
    if re.search(pattern, haystack) is not None:
        failures.append(f"{name}：{where} 中仍存在 /{pattern}/")


program = read("Updater/Program.cs")
files = read("Updater/Files.cs")
queue = read("src/Overview/OverviewUpdateQueue.cs")
recovery = read("src/Overview/RecoveryLauncher.cs")
publisher = read("src/Overview/PackPublisher.cs")
install_paths = read("src/Overview/InstallationPaths.cs")

# 1. 真实游戏根：worker 收 --game-root，只有真按游戏根语义的地方改用它
must(program, r'Array\.IndexOf\(args, "--game-root"\)', "worker 解析 --game-root", "Updater/Program.cs")
must(program, r"Files\.GameRoot = Path\.GetFullPath", "worker 设置真实游戏根", "Updater/Program.cs")
must(files, r"internal static string GameRootFor\(string bepinExRoot\)", "游戏根取值（带回退）", "Updater/Files.cs")
must(queue, r'--game-root', "启动 worker 时传入游戏根", "src/Overview/OverviewUpdateQueue.cs")
must(recovery, r"GAMEDIR_BASE64", "恢复脚本携带游戏根", "src/Overview/RecoveryLauncher.cs")
must(recovery, r"'--game-root',", "恢复脚本把游戏根传给 worker", "src/Overview/RecoveryLauncher.cs")
must(publisher, r"GameRoot => Path\.GetFullPath\(Paths\.GameRootPath\)", "导出以真实游戏根为准", "src/Overview/PackPublisher.cs")
must(install_paths, r"Paths\.GameRootPath", "AtGameRoot 以真实游戏根为准", "src/Overview/InstallationPaths.cs")
for relative in ("Updater/PackImports.cs", "Updater/PackTransactions.cs", "Updater/PackCompleteEnvironment.cs",
                 "Updater/PackImportTarget.cs", "Updater/InstallMetadata.cs"):
    must_not(read(relative), r'GetParent\((storage\.Root|root)\)[!?]?\.FullName, "PEAK_Data"|GetParent\((storage\.Root|root)\)[!?]?\.FullName, relative',
             "不再把 BepInEx 父目录当游戏根", relative)
must(read("Updater/PackImports.cs"), r"Files\.Child\(Files\.GameRootFor\(storage\.Root\), relative\)", "核心文件哈希按真实游戏根", "Updater/PackImports.cs")
must(read("Updater/PackTransactions.cs"), r"Files\.Child\(Files\.GameRootFor\(storage\.Root\), requirement\.Path\)", "核心文件复校按真实游戏根", "Updater/PackTransactions.cs")

# 2. 包含边界必须留在原地：管理器档案就在游戏根之外，换成游戏根会把整个安装判成越界
must(files, r"string boundary = Directory\.GetParent\(root\)!\.FullName;\s*\n\s*RejectLinks\(boundary, root\);", "安装边界仍是 BepInEx 父目录", "Updater/Files.cs")
for relative in ("Updater/InstallPlanner.cs", "Updater/PackMaintenance.cs", "Updater/NetworkProbe.cs",
                 "Updater/ShopQueue.cs", "Updater/PackStorage.cs", "Updater/WorkerTargetProtocol.cs"):
    must(read(relative), r"Directory\.GetParent\(", "边界用途保持 BepInEx 父目录", relative)

# 3. 管理器档案：识别而非拒绝
must(files, r"internal static string ManagerProfileRoot\(string root\)", "识别档案根", "Updater/Files.cs")
must_not(files, r'throw new IOException\("[^"]*mods\.yml', "worker 不再用 mods.yml 抛错拦启动", "Updater/Files.cs")
must_not(queue, r"Manager profiles must use their package manager", "插件侧守卫已移除（英文串）", "src/Overview/OverviewUpdateQueue.cs")
must_not(queue, r"mods\.yml", "插件侧不再用 mods.yml 拦更新", "src/Overview/OverviewUpdateQueue.cs")

# 4. mods.yml 版本同步：worker 只记意图，插件负责落盘
must(read("src/Updating/ManagerSyncContracts.cs"), r"class ManagerSyncRecord", "共享同步契约", "src/Updating/ManagerSyncContracts.cs")
must(read("Updater/ManagerProfileSync.cs"), r"Files\.Save\(path, pending\)", "worker 记录待处理意图", "Updater/ManagerProfileSync.cs")
must(read("Updater/MCE.UpdateWorker.csproj"), r"ManagerSyncContracts\.cs", "契约已并入 worker", "Updater/MCE.UpdateWorker.csproj")
must(read("Updater/Installer.cs"), r"ManagerProfileSync\.Record\(plan\.Root, job, plan, rollback\)", "提交/回滚时记账", "Updater/Installer.cs")
sync = read("src/Overview/ManagerProfileSync.cs")
must(sync, r"versionNumber", "插件改写包版本", "src/Overview/ManagerProfileSync.cs")
must(sync, r"manager-sync-backups", "改写前备份 mods.yml", "src/Overview/ManagerProfileSync.cs")
must(sync, r"ManagerRunning\(\)", "管理器运行时推迟", "src/Overview/ManagerProfileSync.cs")
must(read("src/Plugin.cs"), r"ManagerProfileSync\.Apply\(", "启动时应用待处理项", "src/Plugin.cs")

# 5. 结构化预览：worker 出结构，插件出文字与树形
contracts = read("src/Updating/InstallationLayoutContracts.cs")
must(contracts, r"class LayoutTreeNode", "结构化树节点", "src/Updating/InstallationLayoutContracts.cs")
must(contracts, r"enum LayoutSourceKind", "来源类别", "src/Updating/InstallationLayoutContracts.cs")
must(read("src/Updating/ShopContracts.cs"), r"List<LayoutTreeNode> Tree", "任务携带树", "src/Updating/ShopContracts.cs")
must(read("src/Updating/InstallationLayoutPlanner.cs"), r"public static string Label\(string\? name\)", "可读目录名（无哈希）", "src/Updating/InstallationLayoutPlanner.cs")
plan = read("Updater/LayoutPlan.cs")
must(plan, r"plan\.Tree\.Add\(new LayoutTreeNode", "worker 产出树节点", "Updater/LayoutPlan.cs")
must(plan, r"PreferredOuter\(", "包目录优先用可读名称", "Updater/LayoutPlan.cs")
must_not(plan, r"未登记为本管理器安装", "旧措辞已换掉", "Updater/LayoutPlan.cs")
must(read("Updater/WorkerParallelOperations.cs"), r"task\.Tree\.AddRange\(plan\.Tree\)", "树随任务下发", "Updater/WorkerParallelOperations.cs")
ui = read("src/Overview/OverviewPage.Installation.cs")
must(ui, r"LayoutPreviewText\(ShopTask task\)", "按树渲染预览", "src/Overview/OverviewPage.Installation.cs")
must(ui, r"LayoutNodeNote", "逐节点本地化标注", "src/Overview/OverviewPage.Installation.cs")

# 6. 版本号一致
for relative in ("ModConfigEnhance.csproj", "Updater/MCE.UpdateWorker.csproj", "Desktop/MCE.Desktop.csproj"):
    must(read(relative), r"<Version>2\.2\.7</Version>", "版本号已升到 2.2.7", relative)
must(read("manifest.json"), r'"version_number": "2\.2\.7"', "清单版本已升到 2.2.7", "manifest.json")

if failures:
    print("FAIL")
    for item in failures:
        print(" - " + item)
    sys.exit(1)
print("PASS 源码契约断言 6 组")
