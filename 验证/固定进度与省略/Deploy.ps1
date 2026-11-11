$ErrorActionPreference='Stop'
$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new()
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$attempt=Join-Path $PSScriptRoot ('deployment_'+$stamp)
New-Item -ItemType Directory -Path $attempt | Out-Null
$result=[ordered]@{Status='preflight';StartedUtc=[DateTime]::UtcNow.ToString('O');Deployed=$false;Target='';Backup='';Archive='';Files=0;Processes=@();Error=''}
function SaveResult { $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $attempt 'result.json') -Encoding UTF8 }
function CheckProcesses {
 $p=@(Get-Process -Name PEAK,MCE.UpdateWorker,MCE.Desktop -ErrorAction SilentlyContinue)
 $result.Processes=@($p|Select-Object ProcessName,Id,Path)
 if($p.Count -gt 0){throw 'PEAK or MCE helper/viewer is running. Deployment stopped without killing processes.'}
}
function NoLinks([string]$path) {
 for($p=$path;$p;$p=[IO.Path]::GetDirectoryName($p)) {
  if(Test-Path -LiteralPath $p){if((Get-Item -LiteralPath $p -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Linked deployment path: $p"}}
 }
}
try {
 CheckProcesses
 [xml]$csproj=Get-Content -LiteralPath (Join-Path $project 'ModConfigEnhance.csproj') -Raw -Encoding UTF8
 $deploy=($csproj.Project.PropertyGroup|ForEach-Object {$_.DeployDir}|Where-Object {$_}|Select-Object -First 1)
 $game='F:\Games\SteamLibrary\steamapps\common\PEAK'
 $bep=Join-Path $game 'BepInEx'
 $plugins=Join-Path $bep 'plugins'
 $target=$deploy.Replace('$(PluginsDir)',($plugins+'\'))
 $target=[IO.Path]::GetFullPath($target).TrimEnd('\')
 $result.Target=$target
 if(!(Test-Path -LiteralPath (Join-Path $target 'ModConfigEnhance.dll'))){throw 'Existing deployment identity was not found.'}
 NoLinks $target
 # Check only this project's known installation/cache candidates, not unrelated plugins.
 $candidates=@((Join-Path $bep '0核心&基础模组\ModManager_InGame_2.0.0_20261029_215224'),(Join-Path $bep 'ModManager_InGame_2.0.0_20261029_215224'),(Join-Path $bep 'ModConfigEnhance'),(Join-Path $plugins 'ModConfigEnhance'),(Join-Path $plugins '0核心&基础模组\[界面]ModConfig增强-ModConfigEnhance'))
 foreach($candidate in $candidates){if($candidate -ne $target -and (Test-Path -LiteralPath (Join-Path $candidate 'ModConfigEnhance.dll'))){throw "Additional MCE candidate exists: $candidate; confirmation required."}}
 $list=Join-Path $project 'bin\Release\netstandard2.1\release-files.txt'
 $items=@(Get-Content -LiteralPath $list -Encoding UTF8|Where-Object {$_}|ForEach-Object {
  $parts=$_ -split '\|',2
  if($parts.Count -ne 2 -or $parts[1] -match '(^[\\/]|:|(^|[\\/])\.\.([\\/]|$))'){throw 'Unsafe release inventory'}
  if(!(Test-Path -LiteralPath $parts[0] -PathType Leaf)){throw "Missing release file: $($parts[0])"}
  [PSCustomObject]@{Source=$parts[0];Relative=$parts[1];Length=(Get-Item -LiteralPath $parts[0]).Length}
 })
 if($items.Count -lt 20){throw 'Release inventory incomplete.'}
 $result.Files=$items.Count
 $backup=Join-Path $project ('备份\部署前_固定进度与省略_'+$stamp+'\installed')
 New-Item -ItemType Directory -Path $backup -Force|Out-Null
 Get-ChildItem -LiteralPath $target -Force|Copy-Item -Destination $backup -Recurse -Force
 $result.Backup=$backup
 $archive=Join-Path $project ('发布\ModConfigEnhance_2.0.12_'+$stamp+'_fixed-progress-ellipsis.zip')
 $result.Archive=$archive
 SaveResult
 & 'C:\Users\AiAe\.workbuddy\binaries\python\envs\default\Scripts\python.exe' (Join-Path $project 'tools\package_thunderstore.py') --no-build --output $archive 2>&1|Tee-Object -FilePath (Join-Path $attempt 'package.log')
 if($LASTEXITCODE -ne 0){throw 'Release packaging failed.'}
 CheckProcesses
 $result.Status='copying';SaveResult
 try {
  foreach($item in $items){
   $dest=Join-Path $target $item.Relative;NoLinks $dest
   New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($dest)) -Force|Out-Null
   Copy-Item -LiteralPath $item.Source -Destination $dest -Force
   if(!(Test-Path -LiteralPath $dest -PathType Leaf) -or (Get-Item -LiteralPath $dest).Length -ne $item.Length){throw "Copy verification failed: $dest"}
  }
  $tool=Join-Path $target 'Tools\Updater'
  & (Join-Path $tool 'MCE.UpdateWorker.exe') --validate-protocol-manifest $tool
  if($LASTEXITCODE -ne 0){throw 'Installed helper protocol validation failed.'}
 } catch {
  $copyError=$_
  foreach($item in $items){$dest=Join-Path $target $item.Relative;$old=Join-Path $backup $item.Relative;if(Test-Path -LiteralPath $old){Copy-Item -LiteralPath $old -Destination $dest -Force}else{if(Test-Path -LiteralPath $dest){Remove-Item -LiteralPath $dest -Force}}}
  Get-ChildItem -LiteralPath $backup -Force|Copy-Item -Destination $target -Recurse -Force
  $result.Status='rolled-back';throw $copyError
 }
 $result.Status='completed';$result.Deployed=$true
 $result.Inventory=@($items|Select-Object Relative,Length)
 $result.CompletedUtc=[DateTime]::UtcNow.ToString('O')
 SaveResult
 Write-Output ('DEPLOYED '+$items.Count+' files to '+$target)
 Write-Output ('BACKUP '+$backup)
 Write-Output ('ARCHIVE '+$archive)
 Write-Output ('RESULT '+(Join-Path $attempt 'result.json'))
} catch {
 $result.Error=$_.Exception.ToString();if($result.Status -ne 'rolled-back'){$result.Status='blocked-or-failed'};SaveResult
 Write-Error $_;exit 1
}
