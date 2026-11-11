from pathlib import Path
from datetime import datetime
import json, subprocess
ROOT=Path(__file__).resolve().parents[3]
HERE=Path(__file__).parent
command='$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); $p=@(Get-Process -ErrorAction Stop | Where-Object {$_.ProcessName -in @("PEAK","MCE.UpdateWorker")} | Select-Object ProcessName,Id,Path); ConvertTo-Json -InputObject $p -Compress'
p=subprocess.run(['powershell.exe','-NoProfile','-Command',command],capture_output=True,encoding='utf-8',check=True)
processes=json.loads(p.stdout)
if processes: raise SystemExit('Running processes, STOP without killing: '+p.stdout)
now=datetime.now().astimezone(); stamp=now.strftime('%Y%m%d_%H%M%S')
run=HERE/('attempt_delivery_'+stamp); run.mkdir(exist_ok=False)
plan={'prepared_at':now.isoformat(),'version':'2.0.0','zip':str(ROOT/'发布'/f'ModConfigEnhance_2.0.0_{stamp}_dependency-card-fix.zip'),'backup':str(ROOT/'备份'/('部署前_依赖提示布局修复_'+stamp)/'installed'),'verification':str(HERE/'summary.json'),'preparation_processes':processes}
(run/f'plan_{stamp}.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2),encoding='utf-8')
old=ROOT/'验证/IndependentEnvironment20261028/ReleaseFinal/attempt_delivery_20261028_225517/deploy_20261028_225517.py'
script=old.read_text(encoding='utf-8').replace("STAMP = '20261028_225517'",f"STAMP = '{stamp}'")
script=script.replace("validation=json.loads((HERE.parent/'summary-final.json').read_text(encoding='utf-8'))","validation=json.loads(Path(PLAN['verification']).read_text(encoding='utf-8'))")
(run/f'deploy_{stamp}.py').write_text(script,encoding='utf-8')
(HERE/'delivery-current.json').write_text(json.dumps({'stamp':stamp,'script':str(run/f'deploy_{stamp}.py'),'plan':plan},ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'stamp':stamp,'script':str(run/f'deploy_{stamp}.py'),'plan':plan},ensure_ascii=False,indent=2))
