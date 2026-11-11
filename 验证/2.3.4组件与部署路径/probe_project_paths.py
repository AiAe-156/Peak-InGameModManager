"""Read-only: every line in a project file that points into BepInEx/plugins or BepInEx/<cache area>, and whether that
folder exists now. Covers DeployDir, OutputPath and reference HintPaths alike."""
import glob
import html
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8")
MODS = os.path.abspath(sys.argv[1])
GAME = os.path.dirname(MODS)
BEP = os.path.join(GAME, "BepInEx")
SKIP = ("备份", "归档", "验证", "_废弃项目", "反编译", "bin", "obj")
PATTERN = re.compile(r"(\$\((?:PluginsDir|GameDir|PeakDir)\)[^<\"';]*|[A-Z]:\\[^<\"';]*\\BepInEx\\[^<\"';]*)")

for csproj in sorted(glob.glob(os.path.join(MODS, "**", "*.csproj"), recursive=True)):
    rel = os.path.relpath(csproj, MODS)
    if any(part in SKIP for part in rel.split(os.sep)):
        continue
    lines = open(csproj, encoding="utf-8-sig").read().splitlines()
    hits = []
    for number, line in enumerate(lines, 1):
        if line.strip().startswith("<!--"):
            continue
        for match in PATTERN.findall(html.unescape(line)):
            path = match.replace("$(PluginsDir)", os.path.join(BEP, "plugins") + os.sep).replace("$(GameDir)", GAME + os.sep).replace("$(PeakDir)", GAME)
            path = os.path.normpath(path.replace("\\\\", "\\"))
            if "$(" in path or "BepInEx" not in path:
                continue
            inside = os.path.relpath(path, BEP)
            if inside.split(os.sep)[0] in ("core", "config", "patchers") and "plugins" not in inside:
                tag = "core"
            else:
                tag = "ok  " if os.path.exists(path) or os.path.exists(os.path.dirname(path)) and path.endswith(".dll") is False and os.path.isdir(os.path.dirname(path)) else "MISS"
                tag = "ok  " if os.path.exists(path) else "MISS"
            kind = "deploy" if re.search(r"<(DeployDir|OutputPath)", line) else "ref" if "HintPath" in line else "other"
            hits.append((number, tag, kind, inside))
    if hits:
        print("== " + rel)
        for number, tag, kind, inside in hits:
            print("   %4d %s %-6s %s" % (number, tag, kind, inside))
