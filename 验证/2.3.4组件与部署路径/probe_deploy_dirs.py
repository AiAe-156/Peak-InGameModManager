"""Read-only: for every MODs project with a deploy target, show where it deploys and where its assembly actually lives."""
import glob
import html
import os
import re
import sys
sys.stdout.reconfigure(encoding="utf-8")

MODS = os.path.abspath(sys.argv[1])
GAME = os.path.dirname(MODS)
PLUGINS = os.path.join(GAME, "BepInEx", "plugins")
SKIP = ("备份", "归档", "验证", "_废弃项目", "反编译", "bin", "obj")


def prop(text, name):
    m = re.search(r"<%s(?:\s[^>]*)?>(.*?)</%s>" % (name, name), text, re.S)
    return html.unescape(m.group(1).strip()) if m else ""


live = {}
for path in glob.glob(os.path.join(PLUGINS, "**", "*.dll"), recursive=True):
    live.setdefault(os.path.basename(path).lower(), []).append(path)

for csproj in sorted(glob.glob(os.path.join(MODS, "**", "*.csproj"), recursive=True)):
    rel = os.path.relpath(csproj, MODS)
    if any(part in SKIP for part in rel.split(os.sep)):
        continue
    text = open(csproj, encoding="utf-8-sig").read()
    deploy = prop(text, "DeployDir") or ""
    output = prop(text, "OutputPath")
    if not deploy and "BepInEx" in output:
        deploy = output
    if not deploy:
        continue
    values = {"GameDir": GAME + os.sep, "PeakDir": GAME, "PluginsDir": PLUGINS + os.sep}
    for key in ("GameDir", "PeakDir", "PluginsDir"):
        own = prop(text, key)
        if own and "$(" not in own:
            values[key] = own
    resolved = deploy
    for key, value in values.items():
        resolved = resolved.replace("$(%s)" % key, value)
    name = prop(text, "AssemblyName") or os.path.splitext(os.path.basename(csproj))[0]
    version = prop(text, "Version")
    exists = os.path.isdir(resolved)
    copies = [os.path.relpath(p, PLUGINS) for p in live.get((name + ".dll").lower(), [])]
    print(("OK  " if exists else "MISS") + " | " + rel + " | " + name + " " + version)
    print("      deploy: " + os.path.relpath(os.path.normpath(resolved), GAME))
    for c in copies:
        print("      live:   " + c)
