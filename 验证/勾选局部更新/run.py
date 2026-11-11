from pathlib import Path
import subprocess
r=Path(__file__).resolve().parents[2];out=Path(__file__).resolve().parent
s=(r/'src/Overview/OverviewListView.cs').read_text(encoding='utf-8-sig')
a=s.index('    private void Toggle(OverviewMod mod, bool enabled)');b=s.index('    internal Action<OverviewMod>? ModInformationRequested;',a);method=s[a:b]
assert 'Render();' not in method and 'Reload();' not in method
(out/'Production.g.cs').write_text('partial class Harness {\n'+method+'\n}\n',encoding='utf-8')
subprocess.run(['C:/Program Files/dotnet/dotnet.exe','run','--project',str(out/'Checks.csproj'),'-c','Release'],cwd=r,check=True)
