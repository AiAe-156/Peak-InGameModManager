# -*- coding: utf-8 -*-
"""IGMM 0.0.2（2026-11-18）从 MCE 验证/快捷键页_20261115/combo-checks/ 搬来，默认指向 IGMM 的 src，另加 IGMM 旧格式迁移检查。
手柄组合键判定夹具：把本目录的 Program.cs / csproj 与 src/GamepadComboRules.cs、src/Overview/OverviewPadSteps.cs、src/Overview/GroupSteps.cs 复制到暂存目录再 dotnet run。
用法：python run.py [src 目录（默认本工程 src）] [暂存目录（默认 %TEMP%\\igmm-combo-checks）]
不在工程目录里生成 bin/obj。"""
import os, shutil, subprocess, sys
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", "..", "..", "src"))
WORK = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.environ.get("TEMP", "."), "igmm-combo-checks")
DOTNET = os.environ.get("DOTNET") or "dotnet"  # 可用环境变量 DOTNET 指定 dotnet 可执行文件
if os.path.exists(WORK): shutil.rmtree(WORK)
os.makedirs(WORK)
for name in ("Program.cs", "combo-checks.csproj"): shutil.copy(os.path.join(HERE, name), WORK)
shutil.copy(os.path.join(SRC, "GamepadComboRules.cs"), WORK)
# 1.1.0 上线前修正（2026-11-16）：概览页手柄的纯逻辑（页签 / 工具行 / 防连发）一起检查。
shutil.copy(os.path.join(SRC, "Overview", "OverviewPadSteps.cs"), WORK)
# 1.1.0 实测修正（2026-11-16）：分组拖放落点、新分组预填名、成员排序键。
shutil.copy(os.path.join(SRC, "Overview", "GroupSteps.cs"), WORK)
print("rules:", os.path.join(SRC, "GamepadComboRules.cs"), "+ Overview/OverviewPadSteps.cs + Overview/GroupSteps.cs")
env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
r = subprocess.run([DOTNET, "run", "-c", "Release", "-v:q"], cwd=WORK, env=env)
sys.exit(r.returncode)
