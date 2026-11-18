#!/usr/bin/env python3
"""整合安装包：本体 + 全部前置依赖（含依赖的依赖）+ BepInEx，解压到 PEAK 游戏文件夹即可用。

只读取：本工程的发行清单、r2modman 缓存里未改动的原包。只写入：发布/ 下的 zip 和本目录的 result.json。
布局与 r2modman 档案一致：包内 plugins/ patchers/ core/ 分别进 BepInEx/<同名>/<Team-Name>/，其余文件进
BepInEx/plugins/<Team-Name>/；BepInExPack 的内容放到压缩包根（即游戏文件夹）。
"""
import argparse
import datetime
import hashlib
import json
import os
import re
import sys
import zipfile
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
CACHE = Path(os.environ["APPDATA"]) / "r2modmanPlus-local" / "PEAK" / "cache"
MANAGER_KEY = "AiAeT-InGameModManager"
BEPINEX = ("BepInEx-BepInExPack_PEAK", "5.4.75301")
BEPINEX_CORE_VERSION = "5.4.23.5"
BEPINEX_CORE_ARCHIVE = PROJECT.parent / "反编译/BepInEx_5.4.23.3_PEAK2.6.b_2026-10-17/验证/升级5.4.23.5_20261017/BepInEx_win_x64_5.4.23.5.zip"
BEPINEX_CORE_SHA256 = "82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4"
PACKAGES = [
    ("PEAKModding-PEAKLib_Core", "1.7.2"),
    ("PEAKModding-PEAKLib_UI", "1.7.2"),
    ("PEAKModding-ModConfig", "1.8.2"),
    ("PEAKModding-SoftDependencyFix", "1.0.0"),
    ("MonoDetour-MonoDetour_BepInEx_5", "0.7.1701"),
    ("MonoDetour-MonoDetour", "0.7.1701"),
]
ROUTED = {"plugins", "patchers", "core", "monomod"}
README_NAME = "安装说明 README.txt"


def version_tuple(text: str):
    return tuple(int(part) for part in text.split("."))


def split_dependency(text: str):
    team_name, _, version = text.rpartition("-")
    return team_name, version


