import os, re, collections
bep_forms = {
 "lib_esc": r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\\",
 "alias_esc": r"F:\Games\Steam\steamapps\common\PEAK\BepInEx\\",
 "lib_fwd": "F:/Games/SteamLibrary/steamapps/common/PEAK/BepInEx/",
 "alias_fwd": "F:/Games/Steam/steamapps/common/PEAK/BepInEx/",
}
targets = {"MCE": ["MCE\\\\", "MCE/", "MCE\""], "config_MCE": ["config\\ModConfigEnhance", "config/ModConfigEnhance"]}
roots = {"runtime": r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\MCE",
         "config": r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\config\ModConfigEnhance"}
out = open("forms.txt", "w", encoding="utf-8")
for rn, r in roots.items():
    stats = collections.Counter(); perdir = collections.Counter(); big=[]
    for dp, dns, fns in os.walk(r):
        for f in fns:
            if not f.lower().endswith(".json"): continue
            p = os.path.join(dp, f)
            try:
                sz = os.path.getsize(p)
                if sz > 64*1024*1024: big.append((sz, os.path.relpath(p, r)))
                t = open(p, encoding="utf-8", errors="replace").read()
            except Exception as e:
                continue
            rel = os.path.relpath(p, r).replace("\\", "/")
            top = "/".join(rel.split("/")[:2]) if "/" in rel else rel
            hit = False
            for fn, fv in bep_forms.items():
                low = t.lower()
                for tn, tvs in targets.items():
                    for tv in tvs:
                        key = (fv + tv).lower() if fn.endswith("esc") or tv.find("\\")<0 else None
                        if fn.endswith("fwd"): tvv = tv.replace("\\\\", "/")
                        else: tvv = tv
                        needle = (fv + tvv).lower()
                        c = low.count(needle)
                        if c:
                            stats[(fn, tn, tvv)] += c; hit = True
                # case-sensitive variant check for lowercase 'mce' (e.g. \mce\)
                cs = t.count(fv + "mce\\\\") + t.count(fv + "Mce\\\\")
                if cs: stats[(fn, "MCE_casevar", "")] += cs
            # MCE-something sibling false positives
            for m in re.finditer(r"BepInEx(?:\\|/)(MCE[^\/\"]+)", t):
                stats[("sibling", m.group(1)[:30], "")] += 1
            if hit: perdir[top] += 1
    out.write(f"== {rn}\n")
    for k, v in sorted(stats.items()): out.write(f"  {k}: {v}\n")
    out.write("  files-with-hits by dir:\n")
    for k, v in perdir.most_common(): out.write(f"    {k}: {v}\n")
    out.write(f"  >64MB json: {big}\n")
out.close()
