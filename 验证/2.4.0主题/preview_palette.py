"""Preview: run every overview chrome colour through the same Tone() mapping as ModConfigLook.cs and draw both themes.
Also renders the rail shape per level. Output: palette_preview.png next to this script."""
import colorsys, os, re, sys
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
BLUE, PURPLE = 207 / 360, 272 / 360

def hue_dist(a, b):
    d = abs(a - b); return min(d, 1 - d)

def tone(c, dark):
    r, g, b = c[:3]; a = c[3] if len(c) > 3 else 1
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    hue = PURPLE if dark else BLUE
    if s >= .08 and hue_dist(h, hue) < 15 / 360: return c
    if v > .7 and s < .32:
        if not dark: return c
        out = colorsys.hsv_to_rgb(hue, min(1, s * .6 + .22), v)
    else:
        grey = s < .08
        warm = 15 / 360 <= h <= 65 / 360 and s < .7 and v < .8
        cool = 165 / 360 <= h <= 232 / 360 and s < .8
        if not (grey or warm or cool): return c
        if cool and not dark: return c
        sat = (.30 if dark else .34) if grey else max(0, min(.8, s * (1.15 if dark else 1.35)))
        out = colorsys.hsv_to_rgb(hue, sat, v)
    return (*out, a)

SAMPLES = [
    ('Brown frame', (.16, .13, .10, .96)), ('Gold border', (.70, .60, .37)), ('Ivory text', (.94, .91, .81)),
    ('Muted text', (.70, .70, .64)), ('Neutral btn', (0x4a / 255, 0x3b / 255, 0x29 / 255)), ('Edit btn', (0x26 / 255, 0x40 / 255, 0x59 / 255)),
    ('Overview btn', (0x35 / 255, 0x55 / 255, 0x52 / 255)), ('Active tab', (.20, .48, .60)), ('Header', (.12, .13, .15, .92)),
    ('Row', (.10, .11, .13, .4)), ('Selected row', (.18, .34, .43, .72)), ('Mgmt window', (.16, .13, .10)),
    ('Input', (.11, .10, .08, .98)), ('Scrollbar', (.1, .1, .1, .5)), ('Col line', (.77, .76, .66, .8)),
    ('Backdrop', (.05, .07, .07, .84)), ('Commit btn', (0x26 / 255, 0x4d / 255, 0x33 / 255)), ('Danger btn', (0x66 / 255, 0x1a / 255, 0x21 / 255)),
    ('Status blue', (.43, .68, .92)), ('Warn orange', (.95, .57, .43)), ('Group green', (0x69 / 255, 0xB8 / 255, 0x7B / 255)),
]
GAME_BOX = (0.0708, 0.1818, 0.2830); PURPLE_BOX = (0.086, 0.043, 0.137)

def rgb255(c): return tuple(int(max(0, min(1, x)) * 255) for x in c[:3])

W, ROWH = 760, 30
img = Image.new('RGB', (W, 60 + ROWH * len(SAMPLES) + 220), (24, 24, 24))
d = ImageDraw.Draw(img)
try: font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 15)
except OSError: font = ImageFont.load_default()
d.text((10, 10), 'original', fill=(220, 220, 220), font=font)
d.text((250, 10), 'blue theme', fill=(220, 220, 220), font=font)
d.text((500, 10), 'purple theme', fill=(220, 220, 220), font=font)
for i, (name, c) in enumerate(SAMPLES):
    y = 40 + i * ROWH
    for col, (x, dark) in enumerate([(10, None), (250, False), (500, True)]):
        bg = rgb255(GAME_BOX if dark is False else PURPLE_BOX if dark else (40, 34, 28))
        if dark is None: bg = (40, 34, 28)
        d.rectangle([x, y, x + 230, y + ROWH - 4], fill=bg)
        cc = c if dark is None else tone(c, dark)
        d.rectangle([x + 4, y + 3, x + 60, y + ROWH - 7], fill=rgb255(cc))
        d.text((x + 68, y + 4), name, fill=(235, 235, 235), font=font)

# rail preview: levels 0..3 bottom-aligned
y0 = 60 + ROWH * len(SAMPLES)
d.text((10, y0), 'group rails (overview, row height 34, base width 3 ×4 zoom)', fill=(220, 220, 220), font=font)
x = 20
for level in range(4):
    h = max(.5, 1 - .25 * level); w = max(3 * .5, 3 - level)
    rh = 34 * h; top = y0 + 30 + (34 - rh) * 4
    d.rectangle([x, top, x + w * 4, y0 + 30 + 34 * 4], fill=(124, 199, 230))
    x += (w + 2) * 4
d.rectangle([x + 20, y0 + 30, x + 20 + 2 * 4, y0 + 30 + 34 * 4], fill=(200, 240, 90))
d.text((x + 40, y0 + 90), 'pin accent #C8F05A, 2px', fill=(220, 220, 220), font=font)
img.save(os.path.join(HERE, 'palette_preview.png'))
print('saved')
