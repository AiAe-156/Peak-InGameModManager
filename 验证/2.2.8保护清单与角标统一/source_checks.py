#!/usr/bin/env python3
"""源码契约断言（2.2.8）：①卸载保护改为固定清单、不再用图遍历；②四类角标统一为同一形状与槽位；
③版本提示里那句重复的「有新版本」不再出现。只读，不写任何文件。"""
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


graph = read("src/Updating/ComponentDependencyGraph.cs")
collector = read("src/Updating/ComponentDependencyCollector.cs")
summary = read("src/Overview/DiagnosticSummary.cs")

# 1. 保护 = 固定清单，不再是图遍历结果
must(graph, r"MceRequiredIdentities", "固定保护清单", "src/Updating/ComponentDependencyGraph.cs")
must(graph, r"com\.github\.PEAKModding\.PEAKLib\.ModConfig", "清单含 ModConfig 宿主", "src/Updating/ComponentDependencyGraph.cs")
must(graph, r"com\.github\.PEAKModding\.PEAKLib\.Core", "清单含 PEAKLib.Core", "src/Updating/ComponentDependencyGraph.cs")
must(graph, r"MceRequiredBy\(target\)", "保护取自清单", "src/Updating/ComponentDependencyGraph.cs")
must_not(graph, r"paths\.TryGetValue\(target\.Id", "不再用闭包判定保护", "src/Updating/ComponentDependencyGraph.cs")

# 1b. 不再伪造「可能的提供者」硬边
must_not(collector, r'"possible provider for unresolved"', "不再伪造 possible provider 硬边", "src/Updating/ComponentDependencyCollector.cs")

# 1c. 卸载降级为提示：受保护组件仍可卸载，只是先确认
must(summary, r"capability\.CanUninstall && capability\.ProtectionPath\.Count == 0", "2.3.9：白名单内不可卸载", "src/Overview/DiagnosticSummary.cs")
must(summary, r"不能停用或卸载", "2.3.9：文案为「不能停用或卸载」", "src/Overview/DiagnosticSummary.cs")
terms = read("src/Loc.InlineTerms.cs")
must(terms, r"卸载前会再次确认", "词条表同步", "src/Loc.InlineTerms.cs")
must_not(terms, r"仅可更新", "旧词条已移除", "src/Loc.InlineTerms.cs")
must_not(read("src/Overview/SourceUi.cs"), r"UninstallProtection", "2.3.9：不再有「确认后卸载白名单」路径", "src/Overview/SourceUi.cs")
must(read("src/Overview/ComponentDetailsView.cs"), r"Uninstall a base component", "2.3.9：基础组件卸载额外提醒", "src/Overview/ComponentDetailsView.cs")

# 2. 角标统一：一个几何来源、四类共用
chip = read("src/Overview/StatusChip.cs")
must(chip, r"Size = 8, Step = 10, RightInset = 2, TopInset = 3", "统一尺寸与间距（2.3.6 起间距 10、按优先级紧排）", "src/Overview/StatusChip.cs")
must(chip, r"UnreadSlot = 0, UpdateSlot = 1, FirstPendingSlot = 2", "统一槽位", "src/Overview/StatusChip.cs")
must(chip, r"RoundedChip", "统一为圆角方形", "src/Overview/StatusChip.cs")
update = read("src/Overview/AvailableUpdateDot.cs")
must(update, r"StatusChip\.Create\(host", "有新版本走共享槽位", "src/Overview/AvailableUpdateDot.cs")
must(update, r"StatusChip\.CreateAt\(row", "版本列角标共用同一形状", "src/Overview/AvailableUpdateDot.cs")
must_not(update, r"AvailableUpdateCircle", "圆形实现已删除", "src/Overview/AvailableUpdateDot.cs")
must(read("src/Overview/ChangeLedger.cs"), r"StatusChip\.Create\(MarkerHost", "未读变动走共享槽位", "src/Overview/ChangeLedger.cs")
pending = read("src/Overview/ProfilePendingMarkers.cs")
must(pending, r"_firstSlot", "待处理角标可加入共享车道", "src/Overview/ProfilePendingMarkers.cs")
must(pending, r"StatusChip\.Create\(transform, i == 0 \? StatusChip\.FirstPendingSlot : StatusChip\.RestartSlot", "待处理角标用共享车道优先级（2.3.6）", "src/Overview/ProfilePendingMarkers.cs")
view = read("src/Overview/OverviewListView.cs")
must(view, r"firstSlot: StatusChip\.FirstPendingSlot", "行内传入槽位基址", "src/Overview/OverviewListView.cs")
must(view, r"StatusChip\.Reserve\(laneChips\)", "标题预留按实际角标数计算", "src/Overview/OverviewListView.cs")
for relative in ("src/Overview/OverviewListView.cs", "src/Overview/OverviewListView.Infrastructure.cs"):
    must_not(read(relative), r"NameReserve", "旧固定预留已移除", relative)

# 3. 版本提示不再重复「有新版本」
must(view, r"check == Loc\.T\(\"Update available\"", "重复的检查消息被识别并丢弃", "src/Overview/OverviewListView.cs")
must(view, r"string\.Join\(\"\\n\", lines\)", "按行拼装提示", "src/Overview/OverviewListView.cs")

# 4. 版本号一致
for relative in ("ModConfigEnhance.csproj", "Updater/MCE.UpdateWorker.csproj", "Desktop/MCE.Desktop.csproj"):
    must(read(relative), r"<Version>2\.\d+\.\d+</Version>", "版本号为 2.2.x（不再钉死 2.2.8）", relative)
must(read("manifest.json"), r'"version_number": "2\.\d+\.\d+"', "清单版本为 2.2.x", "manifest.json")

if failures:
    print("FAIL")
    for item in failures:
        print(" - " + item)
    sys.exit(1)
print("PASS 源码契约断言 4 组")
