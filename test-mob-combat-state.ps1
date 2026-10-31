param(
    [string]$BepInExDirectory='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/profiles/Client/BepInEx',
    [string]$ValheimDirectory='D:/Steam-Bibliotheksordner/steamapps/common/Valheim',
    [string]$FgnAssembly='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/cache/VerdantsAscent-FiresGhettoNetworking/1.5.72/plugins/FiresGhettoNetworking/VAGhettoNetworking.dll',
    [switch]$DebugSystem = $true,
    [switch]$SkipNative
)
$ErrorActionPreference='Stop'
$taskAddon=Join-Path $PSScriptRoot $(if($DebugSystem){'bin/debug-system/TooManyLags.dll'}else{'bin/TooManyLags.dll'})
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskOutput=Join-Path $PSScriptRoot 'bin/combat-state-tests'
New-Item -ItemType Directory -Force -Path $taskOutput|Out-Null
$taskCore=Join-Path $BepInExDirectory 'core'
Get-ChildItem -LiteralPath $taskCore -File|Where-Object Name -Match '^(0Harmony|Mono).*\.dll$'|Copy-Item -Destination $taskOutput -Force
$taskExe=Join-Path $taskOutput 'CombatStateTests.exe'
$taskSources=@('src/FieldReader.cs','src/MobCombatState.cs','tests/MobCombatStateFixtures.cs','tests/MobCombatStateTests.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/out:'+$taskExe) $taskSources
if($LASTEXITCODE -ne 0){throw 'Combat-state fixture compilation failed.'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Combat-state fixture failed.'}
$taskHost=Join-Path $taskOutput 'CombatStateMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$taskHost) (Join-Path $PSScriptRoot 'tests/CombatMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'Combat-state Mono host compilation failed.'}
$taskLog=@(& $taskHost $taskExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') 2>&1)
if($LASTEXITCODE -ne 0 -or !($taskLog -match '^PASS: \d+ production combat-state')){$taskLog|Write-Output;throw 'Embedded Mono combat-state fixture failed.'}
$taskLog|Write-Output
if($SkipNative){return}
$taskAudit=Join-Path $taskOutput 'CombatStateNativeAudit.exe'
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/reference:'+(Join-Path $taskCore 'Mono.Cecil.dll')) ('/out:'+$taskAudit) (Join-Path $PSScriptRoot 'tests/MobCombatStateNativeAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Combat-state native audit compilation failed.'}
$taskNativeHost=Join-Path $taskOutput 'NativeMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$taskNativeHost) (Join-Path $PSScriptRoot 'tests/StateMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'Combat-state native Mono host compilation failed.'}
$taskLog=@(& $taskNativeHost $taskAudit (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') $taskAddon $taskCore $FgnAssembly 2>&1)
$taskLog|Set-Content -LiteralPath (Join-Path $taskOutput 'native-install.log') -Encoding UTF8
if($LASTEXITCODE -ne 0 -or !($taskLog -match '^PASS: \d+ actual combat-state')){$taskLog|Write-Output;throw 'Actual combat-state native contract audit failed.'}
$taskLog|Where-Object{$_ -match '^(PASS:|LIMIT:)'}|Write-Output
