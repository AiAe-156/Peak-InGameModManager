# -*- coding: utf-8 -*-
"""推送前按值的形状扫描 git 跟踪的全部文件（不看键名）。只打印「文件:行号:类型」，不打印命中的值本身。
用法：python -I secret_scan.py <仓库根>"""
import io
import os
import re
import subprocess
import sys

root = sys.argv[1]
files = subprocess.run(["git", "-c", "core.quotepath=false", "ls-files"], cwd=root, capture_output=True, text=True, encoding="utf-8").stdout.splitlines()
RULES = [
    ("openai-like", re.compile(r"\bsk-[A-Za-z0-9_-]{20,}")),
    ("sk_tr", re.compile(r"\bsk_tr_[A-Za-z0-9]{10,}")),
    ("github-token", re.compile(r"\b(ghp|gho|ghu|ghs|ghr|github_pat)_[A-Za-z0-9_]{20,}")),
    ("jwt", re.compile(r"\beyJ[A-Za-z0-9_-]{15,}\.[A-Za-z0-9_-]{10,}")),
    ("private-key", re.compile(r"BEGIN [A-Z ]*PRIVATE KEY")),
    ("aws", re.compile(r"\bAKIA[0-9A-Z]{16}\b")),
    ("long-b64", re.compile(r"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{120,}={0,2}(?![A-Za-z0-9+/])")),
    ("long-hex", re.compile(r"(?<![0-9a-fA-F])[0-9a-fA-F]{96,}(?![0-9a-fA-F])")),
]
SKIP_EXT = (".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp4", ".webm", ".dll", ".exe", ".ttf", ".otf", ".zip", ".ico", ".pdb")
hits = []
for f in files:
    if f.lower().endswith(SKIP_EXT):
        continue
    p = os.path.join(root, f)
    try:
        if os.path.getsize(p) > 8 << 20:
            continue
        text = io.open(p, encoding="utf-8", errors="ignore").read()
    except OSError:
        continue
    for i, line in enumerate(text.splitlines(), 1):
        for name, rx in RULES:
            if rx.search(line):
                hits.append(f"{f}:{i}:{name}")
# 签名私钥文件名也查一下
for f in files:
    if re.search(r"author-key|\.pem$|\.pfx$|\.p12$|id_rsa|\.env$", f, re.I):
        hits.append(f"{f}:0:suspicious-file")
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "secret_scan.txt")
io.open(out, "w", encoding="utf-8").write("\n".join(hits) + "\n")
print(len(files), "files scanned,", len(hits), "hits")
