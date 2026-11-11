from pathlib import Path
import sys
root = Path(sys.argv[1])
def sub(name, old, new, count=1):
    p = root / name; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (name, old[:80], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
W = 'Updater/WorkerParallelOperations.cs'
sub(W, '''        var plan=LayoutPlanner.Build(root,request,ledger, message => {''',
'''        // 2.3.0 requests carry overview rows and use the group-folder organizer (no renaming, nested groups, nested-package extraction).
        Func<string,LayoutQueueRequest,ShopLedger,Action<string>?,LayoutPlan> build=request.Rows.Count>0?GroupLayoutPlanner.Build:LayoutPlanner.Build;
        var plan=build(root,request,ledger, message => {''')
sub(W, '''        if(request.Mode==InstallationLayoutMode.Disabled)throw new IOException("请选择启用的整理方式。");''',
'''        if(request.Rows.Count==0&&request.Mode==InstallationLayoutMode.Disabled)throw new IOException("请选择启用的整理方式。");''')
I = 'Updater/Installer.cs'
sub(I, '''    static void VerifyLayout(Transaction plan)
    {
        foreach (var move in plan.Moves)
        {
            if (!Directory.Exists(Files.Child(plan.Root, move.To))) throw new IOException("目标目录不存在：" + move.To);
            if (Directory.Exists(Files.Child(plan.Root, move.From))) throw new IOException("源目录仍然存在：" + move.From);
        }
    }''', '''    /// <summary>Later moves may run inside folders an earlier move produced (a renamed category folder, then a package
    /// leaving it), so a move's own destination can legitimately be gone again. What must hold is the end state: every
    /// moved package is at its final place, and the last move into each destination left it there.</summary>
    static void VerifyLayout(Transaction plan)
    {
        if (plan.Layout != null && plan.Layout.Units.Count > 0)
        {
            foreach (var unit in plan.Layout.Units)
                if (!Directory.Exists(Files.Child(plan.Root, unit.NewRoot))) throw new IOException("目标目录不存在：" + unit.NewRoot);
            return;
        }
        foreach (var move in plan.Moves)
        {
            if (!Directory.Exists(Files.Child(plan.Root, move.To))) throw new IOException("目标目录不存在：" + move.To);
            if (Directory.Exists(Files.Child(plan.Root, move.From))) throw new IOException("源目录仍然存在：" + move.From);
        }
    }''')
# Rollback: a container that a later move renamed must be undone in reverse order — already reverse; only the
# "original place occupied" check must ignore an empty folder left by a later extraction.
sub(I, '''            if (Directory.Exists(from) || File.Exists(from)) throw new IOException("原位置已被占用，未回退：" + move.From);''',
'''            if (File.Exists(from) || Directory.Exists(from) && Directory.EnumerateFileSystemEntries(from).Any()) throw new IOException("原位置已被占用，未回退：" + move.From);
            if (Directory.Exists(from)) Directory.Delete(from);''')
# Commit: a destination may exist as an empty folder left behind by an earlier move (an emptied shell); only refuse real content.
sub(I, '''            if (Directory.Exists(to) || File.Exists(to)) throw new IOException("目标位置已被占用：" + move.To);''',
'''            if (File.Exists(to) || Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any()) throw new IOException("目标位置已被占用：" + move.To);
            if (Directory.Exists(to)) Directory.Delete(to);''')
print('ok')
