param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskOutput = Join-Path $PSScriptRoot 'bin\session-settings-tests'
$taskCore = Join-Path $BepInExDirectory 'core'
$taskBepInEx = Join-Path $taskCore 'BepInEx.dll'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
Copy-Item -LiteralPath $taskBepInEx -Destination $taskOutput -Force
foreach ($dep in @('0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) { Copy-Item -LiteralPath (Join-Path $taskCore $dep) -Destination $taskOutput -Force }
$taskSources = @('src\FgnSessionSettings.cs', 'src\FgnSimulationStartup.cs', 'tests\FgnSessionSettingsTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
foreach ($taskVariant in @('Legacy', 'FGN_165')) {
    $taskExe = Join-Path $taskOutput ('FgnSessionSettingsTests-' + $taskVariant + '.exe')
    & $taskCompiler '/nologo' '/target:exe' '/optimize+' '/langversion:5' '/warnaserror+' ('/define:' + $taskVariant) ('/reference:' + $taskBepInEx) ('/reference:' + (Join-Path $taskCore '0Harmony.dll')) ('/out:' + $taskExe) $taskSources
    if ($LASTEXITCODE -ne 0) { throw "FGN session settings fixture compilation failed: $taskVariant" }
    & $taskExe (Join-Path $taskOutput ($taskVariant + '.cfg')) $taskCore
    if ($LASTEXITCODE -ne 0) { throw "FGN session settings fixture failed: $taskVariant" }
}
