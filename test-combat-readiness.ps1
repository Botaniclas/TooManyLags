param(
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [switch]$DebugSystem
)
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskTests = Join-Path $PSScriptRoot $(if ($DebugSystem) {'bin/combat-tests-debug'} else {'bin/combat-tests'})
New-Item -ItemType Directory -Force -Path $taskTests | Out-Null
$taskOutput = Join-Path $taskTests 'CombatPupReadinessTests.exe'
$taskSources = @('src\FieldReader.cs','src\NativePup.cs','src\CellReadiness.cs','tests\CombatPupReadinessTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$taskSources += @('src\MobCollisionReadiness.cs','tests\MobCollisionGateTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$taskHarmony = Join-Path $BepInExDirectory 'core\0Harmony.dll'
Copy-Item -LiteralPath $taskHarmony -Destination $taskTests -Force
Get-ChildItem -LiteralPath (Join-Path $BepInExDirectory 'core') -Filter 'Mono*.dll' | Copy-Item -Destination $taskTests -Force
& $taskCompiler '/nologo' ('/define:' + $(if ($DebugSystem) {'TML_DEBUG_SYSTEM'} else {'TML_NORMAL_BUILD'})) '/target:exe' '/optimize+' '/langversion:5' '/warnaserror+' ('/reference:' + $taskHarmony) ('/out:' + $taskOutput) $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Combat optional PUP readiness fixture compilation failed.' }
& $taskOutput
if ($LASTEXITCODE -ne 0) { throw 'Combat optional PUP readiness fixtures failed.' }
$taskMonoHost = Join-Path $taskTests 'PupReadinessMonoHost.exe'
& $taskCompiler '/nologo' '/target:exe' '/platform:x64' '/optimize+' '/langversion:5' '/warnaserror+' ('/out:' + $taskMonoHost) (Join-Path $PSScriptRoot 'tests\CombatMonoFixtureHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'PUP readiness Mono host compilation failed.' }
& $taskMonoHost $taskOutput (Join-Path $ValheimDirectory 'valheim_Data\Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge\EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge\etc')
if ($LASTEXITCODE -ne 0) { throw 'Optional PUP readiness failed on embedded Valheim Mono.' }
