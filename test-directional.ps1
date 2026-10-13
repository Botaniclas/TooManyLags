$ErrorActionPreference='Stop'
$authorityCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$authorityOutput=Join-Path $PSScriptRoot 'bin\directional-tests'
New-Item -ItemType Directory -Force -Path $authorityOutput | Out-Null
$predictionExe = Join-Path $authorityOutput 'CameraPredictionTests.exe'
& $authorityCompiler /nologo /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 ('/out:'+$predictionExe) (Join-Path $PSScriptRoot 'src/AuthorityGrid.cs') (Join-Path $PSScriptRoot 'src/CameraPrediction.cs') (Join-Path $PSScriptRoot 'tests/CameraPredictionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Camera prediction compilation failed.' }
& $predictionExe
if ($LASTEXITCODE -ne 0) { throw 'Camera prediction regression failed.' }
$authorityExe=Join-Path $authorityOutput 'DirectionalGridTests.exe'
& $authorityCompiler /nologo /optimize+ /warnaserror+ /langversion:5 ('/out:'+$authorityExe) (Join-Path $PSScriptRoot 'src\AuthorityGrid.cs') (Join-Path $PSScriptRoot 'tests\DirectionalGridTests.cs')
if($LASTEXITCODE -ne 0){throw 'Directional compile failed.'}
& $authorityExe
if($LASTEXITCODE -ne 0){throw 'Directional tests failed.'}
$taskOverlapExe = Join-Path $authorityOutput 'ServerOverlapCooldownTests.exe'
& $authorityCompiler /nologo /optimize+ /warnaserror+ /langversion:5 ('/out:'+$taskOverlapExe) (Join-Path $PSScriptRoot 'src/AuthorityGrid.cs') (Join-Path $PSScriptRoot 'tests/ServerOverlapCooldownTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Camera overlap compilation failed.' }
& $taskOverlapExe
if ($LASTEXITCODE -ne 0) { throw 'Camera overlap regression failed.' }
foreach ($mode in @('ServerSimulation','PlayerSimulation')) {
    $taskGrid = 'src/AuthorityGrid.cs'; $taskDefine = '/define:SERVER_SIMULATION'
    if ($mode -eq 'PlayerSimulation') { $taskGrid = 'src/PlayerSimulation/AuthorityGrid.cs'; $taskDefine = '/define:PLAYER_SIMULATION' }
    $taskExe = Join-Path $authorityOutput ($mode + 'HeadingCoverageTests.exe')
    & $authorityCompiler /nologo /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 $taskDefine ('/out:'+$taskExe) (Join-Path $PSScriptRoot $taskGrid) (Join-Path $PSScriptRoot 'tests/HeadingCoverageTests.cs')
    if ($LASTEXITCODE -ne 0) { throw ('Heading coverage compilation failed: ' + $mode) }
    & $taskExe
    if ($LASTEXITCODE -ne 0) { throw ('Heading coverage regression failed: ' + $mode) }
}
