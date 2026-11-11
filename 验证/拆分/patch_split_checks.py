# -*- coding: utf-8 -*-
"""给 split-checks 补复查指出的场景（#1 #2 #4 #5 #7 #8 #9），并按新规则改场景 B。用法：python -I patch_split_checks.py"""
import io
import os

p = os.path.join(os.path.dirname(os.path.abspath(__file__)), "split-checks", "Program.cs")
t = io.open(p, encoding="utf-8").read()
oldB = '''    var r = ManagerSplitMigration.Run(bep, config, "", busy: false);
    Check(!r.Data && File.ReadAllText(Path.Combine(config, "InGameModManager", "language", "zh-cn.txt")) == "NEW=新", "[B] existing new folder is never overwritten");'''
newB = '''    Write(Path.Combine(config, "ModConfigEnhance", "profiles", "p.json"), "{}");
    var r = ManagerSplitMigration.Run(bep, config, "", busy: false);
    // 复查 #2：新目录已在（别的代码先建了它）也要补齐；已有的文件绝不覆盖
    Check(r.Data && File.ReadAllText(Path.Combine(config, "InGameModManager", "language", "zh-cn.txt")) == "NEW=新" && File.Exists(Path.Combine(config, "InGameModManager", "profiles", "p.json")), "[B] existing new folder: missing files filled in, existing files never overwritten");'''
assert t.count(oldB) == 1
t = t.replace(oldB, newB)
extra = io.open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "split_checks_extra.cs.txt"), encoding="utf-8").read()
anchor = '\nConsole.WriteLine(failed == 0 ? "ALL PASS"'
assert t.count(anchor) == 1
t = t.replace(anchor, "\n" + extra + anchor)
io.open(p, "w", encoding="utf-8", newline="\n").write(t)
print("ok")
