"""Compile exact production methods against narrow Unity/view stubs (no copied algorithm)."""
from pathlib import Path
import subprocess
root=Path(__file__).resolve().parents[3]
out=Path(__file__).resolve().parent
rows=(root/'src/Overview/OverviewListView.VirtualRows.cs').read_text(encoding='utf-8-sig')
infra=(root/'src/Overview/OverviewListView.Infrastructure.cs').read_text(encoding='utf-8-sig')
def method(source, signature):
    start=source.index(signature); brace=source.index('{',start); depth=1; end=brace+1
    while depth:
        depth += (source[end]=='{')-(source[end]=='}'); end+=1
    return source[start:end]
names=['private void RefreshRowData()', 'private string RowUpdateStamp(VirtualRow row)', 'private void RefreshVirtualRows()']
methods='\n'.join(method(rows,n) for n in names)+'\n'+method(infra,'private void FlushInfrastructureRedraw()')
(out/'ProductionMethods.g.cs').write_text('using System; using System.Linq; using System.Collections.Generic;\npartial class Harness {\n'+methods+'\n}\n',encoding='utf-8')
subprocess.run(['dotnet','run','--project',str(out/'PerformanceRepairChecks.csproj'),'-c','Release'],check=True,cwd=root)
