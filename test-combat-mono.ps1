param([string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim', [switch]$DebugSystem)
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskDirectory = Join-Path $PSScriptRoot $(if ($DebugSystem) {'bin/combat-tests-debug'} else {'bin/combat-tests'})
$taskHost = Join-Path $taskDirectory 'MonoFixtureHost.exe'
& $taskCompiler '/nologo' '/target:exe' '/platform:x64' '/optimize+' '/langversion:5' '/warnaserror+' ('/out:' + $taskHost) (Join-Path $PSScriptRoot 'tests\CombatMonoFixtureHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mono fixture host compilation failed.' }
& $taskHost (Join-Path $taskDirectory 'CombatRegression.dll') (Join-Path $ValheimDirectory 'valheim_Data\Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge\EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge\etc')
if ($LASTEXITCODE -ne 0) { throw 'Combat fixture failed on embedded Valheim Mono.' }
