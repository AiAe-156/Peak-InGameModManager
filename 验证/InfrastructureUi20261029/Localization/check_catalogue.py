"""Check English-first UI text and the built-in English/Simplified/Traditional catalogue.

--sync adds existing literal translations that have not reached the catalogue yet.
No game configuration or generated personal language file is read or written.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
import ctypes
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / "src"
CATALOGUE = SOURCE / "Loc.InlineTerms.cs"
STRING = r'"(?:\\.|[^"\\])*"'
TERMS = re.compile(r'new\(\s*(' + STRING + r')\s*,\s*(' + STRING + r')\s*,\s*(' + STRING + r')\s*,\s*(' + STRING + r')\s*\)')
DIRECT = re.compile(r'Loc\.(?:T|F)\(\s*(' + STRING + r')\s*,\s*(' + STRING + r')\s*,\s*(' + STRING + r')')
WRAPPED = re.compile(r'(?<![\w.])(?:T|L)\(\s*(' + STRING + r')\s*,\s*(' + STRING + r')(?:\s*,\s*(' + STRING + r'))?')
INLINE = re.compile(r'Loc\.Inline\(\s*(' + STRING + r')\s*\)')
CJK = re.compile(r'[\u3400-\u9fff]')
ARGUMENTS = re.compile(r'(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})')
# These calls deliberately choose machine-readable language/state markers.
CONTROL_MARKERS = {"en", "yes"}


def literal(value: str) -> str:
    return json.loads(value)


def traditional(value: str) -> str:
    # Existing two-argument wrappers have no Traditional Chinese source literal.
    # Use Windows' Chinese character mapping for their new explicit catalogue slot.
    kernel = ctypes.windll.kernel32
    kernel.LCMapStringEx.argtypes = (ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_wchar_p,
                                    ctypes.c_int, ctypes.c_wchar_p, ctypes.c_int,
                                    ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ssize_t)
    kernel.LCMapStringEx.restype = ctypes.c_int
    result = ctypes.create_unicode_buffer(len(value) * 2 + 2)
    count = kernel.LCMapStringEx("zh-TW", 0x04000000, value, len(value), result, len(result), None, None, 0)
    if count == 0:
        raise ctypes.WinError()
    return result[:count]


def catalogue():
    rows = []
    for path in SOURCE.glob("Loc*.cs"):
        text = path.read_text(encoding="utf-8-sig")
        rows.extend((tuple(map(literal, match.groups())), path, text.count("\n", 0, match.start()) + 1)
                    for match in TERMS.finditer(text))
    return rows


def calls():
    rows = []
    for path in SOURCE.rglob("*.cs"):
        text = path.read_text(encoding="utf-8-sig")
        for regex in (DIRECT, WRAPPED, INLINE):
            for match in regex.finditer(text):
                values = [literal(value) if value is not None else None for value in match.groups()]
                if values[0] in CONTROL_MARKERS:
                    continue
                rows.append((values, path, text.count("\n", 0, match.start()) + 1))
    return rows


def append(rows):
    text = CATALOGUE.read_text(encoding="utf-8-sig")
    position = text.rfind("    };")
    assert position >= 0
    lines = ["        // Component UI and previously uncatalogued English-first messages.\n"]
    for english, simplified, converted in rows:
        values = [english, english, simplified, converted]
        lines.append("        new(" + ", ".join(json.dumps(value, ensure_ascii=False) for value in values) + "),\n")
    CATALOGUE.write_text(text[:position] + "".join(lines) + text[position:], encoding="utf-8", newline="\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--sync", action="store_true")
    args = parser.parse_args()
    rows = catalogue()
    known = {row[0][1] for row in rows}
    candidates = {}
    for values, path, line in calls():
        english = values[0]
        if english not in known:
            candidates.setdefault(english, (values, path, line))
    if args.sync:
        additions = []
        for english, (values, _, _) in candidates.items():
            if len(values) >= 2:
                simplified = values[1]
                converted = values[2] if len(values) > 2 and values[2] is not None else traditional(simplified)
                additions.append((english, simplified, converted))
        if additions:
            append(additions)
        print("Added catalogue terms:", len(additions))
        rows = catalogue()
        known = {row[0][1] for row in rows}
    failures = []
    for values, path, line in rows:
        _, english, simplified, converted = values
        if CJK.search(english):
            failures.append(f"Chinese in English default: {path.relative_to(ROOT)}:{line}")
        expected = set(ARGUMENTS.findall(english))
        if expected != set(ARGUMENTS.findall(simplified)) or expected != set(ARGUMENTS.findall(converted)):
            failures.append(f"Placeholder mismatch: {path.relative_to(ROOT)}:{line}: {english}")
    missing = {}
    for values, path, line in calls():
        if CJK.search(values[0]):
            failures.append(f"Chinese source text: {path.relative_to(ROOT)}:{line}")
        if values[0] not in known:
            missing.setdefault(values[0], f"{path.relative_to(ROOT)}:{line}")
    print("Catalogue entries:", len(rows))
    print("English defaults / translation placeholders:", "PASS" if not failures else "FAIL")
    print("Uncatalogued UI messages:", len(missing))
    groups = defaultdict(set)
    for values, _, _ in rows:
        groups[values[1].strip()].add(values[2:])
    collisions = [key for key, values in groups.items() if len(values) > 1]
    print("Conflicting translations after trimming English keys:", len(collisions))
    failures.extend("Conflicting built-in translation: " + repr(key) for key in collisions)
    for failure in failures:
        print(failure)
    for english, location in missing.items():
        print(location, repr(english))
    return int(bool(failures or missing))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main())
