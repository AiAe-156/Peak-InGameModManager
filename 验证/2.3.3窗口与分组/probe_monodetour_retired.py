import json
import os
import sys

home = sys.argv[1]
queue = json.load(open(os.path.join(home, "queue.json"), encoding="utf-8"))
history = os.path.join(home, "history.json")
jobs = queue["Jobs"] + (json.load(open(history, encoding="utf-8")) if os.path.exists(history) else [])
marker = "BepInEx" + chr(92)
for job in jobs:
    if "MonoDetour" not in job.get("Package", ""):
        continue
    tx_path = os.path.join(home, "jobs", job["Id"], "transaction.json")
    print(job["Id"][:8], job["Package"], job["State"], job.get("UpdatedUtc", "")[:19], "tx" if os.path.exists(tx_path) else "-")
    if not os.path.exists(tx_path):
        continue
    tx = json.load(open(tx_path, encoding="utf-8"))
    print("    phase", tx.get("Phase"))
    for r in tx.get("Retired", []):
        target = r["Target"]
        backup = r.get("Backup", "")
        print("    retired", target.split(marker)[-1], "| backup:", "OK" if backup and os.path.exists(backup) else "MISSING", "| live:", os.path.exists(target))
