import os, sys
roots = [r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\config\ModConfigEnhance",
         r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\config\InGameModManager",
         r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\MCE",
         r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\IGMM"]
out = open("lens.txt", "w", encoding="utf-8")
for r in roots:
    out.write(f"== {r} exists={os.path.isdir(r)}\n")
    if not os.path.isdir(r): continue
    top = os.listdir(r)
    out.write("top: " + ", ".join(sorted(top)) + "\n")
    buckets = {}
    longest = []
    nfiles = 0
    for dp, dns, fns in os.walk(r):
        rel = os.path.relpath(dp, r).replace("\\", "/")
        for d in dns:
            p = os.path.join(dp, d)
            L = len(p)
            if L >= 240: longest.append((L, "D", os.path.relpath(p, r)))
        for f in fns:
            nfiles += 1
            p = os.path.join(dp, f)
            L = len(p)
            if L >= 240: longest.append((L, "F", os.path.relpath(p, r)))
    out.write(f"files={nfiles}\n")
    longest.sort(reverse=True)
    for L, k, p in longest[:60]:
        out.write(f"  {L} {k} {p}\n")
    out.write(f"  count>=240: {len(longest)}\n")
out.close()
