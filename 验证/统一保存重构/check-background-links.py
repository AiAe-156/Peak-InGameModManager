from pathlib import Path
r=Path(__file__).resolve().parents[2]
a=(r/'src/Overview/OverviewListView.cs').read_text(encoding='utf-8-sig')
b=(r/'src/Overview/OverviewMaintenance.cs').read_text(encoding='utf-8-sig')
c=(r/'src/Overview/ModSources.Discovery.cs').read_text(encoding='utf-8-sig')
d=(r/'src/Overview/SourceUi.cs').read_text(encoding='utf-8-sig')
assert 'if (!ListVisible) return;' in a and 'SourceUi.HomepageHint(mod)' in a
assert 'if (!visible) return;' in b
local=c[c.index('internal static bool ExplicitLocal'):c.index('internal static bool Verified')]
assert 'Records.TryGetValue' in local and 'PersonalScope(record, mod)' in local and 'record.explicitLocal &&' not in local
assert 'action is "project" or "github"' in d and '打开 GitHub 链接' in d
for lang in ['zh-cn','zh-tw','en-us']:
 s=(r/'Help'/lang/'overview.md').read_text(encoding='utf-8')
 for anchor in ['top','start','first-steps','concepts','reading']:assert '{#'+anchor+'}' in s
 assert '250' in s and 'ConfigEntry.Value' in s and 'GitHub' in s
print('PASS background/source-local/menu/three-language guide contracts')
