"""2.4.3 源码断言：交给系统 API 的结构里不得出现 StringBuilder 字段。

背景：LogSavePicker 曾把 [MarshalAs(LPWStr)] StringBuilder 写成 OPENFILENAMEW 的字段；.NET 会抛
MarshalDirectiveException，Unity Mono 却在 Marshal.SizeOf 里直接中止进程（2026-11-10 实机崩溃）。
StringBuilder 只能作为 P/Invoke 参数；结构里的可写缓冲一律 IntPtr + AllocHGlobal。
"""
import re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
failures = []
checked = 0
for folder in ("src", "Updater", "Desktop"):
    for path in (ROOT / folder).rglob("*.cs"):
        if any(part in ("bin", "obj") for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8-sig")
        for match in re.finditer(r"\[StructLayout\([^\]]*\)\]\s*(?:\w+\s+)*?(?:class|struct)\s+(\w+)\s*\{", text):
            checked += 1
            depth, i = 1, match.end()
            while depth and i < len(text):
                depth += {"{": 1, "}": -1}.get(text[i], 0)
                i += 1
            body = text[match.end():i]
            if re.search(r"\bStringBuilder\b", body):
                failures.append(f"{path.relative_to(ROOT)}: {match.group(1)} 含 StringBuilder 字段")

picker = (ROOT / "src/Overview/LogSavePicker.cs").read_text(encoding="utf-8-sig")
for needle in ("AllocHGlobal", "FreeHGlobal", "public IntPtr lpstrFile", "MarshalDirectiveException"):
    if needle not in picker:
        failures.append("LogSavePicker 缺少 " + needle)
window = (ROOT / "src/Overview/LogWindow.cs").read_text(encoding="utf-8-sig")
if "LogSavePicker.FallbackPath" not in window or "导出完成（固定位置）" not in window:
    failures.append("LogWindow 没有对话框不可用时的固定目录兜底")

print(f"checked {checked} interop structs")
for f in failures:
    print("FAIL", f)
print("OK" if not failures else f"{len(failures)} failure(s)")
sys.exit(1 if failures else 0)
