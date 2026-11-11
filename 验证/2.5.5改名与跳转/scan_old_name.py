# -*- coding: utf-8 -*-
"""只读：列出源码字符串里玩家看得见的 MCE / ModConfig Enhance 旧名。用法 python -I scan_old_name.py <src> <输出>"""
import io
import os
import re
import sys

src, out = sys.argv[1], sys.argv[2]
STR = re.compile(r'"((?:[^"\\]|\\.)*)"')
NAME = re.compile(r"\bMCE\b|ModConfig ?Enhance")
# 内部标识：键名、GUID、程序集、目录名、日志前缀，不算玩家可见文字
INTERNAL = re.compile(r"^(MCE_|ModConfigEnhance\.|com\.aiae|MCE\.|MCEPack|ModConfigEnhance$|ModConfigEnhance_|MCE$)")
hits = []
for d, _, fs in os.walk(src):
    for f in fs:
        if not f.endswith(".cs"):
            continue
        p = os.path.join(d, f)
        for i, line in enumerate(io.open(p, encoding="utf-8-sig"), 1):
            s = line.strip()
            if s.startswith("//"):
                continue
            for m in STR.finditer(line):
                t = m.group(1)
                if NAME.search(t) and not INTERNAL.match(t) and (" " in t or any(ord(c) > 127 for c in t)):
                    hits.append(f"{os.path.relpath(p, src)}:{i}: {t[:140]}")
io.open(out, "w", encoding="utf-8", newline="\n").write("\n".join(hits) + "\n")
print(len(hits))
