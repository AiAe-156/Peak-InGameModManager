# -*- coding: utf-8 -*-
"""修 link_identity.py 的错：单行 csproj 被插在 <Project> 之前。先 git checkout 回原样，再在第一个链接 src 的 <Compile 前面插（同一行内）。
用法：python -I relink_identity.py <工程根>"""
import io
import os
import re
import subprocess
import sys

root = sys.argv[1]
names = subprocess.run(["git", "-c", "core.quotepath=false", "diff", "--name-only", "--", "验证"], cwd=root, capture_output=True, text=True, encoding="utf-8").stdout.split()
names = [n for n in names if n.endswith(".csproj")]
subprocess.run(["git", "checkout", "--"] + names, cwd=root, check=True)
done = []
for n in names:
    p = os.path.join(root, n)
    t = io.open(p, encoding="utf-8-sig").read()
    m = re.search(r'<Compile Include="([^"]*?)src[\\/]', t)
    if not m:
        print("no src link:", n); continue
    prefix = m.group(1)
    sep = "\\" if "\\" in t[m.start():m.start() + 200] else "/"
    link = f'<Compile Include="{prefix}src{sep}Updating{sep}ManagerIdentity.cs" Link="ManagerIdentity.cs" />'
    t = t[:m.start()] + link + ("\n    " if "\n" in t[max(0, m.start() - 10):m.start()] else "") + t[m.start():]
    io.open(p, "w", encoding="utf-8", newline="").write(t)
    done.append(n)
print(len(done), "relinked")
