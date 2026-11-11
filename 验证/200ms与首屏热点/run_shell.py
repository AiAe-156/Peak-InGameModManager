"""Extract and execute current PumpShell verbatim, without Unity."""
from pathlib import Path
import subprocess
root=Path(__file__).resolve().parents[2]
out=Path(__file__).resolve().parent
source=(root/'src/Overview/OverviewController.cs').read_text(encoding='utf-8-sig')
start=source.index('    private void PumpShell()');end=source.index('{',start)+1;depth=1
while depth:
    depth+=(source[end]=='{')-(source[end]=='}');end+=1
(out/'ShellProduction.g.cs').write_text('using System;\npartial class ShellHarness {\n'+source[start:end]+'\n}\n',encoding='utf-8')
subprocess.run(['C:/Program Files/dotnet/dotnet.exe','run','--project',str(out/'ShellChecks.csproj'),'-c','Release','-p:EnableDeployment=false'],check=True,cwd=root)
