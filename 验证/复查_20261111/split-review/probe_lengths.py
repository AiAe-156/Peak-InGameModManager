import os, sys
BS = chr(92)
B = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx"
out = []
cfg = os.path.join(B, "config", "ModConfigEnhance")
rt = os.path.join(B, "MCE")


def walk(root):
    for d, dirs, files in os.walk(root):
        for f in files:
            yield os.path.join(d, f)


skipped = [os.path.join(cfg, "output", "packs").lower(), os.path.join(cfg, "exports").lower()]
n = 0
danger = 0
for p in walk(cfg):
    if any(p.lower().startswith(s + os.sep) for s in skipped):
        continue
    n += 1
    if 252 <= len(p) <= 259:
        danger += 1
out.append(f"config files considered={n}, final len 252..259 (staging path >259)={danger}")

n = 0
over = 0
maxlen = 0
for p in walk(rt):
    n += 1
    maxlen = max(maxlen, len(p))
    if len(p) + 1 > 259:
        over += 1
out.append(f"runtime files={n}, max len={maxlen}, new path >259 after MCE->IGMM={over}")

esc2 = BS + BS
cnt = 0
refs = []
needles = ["BepInEx" + esc2 + "MCE" + esc2, "BepInEx" + esc2 + "MCE\"", "BepInEx/MCE/"]
for p in walk(cfg):
    if not p.lower().endswith(".json"):
        continue
    try:
        t = open(p, encoding="utf-8", errors="ignore").read()
    except Exception:
        continue
    k = sum(t.count(x) for x in needles)
    if k:
        cnt += k
        refs.append(os.path.relpath(p, cfg))
out.append(f"config JSON refs to BepInEx/MCE: {cnt} in {len(refs)} files: {refs[:15]}")

cnt = 0
refs = []
needles = ["config" + esc2 + "ModConfigEnhance", "config/ModConfigEnhance"]
for p in walk(rt):
    if not p.lower().endswith(".json"):
        continue
    rel = os.path.relpath(p, rt).replace(BS, "/")
    try:
        if os.path.getsize(p) > 64 * 1024 * 1024:
            continue
        t = open(p, encoding="utf-8", errors="ignore").read()
    except Exception:
        continue
    k = sum(t.count(x) for x in needles)
    if k:
        cnt += k
        refs.append(rel)
out.append(f"runtime JSON refs to config/ModConfigEnhance: {cnt} in {len(refs)} files: {refs[:20]}")

# runtime JSON referring to runtime itself with forward or back slashes
cnt = 0
refs = []
needles = ["BepInEx" + esc2 + "MCE" + esc2, "BepInEx/MCE/"]
for p in walk(rt):
    if not p.lower().endswith(".json"):
        continue
    rel = os.path.relpath(p, rt).replace(BS, "/")
    top = rel.split("/")[0]
    try:
        t = open(p, encoding="utf-8", errors="ignore").read()
    except Exception:
        continue
    k = sum(t.count(x) for x in needles)
    if k:
        cnt += k
        refs.append(rel)
skipped_top = [r for r in refs if r.split("/")[0] in ("backups", "downloads", "tools", "layout-backups")]
out.append(f"runtime JSON self refs: {cnt} in {len(refs)} files; in skipped tops: {skipped_top[:10]}")
out.append("sample: " + str(refs[:20]))

open(sys.argv[1], "w", encoding="utf-8").write("\n".join(out))
