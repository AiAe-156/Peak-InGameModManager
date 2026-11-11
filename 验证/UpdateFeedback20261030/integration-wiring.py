from pathlib import Path
root = Path(__file__).resolve().parents[2]
menu = (root/'src/Overview/OverviewListView.cs').read_text(encoding='utf-8-sig')
page = (root/'src/Overview/OverviewPage.Updates.cs').read_text(encoding='utf-8-sig')
queue = (root/'src/Overview/OverviewUpdateQueue.cs').read_text(encoding='utf-8-sig')
terms = (root/'src/Loc.UpdaterTerms.cs').read_text(encoding='utf-8-sig')
checks = [
 ('latest menu has latest callback', 'if (selected == "update") OverviewUpdateQueue.Enqueue(_repo, new[] { mod }, _status);' in menu),
 ('restore has separate callback', 'else if (selected == "restore") OverviewUpdateQueue.EnqueueModpackVersion(_repo, mod, _status);' in menu),
 ('restore only shown with pin', 'if (pinned.Length > 0) options.Add' in menu),
 ('old misleading hint gone', 'Queue actions use this version.' not in menu),
 ('batch entry connected', 'QueueAllUpdates,' in page),
 ('component count comes from checked result', 'InfrastructurePackages.Results.Where' in page and 'components.Length' in page),
 ('components are not turned into ordinary targets', 'EnqueueAll(_repo, mods,' in page and 'new OverviewMod' not in page),
 ('each component has its own store entry', 'options.AddRange(components.Select' in page and 'ShopWindow.Open(_repo, mods, false, selected)' in page),
 ('component warning not silently discarded', 'OverviewUi.ShowText' in page and 'message + "\\n\\n" + notice' in page),
 ('backend core safety remains', 'UpdateWritePolicy.PackageRootAllowed' in queue and 'Protected component: use the store component action preview; Core stays read-only.' in queue),
 ('preview phrase registered', 'new("Preview component: {0}"' in terms),
 ('batch warning registered', 'new("Checked component updates: {0}.' in terms),
]
for name, ok in checks:
 print(('PASS ' if ok else 'FAIL ') + name)
assert all(ok for _,ok in checks)
print(f'TOTAL {len(checks)} integration source wiring checks passed; not Unity visual acceptance')
