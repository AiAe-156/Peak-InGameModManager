"""Read-only: of the 791 two-argument T(en, zh) call sites, how many have their zh-tw rescued by the catalogue?

Loc.T(en, zh, zh) looks the English up in the active language file first (_inlineSnapshot); only when the English is
not there does it fall back to the zh argument for both Simplified and Traditional. The built-in language files are
filled from AllInlineTerms, so a 2-arg call is correct in zh-tw only if its English is a catalogue term.
"""
import os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'src')
STR = r'"((?:[^"\\]|\\.)*)"'
term_re = re.compile(r'new\(\s*' + STR + r'\s*,\s*' + STR + r'\s*,\s*' + STR + r'\s*,\s*' + STR + r'\s*\)')
cat = {}
for f in os.listdir(SRC):
    if f.startswith('Loc') and f.endswith('.cs'):
        for m in term_re.finditer(open(os.path.join(SRC, f), encoding='utf-8').read()):
            cat[m.group(1).strip()] = m.group(4)
two = re.compile(r'(?<![\w.])T\(\s*' + STR + r'\s*,\s*' + STR + r'\s*\)')
total = rescued = 0; per = {}; samples = {}
for d, _, fs in os.walk(SRC):
    if os.sep + 'bin' in d or os.sep + 'obj' in d: continue
    for f in fs:
        if not f.endswith('.cs') or f.startswith('Loc'): continue
        p = os.path.join(d, f)
        for n, line in enumerate(open(p, encoding='utf-8'), 1):
            for m in two.finditer(line):
                total += 1
                en = m.group(1).strip()
                if en in cat: rescued += 1
                else:
                    per[f] = per.get(f, 0) + 1
                    samples.setdefault(f, []).append(f'{n}: {m.group(2)[:40]}')
print(f'two-arg calls: {total}; zh-tw rescued by catalogue: {rescued}; shows Simplified in zh-tw: {total-rescued}')
for f, c in sorted(per.items(), key=lambda x: -x[1]): print(f'  {f}: {c}   e.g. {samples[f][0]}')
