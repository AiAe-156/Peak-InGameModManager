#!/usr/bin/env python3
"""把当前 2.2.8 发行复制进 r2modman 的 123123 档案（用户指定），用于验证管理器档案布局。
先备份档案里原有的 MCE 包目录，再按发行清单镜像，最后逐文件核对大小。不建 mods.yml 记录。"""
import datetime
import json
import shutil
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

PROJECT = Path(__file__).resolve().parents[2]
PROFILE = Path(r"C:\Users\AiAe\AppData\Roaming\r2modmanPlus-local\PEAK\profiles\123123")
PACKAGE = PROFILE / "BepInEx" / "plugins" / "AiAeT-ModConfigEnhance"
STAMP = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")


def main() -> int:
    version = json.loads((PROJECT / "manifest.json").read_text(encoding="utf-8-sig"))["version_number"]
    if not PACKAGE.is_dir():
        raise SystemExit(f"档案里没有 MCE 包目录：{PACKAGE}")
    release_list = PROJECT / "bin" / "Release" / "netstandard2.1" / "release-files.txt"
    payload = []
    for line in release_list.read_text(encoding="utf-8-sig").splitlines():
        if not line.strip():
            continue
        source, _, relative = line.partition("|")
        source_path = Path(source)
        if not source_path.is_file():
            raise SystemExit(f"发行清单文件缺失：{source_path}")
        payload.append((source_path, PACKAGE / relative, relative))

    backup = PROJECT / "备份" / f"123123档案_MCE包_复制前_{STAMP}"
    shutil.copytree(PACKAGE, backup)
    print(f"备份原有包目录 → {backup}")

    for source, destination, relative in payload:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
    problems = [relative for source, destination, relative in payload
                if not destination.is_file() or destination.stat().st_size != source.stat().st_size]
    if problems:
        print("逐文件核对不符：" + "; ".join(problems))
        return 1
    installed = json.loads((PACKAGE / "manifest.json").read_text(encoding="utf-8-sig"))["version_number"]
    print(f"已复制 {len(payload)} 个文件，档案内 MCE 版本 {installed}（期望 {version}）")
    return 0 if installed == version else 1


if __name__ == "__main__":
    raise SystemExit(main())
