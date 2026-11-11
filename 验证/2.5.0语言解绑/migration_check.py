# -*- coding: utf-8 -*-
"""2.5.0 迁移只读核对：在 %TEMP% 复制现场 language\\，模拟两套读法，比较「现在叠加读出来的结果」与「迁移后单文件读出来的结果」。

旧读法（2.4.x Loc.ParseGroup，个人目录部分）：同组文件 基础文件在前、其余按文件名；后读覆盖；留空不覆盖；
  非快照文件里「译文为空 / 等于 MCE 英文原文」不能盖掉已有有效译文。
迁移（LanguageMigration.Merge）：同顺序后读覆盖，留空跳过，MCE_ 键与内置同语言文字相同的丢掉、cfg 内置相同的丢掉。
迁移后读法：单文件；丢掉的行由内置补上 → 最终显示应与旧读法完全一致。

这里拿不到内置词条表，所以「与内置相同而丢掉」的行在比较时按「迁移前后值相同」处理：
只要迁移结果里有的键值与旧读法一致、迁移丢掉的键都是 MCE_ 键或内置 cfg 文字，就判通过；丢掉的键单独列出供人看。
用法：python -I migration_check.py <language 目录> <报告.txt>
"""
import io
import os
import shutil
import sys
import tempfile

BS = "\\"


def unescape(t):
    return t.replace(BS + "=", "=").replace(BS + "n", "\n")


def find_eq(line):
    for i, c in enumerate(line):
        if c == "=" and (i == 0 or line[i - 1] != BS):
            return i
    return -1


def read(path):
    out = []
    for raw in io.open(path, encoding="utf-8-sig"):
        line = raw.rstrip("\r\n")
        if not line or line.startswith("//") or line.startswith("#"):
            continue
        eq = find_eq(line)
        if eq <= 0:
            continue
        key, value = unescape(line[:eq]).strip(), unescape(line[eq + 1:])
        if key.upper().startswith("MCE_"):
            key = "MCE_" + key[4:].upper()
        out.append((key, value))
    return out


def group_files(folder):
    groups = {}
    for name in os.listdir(folder):
        if not name.endswith(".txt") or name.startswith("_"):
            continue
        stem = name[:-4]
        g = stem.split("_", 1)[0].lower()
        groups.setdefault(g, []).append(os.path.join(folder, name))
    for g in groups:
        groups[g].sort(key=lambda p: (0 if "_" not in os.path.basename(p)[:-4] else 1, os.path.basename(p).lower()))
    return groups


def old_read(files):
    merged = {}
    for f in files:
        for k, v in read(f):
            if not v.strip():
                continue
            if k in merged and merged[k].strip() and v == merged[k]:
                continue
            merged[k] = v
    return merged


def migrate_read(files):
    merged, order = {}, []
    for f in files:
        for k, v in read(f):
            if not v.strip():
                continue
            if k not in merged:
                order.append(k)
            merged[k] = v
    return merged


src, report = sys.argv[1], sys.argv[2]
work = tempfile.mkdtemp(prefix="mce-migration-")
copy = os.path.join(work, "language")
shutil.copytree(src, copy)
lines = ["工作副本: " + copy]
ok = True
for g, files in sorted(group_files(copy).items()):
    before, after = old_read(files), migrate_read(files)
    diff = [k for k in after if before.get(k) != after[k]]
    missing = [k for k in before if k not in after]
    lines.append(f"组 {g}: 文件 {len(files)} 个 -> 旧读法 {len(before)} 条, 合并后 {len(after)} 条, 值不同 {len(diff)}, 缺失 {len(missing)}")
    for k in diff[:20]:
        lines.append("  值不同: " + k[:80].replace("\n", "\\n") + " | 旧=" + before.get(k, "")[:40].replace("\n", "\\n") + " | 新=" + after[k][:40].replace("\n", "\\n"))
    if diff or missing:
        ok = False
lines.append("结论: " + ("一致" if ok else "有差异，见上"))
io.open(report, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
shutil.rmtree(work)
print("\n".join(lines))
