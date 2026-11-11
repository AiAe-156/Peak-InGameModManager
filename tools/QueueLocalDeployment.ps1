param([switch]$AllowSameVersion)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = [xml](Get-Content -LiteralPath (Join-Path $projectRoot 'ModConfigEnhance.csproj') -Raw)
$gameRoot = [IO.Path]::GetFullPath($project.SelectSingleNode('//GameDir').InnerText)
$bepRoot = Join-Path $gameRoot 'BepInEx'
$pluginsRoot = (Join-Path $bepRoot 'plugins') + '\'
$targetRoot = [IO.Path]::GetFullPath($project.SelectSingleNode('//DeployDir').InnerText.Replace('$(PluginsDir)', $pluginsRoot))
$relativeTarget = [IO.Path]::GetRelativePath($bepRoot, $targetRoot)
$runtimeRoot = Join-Path $bepRoot 'MCE'
$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'manifest.json') -Raw | ConvertFrom-Json
$installed = Get-Content -LiteralPath (Join-Path $targetRoot 'manifest.json') -Raw | ConvertFrom-Json
if ([version]$manifest.version_number -lt [version]$installed.version_number -or ([version]$manifest.version_number -eq [version]$installed.version_number -and -not $AllowSameVersion)) { throw '本机已经是目标版本或更高；同版本更新需要显式允许。' }

