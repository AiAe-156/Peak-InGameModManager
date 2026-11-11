"""2.3.0 源码断言：嵌套分组接线、导出版本、整理不改名、拆嵌套、回收站、首次智能分组。"""
from pathlib import Path
import re, sys
ROOT = Path(__file__).resolve().parents[2]
def src(p): return (ROOT / p).read_text(encoding='utf-8')
ok = fail = 0
def check(name, cond):
    global ok, fail
    print(('PASS ' if cond else 'FAIL ') + name); ok += bool(cond); fail += (not cond)

models = src('src/Overview/OverviewModels.cs')
check('OverviewGroup.parentId', 'public string parentId = "";' in models)
check('OverviewMod.Outer/Inner', 'internal OverviewMod? Outer;' in models and 'internal List<OverviewMod> Inner' in models)
editor = src('src/Overview/OverviewGroupEditor.cs')
check('编辑器回滚快照含 parentId', 'order = g.order, parentId = g.parentId' in editor)
check('编辑器有上级分组下拉', 'private void ChooseParent()' in editor and 'GroupTree.MaxDepth' in editor)
check('编辑器删除走上移一层', 'GroupTree.Remove(_repository.State.groups' in editor)
check('编辑器上下移只在同级', 'GroupTree.MoveAmongSiblings' in editor)
check('变更核对含上级分组', '("parentId", Loc.T("Parent group"' in src('src/Overview/ProfileChangeReview.cs'))

# No remaining flat ordering by group.order outside GroupTree.
flat = []
for f in (ROOT / 'src').rglob('*.cs'):
    if 'obj' in f.parts or f.name in ('GroupTree.cs',): continue
    t = f.read_text(encoding='utf-8')
    if re.search(r'groups\.OrderBy\(\s*\w+\s*=>\s*\w+\.order\)', t) or re.search(r'groups\.FirstOrDefault\([^)]*\)\?\.order\s*\?\?\s*int\.MaxValue', t): flat.append(f.name)
check('没有残留的平铺分组排序：' + ','.join(flat), not flat)

lst = src('src/Overview/OverviewListView.cs')
check('概览按树序排序', 'GroupSortKey(m.Rule.groupId)' in lst)
check('概览折叠按子树', 'RowHiddenByCollapse(m.Rule.groupId)' in lst and 'Groups.HeaderHidden(id' in lst)
check('概览标题右键菜单', 'ShowGroupMenu(group, data)' in lst)
check('概览分组标题可拖动挂载', 'BeginGroupDrag(group)' in lst and 'AttachGroup(dragged.id, groupId)' in lst)
check('嵌套模组角标', 'StatusChip.Nested' in lst and 'mod.Outer != null' in lst)
check('外层停用时内层不可开', '_repo.BlockedByOuter(mod)' in lst)

check('侧栏按树', '_groupTree = new GroupTree(repository.State.groups);' in src('src/ModConfigSidebarWidgets.Groups.cs'))
check('快捷键页按树', 'var tree=new GroupTree(_repo.State.groups);' in src('src/Overview/OverviewHotkeyView.cs'))
for f in ['ManagementModSelector.cs', 'LogWindow.cs', 'OverviewDependencyHints.Components.cs', 'ShopWindow.cs', 'ProfileChangeReview.cs']:
    check(f + ' 按树', 'GroupTree(' in src('src/Overview/' + f))

check('启停连带内层', 'OverviewEnablementDraft.SetWithNested(_draftMods, mod, enabled);' in src('src/Overview/OverviewRepository.Profiles.cs'))
draft = src('src/Overview/OverviewEnablementDraft.cs')
check('连带记下并恢复内层', 'nestedEnabled = on.Select' in draft and 'restore.Contains(i.Guid)' in draft)

store = src('src/Overview/OverviewProfileStore.cs')
check('profile.json 有嵌套写 2', 'persisted.schemaVersion = GroupTree.SchemaFor(' in store)
check('导入接受 1/2', 'GroupTree.SupportedSchema(importedSource.schemaVersion)' in store)
check('导出 profile.json 写版本', 'GroupTree.HasNesting(profile["state"]?["groups"]' in src('src/Overview/OverviewProfileExport.cs'))
check('pack-information 写版本', 'schemaVersion = GroupTree.SchemaFor(state.groups)' in src('src/Overview/PackWorkspaceInformation.cs'))
pair = src('src/Overview/ProfilePairPersistence.cs')
check('配置快照仍只认 1', 'config.Value<int>("schemaVersion") != 1' in pair)

inst = src('src/Overview/OverviewPage.Installation.cs')
check('整理请求带行与上级分组', 'Rows = rows.Select(m => new LayoutQueueRow' in inst and 'ParentId = g.parentId' in inst)
check('去掉译名改名入口', 'GroupAndTranslatedOuterFolder' not in inst and '预览分组并使用译名' not in src('src/Overview/OverviewPage.Management.cs'))
planner = src('src/Updating/GroupFolderPlanner.cs')
check('规划器不改模组文件夹名（目标=分组路径/原叶名）', '+ "/" + Leaf(p);' in planner)
check('规划器拆嵌套先内后外', 'ThenByDescending(m => Depth(sim.Current[m]))' in planner)
check('规划器管理器最后', 'OrderBy(m => Paths.Equals(m, manager) ? 1 : 0)' in planner)
check('MMHOOK/MCEPack 不碰', 'parts[1].Equals("MMHOOK"' in planner and 'StartsWith("MCEPack_"' in planner)
worker = src('Updater/WorkerParallelOperations.cs')
check('worker 有行时走新规划器', 'request.Rows.Count>0?GroupLayoutPlanner.Build:LayoutPlanner.Build' in worker)
installer = src('Updater/Installer.cs')
check('执行端校验最终位置', 'foreach (var unit in plan.Layout.Units)' in installer)
check('回退逐级清理本次新建的空分组目录（2.3.1 起只删新建的）', '(plan.CreatedDirectories ?? []).OrderByDescending' in installer)

# 2.3.1：回收站移到 worker，空文件夹改复选框随整理执行（断言在 验证/2.3.1审查修复/source_checks.py）
rb = src('Updater/RecycleBin.cs')
check('回收站允许撤销', 'FofAllowUndo' in rb and 'SHFileOperationW' in rb)
check('回收站只收无模组文件的 plugins 内文件夹', '"outside plugins"' in rb and '"has mod files"' in rb and '"link"' in rb)
view = src('src/Overview/LayoutReviewView.cs')
check('空文件夹有复选框', 'RecycleSelection()' in view)
codes = set(re.findall(r'(?:reasons\[p\]|notes\[p\]|notes\[manager\])\.Add\("([A-Za-z]+)"\)', planner))
missing = [c for c in codes if c not in ('ManagerMovedLast', 'CarriedByFolderRename', 'NestedExtracted') and '"' + c + '"' not in view]
check('整理说明码都有三语说明：' + ','.join(missing), not missing)

smart = src('src/Overview/OverviewPage.SmartGrouping.cs')
check('智能分组只在没有分组时', '_repo.State.groups.Count > 0 || File.Exists(SmartGroupingMarker)' in smart)
check('智能分组确认即保存', '_repo.SavePackInformation(ConfigurationSaveScope.Groups);' in smart)
check('智能分组从页面进入触发', 'MaybeOfferSmartGrouping();' in src('src/Overview/OverviewController.cs'))
print(f'=== {ok} passed, {fail} failed ===')
sys.exit(1 if fail else 0)
