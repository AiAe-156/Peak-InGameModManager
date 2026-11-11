import os, re, sys
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
fails = []
def read(rel): return open(os.path.join(ROOT, rel), encoding='utf-8').read()
def must(text, pattern, label, rel):
    if not re.search(pattern, text): fails.append(f'{label}：在 {rel} 中找不到 /{pattern}/')
def must_not(text, pattern, label, rel):
    if re.search(pattern, text): fails.append(f'{label}：{rel} 仍含 /{pattern}/')

chip = read('src/Overview/StatusChip.cs')
must_not(chip, r'class StatusLane|void Pack\(', '2.3.7：角标位置固定，不紧排（用户拍板）', 'StatusChip.cs')
must(chip, r'Step = 10', '间距缩小为 10', 'StatusChip.cs')
must(chip, r'if \(raycast\) \{ var hit = box\.gameObject\.AddComponent<Image>\(\)', '整个 16×16 热区可悬停', 'StatusChip.cs')
must(chip, r'Restart = new\(', '待重启独立配色', 'StatusChip.cs')

view = read('src/Overview/OverviewListView.cs')
must(view, r'int laneChips=shapeChip\?StatusChip\.ShapeSlot\+1', '固定槽位：预留到最左一个已用槽位', 'OverviewListView.cs')
must(view, r'StatusChip\.NestedSlot', '嵌套角标用优先级', 'OverviewListView.cs')
must(view, r'StatusChip\.ShapeSlot', '资源包/散放角标用优先级', 'OverviewListView.cs')
must(view, r'RenderFold\(\); \}, 22, 26\)', '分组折叠走复用路径', 'OverviewListView.cs')
must_not(view, r'Select\(mod, capturedIndex, data\)', '点击序号不再用构建时下标', 'OverviewListView.cs')

rows = read('src/Overview/OverviewListView.VirtualRows.cs')
must(rows, r'private void ParkBuiltModRows\(\)', '折叠时停放已建行', 'OverviewListView.VirtualRows.cs')
must(rows, r'parked\.DisplayStamp==RowUpdateStamp\(parked\)', '数据变了的行不复用', 'OverviewListView.VirtualRows.cs')

infra = read('src/Overview/OverviewListView.Infrastructure.cs')
must(infra, r'_infrastructureCollapsed = !_infrastructureCollapsed; RenderFold\(\);', '基础组件折叠走复用路径', 'OverviewListView.Infrastructure.cs')

pending = read('src/Overview/ProfilePendingMarkers.cs')
must(pending, r'StatusChip\.Restart \}', '待重启改紫色', 'ProfilePendingMarkers.cs')
must(pending, r'Orange: this enable/disable change is not saved yet', '悬停先说明角标含义', 'ProfilePendingMarkers.cs')

ledger = read('src/Overview/ChangeLedger.cs')
must(ledger, r'StatusChip\.Unread, raycast: true\)', '未读变动角标整格热区', 'ChangeLedger.cs')

pres = read('src/Overview/ModEnablementPresentation.cs')
must(pres, r'internal static bool Active\(OverviewMod mod\) => mod\.ResourcePack \? mod\.TargetEnabled : mod\.Loaded;', '资源包按文件启用判断状态', 'ModEnablementPresentation.cs')

name = read('src/Overview/ModNameLayout.cs')
must(name, r'Math\.Min\(370, tableWidth \* \.27f\)', '名称列加宽', 'ModNameLayout.cs')

for rel in ['src/Overview/OverviewListView.Groups.cs', 'src/Overview/OverviewHotkeyView.cs', 'src/Overview/OverviewPage.SmartGrouping.cs']:
    must_not(read(rel), r'OverviewUi\.Panel\("(Group)?Rail"', '分组色条改纯矩形', rel)

updates = read('src/Overview/OverviewPage.Updates.cs')
must(updates, r'if \(OverviewUpdateQueue\.HasRead\)', '2.3.7：队列真读到之后才比较', 'OverviewPage.Updates.cs')
must(updates, r'if \(_updateInventoryKey == null\) _updateInventoryKey = inventory;', '2.3.7：首次比较只记基准', 'OverviewPage.Updates.cs')
must(read('src/Overview/OverviewPage.Caching.cs'), r'_updateInventoryKey = OverviewUpdateQueue\.HasRead \? UpdateInventoryKey\(\) : null;', '2.3.7：进页重扫后重置基准', 'OverviewPage.Caching.cs')
must(read('src/Overview/OverviewUpdateQueue.cs'), r'_hasRead=true;_snapshot=value\.Snapshot;', '2.3.7：队列已读标记', 'OverviewUpdateQueue.cs')

if fails:
    print('FAIL'); [print(' - ' + f) for f in fails]; sys.exit(1)
print('PASS 2.3.6/2.3.7 源码断言')
