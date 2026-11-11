from pathlib import Path
import subprocess
r=Path(__file__).resolve().parents[2]; out=Path(__file__).resolve().parent
s=(r/'src/Overview/ModToggleService.cs').read_text(encoding='utf-8-sig')
def method(signature):
 a=s.index(signature); b=s.index('{',a); i=b+1; depth=1
 while depth:
  depth+=(s[i]=='{')-(s[i]=='}');i+=1
 return s[a:i]
(out/'Production.g.cs').write_text('using Newtonsoft.Json.Linq; using System.Text;\npartial class Checks {\n'+method('    private static void CheckDependencies(')+'\n'+method('    private static void CheckPackageDependencies(')+'\n}\n',encoding='utf-8')
subprocess.run(['C:/Program Files/dotnet/dotnet.exe','run','--project',str(out/'Checks.csproj'),'-c','Release'],cwd=r,check=True)
files={p.name:p.read_text(encoding='utf-8-sig') for p in (r/'src/Overview').glob('*.cs')}
assert files['OverviewListView.cs'].count('ShowModActions(mod, data)')==3
applied = files['OverviewRepository.Applied.cs']
assert 'skipEnablement' in applied and 'var committedMods = Clone(_draftMods)' in applied
assert 'target.enabled = mods.First' in applied
assert applied.index('_draftConfig = committedConfig; _draftMods = committedMods;') < applied.index('                Save();')
assert '_draftConfig = workingConfig; _draftMods = workingMods;' in applied
assert 'pending.restartMods.RemoveAll' in files['OverviewRepository.Applied.cs']
assert 'overwriteExternal, true, adviceApproved, onSaved)' in files['OverviewPage.Profiles.cs']
assert 'skipEnablement: skipEnablement' in files['OverviewRepository.SaveScope.cs']
assert 'PointerDropdown(data.position' in files['OverviewListView.ModActions.cs']
assert 'ComponentDetailsView.Open' in files['OverviewListView.ModActions.cs']
assert 'SelectInformation(_managementSelectedMod != null)' in files['OverviewPage.Management.cs']
print('PASS 7 UI and partial-apply wiring checks')
