import os, sys, json
B = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx"
idx = json.load(open(os.path.join(B, "MCE", "packs", "index.json"), encoding="utf-8"))
out = []
def mgr(files):
    r = []
    for f in files or []:
        p = f.get("Path", "")
        low = p.lower()
        if low.endswith("modconfigenhance.dll") or low.endswith("ingamemodmanager.dll") or "updateworker.exe" in low:
            r.append(p)
    return r
out.append("HostRoots: " + json.dumps(idx.get("HostRoots"), ensure_ascii=False))
out.append("ActiveFiles manager entries: " + json.dumps(mgr(idx.get("ActiveFiles")), ensure_ascii=False))
for p in idx.get("Packs", []):
    out.append(f"pack {p.get('Name')} env={p.get('EnvironmentVersion')} content={p.get('Content')} mgr=" + json.dumps(mgr(p.get("Files")), ensure_ascii=False))
    for k in ("PendingUpdate", "UnsavedEnvironment"):
        if p.get(k):
            out.append(f"   {k} mgr=" + json.dumps(mgr(p[k].get("Files")), ensure_ascii=False))
open(sys.argv[1], "w", encoding="utf-8").write("\n".join(out))
