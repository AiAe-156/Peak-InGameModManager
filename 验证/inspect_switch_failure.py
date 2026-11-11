import json
import hashlib
from pathlib import Path
r=Path('F:/Games/SteamLibrary/steamapps/common/PEAK/BepInEx')
t=json.loads((r/'MCE/pack-task.json').read_text(encoding='utf-8-sig'))
d=json.loads((r/'MCE/packs/.transactions'/t['Id']/'switch.json').read_text(encoding='utf-8-sig'))
print('TASK',t['Id'],'STATE',t['State'],'PHASE',d['Phase'],'ERROR',t.get('Error'))
print('REMOVED',len(d['Removed']), 'NEXT', [f['Path'] for f in d['Originals'][len(d['Removed']):len(d['Removed'])+3]])
plan=r/'MCE/packs/.transactions'/t['Id']/'switch.json'
print('PLAN_ATTRIBUTES',plan.stat().st_file_attributes)
print('ORIGINAL_ERROR_OPERATION',t.get('Request',{}).get('Operation'))
print('ORIGINALS',len(d['Originals']),'PRESENT',sum((r/f['Path']).is_file() for f in d['Originals']),'PLACED',len(d['Placed']))
for f in d['Originals'][max(0,len(d['Removed'])-1):len(d['Removed'])+3]:
 p=r/f['Path']; print(f['Path'],'exists',p.exists(),'attributes',getattr(p.stat(),'st_file_attributes',None) if p.exists() else None)
transaction=r/'MCE/packs/.transactions'/t['Id']
mismatches=[]
for f in d['Originals']:
 p=r/f['Path']
 if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest().lower()!=f['Hash'].lower(): mismatches.append(f['Path'])
print('ORIGINAL_CONTENT_MISMATCHES',mismatches)
print('PARTIALS')
for p in transaction.rglob('*.mce-part'): print(p.relative_to(transaction),'attributes',p.stat().st_file_attributes)
