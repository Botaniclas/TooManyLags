param([string]$ValheimDirectory='D:/Steam-Bibliotheksordner/steamapps/common/Valheim',
[string]$BepInExDirectory='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/profiles/Client/BepInEx')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot
$taskOutput=Join-Path $taskRoot 'bin/mob-bridge-tests'
$taskCore=Join-Path $BepInExDirectory 'core'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskExe=Join-Path $taskOutput 'BridgeHookTiming.exe'
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/reference:'+(Join-Path $taskCore 'Mono.Cecil.dll')) ('/out:'+$taskExe) (Join-Path $taskRoot 'tests/MobBridge/BridgeHookTiming.cs')
if($LASTEXITCODE -ne 0){throw 'Hook timing compilation failed.'}
$taskDll=Join-Path $taskRoot 'bin/debug-system/TooManyLags.dll'
& (Join-Path $taskOutput 'BridgeMonoHost.exe') $taskExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') $taskDll $taskCore $taskDll
if($LASTEXITCODE -ne 0){throw 'Hook timing failed.'}
