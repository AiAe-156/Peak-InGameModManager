import io, glob, os, sys
cfg = ""
for p in glob.glob(os.path.join(sys.argv[1], "*.cfg")):
    try: cfg += io.open(p, encoding="utf-8", errors="replace").read()
    except Exception: pass
hit = miss = 0; misses = []
for line in io.open("outside.txt", encoding="utf-8"):
    k = line.rstrip("\n").replace("\n", "\n")
    if not k: continue
    first = k.split("\n")[0][:60]
    if first in cfg: hit += 1
    else: miss += 1; misses.append(k)
print("in current cfg files", hit, "| not in any cfg", miss)
io.open("outside_not_cfg.txt", "w", encoding="utf-8").write("\n".join(m.replace("\n","\n") for m in misses))
