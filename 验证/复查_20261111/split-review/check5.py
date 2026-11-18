import os, json, re
BS = chr(92)
out = open("check5.txt", "w", encoding="utf-8")
rt = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\MCE"
for alias in [rt, r"F:\Games\Steam\steamapps\common\PEAK\BepInEx\MCE"]:
    n = {}
    for dp, dns, fns in os.walk(rt):
        for name in fns + dns:
            L = len(os.path.join(dp, name)) - len(rt) + len(alias)
            if L in (258, 259, 260): n[L] = n.get(L, 0) + 1
    out.write(f"runtime entries with length 258/259/260 under alias {alias}: {n}\n")
# cfg keys whose value looks like a path (print key names only)
cfg = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\config\com.aiae.modconfigenhance.cfg"
sec = ""
for line in open(cfg, encoding="utf-8-sig"):
    s = line.strip()
    if s.startswith("["): sec = s
    elif "=" in s and not s.startswith("#"):
        k, v = s.split("=", 1)
        if ":" + BS in v or ":/" in v or "ModConfigEnhance" in v or "MCE" + BS in v:
            out.write(f"cfg path-like value: {sec} {k.strip()} (contains ModConfigEnhance={'ModConfigEnhance' in v}, MCE\\={'MCE'+BS in v})\n")
# profiles/index.json: which fields carry config root
p = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\config\ModConfigEnhance\profiles\index.json"
def walk(o, path):
    if isinstance(o, dict):
        for k, v in o.items(): walk(v, path + "." + k)
    elif isinstance(o, list):
        for v in o: walk(v, path + "[]")
    elif isinstance(o, str) and "ModConfigEnhance" in o:
        out.write("index.json field with config path: " + path + " endswith-sep=" + str(o.endswith(BS)) + "\n")
walk(json.load(open(p, encoding="utf-8")), "$")
out.close()
