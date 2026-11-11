param([switch]$Headless)
$ErrorActionPreference='Stop'
$script:root=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('RjpcR2FtZXNcU3RlYW1MaWJyYXJ5XHN0ZWFtYXBwc1xjb21tb25cUEVBS1xNT0RzXE1vZENvbmZpZ0VuaGFuY2Vc6aqM6K+BXFVwZGF0ZUZlZWRiYWNrMjAyNjEwMzBcUXVldWVDaGVja3Ncc2FuZGJveC0yMDMyMDVcU3RlYW1BbGlhc1xzdGVhbWFwcHNcY29tbW9uXFBFQUtcQmVwSW5FeA=='))
$script:mceHome=Join-Path $script:root 'MCE'
function Assert-NoLink([string]$path) {
 $p=[IO.Path]::GetFullPath($path)
 $boundary=[IO.Path]::GetDirectoryName($script:root)
 if(!$p.Equals($boundary,[StringComparison]::OrdinalIgnoreCase) -and !$p.StartsWith($boundary+'',[StringComparison]::OrdinalIgnoreCase)){throw "Path outside selected game: $path"}
 while($p){
  if([IO.File]::Exists($p) -or [IO.Directory]::Exists($p)){
   if(([IO.File]::GetAttributes($p) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw "Linked path rejected: $path"}
  }
  if($p.Equals($boundary,[StringComparison]::OrdinalIgnoreCase)){break}
  $p=[IO.Path]::GetDirectoryName($p)
 }
}
function Read-RecoveryJson([string]$path) {
 Assert-NoLink $path
 $stream=[IO.File]::Open($path,'Open','Read',([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
 try{$reader=New-Object IO.StreamReader($stream,[Text.Encoding]::UTF8);try{return ($reader.ReadToEnd()|ConvertFrom-Json)}finally{$reader.Dispose()}}finally{$stream.Dispose()}
}
function Assert-RecoveryIndex {
 $path=Join-Path $script:mceHome 'packs\index.json';Assert-NoLink $path
 if([IO.File]::Exists($path)){
  $data=Read-RecoveryJson $path
  $schema=1;$minimum=1
  if($null -ne $data.SchemaVersion){$schema=$data.SchemaVersion}
  if($null -ne $data.MinimumWorkerProtocol){$minimum=$data.MinimumWorkerProtocol}
  if(($schema -isnot [int] -and $schema -isnot [long]) -or ($minimum -isnot [int] -and $minimum -isnot [long]) -or $schema -lt 1 -or $schema -gt 2 -or $minimum -lt 1 -or $minimum -gt 2 -or ($schema -eq 2 -and $minimum -ne 2)){throw 'Unsupported index schema/protocol. Originals retained; no recovery started.'}
 }
}
function Get-Sha256([string]$path) {
 $sha=[Security.Cryptography.SHA256]::Create();$input=[IO.File]::OpenRead($path)
 try{return ([BitConverter]::ToString($sha.ComputeHash($input))).Replace('-','').ToLowerInvariant()}finally{$input.Dispose();$sha.Dispose()}
}
# Mirrors WorkerProtocol.ValidateTools: exact complete inventory, protocol2, build digest, no links/residue.
# This is release integrity, not a publisher signature.
function Test-RecoveryTools([string]$directory) {
 Assert-NoLink $directory
 $manifest=Join-Path $directory 'worker-protocol.manifest';Assert-NoLink $manifest
 $lines=[IO.File]::ReadAllLines($manifest)
 if($lines.Length -lt 3 -or $lines[0] -cne 'MCE-WORKER|2'){throw 'Missing/incompatible protocol manifest'}
 $files=New-Object 'Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
 $pending=New-Object 'Collections.Generic.Stack[string]';$pending.Push($directory)
 while($pending.Count -gt 0){
  $current=$pending.Pop();Assert-NoLink $current
  foreach($child in [IO.Directory]::GetDirectories($current)){Assert-NoLink $child;$pending.Push($child)}
  foreach($file in [IO.Directory]::GetFiles($current)){
   Assert-NoLink $file
   $relative=$file.Substring($directory.Length+1).Replace('\','/')
   if($relative -ceq 'worker-protocol.manifest'){continue}
   if($relative -match '(^|/)(bin|obj|tests|test|\.git)(/|$)' -or [IO.Path]::GetExtension($relative) -in @('.pdb','.cs','.csproj','.log','.tmp','.partial')){throw 'Build/test residue in tool release'}
   if($relative -match '[\x00-\x1f\x7f-\x9f|:]' -or @($relative.Split('/')|Where-Object {$_ -eq '.' -or $_ -eq '..' -or $_.EndsWith('.') -or $_.EndsWith(' ')}).Count){throw 'Invalid tool path'}
   $files.Add($relative,(Get-Sha256 $file))
  }
 }
 foreach($required in @('MCE.UpdateWorker.exe','Mono.Cecil.LICENSE.txt','Runtime-LICENSE.TXT','Runtime-THIRD-PARTY-NOTICES.TXT')){if(!$files.ContainsKey($required)){throw "Incomplete tool release: $required"}}
 $payload=(@($files.GetEnumerator()|ForEach-Object {$_.Key+'|'+$_.Value}) -join "`n")
 $sha=[Security.Cryptography.SHA256]::Create()
 try{$build=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($payload)))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
 if($lines[1] -cne ('build|'+$build) -or ($lines[2..($lines.Length-1)] -join "`n") -cne $payload){throw 'Damaged tool release manifest/dependencies'}
 return $build
}
function Find-RecoveryTools {
 Assert-RecoveryIndex
 $tools=Join-Path $script:mceHome 'tools';Assert-NoLink $tools
 $errors=New-Object 'Collections.Generic.List[string]'
 if([IO.Directory]::Exists($tools)){
  foreach($candidate in ([IO.Directory]::GetDirectories($tools,'p2-*')|Sort-Object -Descending)){
   if([IO.Path]::GetFileName($candidate).Contains('.stage-')){continue}
   try{$null=Test-RecoveryTools $candidate;return $candidate}catch{$errors.Add($_.Exception.Message)}
  }
 }
 throw ('No verified protocol2 cached tools available. Originals retained. '+($errors -join '; '))
}
function Get-RecoveryArguments([string]$mode,[string]$selector='') {
 if($mode -notin @('--recovery-status','--recover')){throw 'Recovery-only mode required'}
 if($mode -eq '--recover' -and $selector -cnotmatch '^(update|pack|shop):[a-fA-F0-9]{32}$'){throw 'Select an existing unfinished transaction'}
 if($mode -eq '--recovery-status' -and $selector){throw 'Unexpected status selector'}
 $args=@('--root',('"'+$script:root.TrimEnd('\','/')+'"'),$mode)
 if($selector){$args+=@('--journal',$selector)}
 return ($args -join ' ')
}
function Start-RecoveryProcess([string]$mode,[string]$selector='') {
 $tools=Find-RecoveryTools
 $info=New-Object Diagnostics.ProcessStartInfo
 $info.FileName=Join-Path $tools 'MCE.UpdateWorker.exe';$info.Arguments=Get-RecoveryArguments $mode $selector
 $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WorkingDirectory=$tools
 $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
 $info.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR']=Join-Path $script:mceHome 'tools\.extract'
 $process=New-Object Diagnostics.Process;$process.StartInfo=$info
 if(!$process.Start()){throw 'Could not start recovery worker'}
 return @{Process=$process;Output=$process.StandardOutput.ReadToEndAsync();Error=$process.StandardError.ReadToEndAsync();Mode=$mode}
}
function Get-RecoveryEntries([string]$output) {
 $entries=@()
 foreach($line in ($output -split "`r?`n")){
  if($line -cmatch '^((update|pack|shop):[a-fA-F0-9]{32}) [a-z-]+ (recoverable|waiting-exit) '){$entries+=$Matches[1]}
 }
 return $entries
}
if($Headless){return}
try{
 Add-Type -AssemblyName System.Windows.Forms
 Add-Type -AssemblyName System.Drawing
 $form=New-Object Windows.Forms.Form;$form.Text='MCE Recovery / 仅恢复既有未完成事务';$form.Size=New-Object Drawing.Size(820,580);$form.MinimumSize=$form.Size;$form.StartPosition='CenterScreen'
 $label=New-Object Windows.Forms.Label;$label.Location=New-Object Drawing.Point(16,16);$label.Size=New-Object Drawing.Size(770,65);$label.Text='Recovery only: no new commands, switch retries or migrations. Exit PEAK first. Select a listed unfinished transaction and confirm. / 不处理新命令、重试或迁移；请先退出游戏。';$form.Controls.Add($label)
 $details=New-Object Windows.Forms.TextBox;$details.Location=New-Object Drawing.Point(16,88);$details.Size=New-Object Drawing.Size(770,335);$details.Multiline=$true;$details.ReadOnly=$true;$details.ScrollBars='Both';$details.Anchor='Top,Bottom,Left,Right';$form.Controls.Add($details)
 $list=New-Object Windows.Forms.ComboBox;$list.Location=New-Object Drawing.Point(16,440);$list.Size=New-Object Drawing.Size(770,28);$list.DropDownStyle='DropDownList';$list.Anchor='Bottom,Left,Right';$form.Controls.Add($list)
 $refresh=New-Object Windows.Forms.Button;$refresh.Text='Refresh / 检查';$refresh.Location=New-Object Drawing.Point(16,480);$refresh.Size=New-Object Drawing.Size(180,34);$refresh.Anchor='Bottom,Left';$form.Controls.Add($refresh)
 $recover=New-Object Windows.Forms.Button;$recover.Text='Recover selected / 恢复所选';$recover.Location=New-Object Drawing.Point(210,480);$recover.Size=New-Object Drawing.Size(300,34);$recover.Anchor='Bottom,Left';$recover.Enabled=$false;$form.Controls.Add($recover)
 $script:active=$null;$script:starting=$null
 $script:launcherPath=$PSCommandPath
 function Show-RecoveryError([string]$message){$details.ForeColor=[Drawing.Color]::DarkRed;$details.Text='FAILED / 未完成 - originals retained / 保留原件'+"`r`n"+$message;$recover.Enabled=$false}
 function Begin-Recovery([string]$mode,[string]$selector=''){
  $refresh.Enabled=$false;$recover.Enabled=$false;$list.Enabled=$false;$details.ForeColor=[Drawing.Color]::Black;$details.Text='Working / 正在检查或恢复，请等待；窗口仍可移动。'
  try{
   # Manifest hashing can be large. Run validation AND process startup off the Forms thread.
   $ps=[PowerShell]::Create()
   $null=$ps.AddScript('param($path,$mode,$selector) . $path -Headless; Start-RecoveryProcess $mode $selector').AddArgument($script:launcherPath).AddArgument($mode).AddArgument($selector)
   $script:starting=@{Shell=$ps;Handle=$ps.BeginInvoke()}
  }catch{Show-RecoveryError $_.Exception.ToString();$refresh.Enabled=$true;$list.Enabled=$true}
 }
 $refresh.Add_Click({$list.Items.Clear();Begin-Recovery '--recovery-status'})
 $recover.Add_Click({
  if($script:active -or $script:starting -or $null -eq $list.SelectedItem){return}
  $selection=[string]$list.SelectedItem
  if([Windows.Forms.MessageBox]::Show(('Roll back ONLY this existing unfinished transaction? No new commands/retries/migration. Exit PEAK first. / 仅回滚所选既有未完成事务，不自动重试或迁移。继续？'+"`r`n"+$selection),'MCE Recovery','YesNo','Warning') -ne 'Yes'){return}
  Begin-Recovery '--recover' $selection
 })
 $timer=New-Object Windows.Forms.Timer;$timer.Interval=150
 $timer.Add_Tick({
  if($script:starting){
   if(!$script:starting.Handle.IsCompleted){return}
   $pending=$script:starting;$script:starting=$null
   try{
    $result=$pending.Shell.EndInvoke($pending.Handle)
    if($pending.Shell.HadErrors){throw ($pending.Shell.Streams.Error|Out-String)}
    if($result.Count -ne 1){throw 'Recovery startup did not return a process'}
    $script:active=$result[0]
   }catch{Show-RecoveryError $_.Exception.ToString();$refresh.Enabled=$true;$list.Enabled=$true}
   finally{$pending.Shell.Dispose()}
  }
  if(!$script:active){return}
  $a=$script:active
  if(!$a.Process.HasExited -or !$a.Output.IsCompleted -or !$a.Error.IsCompleted){return}
  try{
   $output=$a.Output.GetAwaiter().GetResult();$stderr=$a.Error.GetAwaiter().GetResult();$code=$a.Process.ExitCode
   $list.Items.Clear()
   if($code -ne 0 -or $stderr){Show-RecoveryError ('Exit code: '+$code+"`r`nSTDERR: "+$stderr+"`r`n"+$output)}
   elseif($a.Mode -eq '--recovery-status'){
    $entries=@(Get-RecoveryEntries $output)
    foreach($entry in $entries){$null=$list.Items.Add($entry)}
    if($entries.Count){$list.SelectedIndex=0;$recover.Enabled=$true;$details.Text=$output}else{$details.Text='No existing unfinished supported transactions / 没有可恢复的既有未完成事务。New commands were not processed.'+"`r`n"+$output}
   }else{$details.Text='Recovery command finished; check result below. Refresh to check remaining transactions. / 恢复命令已结束，请核对结果；点击检查重新列出事务。'+"`r`n"+$output}
  }catch{Show-RecoveryError $_.Exception.ToString()}
  finally{$a.Process.Dispose();$script:active=$null;$refresh.Enabled=$true;$list.Enabled=$true}
 })
 $form.Add_FormClosing({param($sender,$eventArgs) if($script:active -or $script:starting){$eventArgs.Cancel=$true;[Windows.Forms.MessageBox]::Show('Recovery worker is running. Wait for it to finish; do not interrupt file recovery. / 恢复进程仍在运行，请等待。')|Out-Null}})
 $form.Add_Shown({Begin-Recovery '--recovery-status'})
 $timer.Start();[Windows.Forms.Application]::Run($form);$timer.Dispose()
}catch{Add-Type -AssemblyName System.Windows.Forms;[Windows.Forms.MessageBox]::Show($_.Exception.ToString(),'MCE Recovery FAILED / 未完成','OK','Error')|Out-Null}