"""Read-only: how much of MCE's inline UI text is in the exportable term catalogue, and how often zh-tw is missing.

T(en, zhCn, zhTw) works without the catalogue (the inline strings are self-sufficient), but only catalogue terms are
written to language files / exported for translators. This counts inline call sites whose English is not in any
catalogue table, and 2-argument T(en, zh) calls (which have no zh-tw of their own).
"""
import os, re, sys, collections
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SRC = os.path.join(ROOT, 'src')
UPD = os.path.join(ROOT, 'Updater')
DESK = os.path.join(ROOT, 'Desktop')

STR = r'"((?:[^"\\]|\\.)*)"'
term_re = re.compile(r'new\(\s*' + STR + r'\s*,\s*' + STR + r'\s*,\s*' + STR + r'\s*,\s*' + STR + r'\s*\)')
catalogue = set()
for f in os.listdir(SRC):
    if f.startswith('Loc') and f.endswith('.cs'):
        for m in term_re.finditer(open(os.path.join(SRC, f), encoding='utf-8').read()):
            catalogue.add(m.group(1).strip()); catalogue.add(m.group(2).strip())

three = re.compile(r'\b(?:Loc\.)?(T|F)\(\s*' + STR + r'\s*,\s*' + STR + r'\s*,\s*' + STR)
two = re.compile(r'\bT\(\s*' + STR + r'\s*,\s*' + STR + r'\s*\)')
han = re.compile(r'[一-鿿]')
simp_only = set('这为与说开关设检页时还应后发进们个样实际单项现变质务击键选择该条码类户历删换压导读帮将启们')

stats = collections.Counter(); missing = collections.defaultdict(list); twoarg = collections.defaultdict(list); copied = []
for base in (SRC,):
    for d, _, fs in os.walk(base):
        if os.sep + 'bin' in d or os.sep + 'obj' in d: continue
        for f in fs:
            if not f.endswith('.cs') or f.startswith('Loc'): continue
            p = os.path.join(d, f); rel = os.path.relpath(p, ROOT)
            area = rel.split(os.sep)[1] if rel.count(os.sep) > 1 else 'src'
            for n, line in enumerate(open(p, encoding='utf-8'), 1):
                for m in three.finditer(line):
                    stats['three'] += 1
                    en, zc, zt = m.group(2).strip(), m.group(3), m.group(4)
                    if en not in catalogue: stats['three_not_in_catalogue'] += 1; missing[area].append(f'{rel}:{n}  {en[:70]}')
                    if zt == zc and han.search(zc) and any(c in simp_only for c in zc): copied.append(f'{rel}:{n}  {zc[:50]}')
                for m in two.finditer(line):
                    stats['two'] += 1; twoarg[area].append(f'{rel}:{n}  {m.group(1)[:60]}')

# hard-coded Chinese passed straight to UI in src (not via T/F/Loc)
direct = []
ui_call = re.compile(r'(Button|Label|Text|_status|ShowText|Ask\w*|\.text\s*=)\s*\(?[^;]*"[^"]*[一-鿿][^"]*"')
for d, _, fs in os.walk(SRC):
    if os.sep + 'bin' in d or os.sep + 'obj' in d: continue
    for f in fs:
        if not f.endswith('.cs') or f.startswith('Loc'): continue
        p = os.path.join(d, f); rel = os.path.relpath(p, ROOT)
        for n, line in enumerate(open(p, encoding='utf-8'), 1):
            s = line.strip()
            if s.startswith('//') or 'Log.Log' in s or 'LogInfo' in s or 'LogWarning' in s or 'LogError' in s or 'LogDebug' in s: continue
            if ui_call.search(s) and not re.search(r'\b(T|F|Loc\.T|Loc\.F)\(', s): direct.append(f'{rel}:{n}  {s[:110]}')

worker_lang = os.path.join(UPD, 'WorkerLanguage.cs')
worker_txt = open(worker_lang, encoding='utf-8').read() if os.path.exists(worker_lang) else ''
desk_han = []
for d, _, fs in os.walk(DESK):
    if os.sep + 'bin' in d or os.sep + 'obj' in d: continue
    for f in fs:
        if f.endswith(('.cs', '.xaml')):
            p = os.path.join(d, f)
            for n, line in enumerate(open(p, encoding='utf-8', errors='ignore'), 1):
                if han.search(line) and not line.strip().startswith('//'): desk_han.append(f'{os.path.relpath(p, ROOT)}:{n}')

print('catalogue terms:', len(catalogue))
print('three-arg T/F call sites:', stats['three'], ' not in catalogue:', stats['three_not_in_catalogue'])
print('two-arg T(en,zh) call sites:', stats['two'])
print('hard-coded Chinese UI lines (no T/F):', len(direct))
print('zh-tw identical to zh-cn and contains simplified-only chars:', len(copied))
print('worker language file present:', bool(worker_txt), ' desktop lines with Chinese:', len(desk_han))
print('\n== not in catalogue, by area (count, 3 samples)')
for a, v in sorted(missing.items(), key=lambda x: -len(x[1])): print(f'{a}: {len(v)}'); [print('   ', x) for x in v[:3]]
print('\n== two-arg, by area')
for a, v in sorted(twoarg.items(), key=lambda x: -len(x[1])): print(f'{a}: {len(v)}'); [print('   ', x) for x in v[:2]]
print('\n== hard-coded Chinese (first 15)'); [print('   ', x) for x in direct[:15]]
print('\n== zh-tw copied from zh-cn (first 10)'); [print('   ', x) for x in copied[:10]]
