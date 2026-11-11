# -*- coding: utf-8 -*-
"""补做 2.5.5 部署的最后一步：新工程缺 `发布/`，deploy.py 在复制、逐文件核对、助手协议校验都通过后打包时失败（未回滚，现装已是 2.5.5）。
这里只按发行清单打 ZIP、重新核对现装大小，并补写 result.json；不再写现装目录。"""
import datetime
import json
import subprocess
import zipfile
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[2]
STAMP = "20261111_183601"
OUT = PROJECT / "验证" / "2.5.5改名与跳转" / f"deployment_{STAMP}"
TARGET = Path(r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\plugins\0基础模组\ModManager_InGame_2.0.0_20261029_215224")
listing = PROJECT / "bin" / "Release" / "netstandard2.1" / "release-files.txt"

payload = []
for line in listing.read_text(encoding="utf-8-sig").splitlines():
    if line.strip():
        source, _, relative = line.partition("|")
        payload.append((Path(source), TARGET / relative, relative))

bad = [r for s, d, r in payload if not d.is_file() or d.stat().st_size != s.stat().st_size]
worker = TARGET / "Tools" / "Updater" / "MCE.UpdateWorker.exe"
probe = subprocess.run([str(worker), "--protocol"], capture_output=True, text=True, errors="replace", timeout=60)
version = json.loads((TARGET / "manifest.json").read_text(encoding="utf-8-sig"))["version_number"]

(PROJECT / "发布").mkdir(exist_ok=True)
archive = PROJECT / "发布" / f"ModConfigEnhance_{version}_{STAMP}_rename-and-jump.zip"
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
    for source, _, relative in payload:
        bundle.write(source, relative.replace("\\", "/"))

ok = not bad and probe.returncode == 0 and "protocol=2" in probe.stdout and version == "2.5.5"
report = {
    "Status": "completed" if ok else "failed",
    "FinishedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "Version": version, "Deployed": ok, "Target": str(TARGET),
    "Backup": str(PROJECT / "备份" / f"部署前_2.5.5改名与跳转_{STAMP}" / "installed"),
    "Archive": str(archive), "ArchiveBytes": archive.stat().st_size, "Files": len(payload),
    "Protocol": probe.stdout.strip(), "Mismatched": bad,
    "Note": "deploy.py 复制/核对/协议均通过，打包时因新工程缺 发布/ 目录失败；本脚本补打 ZIP 并重新核对后写本文件。",
    "Inventory": [{"Relative": r, "Length": d.stat().st_size} for _, d, r in payload if d.is_file()],
}
OUT.mkdir(parents=True, exist_ok=True)
(OUT / "result.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(report["Status"], report["Files"], len(bad), report["ArchiveBytes"], report["Protocol"])
