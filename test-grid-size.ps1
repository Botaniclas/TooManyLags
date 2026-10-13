param([string]$BepInExDirectory = 'C:\Users\Niclas\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Client\BepInEx')
$ErrorActionPreference = 'Stop'
$sizeCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$sizeOutput = Join-Path $PSScriptRoot 'bin/grid-size-tests'
New-Item -ItemType Directory -Force -Path $sizeOutput | Out-Null
foreach ($mode in @('ServerSimulation', 'PlayerSimulation')) {
    $sizeGrid = 'src/AuthorityGrid.cs'; $sizeGate = 'src/RegionContentionGate.cs'; $sizeDefine = '/define:SERVER_SIMULATION'
    if ($mode -eq 'PlayerSimulation') { $sizeGrid = 'src/PlayerSimulation/AuthorityGrid.cs'; $sizeGate = 'src/PlayerSimulation/RegionContentionGate.cs'; $sizeDefine = '/define:PLAYER_SIMULATION' }
    $sizeExecutable = Join-Path $sizeOutput ($mode + 'GridCellSizeTests.exe')
    & $sizeCompiler /nologo /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 $sizeDefine ('/out:' + $sizeExecutable) (Join-Path $PSScriptRoot $sizeGrid) (Join-Path $PSScriptRoot $sizeGate) (Join-Path $PSScriptRoot 'tests/GridCellSizeTests.cs')
    if ($LASTEXITCODE -ne 0) { throw ('Cell-size regression compilation failed: ' + $mode) }
    & $sizeExecutable
    if ($LASTEXITCODE -ne 0) { throw ('Cell-size regression failed: ' + $mode) }
}
$sizeBepInEx = Join-Path $BepInExDirectory 'core/BepInEx.dll'
Copy-Item -LiteralPath $sizeBepInEx -Destination $sizeOutput -Force
$sizeConfigExecutable = Join-Path $sizeOutput 'GridSizeConfigTests.exe'
& $sizeCompiler /nologo /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 ('/reference:' + $sizeBepInEx) ('/out:' + $sizeConfigExecutable) (Join-Path $PSScriptRoot 'src/PlayerSimulation/AuthorityGrid.cs') (Join-Path $PSScriptRoot 'src/PlayerSimulation/Settings.cs') (Join-Path $PSScriptRoot 'tests/GridSizeConfigTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Cell-size config regression compilation failed.' }
& $sizeConfigExecutable $sizeOutput
if ($LASTEXITCODE -ne 0) { throw 'Cell-size config regression failed.' }
