import sys
from PIL import Image
img = Image.open(sys.argv[1]).convert('RGB'); W, H = img.size
print('size', W, H)
# gold-ish pixels: r high, b low
def gold(p): r, g, b = p; return r > 140 and g > 100 and b < 90 and r - b > 80
cols = [x for x in range(W) if any(gold(img.getpixel((x, y))) for y in range(0, H // 2))]
# group columns into runs
runs, start, prev = [], None, None
for x in cols:
    if start is None: start = prev = x; continue
    if x - prev > 3: runs.append((start, prev)); start = x
    prev = x
if start is not None: runs.append((start, prev))
for a, b in runs:
    ys = [y for y in range(0, H // 2) for x in range(a, b + 1) if gold(img.getpixel((x, y)))]
    print(f'x {a}-{b} (w {b - a + 1})  y {min(ys)}-{max(ys)} (h {max(ys) - min(ys) + 1})')
