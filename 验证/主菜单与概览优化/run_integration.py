"""Serial offline integration gate. Never deploys or starts PEAK."""
from pathlib import Path
from datetime import datetime, timezone
import json
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
RUN = Path(__file__).resolve().parent / ('integration_' + datetime.now().strftime('%Y%m%d_%H%M%S'))
RUN.mkdir(exist_ok=False)
DOTNET = r'C:\Program Files\dotnet\dotnet.exe'
PYTHON = r'C:\Users\AiAe\.workbuddy\binaries\python\envs\default\Scripts\python.exe'
steps = [
    ('worker-publish', [DOTNET, 'publish', 'Updater/MCE.UpdateWorker.csproj', '-c', 'Release', '-o', 'Updater/bin/framework-bundle', '-p:EnableDeployment=false']),
    ('desktop-build', [DOTNET, 'build', 'Desktop/MCE.Desktop.csproj', '-c', 'Release', '-p:EnableDeployment=false']),
    ('desktop-selftest', [str(ROOT / 'Desktop/bin/Release/net48/MCE.Desktop.exe'), '--self-test']),
    ('plugin-release', [DOTNET, 'build', 'ModConfigEnhance.csproj', '-c', 'Release', '-p:EnableDeployment=false', '-t:Build;CollectReleaseFiles']),
]
for name, project in [
    ('installation', 'InstallationFlowChecks/InstallationFlowChecks.csproj'),
    ('shop', 'ShopChecks/ShopChecks.csproj'),
    ('packs', 'PackLibraryChecks/PackLibraryChecks.csproj'),
    ('deployment-policy', 'LocalDeploymentChecks/LocalDeploymentChecks.csproj'),
    ('dropdown', 'DropdownSuffixChecks/DropdownSuffixChecks.csproj'),
    ('settings-dropdown', 'SettingsCompactDropdown20261030/GeometryChecks.csproj'),
    ('update-markers', 'ModUpdateMarkerChecks/ModUpdateMarkerChecks.csproj'),
    ('relationships', 'SharedRelationships20261029/SharedRelationships.csproj'),
    ('catalog', 'SharedStoreCatalog20261029/SharedCatalogChecks.csproj'),
    ('update-flow', 'UpdateFlow20261029/Checks.csproj'),
    ('source-updates', 'SourceUpdateChecks/SourceUpdateChecks.csproj'),
    ('source-offline-import', 'SourceUpdateChecks/OfflineImport/OfflineImport.csproj'),
    ('group-progress', '主菜单与概览优化/GroupProgressChecks/GroupProgressChecks.csproj'),
    ('overview-performance', '主菜单与概览优化/PerformanceChecks/PerformanceChecks.csproj'),
]:
    steps.append((name, [DOTNET, 'run', '--project', '验证/' + project, '-c', 'Release', '-p:EnableDeployment=false']))
steps += [
    ('helper-protocol', [str(ROOT / 'Updater/bin/framework-bundle/MCE.UpdateWorker.exe'), '--validate-protocol-manifest', str(ROOT / 'Updater/bin/framework-bundle')]),
    ('release-inventory', [PYTHON, 'tools/package_thunderstore.py', '--check-only', '--no-build']),
]
summary = {'startedUtc': datetime.now(timezone.utc).isoformat(), 'steps': [], 'status': 'running'}

def save():
    (RUN / 'summary.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')

save()
for name, args in steps:
    print('RUN ' + name, flush=True)
    log = RUN / (name + '.log')
    with log.open('wb') as output:
        result = subprocess.run(args, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT)
    summary['steps'].append({'name': name, 'command': args, 'exitCode': result.returncode, 'log': str(log)})
    save()
    if result.returncode:
        summary['status'] = 'failed'
        save()
        print('FAILED ' + name + '; see ' + str(log), flush=True)
        sys.exit(result.returncode)
summary['status'] = 'passed'
summary['completedUtc'] = datetime.now(timezone.utc).isoformat()
save()
print('PASSED: ' + str(RUN / 'summary.json'), flush=True)
