import os, sys, re
BS = chr(92)
B = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx"
cfg = os.path.join(B, "config", "ModConfigEnhance", "data", "runtime")
out = []
for name in ("sources.json", "source-identities.json"):
    t = open(os.path.join(cfg, name), encoding="utf-8", errors="ignore").read()
    hits = [m.start() for m in re.finditer(re.escape("BepInEx" + BS + BS + "MCE"), t)]
    out.append(f"{name}: {len(hits)} hits")
    for h in hits[:3]:
        seg = t[max(0, h - 120):h + 120]
        # mask nothing secret-like: these are paths only
        out.append("  ..." + seg.replace("\n", " ") + "...")
    keys = set()
    for h in hits:
        k = t.rfind('"', 0, t.rfind(':', 0, h))
        k2 = t.rfind('"', 0, k)
        keys.add(t[k2 + 1:k])
    out.append("  keys: " + str(list(keys)[:10]))
open(sys.argv[1], "w", encoding="utf-8").write("\n".join(out))
