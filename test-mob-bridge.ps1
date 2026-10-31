param(
    [string]$BepInExDirectory='C:/Users/Niclas/AppData/Roaming/Thunderstore Mod Manager/DataFolder/Valheim/profiles/Client/BepInEx',
    [string]$ValheimDirectory='D:/Steam-Bibliotheksordner/steamapps/common/Valheim',
    [switch]$Release
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$output = Join-Path $PSScriptRoot 'bin/mob-bridge-tests'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$core = Join-Path $BepInExDirectory 'core'
Get-ChildItem -LiteralPath $core -File |
    Where-Object Name -Match '^(0Harmony|Mono\.Cecil|MonoMod\.).*\.dll$' |
    Copy-Item -Destination $output -Force
$exe = Join-Path $output 'BridgeTimelineTests.exe'
$sources = @(
    'src/MobBridge/MobBridgeTimeline.cs',
    'src/MobBridge/MobBridgeClockCadence.cs',
    'src/MobBridge/MobBridgeObserverPacket.cs',
    'src/MobBridge/MobBridgeRecording.cs',
    'src/MobBridge/MobBridgeAnimationHistory.cs',
    'src/MobBridge/MobBridgeCaptureClock.cs',
    'src/MobBridge/MobBridgeTakeover.cs',
    'src/MobBridge/MobBridgeRateTracker.cs',
    'src/MobBridge/MobBridgeFrame.cs',
    'src/MobBridge/MobBridgePoseHistory.cs',
    'src/MobBridge/MobBridgeTargetTimeline.cs',
    'src/MobBridge/MobBridgeAttackGate.cs',
    'src/MobBridge/MobBridgeHitFilter.cs',
    'src/MobBridge/MobBridgePresentationPoint.cs',
    'src/MobBridge/Native/NativeFlightTransition.cs',
    'tests/MobVisualFixtures.cs',
    'tests/MobBridge/BridgeTimelineTests.cs'
) | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:exe /define:TML_DEBUG_SYSTEM /optimize+ /warnaserror+ /langversion:5 ('/out:' + $exe) $sources
if ($LASTEXITCODE -ne 0) { throw 'Mob bridge fixture compilation failed.' }
& $exe
if ($LASTEXITCODE -ne 0) { throw 'Mob bridge fixtures failed.' }
$latencyExe = Join-Path $output 'BridgeLatencyTests.exe'
& $compiler /nologo /target:exe /define:TML_MOB_BRIDGE /optimize+ /warnaserror+ /langversion:5 ('/out:'+$latencyExe) (Join-Path $PSScriptRoot 'src/MobBridge/MobBridgeLatency.cs') (Join-Path $PSScriptRoot 'tests/MobBridge/BridgeLatencyTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Presentation RTT fixtures failed to compile.' }
& $latencyExe
if ($LASTEXITCODE -ne 0) { throw 'Presentation RTT fixtures failed.' }
Write-Output 'PASS: mob bridge timing and takeover fixtures.'
$hitExe = Join-Path $output 'BridgeHitTests.exe'
$hitSources = @('src/MobBridge/MobBridgeHit.cs', 'src/MobBridge/MobBridgeSkin.cs', 'tests/MobVisualFixtures.cs',
    'tests/MobBridge/BridgeHitTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:exe /define:TML_DEBUG_SYSTEM /optimize+ /warnaserror+ /langversion:5 ('/out:' + $hitExe) $hitSources
if ($LASTEXITCODE -ne 0) { throw 'Mob bridge hitbox fixture compilation failed.' }
& $hitExe
if ($LASTEXITCODE -ne 0) { throw 'Mob bridge hitbox fixtures failed.' }
$effectExe = Join-Path $output 'BridgeEffectAttachmentTests.exe'
$effectSources = @('src/MobBridge/MobBridgeEffectAttachment.cs', 'src/MobBridge/MobBridgeHit.cs', 'src/MobBridge/MobBridgeSkin.cs',
    'tests/MobVisualFixtures.cs', 'tests/MobBridge/BridgeHitTests.cs', 'tests/MobBridge/BridgeEffectAttachmentTests.cs') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:exe /main:BridgeEffectAttachmentTests /define:TML_DEBUG_SYSTEM /optimize+ /warnaserror+ /langversion:5 ('/out:' + $effectExe) $effectSources
if ($LASTEXITCODE -ne 0) { throw 'Attached FX fixture compilation failed.' }
& $effectExe
if ($LASTEXITCODE -ne 0) { throw 'Attached FX fixtures failed.' }
$audit = Join-Path $output 'BridgeNativeAudit.exe'
$plugin = Join-Path $PSScriptRoot $(if ($Release) { 'bin/TooManyLags.dll' } else { 'bin/debug-system/TooManyLags.dll' })
& $compiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 ('/reference:' + (Join-Path $core 'Mono.Cecil.dll')) ('/out:' + $audit) (Join-Path $PSScriptRoot 'tests/MobBridge/BridgeNativeAudit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mob bridge DLL audit compilation failed.' }
if ($Release) { & $audit $plugin release } else { & $audit $plugin }
if ($LASTEXITCODE -ne 0) { throw 'Mob bridge DLL audit failed.' }

$presentation = Join-Path $output 'BridgePresentationNativeAudit.exe'
& $compiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 `
    ('/reference:' + (Join-Path $core '0Harmony.dll')) `
    ('/reference:' + (Join-Path $core 'Mono.Cecil.dll')) ('/out:' + $presentation) `
    (Join-Path $PSScriptRoot 'tests/MobBridge/BridgePresentationNativeAudit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mob bridge presentation audit compilation failed.' }
$hostExe = Join-Path $output 'BridgeMonoHost.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ `
    ('/out:' + $hostExe) (Join-Path $PSScriptRoot 'tests/StateMonoFixtureHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mob bridge Mono host compilation failed.' }
$hostOutput = @(& $hostExe $presentation (Join-Path $ValheimDirectory 'valheim_Data/Managed') `
    (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') `
    (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') $plugin $core $plugin 2>&1)
$hostOutput | ForEach-Object { Write-Output $_ }
if ($LASTEXITCODE -ne 0 -or !($hostOutput -match '^PASS: \d+ actual game presentation hooks')) {
    throw 'Actual game presentation hook audit failed.'
}
$weapons = Join-Path $output 'BridgeWeaponNativeAudit.exe'
& $compiler /nologo /target:exe /optimize+ /warnaserror+ /langversion:5 `
    ('/reference:' + (Join-Path $core 'Mono.Cecil.dll')) ('/out:' + $weapons) `
    (Join-Path $PSScriptRoot 'tests/MobBridge/BridgeWeaponNativeAudit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native weapon differential audit compilation failed.' }
$weaponOutput = @(& $hostExe $weapons (Join-Path $ValheimDirectory 'valheim_Data/Managed') `
    (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') `
    (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc') $plugin $core $plugin 2>&1)
$weaponOutput | ForEach-Object { Write-Output $_ }
if ($LASTEXITCODE -ne 0 -or !($weaponOutput -match '^PASS: \d+ native/fake weapon selection')) {
    throw 'Native weapon differential audit failed.'
}
