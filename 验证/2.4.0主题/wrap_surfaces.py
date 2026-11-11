"""Wrap dark / neutral chrome colour literals in ModConfigLook.Surface(...) so the overview follows the theme.

Only `new Color(r, g, b[, a])` literals are touched, never `static readonly` initialisers (those are converted by hand),
never document-content files, never semantic colour tables. Dry run by default; pass --apply to write.
"""
import colorsys, io, os, re, sys

SRC = sys.argv[1]
APPLY = '--apply' in sys.argv
INCLUDE_DIRS = ['Overview']
EXCLUDE_FILES = {
    'StatusChip.cs', 'ModEnablementPresentation.cs', 'GroupPalette.cs', 'OverviewDocumentView.cs', 'DocCanvas.cs',
    'IntroductionReadingUi.cs', 'GroupRailShape.cs',
}
EXCLUDE_LINES = {('SourceUi.cs', 319), ('SourceUi.cs', 320)}
LIT = re.compile(r'new Color\(\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?\s*(?:,\s*([^()]+?))?\)')

def neutral(r, g, b):
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    return v < .5 or s < .25

total = 0
for d in INCLUDE_DIRS:
    for dp, _, fs in os.walk(os.path.join(SRC, d)):
        for f in fs:
            if not f.endswith('.cs') or f in EXCLUDE_FILES:
                continue
            p = os.path.join(dp, f)
            lines = io.open(p, encoding='utf-8').read().split('\n')
            changed = False
            for i, line in enumerate(lines, 1):
                if (f, i) in EXCLUDE_LINES or 'static readonly' in line or 'Color.clear' == line.strip():
                    continue
                out, pos, hits = [], 0, 0
                for m in LIT.finditer(line):
                    try:
                        r, g, b = (float(x) for x in m.groups()[:3])
                    except ValueError:
                        continue
                    if max(r, g, b) > 1.0001 or not neutral(r, g, b):
                        continue
                    if line[max(0, m.start() - 22):m.start()].endswith('ModConfigLook.Surface('):
                        continue
                    out.append(line[pos:m.start()])
                    out.append('ModConfigLook.Surface(' + m.group(0) + ')')
                    pos = m.end()
                    hits += 1
                if hits:
                    out.append(line[pos:])
                    new = ''.join(out)
                    total += hits
                    print(f'{os.path.relpath(p, SRC)}:{i}  x{hits}  {line.strip()[:120]}')
                    lines[i - 1] = new
                    changed = True
            if changed and APPLY:
                io.open(p, 'w', encoding='utf-8', newline='').write('\n'.join(lines))
print('total literals:', total, '(applied)' if APPLY else '(dry run)')
