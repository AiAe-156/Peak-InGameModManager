#!/usr/bin/env python3
"""源码契约断言：这些改动落在无法单独编译的深层类型里（仓库/列表视图），
用源码断言补上纯逻辑检查覆盖不到的部分。只读，不写任何文件。"""
import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "src"

failures = []


def read(relative: str) -> str:
    path = SRC / relative
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


pending = read("Overview/OverviewRepository.Pending.cs")
maintenance = read("Overview/OverviewMaintenance.cs")
listview = read("Overview/OverviewListView.cs")

# 1. 待应用状态：失效驱动 + 版本号，安全窗口放宽到 2 秒
must(pending, r"PendingViewRevision", "待应用状态暴露失效版本号", "OverviewRepository.Pending.cs")
must(pending, r"PendingViewRevision\+\+", "失效时递增版本号", "OverviewRepository.Pending.cs")
must(pending, r"OverviewMaintenance\.PendingInvalidated\s*=\s*true",
     "失效时请求维护巡检立即开新一轮", "OverviewRepository.Pending.cs")
must(pending, r"_pendingViewUntil\s*=\s*UnityEngine\.Time\.unscaledTime\s*\+\s*2f",
     "兜底窗口为 2 秒", "OverviewRepository.Pending.cs")
must_not(pending, r"_pendingViewUntil\s*=\s*UnityEngine\.Time\.unscaledTime\s*\+\s*\.2f",
         "不得回到 0.2 秒定时重算", "OverviewRepository.Pending.cs")

# 2. 列表侧待应用标记：跟失效版本走，不按 0.19 秒计时
must(listview, r"_markerStateRevision\s*!=\s*_repo\.PendingViewRevision",
     "标记缓存跟随失效版本", "OverviewListView.cs")
must_not(listview, r"Time\.unscaledTime\s*\+\s*\.19f", "不得保留 0.19 秒计时缓存", "OverviewListView.cs")

# 3. 维护巡检：预算 + 固定游标，且不再每帧全量拷贝脏集合
must(maintenance, r"RowBuildBudget\s+_sweepBudget\s*=\s*new\(maxSteps:\s*\d+\)",
     "复用 RowBuildBudget 作为每帧预算", "OverviewMaintenance.cs")
must(maintenance, r"_sweepBudget\.TryTake\(", "每步都过预算", "OverviewMaintenance.cs")
must(maintenance, r"_roundCursor", "周期巡检使用固定游标", "OverviewMaintenance.cs")
must(maintenance, r"while\(_urgentCursor>=_urgent\.Count[^)]*_roundCursor<_round\.Count",
     "空转的轮内条目不计预算", "OverviewMaintenance.cs")
must_not(maintenance, r"_keys\.AddRange", "不得每帧全量拷贝脏集合", "OverviewMaintenance.cs")

# 4. 巡检不得对全体脏项在同一帧内无条件跑完（预算之外的整轮直跑）
must_not(maintenance, r"foreach\(var item in Checks\)\{Dirty\[item\.Key\]=item\.Value;refresh",
         "不得绕过预算直接刷新", "OverviewMaintenance.cs")

# 5. 本轮不许新增性能采样/监控钩子（诊断统一归 FrameDoctor）。
#    只查本轮新增逻辑所在的两个文件：OverviewListView.cs 本就有其它日志，不在此列。
for name, text in (("OverviewMaintenance.cs", maintenance),
                   ("OverviewRepository.Pending.cs", pending)):
    must_not(text, r"\b(LogDebug|LogInfo|LogWarning|LogError)\s*\(", "新增了日志/采样", name)

if failures:
    print("源码断言失败：")
    for item in failures:
        print("  FAIL  " + item)
    sys.exit(1)

print("源码断言全部通过（5 组）")
