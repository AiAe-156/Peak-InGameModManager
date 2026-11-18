import os, re, json, collections
BS = chr(92)
cfg = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\config\ModConfigEnhance"
rt = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\MCE"
out = open("check3.txt", "w", encoding="utf-8")
# (1) occurrences in config JSON of runtime root, by following char
mce_esc = "BepInEx" + BS*2 + "MCE"
cfg_esc = "config" + BS*2 + "ModConfigEnhance"
for label, root, needle in [("config-json has BepInEx\\\\MCE", cfg, mce_esc), ("config-json has config\\\\MCE", cfg, cfg_esc), ("runtime-json has BepInEx\\\\MCE", rt, mce_esc)]:
    follow = collections.Counter(); files = collections.Counter()
    for dp, dns, fns in os.walk(root):
        if "layout-backups" in dp: continue
        for f in fns:
            if not f.lower().endswith(".json"): continue
            p = os.path.join(dp, f)
            try: t = open(p, encoding="utf-8", errors="replace").read()
            except Exception: continue
            i = 0
            while True:
                i = t.find(needle, i)
                if i < 0: break
                ch = t[i+len(needle)] if i+len(needle) < len(t) else "EOF"
                follow[ch] += 1
                top = os.path.relpath(p, root).replace(BS, "/").split("/")
                files["/".join(top[:2])] += 1
                i += len(needle)
    out.write(f"== {label}: follow-chars {dict(follow)}\n   by dir {dict(files.most_common(12))}\n")
# (2) sources.json record stats
p = os.path.join(cfg, "data", "runtime", "sources.json")
d = json.load(open(p, encoding="utf-8"))
st = collections.Counter()
for k, v in d.items():
    st["records"] += 1
    if v.get("environment"): st["env"] += 1
    if v.get("instance"): st["instance"] += 1
    if v.get("note") == "user": st["user"] += 1
    if v.get("explicitLocal"): st["explicitLocal"] += 1
    if v.get("note") == "user" and v.get("environment"): st["user+env"] += 1
out.write("sources.json: " + str(dict(st)) + "\n")
p = os.path.join(cfg, "data", "runtime", "source-identities.json")
d = json.load(open(p, encoding="utf-8"))
out.write("source-identities.json keys: " + str(len(d)) + "\n")
# (3) unicode escapes in worker JSON
q = open(os.path.join(rt, "queue.json"), encoding="utf-8").read()
out.write("queue.json u-escapes: " + str(len(re.findall(re.escape(BS) + "u[0-9A-Fa-f]{4}", q))) + " literal CJK: " + str(len(re.findall("[\u4e00-\u9fff]", q))) + "\n")
out.close()
