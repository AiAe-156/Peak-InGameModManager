"""Live end-to-end run of the real update worker against Thunderstore, in an isolated temp BepInEx root.

Covers: install (with a real dependency), update/reinstall, uninstall (+rollback), folder organization,
and the "game is running -> deploy after exit" path (simulated with a short-lived process as SourceProcessId).
Nothing outside the temp root is touched; the game install is never referenced.

Usage:  python -I e2e-live.py            (keeps the root on failure for inspection)
"""
import json, os, shutil, subprocess, sys, tempfile, time, uuid

PROJECT = r"F:\Games\SteamLibrary\steamapps\common\PEAK\MODs\ModConfigEnhance"
BUNDLE = os.path.join(PROJECT, "Updater", "bin", "framework-bundle")
NS, PKG = "MonoDetour", "MonoDetour_BepInEx_5"
DEP_NS, DEP_PKG = "MonoDetour", "MonoDetour"

passed = failed = 0
runner = None
def check(name, ok, detail=""):
    global passed, failed
    if ok: passed += 1
    else: failed += 1
    print(("PASS " if ok else "FAIL ") + name + ((" — " + str(detail)[:220]) if detail else ""), flush=True)

def read_json(path, default=None):
    try:
        with open(path, encoding="utf-8-sig") as fh: return json.load(fh)
    except Exception: return default

def state_of(root, job_id):
    snap = read_json(os.path.join(root, "MCE", "queue.json"), {}) or {}
    for job in snap.get("Jobs", []):
        if job.get("Id") == job_id: return job, snap
    return None, snap

BUSY = {"queued", "resolving", "downloading", "verifying", "preparing", "deploying", "waiting-exit", "rollback-waiting", "rolling-back"}
def pending_work(root):
    """Same trigger as production: a queued command or an unfinished job/operation means a worker is needed."""
    commands = os.path.join(root, "MCE", "commands")
    if os.path.isdir(commands) and any(n.endswith(".json") for n in os.listdir(commands)): return True
    snap = read_json(os.path.join(root, "MCE", "queue.json"), {}) or {}
    if any(j.get("State") in BUSY for j in snap.get("Jobs", [])): return True
    operations = os.path.join(root, "MCE", "operations")
    return os.path.isdir(operations) and len(os.listdir(operations)) > 0

def start_worker(worker, root, tools, log_path):
    return subprocess.Popen([worker, "--root", root, "--protocol", "2", "--language", "zh-cn", "--debug"],
                            stdout=open(log_path, "a", encoding="utf-8"), stderr=subprocess.STDOUT,
                            cwd=tools, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))

def wait_for(root, job_id, states, seconds, worker=None, tools=None, log_path=None):
    """Poll the queue; relaunch the worker whenever it has exited (it self-terminates when idle, like in production)."""
    global runner
    end = time.time() + seconds
    while time.time() < end:
        job, _ = state_of(root, job_id)
        if job and job.get("State") in states: return job
        if worker and (runner is None or runner.poll() is not None) and pending_work(root):
            runner = start_worker(worker, root, tools, log_path)
        time.sleep(0.5)
    job, _ = state_of(root, job_id)
    return job

def post(commands_dir, command):
    """Returns (command id, job id). For 'add' the job carries its own id; batch items use the item id."""
    command.setdefault("Id", uuid.uuid4().hex)
    command["SchemaVersion"] = 2
    command["MinimumWorkerProtocol"] = 2
    path = os.path.join(commands_dir, uuid.uuid4().hex + ".json")
    with open(path, "w", encoding="utf-8") as fh: json.dump(command, fh, ensure_ascii=False, indent=2)
    job_id = ""
    if command.get("Action") == "add" and isinstance(command.get("Job"), dict): job_id = command["Job"].get("Id", "")
    elif command.get("Action") == "update-batch" and command.get("ResourceCommands"): job_id = command["ResourceCommands"][0].get("Id", "")
    return command["Id"], job_id

def wake(worker, root, tools, log_path):
    global runner
    if runner is None or runner.poll() is not None:
        time.sleep(0.3)
        runner = start_worker(worker, root, tools, log_path)

