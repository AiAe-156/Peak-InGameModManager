from pathlib import Path
import subprocess
root=Path(__file__).resolve().parents[2]
out=Path(__file__).resolve().parent
source=(root/'src/Overview/ModToggleService.cs').read_text(encoding='utf-8-sig')
start=source.index('    private static string ExistingFile(string original)')
end=source.index('    private static void AddMove(',start)
(out/'LookupHarness.g.cs').write_text('using ModConfigEnhance; using ModConfigEnhance.Updating; using ModConfigEnhance.Overview;\nstatic class LookupHarness {\n'+source[start:end]+'\n}\nnamespace ModConfigEnhance { static class Loc { internal static string F(string en,string zh,string tw,params object[] args)=>string.Format(en,args); } }\n',encoding='utf-8')
subprocess.run(['dotnet','run','--project',str(out/'Checks.csproj'),'-c','Release'],cwd=root,check=True)
