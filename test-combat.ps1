param(
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$FgnAssembly = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\cache\VerdantsAscent-FiresGhettoNetworking\1.5.41\VAGhettoNetworking.dll',
    [string]$PupAssembly = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\cache\PUP82-PUP_FPS\1.0.34\PUP_FPS.dll',
    [switch]$SkipNativeAudit,
    [switch]$DebugSystem
)
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskTests = Join-Path $PSScriptRoot $(if ($DebugSystem) {'bin/combat-tests-debug'} else {'bin/combat-tests'})
New-Item -ItemType Directory -Force -Path $taskTests | Out-Null
$taskHarmony = Join-Path $BepInExDirectory 'core\0Harmony.dll'
Copy-Item -LiteralPath $taskHarmony -Destination $taskTests
Get-ChildItem -LiteralPath (Join-Path $BepInExDirectory 'core') -Filter 'Mono*.dll' | Copy-Item -Destination $taskTests
$taskSources = @('src\FriendlyCreatures.cs','src\FieldReader.cs','src\FgnCombatBridge.cs','src\FgnCombatOwnership.cs','src\FgnMobHandoff.cs','src\FgnBossPolicy.cs','src\FgnTargetEvents.cs','src\FgnAttackRetry.cs','src\FgnCombatStatus.cs','tests\CombatFixtures.cs','tests\CombatTests.cs','tests\CombatFriendlyTests.cs','tests\CombatHandoffTests.cs','tests\CombatBossTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$taskSources += @('src\FgnTargetBoundary.cs','tests\CombatBoundaryTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$taskSources += @('src\FgnSoloCombatTest.cs','tests\CombatSoloTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$taskSources += Join-Path $PSScriptRoot 'tests\CombatReadinessRegressionTests.cs'
$taskAssembly = Join-Path $taskTests ('CombatRegression-' + [Guid]::NewGuid().ToString('N') + '.dll')
$taskCompileOutput = @(& $taskCompiler '/nologo' ('/define:' + $(if ($DebugSystem) {'TML_DEBUG_SYSTEM'} else {'TML_NORMAL_BUILD'})) '/target:exe' '/optimize+' '/langversion:5' '/warnaserror+' ('/reference:' + $taskHarmony) ('/out:' + $taskAssembly) $taskSources 2>&1)
if ($LASTEXITCODE -ne 0) {
    $taskCompileOutput | Write-Output
    Write-Output ('Combat fixture compiler exit=' + $LASTEXITCODE + '; outputExists=' + (Test-Path -LiteralPath $taskAssembly) +
        '; sources=' + $taskSources.Count)
    throw 'Combat fixture compilation failed.'
}
$taskHost = Join-Path $taskTests 'ManagedFixtureHost.exe'
& $taskCompiler '/nologo' '/target:exe' '/platform:x86' '/optimize+' '/langversion:5' '/warnaserror+' ('/out:' + $taskHost) (Join-Path $PSScriptRoot 'tests\CombatManagedFixtureHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Managed fixture host compilation failed.' }
& $taskHost $taskAssembly
if ($LASTEXITCODE -ne 0) { throw 'Combat ownership tests failed.' }
# The following Mono suite must execute this run's fixture, not an old
# fixed-name binary left over from an earlier build.
Copy-Item -LiteralPath $taskAssembly -Destination (Join-Path $taskTests 'CombatRegression.dll') -Force
if ($SkipNativeAudit) { return }
$taskCecil = Join-Path $BepInExDirectory 'core\Mono.Cecil.dll'
$taskAudit = Join-Path $taskTests 'NativeCombatAudit.exe'
& $taskCompiler '/nologo' ('/define:' + $(if ($DebugSystem) {'TML_DEBUG_SYSTEM'} else {'TML_NORMAL_BUILD'})) '/target:exe' '/optimize+' '/langversion:5' '/warnaserror+' ('/reference:' + $taskCecil) ('/out:' + $taskAudit) (Join-Path $PSScriptRoot 'tests\CombatNativeAudit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Combat native audit compilation failed.' }
& $taskAudit $FgnAssembly (Join-Path $ValheimDirectory 'valheim_Data\Managed') (Join-Path $PSScriptRoot $(if ($DebugSystem) {'bin/debug-system/TooManyLags.dll'} else {'bin/TooManyLags.dll'})) $PupAssembly $(if ($DebugSystem) {'debug'} else {'normal'})
if ($LASTEXITCODE -ne 0) { throw 'Actual FGN/native combat contracts failed.' }
