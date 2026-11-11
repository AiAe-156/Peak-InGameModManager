
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root='C:/test'; $expected='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
$form=New-Object Windows.Forms.Form
$form.Text='MCE 整合包切换';$form.Size=New-Object Drawing.Size(600,290);$form.StartPosition='CenterScreen'
$label=New-Object Windows.Forms.Label;$label.Location=New-Object Drawing.Point(18,18);$label.Size=New-Object Drawing.Size(550,165);$label.Font=New-Object Drawing.Font('Microsoft YaHei',11)
$form.Controls.Add($label)
$bar=New-Object Windows.Forms.ProgressBar;$bar.Location=New-Object Drawing.Point(18,190);$bar.Size=New-Object Drawing.Size(550,22);$form.Controls.Add($bar)
$clock=[Diagnostics.Stopwatch]::StartNew();$timer=New-Object Windows.Forms.Timer;$timer.Interval=500
$timer.Add_Tick({try {
 $task=Get-Content -LiteralPath (Join-Path $root 'pack-task.json') -Raw -Encoding UTF8|ConvertFrom-Json
 if($task.Id -ne $expected){$label.Text='当前切换记录已被替换，请检查 MCE 操作状态。';return}
 $phase=switch($task.State){'waiting-exit'{'等待 PEAK 游戏进程退出'} 'switching'{'正在保存当前包并替换文件'} 'completed'{'切换完成，可以启动游戏'} 'failed'{'切换失败，请查看下方原因'} 'recovery-required'{'恢复尚未完成，请勿启动游戏'} default{'正在提交切换请求'}}
 $details='';$plan=Join-Path $root ('packs\.transactions\'+$expected+'\switch.json')
 if(Test-Path -LiteralPath $plan){$p=Get-Content -LiteralPath $plan -Raw -Encoding UTF8|ConvertFrom-Json;$done=@($p.Removed).Count+@($p.Placed).Count;$total=@($p.Originals).Count+@($p.Desired).Count;$details='文件处理：'+$done+' / '+$total;if($total -gt 0){$bar.Value=[Math]::Min(99,[int](100*$done/$total))}}
 $label.Text=$phase+"`r`n整合包："+$task.Proposed.Name+"`r`n"+$details+"`r`n已用时："+[int]$clock.Elapsed.TotalSeconds+' 秒'+"`r`n"+$task.Message+"`r`n"+$task.Error
 if($task.State -eq 'completed'){$bar.Value=100;$timer.Stop()}
 if($task.State -in @('failed','cancelled')){$timer.Stop()}
}catch{$label.Text='正在读取进度……'+$_.Exception.Message}})
$timer.Start();[Windows.Forms.Application]::Run($form);$timer.Dispose()
