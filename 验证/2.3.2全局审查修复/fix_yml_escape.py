import io, sys

p = sys.argv[1]
s = io.open(p, encoding='utf-8', newline='').read()
lines = s.split('\n')
i = next(k for k, l in enumerate(lines) if k > 170 and l.startswith('        string newline = text.Contains("'))
j = next(k for k, l in enumerate(lines) if k > i and l.startswith("').ToList();"))
BS = chr(92)
good = [
    '        string newline = text.Contains("' + BS + 'r' + BS + 'n") ? "' + BS + 'r' + BS + 'n" : "' + BS + 'n";',
    '        bool trailingNewline = text.EndsWith("' + BS + 'n", StringComparison.Ordinal);',
    '        var lines = text.Replace("' + BS + 'r' + BS + 'n", "' + BS + 'n").Split(\'' + BS + 'n\').ToList();',
]
eol = '\r' if lines[i - 1].endswith('\r') else ''
lines[i:j + 1] = [g + eol for g in good]
io.open(p, 'w', encoding='utf-8', newline='').write('\n'.join(lines))
print('fixed', i, j)
