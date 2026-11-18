# -*- coding: utf-8 -*-
"""0.0.2 导出检查：完整包（自带 BepInEx 加载器）模拟夹具。把本目录与 IGMM 的发布策略源码
（PublicationFilePolicy / PublicationScrub / PluginFileNames / ManagerIdentity / PackContracts）复制到暂存目录再 dotnet run。
只读取游戏目录；生成的包、解压出来的「干净游戏目录」和 summary.txt 都在暂存目录。
用法：python -I run.py [游戏根目录（默认环境变量 PEAK_GAME_DIR）] [暂存目录（默认 %TEMP%\\igmm-publish-sim）]
可用环境变量 DOTNET 指定 dotnet 可执行文件。"""
import os, shutil, subprocess, sys
sys.stdout.reconfigure(encoding="utf-8")
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.normpath(os.path.join(HERE, "..", "..", "..", "src"))
GAME = sys.argv[1] if len(sys.argv) > 1 else os.environ.get("PEAK_GAME_DIR", "")
WORK = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.environ.get("TEMP", "."), "igmm-publish-sim")
if not GAME: sys.exit("游戏根目录未指定（参数或 PEAK_GAME_DIR）")
DOTNET = os.environ.get("DOTNET") or "dotnet"
BUILD = WORK + "-build"
if os.path.exists(BUILD): shutil.rmtree(BUILD)
os.makedirs(BUILD)
for name in ("Program.cs", "Stubs.cs", "publish-sim.csproj"): shutil.copy(os.path.join(HERE, name), BUILD)
for rel in ("Overview/PublicationFilePolicy.cs", "Overview/PublicationScrub.cs", "Updating/PluginFileNames.cs", "Updating/ManagerIdentity.cs", "Updating/PackContracts.cs"):
    shutil.copy(os.path.join(SRC, *rel.split("/")), BUILD)
env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
r = subprocess.run([DOTNET, "run", "-c", "Release", "-v:q", "--", GAME, WORK], cwd=BUILD, env=env)
sys.exit(r.returncode)
