# -*- coding: utf-8 -*-
"""只读：用户语言文件里的原文条目，哪些不是「导出翻译」会收集的配置页文字。
这些条目现在可能靠 XUnity 显示到游戏别处（模组自己的界面、HUD 等），去掉 XUnity 后会失效。
用法：python -I coverage.py <config/ModConfigEnhance> <输出 txt>"""
import glob
import io
import os
import re
import sys

BS = "\\"


def unescape(text):
    out, i = [], 0
    while i < len(text):
        c = text[i]
        if c == BS and i + 1 < len(text):
            n = text[i + 1]
            out.append({"n": "\n", "=": "=", BS: BS}.get(n, BS + n))
            i += 2
            continue
        out.append(c)
        i += 1
    return "".join(out)


def split(line):
    for i, c in enumerate(line):
        if c == "=" and (i == 0 or line[i - 1] != BS):
            return (line[:i], line[i + 1:]) if i > 0 else None
    return None


def read(path, keep_empty):
    result = {}
    for raw in io.open(path, encoding="utf-8-sig"):
        line = raw.rstrip("\r\n")
        if not line or line.startswith("//") or line.startswith("#"):
            continue
        pair = split(line)
        if pair is None:
            continue
        key, value = unescape(pair[0]).strip(), unescape(pair[1])
        if value.strip() or keep_empty:
            result[key] = value
    return result


root, output = sys.argv[1], sys.argv[2]
lang = {}
for path in sorted(glob.glob(os.path.join(root, "language", "zh-cn*.txt"))):
    lang.update(read(path, False))
exported = set()
for path in glob.glob(os.path.join(root, "export", "*.txt")):
    exported.update(read(path, True).keys())

own = [k for k in lang if k.upper().startswith("MCE_") or k.startswith("ModConfigEnhance.")]
texts = [k for k in lang if k not in own]
outside = sorted(k for k in texts if k not in exported)
print("zh-cn effective entries", len(lang), "| MCE/own keys", len(own), "| original-text entries", len(texts))
print("originals seen in export files", len(exported))
print("original-text entries not in any export file", len(outside))
io.open(output, "w", encoding="utf-8", newline="\n").write("\n".join(k.replace("\n", "\\n") for k in outside) + "\n")
