import os, json, collections
BS = chr(92)
r = r"F:\Games\SteamLibrary\steamapps\common\PEAK\BepInEx\MCE"
out = open("alias_fields.txt", "w", encoding="utf-8")
def walk(o, path, acc):
    if isinstance(o, dict):
        for k, v in o.items(): walk(v, path + "." + k, acc)
    elif isinstance(o, list):
        for v in o: walk(v, path + "[]", acc)
    elif isinstance(o, str):
        low = o.lower()
        lib = "steamlibrary" in low
        steam = (BS + "steam" + BS) in low or "/steam/" in low
        under_mce = (BS + "bepinex" + BS + "mce" + BS) in low or low.endswith(BS + "bepinex" + BS + "mce") or "/bepinex/mce/" in low
        under_cfg = ("config" + BS + "modconfigenhance") in low or "config/modconfigenhance" in low
        if lib or steam:
            acc[(path, "Lib" if lib else "Steam", "MCE" if under_mce else ("cfgMCE" if under_cfg else "-"))] += 1
for name in ["queue.json", "history.json"]:
    acc = collections.Counter()
    walk(json.load(open(os.path.join(r, name), encoding="utf-8")), "$", acc)
    out.write("== " + name + "\n")
    for k, v in sorted(acc.items()): out.write(f"  {v:5d} {k}\n")
out.close()
