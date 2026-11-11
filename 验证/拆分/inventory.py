# -*- coding: utf-8 -*-
"""只读：按类别列出拆分要改的名字在 src / Updater / Desktop / tools 里的每一处（字符串字面量），写 inventory.txt。"""
import io
import os
import re
import sys

root = sys.argv[1]
CATS = [
    ("guid", re.compile(r"com\.aiae\.modconfigenhance", re.I)),
    ("pkgkey", re.compile(r"AiAeT-ModConfigEnhance")),
    ("dirname", re.compile(r'"ModConfigEnhance[^".]*"')),
    ("mce-dir", re.compile(r'"MCE"|"MCE/|"MCE\\\\|/MCE/|\\\\MCE\\\\|BepInEx/MCE|BepInEx\\\\MCE')),
    ("exe", re.compile(r"MCE\.(UpdateWorker|Desktop)")),
    ("pack", re.compile(r"MCEPack")),
    ("env", re.compile(r"MCEEnvironment")),
    ("recovery", re.compile(r"MCE-Recovery")),
    ("keyprefix", re.compile(r'KeyPrefix|"MCE_"')),
    ("hotkeykey", re.compile(r"ModConfigEnhance\.HotkeyDescription")),
    ("mutex", re.compile(r"Global\\\\|Local\\\\|Mutex|\.lock\"")),
    ("ua", re.compile(r"UserAgent")),
]
out = []
for sub in ("src", "Updater", "Desktop", "tools"):
    for d, dirs, fs in os.walk(os.path.join(root, sub)):
        dirs[:] = [x for x in dirs if x not in ("bin", "obj")]
        for f in fs:
            if not f.endswith((".cs", ".ps1", ".cmd", ".py", ".csproj", ".targets", ".xaml")):
                continue
            p = os.path.join(d, f)
            rel = os.path.relpath(p, root)
            for i, line in enumerate(io.open(p, encoding="utf-8-sig", errors="replace"), 1):
                s = line.strip()
                if s.startswith("//") or s.startswith("///"):
                    continue
                for name, rx in CATS:
                    if rx.search(line):
                        out.append(f"[{name}] {rel}:{i}: {s[:220]}")
io.open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "inventory.txt"), "w", encoding="utf-8").write("\n".join(sorted(out)) + "\n")
from collections import Counter
c = Counter(l.split("]")[0][1:] for l in out)
print(dict(c))
