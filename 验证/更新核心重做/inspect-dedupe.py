import os, glob, collections

G = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx"
skip = os.path.join(G, "MCE")

print("=== 现在 core/patchers/plugins 下的 MonoDetour 文件")
for area in ("core", "patchers", "plugins"):
    for d, dirs, fs in os.walk(os.path.join(G, area)):
        for f in fs:
            if "monodetour" in f.lower():
                p = os.path.join(d, f)
                print("   %-80s %8d" % (p[len(G):], os.path.getsize(p)))

print()
print("=== 最近 3 个任务备份目录的内容")
for b in sorted(glob.glob(os.path.join(G, "MCE", "backups", "*")), key=os.path.getmtime)[-3:]:
    print("  ", os.path.basename(b))
    for r, _, fs in os.walk(b):
        for f in fs:
            print("      %-58s %8d" % (os.path.join(r, f).replace(b, ""), os.path.getsize(os.path.join(r, f))))

print()
print("=== 同名出现在多处的文件（潜在重复加载）")
c = collections.defaultdict(list)
for area in ("core", "patchers", "plugins"):
    for d, dirs, fs in os.walk(os.path.join(G, area)):
        if os.path.normcase(d).startswith(os.path.normcase(skip)):
            continue
        for f in fs:
            c[f.lower()].append(os.path.join(d, f)[len(G):])
dup = {k: v for k, v in c.items() if len(v) > 1}
for name, paths in sorted(dup.items()):
    print("   %-46s" % name)
    for p in paths:
        print("        " + p)
print("   重复文件名总数:", len(dup))