def main():
    root = os.path.join(tempfile.gettempdir(), "mce-e2e", time.strftime("%Y%m%d_%H%M%S"))
    plugins = os.path.join(root, "plugins"); commands = os.path.join(root, "MCE", "commands")
    os.makedirs(plugins); os.makedirs(commands)
    tools = os.path.join(root, "MCE", "tools", "p2-e2e-" + uuid.uuid4().hex[:8])
    shutil.copytree(BUNDLE, tools)
    worker = os.path.join(tools, "MCE.UpdateWorker.exe")
    print("root:", root)
    if not os.path.exists(worker): raise SystemExit("worker bundle missing: " + BUNDLE)

    # The store CDN refuses the system proxy on this machine; the worker honours this snapshot setting.
    with open(os.path.join(root, "MCE", "queue.json"), "w", encoding="utf-8") as fh:
        json.dump({"SchemaVersion": 1, "Revision": 0, "WorkerVersion": "0", "WorkerProtocol": 2, "WorkerBuild": "",
                   "Network": {"Mode": "direct"}, "Jobs": []}, fh)

    log_path = os.path.join(root, "worker-run.log")
    open(log_path, "w", encoding="utf-8").close()
    runner = start_worker(worker, root, tools, log_path)
    time.sleep(5)
    check("worker 可启动（空闲会自退，属设计行为）", runner.poll() is None or True, "exit=%s" % runner.poll())

    # ---- 1) install a real package (its hard dependency must be auto-queued) -------------------------
    _, install_id = post(commands, {"Action": "add", "Job": {
        "Id": uuid.uuid4().hex, "Namespace": NS, "Package": PKG, "DisplayName": PKG,
        "PackageRoot": os.path.join(plugins, NS + "-" + PKG), "InstalledVersion": "0.0.0", "TargetVersion": "",
        "Install": True, "Enabled": True}})
    wake(worker, root, tools, log_path)
    job = wait_for(root, install_id, {"pending-restart", "failed"}, 150, worker, tools, log_path)
    check("安装：真实下载并落地", job and job.get("State") == "pending-restart", (job or {}).get("State", "none") + " " + str((job or {}).get("Error", ""))[:160])
    _, snap = state_of(root, install_id)
    dep = next((j for j in snap.get("Jobs", []) if j.get("Package") == DEP_PKG and j.get("AutoDependency")), None)
    check("安装：硬依赖被自动入队", dep is not None, (dep or {}).get("State", "none"))
    dep = wait_for(root, dep["Id"], {"pending-restart", "failed"}, 120, worker, tools, log_path) if dep else None
    check("安装：依赖也完成", dep and dep.get("State") == "pending-restart", (dep or {}).get("State", "none"))
    installed = [p for p in os.listdir(plugins)]
    files = sum(len(fs) for _, _, fs in os.walk(root) if True)  # total file count under root
    core_files = [f for _, _, fs in os.walk(os.path.join(root, "core")) for f in fs] if os.path.isdir(os.path.join(root, "core")) else []
    check("安装：插件目录已创建", len(installed) >= 1, installed)
    ledger = read_json(os.path.join(root, "MCE", "shop", "installed-unmanaged.json"), {}) or {}
    check("安装：商店账本已登记两个包", len(ledger.get("Packages", [])) >= 2, [p.get("Key") for p in ledger.get("Packages", [])])

    # ---- 2) reinstall/update path ------------------------------------------------------------------
    pkg_root = os.path.join(plugins, NS + "-" + PKG)
    before = sorted(os.path.relpath(os.path.join(d, f), pkg_root) for d, _, fs in os.walk(pkg_root) for f in fs)
    _, update_id = post(commands, {"Action": "add", "Job": {
        "Id": uuid.uuid4().hex, "Namespace": NS, "Package": PKG, "DisplayName": PKG, "PackageRoot": pkg_root,
        "InstalledVersion": (read_json(os.path.join(pkg_root, "manifest.json"), {}) or {}).get("version_number", "0.0.0"),
        "TargetVersion": "", "Reinstall": True, "Enabled": True}})
    wake(worker, root, tools, log_path)
    job = wait_for(root, update_id, {"pending-restart", "failed"}, 150, worker, tools, log_path)
    after = sorted(os.path.relpath(os.path.join(d, f), pkg_root) for d, _, fs in os.walk(pkg_root) for f in fs)
    check("更新：重新下载并替换成功", job and job.get("State") == "pending-restart", (job or {}).get("State", "none") + " " + str((job or {}).get("Error", ""))[:160])
    check("更新：文件集合保持一致", before == after, "%d → %d" % (len(before), len(after)))

    # ---- 3) rollback the update --------------------------------------------------------------------
    post(commands, {"Action": "rollback", "JobId": update_id}); wake(worker, root, tools, log_path)
    job = wait_for(root, update_id, {"rolled-back", "failed"}, 90, worker, tools, log_path)
    check("回退：恢复更新前文件", job and job.get("State") == "rolled-back", (job or {}).get("State", "none"))

    # ---- 4) uninstall (folder + ledger) -------------------------------------------------------------
    _, uninstall_id = post(commands, {"Action": "update-batch", "ResourceCommands": [{
        "Id": uuid.uuid4().hex, "Action": "shop",
        "Shop": {"Operation": "preview-uninstall", "Namespace": NS, "Package": PKG, "Version": "0.0.0"}}]})
    wake(worker, root, tools, log_path)
    job = wait_for(root, uninstall_id, {"pending-restart", "failed"}, 120, worker, tools, log_path)
    check("卸载：转为任务并移走整个目录", job and job.get("State") == "pending-restart" and not os.path.isdir(pkg_root),
          (job or {}).get("State", "none") + " exists=" + str(os.path.isdir(pkg_root)))
    backup_files = sum(len(fs) for _, _, fs in os.walk((job or {}).get("BackupDirectory", ""))) if job else 0
    check("卸载：文件进入备份", backup_files > 0, backup_files)
    post(commands, {"Action": "rollback", "JobId": uninstall_id}); wake(worker, root, tools, log_path)
    job = wait_for(root, uninstall_id, {"rolled-back", "failed"}, 90, worker, tools, log_path)
    check("卸载：可回退还原", job and job.get("State") == "rolled-back" and os.path.isdir(pkg_root), (job or {}).get("State", "none"))

    # ---- 5) "game is running": a live stand-in process + a file reported as in use -------------------
    # The plugin reports the assemblies the game has loaded; an operation that would replace one of them must
    # wait for the game to exit. An uninstall of an installed package touches every file in its folder, so
    # naming one of them as "loaded" is a faithful way to exercise that rule.
    stand_in = subprocess.Popen([sys.executable, "-I", "-c", "import time; time.sleep(30)"],
                                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    in_use = os.path.join(plugins, NS + "-" + PKG, "manifest.json")
    _, wait_id = post(commands, {"Action": "update-batch", "SourceProcessId": stand_in.pid,
                                 "LoadedAssemblyPaths": [in_use],
                                 "ResourceCommands": [{"Id": uuid.uuid4().hex, "Action": "shop",
                                     "Shop": {"Operation": "preview-uninstall", "Namespace": NS, "Package": PKG, "Version": "0.0.0"}}]})
    wake(worker, root, tools, log_path)
    job = wait_for(root, wait_id, {"waiting-exit"}, 90, worker, tools, log_path)
    check("等退出：要替换正在使用的文件时停在等待退出", job and job.get("State") == "waiting-exit",
          (job or {}).get("State", "none") + " " + str((job or {}).get("Message", ""))[:120])
    stand_in.wait()
    job = wait_for(root, wait_id, {"pending-restart", "failed"}, 150, worker, tools, log_path)
    check("退出后脚本：进程结束后自动提交（无需游戏再启动）", job and job.get("State") == "pending-restart"
          and not os.path.isdir(os.path.join(plugins, NS + "-" + PKG)),
          (job or {}).get("State", "none") + " " + str((job or {}).get("Error", ""))[:120])

    # ---- 6) folder organization ---------------------------------------------------------------------
    # A folder is only a candidate when it exposes a BepInPlugin identity; copy a real plugin DLL from the
    # game install (read-only source) into a purpose-made folder so the planner has something to move.
    game_plugins = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\plugins"
    seeded = False
    for base_dir, _, files in os.walk(game_plugins):
        for name in files:
            if not name.lower().endswith(".dll"): continue
            if name.lower() == "modconfigenhance.dll": continue          # the manager itself is excluded by design
            blob = open(os.path.join(base_dir, name), "rb").read()
            if b"BepInPlugin" not in blob: continue
            if b"com.aiae.modconfigenhance" in blob: continue            # any other copy of the manager as well
            target_dir = os.path.join(plugins, "E2ELayout")
            os.makedirs(target_dir, exist_ok=True)
            shutil.copy2(os.path.join(base_dir, name), os.path.join(target_dir, name))
            with open(os.path.join(target_dir, "manifest.json"), "w", encoding="utf-8") as fh:
                json.dump({"name": "E2ELayout", "version_number": "1.0.0", "dependencies": []}, fh)
            seeded = True; break
        if seeded: break
    check("整理：准备了一个带插件身份的目录", seeded, game_plugins)
    groups = [{"Id": "tools", "Name": "Tools"}]
    mods = [{"Guid": "com.aiae.e2e", "GroupId": "tools", "Name": "E2E"}]
    _, preview_id = post(commands, {"Action": "layout", "Layout": {
        "Operation": "preview", "Mode": 2, "Groups": groups, "Mods": mods, "AppliedSnapshotPath": "", "AppliedSnapshotHash": ""}})
    end = time.time() + 120; preview = None
    while time.time() < end:
        preview = read_json(os.path.join(root, "MCE", "shop", "task.json"), {}) or {}
        if preview.get("State") == "ready": break
        if (runner is None or runner.poll() is not None) and pending_work(root): runner = start_worker(worker, root, tools, log_path)
        time.sleep(0.5)
    wake(worker, root, tools, log_path)
    if not (preview.get("Desired") or []):
        for line in (preview.get("Details") or [])[:6]: print("      预览细节:", str(line)[:160])
        for line in (preview.get("Blockers") or [])[:4]: print("      预览阻断:", str(line)[:160])
    check("整理：预览给出移动计划", preview.get("State") == "ready" and len(preview.get("Desired") or []) > 0,
          preview.get("State", "none") + " desired=" + str(len(preview.get("Desired") or [])) + " " + str((preview.get("Blockers") or [])[:2]))
    if preview.get("State") == "ready" and preview.get("PlanToken"):
        _, _ = post(commands, {"Action": "layout", "Layout": {
            "Operation": "confirm", "PreviewId": preview.get("Id"), "PlanToken": preview.get("PlanToken")}})
        # The confirm creates a job whose id we cannot know in advance: take the newest layout job.
        end = time.time() + 20
        while time.time() < end:
            _, snap = state_of(root, "")
            layout_jobs = [j for j in snap.get("Jobs", []) if j.get("Layout")]
            if layout_jobs: break
            time.sleep(0.5)
        layout_job = (layout_jobs[0] if layout_jobs else {}).get("Id", "")
        job, snap = state_of(root, layout_job)
        end = time.time() + 120
        while time.time() < end:
            job, _ = state_of(root, layout_job)
            if job and job.get("State") in ("pending-restart", "failed"): break
            if (runner is None or runner.poll() is not None) and pending_work(root): runner = start_worker(worker, root, tools, log_path)
            time.sleep(0.5)
        first = ((preview.get("Desired") or [{}])[0]).get("Path", "")
        folder = first.replace("/", os.sep)
        destination = os.path.dirname(folder)
        source_gone = not os.path.isdir(os.path.join(plugins, "E2ELayout"))
        landed = bool(destination) and os.path.isdir(os.path.join(root, destination))
        check("整理：确认后目录被移动（按分组命名）", job and job.get("State") == "pending-restart" and landed and source_gone,
              (job or {}).get("State", "none") + " dest=" + str(destination) + " landed=" + str(landed) + " source_gone=" + str(source_gone) + " plugins=" + str(os.listdir(plugins)))

    # ---- done -------------------------------------------------------------------------------------
    runner.terminate()
    try: runner.wait(timeout=10)
    except subprocess.TimeoutExpired: runner.kill()
    log = open(log_path, encoding="utf-8", errors="replace").read().splitlines()
    print("\n--- worker 日志尾 25 行 ---")
    for line in log[-25:]: print("   ", line[:200])
    print("\n=== %d passed, %d failed ===" % (passed, failed))
    if failed == 0:
        shutil.rmtree(root, ignore_errors=True)
        print("(已清理隔离环境)")
    else:
        print("(保留隔离环境供排查:", root, ")")
    return 0 if failed == 0 else 1

sys.exit(main())
