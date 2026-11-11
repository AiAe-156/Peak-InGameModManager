import re, sys, collections
# Usage: python -I parse-build.py <log> — prints distinct compiler errors/warnings, trimmed to file(line,col).
path = sys.argv[1]
seen = collections.OrderedDict()
pattern = re.compile(r'[\\/]([\w.]+\.cs)\((\d+),(\d+)\): (error|warning) (CS\d+|MSB\d+|NETSDK\d+): (.*?)(?: \[|$)')
other = []
for line in open(path, encoding='utf-8', errors='replace'):
    m = pattern.search(line)
    if m:
        key = (m.group(1), m.group(2), m.group(5))
        if key not in seen:
            seen[key] = f'{m.group(4)} {m.group(1)}({m.group(2)},{m.group(3)}) {m.group(5)}: {m.group(6).strip()[:170]}'
    elif ' error ' in line or 'error MSB' in line or 'error NETSDK' in line:
        other.append(line.strip()[:220])
for v in list(seen.values())[:80]:
    print(v)
for o in other[:10]:
    print('OTHER', o)
print(f'--- distinct: {len(seen)} (errors: {sum(1 for v in seen.values() if v.startswith("error"))})')
