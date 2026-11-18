# -*- coding: utf-8 -*-
"""MCE 1.1.0 同步：概览页手柄 / 分组纯逻辑夹具。把本目录的 Program.cs / pad-steps.csproj 与 IGMM 的
src/Overview/OverviewPadSteps.cs、src/Overview/GroupSteps.cs 复制到暂存目录再 dotnet run，不在工程目录里生成 bin/obj。
用法：python -I run.py [IGMM 的 src 目录（默认本工程 src）] [暂存目录（默认 %TEMP%\\igmm-pad-steps）]
可用环境变量 DOTNET 指定 dotnet 可执行文件。"""
import os, shutil, subprocess, sys
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.normpath(os.path.join(HERE, "..", "..", "..", "src"))
WORK = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.environ.get("TEMP", "."), "igmm-pad-steps")
DOTNET = os.environ.get("DOTNET") or "dotnet"
if os.path.exists(WORK): shutil.rmtree(WORK)
os.makedirs(WORK)
for name in ("Program.cs", "pad-steps.csproj"): shutil.copy(os.path.join(HERE, name), WORK)
for name in ("OverviewPadSteps.cs", "GroupSteps.cs"): shutil.copy(os.path.join(SRC, "Overview", name), WORK)
env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
r = subprocess.run([DOTNET, "run", "-c", "Release", "-v:q"], cwd=WORK, env=env)
sys.exit(r.returncode)
