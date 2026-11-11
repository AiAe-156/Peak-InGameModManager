# -*- coding: utf-8 -*-
"""拆分：文档与界面文字里的旧路径改新路径。
config/ModConfigEnhance → config/InGameModManager；BepInEx/MCE → BepInEx/IGMM；MCEPack → IGMMPack；
%LOCALAPPDATA%/ModConfigEnhance → %LOCALAPPDATA%/InGameModManager；MCE-Recovery → IGMM-Recovery；com.aiae.modconfigenhance.cfg → com.aiae.ingamemodmanager.cfg；
MCE.UpdateWorker.exe / MCE.Desktop.exe → IGMM.*；语言文件说明里的 `MCE_` 键前缀 → `IGMM_`。
三种写法都换：/、\\（C# 字符串转义）、\\\\（JSON）。
跳过：changelog（讲迁移时要写旧名）、ManagerIdentity/ManagerSplitMigration（定义旧名）、MceDirectories/MceDirectoryMigration 的旧布局表、
所有 // 注释行（只换玩家可见文字与文档）。
用法：python -I rename_paths.py <工程根> [--apply]"""
import io
import os
import re
import sys

root = sys.argv[1]
apply = "--apply" in sys.argv
SKIP = {"changelog.md", "ManagerIdentity.cs", "ManagerSplitMigration.cs", "OverviewPage.LegacyMceNotice.cs"}
SEPS = ["/", "\\\\", "\\"]
PAIRS = []
for s in SEPS:
    PAIRS += [
        ("config" + s + "ModConfigEnhance", "config" + s + "InGameModManager"),
        ("BepInEx" + s + "MCE" + s, "BepInEx" + s + "IGMM" + s),
        ("%LOCALAPPDATA%" + s + "ModConfigEnhance", "%LOCALAPPDATA%" + s + "InGameModManager"),
    ]
PAIRS += [
    ("`BepInEx/MCE/`", "`BepInEx/IGMM/`"), ("`BepInEx\\\\MCE\\\\`", "`BepInEx\\\\IGMM\\\\`"),
    ("BepInEx/MCE）", "BepInEx/IGMM）"), ("BepInEx/MCE)", "BepInEx/IGMM)"), ("`BepInEx/MCE`", "`BepInEx/IGMM`"),
    ("MCEPack", "IGMMPack"), ("MCE-Recovery", "IGMM-Recovery"),
    ("com.aiae.modconfigenhance.cfg", "com.aiae.ingamemodmanager.cfg"),
    ("MCE.UpdateWorker.exe", "IGMM.UpdateWorker.exe"), ("MCE.Desktop.exe", "IGMM.Desktop.exe"),
    ("`MCE_", "`IGMM_"), ("MCE_KEY=", "IGMM_KEY="), ("MCE_SEARCH=", "IGMM_SEARCH="),
]
RE_COMMENT = re.compile(r"^\s*//")
changed, counts = {}, {}
for sub in ("src", "Help"):
    for d, dirs, fs in os.walk(os.path.join(root, sub)):
        dirs[:] = [x for x in dirs if x not in ("bin", "obj", "media")]
        for f in fs:
            if f in SKIP or not f.endswith((".cs", ".md")):
                continue
            p = os.path.join(d, f)
            raw = io.open(p, "rb").read(); bom = raw.startswith(b"\xef\xbb\xbf")
            text = raw.decode("utf-8-sig"); crlf = "\r\n" in text
            lines = text.replace("\r\n", "\n").split("\n")
            n = 0
            for i, line in enumerate(lines):
                if f.endswith(".cs") and RE_COMMENT.match(line):
                    continue
                if f in ("MceDirectories.cs", "MceDirectoryMigration.cs") and "Moves" in line:
                    continue
                new = line
                for a, b in PAIRS:
                    if a in new:
                        c = new.count(a); new = new.replace(a, b); n += c
                lines[i] = new
            if n:
                out = "\n".join(lines)
                if crlf: out = out.replace("\n", "\r\n")
                changed[p] = (bom, out); counts[os.path.relpath(p, root)] = n
for k, v in sorted(counts.items()):
    print(f"{v:4} {k}")
print(sum(counts.values()), "in", len(counts), "files")
if apply:
    for p, (bom, out) in changed.items():
        io.open(p, "wb").write((b"\xef\xbb\xbf" if bom else b"") + out.encode("utf-8"))
