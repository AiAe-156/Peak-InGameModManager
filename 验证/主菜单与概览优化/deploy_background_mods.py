"""Deploy only the three approved background-mod fixes after offline verification.
No process management, configuration writes, directory migration or routine hashing.
Run manually only after the integration gate passes.
"""
from pathlib import Path
from datetime import datetime, timezone
import json
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
MODS = HERE.parents[2]
BEP = MODS.parent / 'BepInEx'
SPEC = [
    ('汉化/BetterReviveTeleport', 'BetterReviveOrTeleport.dll'),
    ('Better_MicIndicator', 'Better_MicIndicator.dll'),
    ('显示玩家距离/更好的显示玩家距离2', 'BetterPlayerDistance.dll'),
]
STAMP = datetime.now().strftime('%Y%m%d_%H%M%S')
ATTEMPT = HERE / ('background_deployment_' + STAMP)
ATTEMPT.mkdir(exist_ok=False)
RESULT = {'status': 'preflight', 'startedUtc': datetime.now(timezone.utc).isoformat(), 'mods': []}


def save():
    (ATTEMPT / 'result.json').write_text(json.dumps(RESULT, ensure_ascii=False, indent=2), encoding='utf-8')


def processes():
    command = "$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); $p=@(Get-Process -Name PEAK,MCE.UpdateWorker,MCE.Desktop -ErrorAction SilentlyContinue); ConvertTo-Json -InputObject @($p | Select-Object ProcessName,Id,Path) -Compress; exit 0"
    result = subprocess.run(['powershell.exe', '-NoProfile', '-Command', command], capture_output=True, encoding='utf-8', check=True)
    found = json.loads(result.stdout.strip() or '[]')
    RESULT['processes'] = found
    save()
    if found:
        raise RuntimeError('Game or MCE helper/viewer is running; no process was stopped: ' + json.dumps(found))


def safe_path(path):
    for p in [path, *path.parents]:
        if p.exists() and (p.is_symlink() or p.is_junction()):
            raise RuntimeError('Linked deployment path: ' + str(p))


def installations(binary):
    found = []
    for top in BEP.iterdir():
        if not top.is_dir() or top.name.lower() in {'core', 'config', 'patchers', 'cache', 'mce', 'framedoctor-logs'}:
            continue
        found.extend(p for p in top.rglob(binary) if p.is_file())
    return found


try:
    processes()
    preflight = json.loads((HERE / 'installations-preflight.json').read_text(encoding='utf-8'))
    prepared = []
    for relative, binary in SPEC:
        project = MODS / relative
        old = [r for r in preflight if r['name'] == binary]
        found = installations(binary)
        if len(old) != 1 or len(found) != 1 or found[0] != Path(old[0]['path']):
            raise RuntimeError('Installation moved or duplicate copies require confirmation: ' + binary)
        target = found[0].parent
        safe_path(target)
        source = project / ('bin/Release' if binary == 'BetterReviveOrTeleport.dll' else 'bin/Release/netstandard2.1') / binary
        if not source.is_file() or source.stat().st_size == 0:
            raise RuntimeError('Missing built plugin: ' + str(source))
        sources = [source] + [project / n for n in ['README.md', 'CHANGELOG.md', 'manifest.json'] if (project / n).is_file()]
        # A new candidate must include player-facing maintenance notes.
        if not (project / 'CHANGELOG.md').is_file():
            raise RuntimeError('Missing changelog: ' + relative)
        backup = project / '备份' / ('部署前_主菜单空转优化_' + STAMP) / 'installed'
        shutil.copytree(target, backup)
        row = {'project': str(project), 'target': str(target), 'backup': str(backup), 'status': 'backed-up',
               'files': [{'source': str(p), 'relative': p.name, 'size': p.stat().st_size} for p in sources]}
        RESULT['mods'].append(row)
        prepared.append((target, backup, sources, row))
        save()
    processes()
    for target, backup, sources, row in prepared:
        processes()
        if installations(sources[0].name) != [target / sources[0].name]:
            raise RuntimeError('Installation changed immediately before copy')
        row['status'] = 'copying'
        save()
        try:
            for source in sources:
                dest = target / source.name
                safe_path(dest)
                shutil.copy2(source, dest)
                if not dest.is_file() or dest.stat().st_size != source.stat().st_size:
                    raise RuntimeError('Copy verification failed: ' + str(dest))
            row['status'] = 'completed'
            row['completedUtc'] = datetime.now(timezone.utc).isoformat()
        except Exception:
            # Only undo this approved payload; unrelated installed files stay untouched.
            for source in sources:
                previous = backup / source.name
                dest = target / source.name
                if previous.exists():
                    shutil.copy2(previous, dest)
                elif dest.exists():
                    dest.unlink()
            row['status'] = 'rolled-back'
            save()
            raise
        save()
    RESULT['status'] = 'completed'
    RESULT['completedUtc'] = datetime.now(timezone.utc).isoformat()
    save()
    print('Deployed 3 approved mods; result: ' + str(ATTEMPT / 'result.json'))
except Exception as exc:
    RESULT['status'] = 'blocked-or-failed'
    RESULT['error'] = str(exc)
    save()
    print('Deployment stopped: ' + str(exc), file=sys.stderr)
    sys.exit(1)