def files_under(root: Path):
    return sorted(p for p in root.rglob("*") if p.is_file())


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="另存完整安装包，不覆盖已有文件")
    parser.add_argument("--bepinex-core", type=Path, default=BEPINEX_CORE_ARCHIVE)
    args = parser.parse_args()
    manifest = json.loads((PROJECT / "manifest.json").read_text(encoding="utf-8-sig"))
    version = manifest["version_number"]
    entries = {}   # zip path -> source file
    listing = []   # (package key, version, website, file count)

    def add(target: str, source: Path, owner: str):
        target = target.replace("\\", "/")
        key = target.lower()
        if key in entries:
            raise SystemExit(f"路径冲突：{target}（{owner} 与 {entries[key][2]}）")
        entries[key] = (target, source, owner)

    # 1. 本体：按发行清单原样放进 plugins/<包键>/
    release_list = PROJECT / "bin" / "Release" / "netstandard2.1" / "release-files.txt"
    own = 0
    for line in release_list.read_text(encoding="utf-8-sig").splitlines():
        if not line.strip():
            continue
        source, _, relative = line.partition("|")
        if not Path(source).is_file():
            raise SystemExit(f"发行清单文件缺失：{source}")
        add(f"BepInEx/plugins/{MANAGER_KEY}/{relative}", Path(source), MANAGER_KEY)
        own += 1
    listing.append((MANAGER_KEY, version, manifest.get("website_url", ""), own))

    # 2. BepInExPack：BepInExPack_PEAK/ 下的内容就是游戏文件夹里的内容
    pack_root = CACHE / BEPINEX[0] / BEPINEX[1]
    pack_manifest = json.loads((pack_root / "manifest.json").read_text(encoding="utf-8-sig"))
    if pack_manifest["version_number"] != BEPINEX[1]:
        raise SystemExit("BepInExPack 缓存版本不符")
    inner = pack_root / "BepInExPack_PEAK"
    count = 0
    for source in files_under(inner):
        add(source.relative_to(inner).as_posix(), source, BEPINEX[0])
        count += 1
    listing.append((BEPINEX[0], BEPINEX[1], pack_manifest.get("website_url", ""), count))

    # Unity 6 requires the upstream log-writer fix. Use the complete matching managed core,
    # while retaining PEAK's dedicated native Doorstop and its Vulkan compatibility options.
    if hashlib.sha256(args.bepinex_core.read_bytes()).hexdigest() != BEPINEX_CORE_SHA256:
        raise SystemExit("BepInEx 官方核心包与已核验的 5.4.23.5 x64 原包不符")
    core_files = {}
    with zipfile.ZipFile(args.bepinex_core) as upstream:
        if upstream.testzip() is not None:
            raise SystemExit("BepInEx 官方核心包校验失败")
        for info in upstream.infolist():
            if not info.is_dir() and info.filename.startswith("BepInEx/core/"):
                relative = info.filename[len("BepInEx/core/"):]
                if "/" in relative or "\\" in relative:
                    raise SystemExit("官方核心包包含意外的子目录")
                target = "BepInEx/core/" + relative
                if target.lower() not in entries:
                    raise SystemExit("官方核心文件不在原启动环境清单中：" + target)
                core_files[target] = upstream.read(info)
    if len(core_files) != 18:
        raise SystemExit("BepInEx 官方核心应包含完整的 18 个文件")
    for target, data in core_files.items():
        entries[target.lower()] = (target, data, "BepInEx-Core")
    listing.append(("BepInEx-Core", BEPINEX_CORE_VERSION,
                    "https://github.com/BepInEx/BepInEx/releases/tag/v" + BEPINEX_CORE_VERSION, len(core_files)))

    # 3. 其余前置：r2modman 的放置规则
    provided = {MANAGER_KEY: version, BEPINEX[0]: BEPINEX[1]}
    required = [(MANAGER_KEY, dep) for dep in manifest.get("dependencies", [])]
    for key, wanted in PACKAGES:
        root = CACHE / key / wanted
        meta = json.loads((root / "manifest.json").read_text(encoding="utf-8-sig"))
        if meta["version_number"] != wanted or not key.endswith("-" + meta["name"]):
            raise SystemExit(f"缓存内容与预期不符：{key} {wanted}")
        provided[key] = wanted
        required += [(key, dep) for dep in meta.get("dependencies", [])]
        count = 0
        for source in files_under(root):
            relative = source.relative_to(root).as_posix()
            head, _, rest = relative.partition("/")
            if rest and head.lower() in ROUTED:
                add(f"BepInEx/{head.lower()}/{key}/{rest}", source, key)
            elif rest and head.lower() == "config":
                add(f"BepInEx/config/{rest}", source, key)
            else:
                add(f"BepInEx/plugins/{key}/{relative}", source, key)
            count += 1
        listing.append((key, wanted, meta.get("website_url", ""), count))

    # 4. 依赖闭包：每一条声明的依赖都必须在包里，且版本不低于要求
    problems = []
    for owner, dependency in required:
        name, minimum = split_dependency(dependency)
        if name not in provided:
            problems.append(f"{owner} 需要 {dependency}，包里没有")
        elif version_tuple(provided[name]) < version_tuple(minimum):
            problems.append(f"{owner} 需要 {dependency}，包里是 {provided[name]}")
    if problems:
        raise SystemExit("依赖不完整：\n" + "\n".join(problems))

    # 5. 安装说明
    rows = "\n".join(f"  {key} {ver}\n      {site}" for key, ver, site, _ in listing)
    readme = f"""游戏内模组管理器（In-Game Mod Manager）{version} 整合安装包
本体、全部前置模组和 BepInEx 都在这个压缩包里，不需要再单独下载任何东西。

【安装】
1. 关闭 PEAK。
2. 把压缩包里的全部内容解压到 PEAK 游戏文件夹（PEAK.exe 所在的文件夹），提示有同名文件时选择覆盖。
   在 Steam 库里右键 PEAK →「管理」→「浏览本地文件」，打开的就是这个文件夹。
3. 启动游戏。设置菜单里出现「模组概览」，就是装好了。

【已经装过 BepInEx 或其他模组】
- 可以直接解压覆盖，已有的模组不会被删除。
- BepInEx\\config\\BepInEx.cfg 和 doorstop_config.ini 会被换成默认内容。改过这两个文件的话，解压时跳过它们。
- 用 r2modman 或 Thunderstore 模组管理器启动游戏的：它们的模组不在游戏文件夹里，这个包对那种方式不起作用。

【系统要求】
Windows 10 / 11（系统自带的 .NET Framework 4.8）。

【卸载】
删除 BepInEx\\plugins\\{MANAGER_KEY} 文件夹。

【包内容】
{rows}
各前置模组来自原包，许可证文件在各自的文件夹里。
BepInEx 核心整套采用官方 {BEPINEX_CORE_VERSION}，修复 Unity 6 日志写入器错误；保留 PEAK 专用启动器。

------------------------------------------------------------

In-Game Mod Manager {version} - all-in-one package
The mod, every required dependency and BepInEx are included. Nothing else needs to be downloaded.

[Install]
1. Close PEAK.
2. Extract everything in this archive into the PEAK game folder (the one that contains PEAK.exe). Overwrite when asked.
   In your Steam library, right-click PEAK > Manage > Browse local files to open that folder.
3. Start the game. A "Mod overview" entry in the Settings menu means it is installed.

[If BepInEx or other mods are already installed]
- Extracting over them is fine; existing mods are not removed.
- BepInEx\\config\\BepInEx.cfg and doorstop_config.ini are replaced with defaults. Skip them if you have edited them.
- If you launch the game through r2modman or the Thunderstore Mod Manager, this archive has no effect: their mods live
  outside the game folder.

[Requirements]
Windows 10 / 11 (.NET Framework 4.8, included with Windows).

[Uninstall]
Delete the BepInEx\\plugins\\{MANAGER_KEY} folder.

[Contents]
{rows}
Dependency mods come from their original packages; licence files are in their own folders.
The complete official BepInEx {BEPINEX_CORE_VERSION} managed core fixes Unity 6 log-writer errors.
The PEAK-specific native bootstrap is retained.
"""
    readme_bytes = b"\xef\xbb\xbf" + readme.replace("\n", "\r\n").encode("utf-8")

    # 6. 写 zip
    out_dir = PROJECT / "发布"
    out_dir.mkdir(exist_ok=True)
    archive = args.output or out_dir / f"InGameModManager_{version}_整合安装包.zip"
    archive = archive.resolve()
    archive.parent.mkdir(parents=True, exist_ok=True)
    if archive.exists():
        raise SystemExit("保留已有安装包，请使用 --output 另存：" + str(archive))
    ordered = sorted(entries.values(), key=lambda item: item[0].lower())
    with zipfile.ZipFile(archive, "x", zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
        bundle.writestr(README_NAME, readme_bytes)
        for target, source, _ in ordered:
            if isinstance(source, bytes):
                bundle.writestr(target, source)
            else:
                bundle.write(source, target)

    # 7. 回读核对：条目数、每个文件的大小与内容哈希
    mismatched = []
    with zipfile.ZipFile(archive) as bundle:
        if bundle.testzip() is not None:
            raise SystemExit("zip 校验失败")
        names = {info.filename: info for info in bundle.infolist()}
        for target, source, _ in ordered:
            info = names.get(target)
            source_bytes = source if isinstance(source, bytes) else source.read_bytes()
            if info is None or info.file_size != len(source_bytes):
                mismatched.append(target)
                continue
            if hashlib.sha256(bundle.read(target)).digest() != hashlib.sha256(source_bytes).digest():
                mismatched.append(target)
        total = len(names)
    if mismatched:
        raise SystemExit("回读不一致：" + ", ".join(mismatched[:10]))

    extensions = {}
    for target, source, _ in ordered:
        ext = Path(target).suffix.lower()
        extensions[ext] = extensions.get(ext, 0) + 1
    report = {
        "Archive": str(archive),
        "Bytes": archive.stat().st_size,
        "Sha256": hashlib.sha256(archive.read_bytes()).hexdigest(),
        "Entries": total,
        "Packages": [{"Key": k, "Version": v, "Files": n} for k, v, _, n in listing],
        "DependenciesChecked": len(required),
        "TopLevel": sorted({t.split("/")[0] for t, _, _ in ordered} | {README_NAME}),
        "Extensions": dict(sorted(extensions.items(), key=lambda item: -item[1])),
        "BuiltAt": datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
    }
    (HERE / "result.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
