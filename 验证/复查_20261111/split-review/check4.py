import os, json
BS = chr(92)
rt = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\MCE"
cfg = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\config\ModConfigEnhance"
out = open("check4.txt", "w", encoding="utf-8")
lib = "SteamLibrary" + BS*2 + "steamapps" + BS*2 + "common" + BS*2 + "PEAK" + BS*2 + "BepInEx" + BS*2 + "MCE"
q = json.load(open(os.path.join(rt, "queue.json"), encoding="utf-8"))
qids = {j["Id"]: j.get("State") for j in q.get("Jobs", [])}
h = json.load(open(os.path.join(rt, "history.json"), encoding="utf-8"))
hids = {j["Id"]: j.get("State") for j in h}
for j in sorted(os.listdir(os.path.join(rt, "jobs"))):
    p = os.path.join(rt, "jobs", j, "transaction.json")
    if not os.path.exists(p): continue
    t = open(p, encoding="utf-8").read()
    if lib in t:
        tj = json.loads(t)
        out.write(f"{j} phase={tj.get('Phase')} libRefs={t.count(lib)} inQueue={qids.get(j)} inHistory={hids.get(j)} backupsDir={os.path.isdir(os.path.join(rt,'backups',j))}\n")
# config JSON referencing skipped dirs
for needle in ["output" + BS*2 + "packs", BS*2 + "exports" + BS*2, "output/packs"]:
    hits = []
    for dp, dns, fns in os.walk(cfg):
        rel = os.path.relpath(dp, cfg).replace(BS, "/")
        if rel.startswith("output/packs") or rel.startswith("exports"): continue
        for f in fns:
            if f.endswith(".json"):
                pp = os.path.join(dp, f); tt = open(pp, encoding="utf-8", errors="replace").read()
                if needle in tt: hits.append((os.path.relpath(pp, cfg), tt.count(needle)))
    out.write(f"config refs to {needle!r}: {hits[:10]}\n")
# non-json text files under runtime that embed runtime root
for dp, dns, fns in os.walk(rt):
    rel = os.path.relpath(dp, rt).replace(BS, "/")
    if rel.split("/")[0] in ("backups", "downloads", "tools", "layout-backups"): continue
    for f in fns:
        if f.endswith(".json"): continue
        pp = os.path.join(dp, f)
        try:
            if os.path.getsize(pp) > 8 * 1024 * 1024 or f.lower().endswith((".dll",".exe",".zip",".bundle",".png",".mp3",".wav")): continue
        except Exception: continue
        try: tt = open(pp, encoding="utf-8", errors="strict").read()
        except Exception: continue
        if "BepInEx" + BS*2 + "MCE" + BS*2 in tt or "BepInEx" + BS + "MCE" + BS in tt:
            out.write("non-json with runtime path: " + os.path.relpath(pp, rt) + "\n")
out.close()
