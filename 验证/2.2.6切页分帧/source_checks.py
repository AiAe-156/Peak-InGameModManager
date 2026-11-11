#!/usr/bin/env python3
"""2.2.6 切页进入分帧的源码契约断言。

沿用 2.2.5 的只读源码断言方式：改动落在 MonoBehaviour / UIPage 深层类型上，
没有可单独编译的纯逻辑单元。只读，不写任何文件。
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

# ① 首次进入的重活成为可分帧的独立一段
enter = body(source, "internal IEnumerator EnterSteps()")
must(source, r"internal IEnumerator EnterSteps\(\)", "新增 EnterSteps")
for name, pattern in (
    ("纸面样式解析", r"ModConfigPaperStyle\.Resolve\(_parent\.transform\);"),
    ("扫页面树套样式", r"OverviewPaperPanel\.Refresh\(transform\);"),
    ("页面准备（含仓库/列表重载）", r"PrepareCachedPage\(\);"),
    ("配置观察者", r"ObserveProfileParameters\(\);"),
):
    must(enter, pattern, f"{name} 进入分帧段")
must(enter, r"_entryPrepared = true;", "分帧段结束时置就绪标记")
yields = enter.count("yield return null;")
ran.append("进入分帧段至少 3 个让步点")
if yields < 3:
    failures.append(f"EnterSteps 让步点只有 {yields} 处，少于 3 处")

# ② OnPageEnter 只在首次跳过，后续进入必须照旧执行
enter_page = body(source, "public override void OnPageEnter()")
must(enter_page, r"if\(_entryPrepared\) _entryPrepared=false;", "首次进入消费就绪标记")
must(enter_page, r"ModConfigPaperStyle\.Resolve\(_parent\.transform\); OverviewPaperPanel\.Refresh\(transform\);", "后续进入仍自行套样式")
must(enter_page, r"PrepareCachedPage\(\);", "后续进入仍自行准备页面")
must(enter_page, r"ObserveProfileParameters\(\);", "后续进入仍自行挂观察者")
must(enter_page, r"RefreshHeader\(\);", "页头重排仍在进入时执行（需要已激活的宽度）")
must(source, r"private bool _entryPrepared;", "新增就绪标记字段")

# ③ 段序为 建壳 → 绑定 → 首次准备 → 切页
step = body(source, "private void StepOpen()")
order = (step.find("_stage==0"), step.find("_stage==1"), step.find("_stage==2"), step.find("FinishOpen();"))
ran.append("段序为 建壳 → 绑定 → 首次准备 → 切页")
if order[0] < 0 or not (order[0] < order[1] < order[2] < order[3]):
    failures.append(f"段序不对：{order}")
must(step, r"_shell=_page\.EnterSteps\(\);", "第三段走 EnterSteps")
must(step, r"_stage=2;return;", "绑定段跑完下一帧才开准备段")

# ④ 预算与段外行为不得变，也不得新增采样/监控钩子
must(source, r"for\(int steps=0;steps<8 && _shell!=null;steps\+\+\)", "每帧最多 8 步的上限未变")
must(source, r"Frequency>=4\)break;", "每帧约 4ms 的预算未变")
for label, chunk in (("EnterSteps", enter), ("OnPageEnter", enter_page)):
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

print(f"2.2.6 源码断言全部通过（5 组，{len(ran)} 项）")
