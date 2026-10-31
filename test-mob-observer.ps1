param(
    [string]$BepInExDirectory='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/profiles/Client/BepInEx',
    [string]$ValheimDirectory='D:/Steam-Bibliotheksordner/steamapps/common/Valheim',
    [string]$FgnAssembly='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/cache/VerdantsAscent-FiresGhettoNetworking/1.5.72/plugins/FiresGhettoNetworking/VAGhettoNetworking.dll',
    [switch]$DebugSystem=$true,
    [switch]$SkipNative
)
$ErrorActionPreference='Stop'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskOutput=Join-Path $PSScriptRoot 'bin/mob-observer-tests'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskCore=Join-Path $BepInExDirectory 'core'
Get-ChildItem -LiteralPath $taskCore -File | Where-Object Name -Match '^(0Harmony|Mono).*\.dll$' | Copy-Item -Destination $taskOutput -Force
$taskExe=Join-Path $taskOutput 'MobObserverTests.exe'
$taskSources=@('src/FieldReader.cs','src/MobVisualCurve.cs','src/MobHandoffVisual.cs','src/MobObserverHandoff.cs','tests/MobObserverFixtures.cs','tests/MobObserverTests.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/out:'+$taskExe) $taskSources
if($LASTEXITCODE -ne 0){throw 'Mob observer production fixture compile failed.'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Mob observer regression failed.'}
$taskHost=Join-Path $taskOutput 'MobObserverMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$taskHost) (Join-Path $PSScriptRoot 'tests/CombatMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'Mob observer Mono host compile failed.'}
& $taskHost $taskExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc')
if($LASTEXITCODE -ne 0){throw 'Mob observer regression on embedded Valheim Mono failed.'}
$recordedExe=Join-Path $taskOutput 'BridgeObserverTests.exe'
$recordedSources=@('src/MobBridge/MobBridgeObserver.cs','src/MobBridge/MobBridgeObserverPacket.cs',
    'src/MobBridge/MobBridgeTimeline.cs','src/MobBridge/MobBridgeRecording.cs','src/MobBridge/MobBridgeAnimationHistory.cs',
    'tests/MobObserverFixtures.cs','tests/MobBridge/BridgeObserverFixtures.cs','tests/MobBridge/BridgeObserverTests.cs')|
    ForEach-Object{Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /target:exe /define:TML_DEBUG_SYSTEM /optimize+ /langversion:5 /warnaserror+ ('/out:'+$recordedExe) $recordedSources
if($LASTEXITCODE -ne 0){throw 'Recorded observer fixture compile failed.'}
& $recordedExe
if($LASTEXITCODE -ne 0){throw 'Recorded observer regression failed.'}
& $taskHost $recordedExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc')
if($LASTEXITCODE -ne 0){throw 'Recorded observer regression on embedded Valheim Mono failed.'}
if($SkipNative){return}
$taskAddon=Join-Path $PSScriptRoot $(if($DebugSystem){'bin/debug-system/TooManyLags.dll'}else{'bin/TooManyLags.dll'})
$taskNative=Join-Path $taskOutput 'MobObserverNativeAudit.exe'
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/reference:'+(Join-Path $taskCore 'Mono.Cecil.dll')) ('/out:'+$taskNative) (Join-Path $PSScriptRoot 'tests/MobObserverNativeAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Observer native audit compile failed.'}
$taskNativeHost=Join-Path $taskOutput 'ObserverNativeMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$taskNativeHost) (Join-Path $PSScriptRoot 'tests/StateMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'Observer native Mono host compile failed.'}
$taskLog=@(& $taskNativeHost $taskNative (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') $taskAddon $taskCore $FgnAssembly 2>&1)
$taskCode=$LASTEXITCODE
$taskLog|Set-Content -LiteralPath (Join-Path $taskOutput 'native-install.log') -Encoding UTF8
if($taskCode -ne 0){$taskLog|ForEach-Object{Write-Output $_};throw 'Actual game/common observer Harmony contract failed.'}
if(!($taskLog -match '^PASS: \d+ actual addon/game observer contracts')){$taskLog|ForEach-Object{Write-Output $_};throw 'Native Mono host did not confirm completed observer audit.'}
$taskLog|Where-Object{$_ -match '^(PASS:|LIMIT:)'}|ForEach-Object{Write-Output $_}
