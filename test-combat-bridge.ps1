param(
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx'
)
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskTests = Join-Path $PSScriptRoot 'bin\combat-tests'
New-Item -ItemType Directory -Force -Path $taskTests | Out-Null
$taskExe = Join-Path $taskTests 'FgnBridgeRuntimeTests.exe'
$taskSources = @('src\FgnCombatBridge.cs','tests\CombatFixtures.cs','tests\CombatBridgeRuntimeTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $taskCompiler '/nologo' '/target:exe' '/optimize+' '/langversion:5' '/warnaserror+' ('/reference:' + (Join-Path $BepInExDirectory 'core\0Harmony.dll')) ('/out:' + $taskExe) $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Modern bridge fixture compilation failed.' }
$taskRuntime = @{ runtimeOptions = @{ tfm = 'net8.0'; framework = @{ name = 'Microsoft.NETCore.App'; version = '8.0.0' }; rollForward = 'LatestPatch' } }
$taskRuntime | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($taskExe, '.runtimeconfig.json')) -Encoding utf8
& dotnet $taskExe
if ($LASTEXITCODE -ne 0) { throw 'Production bridge binding with overloaded Dictionary.Remove failed.' }
