from pathlib import Path
r=Path(__file__).resolve().parents[2]/'src/Overview'
s=(r/'OverviewListView.StatusFilters.cs').read_text(encoding='utf-8-sig')
t=(r/'OverviewTagFilter.cs').read_text(encoding='utf-8-sig')
p=(r/'OverviewPage.Profiles.cs').read_text(encoding='utf-8-sig')
u=(r/'OverviewUi.Issues.cs').read_text(encoding='utf-8-sig')
for tag in ['$update','$duplicate','$enabled','$disabled','$issue','$changed']:
 assert tag in s and tag in t,tag
assert 'bool showOfficial = false' in t and 'showOfficial ? StandardTags' in t
assert 'ShowRelatedMods(ConfigurationIssueReport.Related' in p
assert 'CloseModal(); inspect();' in u and 'Choose(' not in u
assert 'MatchesRelated(m)' in (r/'OverviewListView.cs').read_text(encoding='utf-8-sig')
assert 'Tags.Matches(e.Mod, StatusMatch)' in (r/'OverviewHotkeyView.cs').read_text(encoding='utf-8-sig')
print('PASS 11 status/related-view wiring contracts')
