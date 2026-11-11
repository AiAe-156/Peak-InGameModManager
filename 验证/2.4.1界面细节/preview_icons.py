"""Re-draw the three toolbar icons (refresh / group / check) and the theme icon with the same formulas as the C# sprites,
place them in 40px paper squares at the 60% icon box, plus a dropdown with the 14px arrow; save icons_preview.png."""
import math, os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
S = 64

def tri_sign(p, a, b): return (p[0]-b[0])*(a[1]-b[1]) - (a[0]-b[0])*(p[1]-b[1])
def in_tri(p, a, b, c):
    d1, d2, d3 = tri_sign(p, a, b), tri_sign(p, b, c), tri_sign(p, c, a)
    return not ((d1 < 0 or d2 < 0 or d3 < 0) and (d1 > 0 or d2 > 0 or d3 > 0))

def refresh():
    img = Image.new('L', (S, S)); px = img.load()
    radius, half, start, sweep, alen, wing = 22, 5, 55, 285, 17, 9.5
    c = (S/2, S/2); endr = math.radians(start - sweep)
    end = (c[0]+math.cos(endr)*radius, c[1]+math.sin(endr)*radius)
    tan = (math.sin(endr), -math.cos(endr)); nor = (tan[1], -tan[0])
    tip = (end[0]+tan[0]*alen, end[1]+tan[1]*alen)
    wa = (end[0]+nor[0]*wing, end[1]+nor[1]*wing); wb = (end[0]-nor[0]*wing, end[1]-nor[1]*wing)
    for y in range(S):
        for x in range(S):
            p = (x+.5, y+.5); dx, dy = p[0]-c[0], p[1]-c[1]
            ang = math.degrees(math.atan2(dy, dx)); swept = (start - ang) % 360
            on = abs(math.hypot(dx, dy) - radius) <= half and swept <= sweep
            px[x, S-1-y] = 255 if on or in_tri(p, tip, wa, wb) else 0
    return img

def groups():
    img = Image.new('L', (S, S)); px = img.load()
    for y in range(S):
        for x in range(S):
            fx, fy = x+.5, y+.5
            on = any(abs(fy-cy) <= 5 and ((8 <= fx <= 18) or (24 <= fx <= 56)) for cy in (15, 32, 49))
            px[x, S-1-y] = 255 if on else 0
    return img

def seg(p, a, b):
    ab = (b[0]-a[0], b[1]-a[1]); t = max(0, min(1, ((p[0]-a[0])*ab[0]+(p[1]-a[1])*ab[1])/(ab[0]**2+ab[1]**2)))
    return math.hypot(p[0]-(a[0]+ab[0]*t), p[1]-(a[1]+ab[1]*t))

def check():
    img = Image.new('L', (S, S)); px = img.load()
    a, b, c = (11, 33), (26, 17), (53, 47)
    for y in range(S):
        for x in range(S):
            d = min(seg((x+.5, y+.5), a, b), seg((x+.5, y+.5), b, c))
            px[x, S-1-y] = int(255*max(0, min(1, 5.5-d)))
    return img

def theme():
    img = Image.new('L', (S, S)); px = img.load()
    c, bite = (30, 32), (42, 41)
    for y in range(S):
        for x in range(S):
            p = (x+.5, y+.5)
            inside = max(0, min(1, 23-math.hypot(p[0]-c[0], p[1]-c[1])+.5))
            cut = max(0, min(1, 18-math.hypot(p[0]-bite[0], p[1]-bite[1])+.5))
            px[x, S-1-y] = int(255*inside*(1-cut))
    return img

GOLD = (171, 129, 10); CREAM = (223, 218, 194); BOX = (18, 46, 72); BG = (40, 48, 52)
Z = 4
canvas = Image.new('RGB', (520*Z//2, 80*Z//2), BG)
d = ImageDraw.Draw(canvas)
def square(x, icon):
    size = 40*Z//2; d.rounded_rectangle([x, 10*Z//2, x+size, 10*Z//2+size], 8, fill=CREAM)
    box = round(40*.6)*Z//2; ic = icon.resize((box, box), Image.LANCZOS)
    col = Image.new('RGB', ic.size, GOLD); canvas.paste(col, (x+(size-box)//2, 10*Z//2+(size-box)//2), ic)
# dropdowns: 122 & 160 wide, 40 high, arrow 14x8.7, caption to arrow-12
for x, w, text in ((10, 70, '-'), (90, 80, '简体…')):
    X, Wd = x*Z//2, w*Z//2
    d.rounded_rectangle([X, 10*Z//2, X+Wd, 50*Z//2], 8, fill=BOX)
    aw, ah = 14*Z//2, int(14*.62)*Z//2; ax = X+Wd-8*Z//2-aw; ay = 30*Z//2-ah//2
    d.polygon([(ax, ay), (ax+aw, ay), (ax+aw//2, ay+ah)], fill=GOLD)
    d.text((X+8*Z//2, 22*Z//2), 'caption area →' if w > 75 else 'cap', fill=GOLD)
    d.line([(ax-4*Z//2, 14*Z//2), (ax-4*Z//2, 46*Z//2)], fill=(90, 90, 90))
for i, icon in enumerate((refresh(), groups(), check(), theme())):
    square((190+i*48)*Z//2, icon)
canvas.save(os.path.join(HERE, 'icons_preview.png'))
print('saved')
