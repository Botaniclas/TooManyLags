param(
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim'
)
$ErrorActionPreference = 'Stop'
$mobCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$mobOutput = Join-Path $PSScriptRoot 'bin/mob-integration-tests'
$mobCore = Join-Path $BepInExDirectory 'core'
New-Item -ItemType Directory -Force -Path $mobOutput | Out-Null
foreach ($mobDependency in @('0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) { Copy-Item -LiteralPath (Join-Path $mobCore $mobDependency) -Destination $mobOutput -Force }
$mobSources = @('src/LocalAgent.cs','src/ShipTransferGuard.cs','src/Handoffs.cs','src/NativeOwnership.cs','src/HandoffTransaction.cs','src/RecipientReadiness.cs','src/RecipientRequirement.cs','src/RequestRouter.cs','src/IncineratorProtection.cs','src/FieldReader.cs','src/FriendlyCreatures.cs','src/MobPolicy.cs','tests/HandoffFixtures.cs','tests/MobIntegrationTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$mobExe = Join-Path $mobOutput 'MobIntegrationTests.exe'
& $mobCompiler /nologo /target:exe /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 /define:MOB_INTEGRATION ('/reference:' + (Join-Path $mobCore '0Harmony.dll')) ('/out:' + $mobExe) $mobSources
if ($LASTEXITCODE -ne 0) { throw 'Mob integration compilation failed.' }
& $mobExe
if ($LASTEXITCODE -ne 0) { throw 'Mob policy/source/transaction integration failed.' }
$mobMono = Join-Path $mobOutput 'MonoFixtureHost.exe'
& $mobCompiler /nologo /target:exe /platform:x64 /optimize+ /warnaserror+ /langversion:5 ('/out:' + $mobMono) (Join-Path $PSScriptRoot 'tests/TextMonoFixtureHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mob integration Mono host compilation failed.' }
& $mobMono $mobExe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc')
if ($LASTEXITCODE -ne 0) { throw 'Mob integration failed on embedded Valheim Mono.' }
