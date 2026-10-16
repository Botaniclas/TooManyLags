param(
    [string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx',
    [string]$ValheimDirectory = 'D:\Steam-Bibliotheksordner\steamapps\common\Valheim'
)
$ErrorActionPreference='Stop'
$textCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$textCore=Join-Path $BepInExDirectory 'core'
$textOutput=Join-Path $PSScriptRoot 'bin/text-editing-tests'
New-Item -ItemType Directory -Force -Path $textOutput | Out-Null
foreach($textDependency in @('0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) {Copy-Item -LiteralPath (Join-Path $textCore $textDependency) -Destination $textOutput -Force}
$textSources=@('src/TextEditingProtection.cs','tests/TextEditingFixtures.cs','tests/TextEditingTests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
$textExe=Join-Path $textOutput 'TextEditingTests.exe'
& $textCompiler /nologo /target:exe /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 ('/reference:'+(Join-Path $textCore '0Harmony.dll')) ('/out:'+$textExe) $textSources
if($LASTEXITCODE -ne 0){throw 'Text-edit fixture compilation failed.'}
& $textExe
if($LASTEXITCODE -ne 0){throw 'Text-edit lifecycle tests failed.'}
$textControllerSources=@('src/AuthorityController.cs','src/AuthorityGrid.cs','src/CameraHeading.cs','src/CameraPrediction.cs','src/OwnershipCadence.cs','src/IntervalConfig.cs','src/RecipientRequirement.cs','src/RegionContentionGate.cs','tests/CameraRuntimeFixtures.cs','tests/TextControllerTests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
$textControllerExe=Join-Path $textOutput 'TextControllerTests.exe'
& $textCompiler /nologo /target:exe /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 ('/reference:'+(Join-Path $textCore '0Harmony.dll')) ('/out:'+$textControllerExe) $textControllerSources
if($LASTEXITCODE -ne 0){throw 'Text-edit controller compilation failed.'}
& $textControllerExe
if($LASTEXITCODE -ne 0){throw 'Text-edit controller tests failed.'}
$textMonoHost=Join-Path $textOutput 'MonoFixtureHost.exe'
& $textCompiler /nologo /target:exe /platform:x64 /optimize+ /langversion:5 /warnaserror+ ('/out:'+$textMonoHost) (Join-Path $PSScriptRoot 'tests/TextMonoFixtureHost.cs')
if($LASTEXITCODE -ne 0){throw 'Text-edit Mono host compilation failed.'}
foreach($textFixture in @($textExe,$textControllerExe)) {
    & $textMonoHost $textFixture (Join-Path $ValheimDirectory 'valheim_Data/Managed') (Join-Path $ValheimDirectory 'MonoBleedingEdge/EmbedRuntime') (Join-Path $ValheimDirectory 'MonoBleedingEdge/etc')
    if($LASTEXITCODE -ne 0){throw ('Text-edit fixtures failed on embedded Valheim Mono: '+$textFixture)}
}
