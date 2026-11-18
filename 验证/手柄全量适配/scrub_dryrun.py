# -*- coding: utf-8 -*-
"""Dry run of the publication scrubber rules against the live BepInEx/config (read only; prints key names, never values)."""
import re, os, sys
key = re.compile(r"(?i)(api[\s_.-]*key|(?<![a-z])secret|(?<![a-z])token|(?<![a-z])pass(word|wd)(?![a-z])|(?<![a-z])pwd(?![a-z])|(?<![a-z])app[\s_.-]*id(?![a-z])|access[\s_.-]*key|private[\s_.-]*key|client[\s_.-]*secret|(?<![a-z])cookie|session[\s_.-]*(id|key)|webhook)")
val = re.compile(r"(sk-[A-Za-z0-9_-]{16,}|sk_tr_[A-Za-z0-9]{8,}|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}|[a-z][a-z0-9+.-]*://[^\s/:@]+:[^\s/@]+@|-----BEGIN [A-Z ]*PRIVATE KEY-----)")
line = re.compile(r"^(\s*)([^=#;\[\]\r\n][^=\r\n]*?)(\s*=\s*)(.*?)(\s*)$")
skip = re.compile(r"^(none|null|default|off|on|disabled|enabled|hidden|-1|0)$", re.I)
root = sys.argv[1]
hits = []; n = 0
for d, _, fs in os.walk(root):
    for f in fs:
        if os.path.splitext(f)[1].lower() not in ('.cfg', '.ini', '.yml', '.yaml', '.toml', '.properties'): continue
        p = os.path.join(d, f); n += 1
        t = open(p, encoding='utf-8', errors='replace').read()
        for ln in t.split('\n'):
            s = ln.strip()
            if s.startswith(('#', ';', '//')): continue
            m = line.match(ln.rstrip('\r'))
            if not m: continue
            k = m.group(2).strip(); v = m.group(4).strip().strip('"')
            if not v: continue
            if val.search(v): hits.append((p, k, 'VALUE-SHAPE', len(v)))
            elif key.search(k) and v.lower() not in ('true', 'false') and not skip.match(v): hits.append((p, k, 'KEYNAME', len(v)))
print('files', n, 'hits', len(hits))
for h in hits: print(os.path.relpath(h[0], root), '|', h[1], '|', h[2], '| len', h[3])
