import os, re, collections
BS = chr(92)
roots = {"runtime": r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\MCE",
         "config": r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\config\ModConfigEnhance"}
sep = "(?:" + re.escape(BS + BS) + "|/|" + re.escape(BS) + ")"
pat = re.compile("([A-Za-z]:" + sep + '[^"]{0,80}?BepInEx' + sep + '[^"' + re.escape(BS) + '/]{0,40}' + "(?:" + sep + '[^"' + re.escape(BS) + '/]{0,20})?)')
out = open("forms2.txt", "w", encoding="utf-8")
for rn, r in roots.items():
    c = collections.Counter(); dirs = collections.defaultdict(collections.Counter)
    for dp, dns, fns in os.walk(r):
        for f in fns:
            if not f.lower().endswith(".json"): continue
            p = os.path.join(dp, f)
            try: t = open(p, encoding="utf-8", errors="replace").read()
            except Exception: continue
            rel = os.path.relpath(p, r).replace(BS, "/").split("/")
            top = "/".join(rel[:2]) if len(rel) > 2 else rel[0]
            for m in pat.finditer(t):
                k = m.group(1)
                # keep only the path head up to the 2nd component after BepInEx
                c[k] += 1; dirs[k][top] += 1
    out.write(f"== {rn}\n")
    for k, v in c.most_common(60):
        out.write(f"  {v:6d} {k}   <- {dict(dirs[k].most_common(5))}\n")
out.close()
