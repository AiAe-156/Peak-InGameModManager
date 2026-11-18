import os, re
BS = chr(92)
roots = [r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\config\ModConfigEnhance", r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\MCE"]
out = open("pipe.txt", "w", encoding="utf-8")
needle = "config" + BS*2 + "ModConfigEnhance|"
needle2 = "BepInEx" + BS*2 + "MCE|"
for r in roots:
    for dp, dns, fns in os.walk(r):
        if "layout-backups" in dp: continue
        for f in fns:
            if not f.endswith(".json"): continue
            p = os.path.join(dp, f)
            t = open(p, encoding="utf-8", errors="replace").read()
            for nd in (needle, needle2):
                i = t.find(nd)
                if i >= 0:
                    out.write(f"{os.path.relpath(p, r)} count={t.count(nd)} needle={nd!r}\n  sample: {t[max(0,i-120):i+160]!r}\n")
out.close()
