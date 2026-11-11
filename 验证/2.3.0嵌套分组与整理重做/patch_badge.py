from pathlib import Path
import sys
root = Path(sys.argv[1]) / 'src/Overview'
def sub(name, old, new, count=1):
    p = root / name; s = p.read_text(encoding='utf-8')
    assert s.count(old) == count, (name, old[:80], s.count(old))
    p.write_text(s.replace(old, new), encoding='utf-8')
L = 'OverviewListView.cs'
sub(L, '''                int laneChips=(hasModUpdate?1:0)+(mod.HasUpdate?1:0)+(pendingMarker?.NeedsSave==true?1:0)+(pendingMarker?.NeedsRestart==true?1:0);''',
'''                int pendingChips=(pendingMarker?.NeedsSave==true?1:0)+(pendingMarker?.NeedsRestart==true?1:0);
                bool nestedChip=mod.Outer!=null;
                // Slots are positional (unread 0, update 1, pending from 2); the nested chip goes after the pending ones.
                int laneChips=nestedChip?StatusChip.FirstPendingSlot+pendingChips+1:(hasModUpdate?1:0)+(mod.HasUpdate?1:0)+pendingChips;''')
sub(L, '''                BuildConflictBadge(nameMarkers, mod, _currentConflicts, conflictRight);''',
'''                BuildConflictBadge(nameMarkers, mod, _currentConflicts, conflictRight);
                if (nestedChip)
                {
                    var nested = StatusChip.Create(nameMarkers, StatusChip.FirstPendingSlot + pendingChips, StatusChip.Nested, raycast: true);
                    AddHint(nested.gameObject, T("Nested inside ", "内嵌于「") + OverviewLanguageBridge.GetDisplayName(mod.Outer!) + T(". Nested groups are recommended instead of nesting mod folders; folder organization moves it out.", "」。建议用嵌套分组代替文件夹嵌套；目录整理会把它拆出来。"));
                }''')
sub('StatusChip.cs', '''    internal static readonly Color Unread = new(.43f, .68f, .92f, 1f);''',
'''    internal static readonly Color Unread = new(.43f, .68f, .92f, 1f);
    /// <summary>2.3.0: this mod sits inside another mod's folder (amber, like other advice).</summary>
    internal static readonly Color Nested = new(.88f, .63f, .33f, 1f);''')
print('ok')
