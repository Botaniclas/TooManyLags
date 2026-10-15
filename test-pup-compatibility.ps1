param(
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim'
)
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$testDir=Join-Path $PSScriptRoot 'bin/pup-compatibility-tests'
$fixtures=Join-Path $PSScriptRoot 'tests/PupCompatibility'
$harmony=Join-Path $BepInExDirectory 'core/0Harmony.dll'
New-Item -ItemType Directory -Force -Path $testDir | Out-Null
Copy-Item -LiteralPath $harmony -Destination $testDir -Force
Get-ChildItem -LiteralPath (Join-Path $BepInExDirectory 'core') -Filter 'Mono*.dll' | Copy-Item -Destination $testDir -Force
$foreign=Join-Path $testDir 'PupForeignFixtures.dll'
& $compiler /nologo /target:library /optimize+ /langversion:5 /warnaserror+ ('/out:'+$foreign) (Join-Path $fixtures 'RuntimeFixtures.cs') (Join-Path $fixtures 'ModernPupFixtures.cs')
if($LASTEXITCODE -ne 0){throw 'Optional PUP foreign fixtures failed to compile.'}
$sources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src/PupCompatibility') -Filter '*.cs' | Select-Object -ExpandProperty FullName)
$sources+=@((Join-Path $fixtures 'RuntimeTests.cs'),(Join-Path $fixtures 'ModuleTests.cs'))
$exe=Join-Path $testDir 'PupCompatibilityTests.exe'
& $compiler /nologo /define:TESTING /optimize+ /langversion:5 /warnaserror+ /main:PupCompatibilityRuntimeTests ('/reference:'+$harmony) ('/reference:'+$foreign) ('/out:'+$exe) $sources
if($LASTEXITCODE -ne 0){throw 'Production PUP compatibility fixtures failed to compile.'}
& $exe
if($LASTEXITCODE -ne 0){throw 'Production PUP compatibility fixtures failed.'}
$hostExe=Join-Path $testDir 'MonoFixtureHost.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$hostExe) (Join-Path $PSScriptRoot 'tests/CombatMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'PUP compatibility Mono host failed to compile.'}
& $hostExe $exe (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc')
if($LASTEXITCODE -ne 0){throw 'Production PUP compatibility fixtures failed on Valheim Mono.'}
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'audit-pup-native.ps1') -ValheimDirectory $ValheimDirectory
if($LASTEXITCODE -ne 0){throw 'Embedded PUP native contracts failed.'}
