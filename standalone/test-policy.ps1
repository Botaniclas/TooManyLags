$ErrorActionPreference = 'Stop'
$standaloneCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$standaloneTests = Join-Path $PSScriptRoot 'bin/policy-tests'
New-Item -ItemType Directory -Path $standaloneTests -Force | Out-Null
foreach ($standaloneSuite in @('GridTests','GridCellSizeTests','GridTimingConfigTests','DirectionalGridTests','HeadingCoverageTests','CameraPredictionTests','FieldCacheTests','RegionGateTests','StandalonePolicyTests')) {
    $standaloneSources = @('src/AuthorityGrid.cs', 'src/CameraPrediction.cs', 'src/IntervalConfig.cs', 'src/RegionContentionGate.cs', 'src/HandoffTransaction.cs', ('tests/' + $standaloneSuite + '.cs')) | ForEach-Object { Join-Path $PSScriptRoot $_ }
    $standaloneExe = Join-Path $standaloneTests ($standaloneSuite + '.exe')
    & $standaloneCompiler /nologo /optimize+ /warnaserror+ /nowarn:0649 /langversion:5 /define:PLAYER_SIMULATION ('/out:' + $standaloneExe) $standaloneSources
    if ($LASTEXITCODE -ne 0) { throw ('Test compilation failed: ' + $standaloneSuite) }
    & $standaloneExe
    if ($LASTEXITCODE -ne 0) { throw ('Test failed: ' + $standaloneSuite) }
}
