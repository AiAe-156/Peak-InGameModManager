import sys
from pathlib import Path
root = Path(__file__).resolve().parents[2]
p = root / "src/Overview/ShopWindow.cs"
s = p.read_text(encoding="utf-8")
a = s.index("    private void ShowTask()\n")
b = s.index("    private void ShowUninstallConfirmation(ShopTask task)")
new = (Path(__file__).parent / "showtask_115.cs.txt").read_text(encoding="utf-8")
s = s[:a] + new + s[b:]
s = s.replace("private string ShopText(string text)", "private static string ShopText(string text)", 1)
p.write_text(s, encoding="utf-8")
print("ok")
