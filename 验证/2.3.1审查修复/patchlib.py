"""Exact-text patch helper that keeps each file's own line endings and BOM."""
import io
import sys

ROOT = r"F:\Games\SteamLibrary\steamapps\common\PEAK\MODs\ModConfigEnhance"


def sub(rel, old, new, count=1):
    path = ROOT + "\\" + rel.replace("/", "\\")
    raw = io.open(path, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw[3:].decode("utf-8") if bom else raw.decode("utf-8")
    crlf = "\r\n" in text
    work = text.replace("\r\n", "\n")
    old_n, new_n = old.replace("\r\n", "\n"), new.replace("\r\n", "\n")
    found = work.count(old_n)
    if found != count:
        sys.exit(f"{rel}: expected {count} match(es), found {found}: {old_n[:80]!r}")
    work = work.replace(old_n, new_n)
    if crlf:
        work = work.replace("\n", "\r\n")
    data = work.encode("utf-8")
    io.open(path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + data)
    print("ok", rel)
