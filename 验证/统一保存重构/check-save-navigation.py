from pathlib import Path
root = Path(__file__).resolve().parents[2]
s = (root / 'src/Overview/OverviewPage.Profiles.cs').read_text(encoding='utf-8-sig')
body = s[s.index('    private bool SaveCurrentProfile('):s.index('    private void ShowOriginalPresetSaveChoice(')]
checks = [
    ('leave continuation enters save', '() => SaveCurrentProfile(onSaved: leave)' in s),
    ('success invokes continuation', 'onSaved?.Invoke();' in body),
    ('resource confirmation preserves decisions', 'true, overwriteExternal, skipEnablement, adviceApproved, onSaved)' in body),
    ('author confirmation preserves continuation', 'ShowOriginalPresetSaveChoice(() => SaveCurrentProfile(' in body and 'adviceApproved, onSaved)); return false;' in body),
    ('compatibility confirmation preserves continuation', 'skipEnablement, true, onSaved)' in body),
    ('external confirmation preserves skip choice', 'true, skipEnablement, adviceApproved, onSaved)' in body),
    ('dependency confirmation preserves overwrite choice', 'true, true, resourcesApproved, overwriteExternal, skipEnablement, adviceApproved, onSaved)' in body),
    ('skip confirmation preserves continuation', 'overwriteExternal, true, adviceApproved, onSaved)' in body),
    ('save path submits only queued updates', 'SubmitResourceDraft' not in s),
]
for label, ok in checks:
    assert ok, label
print(f'PASS {len(checks)} save/navigation source-contract checks')
