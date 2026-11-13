"""Isolated unified-save regression. Never deploys or operates a live installation."""
from pathlib import Path
import json
import subprocess
import sys

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
DOTNET = 'C:/Program Files/dotnet/dotnet.exe'
steps = []
def add(name, log, args):
    steps.append((name, HERE / log, args))
for name, project in [('plugin', 'ModConfigEnhance.csproj'), ('worker', 'Updater/MCE.UpdateWorker.csproj'), ('desktop', 'Desktop/MCE.Desktop.csproj')]:
    add(name, name + '-build.log', [DOTNET, 'build', project, '-c', 'Release', '--no-restore', '-p:EnableDeployment=false'])
for name, directory, log in [
    ('documents', 'DocumentChecks', 'document-checks'), ('scope', 'ScopeChecks', 'scope-checks'),
    ('migration', 'MigrationChecks', 'migration-checks'), ('issues', 'IssueReportChecks', 'issue-report-checks'), ('authors', 'AuthorWorkingChecks', 'author-working-checks'),
    ('presets', 'PresetChecks', 'preset-checks'), ('cache', 'CacheChecks', 'cache-checks'),
    ('native', 'NativeChecks', 'native-checks'), ('resource-draft', 'ResourceDraftChecks', 'resource-draft-checks')]:
    add(name, log + '.log', [DOTNET, 'run', '--project', str(HERE / directory / (directory + '.csproj')), '-c', 'Release'])
for directory, log in [('HarmonyChecks', 'harmony'), ('ConfigTransactionChecks', 'config-transaction')]:
    add(directory + '-build', log + '-build.log', [DOTNET, 'build', str(HERE / directory / (directory + '.csproj')), '-c', 'Release'])
    add(directory, log + '-checks.log', [str(HERE / directory / 'bin/Release/net48' / (directory + '.exe'))])
for directory, log in [('ShopChecks', 'shop'), ('PackLibraryChecks', 'pack'), ('InstallationFlowChecks', 'installation'), ('ModImportChecks', 'import')]:
    args = [DOTNET, 'run', '--project', str(ROOT / '验证' / directory / (directory + '.csproj')), '-c', 'Release']
    if directory == 'ModImportChecks':
        args += ['--framework', 'net10.0']
    add(directory, log + '-regression.log', args)
add('navigation', 'navigation-checks.log', [sys.executable, str(ROOT / '验证/应用与导航/run.py')])
add('save-navigation', 'save-navigation-checks.log', [sys.executable, str(HERE / 'check-save-navigation.py')])
add('installer-core', 'installer-core-checks.log', [DOTNET, 'run', '--project', str(ROOT / '验证/更新核心重做/InstallerChecks/InstallerChecks.csproj'), '-c', 'Release'])
# 2.3.8: every visible string must be three-language and in the exported catalogue.
add('translation-catalogue', 'translation-catalogue-checks.log', [sys.executable, '-I', str(ROOT / '验证/发布前审查/collect_terms.py'), '--check'])
add('translation-hardcoded', 'translation-hardcoded-checks.log', [sys.executable, '-I', str(ROOT / '验证/发布前审查/hardcoded_chinese.py')])
# 2.3.10: built-in help — links, anchors, images, three-language parity, no tables.
add('help-docs', 'help-docs-checks.log', [sys.executable, '-I', str(ROOT / '验证/2.3.10帮助重做/help_checks.py')])
# 2.3.11: PEAK 3.1.0 decimal display for mod float settings.
add('decimal-display', 'decimal-display-checks.log', [DOTNET, 'run', '--project', str(ROOT / '验证/2.3.11小数显示/checks/Checks.csproj'), '-c', 'Release'])
add('interop-structs', 'interop-structs-checks.log', [sys.executable, '-I', str(ROOT / '验证/2.4.3日志导出崩溃/interop_checks.py')])
add('window-navigation', 'window-navigation-checks.log', [DOTNET, 'run', '--project', str(ROOT / '验证/窗口导航重做/GridChecks/GridChecks.csproj'), '-c', 'Release'])
add('reader-input', 'reader-input-checks.log', [DOTNET, 'run', '--project', str(ROOT / '验证/窗口导航重做/ReaderChecks/ReaderChecks.csproj'), '-c', 'Release'])
add('startup-publication', 'startup-publication-checks.log', [DOTNET, 'run', '--project', str(ROOT / '验证/0.0.1发布/StartupChecks/StartupChecks.csproj'), '-c', 'Release'])
results = []
for name, log, args in steps:
    with log.open('w', encoding='utf-8') as output:
        result = subprocess.run(args, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT)
    results.append({'name': name, 'exitCode': result.returncode, 'log': log.name})
    print(('PASS' if result.returncode == 0 else 'FAIL') + ' ' + name, flush=True)
(HERE / 'regression-summary.json').write_text(json.dumps({'deployment': False, 'steps': results}, ensure_ascii=False, indent=2), encoding='utf-8')
sys.exit(1 if any(r['exitCode'] for r in results) else 0)
