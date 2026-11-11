"""Read-only aggregation of user-provided FrameDoctor run; no game changes."""
import json
from collections import Counter, defaultdict
from pathlib import Path
run=Path('F:/Games/SteamLibrary/steamapps/common/PEAK/BepInEx/FrameDoctor-Logs/2026-10-31_01-34-14')
events=[json.loads(line) for line in (run/'framedoc_events.jsonl').read_text(encoding='utf-8-sig').splitlines()]
heavy=defaultdict(lambda:{'count':0,'selfMs':0,'maxMs':0})
for e in events:
    if e['type']!='HeavyCall':continue
    key=e['method']+' <- '+(e.get('chain') or '(not recorded)')
    v=heavy[key];v['count']+=1;v['selfMs']+=e['selfMs'];v['maxMs']=max(v['maxMs'],e['selfMs'])
windows=[]
for start,end in [('01:34:48','01:35:05'),('01:35:05','01:36:50'),('01:37:05','01:39:35'),('01:40:05','01:41:20')]:
    spikes=[e for e in events if e['type']=='MacroSpike' and start<=e['ts']<end]
    windows.append({'start':start,'end':end,'spikes':len(spikes),'classes':dict(Counter(e['class'] for e in spikes)),'maxMs':max((e['frameMs'] for e in spikes),default=0)})
previous={};deltas=[]
for e in events:
    if e['type']!='MethodProfile':continue
    key=e['method'];old=previous.get(key);previous[key]=e
    if old and any(s in key for s in ['OverviewPage.Update','OverviewListPump.Update','OverviewDependencyDriver.Update','GameObject.SetActive','TMP_Text.set_text']):
        deltas.append({'tick':e['tick'],'ts':e['ts'],'method':key,'deltaSelfMs':round(e['selfMs']-old['selfMs'],2),'deltaCalls':e['calls']-old['calls']})
result={'run':str(run),'counts':dict(Counter(e['type'] for e in events)), 'windows':windows,
        'heavyCalls':dict(sorted(heavy.items(),key=lambda p:-p[1]['selfMs'])), 'methodDeltas':deltas,
        'note':'MethodProfile outputs may be top-N snapshots; absent methods are not zero. HeavyCall includes GC and uninstrumented descendants. MacroSpike frame can refer to prior-frame timing; do not blindly join by frame.'}
out=Path(__file__).with_name('framedoctor-analysis.json');out.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print('written',out)
print('SetActive/SettingsTABSButton',heavy['UnityEngine.CoreModule:UnityEngine.GameObject.SetActive(System.Boolean) <- Assembly-CSharp:SettingsTABSButton.Update()'])
