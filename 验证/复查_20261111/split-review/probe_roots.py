import os, sys, re, collections
BS = chr(92)
B = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx"
rt = os.path.join(B, "MCE")
cfg = os.path.join(B, "config", "ModConfigEnhance")
out = []
pat = re.compile(r"[A-Za-z]:(?:\\\\|/)[^\"]{0,80}?BepInEx(?:\\\\|/)(?:MCE|config(?:\\\\|/)ModConfigEnhance)")
roots = collections.Counter()
uesc = 0
for base in (rt, cfg):
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
            for m in pat.finditer(t):
                s = m.group(0)
                if BS + "u" in s:
                    uesc += 1
                key = s.split("BepInEx")[0]
                roots[key] += 1
out.append("path prefixes before BepInEx in JSON:")
for k, v in roots.most_common(10):
    out.append(f"  {v}  {k}")
out.append(f"matches containing \\u escapes: {uesc}")
log = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\LogOutput.log"
try:
    t = open(log, encoding="utf-8", errors="ignore").read(20000)
    m = re.search(r"[A-Za-z]:\\[^\r\n]*?PEAK", t)
    out.append("LogOutput sample path: " + (m.group(0) if m else "none"))
except Exception as e:
    out.append("log read fail " + str(e))
open(sys.argv[1], "w", encoding="utf-8").write("\n".join(out))
