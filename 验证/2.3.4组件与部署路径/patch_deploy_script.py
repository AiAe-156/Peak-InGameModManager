import io
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "验证")
OLD = '''    if not target.is_dir():
        raise SystemExit(f"现装目录不存在：{target}")
'''
NEW = '''    if not target.is_dir():
        raise SystemExit(f"现装目录不存在：{target}")
    # 分区规则 6.2.1：部署前查一次部署目标与现装位置是否一致（目录整理会挪动现装）。
    checker = PROJECT.parent / "_tools" / "check_deploy_dir.py"
    check = subprocess.run([sys.executable, "-I", str(checker), str(PROJECT / "ModConfigEnhance.csproj")],
                           capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(check.stdout.strip())
    if check.returncode != 0:
        report["Error"] = "部署目标与现装位置不一致，已中止：\\n" + check.stdout
        return finish(1)
'''
for label in ("2.3.3窗口与分组",):
    path = os.path.join(ROOT, label, "deploy.py")
    s = io.open(path, encoding="utf-8", newline="").read()
    nl = "\\r\\n" if "\\r\\n" in s else "\\n"
    old, new = OLD, NEW
    if "\\r\\n" in s:
        old, new = OLD.replace("\\n", "\\r\\n"), NEW.replace("\\n", "\\r\\n")
    assert s.count(old) == 1, path
    io.open(path, "w", encoding="utf-8", newline="").write(s.replace(old, new))
    print("patched", label)
