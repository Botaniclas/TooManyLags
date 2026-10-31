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
$taskOutput=Join-Path $PSScriptRoot 'bin/mob-visual-tests'
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
$taskCore=Join-Path $BepInExDirectory 'core'
Get-ChildItem -LiteralPath $taskCore -File | Where-Object Name -Match '^(0Harmony|Mono).*\.dll$' | Copy-Item -Destination $taskOutput -Force
$taskExe=Join-Path $taskOutput 'MobVisualTests.exe'
$taskSources=@('src/FieldReader.cs','src/MobVisualCurve.cs','src/MobHandoffVisual.cs','tests/MobVisualFixtures.cs','tests/MobVisualTests.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/out:'+$taskExe) $taskSources
if($LASTEXITCODE -ne 0){throw 'Mob visual production fixture compile failed.'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Mob visual regression failed.'}
$taskHost=Join-Path $taskOutput 'MobVisualMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$taskHost) (Join-Path $PSScriptRoot 'tests/CombatMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'Mob visual Mono host compile failed.'}
& $taskHost $taskExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc')
if($LASTEXITCODE -ne 0){throw 'Mob visual regression on embedded Valheim Mono failed.'}
$taskTrajectory=Join-Path $taskOutput 'MobVisualTrajectoryTests.exe'
$taskTrajectorySources=@('src/FieldReader.cs','src/MobVisualCurve.cs','src/MobHandoffVisual.cs','tests/MobVisualFixtures.cs','tests/MobVisualTrajectoryTests.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/out:'+$taskTrajectory) $taskTrajectorySources
if($LASTEXITCODE -ne 0){throw 'Mob native-trajectory fixture compile failed.'}
& $taskTrajectory
if($LASTEXITCODE -ne 0){throw 'Mob native-trajectory regressions failed.'}
& $taskHost $taskTrajectory (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc')
if($LASTEXITCODE -ne 0){throw 'Mob native-trajectory regressions on Valheim Mono failed.'}
if($SkipNative){return}
$taskNative=Join-Path $taskOutput 'MobVisualNativeAudit.exe'
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/reference:'+(Join-Path $taskCore 'Mono.Cecil.dll')) ('/out:'+$taskNative) (Join-Path $PSScriptRoot 'tests/MobVisualNativeAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Visual native audit compile failed.'}
$taskNativeHost=Join-Path $taskOutput 'VisualNativeMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$taskNativeHost) (Join-Path $PSScriptRoot 'tests/StateMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'Visual native Mono host compile failed.'}
$taskLog=@(& $taskNativeHost $taskNative (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') $taskAddon $taskCore $FgnAssembly 2>&1)
$taskCode=$LASTEXITCODE
$taskLog|Set-Content -LiteralPath (Join-Path $taskOutput 'native-install.log') -Encoding UTF8
if($taskCode -ne 0){$taskLog|ForEach-Object{Write-Output $_};throw 'Actual FGN/game visual Harmony contract failed.'}
if(!($taskLog -match '^PASS: \d+ actual addon/game/FGN visual contracts')){$taskLog|ForEach-Object{Write-Output $_};throw 'Native Mono host did not confirm the completed visual audit.'}
$taskLog|Where-Object{$_ -match '^(PASS:|LIMIT:)'}|ForEach-Object{Write-Output $_}
