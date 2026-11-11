"""Authorized full MCE delivery only; no runtime operations or user-data writes."""
from pathlib import Path, PurePosixPath
from datetime import datetime
import json, os, shutil, subprocess, sys, zipfile

ROOT = Path(__file__).resolve().parents[3]
STAMP = '20261028_173858'
HERE = Path(__file__).resolve().parent
PLAN = json.loads((HERE / f'plan_{STAMP}.json').read_text(encoding='utf-8'))
TARGET = ROOT.parent.parent / 'BepInEx/plugins/0核心&基础模组/[界面]ModConfig增强-ModConfigEnhance'
BACKUP = Path(PLAN['backup'])
ZIP = Path(PLAN['zip'])
RESULT = HERE / f'deployment_{STAMP}.json'
LOG = HERE / f'deployment_{STAMP}.log'
if RESULT.exists() or LOG.exists():
    raise SystemExit('Existing delivery record: stop, do not overwrite')
log = LOG.open('x', encoding='utf-8')
result = dict(PLAN, started_at=datetime.now().astimezone().isoformat(), target=str(TARGET), status='preflight', process_checks=[])

def note(value):
    text = value if isinstance(value, str) else json.dumps(value, ensure_ascii=False)
    print(text, flush=True)
    log.write(text + '\n'); log.flush()

def stopped(stage):
    command = '$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); $p=@(Get-Process -ErrorAction Stop | Where-Object {$_.ProcessName -in @("PEAK","MCE.UpdateWorker") } | Select-Object ProcessName,Id,Path); ConvertTo-Json -InputObject $p -Compress'
    p = subprocess.run(['powershell.exe','-NoProfile','-Command',command], capture_output=True, encoding='utf-8', check=True)
    processes = json.loads(p.stdout)
    result['process_checks'].append(dict(stage=stage, time=datetime.now().astimezone().isoformat(), processes=processes))
    note(result['process_checks'][-1])
    if processes: raise RuntimeError('Game/worker running: deployment stopped, no process killed')

def safe_tree(path):
    for p in [path, *path.parents, *path.rglob('*')]:
        if p.is_symlink() or (hasattr(p, 'is_junction') and p.is_junction()):
            raise RuntimeError('Link/junction rejected: ' + str(p))

def route():
    b = ROOT.parent.parent / 'BepInEx'
    folder = TARGET.name
    candidates = [TARGET, b/folder, b/'0核心&基础模组'/folder, b/'ModConfigEnhance', b/'plugins/ModConfigEnhance']
    checks = [dict(path=str(p), exists=p.exists()) for p in candidates]
    result['route_checks'] = checks
    note({'explicit_install_cache_candidates': checks})
    if not TARGET.is_dir() or not (TARGET/'ModConfigEnhance.dll').is_file() or any(p.exists() for p in candidates[1:]):
        raise RuntimeError('Installation/cache conflict or route changed; coordination required')
    safe_tree(TARGET)

def tree_inventory(path):
    return {p.relative_to(path).as_posix(): {'size':p.stat().st_size, 'mtime_ns':p.stat().st_mtime_ns} for p in path.rglob('*') if p.is_file()}

