from pathlib import Path
r=Path(__file__).resolve().parents[2]/'src/Overview'
a=(r/'ConfigurationIssueReport.cs').read_text(encoding='utf-8-sig')
b=(r/'OverviewUi.Issues.cs').read_text(encoding='utf-8-sig')
c=(r/'OverviewListView.SourceRetry.cs').read_text(encoding='utf-8-sig')
assert 'Category = 5' in a and '来源未识别' in a and '在手动识别之前，该模组将无法自动检测更新' in a
assert 'result.Add(provider)' in a
assert '一键重新识别' in b and '手动识别…' in b
assert 'force: true' in c and 'SourceUi.BatchResults' in c and 'ModSources.Coordinator' in c
assert '_relatedPackages.Contains(p.Key)' in (r/'OverviewListView.Infrastructure.cs').read_text(encoding='utf-8-sig')
print('PASS 10 source-notice/retry/provider wiring contracts')
