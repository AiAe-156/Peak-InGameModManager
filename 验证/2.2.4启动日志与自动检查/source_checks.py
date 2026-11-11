#!/usr/bin/env python3
"""2.2.4 源码契约断言。

本轮的改动都落在无法单独编译的目标里（游戏侧深层类型、worker 的 net48 程序集），
用源码断言代替纯逻辑桩。只读，不写任何文件。
"""
import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "src"
UP = ROOT / "Updater"

failures = []
ran = []


def read(base: Path, relative: str) -> str:
    path = base / relative
    if not path.is_file():
        failures.append(f"缺少文件 {relative}")
        return ""
    return path.read_text(encoding="utf-8")


def must(text: str, pattern: str, name: str, where: str):
    ran.append(name)
    if re.search(pattern, text) is None:
        failures.append(f"{name}：在 {where} 中找不到 /{pattern}/")


def must_not(text: str, pattern: str, name: str, where: str):
    ran.append(name)
    if re.search(pattern, text) is not None:
        failures.append(f"{name}：{where} 中仍存在 /{pattern}/")


queue = read(SRC, "Overview/OverviewUpdateQueue.cs")
auto = read(SRC, "Overview/AutomaticUpdateChecks.cs")
repo = read(SRC, "Overview/OverviewRepository.cs")
repo_profiles = read(SRC, "Overview/OverviewRepository.Profiles.cs")
logwin = read(SRC, "Overview/LogWindow.cs")
repeat = read(SRC, "Diagnostics/LogRepeatIndex.cs")
install_page = read(SRC, "Overview/OverviewPage.Installation.cs")
qw = read(UP, "QueueWorker.cs")
storage = read(UP, "InstallStorage.cs")
layout = read(UP, "LayoutPlan.cs")
wops = read(UP, "WorkerParallelOperations.cs")

# ① 启动不再重播历史终态
must(queue, r"if \(first && \(job\.State == UpdateStates\.Completed",
     "首次看到的纯信息性终态不再播报", "OverviewUpdateQueue.cs")
must(queue, r"UpdateStates\.RolledBack \|\| job\.State == UpdateStates\.Superseded",
     "已回退/已被接替同样跳过", "OverviewUpdateQueue.cs")

# ② 自动检查：已应用视图 + 复用重载扫描 + 重试复用准备结果
must(auto, r"new OverviewRepository\(appliedView:true\)",
     "自动检查改用已应用视图（跳过草稿写入）", "AutomaticUpdateChecks.cs")
must(auto, r"repo\.ReloadInstalled", "复用仓库重载已完成的扫描", "AutomaticUpdateChecks.cs")
must_not(auto, r"new OverviewCatalogue\(\)\.Scan\(", "不得再扫第二遍", "AutomaticUpdateChecks.cs")
must(auto, r"_prepared", "重试复用上一次的准备结果", "AutomaticUpdateChecks.cs")
must(auto, r"if \(settled\) _prepared = null", "正常完成后丢弃准备结果", "AutomaticUpdateChecks.cs")
must(repo, r"ReloadInstalled \{ get; private set; \}", "仓库暴露重载扫描结果", "OverviewRepository.cs")
must(repo_profiles, r"ReloadInstalled = installed;", "重载时写入该结果", "OverviewRepository.Profiles.cs")

# ③ worker 扫盘节流跨进程生效
must(storage, r"internal static bool SweepDue\(string home\)", "新增 SweepDue", "InstallStorage.cs")
must(storage, r"internal static void MarkSwept\(string home\)", "新增 MarkSwept", "InstallStorage.cs")
must(storage, r"storage-sweep\.stamp", "节流标记文件名", "InstallStorage.cs")
must(qw, r"InstallStorage\.SweepDue\(home\)", "worker 走新节流", "QueueWorker.cs")
must_not(qw, r"\bsweptAt\b", "旧的进程内节流已移除", "QueueWorker.cs")

# ④ 目录整理预览：预览不算哈希、每个包只走一遍、有阶段进度
must_not(layout, r"Hash = Files\.Hash\(file\)", "预览阶段不再全量算哈希", "LayoutPlan.cs")
must(layout, r"walkedFiles", "每个包目录只枚举一遍", "LayoutPlan.cs")
must(layout, r"Action<string>\? progress = null", "Build 接受进度回调", "LayoutPlan.cs")
must(layout, r"void Step\(string message\)", "阶段进度同时写日志与任务", "LayoutPlan.cs")
must(wops, r"file\.Hash=Files\.Hash\(", "确认落地时才对要移动的文件算哈希", "WorkerParallelOperations.cs")
must(wops, r"shop\.WriteTask\(task\)", "阶段进度写回任务文件", "WorkerParallelOperations.cs")
must(install_page, r'task\.State != "ready"', "界面在没有 ready 时显示进度", "OverviewPage.Installation.cs")

# ⑤ 日志窗口：判定内容变化不再建中间字符串；分组只排序一次
must_not(repeat, r"Key\(r\)\) \{ hash \^= c", "快照哈希不再依赖拼好的键", "LogRepeatIndex.cs")
must_not(repeat, r"foreach \(char c in r\.Epoch \+ ", "不再拼接整行字符串", "LogRepeatIndex.cs")
must(repeat, r"private static void FoldText\(ref ulong hash", "改为逐字段折叠", "LogRepeatIndex.cs")
must(logwin, r"var groups = _repeatIndex\.Groups;", "分组只取一次", "LogWindow.cs")
must_not(logwin, r"_repeatIndex\.Groups\.Where", "不再重复排序分组", "LogWindow.cs")

# ⑥ 本轮不得新增性能采样/监控钩子（诊断统一归 FrameDoctor）。
#    只查本轮新逻辑所在、且原先没有日志的文件：OverviewUpdateQueue.cs / AutomaticUpdateChecks.cs
#    本来就带 ManagerLog / 警告输出，不在此列。
for name, text in (("LogRepeatIndex.cs", repeat), ("OverviewPage.Installation.cs", install_page)):
    must_not(text, r"\b(LogDebug|LogInfo|LogWarning|LogError)\s*\(", "新增了采样/日志", name)

if failures:
    print("源码断言失败：")
    for item in failures:
        print("  FAIL  " + item)
    sys.exit(1)

print(f"2.2.4 源码断言全部通过（6 组，{len(ran)} 项）")
