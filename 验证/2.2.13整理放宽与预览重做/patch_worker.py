from pathlib import Path
import sys
root = Path(sys.argv[1])
snip = Path(__file__).with_name('snippets')
def sub(path, old, new, count=1):
    p = root / path; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (path, old[:70], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
def piece(name): return (snip / name).read_text(encoding='utf-8')

L = 'Updater/LayoutPlan.cs'
sub(L, '                OwnershipComplete = true, FileInventoryComplete = true, IsCoreOrPatcher = false, IsProtectedManager = false,',
       '                OwnershipComplete = true, FileInventoryComplete = true, IsCoreOrPatcher = false,\n'
       '                IsProtectedManager = discovered.Any(p => Files.Compare.Equals(p.Guid, "com.aiae.modconfigenhance")),')
sub(L, '? g : "ungrouped", IsLoaded = LayoutFact.No });',
       '? g : "ungrouped", IsLoaded = LayoutFact.No,\n'
       '                    Depth = Slash(Path.GetRelativePath(folder, plugin.File)).Count(c => c == \'/\') });')
sub(L, '            var group = ContainerGroup(folder, name, input.Groups);',
       '            var group = ContainerGroup(folder, name, input.Groups) ?? InferredContainer(root, folder, request, input.Groups);')
sub(L, '    static ShopPackage? FindLedgerEntry(', piece('inferred.cs.txt') + '    static ShopPackage? FindLedgerEntry(')
sub(L, '                plan.Moves.Add(new MovePair { From = unit.OriginalPath, To = unit.DestinationPath });',
       '                plan.Moves.Add(new MovePair { From = unit.OriginalPath, To = unit.DestinationPath });\n'
       '                if (unit.Notes.Contains("ManagerMovedLast")) plan.Details.Add(unit.OriginalPath + "：管理器自身目录，最后移动");')
sub(L, '        foreach (var unit in result.Units)\n        {\n            // The worker reports the category',
       piece('manager_last.cs.txt') + '        foreach (var unit in result.Units)\n        {\n            // The worker reports the category')
sub(L, '                Reasons = string.Join(", ", unit.Reasons)\n            });',
       '                Reasons = string.Join(", ", unit.Reasons), Notes = unit.Notes.ToList(),\n'
       '                Ungrouped = unit.Disposition == InstallationLayoutDisposition.Move && Ungrouped(input, unit.UnitId),\n'
       '                Manager = unit.Notes.Contains("ManagerMovedLast")\n            });')

W = 'Updater/WorkerParallelOperations.cs'
sub(W, 'file.Hash=Files.Hash(Files.Child(root,unit.OldRoot+"/"+Path.GetFileName(file.Path)));',
       'file.Hash=Files.Hash(Files.Child(root,LayoutPaths.OldPath(unit,file)));')
I = 'Updater/Installer.cs'
sub(I, 'string oldPath = Files.Child(plan.Root, unit.OldRoot + "/" + Path.GetFileName(file.Path));',
       'string oldPath = Files.Child(plan.Root, LayoutPaths.OldPath(unit, file));')
sub(I, 'Target = Files.Child(plan.Root, u.OldRoot + "/" + Path.GetFileName(f.Path)), Hash = f.Hash',
       'Target = Files.Child(plan.Root, LayoutPaths.OldPath(u, f)), Hash = f.Hash')
sub(I, 'Path = rollback ? unit.OldRoot + "/" + Path.GetFileName(f.Path) : f.Path, Hash = f.Hash',
       'Path = rollback ? LayoutPaths.OldPath(unit, f) : f.Path, Hash = f.Hash')
P = 'Updater/PackagePlan.cs'
sub(P, 'internal sealed class LayoutUnitChange\n{', piece('layout_paths.cs.txt') + 'internal sealed class LayoutUnitChange\n{')
print('ok')
