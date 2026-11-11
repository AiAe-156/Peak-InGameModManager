#!/usr/bin/env python3
"""2.5.5 部署：读发行清单 → 复查进程 → 完整备份现装 → 复制 → 逐文件核对 → 打包 → 写 result.json。
失败即从备份回滚。不启停游戏进程，不改真实配置/库存/队列。版本号从 manifest.json 读取。"""
import datetime
import html
import json
import os
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")

PROJECT = Path(__file__).resolve().parents[2]
STAMP = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
LABEL = "2.5.5改名与跳转"
GUARD = ("PEAK.exe", "MCE.UpdateWorker.exe", "MCE.Desktop.exe")


def csproj_value(name: str) -> str:
    text = (PROJECT / "ModConfigEnhance.csproj").read_text(encoding="utf-8")
    match = re.search(rf"<{name}>(.*?)</{name}>", text, re.S)
    if match is None:
        raise SystemExit(f"csproj 缺少 {name}")
    return html.unescape(match.group(1).strip())


def running() -> list:
    result = subprocess.run(["tasklist"], capture_output=True, text=True, errors="replace")
    names = {line.split()[0].lower() for line in result.stdout.splitlines() if line.strip()}
    return [name for name in GUARD if name.lower() in names]


def main() -> int:
    manifest = json.loads((PROJECT / "manifest.json").read_text(encoding="utf-8-sig"))
    version = manifest["version_number"]
    game = Path(csproj_value("GameDir").rstrip("\\/"))
    bep = game / "BepInEx"
    deploy = csproj_value("DeployDir").replace("$(PluginsDir)", str(bep / "plugins") + os.sep)
    target = Path(deploy)
    release_list = PROJECT / "bin" / "Release" / "netstandard2.1" / "release-files.txt"
    if not release_list.is_file():
        raise SystemExit("缺少发行清单 release-files.txt")

    report = {"Status": "failed", "StartedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
              "Version": version, "Deployed": False, "Target": str(target), "Backup": "",
              "Archive": "", "Files": 0, "Processes": running(), "Error": "", "Inventory": []}
    out = PROJECT / "验证" / LABEL / f"deployment_{STAMP}"
    out.mkdir(parents=True, exist_ok=True)

    def finish(code: int):
        (out / "result.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({k: report[k] for k in ("Status", "Version", "Target", "Backup", "Archive", "Files", "Error")},
                         ensure_ascii=False, indent=2))
        return code

    if report["Processes"]:
        report["Error"] = "PEAK / MCE.UpdateWorker / MCE.Desktop 正在运行，停止部署。"
        return finish(1)
    if "ModManager_InGame" not in deploy:
        raise SystemExit("部署目标不是既定的现装目录")
    if not target.is_dir():
        raise SystemExit(f"现装目录不存在：{target}")
    # 分区规则 6.2.1：部署前查一次部署目标与现装位置是否一致（目录整理会挪动现装）。
    checker = PROJECT.parent / "_tools" / "check_deploy_dir.py"
    check = subprocess.run([sys.executable, "-I", str(checker), str(PROJECT / "ModConfigEnhance.csproj")],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(check.stdout.strip())
    if check.returncode != 0:
        report["Error"] = "部署目标与现装位置不一致，已中止：\n" + check.stdout
        return finish(1)

    payload = []
    for line in release_list.read_text(encoding="utf-8-sig").splitlines():
        if not line.strip():
            continue
        source, _, relative = line.partition("|")
        source_path = Path(source)
        if not source_path.is_file():
            raise SystemExit(f"发行清单文件缺失：{source_path}")
        payload.append((source_path, target / relative, relative))
    report["Files"] = len(payload)

    backup = PROJECT / "备份" / f"部署前_{LABEL}_{STAMP}" / "installed"
    report["Backup"] = str(backup)
    shutil.copytree(target, backup)

    written = []
    try:
        # The game or a helper may have started while backing up; never copy over files it has loaded.
        late = running()
        if late:
            raise RuntimeError("复制前发现进程在运行：" + ", ".join(late))
        for source, destination, relative in payload:
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, destination)
            written.append((destination, relative))
        mismatched = [(relative, "missing") for destination, relative in written if not destination.is_file()]
        mismatched += [(relative, f"{destination.stat().st_size} != {source.stat().st_size}")
                       for (source, destination, relative) in payload
                       if destination.is_file() and destination.stat().st_size != source.stat().st_size]
        if mismatched:
            raise RuntimeError("逐文件核对不符：" + "; ".join(f"{r}({why})" for r, why in mismatched))
        worker = target / "Tools" / "Updater" / "MCE.UpdateWorker.exe"
        if not worker.is_file():
            raise RuntimeError("部署后缺少更新助手：" + str(worker))
        if True:
            probe = subprocess.run([str(worker), "--protocol"], capture_output=True, text=True, errors="replace", timeout=60)
            report["Protocol"] = probe.stdout.strip()
            if probe.returncode != 0 or "protocol=2" not in probe.stdout:
                raise RuntimeError("助手协议校验失败：" + (probe.stdout + probe.stderr).strip())
    except Exception as error:
        shutil.rmtree(target, ignore_errors=True)
        shutil.copytree(backup, target)
        report["Error"] = f"部署失败并已从备份回滚：{error}"
        return finish(1)

    (PROJECT / "发布").mkdir(exist_ok=True)  # 新工程首次部署时还没有 发布/
    archive = PROJECT / "发布" / f"ModConfigEnhance_{version}_{STAMP}_rename-and-jump.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for source, _, relative in payload:
            bundle.write(source, relative.replace("\\", "/"))
    report["Archive"] = str(archive)
    for _, destination, relative in payload:
        report["Inventory"].append({"Relative": relative, "Length": destination.stat().st_size})
    installed = json.loads((target / "manifest.json").read_text(encoding="utf-8-sig"))
    if installed.get("version_number") != version:
        raise SystemExit(f"部署后清单版本不是 {version}：{installed.get('version_number')}")
    report["Status"] = "completed"
    report["Deployed"] = True
    return finish(0)


if __name__ == "__main__":
    raise SystemExit(main())
