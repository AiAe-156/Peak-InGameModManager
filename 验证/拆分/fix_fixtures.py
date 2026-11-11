# -*- coding: utf-8 -*-
"""拆分：回归夹具里写死的旧运行目录与程序名改成新名。只改夹具源码（验证/ 下 .cs），不改被测代码。
· "MCE/xxx" 与 Path.Combine(..., "MCE", ...) 的 "MCE" 片段 → IGMM（夹具自己造的目录，被测代码现在读写 BepInEx/IGMM）
· ModConfigEnhance.dll → InGameModManager.dll；MCEEnvironment.json → IGMMEnvironment.json（写入侧夹具）
旧 GUID、旧包键不动：被测代码对它们新旧都认，夹具留着正好验证兼容。
用法：python -I fix_fixtures.py <工程根> [--apply]"""
import io
import os
import re
import sys

root = sys.argv[1]
apply = "--apply" in sys.argv
FILES = [
    "验证/ShopChecks/Program.cs", "验证/PackLibraryChecks/Program.cs", "验证/InstallationFlowChecks/Program.cs",
    "验证/更新核心重做/InstallerChecks/Program.cs", "验证/统一保存重构/CacheChecks/Program.cs", "验证/统一保存重构/NativeChecks/Program.cs",
]
RX = [
    (re.compile(r'"MCE"'), '"IGMM"'),
    (re.compile(r'"MCE/'), '"IGMM/'),
    (re.compile(r'"MCE\\\\'), '"IGMM\\\\'),
    (re.compile(r'BepInEx/MCE/'), 'BepInEx/IGMM/'),
    (re.compile(r'ModConfigEnhance\.dll'), 'InGameModManager.dll'),
]
report = []
for f in FILES:
    p = os.path.join(root, *f.split("/"))
    raw = io.open(p, "rb").read(); bom = raw.startswith(b"\xef\xbb\xbf")
    t = raw.decode("utf-8-sig"); n = t; total = 0
    for rx, rep in RX:
        n, c = rx.subn(rep, n); total += c
    report.append(f"{f}: {total}")
    if apply and n != t:
        io.open(p, "wb").write((b"\xef\xbb\xbf" if bom else b"") + n.encode("utf-8"))
print("\n".join(report))
