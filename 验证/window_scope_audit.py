from pathlib import Path
import json
root=Path(__file__).resolve().parent.parent
out=root/'验证/WindowReadWriteAudit'; out.mkdir(exist_ok=True)
fixture=out/'fixture'; fixture.mkdir(exist_ok=True)
id='a'*32
(fixture/'commands').mkdir(exist_ok=True)
for old in (fixture/'commands').glob('*.json'): old.unlink()
(fixture/'pack-task.json').write_text(json.dumps({'Id':id,'State':'failed','Request':{'Operation':'confirm-switch'},'Proposed':{'Name':'scope-test'},'Message':'test','Error':'test','AttemptId':'old-attempt','AttemptStartedUtc':'2026-10-27T01:00:00Z','AttemptEndedUtc':'2026-10-27T01:00:17Z'}),encoding='utf-8')
s=(root/'src/Overview/PackSwitchProgressWindow.cs').read_text(encoding='utf-8')
s=s.split('string script = @"',1)[1].split('\n";',1)[0].replace('""','"')
s=s.replace('ROOT_PLACEHOLDER',str(fixture).replace("'","''")).replace('ID_PLACEHOLDER',id).replace('WORKER_PLACEHOLDER',str(out/'DO_NOT_EXIST.exe')).replace('ENGLISH_PLACEHOLDER','no')
s=s.replace('$timer.Start();[Windows.Forms.Application]::Run($form);$timer.Dispose()',r'''
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$tick=[Windows.Forms.Timer].GetMethod('OnTick',$flags)
$tick.Invoke($timer,@([EventArgs]::Empty))|Out-Null
Write-Output ('OUTER_LASTTASK_NULL='+($null -eq $lastTask))
Write-Output ('LABEL_UPDATED='+$label.Text.StartsWith('切换失败'))
$click=[Windows.Forms.Button].GetMethod('OnClick',$flags)
$click.Invoke($retry,@([EventArgs]::Empty))|Out-Null
Write-Output ('COMMANDS_WRITTEN='+@(Get-ChildItem -LiteralPath (Join-Path $root 'commands') -Filter '*.json' -ErrorAction SilentlyContinue).Count)
Write-Output ('PERSISTENT_ELAPSED_17='+$label.Text.Contains('17'))
$task=Read-SharedJson (Join-Path $root 'pack-task.json');$task.State='recovery-required';$task.AttemptId='new-attempt';$task.AttemptEndedUtc='';$task|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $root 'pack-task.json') -Encoding UTF8
$script:retryPending=$true;$script:retryAttempt='old-attempt'
$tick.Invoke($timer,@([EventArgs]::Empty))|Out-Null
Write-Output ('RECOVERY_SAME_ID_ACK='+(!$script:retryPending))
$details.Select(2,3);$tick.Invoke($timer,@([EventArgs]::Empty))|Out-Null
Write-Output ('DETAIL_SELECTION_RETAINED='+($details.SelectionStart -eq 2 -and $details.SelectionLength -eq 3))
$timer.Dispose();$form.Dispose()
''')
(out/'scope.ps1').write_text(s,encoding='utf-8-sig')
print(out/'scope.ps1')