copy_started = False
try:
    stopped('before-package-and-backup'); route()
    if BACKUP.exists() or ZIP.exists() or ZIP.with_suffix('.zip.partial').exists():
        raise RuntimeError('Backup/package already exists')
    dotnet = 'C:/Program Files/dotnet/dotnet.exe'
    commands = [
        ('publish', [dotnet,'publish','Updater/MCE.UpdateWorker.csproj','-c','Release','--no-restore','-o','Updater/bin/bundle','-v','quiet','-p:EnableDeployment=false']),
        ('build', [dotnet,'build','ModConfigEnhance.csproj','-c','Release','--no-restore','-p:EnableDeployment=false','-t:Build;CollectReleaseFiles','-v','quiet'])]
    for label, command in commands:
        path = HERE/f'{label}_{STAMP}.log'
        with path.open('x',encoding='utf-8') as output:
            p = subprocess.run(command,cwd=ROOT,stdout=output,stderr=subprocess.STDOUT,encoding='utf-8')
        entry = dict(command=command,exit=p.returncode,log=str(path))
        result.setdefault('build_results',[]).append(entry); note(entry)
        if p.returncode: raise RuntimeError(label+' failed; stop')
    worker_project = (ROOT/'Updater/MCE.UpdateWorker.csproj').read_text(encoding='utf-8')
    for setting in ['<SelfContained>true</SelfContained>','<PublishSingleFile>true</PublishSingleFile>','<RuntimeIdentifier>win-x64</RuntimeIdentifier>']:
        if setting not in worker_project: raise RuntimeError('Worker self-contained contract missing')
    result['worker_bundle'] = dict(self_contained=True,single_file=True,rid='win-x64')
    files = []
    for line in (ROOT/'bin/Release/netstandard2.1/release-files.txt').read_text(encoding='utf-8-sig').splitlines():
        source, name = line.split('|', 1)
        name = name.replace('\\','/')
        rel = PurePosixPath(name)
        if rel.is_absolute() or '..' in rel.parts or ':' in name or not Path(source).is_file():
            raise RuntimeError('Invalid release payload: ' + line)
        files.append((Path(source), name))
    names = {name.lower() for _,name in files}
    if len(names) != len(files): raise RuntimeError('Duplicate release paths')
    expected = {'modconfigenhance.dll','markdig.dll','yamldotnet.dll','tools/updater/mce.updateworker.exe'}
    expected |= {n.lower() for n in ['README.md','CHANGELOG.md','manifest.json','icon.png','LICENSE.txt','THIRD-PARTY-NOTICES.md']}
    expected |= {'demo/'+p.relative_to(ROOT/'Demo').as_posix().lower() for p in (ROOT/'Demo').rglob('*') if p.is_file() and '.' in p.name}
    expected |= {'tools/updater/'+p.name.lower() for p in (ROOT/'Updater/bin/bundle').glob('*.txt')}
    expected |= {'help/'+p.relative_to(ROOT/'Help').as_posix().lower() for p in (ROOT/'Help').rglob('*') if p.is_file() and '.' in p.name and p.suffix.lower() not in {'.md','.json','.partial','.bak'}}
    if expected != names: raise RuntimeError('Incomplete release inventory: ' + str(expected ^ names))
    for binary, inputs in [
        (ROOT/'bin/Release/netstandard2.1/ModConfigEnhance.dll', [ROOT/'ModConfigEnhance.csproj',*ROOT.glob('src/**/*.cs'),*ROOT.glob('Help/**/*.md')]),
        (ROOT/'Updater/bin/bundle/MCE.UpdateWorker.exe', [ROOT/'Updater/MCE.UpdateWorker.csproj',*ROOT.glob('Updater/*.cs'),*ROOT.glob('src/Updating/*.cs')])]:
        latest = max(inputs, key=lambda p:p.stat().st_mtime_ns)
        entry = dict(binary=str(binary),size=binary.stat().st_size,mtime=datetime.fromtimestamp(binary.stat().st_mtime).astimezone().isoformat(),latest_input=str(latest),latest_input_mtime=datetime.fromtimestamp(latest.stat().st_mtime).astimezone().isoformat())
        note(entry); result.setdefault('freshness',[]).append(entry)
        if binary.stat().st_mtime_ns < latest.stat().st_mtime_ns: raise RuntimeError('Stale binary')
    command = [sys.executable, str(ROOT/'tools/package_thunderstore.py'), '--no-build', '--output', str(ZIP)]
    env = dict(os.environ, PYTHONUTF8='1', PYTHONIOENCODING='utf-8')
    env['PATH'] = 'C:\\Program Files\\dotnet;' + env['PATH']
    p = subprocess.run(command, cwd=ROOT, env=env, capture_output=True, encoding='utf-8')
    note({'command': command,'exit':p.returncode,'stdout':p.stdout,'stderr':p.stderr})
    if p.returncode: raise RuntimeError('Packaging failed; stop')
    metadata = {'manifest.json','readme.md','changelog.md','icon.png','license.txt','third-party-notices.md'}
    with zipfile.ZipFile(ZIP) as archive:
        if archive.testzip() is not None: raise RuntimeError('ZIP CRC failed')
        result['zip_crc'] = 'passed'
        wanted = {name if name.lower() in metadata else 'BepInEx/plugins/ModConfigEnhance/'+name:source.stat().st_size for source,name in files}
        if {i.filename:i.file_size for i in archive.infolist()} != wanted: raise RuntimeError('Archive inventory mismatch')
        if json.loads(archive.read('manifest.json'))['version_number'] != '2.0.0': raise RuntimeError('Wrong archive version')
        for name in ['README.md','CHANGELOG.md']:
            if archive.read(name) != (ROOT/name).read_bytes(): raise RuntimeError('Archive documentation differs')
    result['zip_size'] = ZIP.stat().st_size
    before = tree_inventory(TARGET)
    shutil.copytree(TARGET, BACKUP)
    if tree_inventory(TARGET) != before or tree_inventory(BACKUP) != before: raise RuntimeError('Full backup verification failed')
    result['backup_files'] = len(before)
    result['release_inventory'] = [dict(source=str(source),path=name,size=source.stat().st_size) for source,name in files]
    stopped('immediately-before-copy'); route()
    if tree_inventory(TARGET) != before: raise RuntimeError('Installation changed after backup')
    copy_started = True
    for source,name in files:
        dest = TARGET/name
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source,dest)
    checked = []
    for source,name in files:
        dest = TARGET/name
        if not dest.is_file() or dest.stat().st_size != source.stat().st_size: raise RuntimeError('Copy verification failed: '+name)
        checked.append(dict(path=name,size=dest.stat().st_size,exists=True))
    if json.loads((TARGET/'manifest.json').read_text(encoding='utf-8-sig'))['version_number'] != '2.0.0': raise RuntimeError('Wrong installed version')
    for name in ['README.md','CHANGELOG.md']:
        if (TARGET/name).read_bytes() != (ROOT/name).read_bytes(): raise RuntimeError('Installed docs mismatch')
    result.update(status='success',files=len(files),verified_files=checked,completed_at=datetime.now().astimezone().isoformat(),user_data='config/library/queue untouched; no install/uninstall/cleanup/pack switch executed; game not started')
except Exception as error:
    result.update(status='failed', error=str(error), failed_at=datetime.now().astimezone().isoformat())
    if copy_started:
        try:
            shutil.rmtree(TARGET)
            shutil.copytree(BACKUP,TARGET)
            if tree_inventory(TARGET) != tree_inventory(BACKUP): raise RuntimeError('Restored tree mismatch')
            result['rollback'] = 'complete original installation restored'
        except Exception as recovery:
            result['rollback'] = 'FAILED: '+str(recovery)
finally:
    RESULT.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    note(result); log.close()
if result['status'] != 'success': sys.exit(1)
