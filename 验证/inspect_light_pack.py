from pathlib import Path
import json,zipfile,re,datetime
r=Path('F:/Games/SteamLibrary/steamapps/common/PEAK'); b=r/'BepInEx'; home=b/'MCE/packs'
t=json.loads((b/'MCE/pack-task.json').read_text(encoding='utf-8-sig'));i=json.loads((home/'index.json').read_text(encoding='utf-8-sig'));p=next(p for p in i['Packs'] if p['Id']==i['ActivePackId']); print('TASK',t['State'],t.get('Error'));print('ACTIVE',p['Name'],p['Version'],len(i['ActiveFiles']));print('MISSING_ACTIVE',[f['Path'] for f in i['ActiveFiles'] if not (b/f['Path']).is_file()])
for log in [b/'LogOutput.log',r/'output_log.txt']:
 if not log.exists():continue
 text=log.read_text(encoding='utf-8-sig',errors='replace');lines=text.splitlines();print('LOG',log,'mtime',datetime.datetime.fromtimestamp(log.stat().st_mtime),'lines',len(lines))
 hits=[(n,l) for n,l in enumerate(lines) if re.search('PLL|GoToVoid|\[Error|Exception:',l)]
 for n,l in hits[:45]: print(n+1,l[:650])
for f in i['ActiveFiles']:
 if 'pll' in f['Path'].lower(): print('PLL_FILE',f['Path'])
# Config/profile exports only: inspect metadata and recent ZIP entries, do not scan inventory content.
roots=[b/'config/ModConfigEnhance/exports',b/'config/ModConfigEnhance/export']
ws=home/p['Id']/'workspace';roots += [ws/'exports',ws/'export']
for root in roots:
 if root.exists():
  for z in sorted(root.rglob('*.zip'),key=lambda p:p.stat().st_mtime,reverse=True)[:3]:
   with zipfile.ZipFile(z) as a: print('EXPORT',z,z.stat().st_size,'entries',len(a.infolist()),'cfg',sum(x.filename.endswith('.cfg') for x in a.infolist()));print('EXPORT_MANIFEST',[(x.filename,a.read(x).decode('utf-8-sig',errors='replace')[:1500]) for x in a.infolist() if x.filename=='manifest.json'])
