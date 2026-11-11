from pathlib import Path
r=Path(__file__).resolve().parents[2]
g=(r/'src/XUnityMachineTranslationGuard.cs').read_text(encoding='utf-8-sig')
assert 'private void Update()' not in g
for token in ['PatchAfter(plugin,"Initialize"','PatchAfter(plugin,"ReloadTranslations"','"Configure",nameof(EnforceAfterChange)','_enforcing','CreateTranslationJobFor','OnBatchTranslationCompleted']:
 assert token in g,token
s=(r/'src/Overview/SourceUi.cs').read_text(encoding='utf-8-sig')
assert '重置来源并立即自动识别' in s and 'ResetAndIdentify(repo' in s
assert 'identified ? OverviewUi.Blue : Disabled' in (r/'src/Overview/OverviewRowActions.cs').read_text(encoding='utf-8-sig')
assert 'mod.SourceConfirmed && !ModSources.ExplicitLocal(mod)' in (r/'src/Overview/OverviewListView.cs').read_text(encoding='utf-8-sig')
print('PASS event guard/source reset/home tint contracts')
