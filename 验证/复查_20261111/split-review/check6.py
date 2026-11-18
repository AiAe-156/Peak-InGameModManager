import os
BS = chr(92)
src = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\config\ModConfigEnhance"
out = open("check6.txt", "w", encoding="utf-8")
for alias in [r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\config", r"F:\Games\Steam\steamapps\common\PEAK\BepInEx\config"]:
    staging_over = []; final_over = 0; dirs_over = []
    for dp, dns, fns in os.walk(src):
        rel = os.path.relpath(dp, src).replace(BS, "/")
        if rel == "output/packs" or rel.startswith("output/packs/") or rel == "exports" or rel.startswith("exports/"):
            dns[:] = []; continue
        sub = "" if rel == "." else BS + rel.replace("/", BS)
        sdir = alias + BS + "InGameModManager.copying" + sub
        if len(sdir) > 247: dirs_over.append((len(sdir), rel))
        for f in fns:
            final = alias + BS + "InGameModManager" + sub + BS + f
            staging = alias + BS + "InGameModManager.copying" + sub + BS + f
            if len(final) > 259: final_over += 1
            elif len(staging) > 259: staging_over.append((len(staging), rel + "/" + f))
    out.write(f"== alias {alias}\n  final>259 (skipped by code): {final_over}\n  staging>259 but final<=259 (copy would throw): {len(staging_over)}\n")
    for L, p in sorted(staging_over, reverse=True)[:10]: out.write(f"    {L} {p}\n")
    out.write(f"  staging dirs >247 chars: {len(dirs_over)} {sorted(dirs_over, reverse=True)[:3]}\n")
out.close()
