"""Read-only inventory of BepInEx/plugins by shape, to see which installed mods the overview cannot show.

The overview builds a row only for an assembly (`*.dll` or `*.dll.*`) whose metadata declares a [BepInPlugin]
(OverviewCatalogue.Scan). This probe approximates that with a byte search for the BepInPlugin attribute name, which is
enough to separate "has a plugin" from "library / no assembly" for an inventory; it is not the production reader.

Shapes:
  A  folder with manifest.json(.old) and a plugin assembly        -> row today
  B  folder with manifest and only library DLLs (no BepInPlugin)    -> no row (dependency/library package)
  C  folder with manifest and no DLL at all                         -> no row (manifest-only / leftover / assets)
  D  folder with plugin DLL but no manifest                        -> row today (local package)
  E  loose plugin DLL directly in plugins/                          -> row today (root-level single DLL)
  F  loose non-plugin DLL directly in plugins/                      -> no row
  G  folder whose plugin DLLs are all disabled (*.dll.<suffix>)     -> row today if a plugin is found in a disabled copy
"""
import os
import sys

sys.stdout.reconfigure(encoding="utf-8")
PLUGINS = os.path.abspath(sys.argv[1])
MARK = b"BepInPlugin"
CONTAINERS_HINT = {"MMHOOK"}


def is_assembly(name):
    low = name.lower()
    return low.endswith(".dll") or ".dll." in low


def has_plugin(path):
    try:
        with open(path, "rb") as f:
            return MARK in f.read()
    except OSError:
        return False


def manifest_in(folder):
    for name in ("manifest.json", "manifest.json.old"):
        if os.path.isfile(os.path.join(folder, name)):
            return name
    return ""


rows = []
# Loose files in plugins/ root.
for name in sorted(os.listdir(PLUGINS)):
    path = os.path.join(PLUGINS, name)
    if os.path.isfile(path) and is_assembly(name):
        rows.append(("E" if has_plugin(path) else "F", name, "disabled" if not name.lower().endswith(".dll") else ""))


def visit(folder, depth):
    rel = os.path.relpath(folder, PLUGINS)
    manifest = manifest_in(folder)
    files, dirs = [], []
    for entry in os.scandir(folder):
        (dirs if entry.is_dir(follow_symlinks=False) else files).append(entry)
    if manifest or depth > 0 and any(is_assembly(f.name) for f in files):
        # A package boundary: everything below belongs to it, except nested manifests.
        assemblies = []
        stack = [folder]
        while stack:
            current = stack.pop()
            for entry in os.scandir(current):
                if entry.is_dir(follow_symlinks=False):
                    if entry.path != folder and manifest_in(entry.path):
                        visit(entry.path, depth + 1)
                        continue
                    stack.append(entry.path)
                elif is_assembly(entry.name):
                    assemblies.append(entry.path)
        plugins = [a for a in assemblies if has_plugin(a)]
        live = [a for a in plugins if a.lower().endswith(".dll")]
        if plugins:
            shape = ("A" if manifest else "D") if live else "G"
        elif assemblies:
            shape = "B" if manifest else "B-"
        else:
            shape = "C" if manifest else "?"
        note = []
        if manifest.endswith(".old"):
            note.append("manifest.old")
        if plugins and not live:
            note.append("all plugin DLLs disabled")
        rows.append((shape, rel, ", ".join(note)))
        return
    for d in dirs:
        visit(d.path, depth + 1)


for entry in sorted(os.scandir(PLUGINS), key=lambda e: e.name):
    if entry.is_dir(follow_symlinks=False) and entry.name not in CONTAINERS_HINT:
        visit(entry.path, 0)

counts = {}
for shape, _, _ in rows:
    counts[shape] = counts.get(shape, 0) + 1
print("counts:", dict(sorted(counts.items())))
for wanted in ("C", "B", "B-", "F", "G", "E", "?"):
    items = [r for r in rows if r[0] == wanted]
    if not items:
        continue
    print("\n== " + wanted + " (" + str(len(items)) + ")")
    for shape, rel, note in items:
        print("   " + rel + ("   [" + note + "]" if note else ""))
