import os, sys

base = sys.argv[1]
prefix = '\\\\?\\'
removed = 0
for root, dirs, files in os.walk(base, topdown=False):
    for name in dirs:
        path = os.path.join(root, name)
        try:
            if os.listdir(path):
                continue
        except OSError:
            continue
        for candidate in (prefix + os.path.abspath(path), path):
            try:
                os.rmdir(candidate)
                removed += 1
                break
            except OSError:
                pass
print('removed %d empty directories' % removed)

real = os.path.join(base, 'RealPackRoundTrip')
print('RealPackRoundTrip top level:', sorted(os.listdir(real))[:10] if os.path.isdir(real) else 'GONE')
print('root entries:', len(os.listdir(base)))
need = ['统一保存重构', '更新核心重做', 'ShopChecks', 'PackLibraryChecks', 'InstallationFlowChecks', 'InstallationLayoutChecks', 'ModImportChecks', '应用与导航']
print('regression projects present:', all(os.path.exists(os.path.join(base, d)) for d in need))
for name in need:
    count = sum(1 for _, _, fs in os.walk(os.path.join(base, name)) for f in fs if f.endswith(('.cs', '.csproj')))
    print('  %-24s source files: %d' % (name, count))
