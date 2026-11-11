import json, os, sys
bep = sys.argv[1]; tree_path = sys.argv[2]
out = open(sys.argv[3], 'w', encoding='utf-8')
def w(*a): out.write(' '.join(str(x) for x in a) + '\n')
tree = json.load(open(tree_path, encoding='utf-8'))
units = [n['OriginalPath'] for n in tree]
w('preview units', len(units))
manifests = []
plugins_root = os.path.join(bep, 'plugins')
for dp, dn, fn in os.walk(plugins_root):
    rel = 'plugins' + dp[len(plugins_root):].replace(os.sep, '/')
    if any(f.lower() == 'manifest.json' for f in fn): manifests.append(rel)
def owner(p):
    for u in units:
        if p == u or p.startswith(u + '/'): return u
    return None
w('folders with manifest.json', len(manifests))
w('--- packages the preview does not list at all')
for m in manifests:
    if owner(m) is None: w('  ', m)
w('--- preview units that contain other packages inside')
for u in units:
    inner = [m[len(u) + 1:] for m in manifests if m.startswith(u + '/')]
    if inner: w('  ', u, '->', len(inner), inner)
w('--- top-level / container children the preview skipped')
for d in sorted(os.listdir(plugins_root)):
    p = 'plugins/' + d
    if os.path.isdir(os.path.join(plugins_root, d)) and owner(p) is None and not any(u.startswith(p + '/') for u in units):
        files = []
        for dp, dn, fn in os.walk(os.path.join(plugins_root, d)): files += fn
        w('  ', p, '| files', len(files), '| dll', sum(f.lower().endswith('.dll') for f in files), '| manifest', 'manifest.json' in [f.lower() for f in files])
out.close()
