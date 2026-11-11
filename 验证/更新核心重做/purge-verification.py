import os, re, sys, stat, glob

def force_remove(path):
    if not os.path.exists(path):
        return True
    if os.path.isfile(path):
        try:
            os.chmod(path, stat.S_IWRITE)
        except OSError:
            pass
        for candidate in ('\\\\?\\' + os.path.abspath(path), path):
            try:
                os.remove(candidate); return True
            except OSError:
                continue
        return False
    for root, _, files in os.walk(path):
        for name in files:
            try:
                os.chmod(os.path.join(root, name), stat.S_IWRITE)
            except OSError:
                pass
    ok = True
    for root, dirs, files in os.walk(path, topdown=False):
        for name in files:
            target = os.path.join(root, name)
            for candidate in ('\\\\?\\' + os.path.abspath(target), target):
                try:
                    os.remove(candidate); break
                except OSError:
                    ok = False
        for name in dirs:
            target = os.path.join(root, name)
            for candidate in ('\\\\?\\' + os.path.abspath(target), target):
                try:
                    os.rmdir(candidate); break
                except OSError:
                    ok = False
    for candidate in ('\\\\?\\' + os.path.abspath(path), path):
        try:
            os.rmdir(candidate); return True
        except OSError:
            continue
    return ok

def size(p):
    total = 0
    for d, _, fs in os.walk(p):
        for f in fs:
            try:
                total += os.path.getsize(os.path.join(d, f))
            except OSError:
                pass
    return total

base = sys.argv[1]
targets, removed, failures = [], [], []

# Every subdirectory of RealPackRoundTrip is a run output; its sources live at the top level.
real = os.path.join(base, 'RealPackRoundTrip')
if os.path.isdir(real):
    targets += [os.path.join(real, e) for e in os.listdir(real) if os.path.isdir(os.path.join(real, e))]
# Leaked fixtures: a hostile-looking mod folder name plus a short hex suffix, directly under 验证/.
leak = re.compile(r"^RL .*&\s*%!'\$\(\)[0-9a-f]{6}$")
small = ('ThunderstoreRoundTrip', 'Q203218', 'Q203241', 'Q203310', 'qfinal', 'qint', 'q', 'HF-5d456b0b')
for entry in os.listdir(base):
    path = os.path.join(base, entry)
    if not os.path.isdir(path) or path == real:
        continue
    if leak.match(entry) or entry in small:
        targets.append(path)
targets += glob.glob(os.path.join(base, '*.log'))

for path in targets:
    if not os.path.exists(path):
        continue
    measured = size(path) if os.path.isdir(path) else os.path.getsize(path)
    (removed if force_remove(path) else failures).append((os.path.basename(path), measured))

print('deleted %d items, reclaimed %.2f GB' % (len(removed), sum(r[1] for r in removed) / 1073741824))
for name, s in sorted(removed, key=lambda x: -x[1])[:10]:
    print('  %-42s %8.0f MB' % (name, s / 1048576))
if failures:
    print('FAILED (%d): %s' % (len(failures), [f[0] for f in failures][:8]))
print('verification dir now %.2f GB' % (size(base) / 1073741824))
