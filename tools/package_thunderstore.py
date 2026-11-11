# -*- coding: utf-8 -*-
"""使用项目与部署共用的文件清单生成 Thunderstore ZIP。

默认构建 Release 主插件及使用系统 Framework 4.8 的轻量助手；--no-build 用于已构建产物。
始终禁止部署，已有发布包不覆盖。
商店页面资料放 ZIP 根目录，插件、助手和示例放 BepInEx/plugins/InGameModManager/。
"""
import argparse
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--release", action="store_true", help="兼容原调用方式，默认即 Release")
parser.add_argument("--debug", action="store_true")
parser.add_argument("--check-only", action="store_true", help="只核对清单，不生成发行包")
parser.add_argument("--no-build", action="store_true")
parser.add_argument("--output", type=Path, help="另存发布包，已有文件仍不覆盖")
args = parser.parse_args()
cfg = "Debug" if args.debug else "Release"
manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8-sig"))
version = manifest["version_number"]
if not re.fullmatch(r"\d+\.\d+\.\d+", version) or len(manifest["description"]) > 250:
    sys.exit("发布版本必须为三段数字，商店简介不能超过 250 字符")
plugin_version = re.search(r'PluginVersion\s*=\s*"([^"]+)"', (ROOT / "src/Plugin.cs").read_text(encoding="utf-8-sig")).group(1)
for project in (ROOT / "ModConfigEnhance.csproj", ROOT / "Updater/MCE.UpdateWorker.csproj"):
    if ET.parse(project).findtext(".//Version") != version:
        sys.exit("项目版本与 manifest 不一致：" + str(project))
if plugin_version != version:
    sys.exit("插件声明版本与 manifest 不一致")
out = args.output.resolve() if args.output else ROOT / "发布" / f"InGameModManager_{version}.zip"
partial = out.with_suffix(".zip.partial")
if not args.check_only and (out.exists() or partial.exists()):
    sys.exit("输出已经存在，保留原文件：" + str(out))

def run(*command):
    subprocess.run(command, cwd=ROOT, check=True)

if not args.no_build:
    run("dotnet", "publish", "Updater/MCE.UpdateWorker.csproj", "-c", "Release", "-o", "Updater/bin/framework-bundle", "-p:EnableDeployment=false")
    run("dotnet", "build", "Desktop/MCE.Desktop.csproj", "-c", "Release", "-p:EnableDeployment=false")
    run("dotnet", "build", "ModConfigEnhance.csproj", "-c", cfg, "-p:EnableDeployment=false")
run("dotnet", "msbuild", "ModConfigEnhance.csproj", "-t:CollectReleaseFiles", f"-p:Configuration={cfg}", "-p:EnableDeployment=false", "-nologo", "-v:quiet")

inventory = ROOT / "bin" / cfg / "netstandard2.1/release-files.txt"
files = {}
for line in inventory.read_text(encoding="utf-8-sig").splitlines():
    source, name = line.split("|", 1)
    source = Path(source)
    name = name.replace("\\", "/")
    if PurePosixPath(name).is_absolute() or ".." in PurePosixPath(name).parts or ":" in name:
        sys.exit("文件清单含不安全路径：" + name)
    if name.lower() in files or not source.is_file():
        sys.exit("清单文件重复或不存在：" + name)
    files[name.lower()] = (source, name)
required = {"ingamemodmanager.dll", "markdig.dll", "yamldotnet.dll", "manifest.json", "readme.md", "changelog.md", "icon.png", "license.txt", "third-party-notices.md",
            "tools/updater/igmm.desktop.exe", "tools/updater/igmm.desktop.exe.config", "tools/updater/worker-protocol.manifest", "tools/updater/igmm.updateworker.exe", "tools/updater/mono.cecil.license.txt", "tools/updater/runtime-license.txt", "tools/updater/runtime-third-party-notices.txt"}
if not required.issubset(files):
    sys.exit("发布清单缺少必需文件：" + ", ".join(sorted(required - files.keys())))
for binary, inputs in (
    (files["ingamemodmanager.dll"][0], [ROOT / "ModConfigEnhance.csproj", *ROOT.glob("src/**/*.cs"), *ROOT.glob("Help/**/*.md")]),
    (files["tools/updater/igmm.updateworker.exe"][0], [ROOT / "Updater/MCE.UpdateWorker.csproj", *ROOT.glob("Updater/*.cs"), *ROOT.glob("src/Updating/*.cs")]),
):
    if binary.stat().st_mtime < max(p.stat().st_mtime for p in inputs):
        sys.exit("编译产物早于源码，请重新构建：" + str(binary))
tool_size = sum(source.stat().st_size for source, name in files.values() if name.lower().startswith("tools/updater/"))
if tool_size > 10 * 1024 * 1024:
    sys.exit(f"辅助工具目录超过 10 MiB：{tool_size} bytes")
if args.check_only:
    print(f"Release inventory validated: {len(files)} files, {cfg}, version {version}; no archive created")
    sys.exit(0)
out.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(partial, "x", zipfile.ZIP_DEFLATED) as archive:
    metadata = {"manifest.json", "readme.md", "changelog.md", "icon.png", "license.txt", "third-party-notices.md"}
    for source, name in sorted(files.values(), key=lambda item: item[1].lower()):
        destination = name if name.lower() in metadata else "BepInEx/plugins/InGameModManager/" + name
        archive.write(source, destination)
with zipfile.ZipFile(partial) as archive:
    bad = archive.testzip()
    if bad is not None:
        sys.exit("压缩包校验失败，保留未完成文件：" + bad)
partial.rename(out)
print(f"{out} ({out.stat().st_size} bytes, {len(files)} files, {cfg}, version {version})")
