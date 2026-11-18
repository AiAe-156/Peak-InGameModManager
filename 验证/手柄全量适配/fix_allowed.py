# -*- coding: utf-8 -*-
"""Repair the hardcoded_chinese.py ALLOWED entry for the bilingual newcomer install note (lines 44-45 were split by a
literal newline). Replaces the broken two lines with one line whose key is the text backslash-r backslash-n quote."""
import io, os
p = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '发布前审查', 'hardcoded_chinese.py')
s = io.open(p, encoding='utf-8').read()
lines = s.split('\n')
out = []; i = 0
while i < len(lines):
    if lines[i].rstrip('\r') == "    r'" and i + 1 < len(lines) and 'newcomer install note' in lines[i + 1]:
        bs = chr(92)
        out.append("    '" + bs + bs + 'r' + bs + bs + 'n"' + "': 'newcomer install note (PackPublisher.NewcomerNote): one bilingual text file, Chinese block + English block, on purpose',")
        i += 2; continue
    out.append(lines[i]); i += 1
io.open(p, 'w', encoding='utf-8', newline='').write('\n'.join(out))
print('ok')
