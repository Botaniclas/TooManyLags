param(
    [string]$BepInExDirectory='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/profiles/Client/BepInEx',
    [string]$ValheimDirectory='D:/Steam-Bibliotheksordner/steamapps/common/Valheim',
    [switch]$DebugSystem=$true,
    [switch]$SkipNative
)
$ErrorActionPreference='Stop'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskOutput=Join-Path $PSScriptRoot 'bin/mob-source-tests'
New-Item -ItemType Directory -Force -Path $taskOutput|Out-Null
$taskCore=Join-Path $BepInExDirectory 'core'
Get-ChildItem -LiteralPath $taskCore -File|Where-Object Name -Match '^(0Harmony|Mono).*\.dll$'|Copy-Item -Destination $taskOutput -Force
$taskExe=Join-Path $taskOutput 'MobSourceTests.exe'
$taskSources=@('src/MobSourceHandoff.cs','src/MobNativePublication.cs','tests/MobSourceFixtures.cs','tests/MobSourceTests.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/out:'+$taskExe) $taskSources
if($LASTEXITCODE -ne 0){throw 'Source handoff fixture compilation failed.'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Source handoff fixture failed.'}
$taskHost=Join-Path $taskOutput 'MobSourceMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$taskHost) (Join-Path $PSScriptRoot 'tests/CombatMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'Source handoff Mono host compilation failed.'}
$taskLog=@(& $taskHost $taskExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') 2>&1)
if($LASTEXITCODE -ne 0 -or !($taskLog -match '^PASS: \d+ production source-handoff')){$taskLog|Write-Output;throw 'Embedded Mono source handoff fixture failed.'}
$taskLog|Write-Output
if($SkipNative){return}
$taskAddon=Join-Path $PSScriptRoot $(if($DebugSystem){'bin/debug-system/TooManyLags.dll'}else{'bin/TooManyLags.dll'})
$taskAudit=Join-Path $taskOutput 'MobSourceNativeAudit.exe'
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/reference:'+(Join-Path $taskCore 'Mono.Cecil.dll')) ('/out:'+$taskAudit) (Join-Path $PSScriptRoot 'tests/MobSourceNativeAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Source native audit compilation failed.'}
$taskNativeHost=Join-Path $taskOutput 'MobSourceNativeMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$taskNativeHost) (Join-Path $PSScriptRoot 'tests/StateMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'Source native Mono host compilation failed.'}
$taskLog=@(& $taskNativeHost $taskAudit (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') $taskAddon $taskCore $taskAddon 2>&1)
$taskLog|Set-Content -LiteralPath (Join-Path $taskOutput 'native-install.log') -Encoding UTF8
if($LASTEXITCODE -ne 0 -or !($taskLog -match '^PASS: \d+ actual source')){$taskLog|Write-Output;throw 'Actual source native contract audit failed.'}
$taskLog|Where-Object{$_ -match '^(PASS:|LIMIT:)'}|Write-Output
