"""2.3.1 源码断言：审查修复的 11 项与整理窗口重做都在源码里，且没有被改回。"""
from pathlib import Path
import re, sys
ROOT = Path(__file__).resolve().parents[2]
def src(p): return (ROOT / p).read_text(encoding='utf-8')
ok = fail = 0
def check(name, cond):
    global ok, fail
    print(('PASS ' if cond else 'FAIL ') + name); ok += bool(cond); fail += (not cond)

applied = src('src/Overview/OverviewRepository.Applied.cs')
check('1 已应用快照接受版本 2', '!GroupTree.SupportedSchema(snapshot.schemaVersion)' in applied and 'snapshot.schemaVersion != 1' not in applied)
check('1 草稿修复接受版本 2', '!GroupTree.SupportedSchema(applied.schemaVersion)' in src('src/Overview/OverviewRepository.DraftRecovery.cs'))
imports = src('Updater/PackImports.cs')
check('2 worker 导入接受默认方案版本 2', 'stateSchema is not (1 or 2)' in imports and 'state?["schemaVersion"]?.GetValue<int>() != 1' not in imports)
scope = src('src/Overview/OverviewRepository.SaveScope.cs')
check('3 分组保存先并入临时规则', re.search(r'SavePackInformation\(ConfigurationSaveScope scope[^{]*\{\s*EnsureLocalPackCopy\(\);[^}]*?MergeEditedRules\(\);', scope, re.S) is not None)
check('4 整理采集认 manifest.json.old', 'File.Exists(manifest + ".old")' in src('Updater/GroupLayoutPlanner.cs'))
check('4 智能分组认 manifest.json.old', '"manifest.json.old"' in src('src/Overview/OverviewPage.SmartGrouping.cs'))
check('4 嵌套判定认 manifest.json.old', 'file += ".old"' in src('src/Overview/NestedMods.cs'))
draft = src('src/Overview/OverviewEnablementDraft.cs')
check('5 计划状态随外层', 'Own(draft, mod) && (mod.Outer == null || !mod.CanToggle || Planned(draft, mod.Outer))' in draft)
check('5 不可启停的内层不连带', 'i.CanToggle && Planned(draft, i)' in draft and 'mod.Outer != null && mod.CanToggle' in draft)
check('6 方案导出剥离 nestedEnabled', 'target.Remove("nestedEnabled")' in src('src/Overview/OverviewProfileExport.cs'))
check('6 配置预设导出剥离 nestedEnabled', 'portable.nestedEnabled.Clear()' in src('src/Overview/ConfigurationPresetFile.cs'))
groups = src('src/Overview/OverviewListView.Groups.cs')
check('7 分组菜单三处失败回滚', groups.count('undo();') == 3 and groups.count('var undo = GroupEditCheckpoint();') == 3)
installer = src('Updater/Installer.cs')
check('8 回退只删本次新建目录', 'plan.CreatedDirectories ?? []' in installer and 'foreach (string to in plan.Moves.Select(m => m.To))' not in installer)
check('8 回退恢复被复用的空目录', 'plan.ReplacedEmptyDirectories ?? []' in installer)
tree = src('src/Overview/GroupTree.cs')
check('9 删除后子分组占原位置', 'siblings.Take(at).Concat(children).Concat(siblings.Skip(at + 1))' in tree)
check('10 编辑器删除文案为上移一层', '组内模组和子分组会上移一层' in src('src/Overview/OverviewGroupEditor.cs'))
palette = src('src/Overview/GroupPalette.cs')
colours = re.findall(r'#[0-9A-Fa-f]{6}', palette)
check('11 色板无重复色值', len(colours) == len({c.upper() for c in colours}))

view = src('src/Overview/LayoutReviewView.cs')
check('窗口：空文件夹没有删除按钮与弹窗', 'ConfirmRecycle' not in view and 'OverviewUi.Confirm(' not in view and 'RecycleBin' not in view)
check('窗口：分区总复选框 + 每行复选框', view.count('OverviewUi.Check(') == 2 and '整理时一并放进回收站' in view)
check('窗口：勾选结果交给确认', 'internal List<string> RecycleSelection()' in view)
inst = src('src/Overview/OverviewPage.Installation.cs')
check('窗口：确认请求带上勾选的空文件夹', 'RecycleEmpty = tree?.RecycleSelection()' in inst)
check('窗口：树状预览窗口放大', '"MCE_InstallationReview", drawTree ? 1680 : 860, drawTree ? 1300 : 560' in inst)
check('窗口：标题区压缩', 'compactSummary: drawTree, dividerY: drawTree ? 84 : 109' in inst)
check('插件侧回收站已移除', not (ROOT / 'src/Overview/RecycleBin.cs').exists())
worker = src('Updater/WorkerParallelOperations.cs')
check('worker：只接受预览列出的空文件夹', 'n.Kind==LayoutNodeKind.Empty' in worker and 'request.RecycleEmpty' in worker)
check('worker：全部移动成功后才回收', installer.index('RecycleBin.SendAll') > installer.index('LayoutBookkeeping(job, plan, rollback: false'))
rb = src('Updater/RecycleBin.cs')
check('worker：回收前复查（plugins 内、无模组文件、无链接）', '"outside plugins"' in rb and '"has mod files"' in rb and '"link"' in rb and 'FofAllowUndo' in rb)
check('worker：不合格只提示不失败', '空文件夹未放进回收站' in installer)
check('事务带上勾选项', 'RecycleAfter = preview.RecycleAfter.ToList()' in src('Updater/QueueWorker.cs'))

versions = [re.search(r'<Version>([^<]+)</Version>', src(p)).group(1) for p in ('ModConfigEnhance.csproj', 'Updater/MCE.UpdateWorker.csproj', 'Desktop/MCE.Desktop.csproj')]
versions += [re.search(r'"version_number": "([^"]+)"', src('manifest.json')).group(1), re.search(r'PluginVersion = "([^"]+)"', src('src/Plugin.cs')).group(1)]
versions += [re.search(r'^# .* (\d+\.\d+\.\d+) ', src('README.md'), re.M).group(1)]
check('版本号一致：' + ','.join(versions), len(set(versions)) == 1)
print(f'=== {ok} passed, {fail} failed ===')
sys.exit(1 if fail else 0)
