from pathlib import Path
r=Path(__file__).resolve().parents[2]/'src/Overview'
s=(r/'OverviewPage.SourceTools.cs').read_text(encoding='utf-8-sig')
u=(r/'SourceUi.cs').read_text(encoding='utf-8-sig')
d=(r/'ModSources.Discovery.cs').read_text(encoding='utf-8-sig')
assert 'PendingMods(_mods)' not in s
assert s.index('CaptureBefore(targets)')<s.index('await ModSources.Catalog')
assert 'Discover(targets, cancellation.Token, true, catalog)' in s
body=u[u.index('internal static void BatchResults(OverviewRepository repo, SourceIdentificationBatch'):u.index('private static string VersionText')]
assert 'ModSources.Apply(' not in body and 'batch.Summary' in body
assert 'batch.AfterManualAction()' in body and 'entry.Reason' in body
assert 'catalogSnapshot ??' in d and 'Apply(mod, catalog)' in d
assert 'ReadInstalledStore(mod) != null' in (r/'ModSources.Installed.cs').read_text(encoding='utf-8-sig')
assert 'CancellationToken.None' not in (r/'SourceUi.AutoIdentify.cs').read_text(encoding='utf-8-sig')
print('PASS 10 frozen batch/full totals/catalog/lifecycle contracts')
