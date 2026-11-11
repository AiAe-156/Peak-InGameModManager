from pathlib import Path
r=Path(__file__).resolve().parents[2]
def read(p):return (r/p).read_text(encoding='utf-8-sig')
s=read('src/Overview/OverviewListView.VirtualRows.cs');c=read('src/Overview/OverviewCascade.cs');v=read('src/Overview/OverviewListView.cs');f=read('src/Updating/PluginFileNames.cs')
checks={
'no scroll eviction':'ParkRow(' not in s and 'Cache(row)' not in s,
'no viewport creation gate':'VirtualRowRange.Contains' not in s,
'loading continues stationary':'if(!_residentPending && immediate==null)return' in s,
'loaded views never rebound':'if(row.View.gameObject.activeSelf)continue' in s,
'whole model walked':'ResidentBuildOrder.Create(_virtualRows.Count,first)' in s and 'foreach(var row in _buildOrder)' in s,
'progress continues after batch':'_residentPending=pending' in s,
'reentry no replay when complete':'if(_residentPending && !_cascade.HasSequence)PlayAppearance()' in v,
'new row fade has no 2 second expiry':'_appendUntil' not in c,
'fade registered for offscreen row':'Enqueue(new Row(row),_appendWave++)' in c,
'no suffix whitelist':'suffix.Equals(".old"' not in f and 'private static bool HasBackupSuffix(string path) => IsDisabled(path)' in f,
'active DLL wins':'if(active!=null)return copies.Where' in f,
'absent DLL multiple candidates retained':'With no active DLL all suffixed files are disabled candidates' in f,
'200ms retained':'Delay = .200f' in read('src/Overview/HoverDwell.cs'),
'refresh centered retained':'rect.offsetMin=min;rect.offsetMax=max' in read('src/Overview/RefreshIconFeedback.cs'),
'profile distinction retained':'游戏当前使用：{0}' in read('src/Overview/OverviewPage.Profiles.cs'),
}
for k,ok in checks.items():print(('PASS ' if ok else 'FAIL ')+k)
assert all(checks.values())
print('RESULT',len(checks),'source contracts passed; Unity acceptance pending')
