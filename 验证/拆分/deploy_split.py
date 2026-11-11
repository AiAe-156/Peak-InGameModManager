# -*- coding: utf-8 -*-
"""IGMM 1.0.0 拆分首发部署（用户已要求拆分，长期部署授权见项目 AGENTS.md）。
与以往部署不同：现装目录里是旧版 ModConfigEnhance.dll（GUID com.aiae.modconfigenhance），新版叫 InGameModManager.dll。
两个 DLL 同时留在 plugins 会被一起加载，所以：
  1. 复查进程（PEAK / 新旧助手 / 进度窗）与部署路径检查
  2. 整目录备份现装 → 备份/部署前_拆分1.0.0_<时间>/installed
  3. 新目录 plugins/0基础模组/[界面]游戏内模组管理器-InGameModManager/ 按发行清单写入
  4. 逐文件存在与大小核对、助手 --protocol 校验
  5. 旧目录整体移到备份（不删）；失败则删新目录、旧目录原样
  6. 改 csproj 的 DeployDir 到新目录（由本脚本完成，并写进 result.json）
不碰：config/、BepInEx/MCE（插件首次启动自己迁移）、core、PEAK_Data。
用法：python -I deploy_split.py"""
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

PROJECT = Path(__file__).resolve().parents[2]
LABEL = "拆分1.0.0"
STAMP = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
GUARD = ("PEAK.exe", "MCE.UpdateWorker.exe", "MCE.Desktop.exe", "IGMM.UpdateWorker.exe", "IGMM.Desktop.exe")
NEW_NAME = "[界面]游戏内模组管理器-InGameModManager"


def csproj(name):
    text = (PROJECT / "ModConfigEnhance.csproj").read_text(encoding="utf-8-sig")
    m = re.search(rf"<{name}>(.*?)</{name}>", text, re.S)
    if not m:
        raise SystemExit("csproj 缺少 " + name)
    return html.unescape(m.group(1).strip())


def running():
    out = subprocess.run(["tasklist"], capture_output=True, text=True, errors="replace").stdout
    names = {line.split()[0].lower() for line in out.splitlines() if line.strip()}
    return [g for g in GUARD if g.lower() in names]


def main():
    version = json.loads((PROJECT / "manifest.json").read_text(encoding="utf-8-sig"))["version_number"]
    game = Path(csproj("GameDir").rstrip("\\/"))
    plugins = game / "BepInEx" / "plugins"
    old = Path(csproj("DeployDir").replace("$(PluginsDir)", str(plugins) + os.sep).rstrip("\\/"))
    new = old.parent / NEW_NAME
    out = PROJECT / "验证" / "拆分" / f"deployment_{STAMP}"
    out.mkdir(parents=True, exist_ok=True)
    report = {"Status": "failed", "StartedUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(), "Version": version,
              "OldTarget": str(old), "NewTarget": str(new), "Backup": "", "Archive": "", "Files": 0, "Processes": running(), "Error": ""}

    def finish(code):
        (out / "result.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({k: report[k] for k in ("Status", "Version", "NewTarget", "Backup", "Archive", "Files", "Error")}, ensure_ascii=False, indent=2))
        return code

    if report["Processes"]:
        report["Error"] = "进程在运行，停止部署：" + ", ".join(report["Processes"]); return finish(1)
    check = subprocess.run([sys.executable, "-I", str(PROJECT.parent / "_tools" / "check_deploy_dir.py"), str(PROJECT / "ModConfigEnhance.csproj")], capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(check.stdout.strip())
    if check.returncode != 0:
        report["Error"] = "部署路径检查不通过：\n" + check.stdout; return finish(1)
    if not old.is_dir():
        report["Error"] = "现装目录不存在：" + str(old); return finish(1)
    if new.exists():
        report["Error"] = "新目录已存在，停止（请先确认）：" + str(new); return finish(1)

    listing = PROJECT / "bin" / "Release" / "netstandard2.1" / "release-files.txt"
    payload = []
    for line in listing.read_text(encoding="utf-8-sig").splitlines():
        if line.strip():
            source, _, relative = line.partition("|")
            if not Path(source).is_file():
                raise SystemExit("发行清单文件缺失：" + source)
            payload.append((Path(source), new / relative, relative))
    report["Files"] = len(payload)

    backup = PROJECT / "备份" / f"部署前_{LABEL}_{STAMP}" / "installed"
    report["Backup"] = str(backup)
    shutil.copytree(old, backup)

    try:
        late = running()
        if late:
            raise RuntimeError("复制前发现进程在运行：" + ", ".join(late))
        for source, destination, _ in payload:
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, destination)
        bad = [r for s, d, r in payload if not d.is_file() or d.stat().st_size != s.stat().st_size]
        if bad:
            raise RuntimeError("逐文件核对不符：" + ", ".join(bad[:5]))
        worker = new / "Tools" / "Updater" / "IGMM.UpdateWorker.exe"
        probe = subprocess.run([str(worker), "--protocol"], capture_output=True, text=True, errors="replace", timeout=60)
        report["Protocol"] = probe.stdout.strip()
        if probe.returncode != 0 or "protocol=2" not in probe.stdout:
            raise RuntimeError("助手协议校验失败：" + (probe.stdout + probe.stderr).strip())
        # 旧目录整体移走（已有完整备份；移走而非删除，便于手工核对）
        retired = PROJECT / "备份" / f"部署前_{LABEL}_{STAMP}" / "retired-folder"
        shutil.move(str(old), str(retired))
        report["RetiredFolder"] = str(retired)
    except Exception as error:
        shutil.rmtree(new, ignore_errors=True)
        if not old.exists() and backup.exists():
            shutil.copytree(backup, old)
        report["Error"] = f"部署失败并已回滚：{error}"
        return finish(1)

    # DeployDir 跟到新目录（分区规则 6.2.1：目录变了必须同步，否则下次构建装出第二份）
    path = PROJECT / "ModConfigEnhance.csproj"
    text = path.read_text(encoding="utf-8-sig")
    old_line = re.search(r"<DeployDir>.*?</DeployDir>", text).group(0)
    new_line = "<DeployDir>$(PluginsDir)" + str(new.relative_to(plugins)) + "\\</DeployDir>"
    path.write_text(text.replace(old_line, new_line), encoding="utf-8")
    report["DeployDir"] = new_line

    (PROJECT / "发布").mkdir(exist_ok=True)
    archive = PROJECT / "发布" / f"InGameModManager_{version}_{STAMP}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for source, _, relative in payload:
            bundle.write(source, relative.replace("\\", "/"))
    report["Archive"] = str(archive)
    report["Status"] = "completed"
    return finish(0)


if __name__ == "__main__":
    raise SystemExit(main())
