import pathlib, subprocess, json, shutil, uuid, os
base = pathlib.Path(__file__).resolve().parent
fixture = base / ('cli-fixture-' + uuid.uuid4().hex)
root = fixture / 'BepInEx'
(root / 'plugins').mkdir(parents=True)
source = base / 'bundle'
# Copy release notices only into the isolated publish output.
for name in ['Mono.Cecil.LICENSE.txt', 'Runtime-LICENSE.TXT', 'Runtime-THIRD-PARTY-NOTICES.TXT']:
    shutil.copy2(base.parents[2] / 'Updater' / 'bin' / 'bundle' / name, source / name)
exe = source / 'MCE.UpdateWorker.exe'
def run(exe, *args, ok=True):
    p = subprocess.run([str(exe), *map(str,args)], capture_output=True, text=True, encoding='utf-8', errors='replace')
    if (p.returncode == 0) != ok: raise AssertionError((args,p.stdout,p.stderr))
    print('PASS', ' '.join(map(str,args)), p.stdout.strip())
    return p
run(exe, '--write-protocol-manifest', source)
cached = root / 'MCE' / 'tools' / 'cached'; shutil.copytree(source,cached)
exe = cached / 'MCE.UpdateWorker.exe'
run(exe, '--protocol')
home = root / 'MCE'; (home / 'commands').mkdir()
command = home / 'commands' / 'user.json'; command.write_text('{"Action":"add"}')
run(exe, '--root', root, '--recover')
assert command.exists() and not (home/'queue.json').exists()
jobid=uuid.uuid4().hex
job={'Id':jobid, 'PackageRoot':str(root/'plugins'/'Example'), 'State':'rollback-waiting'}
(root/'plugins'/'Example').mkdir()
(home/'jobs'/jobid).mkdir(parents=True)
journal=home/'jobs'/jobid/'transaction.json'
plan={'Phase':'committing','Root':str(root),'PackageRoot':job['PackageRoot'],'JobId':jobid,'Originals':[],'Desired':[],'Candidates':[],'Parameters':[]}
journal.write_text(json.dumps(plan))
queue=home/'queue.json';queue.write_text(json.dumps({'SchemaVersion':1,'Jobs':[job]}))
run(exe,'--root',root,'--recovery-status')
assert json.loads(journal.read_text())['Phase']=='committing'
run(exe,'--root',root,'--recover','--journal','update:'+jobid)
assert json.loads(journal.read_text())['Phase']=='rolled-back'
assert json.loads(queue.read_text())['Jobs'][0]['State']=='rolled-back' and command.exists()
# Use a currently live PID as the journal owner: only report waiting; never kill it.
plan['SourceProcessId']=os.getpid();job['SourceProcessId']=os.getpid()
journal.write_text(json.dumps(plan));queue.write_text(json.dumps({'SchemaVersion':1,'Jobs':[job]}))
before=journal.read_bytes(); qb=queue.read_bytes()
p=run(exe,'--root',root,'--recover');assert 'waiting-exit' in p.stdout and journal.read_bytes()==before and queue.read_bytes()==qb
journal.write_text('{broken');before=journal.read_bytes();run(exe,'--root',root,'--recover',ok=False);assert journal.read_bytes()==before and command.exists()
run(exe,'--root',root,'--recover','--journal',str(fixture/'outside.json'),ok=False)
# A higher protocol is rejected before a normal worker can consume commands.
(home/'packs').mkdir();index=home/'packs'/'index.json';index.write_text('{"SchemaVersion":2,"MinimumWorkerProtocol":3}')
run(exe,'--root',root,'--protocol','2','--once',ok=False);assert command.exists()
print('CLI recovery checks passed; no core/game payload dependency; commands untouched.')
