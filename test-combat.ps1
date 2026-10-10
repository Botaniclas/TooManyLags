param(
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim',
    [string]$FgnAssembly = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\cache\VerdantsAscent-FiresGhettoNetworking\1.5.41\VAGhettoNetworking.dll',
    [string]$PupAssembly = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\cache\PUP82-PUP_FPS\1.0.34\PUP_FPS.dll',
    [switch]$SkipNativeAudit
)
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$taskTests = Join-Path $PSScriptRoot 'bin\combat-tests'
New-Item -ItemType Directory -Force -Path $taskTests | Out-Null
$taskHarmony = Join-Path $BepInExDirectory 'core\0Harmony.dll'
Copy-Item -LiteralPath $taskHarmony -Destination $taskTests
Get-ChildItem -LiteralPath (Join-Path $BepInExDirectory 'core') -Filter 'Mono*.dll' | Copy-Item -Destination $taskTests
$taskSources = @('src\FriendlyCreatures.cs','src\FieldReader.cs','src\FgnCombatBridge.cs','src\FgnCombatOwnership.cs','src\FgnTargetEvents.cs','src\FgnAttackRetry.cs','src\FgnCombatStatus.cs','tests\CombatFixtures.cs','tests\CombatTests.cs','tests\CombatFriendlyTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$taskAssembly = Join-Path $taskTests 'CombatRegression.dll'
& $taskCompiler '/nologo' '/target:exe' '/optimize+' '/langversion:5' '/warnaserror+' ('/reference:' + $taskHarmony) ('/out:' + $taskAssembly) $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Combat fixture compilation failed.' }
$taskHost = Join-Path $taskTests 'ManagedFixtureHost.exe'
& $taskCompiler '/nologo' '/target:exe' '/platform:x86' '/optimize+' '/langversion:5' '/warnaserror+' ('/out:' + $taskHost) (Join-Path $PSScriptRoot 'tests\CombatManagedFixtureHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Managed fixture host compilation failed.' }
& $taskHost $taskAssembly
if ($LASTEXITCODE -ne 0) { throw 'Combat ownership tests failed.' }
if ($SkipNativeAudit) { return }
$taskCecil = Join-Path $BepInExDirectory 'core\Mono.Cecil.dll'
$taskAudit = Join-Path $taskTests 'NativeCombatAudit.exe'
& $taskCompiler '/nologo' '/target:exe' '/optimize+' '/langversion:5' '/warnaserror+' ('/reference:' + $taskCecil) ('/out:' + $taskAudit) (Join-Path $PSScriptRoot 'tests\CombatNativeAudit.cs')
if ($LASTEXITCODE -ne 0) { throw 'Combat native audit compilation failed.' }
& $taskAudit $FgnAssembly (Join-Path $ValheimDirectory 'valheim_Data\Managed') (Join-Path $PSScriptRoot 'bin\TooManyLags.dll') $PupAssembly
if ($LASTEXITCODE -ne 0) { throw 'Actual FGN/native combat contracts failed.' }
