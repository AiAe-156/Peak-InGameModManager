"""Re-run the two MonoDetour updates on the REAL install so the duplicate-retire rule cleans the stale copies.

Mirrors what the in-game "update" button does: posts an update-batch with a store preview-install per package.
The same-version install makes the worker take the reinstall path, which re-plans from the current zips and
archives the same-named stale copies left by the previous (folder-namespaced) installation.

Run with the game closed.  python -I rerun-mce-update.py [--dry-run]
"""
import json, os, shutil, subprocess, sys, time, uuid

GAME = r"F:\Games\SteamLibrary\steamapps\common\PEAK"
ROOT = os.path.join(GAME, "BepInEx")
PROJECT = r"F:\Games\SteamLibrary\steamapps\common\PEAK\MODs\ModConfigEnhance"
BUNDLE = os.path.join(PROJECT, "Updater", "bin", "framework-bundle")
PACKAGES = [("MonoDetour", "MonoDetour_BepInEx_5", "0.7.1701"), ("MonoDetour", "MonoDetour", "0.7.1701")]
BUSY = {"queued", "resolving", "downloading", "verifying", "preparing", "deploying", "waiting-exit", "rollback-waiting", "rolling-back"}
DRY = "--dry-run" in sys.argv

def read_json(path, default=None):
    try:
        with open(path, encoding="utf-8-sig") as fh: return json.load(fh)
    except Exception: return default

def jobs():
    return (read_json(os.path.join(ROOT, "MCE", "queue.json"), {}) or {}).get("Jobs", [])

def pending():
    if any(j.get("State") in BUSY for j in jobs()): return True
    commands = os.path.join(ROOT, "MCE", "commands")
    return os.path.isdir(commands) and any(n.endswith(".json") for n in os.listdir(commands))

def start(tools):
    return subprocess.Popen([os.path.join(tools, "MCE.UpdateWorker.exe"), "--root", ROOT, "--protocol", "2",
                             "--language", "zh-cn", "--debug"],
                            stdout=open(os.path.join(PROJECT, "验证", "更新核心重做", "rerun-worker.log"), "a", encoding="utf-8"),
                            stderr=subprocess.STDOUT, cwd=tools,
                            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))

def main():
    running = [p for p in ("PEAK", "MCE.UpdateWorker", "MCE.Desktop")
               if subprocess.run(["powershell", "-NoProfile", "-Command",
                                  "Get-Process -Name %s -ErrorAction SilentlyContinue | Measure-Object | Select-Object -ExpandProperty Count" % p],
                                 capture_output=True, text=True).stdout.strip() not in ("", "0")]
    if running:
        print("拒绝执行：以下进程仍在运行 ->", running); return 1

    stamp = time.strftime("%Y%m%d_%H%M%S")
    tools = os.path.join(ROOT, "MCE", "tools", "p2-rerun-" + stamp)
    shutil.copytree(BUNDLE, tools)
    commands = os.path.join(ROOT, "MCE", "commands"); os.makedirs(commands, exist_ok=True)
    batch = {"Id": uuid.uuid4().hex, "Action": "update-batch", "SchemaVersion": 2, "MinimumWorkerProtocol": 2,
             "ResourceCommands": [{"Id": uuid.uuid4().hex, "Action": "shop",
                                   "Shop": {"Operation": "preview-install", "Namespace": ns, "Package": pkg, "Version": ver}}
                                  for ns, pkg, ver in PACKAGES]}
    print("将要提交的批次:", [(c["Shop"]["Namespace"], c["Shop"]["Package"], c["Shop"]["Version"]) for c in batch["ResourceCommands"]])
    print("工作目录:", tools)
    if DRY:
        print("(dry-run，未写入命令)"); return 0
    with open(os.path.join(commands, uuid.uuid4().hex + ".json"), "w", encoding="utf-8") as fh:
        json.dump(batch, fh, ensure_ascii=False, indent=2)

    runner = start(tools)
    end = time.time() + 420
    while time.time() < end:
        time.sleep(1)
        mine = [j for j in jobs() if j.get("Id") in {c["Id"] for c in batch["ResourceCommands"]}]
        if mine and all(j.get("State") in ("pending-restart", "failed", "stopped", "removed") for j in mine): break
        if runner.poll() is not None and pending(): runner = start(tools)
    runner.terminate()
    try: runner.wait(timeout=8)
    except subprocess.TimeoutExpired: runner.kill()

    mine = {c["Id"]: c["Shop"]["Package"] for c in batch["ResourceCommands"]}
    print("\n--- 结果 ---")
    for j in jobs():
        if j.get("Id") in mine:
            print("  %-24s %-16s %s %s" % (mine[j["Id"]], j.get("State"), (j.get("Message") or "")[:40], (j.get("Error") or "")[:80]))
            for w in (j.get("Warnings") or []): print("      提示:", w[:120])
    return 0

sys.exit(main())
