"""2.2.13 源码断言：整理放宽（管理器参与、重复/多份只提示）、子目录文件路径、两行行布局、树状预览接线。"""
from pathlib import Path
import re, sys
ROOT = Path(__file__).resolve().parents[2]
def src(p): return (ROOT / p).read_text(encoding='utf-8')
ok = fail = 0
def check(name, cond):
    global ok, fail
    print(('PASS ' if cond else 'FAIL ') + name); ok += bool(cond); fail += (not cond)
planner = src('src/Updating/InstallationLayoutPlanner.cs')
check('管理器不再进 reasons', 'reasons.Add("ProtectedManagerExcluded")' not in planner and 'notes.Add("ManagerMovedLast")' in planner)
check('同包多份副本只提示', 'notes.Add("DuplicatePluginId")' in planner and 'reasons.Add("DuplicatePluginId")' not in planner)
check('多目录同插件只提示', 'notes.Add("PluginOwnedByMultiplePackages")' in planner and 'reasons.Add("PluginOwnedByMultiplePackages")' not in planner)
check('分组按最浅层插件决定', 'p.Depth == top' in planner)
check('链接/越界/路径长度仍拦截', all(k in planner for k in ['"LinkRejected"', '"SourceOutsidePluginDirectory"', '"PathLengthExceeded"', '"DestinationOccupied"']))
plan = src('Updater/LayoutPlan.cs')
check('改名分类目录按包推断', 'InferredContainer(root, folder, request, input.Groups)' in plan)
check('管理器目录最后移动', 'managerMoves' in plan and 'plan.Moves.Add(move)' in plan)
check('树节点带 Notes/Ungrouped/Manager', 'Notes = unit.Notes.ToList()' in plan and 'Ungrouped = ' in plan and 'Manager = ' in plan)
for f in ['Updater/Installer.cs', 'Updater/WorkerParallelOperations.cs']:
    check(f + ' 不再只取文件名拼旧路径', 'OldRoot + "/" + Path.GetFileName' not in src(f) and 'OldRoot+"/"+Path.GetFileName' not in src(f))
check('LayoutPaths.OldPath 保留包内相对路径', 'path.Substring(newRoot.Length + 1)' in src('Updater/PackagePlan.cs'))
view = src('src/Overview/UpdateQueueView.cs')
check('行布局按实际文字宽度', 'private static void LayoutLines(Row row)' in view and 'LayoutLines(row);' in view)
check('名称不再给版本/速度预留固定列', 'versionsW = 170' not in view and 'speedW = 116' not in view)
inst = src('src/Overview/OverviewPage.Installation.cs')
check('整理预览用树状视图', 'LayoutReviewView.Show(panel, task.Tree' in inst)
rv = src('src/Overview/LayoutReviewView.cs')
check('每个说明代码都有中文', all(('"' + c + '"') in rv for c in re.findall(r'(?:reasons|notes)\.Add\("([A-Za-z]+)"', planner) if c not in ('ManagerMovedLast',)))
check('卡片/行有背景框', 'OverviewUi.Panel("Section"' in rv and 'OverviewUi.Panel("Package"' in rv and 'OverviewUi.Panel("Tag"' in rv)
print(f'=== {ok} passed, {fail} failed ===')
sys.exit(1 if fail else 0)
