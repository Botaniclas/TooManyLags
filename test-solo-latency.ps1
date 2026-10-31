param(
    [string]$BepInExDirectory='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/profiles/Client/BepInEx',
    [string]$ValheimDirectory='D:/Steam-Bibliotheksordner/steamapps/common/Valheim'
)
$ErrorActionPreference='Stop'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$taskCore=Join-Path $BepInExDirectory 'core'
$taskOutput=Join-Path $PSScriptRoot 'bin/solo-latency-tests'
New-Item -ItemType Directory -Force -Path $taskOutput|Out-Null
Get-ChildItem -LiteralPath $taskCore -File|Where-Object Name -Match '^(0Harmony|Mono).*\.dll$'|Copy-Item -Destination $taskOutput -Force
$taskExe=Join-Path $taskOutput 'SoloLatencyTests.exe'
$taskSources=@('src/SoloNetworkLatency.cs','tests/SoloLatencyFixtures.cs','tests/SoloLatencyTests.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /define:TML_DEBUG_SYSTEM /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/out:'+$taskExe) $taskSources
if($LASTEXITCODE -ne 0){throw 'Solo latency fixtures did not compile.'}
& $taskExe
if($LASTEXITCODE -ne 0){throw 'Solo latency fixture assertions failed.'}
$taskHost=Join-Path $taskOutput 'LatencyMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$taskHost) (Join-Path $PSScriptRoot 'tests/CombatMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'Latency Mono host failed to compile.'}
$taskMono=@(& $taskHost $taskExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') 2>&1)
if($LASTEXITCODE -ne 0 -or !($taskMono -match '^PASS:')){$taskMono|Write-Output;throw 'Embedded Mono latency fixtures did not finish successfully.'}
$taskMono|Where-Object{$_ -match '^PASS:'}|Write-Output
$taskNative=Join-Path $taskOutput 'SoloLatencyNativeAudit.exe'
& $taskCompiler /nologo /target:exe /optimize+ /langversion:5 /warnaserror+ ('/reference:'+(Join-Path $taskCore '0Harmony.dll')) ('/reference:'+(Join-Path $taskCore 'Mono.Cecil.dll')) ('/out:'+$taskNative) (Join-Path $PSScriptRoot 'tests/SoloLatencyNativeAudit.cs')
if($LASTEXITCODE -ne 0){throw 'Native latency audit compile failed.'}
$taskNativeHost=Join-Path $taskOutput 'LatencyNativeMonoHost.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$taskNativeHost) (Join-Path $PSScriptRoot 'tests/StateMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'Native latency Mono host compile failed.'}
$taskLog=@(& $taskNativeHost $taskNative (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') (Join-Path $PSScriptRoot 'bin/debug-system/TooManyLags.dll') $taskCore '' 2>&1)
$taskCode=$LASTEXITCODE
$taskLog|Set-Content -LiteralPath (Join-Path $taskOutput 'native-install.log') -Encoding UTF8
if($taskCode -ne 0 -or !($taskLog -match '^PASS: actual game ZRpc')){$taskLog|Write-Output;throw 'Native latency hooks did not finish their audit.'}
$taskLog|Where-Object{$_ -match '^(PASS:|LIMIT:)'}|Write-Output