function Assert-LocalPath([string]$Path, [string]$Boundary) {
    $full = [IO.Path]::GetFullPath($Path)
    $base = [IO.Path]::GetFullPath($Boundary).TrimEnd('\')
    if ($full -ne $base -and -not $full.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)) { throw "路径越界：$full" }
    for ($current = $full; $current -and ($current -eq $base -or $current.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)); $current = [IO.Path]::GetDirectoryName($current)) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "安装位置内部存在链接：$current" }
    }
}
function Save-Json([string]$Path, $Value) {
    $temporary = $Path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    [IO.File]::WriteAllText($temporary,($Value | ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporary,$Path,$true)
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Physical-Directory([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    for ($pass=0; $pass -lt 8; $pass++) {
        $changed = $false
        for ($part=[IO.DirectoryInfo]::new($resolved); $part; $part=$part.Parent) {
            if (-not $part.Exists -or -not ($part.Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
            $target = $part.ResolveLinkTarget($true)
            if (-not $target) { throw '无法解析安装位置映射。' }
            $tail = $resolved.Substring($part.FullName.Length).TrimStart('\')
            $resolved = [IO.Path]::GetFullPath((Join-Path $target.FullName $tail)); $changed = $true; break
        }
        if (-not $changed) { return $resolved.TrimEnd('\') }
    }
    throw '安装位置映射层数异常。'
}

Assert-LocalPath $targetRoot $gameRoot
Assert-LocalPath $runtimeRoot $gameRoot
$commandBepRoot = $bepRoot
$activeWorker = $null
$queuePath = Join-Path $runtimeRoot 'queue.json'
if (Test-Path -LiteralPath $queuePath) {
    $priorQueue = Get-Content -LiteralPath $queuePath -Raw | ConvertFrom-Json
    if ($priorQueue.WorkerProcessId -gt 0) { $activeWorker = Get-Process -Id $priorQueue.WorkerProcessId -ErrorAction SilentlyContinue }
    if ($activeWorker) {
        $marker = $activeWorker.Path.IndexOf('\MCE\tools\',[StringComparison]::OrdinalIgnoreCase)
        if ($marker -lt 0) { throw '现有更新助手位置无法确认。' }
        $workerRoot = $activeWorker.Path.Substring(0,$marker)
        if ((Physical-Directory $workerRoot) -ne (Physical-Directory $bepRoot)) { throw '现有助手属于另一安装位置。' }
        $commandBepRoot = $workerRoot
    }
}
$commandTarget = Join-Path $commandBepRoot $relativeTarget
if ((Physical-Directory $commandTarget) -ne (Physical-Directory $targetRoot)) { throw '安装目标映射不一致。' }
$id = [guid]::NewGuid().ToString('N')
$work = Join-Path $runtimeRoot ('jobs\'+$id)
$backup = Join-Path $runtimeRoot ('backups\'+$id)
$stage = Join-Path $work 'local-release'
New-Item -ItemType Directory -Path $stage,(Join-Path $backup 'original'),(Join-Path $runtimeRoot 'commands') -Force | Out-Null
$payload = [Collections.Generic.List[object]]::new()
$releaseList = Join-Path $projectRoot 'bin\Release\netstandard2.1\release-files.txt'
foreach ($line in Get-Content -LiteralPath $releaseList) {
    $parts = $line.Split('|',2)
    if ($parts.Count -ne 2 -or -not(Test-Path -LiteralPath $parts[0] -PathType Leaf)) { throw '发布清单文件缺失或格式无效。' }
    $payload.Add(@{Source=$parts[0];Relative=$parts[1]})
}
$bundle = Join-Path $projectRoot 'Updater\bin\bundle'
if (-not ($payload | Where-Object Relative -eq 'Tools\Updater\MCE.UpdateWorker.exe')) { throw '缺少自包含更新助手。' }
$originals = [Collections.Generic.List[object]]::new()
$desired = [Collections.Generic.List[object]]::new()
foreach ($item in $payload) {
    $source = Join-Path $stage $item.Relative
    $target = Join-Path $targetRoot $item.Relative
    Assert-LocalPath $source $work; Assert-LocalPath $target $targetRoot
    New-Item -ItemType Directory -Path (Split-Path $source -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $item.Source -Destination $source
    $desired.Add([ordered]@{Source=$source;Target=$target;Hash=(Hash $source);Backup='';Plugin=($item.Relative -eq 'ModConfigEnhance.dll');Written=$false})
    if (Test-Path -LiteralPath $target) {
        $originalHash = Hash $target
        $copy = Join-Path $backup ('original\'+[guid]::NewGuid().ToString('N')+'_'+[IO.Path]::GetFileName($target))
        Copy-Item -LiteralPath $target -Destination $copy
        if ((Hash $copy) -ne $originalHash) { throw '备份期间原文件发生变化，停止部署。' }
        $originals.Add([ordered]@{Source='';Target=$target;Hash=$originalHash;Backup=$copy;Plugin=($item.Relative -eq 'ModConfigEnhance.dll');Written=$false})
    }
}
# Stale staging files are archived by the same exit-gated transaction as the old release.
foreach ($file in Get-ChildItem -LiteralPath $targetRoot -Recurse -File -Filter '*.pending') {
    Assert-LocalPath $file.FullName $targetRoot
    $originalHash = Hash $file.FullName
    $copy = Join-Path $backup ('original\'+[guid]::NewGuid().ToString('N')+'_'+$file.Name)
    Copy-Item -LiteralPath $file.FullName -Destination $copy
    if ((Hash $copy) -ne $originalHash) { throw '临时文件在备份时发生变化，停止整理。' }
    $originals.Add([ordered]@{Source='';Target=$file.FullName;Hash=$originalHash;Backup=$copy;Plugin=$false;Written=$false})
}
$parameters = [Collections.Generic.List[object]]::new()
$config = Join-Path $bepRoot 'config\com.aiae.modconfigenhance.cfg'
if (Test-Path -LiteralPath $config) {
    $hash = Hash $config; $copy = Join-Path $backup 'original\MCE-settings.cfg'
    Copy-Item -LiteralPath $config -Destination $copy
    if ((Hash $copy) -ne $hash) { throw '设置在备份期间发生变化，停止部署。' }
    $parameters.Add([ordered]@{Source='';Target=$config;Hash=$hash;Backup=$copy;Plugin=$false;Written=$false})
}
$game = @(Get-Process PEAK -ErrorAction SilentlyContinue)
$gamePid = if ($game.Count) { $game[0].Id } else { 0 }
$plan = [ordered]@{
    Root=$bepRoot;PackageRoot=$targetRoot;JobId=$id;OldVersion=$installed.version_number;NewVersion=$manifest.version_number
    NewTree=$stage;Phase='prepared';CanPlaceLive=$false;SourceProcessId=$gamePid;RemoveAfterRollback=$false;RollbackRequested=$false;StoreVerified=$false
    Originals=@($originals.ToArray());Desired=@($desired.ToArray());Candidates=@();Parameters=@($parameters.ToArray());Dependencies=@()
}
Save-Json (Join-Path $work 'transaction.json') $plan
$job = [ordered]@{
    Id=$id;Namespace='AiAeT';Package='ModConfigEnhance';DisplayName=('ModConfigEnhance · '+$manifest.version_number+' 安装')
    PackageRoot=$commandTarget;InstalledVersion=$installed.version_number;TargetVersion=$manifest.version_number;RunningVersion=$installed.version_number
    Homepage='https://github.com/AiAe-156/Peak-ModConfigEnhance';State='queued';Message='本地构建安装，等游戏退出后提交。';Error=''
    CreatedUtc=[DateTime]::UtcNow.ToString('O');UpdatedUtc=[DateTime]::UtcNow.ToString('O');WorkDirectory=$work;BackupDirectory=$backup
    InstallMode='local-deployment';DownloadUrl='';ETag='';DownloadedBytes=0;TotalBytes=0;Progress=1;SourceProcessId=$gamePid
    Enabled=$true;AllowDowngrade=$false;RestoreParameters=$false;PluginGuids=@('com.aiae.modconfigenhance');AssemblyPaths=@((Join-Path $targetRoot 'ModConfigEnhance.dll'));LoadedAssemblyPaths=@();ConfigurationFiles=@($config)
}
$commandId = [guid]::NewGuid().ToString('N')
$command = [ordered]@{SchemaVersion=1;Id=$commandId;Action='add';JobId=$id;Job=$job;Network=$null;RestoreParameters=$false;SourceProcessId=$gamePid;LoadedAssemblyPaths=@()}
Save-Json (Join-Path $runtimeRoot ('commands\'+[DateTime]::UtcNow.Ticks+'_'+$commandId+'.json')) $command

$exe = Get-Item -LiteralPath (Join-Path $bundle 'MCE.UpdateWorker.exe')
$tools = Join-Path $runtimeRoot ('tools\'+$manifest.version_number+'-'+$exe.Length+'-'+$exe.LastWriteTimeUtc.Ticks)
New-Item -ItemType Directory -Path $tools -Force | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $bundle -File | Where-Object { $_.Name -eq 'MCE.UpdateWorker.exe' -or $_.Extension -eq '.txt' }) {
    $destination = Join-Path $tools $file.Name
    if (-not(Test-Path -LiteralPath $destination)) { Copy-Item -LiteralPath $file.FullName -Destination $destination }
}
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $tools 'MCE.UpdateWorker.exe'))
$start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden; $start.WorkingDirectory=$tools
$start.ArgumentList.Add('--root'); $start.ArgumentList.Add($commandBepRoot)
$start.Environment['DOTNET_BUNDLE_EXTRACT_BASE_DIR']=Join-Path $runtimeRoot 'tools\.extract'
if ($activeWorker -and (Get-Process -Id $activeWorker.Id -ErrorAction SilentlyContinue)) { $workerPid = $activeWorker.Id }
else { $process = [Diagnostics.Process]::Start($start); $workerPid = $process.Id; $process.Dispose() }
$queuePath = Join-Path $runtimeRoot 'queue.json'
$state = ''
for ($attempt=0; $attempt -lt 16; $attempt++) {
    if (Test-Path -LiteralPath $queuePath) {
        $queue = Get-Content -LiteralPath $queuePath -Raw | ConvertFrom-Json
        $found = $queue.Jobs | Where-Object Id -eq $id | Select-Object -First 1
        if ($found) { $state=$found.State; if($state -in @('waiting-exit','pending-restart','completed','failed')){break} }
    }
    Start-Sleep -Milliseconds 250
}
if (-not $state) {
    $detail = if ($queue -and $queue.Error) { $queue.Error } else { '更新助手未接收任务，请保留本地发布包和备份后重试。' }
    throw ('安装尚未开始：' + $detail)
}
[ordered]@{JobId=$id;WorkerPid=$workerPid;State=$state;From=$installed.version_number;To=$manifest.version_number;Queue=$queuePath;Backup=$backup} | ConvertTo-Json
