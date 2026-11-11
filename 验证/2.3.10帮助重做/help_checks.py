"""Help consistency check (2.3.10): every internal link points to an existing page and anchor, every image exists,
the three languages have the same pages and the same anchors, and no Markdown table is used (the parser has no
table extension). Exit code 1 on any problem.
"""
import os, re, sys
sys.stdout.reconfigure(encoding='utf-8')
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
HELP = os.path.join(ROOT, 'Help')
LANGS = ['zh-cn', 'en-us', 'zh-tw']
PAGES = ['overview', 'mods', 'profiles', 'translation', 'packs', 'authoring', 'sources', 'export', 'faq', 'changelog']
problems = []
anchors = {}
for L in LANGS:
    for p in PAGES:
        path = os.path.join(HELP, L, p + '.md')
        if not os.path.exists(path): problems.append(f'{L}: missing page {p}'); continue
        text = open(path, encoding='utf-8').read()
        anchors[(L, p)] = set(re.findall(r'\{#([a-z0-9-]+)\}', text)) | {'top'}
        for n, line in enumerate(text.split('\n'), 1):
            if line.lstrip().startswith('|') and line.rstrip().endswith('|'): problems.append(f'{L}/{p}:{n}: Markdown table (not rendered)')
for L in LANGS:
    for p in PAGES:
        path = os.path.join(HELP, L, p + '.md')
        if not os.path.exists(path): continue
        text = open(path, encoding='utf-8').read()
        for n, line in enumerate(text.split('\n'), 1):
            for img in re.findall(r'!\[[^\]]*\]\(([^)]+)\)', line):
                if not os.path.exists(os.path.join(HELP, L, img)): problems.append(f'{L}/{p}:{n}: image not found {img}')
            for target in re.findall(r'(?<!!)\[[^\]]+\]\(([^)]+)\)', line):
                if target.startswith('http') or target.startswith('media/'): continue
                page, _, anchor = target.partition('#')
                page = page[:-3] if page.endswith('.md') else page
                page = page or p; anchor = anchor or 'top'
                if page not in PAGES: problems.append(f'{L}/{p}:{n}: unknown page {target}')
                elif anchor not in anchors.get((L, page), set()): problems.append(f'{L}/{p}:{n}: missing anchor {target}')
for p in PAGES:
    sets = {L: anchors.get((L, p), set()) for L in LANGS}
    if len({frozenset(v) for v in sets.values()}) > 1:
        problems.append(f'{p}: anchors differ between languages: ' + '; '.join(f'{L} only {sorted(sets[L] - sets["zh-cn"])}' for L in LANGS if sets[L] != sets['zh-cn']) + f'; zh-cn only {sorted(sets["zh-cn"] - sets["en-us"])}')
for x in problems: print(x)
print(f'-- {len(problems)} problem(s)')
sys.exit(1 if problems else 0)
