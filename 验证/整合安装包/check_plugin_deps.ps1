param([Parameter(Mandatory=$true)][string]$Root, [Parameter(Mandatory=$true)][string]$Cecil)
# Read-only: lists BepInPlugin / BepInDependency of every DLL under <Root>\BepInEx\plugins and reports unmet hard dependencies.
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::LoadFrom($Cecil)
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory((Join-Path $Root 'BepInEx\core'))
$parameters = New-Object Mono.Cecil.ReaderParameters
$parameters.AssemblyResolver = $resolver
$plugins = @{}
$needs = New-Object System.Collections.ArrayList
$errors = 0
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $Root 'BepInEx\plugins') -Recurse -Filter *.dll)) {
    try { $module = [Mono.Cecil.ModuleDefinition]::ReadModule($file.FullName, $parameters) } catch { continue }
    try {
        foreach ($type in $module.Types) {
            if (-not $type.HasCustomAttributes) { continue }
            $guid = $null
            foreach ($attribute in $type.CustomAttributes) {
                if ($attribute.AttributeType.FullName -ne 'BepInEx.BepInPlugin') { continue }
                $arguments = @($attribute.ConstructorArguments)
                $guid = [string]$arguments[0].Value
                $plugins[$guid] = $file.Name + ' ' + [string]$arguments[2].Value
            }
            if ($null -eq $guid) { continue }
            foreach ($attribute in $type.CustomAttributes) {
                if ($attribute.AttributeType.FullName -ne 'BepInEx.BepInDependency') { continue }
                $arguments = @($attribute.ConstructorArguments)
                $hard = $true
                if ($arguments.Count -gt 1 -and $arguments[1].Type.Name -eq 'DependencyFlags') { $hard = ([int]$arguments[1].Value -band 1) -ne 0 }
                [void]$needs.Add([pscustomobject]@{ Owner = $guid; Target = [string]$arguments[0].Value; Hard = $hard })
            }
        }
    } catch { $errors++; "  ERROR reading " + $file.Name + ": " + $_.Exception.Message }
    $module.Dispose()
}
"PLUGINS " + $plugins.Count
$plugins.GetEnumerator() | Sort-Object Name | ForEach-Object { "  " + $_.Name + "  <- " + $_.Value }
$missing = 0
foreach ($need in $needs) {
    $state = if ($plugins.ContainsKey($need.Target)) { 'present' } elseif ($need.Hard) { $missing++; 'MISSING' } else { 'optional-absent' }
    "  dep " + $need.Owner + " -> " + $need.Target + " hard=" + $need.Hard + " " + $state
}
"MISSING_HARD " + $missing + " READ_ERRORS " + $errors
exit ($missing + $errors)
