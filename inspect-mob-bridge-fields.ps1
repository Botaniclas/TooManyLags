param(
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx'
)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$managed = Join-Path $ValheimDirectory 'valheim_Data/Managed'
$core = Join-Path $BepInExDirectory 'core'
$output = Join-Path $PSScriptRoot 'bin/mob-bridge-schema'
New-Item -ItemType Directory -Force -Path $output | Out-Null
Get-ChildItem -LiteralPath $core -File | Where-Object Name -Match '^(0Harmony|MonoMod\.|Mono\.Cecil).*\.dll$' | Copy-Item -Destination $output -Force
$exe = Join-Path $output 'BridgeFieldSchemaAudit.exe'
$references = @(
    'netstandard.dll','UnityEngine.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll',
    'UnityEngine.AnimationModule.dll','assembly_valheim.dll','assembly_utils.dll',
    'SoftReferenceableAssets.dll'
) | ForEach-Object { Join-Path $managed $_ }
$references += Join-Path $core '0Harmony.dll'
$sources = @(
    'src/MobBridge/Native/NativeSteeringSandbox.cs',
    'src/MobBridge/Native/NativeSteeringPlan.cs',
    'src/MobBridge/Native/NativeAiFork.cs',
    'src/MobBridge/Native/NativeFlightTransition.cs',
    'src/MobBridge/Native/NativeRandomLedger.cs',
    'src/MobBridge/Native/MobBridgeSteeringContext.cs',
    'src/MobBridge/Native/MobBridgeWeaponState.cs',
    'src/MobBridge/MobBridgeFieldCodec.cs',
    'tests/MobBridge/BridgeFieldSchemaAudit.cs'
) | ForEach-Object { Join-Path $PSScriptRoot $_ }
$arguments = @('/nologo','/target:exe','/platform:x64','/optimize+','/langversion:5',
    '/define:TML_DEBUG_SYSTEM',('/out:' + $exe)) + ($references | ForEach-Object { '/reference:' + $_ }) + $sources
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Bridge field schema audit compilation failed.' }
$monoHost = Join-Path $output 'MonoFixtureHost.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 ('/out:' + $monoHost) (Join-Path $PSScriptRoot 'tests/CombatMonoFixtureHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Bridge schema Mono host compilation failed.' }
& $monoHost $exe $managed (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc')
if ($LASTEXITCODE -ne 0) { throw 'Bridge field schema audit failed.' }
Get-Content -LiteralPath (Join-Path $output 'bridge-field-schema.txt')
