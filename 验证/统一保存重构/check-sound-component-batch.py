from pathlib import Path
r=Path(__file__).resolve().parents[2]
v=(r/'src/Overview/OverviewListView.VirtualRows.cs').read_text(encoding='utf-8-sig')
p=(r/'src/Overview/OverviewPage.Updates.cs').read_text(encoding='utf-8-sig')
u=(r/'src/Overview/OverviewUpdateQueue.cs').read_text(encoding='utf-8-sig')
s=(r/'src/Overview/OverviewPage.SourceTools.cs').read_text(encoding='utf-8-sig')
assert '_loadedRowSounds.Add(row.Key)' in v and 'CompleteRowSound(row);CompleteLayerFocus(row)' in v
assert 'PlayLoadedRowSound' in (r/'src/Overview/OverviewCascade.cs').read_text(encoding='utf-8-sig')
assert '逐项预览组件' not in p and '仅更新普通模组' not in p
assert 'StageComponentUpdate(component)' in p and '基础组件更新若失败' in p
assert 'Operation = "preview-install"' in u[u.index('internal static void StageComponentUpdate'):u.index('internal static void Enqueue(')]
assert '所有小角标标记为已读' in s and 'ChangeLedger.Clear();' in s
print('PASS 10 row-sound/all-read/component-batch contracts')
