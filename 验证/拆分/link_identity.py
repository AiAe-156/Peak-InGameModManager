# -*- coding: utf-8 -*-
"""拆分辅助：给链接了 src/ 或 Updater/ 源文件、但还没链接 src/Updating/ManagerIdentity.cs 的 csproj 补一行。
只动 Updater/、Desktop/ 与回归集的夹具（列表见 TARGETS + 自动发现里被 run-regression.py 用到的目录）。
用法：python -I link_identity.py <工程根> [--apply]"""
import io
import os
import re
import sys

root = sys.argv[1]
apply = "--apply" in sys.argv
targets = [os.path.join(root, "Updater", "MCE.UpdateWorker.csproj"), os.path.join(root, "Desktop", "MCE.Desktop.csproj")]
reg = io.open(os.path.join(root, "验证", "统一保存重构", "run-regression.py"), encoding="utf-8").read()
dirs = set()
for m in re.finditer(r"'([A-Za-z0-9]+Checks)'", reg):
    dirs.add(os.path.join(root, "验证", "统一保存重构", m.group(1)))
    dirs.add(os.path.join(root, "验证", m.group(1)))
for extra in ("更新核心重做/InstallerChecks", "2.3.11小数显示/checks", "2.5.2目录重排/layout-checks", "2.5.0语言解绑/regex-checks", "InstallationFlowChecks"):
    dirs.add(os.path.join(root, "验证", *extra.split("/")))
for d in dirs:
    if os.path.isdir(d):
        for f in os.listdir(d):
            if f.endswith(".csproj"):
                targets.append(os.path.join(d, f))
changed = []
for p in sorted(set(targets)):
    t = io.open(p, encoding="utf-8-sig").read()
    if "ManagerIdentity.cs" in t:
        continue
    m = re.search(r'([ \t]*)<Compile Include="([^"]*?)(src[\\/]Updating[\\/])[^"]*"[^>]*/>', t)
    if m:
        indent, prefix, sep = m.group(1), m.group(2), m.group(3)
        line = f'{indent}<Compile Include="{prefix}{sep}ManagerIdentity.cs" Link="ManagerIdentity.cs" />\n'
    else:
        m = re.search(r'([ \t]*)<Compile Include="([^"]*?)src[\\/][^"]*"[^>]*/>', t)
        if not m and not p.endswith("Desktop.csproj"):
            continue
        if m:
            indent, prefix = m.group(1), m.group(2)
            line = f'{indent}<Compile Include="{prefix}src\\Updating\\ManagerIdentity.cs" Link="ManagerIdentity.cs" />\n'
    if m is None:
        # Desktop 没有任何链接：加一个 ItemGroup
        new = t.replace("</Project>", '  <ItemGroup><Compile Include="../src/Updating/ManagerIdentity.cs" Link="ManagerIdentity.cs" /></ItemGroup>\n</Project>')
    else:
        start = t.rfind("\n", 0, m.start()) + 1
        new = t[:start] + line + t[start:]
    changed.append(os.path.relpath(p, root))
    if apply:
        io.open(p, "w", encoding="utf-8", newline="").write(new)
print("\n".join(changed))
print(len(changed))
