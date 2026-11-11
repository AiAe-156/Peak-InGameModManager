"""Classify every Color literal in src/ by hue: warm neutrals (brown/gold/ivory) are theme candidates."""
import colorsys, io, os, re, sys
SRC = sys.argv[1]
pat = re.compile(r'new(?: Color)?\(\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?\s*(?:,\s*([0-9./f ]+?))?\)')
pat32 = re.compile(r'new Color32\(0x([0-9a-fA-F]{2}), *0x([0-9a-fA-F]{2}), *0x([0-9a-fA-F]{2})')
rows = []
for dp, _, fs in os.walk(SRC):
    for f in fs:
        if not f.endswith('.cs'): continue
        p = os.path.join(dp, f)
        for i, line in enumerate(io.open(p, encoding='utf-8'), 1):
            if 'Color' not in line and 'new(' not in line: continue
            for m in pat.finditer(line):
                try: r, g, b = (float(x) for x in m.groups()[:3])
                except ValueError: continue
                if max(r, g, b) > 1.0001: continue
                rows.append((os.path.relpath(p, SRC), i, r, g, b, line.strip()[:110]))
            for m in pat32.finditer(line):
                r, g, b = (int(x, 16) / 255 for x in m.groups())
                rows.append((os.path.relpath(p, SRC), i, r, g, b, line.strip()[:110]))
cats = {}
for row in rows:
    _, _, r, g, b, _ = row
    h, l, s = colorsys.rgb_to_hls(r, g, b)
    mx, mn = max(r, g, b), min(r, g, b)
    if mx - mn < 0.025: c = 'grey'
    elif r >= g >= b and (r - b) > 0.02 and s < 0.6: c = 'warm-neutral'
    elif b >= r and (b - r) > 0.02 and s < 0.5: c = 'cool-neutral'
    else: c = 'chromatic'
    cats.setdefault(c, []).append(row)
for c, items in cats.items():
    print(f'== {c}: {len(items)}')
    if c in sys.argv[2:]:
        for f, i, r, g, b, line in items:
            print(f'  {f}:{i}  ({r:.2f},{g:.2f},{b:.2f})  {line}')
