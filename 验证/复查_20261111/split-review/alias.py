import os, collections, datetime
BS = chr(92)
r = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\MCE"
forms = {"Steam": "F:" + BS*2 + "Games" + BS*2 + "Steam" + BS*2 + "steamapps",
         "SteamLibrary": "F:" + BS*2 + "Games" + BS*2 + "SteamLibrary" + BS*2 + "steamapps",
         "Steam_fwd": "F:/Games/Steam/steamapps", "SteamLibrary_fwd": "F:/Games/SteamLibrary/steamapps",
         "lower_lib": "f:" + BS*2 + "games" + BS*2 + "steamlibrary"}
out = open("alias.txt", "w", encoding="utf-8")
rows = []
for name in ["queue.json", "history.json", "pack-task.json", "resource-batch.json"]:
    p = os.path.join(r, name)
    if os.path.exists(p):
        t = open(p, encoding="utf-8", errors="replace").read()
        rows.append((os.path.getmtime(p), name, {k: t.count(v) for k, v in forms.items()}))
jobs = os.path.join(r, "jobs")
for j in os.listdir(jobs):
    jp = os.path.join(jobs, j)
    for dp, dns, fns in os.walk(jp):
        for f in fns:
            if f.endswith(".json"):
                p = os.path.join(dp, f)
                t = open(p, encoding="utf-8", errors="replace").read()
                c = {k: t.count(v) for k, v in forms.items()}
                if any(c.values()): rows.append((os.path.getmtime(p), "jobs/" + j + "/" + f, c))
rows.sort()
for m, n, c in rows[-40:]:
    out.write(datetime.datetime.fromtimestamp(m).strftime("%Y-%m-%d %H:%M") + " " + n + " " + str({k: v for k, v in c.items() if v}) + "\n")
# which form do worker logs / lock show? check logs dir newest file names only
out.write("logs: " + ", ".join(sorted(os.listdir(os.path.join(r, "logs")))[-5:]) + "\n")
# MCE-Recovery.cmd: does it embed path? report form counts only
for n in ["MCE-Recovery.cmd", "MCE-Recovery.ps1"]:
    p = os.path.join(r, n)
    t = open(p, encoding="utf-8", errors="replace").read()
    out.write(n + " mtime " + datetime.datetime.fromtimestamp(os.path.getmtime(p)).strftime("%Y-%m-%d %H:%M") + " Steam=" + str(t.count("Games" + BS + "Steam" + BS)) + " Lib=" + str(t.count("SteamLibrary")) + " len=" + str(len(t)) + "\n")
out.close()
