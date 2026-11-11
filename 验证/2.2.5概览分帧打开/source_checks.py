#!/usr/bin/env python3
"""2.2.5 概览分帧打开的源码契约断言。

本次改动落在 MonoBehaviour / UIPage 深层类型上（依赖 PEAKLib.UI 与 Unity 运行时），
没有可单独编译的纯逻辑单元，因此与 2.2.4 同样用只读源码断言覆盖。只读，不写任何文件。
"""
import json
import re
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

ROOT = Path(__file__).resolve().parents[2]
CONTROLLER = ROOT / "src" / "Overview" / "OverviewController.cs"

failures = []
ran = []


def must(text: str, pattern: str, name: str):
    ran.append(name)
    if re.search(pattern, text) is None:
        failures.append(f"{name}：找不到 /{pattern}/")


def must_not(text: str, pattern: str, name: str):
    ran.append(name)
    if re.search(pattern, text) is not None:
        failures.append(f"{name}：仍存在 /{pattern}/")


def body(text: str, signature: str) -> str:
    """取某方法体：从签名后到下一个同缩进成员声明之前。"""
    start = text.find(signature)
    if start < 0:
        failures.append(f"缺少方法 {signature}")
        return ""
    rest = text[start + len(signature):]
    stop = re.search(r"\n    (?:internal|private|public|protected|static)[^\n]*\n", rest)
    return rest[: stop.start()] if stop else rest


source = CONTROLLER.read_text(encoding="utf-8")
if not source:
    failures.append("OverviewController.cs 无法读取")

# ① 绑定成为可分帧迭代器，且按建造顺序留出让步点
bind = body(source, "internal IEnumerator BindSteps(OverviewRepository repo)")
must(source, r"internal IEnumerator BindSteps\(OverviewRepository repo\)", "绑定是迭代器")
must(bind, r"if\(_bound\)yield break;", "已是迭代器的提前退出用 yield break")
yields = bind.count("yield return null;")
ran.append("绑定至少 8 步（让步点不少于 7 处）")
if yields < 7:
    failures.append(f"绑定让步点只有 {yields} 处，少于 7 处")
for name, pattern in (
    ("仓库接线、工具入口与悬浮提示", r"hideTip\.Exit = \(\) => _list\?\.HideExternalHint\(\);\s*\n\s*yield return null;"),
    ("页头文本", r"yield return null;\s*\n\s*_packName = OverviewUi\.Text"),
    ("内容区与状态行", r"yield return null;\s*\n\s*_body = OverviewUi\.Rect"),
    ("工具条", r"yield return null;\s*\n\s*BuildUpdateTools\(\);"),
    ("模组列表视图", r"yield return null;\s*\n\s*_list = new OverviewListView"),
    ("快捷键视图", r"yield return null;\s*\n\s*_hotkeys = new OverviewHotkeyView"),
    ("页头重排", r"RefreshHeader\(\);\s*\n\s*yield return null;"),
):
    must(bind, pattern, f"{name} 单列一步")
last_yield = bind.rfind("yield return null;")
bound_at = bind.find("_bound=true;")
ran.append("_bound 在最后一步才置位")
if not (0 <= last_yield < bound_at):
    failures.append("_bound=true 没有排在最后一次让步之后，绑定完成前就会被认为是已绑定")

# ② 旧的同步绑定路径与死代码不得复活
must_not(source, r"BindRepository", "旧的同步绑定方法已移除")
must_not(source, r"internal void Initialize\(UIPage parent, OverviewRepository repo\)", "无调用方的同步初始化已删除")
must_not(source, r"CompleteOpen", "旧的同步完成路径已移除")

# ③ 入口三段各自成段，且共用原有的每帧预算
step = body(source, "private void StepOpen()")
must(source, r"private void StepOpen\(\)", "新增 StepOpen")
must(source, r"private void FinishOpen\(\)", "新增 FinishOpen")
must(source, r"_opening=true;_stage=0;", "打开时从第一段开始")
must(source, r"private int _stage;", "新增段位字段")
must(source, r"StepOpen\(\);return;", "Update 的打开分支改走分段")
must(step, r"_stage==1", "存在第二段（绑定）")
must(step, r"_shell=_page\.BindSteps\(NativeConfigurationDraft\.Repository \?\? new OverviewRepository\(\)\)", "第二段绑定仓库")
order = (step.find("_stage==0"), step.find("_stage==1"), step.find("FinishOpen();"))
ran.append("段序为 建壳 → 绑定 → 切页")
if order[0] < 0 or not (order[0] < order[1] < order[2]):
    failures.append(f"段序不对：{order}")
must(step, r"if\(_shell!=null\) \{ PumpShell\(\);if\(_shell!=null\)return; _stage=1; return; \}", "建壳跑完不再同帧开下一段")
must(source, r"for\(int steps=0;steps<8 && _shell!=null;steps\+\+\)", "每帧最多 8 步的上限未变")
must(source, r"Frequency>=4\)break;", "每帧约 4ms 的预算未变")

# ④ 三段之外不得新增采样/监控钩子（诊断统一归 FrameDoctor）
for label, chunk in (("StepOpen", step), ("FinishOpen", body(source, "private void FinishOpen()")), ("BindSteps", bind)):
    must_not(chunk, r"\b(LogDebug|LogInfo|LogWarning|LogError)\s*\(", f"{label} 未新增日志/采样")

# ⑤ 版本号三处一致
manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
project = re.search(r"<Version>([^<]+)</Version>", (ROOT / "ModConfigEnhance.csproj").read_text(encoding="utf-8")).group(1)
plugin = re.search(r'PluginVersion = "([^"]+)"', (ROOT / "src" / "Plugin.cs").read_text(encoding="utf-8")).group(1)
ran.append("版本号 manifest / csproj / Plugin.cs 一致")
if not (manifest["version_number"] == project == plugin):
    failures.append(f"版本号不一致：manifest={manifest['version_number']} csproj={project} plugin={plugin}")
if not re.fullmatch(r"\d+\.\d+\.\d+", plugin):
    failures.append(f"版本号格式不对：{plugin}")

if failures:
    print("源码断言失败：")
    for item in failures:
        print("  FAIL  " + item)
    sys.exit(1)

print(f"2.2.5 源码断言全部通过（5 组，{len(ran)} 项）")
