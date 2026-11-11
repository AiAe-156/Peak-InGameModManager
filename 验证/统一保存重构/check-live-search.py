from pathlib import Path
r=Path(__file__).resolve().parents[2]/'src/Overview'
s=(r/'OverviewListView.Search.cs').read_text(encoding='utf-8-sig')
l=(r/'OverviewListView.cs').read_text(encoding='utf-8-sig')
for token in ['OverviewUi.Input(', 'onValueChanged.AddListener', 'Time.unscaledTime + .25f','Input.compositionString','_searchRefreshPending = true','mod.RawName','mod.Guid','mod.PackageName','mod.Author','mod.PluginVersion','mod.PackageVersion','mod.RawDescription','OverviewLanguageBridge.GetDisplayName','OverviewLanguageBridge.GetDescription']:
 assert token in s,token
assert '_fixedChromeSearch' not in l
assert 'oldSelection.transform.IsChildOf(_content)' in l
assert '_overviewSearch.isFocused) RestoreRowFocus' in l
assert 'MatchesOverviewSearch(m)' in l
assert 'FlushOverviewSearch();' in l
assert 'ClearOverviewSearch();' in (r/'OverviewListView.StatusFilters.cs').read_text(encoding='utf-8-sig')
print('PASS 20 live-search debounce/fields/focus wiring contracts')
