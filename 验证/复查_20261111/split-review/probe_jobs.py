import os, sys, re, collections, json
BS = chr(92)
B = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx"
out = []
keys = collections.Counter()
phases = collections.Counter()
jobs = os.path.join(B, "MCE", "jobs")
for d, dirs, files in os.walk(jobs):
    for f in files:
        if f != "transaction.json":
            continue
        p = os.path.join(d, f)
        t = open(p, encoding="utf-8", errors="ignore").read()
        if "SteamLibrary" not in t:
            continue
        try:
            j = json.loads(t)
        except Exception:
            continue
        phases[(j.get("Phase"), j.get("Installer"))] += 1
        def visit(o, path):
            if isinstance(o, dict):
                for k, v in o.items():
                    visit(v, path + "." + k)
            elif isinstance(o, list):
                for v in o:
                    visit(v, path + "[]")
            elif isinstance(o, str) and "SteamLibrary" in o:
                sub = "MCE" if (BS + "MCE" + BS) in o or "/MCE/" in o else "other"
                keys[(path, sub)] += 1
        visit(j, "")
out.append("phases: " + str(dict(phases)))
for k, v in keys.most_common(20):
    out.append(f"{v} {k}")
q = json.load(open(os.path.join(B, "MCE", "queue.json"), encoding="utf-8"))
for job in q.get("Jobs", []):
    s = json.dumps(job)
    if "SteamLibrary" in s:
        out.append("queue job state=" + str(job.get("State")) + " keys=" + str([k for k, v in job.items() if isinstance(v, str) and "SteamLibrary" in v]))
open(sys.argv[1], "w", encoding="utf-8").write("\n".join(out))
