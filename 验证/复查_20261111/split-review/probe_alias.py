import os, sys, re, collections
B = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx"
out = []
c = collections.Counter()
needle = "SteamLibrary"
for base in (os.path.join(B, "MCE"), os.path.join(B, "config", "ModConfigEnhance")):
    for d, dirs, files in os.walk(base):
        for f in files:
            if not f.lower().endswith(".json"):
                continue
            p = os.path.join(d, f)
            try:
                if os.path.getsize(p) > 32 * 1024 * 1024:
                    continue
                t = open(p, encoding="utf-8", errors="ignore").read()
            except Exception:
                continue
            n = t.count(needle)
            if n:
                rel = os.path.relpath(p, B).replace(os.sep, "/")
                parts = rel.split("/")
                key = "/".join(parts[:2]) if parts[0] == "MCE" else rel
                c[key] += n
for k, v in c.most_common(30):
    out.append(f"{v}  {k}")
open(sys.argv[1], "w", encoding="utf-8").write("\n".join(out) or "none")
