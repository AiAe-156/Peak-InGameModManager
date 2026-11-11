
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
function Read-SharedJson([string]$path){
 $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 try{$reader=New-Object IO.StreamReader($stream,[Text.Encoding]::UTF8);try{$text=$reader.ReadToEnd()}finally{$reader.Dispose()}}finally{$stream.Dispose()}
 return ($text|ConvertFrom-Json)
}
$root='ROOT_PLACEHOLDER'; $expected='ID_PLACEHOLDER'; $worker='WORKER_PLACEHOLDER'; $lastTask=$null; $lastPlan=$null; $retryPending=$false
$form=New-Object Windows.Forms.Form
$form.Text='MCE 整合包切换';$form.Size=New-Object Drawing.Size(760,560);$form.StartPosition='CenterScreen';$form.MinimumSize=$form.Size
$label=New-Object Windows.Forms.Label;$label.Location=New-Object Drawing.Point(18,18);$label.Size=New-Object Drawing.Size(700,92);$label.Font=New-Object Drawing.Font('Microsoft YaHei',11);$form.Controls.Add($label)
$details=New-Object Windows.Forms.TextBox;$details.Location=New-Object Drawing.Point(18,116);$details.Size=New-Object Drawing.Size(700,286);$details.Multiline=$true;$details.ReadOnly=$true;$details.ScrollBars='Both';$details.Anchor='Top,Bottom,Left,Right';$form.Controls.Add($details)
$bar=New-Object Windows.Forms.ProgressBar;$bar.Location=New-Object Drawing.Point(18,414);$bar.Size=New-Object Drawing.Size(700,18);$bar.Anchor='Bottom,Left,Right';$form.Controls.Add($bar)
$retry=New-Object Windows.Forms.Button;$retry.Text='重试切换';$retry.Location=New-Object Drawing.Point(18,448);$retry.Size=New-Object Drawing.Size(140,34);$retry.Anchor='Bottom,Left';$retry.Enabled=$false;$form.Controls.Add($retry)
$copy=New-Object Windows.Forms.Button;$copy.Text='复制详情';$copy.Location=New-Object Drawing.Point(170,448);$copy.Size=New-Object Drawing.Size(120,34);$copy.Anchor='Bottom,Left';$copy.Add_Click({[Windows.Forms.Clipboard]::SetText($label.Text+"`r`n"+$details.Text)});$form.Controls.Add($copy)
$folder=New-Object Windows.Forms.Button;$folder.Text='打开事务目录';$folder.Location=New-Object Drawing.Point(302,448);$folder.Size=New-Object Drawing.Size(140,34);$folder.Anchor='Bottom,Left';$folder.Add_Click({$p=Join-Path $root ('packs\.transactions\'+$expected);if(Test-Path -LiteralPath $p){Start-Process explorer.exe -ArgumentList ('"'+$p+'"')}});$form.Controls.Add($folder)
$retry.Add_Click({try {
 if($null -eq $lastTask){return}
 $op=if($lastTask.State -eq 'recovery-required'){'retry-recovery'}else{'retry-switch'}
 $id=[Guid]::NewGuid().ToString('N');$command=@{Id=$id;Action='pack';Pack=@{Operation=$op;PreviewId=$expected}}
 $dir=Join-Path $root 'commands';[IO.Directory]::CreateDirectory($dir)|Out-Null
 $path=Join-Path $dir (([DateTime]::UtcNow.Ticks).ToString()+'_'+$id+'.json');$temp=$path+'.tmp'
 [IO.File]::WriteAllText($temp,($command|ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)));[IO.File]::Move($temp,$path)
 Start-Process -FilePath $worker -ArgumentList @('--root',('"'+(Split-Path $root -Parent)+'"')) -WindowStyle Hidden
 $clock.Reset();$clock.Start();$retry.Enabled=$false;$retryPending=$true;$label.Text='已提交重试请求，正在重新检查……'
}catch{$details.Text=$_.Exception.ToString()}})
$clock=[Diagnostics.Stopwatch]::StartNew();$timer=New-Object Windows.Forms.Timer;$timer.Interval=500
$timer.Add_Tick({try {
 $task=Read-SharedJson (Join-Path $root 'pack-task.json')
 if($task.Id -ne $expected){if($retryPending -and $task.Request.Operation -eq 'confirm-switch'){$expected=$task.Id;$retryPending=$false;$clock.Restart()}else{$label.Text='当前任务记录已变化，请返回 MCE 查看。';return}}
 $lastTask=$task
 $phase=switch($task.State){'waiting-exit'{'等待 PEAK 退出'} 'switching'{'正在保存并切换'} 'completed'{'切换完成，可以启动游戏'} 'failed'{'切换失败'} 'recovery-required'{'恢复未完成，请勿启动游戏'} default{'正在处理请求'}}
 $plan=Join-Path $root ('packs\.transactions\'+$expected+'\switch.json');$counts='';$recovery='准备阶段，尚未替换文件'
 if(Test-Path -LiteralPath $plan){$p=Read-SharedJson $plan;$lastPlan=$p;$done=@($p.Removed).Count+@($p.Placed).Count;$total=@($p.Originals).Count+@($p.Desired).Count;$counts='文件处理：'+$done+' / '+$total;if($total -gt 0){$bar.Value=[Math]::Min(99,[int](100*$done/$total))};$recovery=switch($p.Phase){'rolled-back'{'已回滚：原环境恢复完成，可重新检查并重试切换'} 'installed'{'提交完成'} default{'事务阶段：'+$p.Phase+'；备份已保留'}}}
 if($task.State -in @('failed','completed','cancelled','recovery-required')){$clock.Stop()}elseif(!$clock.IsRunning){$clock.Start()}
 $label.Text=$phase+"`r`n整合包："+$task.Proposed.Name+"`r`n"+$counts+'    已用时：'+[int]$clock.Elapsed.TotalSeconds+' 秒'
 $message=$task.Message
 if('ENGLISH_PLACEHOLDER' -eq 'yes'){$message=switch($task.State){'failed'{'The switch failed. Recovery status is shown above.'} 'recovery-required'{'Recovery is incomplete. Exit PEAK and retry recovery.'} 'waiting-exit'{'Waiting for PEAK to exit.'} 'completed'{'Completed.'} default{'Processing...'}}}
 $details.Text=$recovery+"`r`n"+$message+"`r`n"+($task.Error -replace "`r?`n","`r`n")
 $retry.Text=if($task.State -eq 'recovery-required'){'重试恢复原环境'}else{'重新检查并重试'}
 $retry.Enabled=(!$retryPending -and $task.Request.Operation -eq 'confirm-switch' -and $task.State -in @('failed','recovery-required'))
 if($task.State -eq 'completed'){$bar.Value=100}
}catch{$details.Text='读取状态失败：'+$_.Exception.Message}})
$timer.Start();[Windows.Forms.Application]::Run($form);$timer.Dispose()
