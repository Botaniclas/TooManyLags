param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference = 'Stop'
$cameraCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$cameraCore = Join-Path $BepInExDirectory 'core'
$cameraOutput = Join-Path $PSScriptRoot 'bin\camera-tests'
New-Item -ItemType Directory -Force -Path $cameraOutput | Out-Null
foreach ($cameraDependency in @('0Harmony.dll','MonoMod.Utils.dll','MonoMod.RuntimeDetour.dll','Mono.Cecil.dll')) { Copy-Item -LiteralPath (Join-Path $cameraCore $cameraDependency) -Destination $cameraOutput -Force }
foreach ($cameraSuite in @('CameraWakeTests','CameraRuntimeTests')) {
    $cameraSources = @('src\AuthorityGrid.cs','src\CameraHeading.cs','src\CameraPrediction.cs','src\OwnershipCadence.cs','src\IntervalConfig.cs',('tests\' + $cameraSuite + '.cs'))
    if ($cameraSuite -eq 'CameraRuntimeTests') { $cameraSources += @('src\CameraPublisher.cs','src\AuthorityController.cs','src\RecipientRequirement.cs','src\RegionContentionGate.cs','tests\CameraRuntimeFixtures.cs') }
    $cameraSources = $cameraSources | ForEach-Object { Join-Path $PSScriptRoot $_ }
    $cameraExe = Join-Path $cameraOutput ($cameraSuite + '.exe')
    & $cameraCompiler /nologo /target:exe /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 ('/reference:' + (Join-Path $cameraCore '0Harmony.dll')) ('/out:' + $cameraExe) $cameraSources
    if ($LASTEXITCODE -ne 0) { throw ('Camera test compilation failed: ' + $cameraSuite) }
    & $cameraExe
    if ($LASTEXITCODE -ne 0) { throw ('Camera test failed: ' + $cameraSuite) }
}
